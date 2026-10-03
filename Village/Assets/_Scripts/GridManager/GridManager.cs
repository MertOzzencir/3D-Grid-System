using System;
using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    [SerializeField] private int HeightDebugDistance;
    [SerializeField] private bool showBaseGizmos = true;
    public static GridManager Instance;
    public Vector3 GridSize;
    public Dictionary<Vector3Int, GridData> Grids = new Dictionary<Vector3Int, GridData>();

    private Vector3Int ManagerPosition => Vector3Int.RoundToInt(transform.position);

    // Kayıt yüklemesi burada değil, SaveManager.Start'ta: grid Awake'te hazır olur
    private void Awake()
    {
        if (Instance == null)
            Instance = this;

        CreateGridData();
    }
    // Base eklenip silinince (örn. ada eteği yeniden kurulsun diye)
    public event Action BasesChanged;

    // Bir obje grid'e konunca (build menüsü, sürükleyip bırakma, ağaçtan düşen odun...): yerleştirme cilası dinler.
    // Kayıttan yüklerken susturulur (SaveManager), dünya açılırken her şey zıplamasın.
    // Yürüyenlerin (canlı, bot) adım adım yer değiştirmesi (TryMovePlaceable) bu olayı tetiklemez.
    public event Action<GridEntity> EntityPlaced;
    public bool SuppressPlacedEvents { get; set; }

    public bool CanPlaceBase(GridBase entity, Vector3 position) => CanPlaceGeneric(entity, position, d => d.Base);

    public bool PlaceBase(GridBase entity, Vector3 position)
    {
        bool placed = PlaceGeneric(entity, position, d => d.Base, (d, e) => d.Base = e);
        if (placed) BasesChanged?.Invoke();
        return placed;
    }

    public bool RemoveBase(GridBase entity, bool destroy = true)
    {
        bool removed = RemoveGeneric(entity, d => d.Base, d => d.Base = null, destroy);
        if (removed) BasesChanged?.Invoke();
        return removed;
    }

    public bool CanPlaceablePlaceOn(GridPlaceable entity, Vector3 position)
        => CanPlaceGeneric(entity, position, d => d.Placeable) && PlaceableAllowsCells(entity, position);
    public bool PlaceablePlaceOn(GridPlaceable entity, Vector3 position)
        => PlaceableAllowsCells(entity, position) && PlaceGeneric(entity, position, d => d.Placeable, (d, e) => d.Placeable = e);

    // Su: en alt kattaki base'siz hücre. Ayrıca tutulmaz/kaydedilmez; base konup silindikçe kendiliğinden değişir.
    // (İleride başka katlarda su gerekirse ayrı bir parametreyle.)
    public bool IsWater(Vector3Int worldCell)
        => worldCell.y == ManagerPosition.y && TryGetCell(worldCell, out GridData data) && data.Base == null;

    // Sütundaki (dünya x, z) en üstteki base. Yoksa false.
    public bool TryGetTopBase(int worldX, int worldZ, out GridData top)
    {
        Vector3Int manager = ManagerPosition;
        for (int y = Mathf.RoundToInt(GridSize.y) - 1; y >= 0; y--)
        {
            if (Grids.TryGetValue(new Vector3Int(worldX - manager.x, y, worldZ - manager.z), out top) && top.Base != null)
                return true;
        }
        top = null;
        return false;
    }

    // Türe özel hücre kuralı (GridPlaceable.CanOccupy): örn. placeable'lar suya, bot karaya konmaz
    private bool PlaceableAllowsCells(GridPlaceable entity, Vector3 position)
    {
        Vector3Int origin = ManagerPosition + GetIndexFromWorldPosition(position);
        foreach (Vector3Int offset in entity.GetFootprint().FilledCells())
            if (!entity.CanOccupy(origin + offset)) return false;
        return true;
    }
    public bool PlaceableRemoveOn(GridPlaceable entity, bool destroy = true) => RemoveGeneric(entity, d => d.Placeable, d => d.Placeable = null, destroy);

    // Yürüyen placeable'lar (canlılar): eski hücreleri bırakıp yenilerini kaplar, transform'a dokunmaz.
    // Yeni hücrelerden biri grid dışındaysa ya da başka bir placeable'la doluysa hiçbir şey değişmez (false).
    public bool TryMovePlaceable(GridPlaceable entity, Vector3Int originWorld, GridFootprint footprint)
    {
        Vector3Int origin = GetIndexFromWorldPosition(originWorld);
        foreach (Vector3Int offset in footprint.FilledCells())
        {
            if (!Grids.TryGetValue(origin + offset, out GridData data)) return false;
            if (data.Placeable != null && data.Placeable != entity) return false;
            if (!entity.CanOccupy(originWorld + offset)) return false;
        }

        if (entity.PlacedFootprint != null)
        {
            Vector3Int oldOrigin = GetIndexFromWorldPosition(entity.OriginWorldPosition);
            foreach (Vector3Int offset in entity.PlacedFootprint.FilledCells())
                if (Grids.TryGetValue(oldOrigin + offset, out GridData data) && data.Placeable == entity) data.Placeable = null;
        }

        foreach (Vector3Int offset in footprint.FilledCells())
            Grids[origin + offset].Placeable = entity;
        entity.SetPlacement(originWorld, footprint);
        return true;
    }

    // Grid'deki her obje bir kez (çok hücreli objeler tekrar etmez)
    public List<GridBase> GetAllBases() => GetPlacedEntities(d => d.Base);
    public List<GridPlaceable> GetAllPlaceables() => GetPlacedEntities(d => d.Placeable);

    private List<T> GetPlacedEntities<T>(Func<GridData, T> slotGetter) where T : GridEntity
    {
        var seen = new HashSet<T>();
        foreach (GridData data in Grids.Values)
        {
            T entity = slotGetter(data);
            if (entity != null) seen.Add(entity);
        }
        return new List<T>(seen);
    }

    private void CreateGridData()
    {
        for (int y = 0; y < GridSize.y; y++)
            for (int z = 0; z < GridSize.z; z++)
                for (int x = 0; x < GridSize.x; x++)
                {
                    Vector3Int index = new Vector3Int(x, y, z);
                    Grids[index] = new GridData(ManagerPosition + index);
                }
    }

    public bool CanPlaceGeneric<T>(T entity, Vector3 position, Func<GridData, object> slotGetter) where T : GridEntity
    {
        Vector3Int origin = GetIndexFromWorldPosition(position);

        foreach (Vector3Int offset in entity.GetFootprint().FilledCells())
        {
            if (!Grids.TryGetValue(origin + offset, out GridData data)) return false;
            if (slotGetter(data) != null) return false;
        }
        return true;
    }
    public bool PlaceGeneric<T>(T entity, Vector3 position, Func<GridData, object> slotGetter, Action<GridData, T> slotSetter) where T : GridEntity
    {
        if (!CanPlaceGeneric(entity, position, slotGetter))
            return false;

        Vector3Int origin = GetIndexFromWorldPosition(position);

        foreach (Vector3Int offset in entity.GetFootprint().FilledCells())
            slotSetter(Grids[origin + offset], entity);

        // Pivot origin hücresinde durur; model de footprint ile aynı rotasyonda olur
        Vector3Int originWorldPos = ManagerPosition + origin;
        entity.transform.SetPositionAndRotation(originWorldPos, entity.GridRotation);
        entity.OnPlaced(originWorldPos);
        if (!SuppressPlacedEvents) EntityPlaced?.Invoke(entity);
        return true;
    }
    public bool RemoveGeneric<T>(T entity, Func<GridData, object> slotGetter, Action<GridData> slotClearer, bool destroyObject = true) where T : GridEntity
    {
        if (entity == null || entity.PlacedFootprint == null) return false;

        Vector3Int origin = GetIndexFromWorldPosition(entity.OriginWorldPosition);
        bool removedAny = false;

        foreach (Vector3Int offset in entity.PlacedFootprint.FilledCells())
        {
            if (!Grids.TryGetValue(origin + offset, out GridData data)) continue;
            if (!Equals(slotGetter(data), entity)) continue;

            slotClearer(data);
            removedAny = true;
        }

        if (removedAny && destroyObject)
            Destroy(entity.gameObject);

        return removedAny;
    }
    // Entity'nin zemin katmanında, etrafındaki (ve altındaki) ilk boş hücreyi bulur
    public Vector3 GetEmptyGridFromEntityPosition(GridEntity entity, out bool success)
    {
        GridFootprint footprint = entity.PlacedFootprint;
        Vector3Int origin = GetIndexFromWorldPosition(entity.OriginWorldPosition);

        for (int z = footprint.Min.z - 1; z <= footprint.Max.z + 1; z++)
        {
            for (int x = footprint.Min.x - 1; x <= footprint.Max.x + 1; x++)
            {
                if (!Grids.TryGetValue(origin + new Vector3Int(x, 0, z), out GridData value)) continue;
                if (value.Placeable != null || value.Base != null) continue;

                success = true;
                return value.WorldPosition;
            }
        }

        success = false;
        return Vector3.zero;
    }

    private Vector3Int GetIndexFromWorldPosition(Vector3 worldPosition)
        => Vector3Int.RoundToInt(worldPosition - transform.position);

    // Dünya hücresinin verisi (grid dışındaysa false). Canlılar yol bulurken kullanır.
    public bool TryGetCell(Vector3Int worldCell, out GridData data)
        => Grids.TryGetValue(worldCell - ManagerPosition, out data);
    // Hücre grid içinde mi ve placeable slotu boş mu
    public bool IsPlaceableCellFree(Vector3 worldPosition)
        => Grids.TryGetValue(GetIndexFromWorldPosition(worldPosition), out GridData data) && data.Placeable == null;

    public Vector3 GetGridWorldPosition(Vector3 worldPosition, out bool success)
    {
        Vector3Int index = GetIndexFromWorldPosition(worldPosition);
        if (Grids.TryGetValue(index, out GridData data))
        {
            success = true;
            return data.WorldPosition;
        }

        success = false;
        return Vector3.zero;
    }
    public Dictionary<Vector3Int, GridData> GetGridData()
    {
        return Grids;
    }
    // Seçili kat (dünya y'si): gizmolar bu kata kadar çizilir, base yerleştirme de bu kattaki yatay düzleme yapılır.
    // Ekrandaki ▲ ▼ butonları değiştirir; grid'in yükseklik sınırları içinde kalır.
    public int LayerLevel => HeightDebugDistance;

    public void LayerDebugDistance(int i)
    {
        int bottom = ManagerPosition.y;
        int top = ManagerPosition.y + Mathf.Max(0, Mathf.RoundToInt(GridSize.y) - 1);
        HeightDebugDistance = Mathf.Clamp(HeightDebugDistance + i, bottom, top);
    }
    public void ResetLayerDebugDistance()
    {
        HeightDebugDistance = 0;
    }
    private void OnDrawGizmos()
    {
        if (Grids == null) return;

        foreach (var kvp in Grids)
        {
            if (kvp.Value.WorldPosition.y > HeightDebugDistance) continue;

            Vector3 worldPos = transform.position + new Vector3(kvp.Key.x, kvp.Key.y, kvp.Key.z);

            if (showBaseGizmos)
            {
                Gizmos.color = kvp.Value.Base == null ? Color.red : Color.green;
                Gizmos.DrawCube(worldPos, Vector3.one * 0.9f);
            }

            if (kvp.Value.Placeable != null)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawCube(worldPos + Vector3.up * 0.3f, Vector3.one * 0.4f);
            }
        }
    }
}

[Serializable]
public class GridData
{
    public GridBase Base;
    public GridPlaceable Placeable;
    public Vector3Int WorldPosition;
    public GridData(Vector3Int WorldPos)
    {
        WorldPosition = WorldPos;
    }
}
