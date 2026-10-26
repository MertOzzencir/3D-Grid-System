using System;
using UnityEngine;

// Oynak gözler (googly eye): beyaz göz + içinde serbestçe yuvarlanan siyah göz bebeği, kodla kurulur (model gerekmez).
// Göz bebeği yer çekimiyle aşağı düşer, göz hareket edince ataletle çalkalanır (gözün ivmesinin tersine), kenara
// çarpınca seker. Her gözün ayrı bir "dinlenme" kayması var (derpRest): ikisi farklı yerlere bakar, şapşik durur.
// Sağ göz eyePosition'da, sol göz X'te aynası; gözler yanlara (yerel ±X), eyeAngle kadar öne dönük (burun +Z).
// İki göz ortak bir merkeze (Eyes) bağlı: frameRotation ikisini birlikte kafaya göre döndürür (kafa kemiğinin eksenleri
// Blender'dan farklı geldiyse gözlerin bütün düzenini çevirmek için); yerleri ve yönleri birlikte döner.
// Bağlandığı obje (anchor: kafa kemiği ya da modelin kökü) ile hareket eder; Tick her kare (LateUpdate) çağrılır.
// Yer, açı ve boylar her karede ayarlardan uygulanır: Play'deyken Inspector'dan değiştirince anında görünür.
[Serializable]
public class GooglyEyes
{
    [SerializeField] private bool enabled = true;
    [Tooltip("Sağ gözün yeri (anchor'a göre, yerel). Sol göz X'te aynası.")]
    [SerializeField] private Vector3 eyePosition = new Vector3(0.11f, -0.32f, 0.12f);
    [Tooltip("Gözlerin yanlardan öne dönüklüğü (derece; 0 = tam yana bakar)")]
    [SerializeField] private float eyeAngle = 25f;
    [Tooltip("İki gözün birlikte, kafaya (anchor) göre dönüşü (derece): gözlerin bütün düzeni döner")]
    [SerializeField] private Vector3 frameRotation;
    [Tooltip("Her gözün kendi etrafında ek dönüşü (derece, sağ göz; sol göz aynası: Y ve Z ters)")]
    [SerializeField] private Vector3 eyeRotation;
    [Tooltip("Gözün çapı ve göz bebeğinin çapı (birim)")]
    [SerializeField] private float eyeSize = 0.1f;
    [SerializeField] private float pupilSize = 0.05f;
    [Tooltip("Gözün dışarı şişkinliği (çapa oranla; küçük = yassı disk)")]
    [SerializeField, Range(0.1f, 1f)] private float bulge = 0.45f;
    [SerializeField] private Color eyeColor = Color.white;
    [SerializeField] private Color pupilColor = new Color(0.05f, 0.05f, 0.06f);

    [Header("Fizik")]
    [Tooltip("Göz bebeğini aşağı çeken yer çekimi")]
    [SerializeField] private float gravity = 4f;
    [Tooltip("Gözün hareketinden ne kadar çalkalanır")]
    [SerializeField] private float shake = 1f;
    [Tooltip("Sürtünme: büyük = çabuk durur")]
    [SerializeField] private float damping = 3f;
    [Tooltip("Kenara çarpınca sekme (0 = yapışır, 1 = tam seker)")]
    [SerializeField, Range(0f, 1f)] private float bounce = 0.45f;
    [Tooltip("Her gözün kendi dinlenme yerine çekilmesi (şapşik, iki göz farklı yere bakar)")]
    [SerializeField] private float derpPull = 2f;

    private class Eye
    {
        public Transform root;   // gözün merkezi, +Z dışarı bakar
        public Transform ball;
        public Transform pupil;
        public float side;       // sağ +1, sol -1
        public Vector2 offset;   // göz bebeğinin gözün düzlemindeki yeri
        public Vector2 velocity;
        public Vector2 derpRest; // bu gözün tembel bakış yeri
        public Vector3 lastPosition, lastVelocity;
    }

    private Eye left, right;
    private Transform frame; // iki gözün ortak merkezi (frameRotation)

    // anchor: gözlerin bağlanacağı obje (kafa kemiği ya da modelin kökü); shader: Village/Lit (build'e referansla girer)
    public void Build(Transform anchor, Shader shader)
    {
        if (!enabled || anchor == null) return;
        var white = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Lit")) { name = "Googly Eye (generated)", color = eyeColor };
        var black = new Material(white) { name = "Googly Pupil (generated)", color = pupilColor };
        frame = new GameObject("Eyes").transform;
        frame.SetParent(anchor, false);
        frame.localRotation = Quaternion.Euler(frameRotation);
        right = BuildEye(frame, 1f, white, black);
        left = BuildEye(frame, -1f, white, black);
    }

    private Eye BuildEye(Transform anchor, float side, Material white, Material black)
    {
        var root = new GameObject(side > 0f ? "Eye R" : "Eye L").transform;
        root.SetParent(anchor, false);
        var eye = new Eye
        {
            root = root,
            ball = Part(root, white).transform,
            pupil = Part(root, black).transform,
            side = side,
            derpRest = UnityEngine.Random.insideUnitCircle * 0.8f, // göz bebeğinin gidebileceği alana oranla
        };
        Layout(eye);
        eye.lastPosition = root.position;
        eye.offset = eye.derpRest * PupilRange;
        Place(eye);
        return eye;
    }

    // Gözün yeri, açısı ve boyları ayarlardan
    private void Layout(Eye eye)
    {
        eye.root.localPosition = new Vector3(eyePosition.x * eye.side, eyePosition.y, eyePosition.z);
        // Dışarı (+Z) yana bakar, eyeAngle kadar öne döner
        Quaternion look = Quaternion.LookRotation(Quaternion.Euler(0f, -eyeAngle * eye.side, 0f) * new Vector3(eye.side, 0f, 0f), Vector3.up);
        // Kendi etrafında: gözün kendi eksenlerinde (sol göz aynası)
        eye.root.localRotation = look * Quaternion.Euler(eyeRotation.x, eyeRotation.y * eye.side, eyeRotation.z * eye.side);
        eye.ball.localScale = new Vector3(eyeSize, eyeSize, eyeSize * bulge);
        eye.pupil.localScale = new Vector3(pupilSize, pupilSize, pupilSize * 0.35f);
    }

    private static GameObject Part(Transform parent, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        UnityEngine.Object.Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        var renderer = part.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return part;
    }

    // Göz bebeğinin merkezden en fazla uzaklığı (gözün kenarına değer)
    private float PupilRange => Mathf.Max(0f, (eyeSize - pupilSize) * 0.5f);

    public void Tick(float dt)
    {
        if (right == null || dt <= 0f) return;
        if (frame != null) frame.localRotation = Quaternion.Euler(frameRotation);
        Simulate(right, dt);
        Simulate(left, dt);
    }

    private void Simulate(Eye eye, float dt)
    {
        if (eye.root == null) return;
        Layout(eye);
        dt = Mathf.Min(dt, 0.05f);
        Vector3 position = eye.root.position;
        Vector3 velocity = (position - eye.lastPosition) / dt;
        Vector3 acceleration = (velocity - eye.lastVelocity) / dt;
        eye.lastPosition = position;
        eye.lastVelocity = velocity;

        // Göz bebeğine etkiyen kuvvet (dünya): yer çekimi + gözün ivmesinin tersi (atalet); gözün düzlemine izdüşümü
        Vector3 force = Vector3.down * gravity - Vector3.ClampMagnitude(acceleration, 60f) * (0.02f * shake);
        Vector2 local = new Vector2(Vector3.Dot(force, eye.root.right), Vector3.Dot(force, eye.root.up));
        local += (eye.derpRest * PupilRange - eye.offset) * derpPull;

        eye.velocity += local * dt;
        eye.velocity *= Mathf.Exp(-damping * dt);
        eye.offset += eye.velocity * dt;

        // Kenara çarpma: içeri seker
        float range = PupilRange;
        float distance = eye.offset.magnitude;
        if (distance > range && distance > 0f)
        {
            Vector2 normal = eye.offset / distance;
            eye.offset = normal * range;
            float into = Vector2.Dot(eye.velocity, normal);
            if (into > 0f) eye.velocity -= normal * into * (1f + bounce);
        }
        Place(eye);
    }

    // Göz bebeği gözün ön yüzünde, kaydığı yerde (yüzey eğrisine yaklaşık oturur)
    private void Place(Eye eye)
    {
        float t = Mathf.Clamp01(eye.offset.magnitude / (eyeSize * 0.5f));
        float depth = eyeSize * bulge * 0.5f * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
        eye.pupil.localPosition = new Vector3(eye.offset.x, eye.offset.y, depth);
    }
}
