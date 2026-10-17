using UnityEngine;

// Tek parça odun. Uzunluğu grid yüksekliğidir (SO Size.y). Katmanlar için bkz. WoodLayout.
// Katmanları her zaman boştur; bir katmana odun eklenince MergedWood'a dönüşür.
public class Wood : SourceBase, IInteractable, IStickyToolTarget, IBlueprintPiece, IWoodStack
{
    [SerializeField] private float followSpeed;

    public int Length => GetData().Size.y;

    public string BlueprintSignature => $"Wood:{Length}";
    public int BlueprintRotationSteps => 0; // dik tek odun her yönde aynı

    // --- IWoodStack ---
    public int BaseLength => Length;
    public int PieceAt(WoodSide side, int layer) => 0;
    public Renderer[] PieceRenderers(WoodSide side) => System.Array.Empty<Renderer>();

    private GridDragMotor drag;
    private WoodMergeTarget mergeTarget;

    private void Awake()
    {
        drag = new GridDragMotor(this, followSpeed);
        mergeTarget = new WoodMergeTarget(this);
    }

    // Kil görünüm (WoodMerger açtıysa): modeli tek parça kil şekliyle değiştirilir. Şekil görselin (Visual) çocuğu:
    // ağaçtan düşme animasyonu görseli taşıyor. Görselin ölçeği (örn. 2BR'de 1, 2, 1) geri alınır ki şekil ölçeksiz
    // hesaplansın (yoksa köşeler uzardı). Aynı uzunluktaki odunlar aynı şekli paylaşır (önbellek).
    private void Start()
    {
        WoodMerger merger = WoodMerger.Instance;
        if (merger == null || Visual == null) return;

        Renderer[] renderers = Visual.GetComponentsInChildren<Renderer>();
        var space = new GameObject("Clay Space").transform;
        space.SetParent(Visual, false);
        Vector3 s = Visual.localScale;
        space.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);

        bool found = false;
        Bounds bounds = default;
        foreach (MeshFilter filter in Visual.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            Bounds mesh = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mesh.center + Vector3.Scale(mesh.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 point = space.InverseTransformPoint(filter.transform.TransformPoint(corner));
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                else bounds.Encapsulate(point);
            }
        }
        if (!found)
        {
            Destroy(space.gameObject);
            return;
        }
        string key = $"wood:{Vector3Int.RoundToInt(bounds.center * 100f)}:{Vector3Int.RoundToInt(bounds.size * 100f)}";
        GameObject clay = merger.AttachClay(space, new System.Collections.Generic.List<Bounds> { bounds }, renderers, key);
        if (clay == null) Destroy(space.gameObject);
    }

    // --- IInteractable: bu odun elde taşınırken ---
    public void Interact(out bool finished)
    {
        if (!drag.TryUseOnTarget(out finished))
            finished = true;
    }

    public void InteractContractBeginnig() => drag.Begin();
    public void InteractContract(out bool success) { success = true; drag.Tick(); }
    public void ContractCancel() => drag.Cancel();

    // --- IToolTarget: başka bir odun bu oduna getirildiğinde ---
    public Vector3 GetToolTargetPosition(IInteractable interacted, out bool accept) => mergeTarget.GetToolTargetPosition(interacted, out accept);
    public Quaternion GetToolTargetRotation() => mergeTarget.GetToolTargetRotation();
    public void OnToolTargetExit() => mergeTarget.OnToolTargetExit();
    public bool OnToolUsed(IInteractable tool) => mergeTarget.OnToolUsed(tool);
    public bool IsStillTargeted(IInteractable interacted, Ray mouseRay) => mergeTarget.IsStillTargeted(interacted, mouseRay);
}
