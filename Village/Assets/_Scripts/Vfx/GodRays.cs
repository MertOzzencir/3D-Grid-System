using System.Collections.Generic;
using UnityEngine;

// Sabah ışık huzmeleri (godlight): güneşin geldiği yönden adaya eğik inen, yarı saydam, uçları sönen ışık şeritleri.
// Gün saatine bağlı (DayCycle.Time01): şafakta belirir, gün doğumunda en güçlü, kuşlukta söner (strength eğrisi).
// Huzmeler bu objenin çevresinde (area) rastgele yerlerde; her biri kendi hızında belirip kaybolur, kaybolunca başka
// bir yerde yeniden doğar ve bu arada yavaşça kayar. Her huzme kendi ekseni etrafında kameraya döner (yassı şerit hep
// geniş yüzüyle görünür), zemine doğru biraz genişler. Rengi güneşin o anki rengi; saydamlık / ton "Village/God Ray"
// materyalinden (tint). Tek mesh, her kare yeniden kurulur. Objeyi adanın ortasına koy.
public class GodRays : MonoBehaviour
{
    private struct Beam
    {
        public Vector2 offset;   // objeye göre (x, z)
        public float width;
        public float length;
        public float phase;      // 0..1 yaşam döngüsü
        public float speed;      // döngü hızı çarpanı
        public Vector2 drift;    // kayma yönü
    }

    [Tooltip("Village/God Ray. Referansla tutulur ki build'e girsin.")]
    [SerializeField] private Shader shader;
    [Tooltip("Boşsa RenderSettings.sun")]
    [SerializeField] private Light sun;
    [Tooltip("Gün saatine göre güç (DayCycle: 0 gün doğumu, 0.25 öğle, ~0.95 şafak). DayCycle yoksa 1.")]
    [SerializeField] private AnimationCurve strength = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(0.08f, 0.7f), new Keyframe(0.15f, 0f), new Keyframe(0.93f, 0f), new Keyframe(1f, 1f));
    [Tooltip("Huzmelerin tonu (güneşin rengiyle çarpılır). A: genel saydamlık.")]
    [SerializeField] private Color tint = new Color(1f, 0.92f, 0.75f, 0.3f);

    [Header("Huzmeler")]
    [SerializeField, Range(1, 24)] private int count = 8;
    [Tooltip("Huzmelerin indiği alan, objenin çevresinde (genişlik x, derinlik z)")]
    [SerializeField] private Vector2 area = new Vector2(14f, 14f);
    [Tooltip("Huzmelerin indiği yükseklik (dünya y): adanın üstü")]
    [SerializeField] private float groundHeight = 0.5f;
    [Tooltip("En (en az, en çok)")]
    [SerializeField] private Vector2 width = new Vector2(0.6f, 1.8f);
    [Tooltip("Boy (en az, en çok)")]
    [SerializeField] private Vector2 length = new Vector2(8f, 14f);
    [Tooltip("Zemine doğru genişleme (zemindeki en = en × bu)")]
    [SerializeField] private float spread = 1.4f;
    [Tooltip("Bir huzmenin belirip kaybolma süresi (saniye, ortalama)")]
    [SerializeField] private float lifetime = 9f;
    [Tooltip("Kayma hızı (birim/saniye)")]
    [SerializeField] private float driftSpeed = 0.15f;
    [Tooltip("Zemine / objelere değdiği yerde yumuşak sönme mesafesi (birim)")]
    [SerializeField] private float depthFade = 1f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int DepthFadeId = Shader.PropertyToID("_DepthFade");

    private readonly List<Beam> beams = new List<Beam>();
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Vector2> uvs = new List<Vector2>();
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
            Beam beam = NewBeam();
            beam.phase = Random.value; // başta hepsi aynı anda doğmasın
            beams.Add(beam);
        }
    }

    private Beam NewBeam()
    {
        float angle = Random.value * Mathf.PI * 2f;
        return new Beam
        {
            offset = new Vector2((Random.value - 0.5f) * area.x, (Random.value - 0.5f) * area.y),
            width = Random.Range(width.x, width.y),
            length = Random.Range(length.x, length.y),
            phase = 0f,
            speed = Random.Range(0.7f, 1.3f),
            drift = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)),
        };
    }

    private void Update()
    {
        float power = DayCycle.Instance != null ? strength.Evaluate(DayCycle.Instance.Time01) : 1f;
        bool visible = power > 0.001f && sun != null && Camera.main != null;
        meshRenderer.enabled = visible;

        // Huzmeler görünmezken de yaşar (birden hepsi aynı yerde belirmesin)
        float dt = Time.deltaTime;
        for (int i = 0; i < beams.Count; i++)
        {
            Beam beam = beams[i];
            beam.phase += dt * beam.speed / Mathf.Max(lifetime, 0.1f);
            beam.offset += beam.drift * (driftSpeed * dt);
            if (beam.phase >= 1f) beam = NewBeam();
            beams[i] = beam;
        }
        if (!visible) return;

        material.SetColor(ColorId, tint);
        material.SetFloat(DepthFadeId, depthFade);
        Rebuild(power);
    }

    private void Rebuild(float power)
    {
        vertices.Clear();
        uvs.Clear();
        colors.Clear();
        triangles.Clear();

        Vector3 down = sun.transform.forward; // ışığın gittiği yön
        Vector3 cameraPosition = Camera.main.transform.position;
        Color sunColor = sun.color;
        Transform holder = meshRenderer.transform;

        foreach (Beam beam in beams)
        {
            Vector3 bottom = new Vector3(transform.position.x + beam.offset.x, groundHeight, transform.position.z + beam.offset.y);
            Vector3 top = bottom - down * beam.length;
            // Şerit kendi ekseni etrafında kameraya döner
            Vector3 side = Vector3.Cross(down, cameraPosition - (top + bottom) * 0.5f).normalized * (beam.width * 0.5f);
            float alpha = Mathf.Sin(Mathf.PI * beam.phase) * power;
            var color = new Color(sunColor.r, sunColor.g, sunColor.b, alpha);

            int start = vertices.Count;
            vertices.Add(holder.InverseTransformPoint(top - side));
            vertices.Add(holder.InverseTransformPoint(top + side));
            vertices.Add(holder.InverseTransformPoint(bottom - side * spread));
            vertices.Add(holder.InverseTransformPoint(bottom + side * spread));
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f));
            for (int k = 0; k < 4; k++) colors.Add(color);
            triangles.AddRange(new[] { start, start + 1, start + 3, start, start + 3, start + 2 });
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
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
