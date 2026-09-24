using UnityEngine;

public class Wood : SourceBase, IInteractable, IToolTarget
{
    [SerializeField] private float followSpeed;
    [SerializeField] private Lenghts woodLength;
    [SerializeField] private Transform getPosition;

    public Lenghts Length => woodLength;

    private GridDragMotor drag;

    private void Awake() => drag = new GridDragMotor(this, followSpeed);

    public void Interact(out bool finished)
    {
        if (!drag.TryUseOnTarget(out finished))
            finished = true;
    }

    public void InteractContractBeginnig() => drag.Begin();
    public void InteractContract(out bool success) { success = true; drag.Tick(); }
    public void ContractCancel() => drag.Cancel();

    public virtual Vector3 GetToolTargetPosition(IInteractable interacted, out bool accept)
    {
        accept = interacted is Wood;
        return accept ? getPosition.position : default;
    }

    public virtual Quaternion GetToolTargetRotation() => Quaternion.identity;

    public virtual void OnToolTargetExit() => WoodMergeRecipeManager.CancelMergeChoice();

    public virtual bool OnToolUsed(IInteractable tool) => WoodMergeRecipeManager.TryMerge(this, tool);

    public void ResolveMergeChoice(WoodMergeRecipe chosenRecipe, Wood otherWood)
        => WoodMergeRecipeManager.ApplyRecipe(this, otherWood, chosenRecipe);
}