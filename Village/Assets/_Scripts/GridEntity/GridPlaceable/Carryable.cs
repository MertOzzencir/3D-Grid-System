using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Sağ tıkla alınıp grid'de taşınan basit eşya (balık, kedi kakası...): GridDragMotor ile taşınır, sağ tık bırakınca
// grid'e konur. Sol tık (elde tutarken) önce altındaki hedefe uygulanır (IToolTarget, örn. kedi balığı yer); hedef yoksa
// alt sınıfın UseWithoutTarget'ı (örn. kakayı ekmek). Görünüş: Model atanırsa o (pivot hücrenin ortasında), yoksa alt
// sınıfın kodla kurduğu geçici görünüm. Kökte collider yoksa görünüşü saran kutu eklenir (tutulabilsin).
// Yığın (Stackable olan türlerde, tohumdaki gibi): bir obje Count kadar eşya; tutarken mouse'un altına gelen aynı türden
// (aynı SO) eşyalar ele uçup yığına katılır, sayı (×N) üstünde görünür. Kullanınca (UseOne) bir azalır, bitince yok olur.
// Sayı kayda girer (ISaveState).
public abstract class Carryable : GridPlaceable, IInteractable, ISaveState
{
    [SerializeField] private float followSpeed = 14f;
    [Header("Görünüm")]
    [Tooltip("Eşyanın modeli (prefab ya da FBX; pivot hücrenin ortasında). Boşsa geçici görünüm çizilir.")]
    [SerializeField] private GameObject model;
    [Tooltip("Geçici görünümün shader'ı (Village/Lit). Referansla tutulur ki build'e girsin (Shader.Find build'de " +
             "referanssız shader'ı bulamaz, primitive'lerin varsayılan materyali de build'de görünmüyordu).")]
    [SerializeField] private Shader placeholderShader;
    [Tooltip("Ortaya çıkınca (örn. kediden düşünce) yerine zıplama süresi ve yüksekliği")]
    [SerializeField] private float hopDuration = 0.45f;
    [SerializeField] private float hopHeight = 0.8f;

    [Header("Yığın (sadece yığınlanan türlerde)")]
    [Tooltip("Yığındaki eşya sayısı (build menüsünden gelen bununla başlar)")]
    [SerializeField, Min(1)] private int count = 1;
    [Tooltip("Tutarken mouse'un bu yarıçapında (birim) kalan aynı tür eşyalar toplanır")]
    [SerializeField] private float collectRadius = 0.35f;
    [Tooltip("Toplanan eşyanın ele uçma süresi (saniye)")]
    [SerializeField] private float collectFlyDuration = 0.18f;
    [Tooltip("Sayı yazısının pivot'tan yüksekliği ve boyu (yazı sadece 1'den fazlaysa görünür)")]
    [SerializeField] private float labelHeight = 0.45f;
    [SerializeField] private float labelSize = 2.5f;

    private const float PunchDuration = 0.2f;
    private const float PunchAmount = 0.25f;

    private bool consumed;   // son eşya kullanıldı: obje yok ediliyor
    private bool collecting; // başka bir yığına toplanıyor (uçuyor)
    private TextMeshPro label;
    private Vector3 visualRestScale = Vector3.one;
    private float punchStart = -1f;
    private readonly RaycastHit[] hitBuffer = new RaycastHit[16];
    private readonly List<GameObject> flying = new List<GameObject>();

    public int Count => count;
    // Aynı türden olanlar tutarken toplanıp yığın olur mu (alt sınıf: balık evet)
    protected virtual bool Stackable => false;

    protected GridDragMotor Drag { get; private set; }
    protected Transform Visual { get; private set; }

    protected virtual void Awake()
    {
        Drag = new GridDragMotor(this, followSpeed);
        BuildVisual();
        visualRestScale = Visual.localScale;
        if (Stackable)
        {
            CreateLabel();
            UpdateLabel();
        }
    }

    private void BuildVisual()
    {
        GameObject built;
        if (model != null) built = Instantiate(model, transform, false);
        else
        {
            built = new GameObject("Placeholder");
            built.transform.SetParent(transform, false);
            BuildPlaceholder(built.transform);
        }
        built.name = "Model";
        Visual = built.transform;

        if (GetComponent<Collider>() != null) return;
        var box = gameObject.AddComponent<BoxCollider>();
        Bounds bounds = default;
        bool any = false;
        foreach (Renderer r in built.GetComponentsInChildren<Renderer>())
        {
            var local = new Bounds(transform.InverseTransformPoint(r.bounds.center), r.bounds.size);
            if (any) bounds.Encapsulate(local);
            else bounds = local;
            any = true;
        }
        box.center = any ? bounds.center : Vector3.down * 0.25f;
        box.size = any ? Vector3.Max(bounds.size, Vector3.one * 0.3f) : new Vector3(0.6f, 0.5f, 0.6f);
    }

    // Model yokken geçici görünüm (parent: modelin yeri)
    protected abstract void BuildPlaceholder(Transform parent);

    // Geçici görünüm parçası: collider'sız primitive, renkli (placeholderShader ile; yoksa primitive'in kendi materyali)
    protected GameObject Primitive(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 scale, Color color,
                                   Quaternion? rotation = null)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = rotation ?? Quaternion.identity;
        part.transform.localScale = scale;
        var renderer = part.GetComponent<Renderer>();
        if (placeholderShader != null) renderer.material = new Material(placeholderShader) { name = "Placeholder (generated)" };
        renderer.material.color = color;
        return part;
    }

    // Grid'e konmuş eşya fromPosition'dan yerine kavis çizerek zıplar
    public void HopFrom(Vector3 fromPosition) => StartCoroutine(Hop(fromPosition));

    private IEnumerator Hop(Vector3 from)
    {
        Vector3 to = transform.position;
        for (float elapsed = 0f; elapsed < hopDuration; elapsed += Time.deltaTime)
        {
            float t = elapsed / hopDuration;
            transform.position = Vector3.Lerp(from, to, t) + Vector3.up * (hopHeight * 4f * t * (1f - t));
            yield return null;
        }
        transform.position = to;
    }

    // Hedef yokken sol tık (elde tutarken). true: eşya kullanıldı (örn. ekildi)
    protected virtual bool UseWithoutTarget() => false;

    // --- IInteractable ---
    public virtual void Interact(out bool finished)
    {
        if (Drag.TryUseOnTarget(out finished)) return;
        finished = UseWithoutTarget();
    }

    public virtual void InteractContractBeginnig()
    {
        consumed = false;
        Drag.Begin();
    }

    public virtual void InteractContract(out bool success)
    {
        success = true;
        Drag.Tick();
        if (Stackable && !consumed) CollectUnderMouse();
    }

    public virtual void ContractCancel() => Drag.Cancel();

    // Yığından bir eşya kullanıldı (örn. kedi bir balık yedi): biri azalır, bitince yok olur (true)
    protected bool UseOne()
    {
        count--;
        Punch();
        UpdateLabel();
        if (count > 0) return false;
        Consume();
        return true;
    }

    // Eşya yok olacak (yendi, ekildi): elden bırakılır, grid'e geri konmaya çalışılmaz
    protected void Consume()
    {
        consumed = true;
        Drag.Abort();
        if (InteractableController.Instance != null) InteractableController.Instance.Release(this);
        Destroy(gameObject);
    }

    // --- Yığın: toplama ---

    private void CollectUnderMouse()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        int hits = Physics.SphereCastNonAlloc(ray, collectRadius, hitBuffer, 200f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits; i++)
        {
            Carryable other = hitBuffer[i].collider.GetComponentInParent<Carryable>();
            if (other == null || other == this || other.collecting || other.GetData() != GetData()) continue;
            Absorb(other);
        }
    }

    private void Absorb(Carryable other)
    {
        count += other.count;
        other.collecting = true;
        other.StopAllCoroutines(); // zıplama animasyonundaysa yarıda kalsın
        GridManager.Instance?.PlaceableRemoveOn(other, false);
        foreach (Collider collider in other.GetComponentsInChildren<Collider>()) collider.enabled = false;
        if (other.label != null) other.label.gameObject.SetActive(false);

        flying.Add(other.gameObject);
        StartCoroutine(FlyIn(other.transform));
        UpdateLabel();
        Punch();
    }

    // Toplanan eşya ele doğru küçülerek uçar, sonra yok olur (sayısı zaten yığında)
    private IEnumerator FlyIn(Transform other)
    {
        Vector3 start = other.position;
        Vector3 startScale = other.localScale;
        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(collectFlyDuration, 0.0001f))
        {
            if (other == null) yield break;
            float e = t * t; // hızlanarak gelsin
            other.position = Vector3.Lerp(start, transform.position, e) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.3f;
            other.localScale = startScale * Mathf.Lerp(1f, 0.3f, e);
            yield return null;
        }
        if (other == null) yield break;
        flying.Remove(other.gameObject);
        Destroy(other.gameObject);
    }

    protected virtual void OnDestroy()
    {
        foreach (GameObject other in flying)
            if (other != null) Destroy(other);
    }

    // --- Yığın: sayı yazısı ve şişme ---

    private void CreateLabel()
    {
        var labelObject = new GameObject("Count");
        labelObject.transform.SetParent(transform, false);
        label = labelObject.AddComponent<TextMeshPro>();
        label.fontSize = labelSize;
        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = FontStyles.Bold;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.rectTransform.sizeDelta = new Vector2(2f, 1f);
    }

    private void UpdateLabel()
    {
        if (label == null) return;
        label.gameObject.SetActive(count > 1);
        label.text = $"×{count}";
    }

    private void Punch() => punchStart = Time.time;

    private void LateUpdate()
    {
        // Yazı eşyayla dönmez, hep kameraya bakar
        if (label != null && label.gameObject.activeSelf && Camera.main != null)
            label.transform.SetPositionAndRotation(transform.position + Vector3.up * labelHeight, Camera.main.transform.rotation);

        if (Visual == null || punchStart < 0f) return;
        float t = (Time.time - punchStart) / PunchDuration;
        if (t >= 1f)
        {
            Visual.localScale = visualRestScale;
            punchStart = -1f;
            return;
        }
        Visual.localScale = visualRestScale * (1f + PunchAmount * Mathf.Sin(t * Mathf.PI) * (1f - t));
    }

    // --- Kayıt ---

    public virtual string CaptureState() => count.ToString();

    public virtual void RestoreState(string state)
    {
        if (int.TryParse(state, out int saved) && saved > 0) count = saved;
        UpdateLabel();
    }
}
