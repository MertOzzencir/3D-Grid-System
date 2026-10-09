using System.Collections;
using UnityEngine;

public abstract class ToolBase : GridPlaceable, IInteractable, IGloveGrip
{
    public Transform VisualTransform;
    [SerializeField] private float followSpeed = 10f;

    [Header("Eldiven")]
    [Tooltip("Eldivenin avucunun sapta oturacağı nokta; VisualTransform'un altında olsun ki sallanmayla birlikte dönsün. " +
             "Y avucun baktığı yön (sapa doğru), Z parmak uçları. Boşsa eldiven normal tutar.")]
    [SerializeField] private Transform gloveGrip;
    [Tooltip("Sapı kavrarken parmakların kıvrılması (0 = düz, 1 = tam kıvrık)")]
    [SerializeField, Range(0f, 1f)] private float gloveGripCurl = 0.7f;
    [Tooltip("Açıksa gloveGrip'ten sapın etrafında 90°'lik 3 kopya daha üretilir; kameraya göre eldivenin önde kaldığı seçilir")]
    [SerializeField] private bool gloveGripFourSides = true;
    [Tooltip("gloveGrip'ten sapın eksenine uzaklık (avuç yönünde). Sap ekseni gloveGrip'in X'i. Sahnede gizmo ile kontrol et.")]
    [SerializeField] private float gloveGripHandleRadius = 0.11f;
    [Tooltip("Elle ayarlanmış dört tutma noktası (sapın dört yanı). Doluysa otomatik üretim yerine bunlar kullanılır. " +
             "Bileşen menüsünden \"Eldiven: 4 tutma noktasını oluştur\" ile başlangıç halleri üretilir, sonra tek tek ayarlanır.")]
    [SerializeField] private Transform[] gloveGripSides = new Transform[0];

    private const float GripSwitchMargin = 0.15f; // sınır açılarda iki yön arasında gidip gelmesin

    private Transform[] gloveGrips;
    private int currentGrip = -1;

    public Transform GripPoint => gloveGrips == null ? gloveGrip : SelectGrip();

    // Şu an seçili tutma noktasının kendi değeri (GloveGripPoint), yoksa aletin genel değeri
    public float GripCurl
    {
        get
        {
            Transform point = gloveGrips != null && currentGrip >= 0 ? gloveGrips[currentGrip] : gloveGrip;
            return point != null && point.TryGetComponent(out GloveGripPoint settings) ? settings.curl : gloveGripCurl;
        }
    }

    [Header("Eğilme (tutarken)")]
    [Tooltip("Tutup gezdirirken gidiş yönüne eğilme: hız (birim/sn) başına açı ve en fazla açı. 0 = eğilmez.")]
    [SerializeField] private float leanPerSpeed = 4f;
    [SerializeField] private float maxLean = 18f;

    [Header("Kullanım")]
    [Tooltip("İki kullanım arasındaki en kısa süre (saniye). Tıklama anından itibaren sayılır.")]
    [SerializeField] private float cooldown = 0.6f;
    [SerializeField] private ToolSwingAnimation swing = new ToolSwingAnimation();

    private GridDragMotor drag;
    protected GridDragMotor Drag => drag;
    // Alt sınıf görseli kendisi döndürürken (örn. orağın animasyonu) eldivenin tutma yönü değişmesin
    protected bool LockGripSelection { get; set; }
    private float nextUseTime;
    private Coroutine useRoutine;
    private Quaternion visualRestRotation;
    private Vector3 visualRestPosition;

    // Eğilme: kökün hızına (GridDragMotor sürer) yaylı, hareketin biraz gerisinden gelir
    private const float LeanStiffness = 70f, LeanDamping = 11f;
    private Vector2 lean, leanVelocity;   // dünya XZ'de (derece, gidiş yönünde)
    private Vector3 lastPosition;
    private float lastHeldTime = float.MinValue;
    private bool leanApplied;

    // Elde tutuluyor mu (InteractContract her kare çağrılır)
    protected bool IsHeld => Time.time - lastHeldTime < 0.1f;
    // Kökün bu karedeki yatay hızı (tutulmuyorsa sıfır)
    protected Vector3 HeldVelocity { get; private set; }
    // Eğilmenin çarpanı (alt sınıf kendi duruşunda kısabilir, örn. orak hasatta)
    protected float LeanScale { get; set; } = 1f;
    // Alt sınıf görseli kendisi yazıyorsa (orak) ToolBase eğilmeyi uygulamaz, sadece hesaplar (LeanRotation)
    protected virtual bool OwnsVisualPose => false;
    protected Vector3 VisualRestPosition => visualRestPosition;
    protected Quaternion VisualRestRotation => visualRestRotation;
    protected bool IsLeaning => lean.sqrMagnitude > 0.0001f || leanVelocity.sqrMagnitude > 0.0001f;

    // Cooldown doldu ve önceki sallanma bitti
    public bool IsReady => Time.time >= nextUseTime && useRoutine == null;

    protected virtual void Awake()
    {
        drag = new GridDragMotor(this, followSpeed);
        if (VisualTransform != null)
        {
            visualRestRotation = VisualTransform.localRotation;
            visualRestPosition = VisualTransform.localPosition;
        }
        lastPosition = transform.position;
        CreateGloveGrips();
    }

    // Eğilme: her kare hesaplanır; OwnsVisualPose değilse görsele (vuruş animasyonunun üstüne) uygulanır
    protected virtual void LateUpdate()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        Vector3 velocity = (transform.position - lastPosition) / Mathf.Max(dt, 0.0001f);
        lastPosition = transform.position;
        HeldVelocity = IsHeld ? Vector3.ProjectOnPlane(velocity, Vector3.up) : Vector3.zero;

        Vector2 target = new Vector2(HeldVelocity.x, HeldVelocity.z) * leanPerSpeed * LeanScale;
        target = Vector2.ClampMagnitude(target, maxLean);
        leanVelocity += (LeanStiffness * (target - lean) - LeanDamping * leanVelocity) * dt;
        lean += leanVelocity * dt;
        if (!IsLeaning)
        {
            lean = Vector2.zero;
            leanVelocity = Vector2.zero;
        }

        if (OwnsVisualPose || VisualTransform == null) return;
        if (!IsLeaning)
        {
            // Bitince bir kez dinlenme pozuna (vuruş sürüyorsa onun pozu kalır)
            if (leanApplied && useRoutine == null)
                VisualTransform.SetLocalPositionAndRotation(visualRestPosition, visualRestRotation);
            leanApplied = false;
            return;
        }
        leanApplied = true;

        // Vuruş sürüyorsa bu karenin vuruş pozu (coroutine LateUpdate'ten önce yazar), yoksa dinlenme pozu
        Quaternion baseRotation = useRoutine != null ? VisualTransform.localRotation : visualRestRotation;
        Quaternion rotation = LeanRotation() * baseRotation;
        // Tutma noktasının etrafında: el sapta kalsın
        Vector3 pivot = Vector3.Scale(GripPivotLocal(), VisualTransform.localScale);
        VisualTransform.SetLocalPositionAndRotation(visualRestPosition + baseRotation * pivot - rotation * pivot, rotation);
    }

    // Gidiş yönüne eğilme (VisualTransform'un parent'ının uzayında)
    protected Quaternion LeanRotation()
    {
        if (lean.sqrMagnitude < 0.0001f) return Quaternion.identity;
        Vector3 axis = Vector3.Cross(Vector3.up, new Vector3(lean.x, 0f, lean.y).normalized);
        Transform parent = VisualTransform != null ? VisualTransform.parent : null;
        if (parent != null) axis = parent.InverseTransformDirection(axis);
        return Quaternion.AngleAxis(lean.magnitude, axis);
    }

    // Eldivenin tuttuğu nokta, görselin uzayında (yoksa görselin pivot'u)
    protected Vector3 GripPivotLocal()
    {
        Transform grip = GripPoint;
        return grip != null && grip.parent == VisualTransform ? grip.localPosition : Vector3.zero;
    }

    // Dört yön: gloveGrip ve onun sap ekseni etrafında 90°, 180°, 270° döndürülmüş kopyaları.
    // Kopyalar gloveGrip'le aynı parent'ta (görsel altında), vuruşta aletle birlikte savrulurlar.
    private void CreateGloveGrips()
    {
        // Elle ayarlanmış noktalar varsa onlar
        if (HasManualGripSides())
        {
            gloveGrips = gloveGripSides;
            return;
        }

        if (gloveGrip == null || !gloveGripFourSides) return;

        GetGripAxis(gloveGrip, out Vector3 center, out Vector3 axis);
        gloveGrips = new Transform[4];
        gloveGrips[0] = gloveGrip;
        for (int k = 1; k < 4; k++)
        {
            Quaternion turn = Quaternion.AngleAxis(90f * k, axis);
            var copy = new GameObject($"{gloveGrip.name} {90 * k}").transform;
            copy.SetParent(gloveGrip.parent, false);
            copy.SetPositionAndRotation(center + turn * (gloveGrip.position - center), turn * gloveGrip.rotation);
            gloveGrips[k] = copy;
        }
    }

    // Sap ekseni: gloveGrip'in X'i, avuç yönünde (Y) handleRadius kadar içeriden geçer
    private void GetGripAxis(Transform grip, out Vector3 center, out Vector3 axis)
    {
        center = grip.position + grip.up * gloveGripHandleRadius * grip.lossyScale.x;
        axis = grip.right;
    }

#if UNITY_EDITOR
    // gloveGrip'ten sapın etrafında 90°, 180°, 270° döndürülmüş üç kopyayı prefab'a gerçek obje olarak ekler ve
    // gloveGripSides'a bağlar. Başlangıç hali otomatik hesapla aynı; sonra her biri elle sapa oturtulur.
    [ContextMenu("Eldiven: 4 tutma noktasını oluştur")]
    private void CreateGripSidesInEditor()
    {
        if (gloveGrip == null)
        {
            Debug.LogWarning("Önce Glove Grip'i ata", this);
            return;
        }

        UnityEditor.Undo.RecordObject(this, "Eldiven tutma noktaları");

        // Noktalar zaten varsa yenilerini üretme, sadece eksik ayar bileşenlerini ekle
        if (HasManualGripSides())
        {
            AddMissingGripSettings(gloveGripSides);
            UnityEditor.EditorUtility.SetDirty(this);
            return;
        }

        GetGripAxis(gloveGrip, out Vector3 center, out Vector3 axis);

        var sides = new Transform[4];
        sides[0] = gloveGrip;
        for (int k = 1; k < 4; k++)
        {
            Quaternion turn = Quaternion.AngleAxis(90f * k, axis);
            var copy = new GameObject($"{gloveGrip.name} {90 * k}").transform;
            UnityEditor.Undo.RegisterCreatedObjectUndo(copy.gameObject, "Eldiven tutma noktaları");
            copy.SetParent(gloveGrip.parent, false);
            copy.SetPositionAndRotation(center + turn * (gloveGrip.position - center), turn * gloveGrip.rotation);
            sides[k] = copy;
        }

        AddMissingGripSettings(sides);
        gloveGripSides = sides;
        UnityEditor.EditorUtility.SetDirty(this);
    }

    // Her noktaya kendi kıvrılma ayarı; başlangıç değeri aletin genel değeri
    private void AddMissingGripSettings(Transform[] sides)
    {
        foreach (Transform side in sides)
        {
            if (side.TryGetComponent(out GloveGripPoint _)) continue;
            var settings = UnityEditor.Undo.AddComponent<GloveGripPoint>(side.gameObject);
            settings.curl = gloveGripCurl;
        }
    }
#endif

    // Avucu kameradan en çok uzağa bakan (eldivenin sapın kamera tarafında kaldığı) yön.
    // Mevcut yön, yenisi belirgin şekilde daha iyi olmadıkça korunur.
    private Transform SelectGrip()
    {
        // Sallanırken görsel (ve grip'ler) dönüyor: yön seçimi vuruş bitene kadar sabit, eldiven sıçramasın
        if ((useRoutine != null || LockGripSelection) && currentGrip >= 0) return gloveGrips[currentGrip];

        Vector3 view = Camera.main.transform.forward;
        int best = 0;
        for (int i = 1; i < gloveGrips.Length; i++)
            if (Vector3.Dot(gloveGrips[i].up, view) > Vector3.Dot(gloveGrips[best].up, view)) best = i;

        if (currentGrip < 0 ||
            Vector3.Dot(gloveGrips[best].up, view) > Vector3.Dot(gloveGrips[currentGrip].up, view) + GripSwitchMargin)
            currentGrip = best;
        return gloveGrips[currentGrip];
    }

    private bool HasManualGripSides()
    {
        if (gloveGripSides == null || gloveGripSides.Length == 0) return false;
        foreach (Transform side in gloveGripSides)
            if (side == null) return false;
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        // Elle ayarlanmış noktalar: her birinin yeri ve avucun baktığı yön
        if (HasManualGripSides())
        {
            for (int k = 0; k < gloveGripSides.Length; k++)
            {
                Transform side = gloveGripSides[k];
                float s = 0.03f * side.lossyScale.x;
                Gizmos.color = k == 0 ? Color.green : Color.yellow;
                Gizmos.DrawSphere(side.position, s);
                Gizmos.DrawLine(side.position, side.position + side.up * s * 3f);      // avucun baktığı yön
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(side.position, side.position + side.forward * s * 3f); // parmak uçları
            }
            return;
        }

        if (gloveGrip == null || !gloveGripFourSides) return;

        GetGripAxis(gloveGrip, out Vector3 center, out Vector3 axis);
        float size = 0.03f * gloveGrip.lossyScale.x;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(center - axis * size * 5f, center + axis * size * 5f); // sap ekseni
        for (int k = 0; k < 4; k++)
        {
            Quaternion turn = Quaternion.AngleAxis(90f * k, axis);
            Vector3 point = center + turn * (gloveGrip.position - center);
            Gizmos.color = k == 0 ? Color.green : Color.yellow;
            Gizmos.DrawSphere(point, size);
            Gizmos.DrawLine(point, point + turn * gloveGrip.up * size * 3f); // avucun baktığı yön
        }
    }

    // Sol tık: hazırsa sallanmayı başlatır. Asıl kullanım (ağacı kesmek) vuruş anında olur.
    public virtual void Interact(out bool finished)
    {
        finished = false;
        if (!IsReady) return;

        nextUseTime = Time.time + cooldown;
        useRoutine = StartCoroutine(UseRoutine());
    }

    protected abstract bool OnUseWithoutTarget();

    public void InteractContractBeginnig()
    {
        currentGrip = -1; // her tutmada yön baştan seçilsin
        drag.Begin();
    }
    public virtual void InteractContract(out bool success)
    {
        success = true;
        lastHeldTime = Time.time;
        drag.Tick();
    }

    public virtual void ContractCancel()
    {
        StopUse();
        drag.Cancel();
    }

    private IEnumerator UseRoutine()
    {
        if (VisualTransform != null)
            yield return swing.Play(VisualTransform, visualRestRotation, Use);
        else
            Use();

        useRoutine = null;
    }

    // Vuruş anı: hedef hâlâ varsa ona uygula, yoksa aletin kendi davranışı
    private void Use()
    {
        if (!drag.TryUseOnTarget(out _))
            OnUseWithoutTarget();
    }

    // Sallanırken bırakılırsa animasyonu kes, görseli düzelt
    private void StopUse()
    {
        if (useRoutine != null)
        {
            StopCoroutine(useRoutine);
            useRoutine = null;
        }
        if (VisualTransform != null)
            VisualTransform.localRotation = visualRestRotation;
    }
}
