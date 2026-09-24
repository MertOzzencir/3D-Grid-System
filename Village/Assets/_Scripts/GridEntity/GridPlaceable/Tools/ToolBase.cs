using UnityEngine;

public abstract class ToolBase : GridPlaceable, IInteractable
{
    public Transform VisualTransform;
    [SerializeField] private float followSpeed = 10f;

    private GridDragMotor drag;

    protected virtual void Awake() => drag = new GridDragMotor(this, followSpeed);

    public void Interact(out bool finished)
    {
        if (!drag.TryUseOnTarget(out finished))
            finished = OnUseWithoutTarget();
    }

    protected abstract bool OnUseWithoutTarget();

    public void InteractContractBeginnig() => drag.Begin();
    public void InteractContract(out bool success) { success = true; drag.Tick(); }
    public void ContractCancel() => drag.Cancel();
}