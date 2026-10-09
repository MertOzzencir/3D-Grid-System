using UnityEngine;

// Orak: hasat aleti (DESIGN.md: hasat yolu bulmacası). Balta gibi sağ tık basılı tutulur. Botun tarlasının üstünde
// hücrenin üstünde süzülür (FarmGrid IToolTarget); sol tık basılıyken geçilen olgun kareler hasat edilir, yol FarmGrid'de
// sayılır (kombo). Sol tık bırakılınca ya da tarladan çıkınca yol biter.
// Animasyon (prosedürel, VisualTransform'u tutma noktasının etrafında döndürür; el sapta kalır). Eksenler modelin uzayında,
// prefab'taki modele göre kullanıcının ayarı: -X etrafında yatar, Z etrafında sallanır.
// - Tutarken (sol tık basılı değil): gidiş yönüne yatar, hareketin biraz gerisinden gelir.
// - Sol tık basılıyken (hasat): 90° yatar (bıçak yatay) ve harvestLift kadar kalkar (yatınca bıçak tutma noktasının
//   yüksekliğine iniyor, tarla parçalarının içinde kalıyordu); hareket ederken gidişin tersine geri çekilir (bıçak arkada
//   kalır), durunca ileri sekip sıfıra oturur (sallama). Her hasat edilen kare geri çekişe küçük bir itki ekler.
//   Sol tık bırakılınca eski haline döner.
// - Full Harvest'ta sapın etrafında tam tur.
public class Sickle : ToolBase
{
    [Header("Hasat duruşu (sol tık basılı)")]
    [Tooltip("Hasatta orağın yatma açısı ve ekseni (modelin uzayında). Ters yöne yatıyorsa açıyı eksi yap.")]
    [SerializeField] private float layAngle = 90f;
    [SerializeField] private Vector3 layAxis = Vector3.left;
    [Tooltip("Yattıktan sonra geri çekilme / sekme ekseni (modelin uzayında)")]
    [SerializeField] private Vector3 swingAxis = Vector3.forward;
    [Tooltip("Hasatta orağın yukarı kalkması (birim): yatık bıçak ekinlerin ortasından geçsin, tarla parçalarına girmesin")]
    [SerializeField] private float harvestLift = 0.45f;
    [Tooltip("Hız (birim/sn) başına geri çekilme açısı. Orak gidiş yönüne doğru (önde) kalıyorsa eksi yap.")]
    [SerializeField] private float swingPerSpeed = 20f;
    [Tooltip("En fazla geri çekilme açısı")]
    [SerializeField] private float maxSwing = 55f;
    [Tooltip("Sallama yayı: sertlik (büyük = hızlı döner) ve sönüm (küçük = durunca daha çok ileri seker)")]
    [SerializeField] private float swingStiffness = 140f;
    [SerializeField] private float swingDamping = 8f;
    [Tooltip("Her hasat edilen karede geri çekişe eklenen itki (derece/sn)")]
    [SerializeField] private float harvestKick = 120f;

    [Header("Tutarken")]
    [Tooltip("Gidiş yönüne yatma: hız (birim/sn) başına açı ve en fazla açı")]
    [SerializeField] private float leanPerSpeed = 4f;
    [SerializeField] private float maxLean = 18f;

    [Header("Full Harvest")]
    [Tooltip("Sapın ekseni (modelin uzayında): tam tur bunun etrafında")]
    [SerializeField] private Vector3 handleAxis = Vector3.up;
    [SerializeField] private float spinDuration = 0.55f;

    private const float LayStiffness = 300f, LayDamping = 30f;
    private const float LeanStiffness = 70f, LeanDamping = 11f;

    private bool leftHeld;
    private float lastHeldTime = float.MinValue;
    private Vector3 restLocalPosition;
    private Quaternion restLocalRotation;
    private float lay, layVelocity;        // 0 = dik, 1 = yatık (hasat)
    private float swing, swingVelocity;    // derece, swingAxis etrafında
    private Vector2 lean, leanVelocity;    // dünya XZ'de yatma (derece, gidiş yönünde)
    private Vector3 lastPosition;
    private float spinStart = -1f;
    private bool animating;

    private bool Held => Time.time - lastHeldTime < 0.1f;
    private bool Harvesting => Held && leftHeld;

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

    // FarmGrid bir kareyi hasat etti: geri çekişe itki (o anki çekiş yönünde; kombo büyüdükçe biraz daha güçlü)
    public void OnHarvested(int combo)
    {
        if (!Harvesting) return;
        float direction = Mathf.Abs(swing) > 1f ? Mathf.Sign(swing) : -Mathf.Sign(swingPerSpeed);
        swingVelocity += direction * harvestKick * (1f + Mathf.Min(combo, 10) * 0.05f);
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
        Transform parent = VisualTransform.parent;

        // Kökün hızı (GridDragMotor sürer)
        Vector3 velocity = (transform.position - lastPosition) / Mathf.Max(dt, 0.0001f);
        lastPosition = transform.position;
        Vector3 flat = Held ? Vector3.ProjectOnPlane(velocity, Vector3.up) : Vector3.zero;

        // Yatma (hasat)
        layVelocity += (LayStiffness * ((Harvesting ? 1f : 0f) - lay) - LayDamping * layVelocity) * dt;
        lay += layVelocity * dt;
        Quaternion layRotation = Quaternion.AngleAxis(layAngle * lay, layAxis);

        // Geri çekilme: yatık orağın X ekseni etrafında, hızın o eksene dik (bıçağı süpüren) bileşeni kadar, gidişin tersine
        float swingTarget = 0f;
        if (Harvesting && flat.sqrMagnitude > 0.0001f)
        {
            Quaternion world = (parent != null ? parent.rotation : Quaternion.identity) * restLocalRotation * layRotation;
            Vector3 axisWorld = world * swingAxis.normalized;
            // Eksen etrafında + yönde dönünce bıçağın ucunun (sapın ucu, Y) gittiği yön
            Vector3 tipDirection = Vector3.Cross(axisWorld, world * handleAxis.normalized);
            float along = Vector3.Dot(flat, Vector3.ProjectOnPlane(tipDirection, Vector3.up).normalized);
            swingTarget = Mathf.Clamp(-along * swingPerSpeed, -maxSwing, maxSwing);
        }
        swingVelocity += (swingStiffness * (swingTarget - swing) - swingDamping * swingVelocity) * dt;
        swing += swingVelocity * dt;
        swing = Mathf.Clamp(swing, -maxSwing * 1.5f, maxSwing * 1.5f);

        // Yatma (tutarken): hasatta kapanır
        Vector2 leanTarget = new Vector2(flat.x, flat.z) * leanPerSpeed * (1f - Mathf.Clamp01(lay));
        leanTarget = Vector2.ClampMagnitude(leanTarget, maxLean);
        leanVelocity += (LeanStiffness * (leanTarget - lean) - LeanDamping * leanVelocity) * dt;
        lean += leanVelocity * dt;

        float spin = 0f;
        if (spinStart >= 0f)
        {
            float t = (Time.time - spinStart) / Mathf.Max(spinDuration, 0.0001f);
            if (t >= 1f) spinStart = -1f;
            else spin = 360f * Mathf.SmoothStep(0f, 1f, t);
        }

        bool active = Mathf.Abs(lay) > 0.001f || Mathf.Abs(layVelocity) > 0.001f || Mathf.Abs(swing) > 0.05f ||
                      Mathf.Abs(swingVelocity) > 0.05f || lean.sqrMagnitude > 0.0001f || leanVelocity.sqrMagnitude > 0.0001f ||
                      spinStart >= 0f;
        if (!active)
        {
            if (animating)
            {
                VisualTransform.SetLocalPositionAndRotation(restLocalPosition, restLocalRotation);
                animating = false;
            }
            swing = swingVelocity = 0f;
            return;
        }
        animating = true;

        // Yatma (tutarken): dünya ekseni (yukarı × gidiş yönü) → parent'ın uzayı
        Quaternion leanRotation = Quaternion.identity;
        if (lean.sqrMagnitude > 0.0001f)
        {
            Vector3 axis = Vector3.Cross(Vector3.up, new Vector3(lean.x, 0f, lean.y).normalized);
            if (parent != null) axis = parent.InverseTransformDirection(axis);
            leanRotation = Quaternion.AngleAxis(lean.magnitude, axis);
        }
        // Sağdaki dönüş önce: önce sap etrafında tur, sonra yatık orağın X'inde sallama, sonra Z'de yatma
        Quaternion rotation = leanRotation * restLocalRotation * layRotation *
                              Quaternion.AngleAxis(swing, swingAxis) *
                              Quaternion.AngleAxis(spin, handleAxis);

        // Tutma noktasının etrafında dön: el sapta kalsın
        Vector3 pivot = GripPoint != null && GripPoint.parent == VisualTransform ? GripPoint.localPosition : Vector3.zero;
        Vector3 scaled = Vector3.Scale(pivot, VisualTransform.localScale);
        Vector3 lift = (parent != null ? parent.InverseTransformDirection(Vector3.up) : Vector3.up) * (harvestLift * lay);
        Vector3 position = restLocalPosition + restLocalRotation * scaled - rotation * scaled + lift;
        VisualTransform.SetLocalPositionAndRotation(position, rotation);
    }
}
