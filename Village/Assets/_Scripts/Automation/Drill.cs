using UnityEngine;
using UnityEngine.InputSystem;

// Matkap (kullanıcı: Astroneer'daki gibi bir alana bakıp basılı tutarak kazmak, tek tek tıklamak değil): elde tutulan
// alet. Sağ tık basılıyken (hazırlık) kazılabilir bloğa (MineableDeposit) nişan alır: matkap nişan noktasına bakar.
// Sol tık basılı kaldıkça nişan noktasının çevresindeki (`radius`) parçalar tek seferde kopar ve cevheri düşer; mouse
// gezdirildikçe fırça da kayar. Kazarken matkap titrer.
public class Drill : ToolBase, IToolReach
{
    [Header("Kazma")]
    [Tooltip("Matkabın uzanabildiği yatay uzaklık (birim; karakterin genel menzilinden bağımsız)")]
    [SerializeField] private float reach = 4.5f;
    [Tooltip("Fırçanın yarıçapı (birim): nişan noktasına bu kadar yakın parçalar aynı anda kopar")]
    [SerializeField] private float radius = 0.4f;
    [Tooltip("Kazarken görselin titreme genliği (birim)")]
    [SerializeField] private float shake = 0.012f;

    private bool drilling;
    private bool wasDrilling;

    public float Reach => reach;

    protected override bool OnUseWithoutTarget() => false;

    // Tek tık bir şey yapmaz: kazma basılı tutarak (InteractContract)
    public override void Interact(out bool finished) => finished = false;

    public override void InteractContract(out bool success)
    {
        base.InteractContract(out success);
        drilling = InteractableController.IsAiming && Mouse.current != null && Mouse.current.leftButton.isPressed
                   && Drag.CurrentTarget is MineableDeposit;
        if (drilling) ((MineableDeposit)Drag.CurrentTarget).DrillTick(radius);
        else if (Drag.CurrentTarget is MineableDeposit deposit) deposit.ShowBrush(radius);
    }

    public override void ContractCancel()
    {
        drilling = false;
        base.ContractCancel();
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (VisualTransform == null) return;
        if (drilling)
            VisualTransform.localPosition = VisualRestPosition + Random.insideUnitSphere * shake;
        else if (wasDrilling)
            VisualTransform.localPosition = VisualRestPosition;
        wasDrilling = drilling;
        if (!IsHeld) drilling = false;
    }
}
