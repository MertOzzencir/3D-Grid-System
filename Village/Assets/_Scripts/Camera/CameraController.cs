using System;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    public static event Action<CameraFacing> OnCameraRotation;

    private static readonly CameraFacing[] FacingSequence =
    {
        CameraFacing.Deg0, CameraFacing.DegNeg90, CameraFacing.DegNeg180, CameraFacing.Deg90
    };
    private int facingIndex = 0;

    [Header("Movement")]
    [SerializeField] private float speed;
    [SerializeField] private float acceleration;

    [Header("Rotation")]
    [SerializeField] private float rotationAngle = 90f;
    [SerializeField] private float rotationSpeed = 200f;
    [SerializeField] private float fallbackOrbitDistance = 10f;

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 20f;
    [SerializeField] private float zoomDamping = 10f;
    [SerializeField] private float minHeight = 3f;
    [SerializeField] private float maxHeight = 20f;
    [SerializeField] private float minPitch = 35f;
    [SerializeField] private float maxPitch = 55f;

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
        InputManager.OnR += StartRotation;
        currentYaw = transform.eulerAngles.y;

        float t = Mathf.InverseLerp(minHeight, maxHeight, transform.position.y);
        pitch = Mathf.Lerp(minPitch, maxPitch, t);
        transform.rotation = Quaternion.Euler(pitch, currentYaw, 0);
    }

    void LateUpdate()
    {
        HandleMovement();
        HandleRotation();
        HandleZoom();
    }

    private void HandleMovement()
    {
        Vector2 movement = InputManager.MovementVectorNormalized();

        Vector3 forward = Quaternion.Euler(0, currentYaw, 0) * Vector3.forward;
        Vector3 right = Quaternion.Euler(0, currentYaw, 0) * Vector3.right;

        Vector3 targetDirection = forward * movement.y + right * movement.x;
        Vector3 targetVelocity = targetDirection.normalized * speed;

        currentVelocity = Vector3.MoveTowards(currentVelocity, targetVelocity, acceleration * Time.deltaTime);
        transform.position += currentVelocity * Time.deltaTime;
    }

    private void StartRotation()
    {
        if (isRotating) return;

        lockedY = transform.position.y;

        Vector3 fullForward = Quaternion.Euler(pitch, currentYaw, 0) * Vector3.forward;
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        Ray ray = new Ray(transform.position, fullForward);

        if (groundPlane.Raycast(ray, out float enter))
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

        targetYaw = currentYaw - rotationAngle;
        isRotating = true;

        facingIndex = (facingIndex + 1) % FacingSequence.Length;
        OnCameraRotation?.Invoke(FacingSequence[facingIndex]);
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