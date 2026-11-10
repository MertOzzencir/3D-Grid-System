using UnityEngine;

// Maden yatağı: zemin (base) türü, üstüne konan madenciye hangi cevheri verdiğini söyler. Geliştirici build mode'undan
// (Space → Base) konur.
public class OreDeposit : GridBase
{
    [SerializeField] private ItemSO ore;
    public ItemSO Ore => ore;
}
