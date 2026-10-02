using System;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    public static event Action<CameraFacing> OnCameraRotation;

    // Kameranın şu an baktığı yön (90° adımlar). Ağaç okları buna göre, ihtiyaç anında konumlanır.
    public static CameraFacing CurrentFacing { get; private set; } = CameraFacing.Deg0;

    private static readonly CameraFacing[] FacingSequence =
    {
        CameraFacing.Deg0, CameraFacing.DegNeg90, CameraFacing.DegNeg180, CameraFacing.Deg90
    };
    private int facingIndex = 0;

    [Header("Movement")]
    [SerializeField] private float speed;
    [SerializeField] private float acceleration;

    [Tooltip("Takip edilen (bot) ekranın ortasına bu hızla oturur")]
    [SerializeField] private float followSharpness = 6f;

    [Header("Rotation")]
    [SerializeField] private float rotationAngle = 90f;
    [SerializeField] private float rotationSpeed = 200f;
    [SerializeField] private float fallbackOrbitDistance = 10f;
    [Tooltip("Dönüş merkezi: ekranın ortasında görünen collider. Hangi layer'lar sayılsın")]
    [SerializeField] private LayerMask pivotMask = ~0;
    [Tooltip("Ekranın ortasında collider yoksa (su, boşluk) dönüş merkezinin alındığı yatay düzlemin yüksekliği")]
    [SerializeField] private float pivotFallbackHeight = 0f;
    [SerializeField] private float maxPivotDistance = 200f;

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 20f;
    [SerializeField] private float zoomDamping = 10f;
    [SerializeField] private float minHeight = 3f;
    [SerializeField] private float maxHeight = 20f;
    [SerializeField] private float minPitch = 35f;
    [SerializeField] private float maxPitch = 55f;

    // Takip edilen (örn. binilen bot): WASD kamerayı kaydırmaz. Kamera onunla birlikte kayar ve onu ekranın ortasına
    // yumuşakça oturtur (zoom bakış yönünde olduğu için ortadaki şey ortada kalır); Q/E onun etrafında döner.
    private static Transform followTarget;
    private static Vector3 lastFollowPosition;

    public static void Follow(Transform target)
    {
        followTarget = target;
        if (target != null) lastFollowPosition = target.position;
    }

    private Vector3 currentVelocity;
    private bool isRotating;
    private float currentYaw;
    private float targetYaw;
    private float pitch;
    private Vector3 pivotPoint;
    private float currentOrbitDistance;
    private float lockedY;
    private float zoomVelocity;

    void Awake()
    {
        CurrentFacing = FacingSequence[facingIndex]; // static: önceki Play oturumundan kalmasın
        followTarget = null;
        InputManager.OnE += RotateRight;
        InputManager.OnQ += RotateLeft;
        currentYaw = transform.eulerAngles.y;

        float t = Mathf.InverseLerp(minHeight, maxHeight, transform.position.y);
        pitch = Mathf.Lerp(minPitch, maxPitch, t);
        transform.rotation = Quaternion.Euler(pitch, currentYaw, 0);
    }

    void OnDestroy()
    {
        InputManager.OnE -= RotateRight;
        InputManager.OnQ -= RotateLeft;
    }

    void LateUpdate()
    {
        HandleMovement();
        HandleRotation();
        HandleZoom();
    }

    private void HandleMovement()
    {
        if (followTarget != null)
        {
            Vector3 target = followTarget.position;
            Vector3 delta = target - lastFollowPosition;
            delta.y = 0f;
            lastFollowPosition = target;
            transform.position += delta;
            pivotPoint += delta; // dönerken de takip etsin (dönüş pozisyonu pivot'tan hesaplanıyor)
            currentVelocity = Vector3.zero;
            if (isRotating) return;

            // Ekranın ortası: bakış ışınının hedefin yüksekliğindeki yatay düzlemi kestiği nokta. Hedefe doğru kayar.
            Vector3 view = transform.forward;
            if (view.y > -0.01f) return;
            Vector3 center = transform.position + view * ((target.y - transform.position.y) / view.y);
            Vector3 offset = target - center;
            offset.y = 0f;
            transform.position += offset * (1f - Mathf.Exp(-followSharpness * Time.deltaTime));
            return;
        }

        Vector2 movement = InputManager.MovementVectorNormalized();

        Vector3 forward = Quaternion.Euler(0, currentYaw, 0) * Vector3.forward;
        Vector3 right = Quaternion.Euler(0, currentYaw, 0) * Vector3.right;

        Vector3 targetDirection = forward * movement.y + right * movement.x;
        Vector3 targetVelocity = targetDirection.normalized * speed;

        currentVelocity = Vector3.MoveTowards(currentVelocity, targetVelocity, acceleration * Time.deltaTime);
        transform.position += currentVelocity * Time.deltaTime;
    }

    // E sağa, Q sola 90°. Elde obje varken de çalışır (tutulan obje ayrıca R ile döner).
    private void RotateRight() => StartRotation(1);
    private void RotateLeft() => StartRotation(-1);

    private void StartRotation(int direction)
    {
        if (isRotating) return;

        lockedY = transform.position.y;

        // Dönüş merkezi ekranın ortasında gerçekten görünen nokta: kamera aynı yükseklik ve yatay uzaklıkta onun
        // etrafında döndüğü için o nokta her yönde ekranın ortasında kalır. (Eskiden y = 0 düzlemi alınıyordu;
        // karoların üstündeki objeler için pivot objenin arkasına düşüyor, obje dönüşte ortadan kayıyordu.)
        Vector3 fullForward = Quaternion.Euler(pitch, currentYaw, 0) * Vector3.forward;
        Ray ray = new Ray(transform.position, fullForward);
        Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, pivotFallbackHeight, 0f));

        if (followTarget != null)
        {
            pivotPoint = followTarget.position; // takip edilen ortada: onun etrafında dön
        }
        else if (Physics.Raycast(ray, out RaycastHit hit, maxPivotDistance, pivotMask, QueryTriggerInteraction.Ignore))
        {
            pivotPoint = hit.point;
        }
        else if (groundPlane.Raycast(ray, out float enter))
        {
            pivotPoint = ray.GetPoint(enter);
        }
        else
        {
            Vector3 flatForward = Quaternion.Euler(0, currentYaw, 0) * Vector3.forward;
            pivotPoint = transform.position + flatForward * fallbackOrbitDistance;
        }

        Vector3 flatCamPos = new Vector3(transform.position.x, 0, transform.position.z);
        Vector3 flatPivot = new Vector3(pivotPoint.x, 0, pivotPoint.z);
        currentOrbitDistance = Vector3.Distance(flatCamPos, flatPivot);

        targetYaw = currentYaw - rotationAngle * direction;
        isRotating = true;

        facingIndex = (facingIndex + direction + FacingSequence.Length) % FacingSequence.Length;
        CurrentFacing = FacingSequence[facingIndex];
        OnCameraRotation?.Invoke(CurrentFacing);
    }

    private void HandleRotation()
    {
        if (!isRotating) return;

        currentYaw = Mathf.MoveTowardsAngle(currentYaw, targetYaw, rotationSpeed * Time.deltaTime);

        Vector3 facing = Quaternion.Euler(0, currentYaw, 0) * Vector3.forward;
        Vector3 newPos = pivotPoint - facing * currentOrbitDistance;
        newPos.y = lockedY;

        transform.position = newPos;
        transform.rotation = Quaternion.Euler(pitch, currentYaw, 0);

        if (Mathf.Abs(Mathf.DeltaAngle(currentYaw, targetYaw)) < 0.05f)
        {
            currentYaw = targetYaw;
            isRotating = false;
        }
    }

    private void HandleZoom()
    {
        if (isRotating) return;

        float scroll = InputManager.ScrollDelta();
        if (Mathf.Abs(scroll) > 0.001f)
            zoomVelocity = scroll * zoomSpeed;

        zoomVelocity = Mathf.MoveTowards(zoomVelocity, 0f, zoomDamping * Time.deltaTime);

        Vector3 forward = Quaternion.Euler(pitch, currentYaw, 0) * Vector3.forward;
        Vector3 newPos = transform.position + forward * zoomVelocity * Time.deltaTime;

        if (newPos.y >= minHeight && newPos.y <= maxHeight)
        {
            transform.position = newPos;

            float t = Mathf.InverseLerp(minHeight, maxHeight, newPos.y);
            pitch = Mathf.Lerp(minPitch, maxPitch, t);
            transform.rotation = Quaternion.Euler(pitch, currentYaw, 0);
        }
        else
        {
            zoomVelocity = 0f;
        }
    }
}
public enum CameraFacing
{
    Deg0 = 0,
    DegNeg90 = -90,
    DegNeg180 = -180,
    Deg90 = 90
}