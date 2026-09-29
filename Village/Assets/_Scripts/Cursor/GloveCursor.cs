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
    private Transform originalParent;

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

    // SurfacePoint / SurfaceNormal'a göre kök objenin olması gereken pozu:
    // avuç yüzeye bakar (Y = -normal), parmaklar ekranın yukarısına (Z), parmak uçları fingerLift kadar kalkık
    private void TargetRootPose(out Vector3 rootPosition, out Quaternion rootRotation)
    {
        Vector3 fingerDirection = FingerDirectionOn(SurfaceNormal);
        Quaternion palmRotation = Quaternion.LookRotation(fingerDirection, -SurfaceNormal)
                                  * Quaternion.Euler(fingerLift, 0f, 0f); // +X etrafında: parmak uçları yüzeyden kalkar
        Vector3 palmPosition = SurfacePoint + SurfaceNormal * surfaceOffset;

        rootRotation = palmRotation * Quaternion.Inverse(palmLocalRotation);
        rootPosition = palmPosition - rootRotation * Vector3.Scale(palmLocalPosition, transform.lossyScale);
    }

    // Elde tutulan obje (yok edildiyse Unity null'u → null)
    private static Transform HeldTransform()
    {
        InteractableController controller = InteractableController.Instance;
        return controller != null && controller.Held is Component component && component != null ? component.transform : null;
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
        Transform point = grip.GripPoint;

        // Hedef kök poz dünyada: avuç (palmContact) tam tutma noktasında ve onunla aynı yönde
        Quaternion rootRotation = point.rotation * Quaternion.Inverse(palmLocalRotation);
        Vector3 rootPosition = point.position - rootRotation * Vector3.Scale(palmLocalPosition, transform.lossyScale);

        originalParent = transform.parent;
        transform.SetParent(point, true);

        attachLocalPosition = point.InverseTransformPoint(rootPosition);
        attachLocalRotation = Quaternion.Inverse(point.rotation) * rootRotation;
        attachStartPosition = transform.localPosition;
        attachStartRotation = transform.localRotation;
        attachProgress = 0f;
    }

    // Child olduğu için aletin her hareketi otomatik; burada sadece ilk oturma kayması yapılır
    private void FollowGrip()
    {
        Transform point = activeGrip.GripPoint;
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
