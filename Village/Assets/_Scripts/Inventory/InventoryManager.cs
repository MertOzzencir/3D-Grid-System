using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance;
    public Dictionary<SourcesSO, int> sources = new Dictionary<SourcesSO, int>();

    private InventoryUIManager inventoryManager;
    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(this.gameObject);
        }
        inventoryManager = GetComponent<InventoryUIManager>();
    }
    public void AddSource(SourcesSO source)
    {
        if (sources.ContainsKey(source))
        {
            sources[source] += 1;
        }
        else
        {
            sources.Add(source, 1);
        }
        inventoryManager.Refresh(this);
    }
    public void ReduceSource(SourcesSO source)
    {
        if (sources.ContainsKey(source))
        {
            sources[source] -= 1;
            if (sources[source] <= 0)
            {
                sources.Remove(source);
                inventoryManager.RemoveSourceOnUI(source);
                return;
            }
        }

        inventoryManager.Refresh(this);
    }
    public int SourceAmount(SourcesSO amount)
    {
        if (sources.ContainsKey(amount))
        {
            return sources[amount];
        }
        return -1;
    }
    public bool CheckSource(SourcesSO checkedSource)
    {
        if (sources.ContainsKey(checkedSource))
            return true;
        return false;
    }
    [ContextMenu("Show Inventory")]
    public void ShowInventory()
    {
        foreach (var a in sources)
        {
            Debug.Log("Source Name: " + a.Key + " Source Amount: " + a.Value);
        }
    }




}
