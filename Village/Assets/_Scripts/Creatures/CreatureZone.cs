using UnityEngine;

public enum CreatureZoneType { Head, Rear, Body }

// Canlının etkileşim bölgesi (kafa, kıç...): bir collider'ın objesinde durur, olayları sahibine (Creature) iletir.
//   sol tık (elde bir şey yokken) → IClickable → Creature.OnZoneClicked
//   sağ tık basılı                → IInteractable sözleşmesi → OnZoneHoldBegin / Tick / End (örn. göbek okşama)
//   mouse üstünde                 → IHoverable → OnZoneHover
// IGloveFreeHold: sağ tıkla "tutulduğunda" eldiven bölgeye çakılmaz, mouse'u takip edip yüzeyde gezer (okşama).
public class CreatureZone : MonoBehaviour, IClickable, IInteractable, IHoverable, IGloveFreeHold
{
    [SerializeField] private CreatureZoneType zone;
    [SerializeField] private Creature owner;

    public CreatureZoneType Zone => zone;
    public Creature Owner => owner;

    public void Setup(CreatureZoneType zoneType, Creature creature)
    {
        zone = zoneType;
        owner = creature;
    }

    private void Awake()
    {
        if (owner == null) owner = GetComponentInParent<Creature>();
    }

    // --- IClickable ---
    public void OnClicked(RaycastHit hit) => owner.OnZoneClicked(zone, hit);

    // --- IHoverable ---
    public void OnHoverEnter() => owner.OnZoneHover(zone, true);
    public void OnHoverExit() => owner.OnZoneHover(zone, false);

    // --- IInteractable (sağ tık basılı) ---
    private bool holding;

    public void InteractContractBeginnig() => holding = owner.OnZoneHoldBegin(zone);

    public void InteractContract(out bool success)
    {
        success = holding && owner.OnZoneHoldTick(zone);
        if (!success) holding = false;
    }

    public void ContractCancel()
    {
        if (!holding) return;
        holding = false;
        owner.OnZoneHoldEnd(zone);
    }

    // Sağ tık basılıyken sol tık: şimdilik bir şey yapmaz, tutma devam eder
    public void Interact(out bool finished) => finished = false;
}
