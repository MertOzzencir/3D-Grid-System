using System;
using Unity.VisualScripting;
using UnityEngine;

public class InventoryUIManager : MonoBehaviour
{
    [SerializeField] private InventorySlot[] slots;
    [SerializeField] private Canvas inventoryCanvas;

    void Awake()
    {
        InputManager.OnTab += OpenInventory;
        inventoryCanvas.gameObject.SetActive(false);
    }

    private void OpenInventory()
    {
        inventoryCanvas.gameObject.SetActive(!inventoryCanvas.gameObject.activeSelf);
    }

    public void Refresh(InventoryManager sm)
    {
        foreach (var (source, amount) in sm.sources)
        {
            InventorySlot currentSlot = FindSlotBySource(source) ?? FindEmptySlot();

            if (currentSlot == null) continue;

            currentSlot.Add(source, amount.ToString(), source.SourceName, source.Icon);
        }
    }
    public void RemoveSourceOnUI(SourcesSO s)
    {
        InventorySlot c = FindSlotBySource(s);
        if (c != null) c.Reset();

    }
    private InventorySlot FindSlotBySource(SourcesSO source)
    {
        foreach (var slot in slots)
            if (slot.SourceOnSlot == source) return slot;
        return null;
    }

    private InventorySlot FindEmptySlot()
    {
        foreach (var slot in slots)
            if (slot.SourceOnSlot == null) return slot;
        return null;
    }
    void OnDestroy()
    {
        InputManager.OnTab -= OpenInventory;
    }

}