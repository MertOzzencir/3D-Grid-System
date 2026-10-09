using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 3D eldiven imleç. Mouse'tan dünyaya avuç büyüklüğünde bir ışın demeti atılır (ortada mouse ışını, etrafında iki halka,
// hepsi kamera yönüne paralel). Çarpmalar collider'a göre gruplanır; avucun en çok kapladığı collider kazanır ve eldiven
// onun çarpmalarının ortalama düzlemine, mouse ışınının o düzlemi kestiği yere oturur (kutunun köşesinde üst ve yan yüzün
// ortalaması: el kenarda yuvarlanır; kedi gibi yuvarlak collider'larda da düzgün). Kazanan, ancak diğerini belirgin şekilde
// geçince değişir: tek ışın köşede üst yüz ile yan duvar arasında gidip geliyordu. Etkileşim de aynı demetten: avucun en
// çok kapladığı etkileşimli collider (IInteractable / IClickable / IHoverable) TargetCollider olur; InteractableController
// tutma, tıklama ve hover'ı buna gönderir (el neyin üstündeyse onunla etkileşilir). Eldiven avucu yüzeye bakacak şekilde
// yerleşir. Normal etrafındaki dönüş kameraya göre sabittir: parmaklar hep ekranın
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

    [Header("Avuç (yüzey ve etkileşim)")]
    [Tooltip("Işın demetinin yarıçapı (birim): eldivenin avucu kadar. Büyüdükçe köşelerde daha kararlı, ama küçük " +
             "objelerin kenarına gelince de onlar seçilir.")]
    [SerializeField] private float palmRadius = 0.22f;
    [Tooltip("Şu an seçili yüzey / hedef, yenisi bu kat daha fazla kaplanmadıkça değişmez (köşede gidip gelmesin)")]
    [SerializeField] private float switchBias = 1.4f;

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

    [Header("Su (kıyı)")]
    [Tooltip("Mouse suya (ya da boşluğa) gidince el en yakın kıyıda durur: kara en fazla bu kadar hücre uzakta aranır")]
    [SerializeField] private int shoreSearchRadius = 6;
    [Tooltip("Kıyıda el karonun kenarından bu kadar içeride durur (birim)")]
    [SerializeField] private float shoreInset = 0.2f;

    [Header("Bot")]
    [Tooltip("Bota binerken koltuğa zıplama süresi (saniye) ve yüksekliği")]
    [SerializeField] private float rideHopDuration = 0.3f;
    [SerializeField] private float rideHopHeight = 0.35f;

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

    // Bota binmiş: eldiven koltukta, mouse'un gösterdiği yeri bot okur (MouseOverLand / MouseLandPoint / MouseWaterPoint)
    private Transform rideSeat;
    private float rideProgress;
    private Vector3 rideStartPosition;
    private Quaternion rideStartRotation;
    public bool IsRiding => rideSeat != null;
    private float currentHopDuration = 0.3f;
    public bool MouseOverLand { get; private set; }
    public Vector3 MouseLandPoint { get; private set; }
    public Vector3 MouseWaterPoint { get; private set; }
    // Mouse suda (ya da botun üstünde), el en yakın kıyıda bekliyor
    public bool IsAtShore { get; private set; }
    // Mouse'un altındaki geçirgen obje (örn. bot): eldiven ona basmaz ama bot "üstüne gelindi" diye biner
    public IGlovePassThrough HoveredPassThrough { get; private set; }
    public Collider HoveredPassThroughCollider { get; private set; }

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

    // Elin altındaki etkileşimli collider (avucun en çok kapladığı) ve oradaki en iyi çarpma; yoksa null
    public Collider TargetCollider { get; private set; }
    public RaycastHit TargetHit { get; private set; }
    public static GloveCursor Instance { get; private set; }

    // Avuç demeti: ortada mouse ışını (ağırlık 3), iç halka 6 ışın (1.5), dış halka 10 ışın (1)
    private static readonly float[] PalmWeights = new float[17];
    private static readonly Vector2[] PalmOffsets = BuildPalmOffsets(PalmWeights);
    private readonly RaycastHit[] palmHits = new RaycastHit[17];
    private readonly float[] palmHitWeights = new float[17];
    private readonly RaycastHit[] rayBuffer = new RaycastHit[32];
    // Collider başına grup hesapları için
    private readonly Collider[] groupCollider = new Collider[17];
    private readonly Vector3[] groupNormalSum = new Vector3[17], groupPointSum = new Vector3[17];
    private readonly float[] groupWeight = new float[17];
    private readonly int[] groupBest = new int[17];

    private static Vector2[] BuildPalmOffsets(float[] weights)
    {
        var offsets = new Vector2[17];
        offsets[0] = Vector2.zero;
        weights[0] = 3f;
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI * 2f / 6f;
            offsets[1 + i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.5f;
            weights[1 + i] = 1.5f;
        }
        for (int i = 0; i < 10; i++)
        {
            float a = (i + 0.5f) * Mathf.PI * 2f / 10f;
            offsets[7 + i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            weights[7 + i] = 1f;
        }
        return offsets;
    }

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

    private void OnEnable()
    {
        Cursor.visible = false;
        Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
        TargetCollider = null;
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
        HoveredPassThrough = null; // sadece yüzey takibinde (FindSurface) dolar: tutarken / binmişken eski değer kalmasın
        HoveredPassThroughCollider = null;
        Collider previousTarget = TargetCollider;
        TargetCollider = null;     // aynı şekilde: sadece yüzey takibinde hedef olur (tutarken, binmişken, UI'da yok)

        if (rideSeat != null)
        {
            SetHidden(false);
            FollowRide(ray);
            return;
        }

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

        FindSurface(ray, previousTarget);
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
        OnBase = SurfaceCollider != null && (SurfaceCollider.GetComponentInParent<GridBase>() != null || IsWalkable(SurfaceCollider));

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

    // Bota bin: eldiven kısa bir zıplamayla koltuğa oturur (avuç aşağı, parmaklar botun ön tarafına), bot onu taşır
    public void BeginRide(Transform seat)
    {
        rideSeat = seat;
        rideProgress = 0f;
        // Uzaktan binince zıplama biraz daha uzun (ışınlanmış gibi durmasın)
        currentHopDuration = Mathf.Clamp(rideHopDuration + Vector3.Distance(transform.position, seat.position) * 0.04f,
                                         rideHopDuration, rideHopDuration * 3f);
        HoveredPassThrough = null;
        rideStartPosition = transform.position;
        rideStartRotation = transform.rotation;
        OnBase = false;
        WalkWeight = 0f;
        IsAtShore = false;
        hasLastSurfacePoint = false;
    }

    // İn: normal takip kaldığı yerden yumuşakça mouse'a döner
    public void EndRide()
    {
        rideSeat = null;
        hasLastSurfacePoint = false;
    }

    // Botten bir noktaya in: mouse imleci o noktanın ekrandaki yerine taşınır, eldiven normal takiple oraya zıplar
    public void EndRideAt(Vector3 worldPoint)
    {
        EndRide();
        Vector3 screen = cam.WorldToScreenPoint(worldPoint);
        if (screen.z <= 0f || Mouse.current == null) return;
        Mouse.current.WarpCursorPosition(new Vector2(Mathf.Clamp(screen.x, 0f, Screen.width - 1),
                                                     Mathf.Clamp(screen.y, 0f, Screen.height - 1)));
    }

    private void FollowRide(Ray ray)
    {
        // Mouse'un gösterdiği yer: kara (bir collider) ya da su
        if (TryRaycastSolid(ray, out RaycastHit hit))
        {
            MouseOverLand = true;
            MouseLandPoint = hit.point;
        }
        else
        {
            MouseOverLand = false;
            MouseWaterPoint = WaterPoint(ray);
        }

        Vector3 up = rideSeat.up;
        HasSurface = true;
        SurfaceCollider = null;
        SurfacePoint = rideSeat.position;
        SurfaceNormal = up;

        Quaternion palmRotation = Quaternion.LookRotation(rideSeat.forward, -up);
        Vector3 palmPosition = rideSeat.position + up * surfaceOffset;
        Quaternion rootRotation = palmRotation * Quaternion.Inverse(palmLocalRotation);
        Vector3 rootPosition = palmPosition - rootRotation * Vector3.Scale(palmLocalPosition, transform.lossyScale);

        // Binerken yay çizerek koltuğa, sonra katı bağlı (bot sallandıkça eldiven de sallanır)
        rideProgress = Mathf.MoveTowards(rideProgress, 1f, Time.deltaTime / Mathf.Max(currentHopDuration, 0.0001f));
        float t = Mathf.SmoothStep(0f, 1f, rideProgress);
        Vector3 position = Vector3.Lerp(rideStartPosition, rootPosition, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * rideHopHeight);
        transform.SetPositionAndRotation(position, Quaternion.Slerp(rideStartRotation, rootRotation, t));
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

    private void FindSurface(Ray ray, Collider previousTarget)
    {
        IsAtShore = false;
        bool solid = TryRaycastSolid(ray, out RaycastHit hit, out IGlovePassThrough passThrough, out Collider passCollider);
        HoveredPassThrough = passThrough;
        HoveredPassThroughCollider = passCollider;
        if (solid)
        {
            SamplePalm(ray, hit, previousTarget);
            return;
        }

        // Boşluk / su (suyun collider'ı yok): mouse'un su seviyesindeki noktasına en yakın kıyıda durur
        Vector3 waterPoint = WaterPoint(ray);
        MouseWaterPoint = waterPoint;
        if (TryFindShore(waterPoint, out RaycastHit shore))
        {
            IsAtShore = true;
            HasSurface = true;
            SurfacePoint = shore.point;
            SurfaceNormal = shore.normal;
            SurfaceCollider = shore.collider;
            return;
        }
        if (placedOnce && HasSurface) return; // yakında kara yok: el son yerinde bekler

        HasSurface = false;
        SurfaceCollider = null;
        SurfaceNormal = Vector3.up;
        SurfacePoint = waterPoint;
    }

    // Avuç demeti: her ışının ilk katı çarpması toplanır, düzlemlere göre gruplanır, en çok kaplanan (ağırlığı en büyük)
    // yüzey kazanır. Önceki karenin yüzeyi ve hedefi switchBias kadar avantajlı.
    private void SamplePalm(Ray ray, RaycastHit center, Collider previousTarget)
    {
        Vector3 forward = ray.direction;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        right = right.sqrMagnitude > 0.0001f ? right.normalized : cam.transform.right;
        Vector3 up = Vector3.Cross(forward, right);
        float radius = palmRadius;

        int count = 0;
        for (int i = 0; i < PalmOffsets.Length; i++)
        {
            RaycastHit h;
            if (i == 0) h = center;
            else
            {
                Vector2 o = PalmOffsets[i] * radius;
                if (!TryNearestSolid(new Ray(ray.origin + right * o.x + up * o.y, forward), out h)) continue;
            }
            palmHits[count] = h;
            palmHitWeights[count] = PalmWeights[i];
            count++;
        }

        // Yüzey: avucun en çok kapladığı collider (önceki karedeki switchBias kadar avantajlı)
        Collider previousSurface = SurfaceCollider;
        int groups = GroupByCollider(count, false);
        int best = PickGroup(groups, previousSurface);

        Vector3 normal = groupNormalSum[best].sqrMagnitude > 0.0001f ? groupNormalSum[best].normalized : center.normal;
        Vector3 average = groupPointSum[best] / Mathf.Max(groupWeight[best], 0.0001f);
        // Eldiven mouse'un altında kalsın: mouse ışınının ortalama düzlemi kestiği nokta (çok yatık ya da uzaksa ortalama)
        Vector3 point = average;
        float denom = Vector3.Dot(forward, normal);
        if (Mathf.Abs(denom) > 0.15f)
        {
            Vector3 onPlane = ray.origin + forward * (Vector3.Dot(average - ray.origin, normal) / denom);
            if ((onPlane - average).sqrMagnitude < radius * radius * 4f) point = onPlane;
        }

        HasSurface = true;
        SurfacePoint = point;
        SurfaceNormal = normal;
        SurfaceCollider = groupCollider[best];

        // Hedef: avucun en çok kapladığı etkileşimli collider
        groups = GroupByCollider(count, true);
        if (groups > 0)
        {
            int chosen = PickGroup(groups, previousTarget);
            TargetCollider = groupCollider[chosen];
            TargetHit = palmHits[groupBest[chosen]];
        }
        for (int g = 0; g < groups; g++) groupCollider[g] = null;
    }

    // Çarpmaları collider'a göre topla (ağırlık, ağırlıklı normal / nokta toplamı, en ağır çarpma). targetsOnly: sadece
    // etkileşimli collider'lar
    private int GroupByCollider(int count, bool targetsOnly)
    {
        int groups = 0;
        for (int i = 0; i < count; i++)
        {
            Collider c = palmHits[i].collider;
            if (targetsOnly && !IsTargetable(c)) continue;
            int g = 0;
            for (; g < groups; g++) if (groupCollider[g] == c) break;
            if (g == groups)
            {
                groupCollider[g] = c;
                groupNormalSum[g] = Vector3.zero;
                groupPointSum[g] = Vector3.zero;
                groupWeight[g] = 0f;
                groupBest[g] = i;
                groups++;
            }
            float w = palmHitWeights[i];
            groupNormalSum[g] += palmHits[i].normal * w;
            groupPointSum[g] += palmHits[i].point * w;
            groupWeight[g] += w;
            if (w > palmHitWeights[groupBest[g]]) groupBest[g] = i;
        }
        return groups;
    }

    // En ağır grup; önceki seçim switchBias kadar avantajlı
    private int PickGroup(int groups, Collider previous)
    {
        int best = 0;
        float bestScore = -1f;
        for (int g = 0; g < groups; g++)
        {
            float score = groupWeight[g] * (groupCollider[g] == previous ? switchBias : 1f);
            if (score > bestScore)
            {
                bestScore = score;
                best = g;
            }
        }
        return best;
    }

    private static bool IsTargetable(Collider collider) =>
        collider.TryGetComponent(out IInteractable _) || collider.TryGetComponent(out IClickable _) ||
        collider.TryGetComponent(out IHoverable _);

    // Işının ilk katı çarpması (geçirgenler atlanır, yürünebilirler katı); bellek ayırmaz
    private bool TryNearestSolid(Ray ray, out RaycastHit result)
    {
        result = default;
        float nearest = float.MaxValue;
        int hits = Physics.RaycastNonAlloc(ray, rayBuffer, maxDistance, surfaceMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits; i++)
        {
            RaycastHit hit = rayBuffer[i];
            if (hit.distance >= nearest) continue;
            if (!IsWalkable(hit.collider) && hit.collider.GetComponentInParent<IGlovePassThrough>() != null) continue;
            nearest = hit.distance;
            result = hit;
        }
        return nearest < float.MaxValue;
    }

    private Vector3 WaterPoint(Ray ray)
    {
        var plane = new Plane(Vector3.up, new Vector3(0f, fallbackHeight, 0f));
        return plane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : ray.GetPoint(20f);
    }

    // Eldivenin basabileceği ilk collider: IGlovePassThrough olanlar (örn. bot) atlanır, onların üstü su sayılır
    private bool TryRaycastSolid(Ray ray, out RaycastHit result) => TryRaycastSolid(ray, out result, out _, out _);

    // passThrough: mouse'un altındaki (zeminden önce) ilk geçirgen obje (örn. bot), yoksa null
    private bool TryRaycastSolid(Ray ray, out RaycastHit result, out IGlovePassThrough passThrough, out Collider passThroughCollider)
    {
        passThrough = null;
        passThroughCollider = null;
        RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance, surfaceMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            // Yürünebilir (örn. botun tarlası) geçirgen bir objenin child'ı olsa da zemin sayılır
            IGlovePassThrough through = IsWalkable(hit.collider) ? null : hit.collider.GetComponentInParent<IGlovePassThrough>();
            if (through != null)
            {
                if (passThrough == null)
                {
                    passThrough = through;
                    passThroughCollider = hit.collider;
                }
                continue;
            }
            result = hit;
            return true;
        }
        result = default;
        return false;
    }

    private static bool IsWalkable(Collider collider) => collider.GetComponentInParent<IGloveWalkable>() != null;

    // Noktaya yataydan en yakın kara sütunu (en üstteki base) bulunur; nokta o karonun içine (kenardan shoreInset
    // içeride) çekilip yukarıdan base'in collider'ına ray atılır. Base'in collider'ı olduğu için el normal base
    // üstündeki gibi durur (yürüme / bekleme).
    private bool TryFindShore(Vector3 point, out RaycastHit result)
    {
        result = default;
        GridManager grid = GridManager.Instance;
        if (grid == null) return false;

        int cx = Mathf.RoundToInt(point.x), cz = Mathf.RoundToInt(point.z);
        float best = float.MaxValue;
        GridData bestCell = null;
        for (int dz = -shoreSearchRadius; dz <= shoreSearchRadius; dz++)
        for (int dx = -shoreSearchRadius; dx <= shoreSearchRadius; dx++)
        {
            if (!grid.TryGetTopBase(cx + dx, cz + dz, out GridData top)) continue;
            // Noktanın karoya (1×1 kare) yatay uzaklığı
            float ox = Mathf.Max(0f, Mathf.Abs(point.x - top.WorldPosition.x) - 0.5f);
            float oz = Mathf.Max(0f, Mathf.Abs(point.z - top.WorldPosition.z) - 0.5f);
            float distance = ox * ox + oz * oz;
            if (distance >= best) continue;
            best = distance;
            bestCell = top;
        }
        if (bestCell == null) return false;

        float half = Mathf.Max(0f, 0.5f - shoreInset);
        Vector3 center = bestCell.WorldPosition;
        Vector3 target = new Vector3(Mathf.Clamp(point.x, center.x - half, center.x + half), center.y + 2f,
                                     Mathf.Clamp(point.z, center.z - half, center.z + half));
        var down = new Ray(target, Vector3.down);
        foreach (Collider collider in bestCell.Base.GetComponentsInChildren<Collider>())
            if (!collider.isTrigger && collider.Raycast(down, out result, 4f)) return true;

        // Collider'a denk gelmediyse karonun düz tepesi
        if (!Physics.Raycast(down, out result, 4f, surfaceMask, QueryTriggerInteraction.Ignore)) return false;
        return result.collider.GetComponentInParent<GridBase>() != null;
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
