using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public abstract class GridEntitySO<TPrefab> : GridEntitySOBase where TPrefab : GridEntity
{
    public TPrefab Prefab;
    public override GridEntity GetPrefabBase() => Prefab;
}

[Serializable]
public class MaskLayer
{
    public bool[] cells;
}

public abstract class GridEntitySOBase : ScriptableObject
{
    public string Name;
    public Sprite Icon;

    [Tooltip("Kayıt dosyasındaki kimliği. Boşsa asset adı kullanılır. Oyun yayınlandıktan sonra değiştirme: eski kayıtlar bu objeyi bulamaz.")]
    [SerializeField] private string saveId;
    public string SaveId => string.IsNullOrEmpty(saveId) ? name : saveId;

    [Tooltip("x = genislik, y = yukseklik (katman sayisi), z = derinlik")]
    [SerializeField] private Vector3Int size = Vector3Int.one;

    [Tooltip("Layers[0] = zemine degen katman, Layers[1] = bir ustu ...")]
    [SerializeField] private List<MaskLayer> layers = new List<MaskLayer>();

    [SerializeField, HideInInspector] private Vector3Int layoutSize;

    [SerializeField, HideInInspector, FormerlySerializedAs("Size")] private Vector2 legacySize;
    [SerializeField, HideInInspector, FormerlySerializedAs("mask")] private bool[] legacyMask;

    // Her rotasyon için bir kez hesaplanır (index = (int)Rotation)
    [NonSerialized] private GridFootprint[] cachedFootprints;

    public Vector3Int Size => size;
    public abstract GridEntity GetPrefabBase();

    public GridFootprint GetFootprint(GridMaskRotator.Rotation rotation = GridMaskRotator.Rotation.Deg0)
    {
        if (cachedFootprints == null)
        {
            EnsureLayout();
            cachedFootprints = GridMaskRotator.AllRotations(BuildFootprint());
        }
        return cachedFootprints[(int)rotation];
    }

    // Katmanlardaki dolu hücreleri pivot'a (0,0,0) göre offset listesine çevirir
    private GridFootprint BuildFootprint()
    {
        var cells = new List<Vector3Int>();
        for (int y = 0; y < size.y; y++)
            for (int z = 0; z < size.z; z++)
                for (int x = 0; x < size.x; x++)
                    if (layers[y].cells[z * size.x + x])
                        cells.Add(new Vector3Int(x, y, z));

        return new GridFootprint(cells.ToArray());
    }

    private void OnValidate()
    {
        EnsureLayout();
        cachedFootprints = null;
    }

    // Size değişince katmanları yeniden boyutlandırır. Eski hücreler koordinatına göre korunur, yeni hücreler dolu başlar.
    public void EnsureLayout()
    {
        MigrateLegacy();
        size = Vector3Int.Max(size, Vector3Int.one);
        if (layoutSize == size && LayersMatchSize()) return;

        List<MaskLayer> oldLayers = layers;
        Vector3Int oldSize = layoutSize;
        layers = new List<MaskLayer>();

        for (int y = 0; y < size.y; y++)
        {
            var layer = new MaskLayer { cells = new bool[size.x * size.z] };
            for (int z = 0; z < size.z; z++)
                for (int x = 0; x < size.x; x++)
                    layer.cells[z * size.x + x] = TryGetOldCell(oldLayers, oldSize, x, y, z, out bool value) ? value : true;
            layers.Add(layer);
        }

        layoutSize = size;
        cachedFootprints = null;
    }

    private bool LayersMatchSize()
    {
        if (layers == null || layers.Count != size.y) return false;
        foreach (MaskLayer layer in layers)
            if (layer?.cells == null || layer.cells.Length != size.x * size.z) return false;
        return true;
    }

    private static bool TryGetOldCell(List<MaskLayer> oldLayers, Vector3Int oldSize, int x, int y, int z, out bool value)
    {
        value = false;
        if (oldLayers == null || y >= oldLayers.Count || x >= oldSize.x || z >= oldSize.z) return false;

        bool[] cells = oldLayers[y]?.cells;
        int index = z * oldSize.x + x;
        if (cells == null || index >= cells.Length) return false;

        value = cells[index];
        return true;
    }

    // Eski assetler: Size(x, y) + mask  →  size(x, 1, y) + Katman 0
    private void MigrateLegacy()
    {
        if (legacyMask == null || legacyMask.Length == 0) return;

        if (layers == null || layers.Count == 0)
        {
            size = new Vector3Int(Mathf.Max(1, (int)legacySize.x), 1, Mathf.Max(1, (int)legacySize.y));
            layers = new List<MaskLayer> { new MaskLayer { cells = legacyMask } };
            layoutSize = size;
        }
        legacyMask = null;
    }
}
