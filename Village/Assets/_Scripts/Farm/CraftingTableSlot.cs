using UnityEngine;

// Çalışma masasının tepsisindeki bir slot: sol tık hücreyi doldurur (IClickable); sağ tık basılı masayı taşır
// (IInteractable, masaya yönlendirir: eldiven slotun üstündeyken de masa tutulabilsin).
public class CraftingTableSlot : MonoBehaviour, IClickable, IInteractable
{
    private SoilCraftingTable table;
    private Vector2Int cell;

    public void Setup(SoilCraftingTable owner, Vector2Int slot)
    {
        table = owner;
        cell = slot;
    }

    public void OnClicked(RaycastHit hit) => table.OnSlotClicked(cell);

    public void InteractContractBeginnig() => table.InteractContractBeginnig();
    public void InteractContract(out bool success) => table.InteractContract(out success);
    public void Interact(out bool finished) => table.Interact(out finished);
    public void ContractCancel() => table.ContractCancel();
}
