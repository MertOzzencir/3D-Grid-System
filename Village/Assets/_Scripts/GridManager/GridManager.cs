using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    [SerializeField] private int HeightDebugDistance;
    public static GridManager Instance;
    public Vector3 GridSize;
    public Dictionary<Vector3Int, GridData> Grids = new Dictionary<Vector3Int, GridData>();

    private GridSaveManager saveManager;
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
    private void CreateGridData()
    {
        for (int y = 0; y < GridSize.y; y++)
        {
            for (int z = 0; z < GridSize.z; z++)
            {
                for (int x = 0; x < GridSize.x; x++)
                {
                    Vector3Int managerPos = new Vector3Int(
    Mathf.RoundToInt(transform.position.x),
    Mathf.RoundToInt(transform.position.y),
    Mathf.RoundToInt(transform.position.z)
);
                    Grids[new Vector3Int(x, y, z)] = new GridData(managerPos + new Vector3Int(x, y, z));
                }
            }
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
        var (size, mask) = entity.GetFootprint();
        Vector3Int origin = GetIndexFromWorldPosition(position);
        int width = Mathf.Max(1, (int)size.x);
        int depth = Mathf.Max(1, (int)size.y);

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                if (mask != null && !mask[z * width + x]) continue;

                Vector3Int cell = new Vector3Int(origin.x + x, origin.y, origin.z + z);
                if (!Grids.TryGetValue(cell, out GridData data)) return false;
                if (slotGetter(data) != null) return false;
            }
        }
        return true;
    }
    public bool PlaceGeneric<T>(T entity, Vector3 position, Func<GridData, object> slotGetter, Action<GridData, T> slotSetter) where T : GridEntity
    {
        if (!CanPlaceGeneric(entity, position, slotGetter))
            return false;

        var (size, mask) = entity.GetFootprint();
        Vector3Int origin = GetIndexFromWorldPosition(position);
        int width = Mathf.Max(1, (int)size.x);
        int depth = Mathf.Max(1, (int)size.y);

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                if (mask != null && !mask[z * width + x]) continue;

                Vector3Int cell = new Vector3Int(origin.x + x, origin.y, origin.z + z);
                slotSetter(Grids[cell], entity);
                if (x == 0 && z == 0) Grids[cell].IsOrigin = true;
            }
        }

        Vector3Int originWorldPos = Grids[origin].WorldPosition;
        entity.transform.position = originWorldPos;
        entity.OnPlaced(originWorldPos);
        return true;
    }
    public bool RemoveGeneric<T>(T entity, Func<GridData, object> slotGetter, Action<GridData> slotClearer, bool destroyObject = true) where T : GridEntity
    {
        if (entity == null) return false;

        Vector2 size = entity.PlacedSize;
        bool[] mask = entity.PlacedMask;
        Vector3Int origin = GetIndexFromWorldPosition(entity.OriginWorldPosition);
        int width = Mathf.Max(1, (int)size.x);
        int depth = Mathf.Max(1, (int)size.y);

        bool removedAny = false;
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                if (mask != null && !mask[z * width + x]) continue;

                Vector3Int cell = new Vector3Int(origin.x + x, origin.y, origin.z + z);
                if (!Grids.TryGetValue(cell, out GridData data)) continue;

                if (Equals(slotGetter(data), entity))
                {
                    slotClearer(data);
                    data.IsOrigin = false;
                    removedAny = true;
                }
            }
        }

        if (removedAny && destroyObject)
            Destroy(entity.gameObject);

        return removedAny;
    }
    public Vector3 GetEmptyGridFromEntityPosition(GridEntity entity, out bool success)
    {
        Vector3 size = entity.GetData().Size;
        float x = size.x;
        float z = size.y;
        success = false;
        for (int zAxis = -1; zAxis < z + 1; zAxis++)
        {
            for (int xAxis = -1; xAxis < x + 1; xAxis++)
            {
                Vector3Int checkingPosition = entity.OriginWorldPosition + new Vector3Int(xAxis, 0, zAxis);
                GetGridData().TryGetValue(checkingPosition, out GridData value);
                if (value == null || value.Placeable != null || value.Base != null) continue;

                success = true;
                return value.WorldPosition;
            }
        }
        return Vector3.zero;
    }

    private Vector3Int GetIndexFromWorldPosition(Vector3 worldPosition)
    {
        Vector3 local = worldPosition - transform.position;
        int x = Mathf.RoundToInt(local.x);
        int y = Mathf.RoundToInt(local.y);
        int z = Mathf.RoundToInt(local.z);
        return new Vector3Int(x, y, z);
    }
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

            Gizmos.color = kvp.Value.Base == null ? Color.red : Color.green;
            Gizmos.DrawCube(worldPos, Vector3.one * 0.9f);

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
