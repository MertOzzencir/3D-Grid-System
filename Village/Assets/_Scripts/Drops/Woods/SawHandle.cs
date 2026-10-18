using UnityEngine;

// Testere sehpasının testeresi: sağ tıkla tutulunca mouse sağa sola sürüklendikçe testere kesim çizgisi boyunca gidip
// gelir (SawBench). Sehpada kesilecek parça yoksa tutulmaz.
public class SawHandle : MonoBehaviour, IInteractable
{
    private SawBench bench;

    public void Init(SawBench owner) => bench = owner;

    public void InteractContractBeginnig() => bench.BeginSaw();
    public void InteractContract(out bool success) => success = bench.TickSaw();
    public void Interact(out bool finished) => finished = false;
    public void ContractCancel() => bench.EndSaw();
}
