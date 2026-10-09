using UnityEngine;

// Orak: hasat aleti (DESIGN.md: hasat yolu bulmacası). Balta gibi sağ tık basılı tutulur. Botun tarlasının üstünde
// hücrenin üstünde süzülür (FarmGrid IToolTarget); sol tık basılıyken geçilen olgun kareler hasat edilir, yol FarmGrid'de
// sayılır (kombo). Sol tık bırakılınca ya da tarladan çıkınca yol biter.
// Animasyon (prosedürel, VisualTransform'u tutma noktasının etrafında döndürür; el sapta kalır):
// - Sol tık basılıyken "biçmeye hazır" duruş: bıçak öne eğilir, bırakınca yaylanarak geri gelir.
// - Giderken gidiş yönüne yatar, hareketin biraz gerisinden gelir.
// - Her hasat edilen karede sapın ekseni etrafında kısa bir biçme vuruşu (sırayla sağa / sola); kombo büyüdükçe canlanır.
// - Full Harvest'ta sapın etrafında tam tur.
// Eksenler modelin uzayında (VisualTransform): sap Y boyunca; bıçak düzlemine dik eksen Z. Ters görünürse açının işaretini çevir.
public class Sickle : ToolBase
{
    [Header("Animasyon")]
    [Tooltip("Basılıyken bıçağın öne eğilme açısı (derece). Ters yöne eğiliyorsa eksi yap.")]
    [SerializeField] private float readyAngle = 40f;
    [Tooltip("Hazır duruşun eğilme ekseni (modelin uzayında)")]
    [SerializeField] private Vector3 readyAxis = Vector3.forward;
    [Tooltip("Sapın ekseni (modelin uzayında): biçme vuruşu ve tam tur bunun etrafında")]
    [SerializeField] private Vector3 handleAxis = Vector3.up;
    [Tooltip("Biçme vuruşunun açısı ve süresi")]
    [SerializeField] private float slashAngle = 55f;
    [SerializeField] private float slashDuration = 0.16f;
    [Tooltip("Giderken yatma: hız (birim/sn) başına açı ve en fazla açı")]
    [SerializeField] private float leanPerSpeed = 4f;
    [SerializeField] private float maxLean = 18f;
    [SerializeField] private float spinDuration = 0.55f;

    private const float ReadyStiffness = 220f, ReadyDamping = 20f;
    private const float LeanStiffness = 70f, LeanDamping = 11f;

    private bool leftHeld;
    private float lastHeldTime = float.MinValue;
    private Vector3 restLocalPosition;
    private Quaternion restLocalRotation;
    private float ready, readyVelocity;
    private Vector2 lean, leanVelocity;   // dünya XZ'de yatma (derece, gidiş yönünde)
    private Vector3 lastPosition;
    private float slashStart = -1f, slashSign = 1f, slashScale = 1f;
    private float spinStart = -1f;
    private bool animating;

    private bool Held => Time.time - lastHeldTime < 0.1f;

    protected override void Awake()
    {
        base.Awake();
        if (VisualTransform != null)
        {
            restLocalPosition = VisualTransform.localPosition;
            restLocalRotation = VisualTransform.localRotation;
        }
        lastPosition = transform.position;
    }

    private void OnEnable() => InputManager.OnMouseLeft += OnMouseLeft;
    private void OnDisable() => InputManager.OnMouseLeft -= OnMouseLeft;

    private void OnMouseLeft(bool pressed)
    {
        leftHeld = pressed;
        if (!pressed) FarmGrid.EndActiveHarvest(); // yol bitti
    }

    protected override bool OnUseWithoutTarget() => false;

    // Sol tık: baltadaki sallanma yerine hemen ilk kare (yol başlangıcı)
    public override void Interact(out bool finished)
    {
        finished = false;
        Drag.TryUseOnTarget(out _);
    }

    public override void InteractContract(out bool success)
    {
        base.InteractContract(out success);
        lastHeldTime = Time.time;
        if (leftHeld) Drag.TryUseOnTarget(out _); // basılı sürükleme: geçilen kareler
    }

    public override void ContractCancel()
    {
        FarmGrid.EndActiveHarvest();
        base.ContractCancel();
    }

    // FarmGrid bir kareyi hasat etti: biçme vuruşu (yön sırayla değişir, kombo büyüdükçe biraz daha geniş ve hızlı)
    public void OnHarvested(int combo)
    {
        slashStart = Time.time;
        slashSign = -slashSign;
        slashScale = 1f + Mathf.Min(combo, 10) * 0.04f;
        animating = true;
    }

    // Bütün olgun ekinler tek yolda: kutlama turu
    public void OnFullHarvest()
    {
        spinStart = Time.time;
        animating = true;
    }

    private void LateUpdate()
    {
        if (VisualTransform == null) return;
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        LockGripSelection = Held; // tutarken seçilen yön bırakana kadar sabit (dönerken el sapta zıplamasın)

        // Hazır duruş: tutarken ve sol tık basılıyken
        float readyTarget = Held && leftHeld ? 1f : 0f;
        readyVelocity += (ReadyStiffness * (readyTarget - ready) - ReadyDamping * readyVelocity) * dt;
        ready += readyVelocity * dt;

        // Yatma: kökün hızı (GridDragMotor sürer); hareketin gerisinden gelsin diye yaylı
        Vector3 velocity = (transform.position - lastPosition) / Mathf.Max(dt, 0.0001f);
        lastPosition = transform.position;
        Vector2 flat = Held ? new Vector2(velocity.x, velocity.z) * leanPerSpeed : Vector2.zero;
        flat = Vector2.ClampMagnitude(flat, maxLean);
        leanVelocity += (LeanStiffness * (flat - lean) - LeanDamping * leanVelocity) * dt;
        lean += leanVelocity * dt;

        float slash = 0f;
        if (slashStart >= 0f)
        {
            float t = (Time.time - slashStart) / Mathf.Max(slashDuration / slashScale, 0.0001f);
            if (t >= 1f) slashStart = -1f;
            // Hızla çıkar, hafif taşarak geri gelir
            else slash = slashSign * slashAngle * slashScale * Mathf.Sin(t * Mathf.PI) * (1f - 0.35f * t);
        }
        float spin = 0f;
        if (spinStart >= 0f)
        {
            float t = (Time.time - spinStart) / Mathf.Max(spinDuration, 0.0001f);
            if (t >= 1f) spinStart = -1f;
            else spin = 360f * Mathf.SmoothStep(0f, 1f, t);
        }

        bool active = Mathf.Abs(ready) > 0.001f || Mathf.Abs(readyVelocity) > 0.001f || lean.sqrMagnitude > 0.0001f ||
                      leanVelocity.sqrMagnitude > 0.0001f || slashStart >= 0f || spinStart >= 0f;
        if (!active)
        {
            if (animating)
            {
                VisualTransform.SetLocalPositionAndRotation(restLocalPosition, restLocalRotation);
                animating = false;
            }
            return;
        }
        animating = true;

        // Yatma: dünya ekseni (yukarı × gidiş yönü) → parent'ın uzayı
        Quaternion leanRotation = Quaternion.identity;
        if (lean.sqrMagnitude > 0.0001f)
        {
            Vector3 axis = Vector3.Cross(Vector3.up, new Vector3(lean.x, 0f, lean.y).normalized);
            Transform parent = VisualTransform.parent;
            if (parent != null) axis = parent.InverseTransformDirection(axis);
            leanRotation = Quaternion.AngleAxis(lean.magnitude, axis);
        }
        Quaternion rotation = leanRotation * restLocalRotation *
                              Quaternion.AngleAxis(readyAngle * ready, readyAxis) *
                              Quaternion.AngleAxis(slash + spin, handleAxis);

        // Tutma noktasının etrafında dön: el sapta kalsın
        Vector3 pivot = GripPoint != null && GripPoint.parent == VisualTransform ? GripPoint.localPosition : Vector3.zero;
        Vector3 fixedPoint = restLocalPosition + restLocalRotation * Vector3.Scale(pivot, VisualTransform.localScale);
        Vector3 position = fixedPoint - rotation * Vector3.Scale(pivot, VisualTransform.localScale);
        VisualTransform.SetLocalPositionAndRotation(position, rotation);
    }
}
