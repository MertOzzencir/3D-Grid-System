using System.Collections.Generic;
using UnityEngine;

// Sabah ışık huzmeleri: sabah pusunun (DayCycle) içinde, güneşin geldiği yönden adaya eğik inen az sayıda, geniş, çok
// hafif ışık huzmesi. Gücü DayCycle.Morning (pusla aynı anda gelir, gider). Huzmeler kaybolmaz: sadece nefes alır gibi
// parlaklıkları hafifçe oynar (breatheAmount) ve alanın içinde çok yavaş kayar (kenara gelince geri döner, sıçramaz).
// Her huzme ekseni etrafında eşit aralıklı `planes` düzlemden kurulur; her düzlem kameraya baktığı kadar görünür (yandan
// bakılan düzlem söner): hangi açıdan bakılırsa bakılsın hacimli görünür, ince çizgiye dönmez. İçindeki toz çizgileri ve
// yumuşak kenarlar shader'da ("Village/God Ray"). Rengi güneşin o anki rengi × tint. Tek mesh, her kare kurulur.
// Objeyi adanın ortasına koy.
public class GodRays : MonoBehaviour
{
    private struct Beam
    {
        public Vector2 offset;   // objeye göre (x, z)
        public float width;
        public float length;
        public float phase;      // nefesin başlangıcı (0..1)
        public float speed;      // nefes hızı çarpanı
        public Vector2 drift;    // kayma yönü
        public float seed;       // toz desenini ayırır
    }

    [Tooltip("Village/God Ray. Referansla tutulur ki build'e girsin.")]
    [SerializeField] private Shader shader;
    [Tooltip("Boşsa RenderSettings.sun")]
    [SerializeField] private Light sun;
    [Tooltip("Huzmelerin tonu (güneşin rengiyle çarpılır). A: genel saydamlık (çok düşük tut: üst üste binerler).")]
    [SerializeField] private Color tint = new Color(1f, 0.92f, 0.75f, 0.12f);

    [Header("Huzmeler")]
    [SerializeField, Range(1, 16)] private int count = 5;
    [Tooltip("Bir huzmenin kaç düzlemden kurulduğu (çok = her açıdan daha dolgun)")]
    [SerializeField, Range(1, 4)] private int planes = 3;
    [Tooltip("Huzmelerin indiği alan, objenin çevresinde (genişlik x, derinlik z)")]
    [SerializeField] private Vector2 area = new Vector2(14f, 14f);
    [Tooltip("Huzmelerin indiği yükseklik (dünya y): adanın üstü")]
    [SerializeField] private float groundHeight = 0.5f;
    [Tooltip("En (en az, en çok)")]
    [SerializeField] private Vector2 width = new Vector2(2.5f, 5f);
    [Tooltip("Boy (en az, en çok)")]
    [SerializeField] private Vector2 length = new Vector2(10f, 16f);
    [Tooltip("Zemine doğru genişleme (zemindeki en = en × bu)")]
    [SerializeField] private float spread = 1.5f;
    [Tooltip("Nefes: parlaklığın ne kadar oynadığı (0 = sabit) ve bir nefesin süresi (saniye, ortalama)")]
    [SerializeField, Range(0f, 1f)] private float breatheAmount = 0.35f;
    [SerializeField] private float breathePeriod = 8f;
    [Tooltip("Kayma hızı (birim/saniye)")]
    [SerializeField] private float driftSpeed = 0.08f;

    [Header("Görünüm (shader)")]
    [Tooltip("Zemine / objelere değdiği yerde yumuşak sönme mesafesi (birim)")]
    [SerializeField] private float depthFade = 1.5f;
    [Tooltip("İçindeki toz çizgilerinin belirginliği (0 = düz ışık)")]
    [SerializeField, Range(0f, 1f)] private float dustAmount = 0.5f;
    [Tooltip("Toz çizgilerinin süzülme hızı")]
    [SerializeField] private float dustSpeed = 0.04f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int DepthFadeId = Shader.PropertyToID("_DepthFade");
    private static readonly int DustAmountId = Shader.PropertyToID("_DustAmount");
    private static readonly int DustSpeedId = Shader.PropertyToID("_DustSpeed");

    private readonly List<Beam> beams = new List<Beam>();
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Vector2> uvs = new List<Vector2>();
    private readonly List<Vector2> seeds = new List<Vector2>();
    private readonly List<Color> colors = new List<Color>();
    private readonly List<int> triangles = new List<int>();
    private Mesh mesh;
    private Material material;
    private MeshRenderer meshRenderer;

    private void Awake()
    {
        if (sun == null) sun = RenderSettings.sun;
        if (shader == null) shader = Shader.Find("Village/God Ray");
        if (shader == null)
        {
            Debug.LogWarning("GodRays: 'Village/God Ray' shader'ı bulunamadı.", this);
            enabled = false;
            return;
        }

        var holder = new GameObject("God Rays");
        holder.transform.SetParent(transform, false);
        mesh = new Mesh { name = "God Rays" };
        mesh.MarkDynamic();
        holder.AddComponent<MeshFilter>().sharedMesh = mesh;
        material = new Material(shader) { name = "God Rays (generated)" };
        meshRenderer = holder.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        for (int i = 0; i < count; i++)
        {
            float angle = Random.value * Mathf.PI * 2f;
            beams.Add(new Beam
            {
                offset = new Vector2((Random.value - 0.5f) * area.x, (Random.value - 0.5f) * area.y),
                width = Random.Range(width.x, width.y),
                length = Random.Range(length.x, length.y),
                phase = Random.value,
                speed = Random.Range(0.7f, 1.3f),
                drift = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)),
                seed = Random.value,
            });
        }
    }

    private void Update()
    {
        float power = DayCycle.Instance != null ? DayCycle.Instance.Morning : 1f;
        bool visible = power > 0.001f && sun != null && Camera.main != null;
        meshRenderer.enabled = visible;

        // Kayma: alanın kenarına gelen huzme geri döner (sıçramaz)
        float dt = Time.deltaTime;
        Vector2 half = area * 0.5f;
        for (int i = 0; i < beams.Count; i++)
        {
            Beam beam = beams[i];
            beam.offset += beam.drift * (driftSpeed * dt);
            if (Mathf.Abs(beam.offset.x) > half.x) beam.drift.x = -Mathf.Sign(beam.offset.x) * Mathf.Abs(beam.drift.x);
            if (Mathf.Abs(beam.offset.y) > half.y) beam.drift.y = -Mathf.Sign(beam.offset.y) * Mathf.Abs(beam.drift.y);
            beams[i] = beam;
        }
        if (!visible) return;

        material.SetColor(ColorId, tint);
        material.SetFloat(DepthFadeId, depthFade);
        material.SetFloat(DustAmountId, dustAmount);
        material.SetFloat(DustSpeedId, dustSpeed);
        Rebuild(power);
    }

    private void Rebuild(float power)
    {
        vertices.Clear();
        uvs.Clear();
        seeds.Clear();
        colors.Clear();
        triangles.Clear();

        Vector3 axis = sun.transform.forward; // ışığın gittiği yön (güneşten zemine)
        Vector3 cameraPosition = Camera.main.transform.position;
        Color sunColor = sun.color;
        Transform holder = meshRenderer.transform;
        // Eksene dik ilk yön; diğer düzlemler bunun eksen etrafında döndürülmüşü
        Vector3 baseSide = Vector3.Cross(axis, Vector3.up);
        if (baseSide.sqrMagnitude < 0.001f) baseSide = Vector3.right;
        baseSide.Normalize();
        float time = Time.time;

        foreach (Beam beam in beams)
        {
            Vector3 bottom = new Vector3(transform.position.x + beam.offset.x, groundHeight, transform.position.z + beam.offset.y);
            Vector3 top = bottom - axis * beam.length;
            Vector3 view = (cameraPosition - (top + bottom) * 0.5f).normalized;
            float breathe = 1f - breatheAmount * 0.5f * (1f + Mathf.Sin(2f * Mathf.PI * (time * beam.speed / Mathf.Max(breathePeriod, 0.1f) + beam.phase)));

            for (int k = 0; k < planes; k++)
            {
                Vector3 side = Quaternion.AngleAxis(180f * k / planes, axis) * baseSide;
                Vector3 normal = Vector3.Cross(axis, side);
                // Kameraya baktığı kadar görünür; düzlemlerin toplamı açıdan bağımsız olsun diye ortalanır
                float facing = Vector3.Dot(normal, view);
                float alpha = power * breathe * facing * facing * (planes > 1 ? 2f / planes : 1f);
                Vector3 halfWidth = side * (beam.width * 0.5f);
                var color = new Color(sunColor.r, sunColor.g, sunColor.b, alpha);
                var seed = new Vector2(beam.seed + k * 0.37f, 0f);

                int start = vertices.Count;
                vertices.Add(holder.InverseTransformPoint(top - halfWidth));
                vertices.Add(holder.InverseTransformPoint(top + halfWidth));
                vertices.Add(holder.InverseTransformPoint(bottom - halfWidth * spread));
                vertices.Add(holder.InverseTransformPoint(bottom + halfWidth * spread));
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                for (int v = 0; v < 4; v++)
                {
                    colors.Add(color);
                    seeds.Add(seed);
                }
                triangles.AddRange(new[] { start, start + 1, start + 3, start, start + 3, start + 2 });
            }
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, seeds);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
        if (mesh != null) Destroy(mesh);
    }
}
