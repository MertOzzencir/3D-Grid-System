using System;
using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    [SerializeField] private int HeightDebugDistance;
    [SerializeField] private bool showBaseGizmos = true;
    [Tooltip("İnce grid'i göster: ince objeli hücrelerin 8 küçük küpü (dolu = turkuaz, boş = tel kafes)")]
    [SerializeField] private bool showFineGizmos = true;
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

    public bool CanPlaceBase(GridBase entity, Vector3 position) => CanPlaceGeneric(entity, position, d => d.Base) && NoFineIn(entity, position);

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
    {
        if (entity is FinePlaceable fine) return fine.CanPlaceAt(position);
        return CanPlaceGeneric(entity, position, d => d.Placeable) && PlaceableAllowsCells(entity, position) && NoFineIn(entity, position);
    }

    public bool PlaceablePlaceOn(GridPlaceable entity, Vector3 position)
    {
        if (entity is FinePlaceable fine) return fine.PlaceAt(position);
        return PlaceableAllowsCells(entity, position) && NoFineIn(entity, position) &&
               PlaceGeneric(entity, position, d => d.Placeable, (d, e) => d.Placeable = e);
    }

    // Tam objenin hücrelerinde ince obje yok
    private bool NoFineIn(GridEntity entity, Vector3 position)
    {
        Vector3Int origin = GetIndexFromWorldPosition(position);
        foreach (Vector3Int offset in entity.GetFootprint().FilledCells())
            if (Grids.TryGetValue(origin + offset, out GridData data) && data.HasFine) return false;
        return true;
    }

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
    public bool PlaceableRemoveOn(GridPlaceable entity, bool destroy = true)
    {
        if (entity is FinePlaceable fine)
        {
            bool removed = fine.ReleaseCells();
            if (removed && destroy) Destroy(fine.gameObject);
            return removed;
        }
        return RemoveGeneric(entity, d => d.Placeable, d => d.Placeable = null, destroy);
    }

    // Yürüyen placeable'lar (canlılar): eski hücreleri bırakıp yenilerini kaplar, transform'a dokunmaz.
    // Yeni hücrelerden biri grid dışındaysa ya da başka bir placeable'la doluysa hiçbir şey değişmez (false).
    public bool TryMovePlaceable(GridPlaceable entity, Vector3Int originWorld, GridFootprint footprint)
    {
        Vector3Int origin = GetIndexFromWorldPosition(originWorld);
        foreach (Vector3Int offset in footprint.FilledCells())
        {
            if (!Grids.TryGetValue(origin + offset, out GridData data)) return false;
            if (data.Placeable != null && data.Placeable != entity) return false;
            if (data.HasFine) return false;
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
    public List<GridPlaceable> GetAllPlaceables()
    {
        List<GridPlaceable> list = GetPlacedEntities(d => d.Placeable);
        var seen = new HashSet<GridPlaceable>(list);
        foreach (GridData data in Grids.Values)
        {
            if (data.FineOccupants == null) continue;
            foreach (FineOccupant occupant in data.FineOccupants)
                if (occupant.Owner != null && seen.Add(occupant.Owner)) list.Add(occupant.Owner);
        }
        return list;
    }

    // ---------- İnce grid (hücre başına 2×2×2) ----------
    // İnce indeks: f = floor((dünya - grid kökü + 0.5) * 2); hücre = f / 2 (aşağı yuvarlanır), küçük küp = f - hücre * 2.
    // Küçük küpün ortası (dünya) = grid kökü + f * 0.5 - 0.25.

    public Vector3Int WorldToFine(Vector3 world)
    {
        Vector3 local = (world - transform.position + Vector3.one * 0.5f) * 2f;
        return new Vector3Int(Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.y), Mathf.FloorToInt(local.z));
    }

    public Vector3 FineCenterWorld(Vector3Int fine) => transform.position + (Vector3)fine * 0.5f - Vector3.one * 0.25f;

    private static int FloorDiv2(int value) => value >= 0 ? value / 2 : (value - 1) / 2;
    private static Vector3Int FineToCell(Vector3Int fine) => new Vector3Int(FloorDiv2(fine.x), FloorDiv2(fine.y), FloorDiv2(fine.z));
    private static byte FineBit(Vector3Int fine, Vector3Int cell)
    {
        Vector3Int sub = fine - cell * 2;
        return (byte)(1 << (sub.x + sub.z * 2 + sub.y * 4));
    }

    // Küçük küp dolu mu (base, tam obje ya da başka bir ince obje); ignore'un kendi küpleri boş sayılır
    public bool IsFineFilled(Vector3Int fine, FinePlaceable ignore = null)
    {
        Vector3Int cell = FineToCell(fine);
        if (!Grids.TryGetValue(cell, out GridData data)) return true; // grid dışı
        if (data.Base != null || data.Placeable != null) return true;
        if (!data.HasFine) return false;
        byte bit = FineBit(fine, cell);
        foreach (FineOccupant occupant in data.FineOccupants)
            if (occupant.Owner != ignore && (occupant.Mask & bit) != 0) return true;
        return false;
    }

    // Kutuya (min köşe, boy: küçük küp) ince obje konabilir mi: hepsi boş, su değil, alttan en az bir yerden destekli
    public bool CanFineOccupy(Vector3Int min, Vector3Int size, FinePlaceable ignore)
    {
        bool supported = false;
        for (int y = 0; y < size.y; y++)
            for (int z = 0; z < size.z; z++)
                for (int x = 0; x < size.x; x++)
                {
                    Vector3Int fine = min + new Vector3Int(x, y, z);
                    if (IsFineFilled(fine, ignore)) return false;
                    Vector3Int cell = FineToCell(fine);
                    if (IsWater(ManagerPosition + cell)) return false;
                    if (y == 0 && IsFineFilled(fine + Vector3Int.down, ignore)) supported = true;
                }
        return supported;
    }

    // Kutunun küçük küplerini ince objeye yaz; kapladığı hücreleri döndürür
    public List<Vector3Int> FineOccupy(FinePlaceable owner, Vector3Int min, Vector3Int size)
    {
        var masks = new Dictionary<Vector3Int, byte>();
        for (int y = 0; y < size.y; y++)
            for (int z = 0; z < size.z; z++)
                for (int x = 0; x < size.x; x++)
                {
                    Vector3Int fine = min + new Vector3Int(x, y, z);
                    Vector3Int cell = FineToCell(fine);
                    masks.TryGetValue(cell, out byte mask);
                    masks[cell] = (byte)(mask | FineBit(fine, cell));
                }
        var cells = new List<Vector3Int>();
        foreach (KeyValuePair<Vector3Int, byte> pair in masks)
        {
            if (!Grids.TryGetValue(pair.Key, out GridData data)) continue;
            data.FineOccupants ??= new List<FineOccupant>();
            data.FineOccupants.Add(new FineOccupant { Owner = owner, Mask = pair.Value });
            data.FineMask |= pair.Value;
            cells.Add(pair.Key);
        }
        return cells;
    }

    // İnce objenin verilen hücrelerdeki küplerini boşalt
    public void FineRelease(FinePlaceable owner, List<Vector3Int> cells)
    {
        foreach (Vector3Int cell in cells)
        {
            if (!Grids.TryGetValue(cell, out GridData data) || data.FineOccupants == null) continue;
            data.FineOccupants.RemoveAll(o => o.Owner == owner);
            byte mask = 0;
            foreach (FineOccupant occupant in data.FineOccupants) mask |= occupant.Mask;
            data.FineMask = mask;
        }
    }

    // Grid indeksi (kayıt için): hücrenin dünya konumu
    public Vector3Int FineCellWorld(Vector3Int fine) => ManagerPosition + FineToCell(fine);

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

            // Tam obje: bütün hücre (yarı saydam mavi)
            if (kvp.Value.Placeable != null)
            {
                Gizmos.color = new Color(0.2f, 0.4f, 1f, 0.35f);
                Gizmos.DrawCube(worldPos, Vector3.one * 0.95f);
            }

            // İnce grid (2×2×2): dolu küçük küpler turkuaz dolu, boşlar ince tel kafes
            if (showFineGizmos && kvp.Value.HasFine)
                for (int bit = 0; bit < 8; bit++)
                {
                    var sub = new Vector3((bit & 1) * 0.5f - 0.25f, ((bit >> 2) & 1) * 0.5f - 0.25f, ((bit >> 1) & 1) * 0.5f - 0.25f);
                    bool filled = (kvp.Value.FineMask & (1 << bit)) != 0;
                    Gizmos.color = filled ? new Color(0.1f, 0.8f, 0.75f, 0.55f) : new Color(0.1f, 0.8f, 0.75f, 0.35f);
                    if (filled) Gizmos.DrawCube(worldPos + sub, Vector3.one * 0.45f);
                    else Gizmos.DrawWireCube(worldPos + sub, Vector3.one * 0.45f);
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

    // İnce grid: hücre 2×2×2 küçük küpe bölünür; bit = (x + z*2 + y*4). Doluysa ince objeler var (FinePlaceable).
    // Bir hücrede ya tam obje (Placeable) ya ince objeler olur, ikisi birden değil.
    [NonSerialized] public byte FineMask;
    [NonSerialized] public List<FineOccupant> FineOccupants;
    public bool HasFine => FineMask != 0;
    public GridData(Vector3Int WorldPos)
    {
        WorldPosition = WorldPos;
    }
}

// Bir ince objenin bir hücrede kapladığı küçük küpler
public struct FineOccupant
{
    public FinePlaceable Owner;
    public byte Mask;
}
