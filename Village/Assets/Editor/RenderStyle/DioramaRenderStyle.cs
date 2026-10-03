using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Render stili "minyatür diorama" (tilt-shift'siz): sıcak güneş + mavi-mor gölgeler, renk ayarı, AO, bloom, vinyet, sis.
// Village → Render Stili: Diorama uygula. Açık sahnenin ışığını / sisini / kamerasını, sahnedeki global Volume'un
// profilini ve URP renderer'larının SSAO ayarını değiştirir. Tek adımda Ctrl+Z ile geri alınır.
// Değerler başlangıç noktası: beğenilmeyen sonra Inspector'dan değiştirilir.
public static class DioramaRenderStyle
{
    // Renkler (sRGB)
    private static readonly Color SunColor = new Color(1.00f, 0.86f, 0.69f);   // hafif şeftali
    private static readonly Color SkyAmbient = new Color(1.00f, 0.95f, 0.89f); // sıcak krem
    private static readonly Color EquatorAmbient = new Color(0.80f, 0.88f, 0.89f); // açık turkuaz-gri
    private static readonly Color GroundAmbient = new Color(0.56f, 0.60f, 0.85f); // mavi-mor: gölgelerin rengi
    private static readonly Color Horizon = new Color(0.81f, 0.90f, 0.89f);    // sis + kamera arka planı
    private static readonly Color ShadowTone = new Color(0.44f, 0.46f, 0.64f); // Split Toning: gölgeler mavi-mor
    private static readonly Color HighlightTone = new Color(0.63f, 0.55f, 0.46f); // Split Toning: aydınlıklar şeftali

    [MenuItem("Village/Render Stili: Diorama uygula")]
    private static void Apply()
    {
        Undo.SetCurrentGroupName("Render Stili: Diorama");
        int group = Undo.GetCurrentGroup();
        var report = new System.Text.StringBuilder("Render stili uygulandı (Ctrl+Z geri alır):\n");

        ApplyLighting(report);
        ApplyCamera(report);
        ApplyVolume(report);
        ApplyAmbientOcclusion(report);

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log(report.ToString());
    }

    // Güneş, ortam ışığı, sis
    private static void ApplyLighting(System.Text.StringBuilder report)
    {
        Object renderSettings = RenderSettingsObject();
        if (renderSettings != null) Undo.RecordObject(renderSettings, "Render Stili");

        Light sun = RenderSettings.sun;
        if (sun == null)
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional) { sun = light; break; }
        if (sun != null)
        {
            Undo.RecordObject(sun, "Render Stili");
            sun.color = SunColor;
            sun.intensity = 2.2f;
            sun.shadows = LightShadows.Soft;
            report.AppendLine($"- Güneş ({sun.name}): şeftali, şiddet 2.2, yumuşak gölge");
        }
        else report.AppendLine("- Güneş bulunamadı (Directional Light yok)");

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = SkyAmbient;
        RenderSettings.ambientEquatorColor = EquatorAmbient;
        RenderSettings.ambientGroundColor = GroundAmbient;
        report.AppendLine("- Ortam ışığı: Gradient (gökyüzü krem, ufuk turkuaz-gri, zemin mavi-mor)");

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Horizon;
        RenderSettings.fogStartDistance = 30f;
        RenderSettings.fogEndDistance = 70f;
        report.AppendLine("- Sis: doğrusal, ufuk renginde, 30 → 70 (kamera uzaklığına göre ayarla)");
    }

    // Kameranın arka planı sisle aynı renk: ufukta çizgi olmasın
    private static void ApplyCamera(System.Text.StringBuilder report)
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            report.AppendLine("- Ana kamera bulunamadı (MainCamera etiketi yok)");
            return;
        }
        Undo.RecordObject(camera, "Render Stili");
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Horizon;
        report.AppendLine($"- Kamera ({camera.name}): arka plan düz renk, sisle aynı");
    }

    // Sahnedeki global Volume'un profili: Split Toning, Color Adjustments, Tonemapping, Bloom, Vignette
    private static void ApplyVolume(System.Text.StringBuilder report)
    {
        Volume volume = null;
        foreach (Volume candidate in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            if (candidate.isGlobal && candidate.sharedProfile != null) { volume = candidate; break; }
        if (volume == null)
        {
            report.AppendLine("- Global Volume bulunamadı; renk ayarı yapılmadı");
            return;
        }
        VolumeProfile profile = volume.sharedProfile;
        Undo.RecordObject(profile, "Render Stili");
        Undo.RecordObjects(profile.components.ToArray(), "Render Stili");

        SplitToning split = Get<SplitToning>(profile);
        split.shadows.Override(ShadowTone);
        split.highlights.Override(HighlightTone);
        split.balance.Override(0f);

        ColorAdjustments color = Get<ColorAdjustments>(profile);
        color.contrast.Override(5f);
        color.saturation.Override(8f);

        Tonemapping tonemapping = Get<Tonemapping>(profile);
        tonemapping.mode.Override(TonemappingMode.Neutral);

        Bloom bloom = Get<Bloom>(profile);
        bloom.threshold.Override(1.1f);
        bloom.intensity.Override(0.3f);
        bloom.scatter.Override(0.6f);

        Vignette vignette = Get<Vignette>(profile);
        vignette.intensity.Override(0.22f);
        vignette.smoothness.Override(0.45f);

        foreach (VolumeComponent component in profile.components) EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssetIfDirty(profile);
        report.AppendLine($"- Volume ({profile.name}): Split Toning, kontrast +5, doygunluk +8, Tonemapping Neutral, " +
                          "Bloom (eşik 1.1, yoğunluk 0.3), Vinyet 0.22");
    }

    // Profilde yoksa eklenir (yeni bileşen alt-asset olarak kaydedilir, geri alınabilir)
    private static T Get<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T component)) return component;
        component = profile.Add<T>();
        component.name = typeof(T).Name;
        component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
        if (EditorUtility.IsPersistent(profile))
        {
            AssetDatabase.AddObjectToAsset(component, profile);
            Undo.RegisterCreatedObjectUndo(component, "Render Stili");
        }
        return component;
    }

    // Projedeki bütün URP renderer'larının SSAO eklentisi: yoğunluk 0.6, yarıçap 0.35
    private static void ApplyAmbientOcclusion(System.Text.StringBuilder report)
    {
        int changed = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data == null) continue;
            foreach (ScriptableRendererFeature feature in data.rendererFeatures)
            {
                if (feature == null || feature.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                var serialized = new SerializedObject(feature);
                SerializedProperty intensity = serialized.FindProperty("m_Settings.Intensity");
                SerializedProperty radius = serialized.FindProperty("m_Settings.Radius");
                if (intensity == null || radius == null) continue;
                intensity.floatValue = 0.6f;
                radius.floatValue = 0.35f;
                serialized.ApplyModifiedProperties(); // Undo kaydı burada
                changed++;
            }
        }
        report.AppendLine(changed > 0
            ? $"- SSAO: yoğunluk 0.6, yarıçap 0.35 ({changed} renderer)"
            : "- SSAO eklentisi bulunamadı");
    }

    // Lighting penceresindeki ayarların nesnesi (Undo için); Unity bunu dışarı açmıyor
    private static Object RenderSettingsObject()
    {
        MethodInfo method = typeof(RenderSettings).GetMethod("GetRenderSettings", BindingFlags.Static | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as Object;
    }
}
