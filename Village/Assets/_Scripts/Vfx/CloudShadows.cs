using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Yavaşça kayan stilize bulut gölgeleri: güneşin (Directional Light) cookie'si olarak.
// Her bulut, ortada büyük bir daire ve kenarından taşan küçük dairelerden oluşan "baloncuklu" bir silüet;
// gölge bu dairelerin birleşimi, kenarı hafif yumuşak.
// Doku dikişsiz tekrar eder ama geniş bir alanı (tileWorldSize) kaplar ve içinde çok sayıda, farklı boyda,
// rastgele dağılmış bulut vardır; böylece oyun alanından bakınca tekrar ya da düzenli ızgara fark edilmez.
// Işık dünyaya bu doku üzerinden vurduğu için Lit kullanan her şey (karolar, ağaçlar, odunlar) otomatik gölge alır;
// su shader'ı da cookie'yi kendisi okur. Doku açılışta üretilir, kaydedilmez.
// Directional Light objesine eklenir. Sadece Play mode'da çalışır.
[RequireComponent(typeof(Light))]
public class CloudShadows : MonoBehaviour
{
    [Header("Görünüm")]
    [Tooltip("Gölgenin ışığı ne kadar azalttığı (0.3 = %30)")]
    [SerializeField, Range(0f, 1f)] private float strength = 0.28f;
    [Tooltip("Ortalama bulut genişliği (birim)")]
    [SerializeField] private float cloudSize = 11f;
    [Tooltip("Bulut boyları bu aralıkta rastgele çarpılır (0.55 - 1.5 = küçükler de büyükler de olur)")]
    [SerializeField] private Vector2 sizeVariation = new Vector2(0.55f, 1.5f);
    [Tooltip("Bulut başına en az / en çok küçük baloncuk")]
    [SerializeField] private Vector2Int puffCount = new Vector2Int(6, 9);
    [Tooltip("Kenar yumuşaklığı (birim)")]
    [SerializeField, Range(0.05f, 3f)] private float edgeSoftness = 0.45f;
    [Tooltip("Arada bir bulutun yanına ikinci bir bulut yapışıp küme oluşturma olasılığı")]
    [SerializeField, Range(0f, 1f)] private float clusterChance = 0.3f;

    [Header("Dağılım")]
    [Tooltip("Dokunun dünyada kapladığı alan (birim). Desen bu mesafede bir tekrar eder; oyun alanından çok büyük olmalı.")]
    [SerializeField] private float tileWorldSize = 160f;
    [Tooltip("Dokudaki bulut sayısı. Gökyüzünün ne kadar bulutlu olduğunu belirler.")]
    [SerializeField, Range(1, 60)] private int cloudsPerTile = 18;
    [Tooltip("Bulut merkezleri arasındaki en küçük mesafe, bulut boyuna göre (üst üste binmesinler)")]
    [SerializeField, Range(0f, 2f)] private float minSpacing = 0.9f;

    [Header("Hareket")]
    [Tooltip("Birim/saniye")]
    [SerializeField] private float speed = 0.45f;
    [SerializeField, Range(0f, 360f)] private float direction = 35f;

    [Header("Doku")]
    [SerializeField] private int textureResolution = 1024;
    [SerializeField] private int seed = 7;

    private struct Circle
    {
        public Vector2 Center; // birim (0..tileWorldSize)
        public float Radius;
    }

    private Light sun;
    private UniversalAdditionalLightData sunData;
    private Texture2D texture;
    private Vector2 offset;

    private void Awake()
    {
        sun = GetComponent<Light>();
        sunData = sun.GetUniversalAdditionalLightData();
    }

    private void OnEnable() => Apply();

    private void OnDisable()
    {
        if (sun != null && sun.cookie == texture) sun.cookie = null;
        if (texture != null) Destroy(texture);
        texture = null;
    }

    private void Update()
    {
        float radians = direction * Mathf.Deg2Rad;
        offset += new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * (speed * Time.deltaTime);
        // Doku tekrar ettiği için bir tur sonra başa sarmak görüntüyü değiştirmez (float büyümesin)
        offset.x = Mathf.Repeat(offset.x, tileWorldSize);
        offset.y = Mathf.Repeat(offset.y, tileWorldSize);
        sunData.lightCookieOffset = offset;
    }

    // Inspector'da oynarken anında görünsün
    private void OnValidate()
    {
        if (Application.isPlaying && isActiveAndEnabled && sun != null) Apply();
    }

    private void Apply()
    {
        Generate();
        sun.cookie = texture;
        sunData.lightCookieSize = Vector2.one * tileWorldSize;
    }

    private void Generate()
    {
        int res = Mathf.Max(64, textureResolution);
        if (texture == null || texture.width != res)
        {
            if (texture != null) Destroy(texture);
            texture = new Texture2D(res, res, TextureFormat.RGBA32, true)
            {
                name = "Cloud Shadows (üretilen)",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
        }

        // Her piksel için dairelerin birleşimine işaretli uzaklık (içeride negatif).
        // Her daire sadece kendi çevresindeki pikselleri günceller; dokunmadığı yer bulutsuzdur.
        float pixelSize = tileWorldSize / res;
        var distance = new float[res * res];
        for (int i = 0; i < distance.Length; i++) distance[i] = float.MaxValue;

        foreach (Circle circle in CreateClouds())
        {
            float reach = circle.Radius + edgeSoftness;
            int minX = Mathf.FloorToInt((circle.Center.x - reach) / pixelSize);
            int maxX = Mathf.CeilToInt((circle.Center.x + reach) / pixelSize);
            int minY = Mathf.FloorToInt((circle.Center.y - reach) / pixelSize);
            int maxY = Mathf.CeilToInt((circle.Center.y + reach) / pixelSize);

            for (int y = minY; y <= maxY; y++)
            {
                int wrappedY = ((y % res) + res) % res; // kenardan taşan kısım karşı kenara
                float dy = (y + 0.5f) * pixelSize - circle.Center.y;
                for (int x = minX; x <= maxX; x++)
                {
                    int wrappedX = ((x % res) + res) % res;
                    float dx = (x + 0.5f) * pixelSize - circle.Center.x;
                    float signed = Mathf.Sqrt(dx * dx + dy * dy) - circle.Radius;
                    int index = wrappedY * res + wrappedX;
                    if (signed < distance[index]) distance[index] = signed;
                }
            }
        }

        var pixels = new Color32[res * res];
        for (int i = 0; i < pixels.Length; i++)
        {
            float cloud = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-edgeSoftness, edgeSoftness, distance[i]));
            byte light = (byte)Mathf.RoundToInt(255f * (1f - strength * cloud));
            pixels[i] = new Color32(light, light, light, 255);
        }

        texture.SetPixels32(pixels);
        texture.Apply(true);
    }

    // Bulutları dokuya rastgele dağıtır (en küçük mesafe kuralıyla), boyları farklı; arada bir kümelenir.
    private List<Circle> CreateClouds()
    {
        var random = new System.Random(seed);
        float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

        var circles = new List<Circle>();
        var placed = new List<(Vector2 center, float size)>();

        for (int c = 0; c < cloudsPerTile; c++)
        {
            float size = cloudSize * Range(sizeVariation.x, sizeVariation.y);

            // En küçük mesafeye uyan rastgele bir yer ara; bulunamazsa en iyi adayla yetin
            Vector2 center = default;
            float bestClearance = float.MinValue;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                var candidate = new Vector2(Range(0f, tileWorldSize), Range(0f, tileWorldSize));
                float clearance = float.MaxValue;
                foreach ((Vector2 otherCenter, float otherSize) in placed)
                    clearance = Mathf.Min(clearance, WrappedDistance(candidate, otherCenter) - (size + otherSize) * 0.5f * minSpacing);
                if (clearance > bestClearance)
                {
                    bestClearance = clearance;
                    center = candidate;
                }
                if (clearance >= 0f) break;
            }

            placed.Add((center, size));
            AddCloud(circles, center, size, random);

            // Küme: yanına yapışık, daha küçük ikinci bir bulut
            if (random.NextDouble() < clusterChance)
            {
                float angle = Range(0f, Mathf.PI * 2f);
                var direction2 = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float size2 = size * Range(0.45f, 0.7f);
                AddCloud(circles, center + direction2 * (size + size2) * 0.38f, size2, random);
            }
        }
        return circles;
    }

    // Tek bulut: ortada büyük daire + kenarından taşan, uzun eksen boyunca daha uzağa yayılan baloncuklar
    private void AddCloud(List<Circle> circles, Vector2 center, float size, System.Random random)
    {
        float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

        float mainRadius = size * 0.3f;
        float angle = Range(0f, Mathf.PI);
        var axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)); // bulutun uzandığı yön
        var side = new Vector2(-axis.y, axis.x);
        float stretch = Range(1.2f, 1.7f); // bazı bulutlar daha uzun, bazıları daha toparlak

        circles.Add(new Circle { Center = Wrap(center), Radius = mainRadius });

        int puffs = random.Next(puffCount.x, puffCount.y + 1);
        for (int p = 0; p < puffs; p++)
        {
            float around = (p + Range(-0.25f, 0.25f)) / puffs * Mathf.PI * 2f;
            float reach = mainRadius * Range(0.75f, 0.95f);
            Vector2 offset2 = axis * (Mathf.Cos(around) * reach * stretch) + side * (Mathf.Sin(around) * reach * 0.85f);
            circles.Add(new Circle { Center = Wrap(center + offset2), Radius = mainRadius * Range(0.5f, 0.72f) });
        }
    }

    private Vector2 Wrap(Vector2 point)
        => new Vector2(Mathf.Repeat(point.x, tileWorldSize), Mathf.Repeat(point.y, tileWorldSize));

    // Tekrar eden dokuda en kısa uzaklık
    private float WrappedDistance(Vector2 a, Vector2 b)
    {
        float dx = Mathf.Abs(a.x - b.x);
        float dy = Mathf.Abs(a.y - b.y);
        dx = Mathf.Min(dx, tileWorldSize - dx);
        dy = Mathf.Min(dy, tileWorldSize - dy);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }
}
