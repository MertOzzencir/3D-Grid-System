using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    [SerializeField] private int HeightDebugDistance;
    [SerializeField] private bool showBaseGizmos = true;
    public static GridManager Instance;
    public Vector3 GridSize;
    public Dictionary<Vector3Int, GridData> Grids = new Dictionary<Vector3Int, GridData>();

    private GridSaveManager saveManager;

    private Vector3Int ManagerPosition => Vector3Int.RoundToInt(transform.position);

    private void Awake()
    {
        saveManager = GetComponent<GridSaveManager>();
        if (Instance == null)
            Instance = this;

        CreateGridData();
        CreateSavedGridEntities();
    }
    public bool CanPlaceBase(GridBase entity, Vector3 position) => CanPlaceGeneric(entity, position, d => d.Base);
    public bool PlaceBase(GridBase entity, Vector3 position) => PlaceGeneric(entity, position, d => d.Base, (d, e) => d.Base = e);
    public bool RemoveBase(GridBase entity, bool destroy = true) => RemoveGeneric(entity, d => d.Base, d => d.Base = null, destroy);

    public bool CanPlaceablePlaceOn(GridPlaceable entity, Vector3 position) => CanPlaceGeneric(entity, position, d => d.Placeable);
    public bool PlaceablePlaceOn(GridPlaceable entity, Vector3 position) => PlaceGeneric(entity, position, d => d.Placeable, (d, e) => d.Placeable = e);
    public bool PlaceableRemoveOn(GridPlaceable entity, bool destroy = true) => RemoveGeneric(entity, d => d.Placeable, d => d.Placeable = null, destroy);

    // Grid'deki her placeable bir kez (çok hücreli objeler tekrar etmez)
    public List<GridPlaceable> GetAllPlaceables()
    {
        var seen = new HashSet<GridPlaceable>();
        foreach (GridData data in Grids.Values)
            if (data.Placeable != null) seen.Add(data.Placeable);
        return new List<GridPlaceable>(seen);
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
    public void CreateSavedGridEntities()
    {
        SaveData[] savedGrids = saveManager.SavedData().ToArray();
        foreach (var a in savedGrids)
        {
            GridEntity prefab = Instantiate(a.Prefab);
            PlaceBase(prefab as GridBase, a.SavedData.WorldPosition);
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
        {
            GridData data = Grids[origin + offset];
            slotSetter(data, entity);
            if (offset == Vector3Int.zero) data.IsOrigin = true;
        }

        // Pivot origin hücresinde durur; model de footprint ile aynı rotasyonda olur
        Vector3Int originWorldPos = ManagerPosition + origin;
        entity.transform.SetPositionAndRotation(originWorldPos, entity.GridRotation);
        entity.OnPlaced(originWorldPos);
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
            data.IsOrigin = false;
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
    public void LayerDebugDistance(int i)
    {
        HeightDebugDistance += i;
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
    public bool IsOrigin;
    public GridData(Vector3Int WorldPos)
    {
        WorldPosition = WorldPos;
    }
}
