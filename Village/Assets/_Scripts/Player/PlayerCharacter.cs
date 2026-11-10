using System.Collections.Generic;
using UnityEngine;

// Oyuncunun karakteri (kullanıcı 2026-11-10: eldiven yerine; şimdilik kapsül). WASD kameraya göre yürütür, kamera onu
// yukarıdan takip eder (CameraController.Follow, Q / E etrafında döner). Objelere sadece yakınından dokunur (`reach`):
// tutma, tıklama, hover ve alet hedefleri menzil dışındaysa yok sayılır (InteractableController, GridDragMotor).
// Tutulan obje karakterin elinde (HoldPoint) durur, bırakınca önündeki hücreye konur. Elindeki objeye çarpmaz.
[RequireComponent(typeof(CharacterController))]
public class PlayerCharacter : MonoBehaviour
{
    public static PlayerCharacter Instance { get; private set; }

    [Header("Hareket")]
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float acceleration = 30f;
    [SerializeField] private float turnSharpness = 14f;
    [SerializeField] private float gravity = 25f;

    [Header("Etkileşim")]
    [Tooltip("Objelere en fazla bu kadar (yatay, birim) uzaktan dokunulur")]
    [SerializeField] private float reach = 2.5f;
    [Tooltip("Tutulan objenin karaktere göre yeri (yerel: ileri, yukarı)")]
    [SerializeField] private Vector3 holdOffset = new Vector3(0f, 1.1f, 0.75f);

    [Header("Görünüm (prototip)")]
    [SerializeField] private Color bodyColor = new Color(0.75f, 0.2f, 0.18f);
    [Tooltip("Gövdenin shader'ı (Village/Lit); referansla tutulur ki build'e girsin")]
    [SerializeField] private Shader bodyShader;

    private CharacterController controller;
    private Vector3 velocity;
    private float verticalSpeed;
    private Component ignoredHeld;
    private readonly List<Collider> ignoredColliders = new List<Collider>();

    public float Reach => reach;
    public Vector3 HoldPoint => transform.TransformPoint(holdOffset);

    private void Awake()
    {
        Instance = this;
        controller = GetComponent<CharacterController>();
        if (transform.childCount == 0) BuildPlaceholder();
    }

    private void Start() => CameraController.Follow(transform);

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Noktanın karaktere yatay uzaklığı menzilin içinde mi
    public bool InReach(Vector3 point) => InReach(point, reach);

    // Farklı bir menzille (örn. aletin kendi menzili: IToolReach)
    public bool InReach(Vector3 point, float distance)
    {
        Vector3 offset = point - transform.position;
        offset.y = 0f;
        return offset.magnitude <= distance;
    }

    // Bırakılan objenin konacağı yer: karakterin önündeki hücrenin (ayak hizasının bir üstü) dünya noktası
    public Vector3 FrontDropPoint => transform.position + transform.forward * 1.1f + Vector3.up * 0.5f;

    private void Update()
    {
        Move();
        IgnoreHeldCollisions();
    }

    private void Move()
    {
        Vector2 input = CameraController.MovementLocked ? Vector2.zero : InputManager.MovementVectorNormalized();
        Transform camera = Camera.main != null ? Camera.main.transform : transform;
        Vector3 forward = Vector3.ProjectOnPlane(camera.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(camera.right, Vector3.up).normalized;
        Vector3 desired = Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f) * moveSpeed;

        velocity = Vector3.MoveTowards(velocity, desired, acceleration * Time.deltaTime);
        verticalSpeed = controller.isGrounded ? -2f : verticalSpeed - gravity * Time.deltaTime;
        controller.Move((velocity + Vector3.up * verticalSpeed) * Time.deltaTime);

        if (desired.sqrMagnitude > 0.01f)
        {
            Quaternion look = Quaternion.LookRotation(desired);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-turnSharpness * Time.deltaTime));
        }
    }

    // Elde tutulan objenin collider'larıyla karakter çarpışmasın (yoksa önündeki obje onu iter / durdurur)
    private void IgnoreHeldCollisions()
    {
        Component held = InteractableController.Instance != null ? InteractableController.Instance.Held as Component : null;
        if (held == ignoredHeld) return;

        foreach (Collider c in ignoredColliders)
            if (c != null) Physics.IgnoreCollision(controller, c, false);
        ignoredColliders.Clear();
        ignoredHeld = held;
        if (held == null) return;

        foreach (Collider c in held.GetComponentsInChildren<Collider>())
        {
            Physics.IgnoreCollision(controller, c, true);
            ignoredColliders.Add(c);
        }
    }

    // Model yokken kapsül + bakış yönünü gösteren vizör
    private void BuildPlaceholder()
    {
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(body.GetComponent<Collider>());
        body.name = "Body";
        body.transform.SetParent(transform, false);
        body.transform.localPosition = new Vector3(0f, controller.height * 0.5f, 0f);
        body.transform.localScale = new Vector3(controller.radius * 2f, controller.height * 0.5f, controller.radius * 2f);
        Paint(body, bodyColor);

        GameObject visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(visor.GetComponent<Collider>());
        visor.name = "Visor";
        visor.transform.SetParent(transform, false);
        visor.transform.localPosition = new Vector3(0f, controller.height * 0.8f, controller.radius * 0.85f);
        visor.transform.localScale = new Vector3(controller.radius * 1.4f, 0.12f, 0.12f);
        Paint(visor, new Color(0.3f, 0.85f, 1f));
    }

    private void Paint(GameObject part, Color color)
    {
        Renderer renderer = part.GetComponent<Renderer>();
        if (bodyShader != null) renderer.material = new Material(bodyShader);
        renderer.material.color = color;
        if (renderer.material.HasProperty("_BaseColor")) renderer.material.SetColor("_BaseColor", color);
    }
}
