using UnityEngine;

// İnsanın kemiğe oturan bir collider'ı (Slave.BuildColliders kurar): eldiven ve tıklama bunu bulur, hepsini insana iletir.
//   sağ tık basılı → insanı taşıma (IInteractable; eldiven popodaki tutma noktasına oturur: IGloveGrip)
//   sol tık        → kıçsa şaplak, değilse kafesten çıkarma (Slave.OnPartClicked)
public class SlavePart : MonoBehaviour, IInteractable, IClickable, IGloveGrip
{
    [SerializeField] private Slave owner;
    [SerializeField] private bool rear;

    public Slave Owner => owner;

    public void Setup(Slave slave, bool isRear)
    {
        owner = slave;
        rear = isRear;
    }

    // --- IClickable ---
    public void OnClicked(RaycastHit hit) => owner.OnPartClicked(rear, hit);

    // --- IInteractable (taşıma insanın kendisinde) ---
    public void Interact(out bool finished) => owner.Interact(out finished);
    public void InteractContract(out bool success) => owner.InteractContract(out success);
    public void InteractContractBeginnig() => owner.InteractContractBeginnig();
    public void ContractCancel() => owner.ContractCancel();

    // --- IGloveGrip ---
    public Transform GripPoint => owner.GripPoint;
    public float GripCurl => owner.GripCurl;
}
