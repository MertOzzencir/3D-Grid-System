using UnityEngine;

// Tek parça odun. Uzunluğu grid yüksekliğidir (SO Size.y). Katmanlar için bkz. WoodLayout.
// Katmanları her zaman boştur; bir katmana odun eklenince MergedWood'a dönüşür.
public class Wood : SourceBase, IInteractable, IToolTarget, IBlueprintPiece, IWoodStack
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
}
