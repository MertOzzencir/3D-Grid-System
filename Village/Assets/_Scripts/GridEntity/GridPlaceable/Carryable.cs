using System.Collections;
using UnityEngine;

// Sağ tıkla alınıp grid'de taşınan basit eşya (balık yemi, kedi kakası...): GridDragMotor ile taşınır, sağ tık bırakınca
// grid'e konur. Sol tık (elde tutarken) önce altındaki hedefe uygulanır (IToolTarget, örn. kedi balığı yer); hedef yoksa
// alt sınıfın UseWithoutTarget'ı (örn. kakayı ekmek). Görünüş: Model atanırsa o (pivot hücrenin ortasında), yoksa alt
// sınıfın kodla kurduğu geçici görünüm. Kökte collider yoksa görünüşü saran kutu eklenir (tutulabilsin).
public abstract class Carryable : GridPlaceable, IInteractable
{
    [SerializeField] private float followSpeed = 14f;
    [Header("Görünüm")]
    [Tooltip("Eşyanın modeli (prefab ya da FBX; pivot hücrenin ortasında). Boşsa geçici görünüm çizilir.")]
    [SerializeField] private GameObject model;
    [Tooltip("Ortaya çıkınca (örn. kediden düşünce) yerine zıplama süresi ve yüksekliği")]
    [SerializeField] private float hopDuration = 0.45f;
    [SerializeField] private float hopHeight = 0.8f;

    protected GridDragMotor Drag { get; private set; }
    protected Transform Visual { get; private set; }

    protected virtual void Awake()
    {
        Drag = new GridDragMotor(this, followSpeed);
        BuildVisual();
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

    // Geçici görünüm parçası: collider'sız primitive, renkli
    protected static GameObject Primitive(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 scale, Color color,
                                          Quaternion? rotation = null)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = rotation ?? Quaternion.identity;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().material.color = color;
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

    public virtual void InteractContractBeginnig() => Drag.Begin();

    public virtual void InteractContract(out bool success)
    {
        success = true;
        Drag.Tick();
    }

    public virtual void ContractCancel() => Drag.Cancel();

    // Eşya yok olacak (yendi, ekildi): elden bırakılır, grid'e geri konmaya çalışılmaz
    protected void Consume()
    {
        Drag.Abort();
        if (InteractableController.Instance != null) InteractableController.Instance.Release(this);
        Destroy(gameObject);
    }
}
