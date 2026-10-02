using UnityEngine;
using UnityEngine.EventSystems;

// 3D eldiven imleç. Mouse'tan dünyaya ray atılır; eldiven, avucunun ortası (palmContact) çarpılan noktaya gelecek ve
// avucu yüzeye bakacak şekilde yerleşir. Normal etrafındaki dönüş kameraya göre sabittir: parmaklar hep ekranın
// "yukarısını" gösterir (kameranın yukarı yönü yüzeye izdüşürülür). Normal kenarlarda aniden değişebildiği için
// pozisyon ve rotasyon yumuşatılarak takip edilir. Hiçbir şeye çarpmazsa su seviyesindeki bir düzlemde durur.
// Mouse bir UI elemanının üstündeyken eldiven gizlenir, sistem imleci görünür.
//
// Kurulum: bu bileşen boş bir kök objede durur, eldiven modeli onun child'ıdır. Modelin içine avucun iç yüzeyinin
// ortasına boş bir obje (palmContact) konur: yeşil (Y) oku avucun baktığı yöne (avuçtan dışarı), mavi (Z) oku parmak uçlarına.
// Parmakların yüzeye yapışması ve durum pozları ayrı bir adım (bkz. CLAUDE.md "Eldiven imleç").
public class GloveCursor : MonoBehaviour
{
    [Header("Kurulum")]
    [Tooltip("Avucun iç yüzeyinin ortası. Y (yeşil) avucun baktığı yöne, Z (mavi) parmak uçlarına baksın.")]
    [SerializeField] private Transform palmContact;
    [Tooltip("Eldivenin oturabileceği yüzeyler")]
    [SerializeField] private LayerMask surfaceMask = ~0;
    [Tooltip("Hiçbir şeye çarpmazsa eldivenin durduğu yükseklik (su seviyesi)")]
    [SerializeField] private float fallbackHeight = 0f;
    [SerializeField] private float maxDistance = 200f;

    [Header("Duruş")]
    [Tooltip("Avucun yüzeyden yüksekliği (gömülmesin diye)")]
    [SerializeField] private float surfaceOffset = 0.02f;
    [Tooltip("Parmak uçlarını yüzeyden kaldıran açı (derece); avuç yüzeyde kalır")]
    [SerializeField] private float fingerLift = 10f;

    [Header("Takip")]
    [Tooltip("Büyüdükçe pozisyon mouse'u daha sıkı takip eder")]
    [SerializeField] private float moveSharpness = 25f;
    [Tooltip("Büyüdükçe yüzeye daha hızlı döner (kenarlardaki ani normal değişimini yumuşatır)")]
    [SerializeField] private float rotateSharpness = 14f;

    [Header("Tutma")]
    [Tooltip("Tutma anında eldivenin tutma noktasına kayma süresi; sonra objeye katı bağlı kalır")]
    [SerializeField] private float attachDuration = 0.08f;

    [Header("Yakalanınca (kedi ağzına alınca)")]
    [Tooltip("Yakalanınca ağza kayma süresi (saniye)")]
    [SerializeField] private float captureAttachDuration = 0.15f;

    [Header("Base Üstünde (yürüme / bekleme)")]
    [Tooltip("Bu hızın (birim/sn, ölçek 1'de) üstünde yürümeye başlar")]
    [SerializeField] private float walkStartSpeed = 0.4f;
    [Tooltip("Durduktan bu kadar sonra bekleme pozuna döner (saniye)")]
    [SerializeField] private float walkStopDelay = 0.2f;
    [Tooltip("Yürüme ↔ bekleme geçiş süresi (saniye)")]
    [SerializeField] private float walkBlendTime = 0.18f;
    [Tooltip("Elin gittiği yöne dönme hızı")]
    [SerializeField] private float turnSharpness = 10f;
    [Tooltip("Bir tam adım döngüsünde (iki adım) alınan yol (ölçek 1'de). Parmaklar yerde kayıyorsa bununla ayarla.")]
    [SerializeField] private float strideLength = 0.35f;
    [Tooltip("Yürürken elin öne-arkaya eğimi (derece); 0 = zemine paralel, artı = parmak kökleri yukarı")]
    [SerializeField] private float walkPalmTilt = 0f;
    [Tooltip("Her adımda elin zıplaması (ölçek 1'de)")]
    [SerializeField] private float walkBob = 0.025f;
    [Tooltip("Elin yürürkenki yüksekliği, parmak boyundan hesaplanana çarpan")]
    [SerializeField] private float walkHeightScale = 1f;

    private Camera cam;
    private Renderer[] renderers;
    private Vector3 palmLocalPosition;       // palmContact'ın kök objeye göre yeri
    private Quaternion palmLocalRotation;    // ve yönü
    private bool placedOnce;
    private bool hidden;

    // Tutulan obje ve eldivenin onu tuttuğu nokta (objenin yerel uzayında: obje dönse de nokta yerinde kalır)
    private Transform grabbed;
    private Vector3 grabLocalPoint;
    private Vector3 grabLocalNormal;

    // Eldivenin kök pozu, tutulan objenin yerel uzayında (child gibi): başlangıç → hedef, attachProgress ile
    private Vector3 attachLocalPosition, attachStartPosition;
    private Quaternion attachLocalRotation, attachStartRotation;
    private float attachProgress;

    // Alet sapı (IGloveGrip) tutuluyorsa: eldiven tutma noktasının child'ı olur, bırakınca eski parent'ına döner
    private IGloveGrip activeGrip;
    private Transform currentGripPoint;
    private Transform originalParent;

    // Base üstü hareket takibi
    private GloveFingers fingers;
    private Vector3 heading = Vector3.forward;   // elin gittiği yön (yüzeyde)
    private Vector3 lastSurfacePoint;
    private bool hasLastSurfacePoint;
    private float smoothedSpeed;
    private float lastMoveTime = float.MinValue;

    // Bir canlı yakaladı: mouse takip edilmez, pozu her kare yakalayan verir (FollowCapture)
    private bool captured;
    private float captureProgress;
    public bool IsCaptured => captured;

    // Base karosunun üstünde ve elde bir şey yok: yürüme / bekleme modu
    public bool OnBase { get; private set; }
    // 0 = bekleme (el yerde, işaret parmağı vuruyor), 1 = yürüme
    public float WalkWeight { get; private set; }
    // Adım döngüsü (tur sayısı): katedilen mesafeyle ilerler, parmaklar yerde kaymasın diye
    public float WalkPhase { get; private set; }

    // Parmakların ve durum sisteminin kullanacağı son yüzey bilgisi
    public Transform PalmContact => palmContact;
    public bool IsGripping => activeGrip != null;
    public float GripCurl => activeGrip != null ? activeGrip.GripCurl : 0f;
    public LayerMask SurfaceMask => surfaceMask;
    public bool HasSurface { get; private set; }
    public Vector3 SurfacePoint { get; private set; }
    public Vector3 SurfaceNormal { get; private set; } = Vector3.up;
    public Collider SurfaceCollider { get; private set; }

    private void Awake()
    {
        cam = Camera.main;
        renderers = GetComponentsInChildren<Renderer>(true);
        fingers = GetComponent<GloveFingers>();

        if (palmContact == null)
        {
            Debug.LogError("GloveCursor: Palm Contact atanmamış", this);
            enabled = false;
            return;
        }

        // Model kök objeye göre sabit durduğu için avucun yerel pozu bir kez alınır
        palmLocalPosition = transform.InverseTransformPoint(palmContact.position);
        palmLocalRotation = Quaternion.Inverse(transform.rotation) * palmContact.rotation;
    }

    private void OnEnable() => Cursor.visible = false;

    private void OnDisable()
    {
        EndGrip();
        Cursor.visible = true;
        SetHidden(false);
    }

    // Kamera ve taşınan objeler Update'te hareket ediyor; eldiven onlardan sonra yerleşsin
    private void LateUpdate()
    {
        // Yakalanmışken pozu yakalayan verir (kendi LateUpdate'inde, kemikleri pozlandıktan sonra)
        if (captured)
        {
            SetHidden(false);
            return;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        // Tutma başladıysa eldiven objeye katı bağlanır: tutarken ray atılmaz, yumuşatma yok (child gibi).
        // UI kontrolünden önce: mouse UI üstündeyken bırakılan aletten de hemen ayrılsın.
        Transform held = HeldTransform();
        if (held != grabbed)
        {
            EndGrip();
            grabbed = held;
            if (held != null) BeginGrab(held, ray);
        }

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        SetHidden(overUI);
        if (overUI) return;

        if (grabbed != null)
        {
            if (activeGrip != null) FollowGrip();
            else FollowGrab();
            return;
        }

        FindSurface(ray);
        UpdateBaseMotion();
        TargetRootPose(out Vector3 rootPosition, out Quaternion rootRotation);

        if (!placedOnce)
        {
            transform.SetPositionAndRotation(rootPosition, rootRotation);
            placedOnce = true;
            return;
        }

        // Kare hızından bağımsız yumuşatma
        float moveT = 1f - Mathf.Exp(-moveSharpness * Time.deltaTime);
        float rotateT = 1f - Mathf.Exp(-rotateSharpness * Time.deltaTime);
        transform.SetPositionAndRotation(
            Vector3.Lerp(transform.position, rootPosition, moveT),
            Quaternion.Slerp(transform.rotation, rootRotation, rotateT));
    }

    // SurfacePoint / SurfaceNormal'a göre kök objenin olması gereken pozu.
    // Normal: avuç yüzeye bakar (Y = -normal), parmaklar ekranın yukarısına (Z), parmak uçları fingerLift kadar kalkık.
    // Base üstünde: parmaklar kamera yerine elin gittiği yöne bakar. Yürürken el zemine paralel kalır (avuç aşağı),
    // bacak boyu kadar havaya kalkar; bacakları parmaklar kök ekleminden aşağı kıvrılarak yapar (GloveFingers).
    // İkisi WalkWeight ile karışır.
    private void TargetRootPose(out Vector3 rootPosition, out Quaternion rootRotation)
    {
        Vector3 normal = SurfaceNormal;
        Vector3 fingerDirection = OnBase ? heading : FingerDirectionOn(normal);
        Quaternion palmRotation = Quaternion.LookRotation(fingerDirection, -normal)
                                  * Quaternion.Euler(fingerLift, 0f, 0f); // +X etrafında: parmak uçları yüzeyden kalkar
        Vector3 palmPosition = SurfacePoint + normal * (surfaceOffset + PressOffset());

        if (WalkWeight > 0f && fingers != null)
        {
            float scale = transform.lossyScale.x;
            // Avuç aşağı, parmaklar gidiş yönüne; +X etrafında eksi açı parmak köklerini kaldırır
            Quaternion walkRotation = Quaternion.LookRotation(heading, -normal) * Quaternion.Euler(-walkPalmTilt, 0f, 0f);

            // Her adımda (yarım döngü) bir zıplama
            float bob = walkBob * scale * Mathf.Abs(Mathf.Sin(WalkPhase * Mathf.PI * 2f));
            float height = fingers.WalkPalmHeight * scale * walkHeightScale + bob;
            Vector3 walkPosition = SurfacePoint + normal * height;

            float w = Mathf.SmoothStep(0f, 1f, WalkWeight);
            palmRotation = Quaternion.Slerp(palmRotation, walkRotation, w);
            palmPosition = Vector3.Lerp(palmPosition, walkPosition, w);
        }

        rootRotation = palmRotation * Quaternion.Inverse(palmLocalRotation);
        rootPosition = palmPosition - rootRotation * Vector3.Scale(palmLocalPosition, transform.lossyScale);
    }

    // Base üstündeyse: hız, gidiş yönü, adım döngüsü ve yürüme ağırlığı. Mesafe, mouse'un yüzeydeki noktasından
    // (yumuşatılmamış hedef) ölçülür; el yumuşak takip etse de adımlar gerçek yola göre atılır.
    private void UpdateBaseMotion()
    {
        OnBase = SurfaceCollider != null && SurfaceCollider.GetComponentInParent<GridBase>() != null;

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float scale = transform.lossyScale.x;
        Vector3 delta = hasLastSurfacePoint ? SurfacePoint - lastSurfacePoint : Vector3.zero;
        lastSurfacePoint = SurfacePoint;
        hasLastSurfacePoint = true;

        Vector3 planar = Vector3.ProjectOnPlane(delta, SurfaceNormal);
        float distance = planar.magnitude;
        if (distance > 3f) distance = 0f; // ışınlanma (ör. kenardan uzak bir yüzeye atlama): adım sayma

        smoothedSpeed = Mathf.Lerp(smoothedSpeed, distance / dt, 1f - Mathf.Exp(-12f * dt));

        // Yön: hareket varsa hareketin yönüne dön; yoksa son yönü koru (yüzeye göre düzeltilmiş)
        if (distance > 0.0005f)
            heading = Vector3.Slerp(heading, planar / distance, 1f - Mathf.Exp(-turnSharpness * dt));
        heading = Vector3.ProjectOnPlane(heading, SurfaceNormal);
        heading = heading.sqrMagnitude > 0.0001f ? heading.normalized : FingerDirectionOn(SurfaceNormal);

        if (OnBase && smoothedSpeed > walkStartSpeed * scale) lastMoveTime = Time.time;
        bool walking = OnBase && Time.time - lastMoveTime < walkStopDelay;
        WalkWeight = Mathf.MoveTowards(WalkWeight, walking ? 1f : 0f, dt / Mathf.Max(walkBlendTime, 0.0001f));

        if (WalkWeight > 0f)
            WalkPhase += distance / Mathf.Max(strideLength * scale, 0.0001f);
    }

    // Elde tutulan obje (yok edildiyse Unity null'u → null). IGloveFreeHold ise eldiven ona bağlanmaz,
    // mouse'u takip etmeye devam eder (örn. kedinin göbeğini okşarken).
    private static Transform HeldTransform()
    {
        InteractableController controller = InteractableController.Instance;
        if (controller == null || controller.Held is IGloveFreeHold) return null;
        return controller.Held is Component component && component != null ? component.transform : null;
    }

    // Bir canlı eldiveni yakaladı (örn. kedi ağzına aldı): mouse takibi durur, eldiven yakalayanın verdiği noktaya oturur.
    // Bırakınca normal takip kaldığı yerden yumuşakça mouse'a döner.
    public void BeginCapture()
    {
        if (captured) return;
        EndGrip();
        captured = true;
        captureProgress = 0f;
        OnBase = false;
        WalkWeight = 0f;
        hasLastSurfacePoint = false;
        HasSurface = false;
        SurfaceCollider = null;
    }

    public void EndCapture()
    {
        captured = false;
        hasLastSurfacePoint = false; // ağızdan mouse'a dönüş adım sayılmasın
    }

    // Avucu (palmContact) verilen noktaya ve yöne oturtur. Yakalayan her kare, kendi pozu hesaplandıktan sonra çağırır.
    public void FollowCapture(Vector3 palmPosition, Quaternion palmRotation)
    {
        if (!captured) return;
        Quaternion rootRotation = palmRotation * Quaternion.Inverse(palmLocalRotation);
        Vector3 rootPosition = palmPosition - rootRotation * Vector3.Scale(palmLocalPosition, transform.lossyScale);

        captureProgress = Mathf.MoveTowards(captureProgress, 1f, Time.deltaTime / Mathf.Max(captureAttachDuration, 0.0001f));
        float t = captureProgress >= 1f ? 1f : Mathf.SmoothStep(0f, 1f, captureProgress);
        transform.SetPositionAndRotation(Vector3.Lerp(transform.position, rootPosition, t),
                                         Quaternion.Slerp(transform.rotation, rootRotation, t));
        SurfacePoint = palmPosition;
        SurfaceNormal = palmRotation * Vector3.down;
    }

    // Pat / şaplak gibi kısa hareket: avuç yüzeyden kalkıp geri iner. lift: kalkma yüksekliği (ölçek 1'de)
    public void PlayPress(float lift, float duration)
    {
        pressLift = lift;
        pressDuration = Mathf.Max(duration, 0.01f);
        pressTime = 0f;
    }

    private float pressLift, pressDuration = 1f, pressTime = float.MaxValue;

    // Şu anki kalkma: sürenin %60'ında kalkar, kalan %40'ında hızla iner (tokat gibi)
    private float PressOffset()
    {
        if (pressTime >= pressDuration) return 0f;
        pressTime += Time.deltaTime;
        float t = Mathf.Clamp01(pressTime / pressDuration);
        float shaped = t < 0.6f ? Mathf.Sin(t / 0.6f * Mathf.PI * 0.5f) : Mathf.Cos((t - 0.6f) / 0.4f * Mathf.PI * 0.5f);
        return pressLift * shaped * transform.lossyScale.x;
    }

    // Tutma anında eldivenin objedeki yeri: son kare zaten objenin üstündeysek o nokta, değilse mouse ray'inin objeye
    // çarptığı ilk nokta, o da yoksa objenin tepesi
    private void CaptureGrab(Transform held, Ray ray)
    {
        Vector3 point, normal;
        if (SurfaceCollider != null && SurfaceCollider.transform.IsChildOf(held))
        {
            point = SurfacePoint;
            normal = SurfaceNormal;
        }
        else if (TryRaycastOn(held, ray, out RaycastHit hit))
        {
            point = hit.point;
            normal = hit.normal;
        }
        else
        {
            Collider collider = held.GetComponentInChildren<Collider>();
            Bounds bounds = collider != null ? collider.bounds : new Bounds(held.position, Vector3.one);
            point = bounds.center + Vector3.up * bounds.extents.y;
            normal = Vector3.up;
        }

        grabLocalPoint = held.InverseTransformPoint(point);
        grabLocalNormal = held.InverseTransformDirection(normal);
    }

    private bool TryRaycastOn(Transform target, Ray ray, out RaycastHit result)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance, surfaceMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (!hit.collider.transform.IsChildOf(target)) continue;
            result = hit;
            return true;
        }
        result = default;
        return false;
    }

    // Tutma başı: tutulan nokta bulunur, eldivenin o noktadaki pozu objenin yerel uzayına çevrilip saklanır.
    // O andaki pozdan oraya attachDuration içinde kayar.
    private void BeginGrab(Transform held, Ray ray)
    {
        // Tutarken base modları kapalı (yürüme pozu tutma pozunu bozmasın)
        OnBase = false;
        WalkWeight = 0f;
        hasLastSurfacePoint = false;

        // Alet sapı: tutma noktasını alet kendisi söyler
        if (held.TryGetComponent(out IGloveGrip grip) && grip.GripPoint != null)
        {
            BeginGrip(grip);
            return;
        }

        CaptureGrab(held, ray);
        UpdateGrabSurface();
        TargetRootPose(out Vector3 rootPosition, out Quaternion rootRotation);

        Quaternion inverseHeld = Quaternion.Inverse(held.rotation);
        attachLocalPosition = held.InverseTransformPoint(rootPosition);
        attachLocalRotation = inverseHeld * rootRotation;
        attachStartPosition = held.InverseTransformPoint(transform.position);
        attachStartRotation = inverseHeld * transform.rotation;
        attachProgress = 0f;
    }

    // Katı bağlı takip: obje bu kare nereye gittiyse eldiven de aynı yerel pozda (yumuşatma yok, gecikme yok)
    private void FollowGrab()
    {
        UpdateGrabSurface();

        attachProgress = Mathf.MoveTowards(attachProgress, 1f, Time.deltaTime / Mathf.Max(attachDuration, 0.0001f));
        float t = Mathf.SmoothStep(0f, 1f, attachProgress);
        Vector3 localPosition = Vector3.Lerp(attachStartPosition, attachLocalPosition, t);
        Quaternion localRotation = Quaternion.Slerp(attachStartRotation, attachLocalRotation, t);
        transform.SetPositionAndRotation(grabbed.TransformPoint(localPosition), grabbed.rotation * localRotation);
    }

    // Alet sapı tutma: eldiven tutma noktasının gerçekten child'ı olur (alet hiç silinmez), avuç o noktaya oturur.
    // Alet dönünce ya da sallanınca (vuruş animasyonu görseli döndürür) eldiven de onunla hareket eder.
    private void BeginGrip(IGloveGrip grip)
    {
        activeGrip = grip;
        originalParent = transform.parent;
        AttachToGripPoint(grip.GripPoint);
    }

    // Eldiveni tutma noktasının child'ı yapar ve oraya attachDuration içinde kaydırır.
    // Alet tutarken başka bir noktaya geçerse (örn. döndürülünce kameraya göre başka yön seçilir) yine bu çağrılır.
    private void AttachToGripPoint(Transform point)
    {
        currentGripPoint = point;

        // Hedef kök poz dünyada: avuç (palmContact) tam tutma noktasında ve onunla aynı yönde
        Quaternion rootRotation = point.rotation * Quaternion.Inverse(palmLocalRotation);
        Vector3 rootPosition = point.position - rootRotation * Vector3.Scale(palmLocalPosition, transform.lossyScale);

        transform.SetParent(point, true);

        attachLocalPosition = point.InverseTransformPoint(rootPosition);
        attachLocalRotation = Quaternion.Inverse(point.rotation) * rootRotation;
        attachStartPosition = transform.localPosition;
        attachStartRotation = transform.localRotation;
        attachProgress = 0f;
    }

    // Child olduğu için aletin her hareketi otomatik; burada ilk oturma kayması ve nokta değişimi yapılır
    private void FollowGrip()
    {
        Transform point = activeGrip.GripPoint;
        if (point == null) return;
        if (point != currentGripPoint) AttachToGripPoint(point);

        HasSurface = true;
        SurfaceCollider = null;
        SurfacePoint = point.position;
        SurfaceNormal = -point.up; // avuç yüzeye (sapa) bakar

        attachProgress = Mathf.MoveTowards(attachProgress, 1f, Time.deltaTime / Mathf.Max(attachDuration, 0.0001f));
        float t = Mathf.SmoothStep(0f, 1f, attachProgress);
        transform.localPosition = Vector3.Lerp(attachStartPosition, attachLocalPosition, t);
        transform.localRotation = Quaternion.Slerp(attachStartRotation, attachLocalRotation, t);
    }

    private void EndGrip()
    {
        if (activeGrip == null) return;
        activeGrip = null;
        currentGripPoint = null;
        transform.SetParent(originalParent, true);
    }

    // Parmaklar tutulan noktanın yüzeyini kullanır
    private void UpdateGrabSurface()
    {
        HasSurface = true;
        SurfaceCollider = null;
        SurfacePoint = grabbed.TransformPoint(grabLocalPoint);
        SurfaceNormal = grabbed.TransformDirection(grabLocalNormal).normalized;
    }

    private void FindSurface(Ray ray)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, surfaceMask, QueryTriggerInteraction.Ignore))
        {
            HasSurface = true;
            SurfacePoint = hit.point;
            SurfaceNormal = hit.normal;
            SurfaceCollider = hit.collider;
            return;
        }

        // Boşluk / su: su seviyesindeki yatay düzlem
        HasSurface = false;
        SurfaceCollider = null;
        SurfaceNormal = Vector3.up;
        var plane = new Plane(Vector3.up, new Vector3(0f, fallbackHeight, 0f));
        SurfacePoint = plane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : ray.GetPoint(20f);
    }

    // Kameranın yukarı yönünün yüzeydeki izdüşümü. Yüzey kameranın yukarısına dik bakıyorsa (izdüşüm sıfıra yakın)
    // kameranın ileri yönü kullanılır.
    private Vector3 FingerDirectionOn(Vector3 normal)
    {
        Vector3 direction = Vector3.ProjectOnPlane(cam.transform.up, normal);
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.ProjectOnPlane(cam.transform.forward, normal);
        return direction.normalized;
    }

    private void SetHidden(bool value)
    {
        if (hidden == value) return;
        hidden = value;
        Cursor.visible = value;
        foreach (Renderer r in renderers)
            if (r != null) r.enabled = !value;
    }
}
