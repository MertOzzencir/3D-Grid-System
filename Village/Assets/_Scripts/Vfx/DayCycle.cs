using UnityEngine;

// Gün saati: cycleMinutes'ta bir tam gün (sabah → öğle → altın saat → gece → sabah). Güneşin (gece: ayın) açısı, rengi,
// şiddeti; ortam ışığı (Gradient); sis ve kamera arka planı; kenar ışığı; uçuşanlar (gece ateş böcekleri) ve suyun gece
// kararması buna bağlı. Öğle değerleri "Render Stili: Diorama" menüsünün değerleriyle aynı.
// Saat kayda girer (SaveManager → SaveFile.dayTime). Sahnede bir tane.
// time: 0 gün doğumu, 0.25 öğle, 0.5 altın saat, ~0.6 gece başlar, ~0.95 şafak.
public class DayCycle : MonoBehaviour
{
    public static DayCycle Instance { get; private set; }

    [Tooltip("Bir tam günün süresi (dakika)")]
    [SerializeField] private float cycleMinutes = 20f;
    [Tooltip("0 gün doğumu, 0.25 öğle, 0.5 altın saat, ~0.6 gece, ~0.95 şafak. Kayıt yoksa buradan başlar.")]
    [SerializeField, Range(0f, 1f)] private float time = 0.1f;
    [Tooltip("Saati durdur (denemek için)")]
    [SerializeField] private bool paused;
    [Tooltip("Boşsa RenderSettings.sun")]
    [SerializeField] private Light sun;

    [Header("Güneş / ay")]
    [SerializeField] private Gradient sunColor = Make(("#FFD2A8", 0f), ("#FFDCB0", 0.25f), ("#FFB06A", 0.5f), ("#9FB4FF", 0.6f), ("#9FB4FF", 0.9f), ("#FFD2A8", 1f));
    [SerializeField] private AnimationCurve sunIntensity = Curve((0f, 1.6f), (0.25f, 2.2f), (0.5f, 1.9f), (0.58f, 0.5f), (0.65f, 0.45f), (0.9f, 0.45f), (0.97f, 0.9f), (1f, 1.6f));
    [Tooltip("Işığın yataydan yüksekliği (derece)")]
    [SerializeField] private AnimationCurve sunElevation = Curve((0f, 15f), (0.25f, 60f), (0.5f, 18f), (0.58f, 50f), (0.9f, 50f), (1f, 15f));
    [Tooltip("Gün boyunca güneşin dönüşü: başlangıç açısı (derece), tam günde 180° döner")]
    [SerializeField] private float azimuthStart = 120f;

    [Header("Ortam ışığı (Gradient)")]
    [SerializeField] private Gradient ambientSky = Make(("#FBEFE6", 0f), ("#FFF1E2", 0.25f), ("#FFD9C2", 0.5f), ("#3B4A78", 0.6f), ("#3B4A78", 0.9f), ("#FBEFE6", 1f));
    [SerializeField] private Gradient ambientEquator = Make(("#D9E6E8", 0f), ("#CCE0E3", 0.25f), ("#E8CFC2", 0.5f), ("#2E3A5E", 0.6f), ("#2E3A5E", 0.9f), ("#D9E6E8", 1f));
    [Tooltip("Zemin rengi = gölgelerin rengi (mavi-mor)")]
    [SerializeField] private Gradient ambientGround = Make(("#9AA6D8", 0f), ("#8F99D9", 0.25f), ("#8A86C9", 0.5f), ("#1D2340", 0.6f), ("#1D2340", 0.9f), ("#9AA6D8", 1f));

    [Header("Ufuk (sis + kamera arka planı)")]
    [SerializeField] private Gradient horizon = Make(("#DFE9EA", 0f), ("#CFE6E3", 0.25f), ("#F3D6C4", 0.5f), ("#1F2A45", 0.6f), ("#1F2A45", 0.9f), ("#DFE9EA", 1f));

    [Header("Gece")]
    [Tooltip("0 gündüz, 1 gece: ateş böcekleri, suyun kararması, kenar ışığının azalması")]
    [SerializeField] private AnimationCurve night = Curve((0f, 0f), (0.5f, 0.1f), (0.6f, 1f), (0.9f, 1f), (1f, 0f));
    [Tooltip("Suyun gece rengi çarpanı (StylizedWater, global)")]
    [SerializeField] private Color waterNightTint = new Color(0.28f, 0.36f, 0.55f);

    private static readonly int NightDarkenId = Shader.PropertyToID("_NightDarken");
    private static readonly int NightTintId = Shader.PropertyToID("_NightTint");

    private RimLight rim;
    private Light rimLight;
    private float rimBaseIntensity;
    private AmbientParticles particles;

    public float Time01
    {
        get => time;
        set
        {
            time = Mathf.Repeat(value, 1f);
            Apply();
        }
    }

    public float Night => night.Evaluate(time);

    private void Awake()
    {
        Instance = this;
        if (sun == null) sun = RenderSettings.sun;
        rim = FindFirstObjectByType<RimLight>();
        if (rim != null)
        {
            rimLight = rim.GetComponent<Light>();
            rimBaseIntensity = rimLight.intensity;
        }
        particles = FindFirstObjectByType<AmbientParticles>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Shader.SetGlobalFloat(NightDarkenId, 0f); // su gündüz rengine dönsün
    }

    private void Update()
    {
        if (!paused) time = Mathf.Repeat(time + Time.deltaTime / Mathf.Max(cycleMinutes * 60f, 1f), 1f);
        Apply();
    }

    private void Apply()
    {
        float nightAmount = night.Evaluate(time);

        if (sun != null)
        {
            float azimuth = azimuthStart - time * 180f;
            sun.transform.rotation = Quaternion.Euler(sunElevation.Evaluate(time), azimuth, 0f);
            sun.color = sunColor.Evaluate(time);
            sun.intensity = sunIntensity.Evaluate(time);
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambientSky.Evaluate(time);
        RenderSettings.ambientEquatorColor = ambientEquator.Evaluate(time);
        RenderSettings.ambientGroundColor = ambientGround.Evaluate(time);

        Color sky = horizon.Evaluate(time);
        RenderSettings.fogColor = sky;
        if (Camera.main != null) Camera.main.backgroundColor = sky;

        if (rimLight != null)
        {
            rimLight.color = sunColor.Evaluate(time);
            rimLight.intensity = rimBaseIntensity * Mathf.Lerp(1f, 0.4f, nightAmount);
        }
        if (particles != null) particles.NightAmount = nightAmount;

        Shader.SetGlobalFloat(NightDarkenId, nightAmount);
        Shader.SetGlobalColor(NightTintId, waterNightTint);
    }

    // --- Varsayılan eğriler ---

    private static Gradient Make(params (string hex, float time)[] keys)
    {
        var colors = new GradientColorKey[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            ColorUtility.TryParseHtmlString(keys[i].hex, out Color color);
            colors[i] = new GradientColorKey(color, keys[i].time);
        }
        var gradient = new Gradient();
        gradient.SetKeys(colors, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return gradient;
    }

    private static AnimationCurve Curve(params (float time, float value)[] keys)
    {
        var curve = new AnimationCurve();
        foreach ((float t, float v) in keys) curve.AddKey(new Keyframe(t, v));
        for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
        return curve;
    }
}
