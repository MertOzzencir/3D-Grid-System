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

    private Camera cam;
    private Renderer[] renderers;
    private Vector3 palmLocalPosition;       // palmContact'ın kök objeye göre yeri
    private Quaternion palmLocalRotation;    // ve yönü
    private bool placedOnce;
    private bool hidden;

    // Parmakların ve durum sisteminin kullanacağı son yüzey bilgisi
    public Transform PalmContact => palmContact;
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
        Cursor.visible = true;
        SetHidden(false);
    }

    // Kamera ve taşınan objeler Update'te hareket ediyor; eldiven onlardan sonra yerleşsin
    private void LateUpdate()
    {
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        SetHidden(overUI);
        if (overUI) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        FindSurface(ray);

        // Hedef avuç pozu: avuç yüzeye bakar (Y = -normal), parmaklar ekranın yukarısına (Z)
        Vector3 fingerDirection = FingerDirectionOn(SurfaceNormal);
        Quaternion palmRotation = Quaternion.LookRotation(fingerDirection, -SurfaceNormal)
                                  * Quaternion.Euler(fingerLift, 0f, 0f); // +X etrafında: parmak uçları yüzeyden kalkar
        Vector3 palmPosition = SurfacePoint + SurfaceNormal * surfaceOffset;

        // Avuç bu poza gelsin diye kök objenin olması gereken pozu
        Quaternion rootRotation = palmRotation * Quaternion.Inverse(palmLocalRotation);
        Vector3 rootPosition = palmPosition - rootRotation * Vector3.Scale(palmLocalPosition, transform.lossyScale);

        if (!placedOnce)
        {
            transform.SetPositionAndRotation(rootPosition, rootRotation);
            placedOnce = true;
            return;
        }

        float moveT = 1f - Mathf.Exp(-moveSharpness * Time.deltaTime);   // kare hızından bağımsız yumuşatma
        float rotateT = 1f - Mathf.Exp(-rotateSharpness * Time.deltaTime);
        transform.SetPositionAndRotation(
            Vector3.Lerp(transform.position, rootPosition, moveT),
            Quaternion.Slerp(transform.rotation, rootRotation, rotateT));
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
