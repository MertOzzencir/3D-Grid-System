using UnityEngine;

// Toon (cel) görünüm denemesinin tek anahtarı. Bileşen açıkken Lit kopyalarını kullanan bütün materyaller (Village/Lit,
// Wood Wobble Lit, Tree Chop Lit, Creature Jiggle Lit; bkz. Shaders/Toon.hlsl) sert geçişli ışıkla çizilir ve kontur
// çizgileri (Shaders/ToonOutline.shader, renderer'daki Full Screen Pass) görünür. Kapatınca ya da bileşen sahnede yoksa
// her şey birebir eski görünüm (URP Lit). Ayarlar global shader değerleri; Play'de değil editörde de anında görünür.
[ExecuteAlways]
public class ToonStyle : MonoBehaviour
{
    private static readonly int ToonId = Shader.PropertyToID("_VillageToon");
    private static readonly int ThresholdId = Shader.PropertyToID("_VillageToonThreshold");
    private static readonly int SoftnessId = Shader.PropertyToID("_VillageToonSoftness");
    private static readonly int ShadowColorId = Shader.PropertyToID("_VillageToonShadowColor");
    private static readonly int ShadowSaturationId = Shader.PropertyToID("_VillageToonShadowSaturation");
    private static readonly int SpecMinSmoothnessId = Shader.PropertyToID("_VillageToonSpecMinSmoothness");
    private static readonly int OutlineFadeDistanceId = Shader.PropertyToID("_VillageOutlineFadeDistance");
    private static readonly int SpecSizeId = Shader.PropertyToID("_VillageToonSpecSize");
    private static readonly int SpecStrengthId = Shader.PropertyToID("_VillageToonSpecStrength");
    private static readonly int AmbientId = Shader.PropertyToID("_VillageToonAmbient");
    private static readonly int ShadowSoftnessId = Shader.PropertyToID("_VillageToonShadowSoftness");
    private static readonly int NormalDetailId = Shader.PropertyToID("_VillageToonNormalDetail");
    private static readonly int MidOffsetId = Shader.PropertyToID("_VillageToonMidOffset");
    private static readonly int MidBrightnessId = Shader.PropertyToID("_VillageToonMidBrightness");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_VillageOutlineWidth");
    private static readonly int OutlineDepthId = Shader.PropertyToID("_VillageOutlineDepth");
    private static readonly int OutlineNormalId = Shader.PropertyToID("_VillageOutlineNormal");
    private static readonly int OutlineColorId = Shader.PropertyToID("_VillageOutlineColor");

    [Header("Işık")]
    [Tooltip("Yüzeyin ışığa bakma miktarı (-1..1) bu değeri geçince aydınlık. Büyüdükçe gölge alanı genişler.")]
    [SerializeField, Range(-1f, 1f)] private float threshold = 0.1f;
    [Tooltip("Aydınlık-gölge geçişinin genişliği. 0'a yakın = bıçak gibi keskin.")]
    [SerializeField, Range(0f, 0.5f)] private float softness = 0.02f;
    [Tooltip("Gölgede yüzeyin rengi bununla çarpılır (siyaha yakın = koyu gölge; hafif mor / mavi gölgenin tonunu kaydırır)")]
    [SerializeField] private Color shadowColor = new Color(0.5f, 0.52f, 0.75f);
    [Tooltip("Gölgede doygunluk artışı: 0 = sadece koyulaşır (kirli), büyüdükçe gölge canlı renkli olur")]
    [SerializeField, Range(0f, 1.5f)] private float shadowSaturation = 0.5f;
    [Tooltip("Ortam ışığının (gökyüzü) payı: gölgeli tarafların ne kadar aydınlanacağı")]
    [SerializeField, Range(0f, 1.5f)] private float ambient = 0.35f;
    [Tooltip("Gerçek zamanlı gölgelerin (objelerin birbirine düşürdüğü) kenar yumuşaklığı. Çok küçükse uzaktaki gölgeler " +
             "pürüzlü / lekeli olur.")]
    [SerializeField, Range(0f, 0.5f)] private float shadowSoftness = 0.15f;
    [Tooltip("Normal map'in (dokudaki oyuklar) ışığa etkisi. 1 = tam (uzakta lekeli bantlar yapabilir), 0 = sadece modelin şekli.")]
    [SerializeField, Range(0f, 1f)] private float normalDetail = 0.3f;
    [Tooltip("Ara ton: ışığa yan bakan aydınlık yerler bu kadar parlak (1 = ara ton yok, iki ton)")]
    [SerializeField, Range(0.5f, 1f)] private float midBrightness = 0.85f;
    [Tooltip("Ara tonun bittiği yer: eşikten bu kadar sonra tam aydınlık")]
    [SerializeField, Range(0.05f, 1f)] private float midOffset = 0.4f;

    [Header("Parlama")]
    [Tooltip("Parlama lekesinin büyüklüğü (0 = yok)")]
    [SerializeField, Range(0f, 0.5f)] private float specularSize = 0.05f;
    [Tooltip("Parlamanın gücü")]
    [SerializeField, Range(0f, 2f)] private float specularStrength = 0.4f;
    [Tooltip("Sadece Smoothness'ı bundan büyük materyaller parlar (metal, ıslak). Çim, toprak, odun mat kalsın diye.")]
    [SerializeField, Range(0f, 1f)] private float specularMinSmoothness = 0.6f;

    [Header("Kontur")]
    [Tooltip("Çizgi rengi = altındaki renk × bu (koyu = belirgin çizgi). A: çizginin gücü, 0 = kontur yok.")]
    [SerializeField] private Color outlineColor = new Color(0.3f, 0.25f, 0.3f, 1f);
    [Tooltip("Çizgi kalınlığı (piksel, 1080p'de; ekran çözünürlüğüyle ölçeklenir, her çözünürlükte aynı görünür)")]
    [SerializeField, Range(0.5f, 4f)] private float outlineWidth = 1f;
    [Tooltip("Bu uzaklıkta (birim) çizgi yarı kalınlığa iner: uzaktakiler incelir")]
    [SerializeField, Range(5f, 100f)] private float outlineFadeDistance = 40f;
    [Tooltip("Dış hat hassasiyeti: derinlik farkı (kameraya uzaklığa oranla) bunu geçince çizgi. Küçük = daha çok çizgi.")]
    [SerializeField, Range(0.01f, 1f)] private float outlineDepth = 0.1f;
    [Tooltip("Köşe hassasiyeti: komşu yüzeylerin yön farkı (0-2) bunu geçince çizgi. Küçük = daha çok çizgi.")]
    [SerializeField, Range(0.1f, 2f)] private float outlineNormal = 0.6f;

    private void OnEnable() => Apply();
    private void OnValidate() => Apply();

    // Kapatınca (ya da bileşen silinince) eski görünüm
    private void OnDisable() => Shader.SetGlobalFloat(ToonId, 0f);

    private void Apply()
    {
        if (!isActiveAndEnabled) return;
        Shader.SetGlobalFloat(ToonId, 1f);
        Shader.SetGlobalFloat(ThresholdId, threshold);
        Shader.SetGlobalFloat(SoftnessId, softness);
        Shader.SetGlobalColor(ShadowColorId, shadowColor);
        Shader.SetGlobalFloat(ShadowSaturationId, shadowSaturation);
        Shader.SetGlobalFloat(SpecMinSmoothnessId, specularMinSmoothness);
        Shader.SetGlobalFloat(OutlineFadeDistanceId, outlineFadeDistance);
        Shader.SetGlobalFloat(AmbientId, ambient);
        Shader.SetGlobalFloat(ShadowSoftnessId, shadowSoftness);
        Shader.SetGlobalFloat(NormalDetailId, normalDetail);
        Shader.SetGlobalFloat(MidOffsetId, midOffset);
        Shader.SetGlobalFloat(MidBrightnessId, midBrightness);
        Shader.SetGlobalFloat(SpecSizeId, specularSize);
        Shader.SetGlobalFloat(SpecStrengthId, specularStrength);
        Shader.SetGlobalColor(OutlineColorId, outlineColor);
        Shader.SetGlobalFloat(OutlineWidthId, outlineWidth);
        Shader.SetGlobalFloat(OutlineDepthId, outlineDepth);
        Shader.SetGlobalFloat(OutlineNormalId, outlineNormal);
    }
}
