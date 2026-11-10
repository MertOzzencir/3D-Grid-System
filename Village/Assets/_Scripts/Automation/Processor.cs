using System.Collections.Generic;
using System.Text;
using UnityEngine;

// İşleyen makine (eritici, montaj): tarifin girdileri dolunca `craftSeconds` sürede çıktıyı üretir. Girdi yuvaları tarifin
// `bufferCrafts` katı kadar parça tutar (kollar beklemeden doldurabilsin).
public class Processor : Machine
{
    [SerializeField] private string displayName = "Smelter";
    [SerializeField] private List<ItemAmount> inputs = new List<ItemAmount>();
    [SerializeField] private ItemSO output;
    [SerializeField] private int outputPerCraft = 1;
    [SerializeField] private float craftSeconds = 2f;
    [SerializeField] private int bufferCrafts = 2;

    private readonly Dictionary<ItemSO, int> stored = new Dictionary<ItemSO, int>();
    private bool crafting;
    private float progress;

    public override bool CanAccept(ItemSO item)
    {
        if (!IsPlaced || item == null) return false;
        ItemAmount need = inputs.Find(i => i.Item == item);
        return need != null && Stored(item) < need.Count * bufferCrafts;
    }

    public override void Accept(ItemSO item)
    {
        stored[item] = Stored(item) + 1;
        RefreshView();
    }

    private int Stored(ItemSO item) => stored.TryGetValue(item, out int count) ? count : 0;

    protected override void Tick(float step)
    {
        if (crafting)
        {
            progress += step;
            if (progress < craftSeconds || !OutputHasRoom(output, outputPerCraft)) return;
            AddOutput(output, outputPerCraft);
            crafting = false;
        }

        if (output == null || !OutputHasRoom(output, outputPerCraft)) return;
        foreach (ItemAmount need in inputs)
            if (need.Item == null || Stored(need.Item) < need.Count) return;
        foreach (ItemAmount need in inputs) stored[need.Item] -= need.Count;
        crafting = true;
        progress = 0f;
    }

    protected override string Status()
    {
        var text = new StringBuilder(displayName).Append("\n<size=70%>");
        foreach (ItemAmount need in inputs)
            if (need.Item != null) text.Append($"{need.Item.Name} {Stored(need.Item)}/{need.Count}  ");
        if (crafting) text.Append($"\n{Mathf.Min(100, Mathf.RoundToInt(progress / craftSeconds * 100))}%");
        if (output != null) text.Append($"\n→ {output.Name} {outputCount}/{outputCapacity}");
        return text.Append("</size>").ToString();
    }
}
