using UnityEngine;

public class SourceBaseStaticManager : MonoBehaviour
{
    [SerializeField] private SourcesSO[] inventoryEntries;

    public static SourcesSO[] Inventory;

    void Awake()
    {
        Inventory = inventoryEntries;
    }

    public static SourcesSO GetItem<T>() where T : SourceBase
    {
        foreach (var a in Inventory)
        {
            if (a.Prefab is T) return a;
        }
        return null;
    }
    public static SourcesSO GetItemByIndex(int index)
    {
        return Inventory[index];
    }
}