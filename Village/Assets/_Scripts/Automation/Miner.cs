using UnityEngine;

// Madenci: maden yatağının üstüne konur (yatak hemen altındaki hücrede), her `interval` saniyede çıkış yuvasına bir
// cevher koyar. Oyuncu maden kırmaz (kullanıcı: Satisfactory gibi).
public class Miner : Machine
{
    [SerializeField] private float interval = 2f;

    private float progress;
    private ItemSO ore;

    protected override void Tick(float step)
    {
        ore = DepositOre();
        if (ore == null || !OutputHasRoom(ore, 1)) return;
        progress += step;
        if (progress < interval) return;
        progress = 0f;
        AddOutput(ore, 1);
    }

    private ItemSO DepositOre()
    {
        Vector3Int below = OriginWorldPosition + Vector3Int.down;
        return GridManager.Instance.TryGetCell(below, out GridData data) && data.Base is OreDeposit deposit ? deposit.Ore : null;
    }

    protected override string Status()
    {
        if (ore == null) return "Miner\n<size=70%>needs an ore deposit</size>";
        return $"Miner\n<size=70%>{ore.Name} {outputCount}/{outputCapacity}</size>";
    }
}
