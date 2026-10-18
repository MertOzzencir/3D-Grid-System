using UnityEngine;

// İstasyonun (Workstation) üstündeki parçanın yerini tutan collider: alet ve parça buraya hedeflenir (istasyona
// yönlendirilir); sağ tıkla tutulunca parça istasyondan alınıp taşınır (bütün çağrılar parçaya yönlendirilir).
public class WorkstationTop : MonoBehaviour, IInteractable, IStickyToolTarget, IHoldTarget
{
    private Workstation block;
    private BoxCollider box;
    private IInteractable carried; // kütükten alınıp taşınan parça

    public void Init(Workstation owner, BoxCollider collider)
    {
        block = owner;
        box = collider;
    }

    public void SetActive(bool active) => box.enabled = active;

    // --- IToolTarget / IHoldTarget: kütüğe yönlendirilir ---
    public Vector3 GetToolTargetPosition(IInteractable interacted, out bool accept) => block.GetToolTargetPosition(interacted, out accept);
    public Quaternion GetToolTargetRotation() => block.GetToolTargetRotation();
    public void OnToolTargetExit() => block.OnToolTargetExit();
    public bool OnToolUsed(IInteractable tool) => block.OnToolUsed(tool);
    public bool IsStillTargeted(IInteractable interacted, Ray mouseRay) => block.IsStillTargeted(interacted, mouseRay);
    public bool TryHold(GridPlaceable item) => block.TryHold(item);

    // --- IInteractable: sağ tıkla tutulunca parça kütükten alınır, sonrası parçaya ---
    public void InteractContractBeginnig()
    {
        carried = block.Release() as IInteractable;
        carried?.InteractContractBeginnig();
    }

    public void InteractContract(out bool success)
    {
        success = carried != null;
        carried?.InteractContract(out success);
    }

    public void Interact(out bool finished)
    {
        finished = true;
        carried?.Interact(out finished);
        if (finished) carried = null;
    }

    public void ContractCancel()
    {
        carried?.ContractCancel();
        // İnce parça uygun yer bulamadıysa (havada kaldı) istasyona geri konur
        if (carried is FinePlaceable piece && !piece.IsPlaced && piece.transform.parent == null) block.TryHold(piece);
        carried = null;
    }
}
