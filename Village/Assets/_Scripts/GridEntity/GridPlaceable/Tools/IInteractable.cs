
using System;

public interface IInteractable
{
    public void Interact(out bool finished);
    public void InteractContract(out bool success);
    public void InteractContractBeginnig();
    public void ContractCancel();
}
