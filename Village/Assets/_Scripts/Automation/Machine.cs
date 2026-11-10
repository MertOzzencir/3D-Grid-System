using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Kolun parça alıp verebildiği her şey (makineler). Kol arkasındaki portun çıkışından alır, önündekinin girişine koyar.
public interface IItemPort
{
    bool CanTake(out ItemSO item);   // çıkış yuvasında alınacak parça var mı
    ItemSO Take();                   // bir tane al
    bool CanAccept(ItemSO item);     // giriş yuvası bu parçayı kabul eder mi
    void Accept(ItemSO item);        // bir tane koy
}

[Serializable]
public class ItemAmount
{
    public ItemSO Item;
    public int Count = 1;
}

// Otomasyon makinelerinin ortak tabanı: grid'de durur, elle taşınabilir (MovablePlaceable), merkezi saatle çalışır,
// çıkış yuvasındaki parçaları üstünde küçük küpler olarak gösterir, üstünde durum yazısı (prototip) taşır.
// Sadece grid'e yerleşikken çalışır (elde taşınırken durur).
public abstract class Machine : MovablePlaceable, IItemPort, IAutomationTick
{
    [Header("Görünüm (prototip)")]
    [Tooltip("Çıkış yuvasındaki parçalar bu yükseklikte (pivottan) küçük küpler olarak dizilir")]
    [SerializeField] protected float stackHeight = 0.45f;
    [SerializeField] private float labelHeight = 1.1f;

    protected ItemSO outputItem;
    protected int outputCount;
    [Tooltip("Çıkış yuvasının alabileceği en fazla parça")]
    [SerializeField] protected int outputCapacity = 10;

    private TextMeshPro label;
    private readonly List<Renderer> stackCubes = new List<Renderer>();
    private static readonly Dictionary<ItemSO, Material> itemMaterials = new Dictionary<ItemSO, Material>();
    private const int VisibleStack = 4;

    public bool IsPlaced => GridManager.Instance != null && GridManager.Instance.TryGetCell(OriginWorldPosition, out GridData data)
                            && data.Placeable == this;

    protected override void Awake()
    {
        base.Awake();
        CreateLabel();
        CreateStack();
    }

    private void OnEnable() => AutomationTicker.Register(this, false);
    private void OnDisable() => AutomationTicker.Unregister(this);

    public void AutomationTick(float step)
    {
        if (!IsPlaced) return;
        Tick(step);
        RefreshView();
    }

    protected abstract void Tick(float step);
    protected abstract string Status();

    // --- IItemPort ---
    public virtual bool CanTake(out ItemSO item)
    {
        item = outputItem;
        return IsPlaced && outputCount > 0 && outputItem != null;
    }

    public virtual ItemSO Take()
    {
        if (outputCount <= 0) return null;
        outputCount--;
        ItemSO taken = outputItem;
        RefreshView();
        return taken;
    }

    public virtual bool CanAccept(ItemSO item) => false;
    public virtual void Accept(ItemSO item) { }

    protected bool OutputHasRoom(ItemSO item, int count)
        => (outputCount == 0 || outputItem == item) && outputCount + count <= outputCapacity;

    protected void AddOutput(ItemSO item, int count)
    {
        outputItem = item;
        outputCount += count;
    }

    // --- Görünüm ---
    private void CreateLabel()
    {
        var go = new GameObject("Label");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * labelHeight;
        label = go.AddComponent<TextMeshPro>();
        label.fontSize = 2.2f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.outlineWidth = 0.25f;
        label.outlineColor = Color.black;
        label.rectTransform.sizeDelta = new Vector2(3f, 1f);
    }

    private void CreateStack()
    {
        for (int i = 0; i < VisibleStack; i++)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(cube.GetComponent<Collider>());
            cube.name = "Output " + i;
            cube.transform.SetParent(transform, false);
            cube.transform.localScale = Vector3.one * 0.16f;
            cube.transform.localPosition = new Vector3((i % 2 - 0.5f) * 0.2f, stackHeight + (i / 2) * 0.18f, 0.25f);
            Renderer renderer = cube.GetComponent<Renderer>();
            renderer.enabled = false;
            stackCubes.Add(renderer);
        }
    }

    protected void RefreshView()
    {
        if (label != null) label.text = Status();
        for (int i = 0; i < stackCubes.Count; i++)
        {
            bool show = outputItem != null && i < outputCount;
            stackCubes[i].enabled = show;
            if (show) stackCubes[i].sharedMaterial = MaterialFor(outputItem, stackCubes[i].sharedMaterial);
        }
    }

    // Parça rengi için malzeme (parça başına bir kez, ilk küpün malzemesinden kopya)
    public static Material MaterialFor(ItemSO item, Material template)
    {
        if (itemMaterials.TryGetValue(item, out Material material) && material != null) return material;
        material = new Material(template) { name = item.Name + " (item)" };
        material.color = item.Color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", item.Color);
        itemMaterials[item] = material;
        return material;
    }

    protected virtual void LateUpdate()
    {
        if (label == null || Camera.main == null) return;
        label.transform.rotation = Camera.main.transform.rotation; // yazı hep kameraya baksın
    }
}
