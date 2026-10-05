using System.Collections.Generic;
using UnityEngine;

// Botun tarla grid'i: botun arkasından gelen tek sayılı kare (size × size), tek kat. Dünya grid'i bütün tarlayı botun
// placeable'ı olarak görür; içeride hangi hücrede hangi parça var, bunu bu sınıf tutar (tarlanın yerel koordinatı:
// x, z 0..size-1). Kök = güvertenin üst yüzünün ortası (Boat kurar ve her kare yerini yazar); parçalar onun child'ı.
// Tarla parçası (FarmPiece) tarlanın üstüne getirilince IToolTarget olarak yerini gösterir; bırakılınca oturur.
// Ekin: hücredeki parçanın o hücresinde durur (FarmPiece tutar, parça kaydırılınca ekin de gider). Tohum (Seed)
// tarlanın üstüne getirilince hücrenin üstünde süzülür; sol tık (basılı tutup sürükleyerek de) ekilebilir hücrelere eker.
// Eldiven tarlanın üstünde base gibi yürür (IGloveWalkable; bot geçirgen kalır).
public class FarmGrid : MonoBehaviour, IToolTarget, IGloveWalkable
{
    // Şu an mouse'un altında, elde tutulan parçaya yer gösteren tarla (FarmPiece bırakılırken sorar)
    public static FarmGrid Hovered { get; private set; }

    private int size;
    private FarmPiece[,] cells;
    private readonly List<FarmPiece> pieces = new List<FarmPiece>();

    // Elde tutulan parça için gösterilen yer
    private bool pendingValid;
    private Vector2Int pendingCell;
    private Quaternion pendingRotation = Quaternion.identity;

    public int Size => size;
    public IReadOnlyList<FarmPiece> Pieces => pieces;

    public void Setup(int farmSize)
    {
        size = Mathf.Max(1, farmSize);
        cells = new FarmPiece[size, size];
    }

    private int Radius => size / 2;

    // Tohumun ekilecek hücrenin üstünde süzüldüğü yükseklik (pivot'tan)
    private const float SeedHoverHeight = 0.6f;

    private bool InBounds(Vector2Int c) => cells != null && c.x >= 0 && c.y >= 0 && c.x < size && c.y < size;

    // Hücrenin (pivot) güverte üstündeki yerel konumu; parçanın pivot'u hücrenin ortasında, tabanı güvertede (+0.5)
    private Vector3 CellLocal(Vector2Int cell) => new Vector3(cell.x - Radius, 0.5f, cell.y - Radius);

    // Parçanın bu hücrede (pivot) ve şu anki rotasyonuyla kaplayacağı hücreler; tarla dışı ya da dolu ise false
    public bool Fits(FarmPiece piece, Vector2Int cell, List<Vector2Int> result = null)
    {
        result?.Clear();
        foreach (Vector3Int offset in piece.GetFootprint().FilledCells())
        {
            if (offset.y != 0) return false; // tarla tek kat: üst üste yok
            var c = new Vector2Int(cell.x + offset.x, cell.y + offset.z);
            if (c.x < 0 || c.y < 0 || c.x >= size || c.y >= size) return false;
            if (cells[c.x, c.y] != null && cells[c.x, c.y] != piece) return false;
            result?.Add(c);
        }
        return true;
    }

    private static readonly List<Vector2Int> scratch = new List<Vector2Int>();

    public bool Attach(FarmPiece piece, Vector2Int cell)
    {
        if (!Fits(piece, cell, scratch)) return false;
        foreach (Vector2Int c in scratch) cells[c.x, c.y] = piece;
        if (!pieces.Contains(piece)) pieces.Add(piece);

        piece.transform.SetParent(transform, false);
        piece.transform.SetLocalPositionAndRotation(CellLocal(cell), piece.GridRotation);
        piece.OnAttachedToFarm(this, cell);
        return true;
    }

    // Hücredeki parça ve parçanın o hücresi (döndürmesiz yerel offset); parça yoksa false
    private bool TryGetPlot(Vector2Int cell, out FarmPiece piece, out Vector2Int tile)
    {
        tile = default;
        piece = InBounds(cell) ? cells[cell.x, cell.y] : null;
        return piece != null && piece.TryGetTileAt(transform.TransformPoint(CellLocal(cell)), out tile);
    }

    // Ekilebilir: hücrede tarla parçası var ve o hücre boş
    public bool CanPlant(Vector2Int cell) => TryGetPlot(cell, out FarmPiece piece, out Vector2Int tile) && !piece.HasCrop(tile);

    public bool TryPlant(CropSO crop, Vector2Int cell) =>
        TryGetPlot(cell, out FarmPiece piece, out Vector2Int tile) && piece.Plant(crop, tile);

    public void Detach(FarmPiece piece)
    {
        for (int x = 0; x < size; x++)
            for (int z = 0; z < size; z++)
                if (cells[x, z] == piece) cells[x, z] = null;
        pieces.Remove(piece);
        piece.OnDetachedFromFarm();
    }

    // Elde tutulan parça bırakıldı: gösterilen yere oturt
    public bool TryAttachHovered(FarmPiece piece)
    {
        if (Hovered != this || !pendingValid) return false;
        bool attached = Attach(piece, pendingCell);
        ClearHover();
        return attached;
    }

    // Mouse'un güverte üstündeki noktası → tarlanın yerel hücresi
    private bool TryGetMouseCell(out Vector2Int cell)
    {
        cell = default;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        var plane = new Plane(transform.up, transform.position);
        if (!plane.Raycast(ray, out float enter)) return false;
        Vector3 local = transform.InverseTransformPoint(ray.GetPoint(enter));
        cell = new Vector2Int(Mathf.RoundToInt(local.x + Radius), Mathf.RoundToInt(local.z + Radius));
        return true;
    }

    // --- IToolTarget: elde parça ya da tohum varken mouse tarlanın üstünde ---

    public Vector3 GetToolTargetPosition(IInteractable interacted, out bool accept)
    {
        accept = false;
        pendingValid = false;
        if (cells == null || !TryGetMouseCell(out Vector2Int cell)) return Vector3.zero;

        // Tohum: tarlanın her hücresinde üstte süzülür (ekilemeyen hücrede de, tarladan düşmesin); ekilebilirse pendingValid
        if (interacted is Seed seed)
        {
            if (!InBounds(cell)) return Vector3.zero;
            pendingCell = cell;
            pendingValid = CanPlant(cell);
            pendingRotation = transform.rotation * seed.GridRotation;
            accept = true;
            return transform.TransformPoint(CellLocal(cell)) + transform.up * SeedHoverHeight;
        }

        if (!(interacted is FarmPiece piece)) return Vector3.zero;
        Hovered = this;
        if (!Fits(piece, cell)) return Vector3.zero;

        pendingValid = true;
        pendingCell = cell;
        pendingRotation = transform.rotation * piece.GridRotation;
        accept = true;
        return transform.TransformPoint(CellLocal(cell));
    }

    public Quaternion GetToolTargetRotation() => pendingRotation;

    // Sol tık. Parça: oturt (elde tutma biter). Tohum: gösterilen hücreye ek (tohum bitene kadar elde kalır).
    public bool OnToolUsed(IInteractable tool)
    {
        if (tool is Seed seed)
        {
            if (!pendingValid || !TryPlant(seed.Crop, pendingCell)) return false;
            pendingValid = false; // aynı karede ikinci kez ekilmesin
            seed.OnPlanted();
            return true;
        }

        if (!(tool is FarmPiece piece) || !TryAttachHovered(piece)) return false;
        InteractableController.Instance?.Release(piece);
        return true;
    }

    public void OnToolTargetExit() => ClearHover();

    private void ClearHover()
    {
        pendingValid = false;
        if (Hovered == this) Hovered = null;
    }

    private void OnDisable() => ClearHover();
}
