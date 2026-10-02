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
//
// Base üstünde (GloveCursor.OnBase): dururken el yerde, işaret parmağı sabırsızca vurur (tık-tık-tık, duraklama).
// Yürürken (WalkWeight) işaret ve orta parmak bacak olur: kök eklem kalça gibi öne-arkaya sallanır, sonraki eklemler
// diz gibi bükülüp ayağı kaldırır; diğer parmaklar kapalı. Adım döngüsü katedilen mesafeyle ilerler (GloveCursor.WalkPhase).
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(GloveCursor))]
public class GloveFingers : MonoBehaviour
{
    private enum FingerRole { Unknown, Thumb, Index, Middle, Ring, Pinky }

    [Serializable]
    private class Finger
    {
        public string name;
        [Tooltip("Yürüme ve bekleme hareketlerinde hangi parmak olduğu")]
        public FingerRole role;
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
        [NonSerialized] public float tipCurve;      // uç ekleme ek kıvrım (derece), temas aramasına dahil
        [NonSerialized] public Vector3[] sideAxis;  // başparmak: avuç düzleminde işaret parmağına doğru kıvrılma ekseni
        [NonSerialized] public float sideCurve;     // bu eksende toplam kıvrım (derece), uca doğru artan dağılımla
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

    [Header("Base Üstünde Bekleme")]
    [Tooltip("İşaret parmağının kalkma miktarı (kıvrılma cinsinden)")]
    [SerializeField] private float tapLift = 0.3f;
    [Tooltip("İşaret parmağının bir tam kalkıp inme süresi (saniye); kesintisiz, yumuşak ritim")]
    [SerializeField] private float tapPeriod = 1.1f;
    [Tooltip("Diğer parmakların uçlarından aşağı kıvrımı (derece, uç ekleme)")]
    [SerializeField] private float idleFingerCurve = 14f;
    [Tooltip("Bu kıvrımın işaret parmağının ritmine eşlik eden hafif kıpırtısı (derece)")]
    [SerializeField] private float idleFingerCurveMotion = 5f;
    [Tooltip("Parmaklar arası gecikme (saniye): kıpırtı dalga gibi yayılır")]
    [SerializeField] private float idleFingerDelay = 0.08f;
    [Tooltip("İşaret parmağı kalkarken ucunun ek kıvrılması (derece, tepede tam); iniyorken açılır")]
    [SerializeField] private float indexTapCurve = 18f;
    [Tooltip("Başparmağın avuç düzleminde işaret parmağına doğru içe kıvrımı (derece)")]
    [SerializeField] private float thumbCurve = 20f;
    [Tooltip("Bu kıvrımın ritme eşlik eden kıpırtısı (derece)")]
    [SerializeField] private float thumbCurveMotion = 6f;

    [Header("Base Üstünde Yürüme (işaret + orta parmak bacak)")]
    [Tooltip("El yatayken bacakların kök eklemden aşağı kıvrılma açısı (derece): 90 = dimdik aşağı, az = uçlar öne")]
    [SerializeField] private float legDownAngle = 75f;

    [Header("Yakalanınca (kedi ağzında)")]
    [Tooltip("Parmakların ortalama kıvrılması (0 düz, 1 tam kıvrık)")]
    [SerializeField, Range(0f, 1f)] private float capturedCurl = 0.35f;
    [Tooltip("Çırpınma: kıvrılmanın inip çıkma miktarı ve hızı (salınım/sn); parmaklar sırayla çırpınır")]
    [SerializeField] private float capturedFlail = 0.35f;
    [SerializeField] private float capturedFlailSpeed = 2.4f;
    [Tooltip("Kalçanın (parmak kök eklemi) öne-arkaya sallanması (derece)")]
    [SerializeField] private float hipSwing = 25f;
    [Tooltip("Yere basarken dizin (sonraki eklemler) hafif bükülmesi (derece)")]
    [SerializeField] private float kneeBend = 8f;
    [Tooltip("Adım atarken dizin ek bükülmesi, ayağı kaldırır (derece)")]
    [SerializeField] private float kneeLift = 55f;
    [Tooltip("Yürürken diğer parmakların (başparmak, yüzük, serçe) kapanması")]
    [SerializeField, Range(0f, 1f)] private float closedCurl = 0.95f;

    private GloveCursor cursor;

    // Yürürken avucun yerden yüksekliği (ölçek 1'de): el yatay, bacak kök eklemden legDownAngle ile aşağı iniyor
    public float WalkPalmHeight { get; private set; } = 0.2f;

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

        CalibrateThumbSide(palmUp);
        MeasureWalkHeight();
    }

    // Başparmağın yana (işaret parmağına doğru) kıvrılma ekseni: yön × (işaret köküne doğru, avuç düzleminde).
    // Unity'de AngleAxis(+açı, a × b) a'yı b'ye döndürdüğü için artı açı başparmağı işaret parmağına kıvırır.
    private void CalibrateThumbSide(Vector3 palmUp)
    {
        Finger thumb = Array.Find(fingers, f => f.valid && f.role == FingerRole.Thumb);
        Finger index = Array.Find(fingers, f => f.valid && f.role == FingerRole.Index);
        if (thumb == null || index == null) return;

        Vector3 thumbDirection = thumb.tip.position - thumb.bones[0].position;
        Vector3 towardIndex = Vector3.ProjectOnPlane(index.bones[0].position - thumb.tip.position, palmUp);
        Vector3 worldAxis = Vector3.Cross(thumbDirection.normalized, towardIndex.normalized).normalized;
        if (worldAxis.sqrMagnitude < 0.5f) return;

        thumb.sideAxis = new Vector3[thumb.bones.Length];
        for (int i = 0; i < thumb.bones.Length; i++)
            thumb.sideAxis[i] = Quaternion.Inverse(thumb.bones[i].rotation) * worldAxis;
    }

    // Bacak = işaret parmağı. El yatayken (avuç aşağı) avuç ortasının yerden yüksekliği:
    // bacağın dikey boyu (boy × sin(legDownAngle)) + kök eklemin avuç yüzeyine göre aşağıda kalan kısmı
    private void MeasureWalkHeight()
    {
        Finger leg = Array.Find(fingers, f => f.valid && f.role == FingerRole.Index);
        if (leg == null) return;

        Transform palm = cursor.PalmContact;
        float knuckleBelowPalm = Vector3.Dot(leg.bones[0].position - palm.position, palm.up); // avuç yönü = aşağı
        float length = 0f;
        for (int i = 0; i < leg.bones.Length; i++)
        {
            Transform next = i + 1 < leg.bones.Length ? leg.bones[i + 1] : leg.tip;
            length += Vector3.Distance(leg.bones[i].position, next.position);
        }
        float height = length * Mathf.Sin(legDownAngle * Mathf.Deg2Rad) + knuckleBelowPalm;
        WalkPalmHeight = Mathf.Max(0f, height) / Mathf.Max(transform.lossyScale.x, 0.0001f);
    }

    // Rol atanmamışsa (eski ayarlar) adından çıkar.
    // Türkçe harfler önce ASCII'ye çevrilir: ToLowerInvariant "İ"yi "i + birleşik nokta" yapıyor, "İşaret" eşleşmiyordu.
    private static FingerRole InferRole(string name)
    {
        string n = (name ?? "")
            .Replace('İ', 'i').Replace('I', 'i').Replace('ı', 'i')
            .Replace('Ş', 's').Replace('ş', 's')
            .Replace('Ç', 'c').Replace('ç', 'c')
            .Replace('Ü', 'u').Replace('ü', 'u')
            .Replace('Ö', 'o').Replace('ö', 'o')
            .Replace('Ğ', 'g').Replace('ğ', 'g')
            .ToLowerInvariant();
        if (n.Contains("bas") || n.Contains("thumb")) return FingerRole.Thumb;
        if (n.Contains("isaret") || n.Contains("index")) return FingerRole.Index;
        if (n.Contains("orta") || n.Contains("middle")) return FingerRole.Middle;
        if (n.Contains("yuzuk") || n.Contains("ring")) return FingerRole.Ring;
        if (n.Contains("serce") || n.Contains("pinky")) return FingerRole.Pinky;
        return FingerRole.Unknown;
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
        if (finger.role == FingerRole.Unknown) finger.role = InferRole(finger.name);
        if (finger.role == FingerRole.Unknown)
            Debug.LogWarning($"GloveFingers: '{finger.name}' parmağının rolü bulunamadı; Inspector'dan Role seç", this);
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
        float walk = cursor.WalkWeight;

        bool idleOnBase = cursor.OnBase && !cursor.IsGripping;

        for (int f = 0; f < fingers.Length; f++)
        {
            Finger finger = fingers[f];
            if (!finger.valid) continue;

            // Base üstünde beklerken işaret dışındaki parmakların uçları hafifçe aşağı kıvrık, işaretin ritmine eşlik eder.
            // Temas aramasına dahil: uçlar yere basar, parmak kökü hafif kemerlenir.
            // Başparmak aşağı değil, yana (işaret parmağına doğru) kıvrılır; kıpırtısı da o eksende.
            // İşaret parmağı da uçtan kıvrık; kalktıkça ucu daha çok kıvrılır (tıklama dalgasıyla aynı anda).
            bool isIndex = finger.role == FingerRole.Index;
            bool isThumb = finger.role == FingerRole.Thumb && finger.sideAxis != null;
            float idleWave = IdleWave(Time.time - f * idleFingerDelay);
            float tipCurve = isIndex
                ? idleFingerCurve + indexTapCurve * IdleWave(Time.time)
                : idleFingerCurve + idleFingerCurveMotion * idleWave;
            finger.tipCurve = idleOnBase && !isThumb ? tipCurve * (1f - walk) : 0f;
            finger.sideCurve = idleOnBase && isThumb
                ? (thumbCurve + thumbCurveMotion * idleWave) * (1f - walk)
                : 0f;

            // Alet sapını kavrarken yüzey aranmaz (aletin collider'ı sallanmada dönmüyor), sabit kavramaya gidilir.
            // Tam yürürken de aranmaz: poz tamamen yürüme döngüsünden gelir.
            float target;
            if (cursor.IsCaptured)
                target = capturedCurl + Mathf.Sin((Time.time * capturedFlailSpeed + f * 0.27f) * Mathf.PI * 2f) * capturedFlail;
            else if (cursor.IsGripping) target = cursor.GripCurl;
            else if (walk >= 0.999f) target = finger.curl;
            else target = SolveContact(finger, radius);
            target += (Mathf.PerlinNoise(finger.noiseSeed, Time.time * idleSpeed) - 0.5f) * 2f * idleNoise;

            // Sönümlü yay
            float acceleration = stiffness * (target - finger.curl) - damping * finger.velocity;
            finger.velocity += acceleration * dt;
            finger.curl += finger.velocity * dt;

            // Base üstünde beklerken işaret parmağı yavaşça kalkıp iner (eksi kıvrılma = yukarı kalkar)
            float tap = idleOnBase && isIndex ? tapLift * IdleWave(Time.time) * (1f - walk) : 0f;
            Apply(finger, finger.curl - tap);

            if (walk > 0f && !cursor.IsGripping)
                BlendTowardWalk(finger, Mathf.SmoothStep(0f, 1f, walk));
        }
    }

    // Bekleme ritmi: 0 → 1 → 0, kesintisiz ve yumuşak (tepede ve dipte yavaşlar)
    private float IdleWave(float time)
        => 0.5f - 0.5f * Mathf.Cos(time / Mathf.Max(tapPeriod, 0.01f) * Mathf.PI * 2f);

    // Yürüme pozu: işaret ve orta parmak bacak (yarım döngü farkla), diğerleri kapalı. Mevcut pozdan weight kadar geçilir.
    private void BlendTowardWalk(Finger finger, float weight)
    {
        bool isLeg = finger.role == FingerRole.Index || finger.role == FingerRole.Middle;

        if (finger.palmBone != null)
        {
            Quaternion palmWalk = finger.palmRest * Quaternion.AngleAxis(isLeg ? 0f : palmCupAngle, finger.palmAxis);
            finger.palmBone.localRotation = Quaternion.Slerp(finger.palmBone.localRotation, palmWalk, weight);
        }

        for (int i = 0; i < finger.bones.Length; i++)
        {
            float angle = isLeg
                ? LegAngle(finger, i)
                : closedCurl * finger.maxCurlAngle * finger.share[i];
            Quaternion walkRotation = finger.rest[i] * Quaternion.AngleAxis(angle, finger.axis[i]);
            finger.bones[i].localRotation = Quaternion.Slerp(finger.bones[i].localRotation, walkRotation, weight);
        }
    }

    // Bacak döngüsü (0..1): ilk yarı yere basar ve geriye iter (kalça önden arkaya), ikinci yarı dizini büküp
    // öne atılır. Pozitif açı avuca doğru; el yatayken avuç aşağı baktığı için kök eklem legDownAngle ile aşağı
    // iner, bunun üstüne artı = bacak geriye.
    private float LegAngle(Finger finger, int boneIndex)
    {
        float cycle = Mathf.Repeat(cursor.WalkPhase + (finger.role == FingerRole.Middle ? 0.5f : 0f), 1f);
        float hip, knee;
        if (cycle < 0.5f)
        {
            float s = cycle / 0.5f;
            hip = Mathf.Lerp(-hipSwing, hipSwing, s);
            knee = kneeBend;
        }
        else
        {
            float s = (cycle - 0.5f) / 0.5f;
            hip = Mathf.Lerp(hipSwing, -hipSwing, Mathf.SmoothStep(0f, 1f, s));
            knee = kneeBend + kneeLift * Mathf.Sin(s * Mathf.PI);
        }

        if (boneIndex == 0) return legDownAngle + hip;
        return knee / Mathf.Max(1, finger.bones.Length - 1); // diz bükülmesi sonraki eklemlere paylaşılır
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

        int last = finger.bones.Length - 1;
        for (int i = 0; i <= last; i++)
        {
            float angle = curl * finger.maxCurlAngle * finger.share[i] + (i == last ? finger.tipCurve : 0f);
            Quaternion rotation = finger.rest[i] * Quaternion.AngleAxis(angle, finger.axis[i]);

            // Yana kıvrım uca doğru artar (2 kemikte %40 + %60): doğal bir kavis
            if (finger.sideAxis != null && finger.sideCurve != 0f)
            {
                float sideShare = last == 0 ? 1f : Mathf.Lerp(0.4f, 0.6f, (float)i / last) / (last == 1 ? 1f : (last + 1) * 0.5f);
                rotation *= Quaternion.AngleAxis(finger.sideCurve * sideShare, finger.sideAxis[i]);
            }
            finger.bones[i].localRotation = rotation;
        }
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
            role = InferRole(label),
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
