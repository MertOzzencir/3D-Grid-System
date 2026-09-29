using System;
using UnityEngine;

// Eldivenin parmakları: her parmak yüzeye değene kadar kıvrılır (yapışır), altında yüzey yoksa sarkar; yayla hareket eder.
//
// Kıvrılma: her parmağın tek serbestliği "kıvrılma" (0 = rest pozu, 1 = tam kıvrık). Toplam açı kemiklere kökten uca
// azalarak dağıtılır. Kıvrılma ekseni açılışta her kemik için hesaplanır: (kemiğin yönü) × (avucun baktığı yön);
// böylece parmak her zaman avuca doğru kapanır, Blender'daki bone roll ya da FBX eksen dönüşümü önemsizdir.
// Avuç kemiği (Blender'da *_Alt) varsa parmak kıvrıldıkça avuç hafifçe çukurlaşır.
//
// Temas: her kare, her parmak için "ucu yüzeye değene kadar ne kadar kıvrılmalı" ikili aramayla bulunur;
// değme testi parmak ucunda ve ara eklemlerde küre (CheckSphere). Parmaklar hedefe yayla gider (hafif taşar, geri gelir).
// GloveCursor yerleştikten sonra çalışır (execution order).
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(GloveCursor))]
public class GloveFingers : MonoBehaviour
{
    [Serializable]
    private class Finger
    {
        public string name;
        [Tooltip("Avuç kemiği (*_Alt). Parmak kıvrıldıkça hafifçe döner, avuç çukurlaşır. Başparmakta boş bırak.")]
        public Transform palmBone;
        [Tooltip("Kıvrılan kemikler, kökten uca (örn. Isaret_Orta, Isaret_Ust)")]
        public Transform[] bones = new Transform[0];
        [Tooltip("Parmak ucu (*_Ust_end)")]
        public Transform tip;
        [Tooltip("Tam kıvrılmada toplam açı (derece), kemiklere dağıtılır")]
        public float maxCurlAngle = 160f;

        [NonSerialized] public Quaternion[] rest;
        [NonSerialized] public Vector3[] axis;      // kemiğin yerel uzayında kıvrılma ekseni
        [NonSerialized] public float[] share;       // toplam açının bu kemiğe düşen payı
        [NonSerialized] public Quaternion palmRest;
        [NonSerialized] public Vector3 palmAxis;
        [NonSerialized] public float curl;          // şu anki (yaylı) değer
        [NonSerialized] public float velocity;
        [NonSerialized] public float noiseSeed;
        [NonSerialized] public bool valid;
    }

    [SerializeField] private Finger[] fingers = new Finger[0];

    [Header("Temas")]
    [Tooltip("Parmak kalınlığının yarısı (ölçek 1'de, dünya birimi). Parmak yüzeye bu kadar yaklaşınca değmiş sayılır.")]
    [SerializeField] private float fingerRadius = 0.035f;
    [Tooltip("En az kıvrılma (eksi = parmak hafif geriye esneyebilir)")]
    [SerializeField] private float minCurl = -0.1f;
    [Tooltip("Yüzeye ulaşmak için en fazla ne kadar kıvrılabilir")]
    [SerializeField] private float maxContactCurl = 0.9f;
    [Tooltip("Altında yüzey yoksa (kenardan taşınca) parmağın sarktığı kıvrılma")]
    [SerializeField] private float hangCurl = 0.45f;
    [SerializeField, Range(3, 10)] private int searchSteps = 7;

    [Header("Avuç")]
    [Tooltip("Parmak tam kıvrılınca avuç kemiğinin döndüğü açı (derece)")]
    [SerializeField] private float palmCupAngle = 12f;

    [Header("Yay")]
    [SerializeField] private float stiffness = 280f;
    [Tooltip("Düşük = daha çok taşar, sallanır")]
    [SerializeField] private float damping = 20f;

    [Header("Canlılık")]
    [Tooltip("Dururken parmakların hafif kıpırdaması")]
    [SerializeField] private float idleNoise = 0.03f;
    [SerializeField] private float idleSpeed = 0.7f;

    private GloveCursor cursor;

    private void Awake()
    {
        cursor = GetComponent<GloveCursor>();
    }

    // Rest pozunda kalibre: GloveCursor'ın Awake'inden sonra, ilk yerleşmeden önce
    private void Start()
    {
        if (cursor.PalmContact == null)
        {
            enabled = false;
            return;
        }

        Vector3 palmUp = cursor.PalmContact.up; // avucun baktığı yön
        foreach (Finger finger in fingers)
            Calibrate(finger, palmUp);
    }

    private void Calibrate(Finger finger, Vector3 palmUp)
    {
        int count = finger.bones.Length;
        finger.valid = count > 0 && finger.tip != null && Array.TrueForAll(finger.bones, b => b != null);
        if (!finger.valid)
        {
            Debug.LogWarning($"GloveFingers: '{finger.name}' parmağının kemikleri eksik, atlandı", this);
            return;
        }

        finger.rest = new Quaternion[count];
        finger.axis = new Vector3[count];
        finger.share = new float[count];

        // Pay kökten uca azalır: 2 kemikte 2/3 + 1/3, 3 kemikte 3/6 + 2/6 + 1/6
        float total = count * (count + 1) / 2f;
        for (int i = 0; i < count; i++)
        {
            Transform bone = finger.bones[i];
            Transform next = i + 1 < count ? finger.bones[i + 1] : finger.tip;
            finger.rest[i] = bone.localRotation;
            finger.share[i] = (count - i) / total;
            finger.axis[i] = CurlAxis(bone, next.position - bone.position, palmUp);
        }

        if (finger.palmBone != null)
        {
            finger.palmRest = finger.palmBone.localRotation;
            finger.palmAxis = CurlAxis(finger.palmBone, finger.bones[0].position - finger.palmBone.position, palmUp);
        }

        finger.noiseSeed = UnityEngine.Random.value * 100f;
    }

    // Unity'de AngleAxis(+açı, a × b) a'yı b'ye doğru döndürür: eksen = yön × avuç yönü → parmak avuca kapanır
    private static Vector3 CurlAxis(Transform bone, Vector3 direction, Vector3 palmUp)
    {
        Vector3 worldAxis = Vector3.Cross(direction.normalized, palmUp).normalized;
        return Quaternion.Inverse(bone.rotation) * worldAxis;
    }

    private void LateUpdate()
    {
        float radius = fingerRadius * transform.lossyScale.x;
        float dt = Mathf.Min(Time.deltaTime, 1f / 30f); // takılmada yay patlamasın

        foreach (Finger finger in fingers)
        {
            if (!finger.valid) continue;

            float target = SolveContact(finger, radius);
            target += (Mathf.PerlinNoise(finger.noiseSeed, Time.time * idleSpeed) - 0.5f) * 2f * idleNoise;

            // Sönümlü yay
            float acceleration = stiffness * (target - finger.curl) - damping * finger.velocity;
            finger.velocity += acceleration * dt;
            finger.curl += finger.velocity * dt;

            Apply(finger, finger.curl);
        }
    }

    // Yüzeye değmeden kıvrılabileceği en büyük değer. Hiç değmiyorsa (altında yüzey yok) sarkma değeri.
    private float SolveContact(Finger finger, float radius)
    {
        Apply(finger, minCurl);
        if (Touches(finger, radius)) return minCurl; // en açık halinde bile değiyor

        Apply(finger, maxContactCurl);
        if (!Touches(finger, radius)) return hangCurl; // hiç değmiyor: boşluğa sarkar

        float lo = minCurl, hi = maxContactCurl;
        for (int step = 0; step < searchSteps; step++)
        {
            float mid = (lo + hi) * 0.5f;
            Apply(finger, mid);
            if (Touches(finger, radius)) hi = mid;
            else lo = mid;
        }
        return lo;
    }

    // Parmak ucu ya da ara eklemleri bir yüzeye değiyor mu. Collider yoksa (su/boşluk) GloveCursor'ın düzlemine bakılır.
    private bool Touches(Finger finger, float radius)
    {
        if (TouchesAt(finger.tip.position, radius)) return true;
        for (int i = 1; i < finger.bones.Length; i++)
            if (TouchesAt(finger.bones[i].position, radius * 0.9f)) return true;
        return false;
    }

    private bool TouchesAt(Vector3 point, float radius)
    {
        if (Physics.CheckSphere(point, radius, cursor.SurfaceMask, QueryTriggerInteraction.Ignore)) return true;
        return !cursor.HasSurface && Vector3.Dot(point - cursor.SurfacePoint, cursor.SurfaceNormal) < radius;
    }

    // Avuç kemiği de dahil: arama sırasında avucun dönüşü parmak ucunun yerini etkiler
    private void Apply(Finger finger, float curl)
    {
        if (finger.palmBone != null)
            finger.palmBone.localRotation = finger.palmRest * Quaternion.AngleAxis(Mathf.Clamp01(curl) * palmCupAngle, finger.palmAxis);

        for (int i = 0; i < finger.bones.Length; i++)
            finger.bones[i].localRotation = finger.rest[i] * Quaternion.AngleAxis(curl * finger.maxCurlAngle * finger.share[i], finger.axis[i]);
    }

    // Senin rig'indeki isimlerden otomatik doldurur (Isaret/Orta/Yuzuk/Serce × Alt/Orta/Ust, Bas_Alt/Bas_Ust).
    // Farklı bir rig'de Inspector'dan elle atanır.
    [ContextMenu("Kemikleri isimden doldur")]
    private void FillFromNames()
    {
        Transform Find(string boneName)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (t.name == boneName) return t;
            return null;
        }

        Finger Make(string label, string prefix, bool isThumb) => new Finger
        {
            name = label,
            palmBone = isThumb ? null : Find(prefix + "_Alt"),
            bones = isThumb
                ? new[] { Find(prefix + "_Alt"), Find(prefix + "_Ust") }
                : new[] { Find(prefix + "_Orta"), Find(prefix + "_Ust") },
            tip = Find(prefix + "_Ust_end"),
            maxCurlAngle = isThumb ? 90f : 160f,
        };

        fingers = new[]
        {
            Make("Başparmak", "Bas", true),
            Make("İşaret", "Isaret", false),
            Make("Orta", "Orta", false),
            Make("Yüzük", "Yuzuk", false),
            Make("Serçe", "Serce", false),
        };
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.6f);
        float radius = fingerRadius * transform.lossyScale.x;
        foreach (Finger finger in fingers)
            if (finger?.tip != null) Gizmos.DrawWireSphere(finger.tip.position, radius);
    }
}
