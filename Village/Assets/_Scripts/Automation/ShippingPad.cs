using System.Collections.Generic;
using UnityEngine;

// Gönderim rampası: İmparatorluğa giden parçaları (robotlar) alır ve sayar. Araçlar sonra gelecek.
public class ShippingPad : Machine
{
    [Tooltip("Kabul ettiği parçalar (boşsa hepsi)")]
    [SerializeField] private List<ItemSO> accepts = new List<ItemSO>();

    private int shipped;

    public override bool CanTake(out ItemSO item)
    {
        item = null;
        return false;
    }

    public override bool CanAccept(ItemSO item) => IsPlaced && item != null && (accepts.Count == 0 || accepts.Contains(item));

    public override void Accept(ItemSO item)
    {
        shipped++;
        RefreshView();
    }

    protected override void Tick(float step) { }

    protected override string Status() => $"Imperial Shipping\n<size=70%>shipped: {shipped}</size>";
}
