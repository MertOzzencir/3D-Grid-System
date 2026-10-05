using System.Collections.Generic;
using UnityEngine;

// Botun tarla grid'i: botun arkasından gelen tek sayılı kare (size × size), tek kat. Dünya grid'i bütün tarlayı botun
// placeable'ı olarak görür; içeride hangi hücrede hangi parça var, bunu bu sınıf tutar (tarlanın yerel koordinatı:
// x, z 0..size-1). Kök = güvertenin üst yüzünün ortası (Boat kurar ve her kare yerini yazar); parçalar onun child'ı.
// Tarla parçası (FarmPiece) tarlanın üstüne getirilince IToolTarget olarak yerini gösterir; bırakılınca oturur.
// Ekinler sonra her hücrenin ikinci slotu olacak.
public class FarmGrid : MonoBehaviour, IToolTarget
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

    // --- IToolTarget: elde parça varken mouse tarlanın üstünde ---

    public Vector3 GetToolTargetPosition(IInteractable interacted, out bool accept)
    {
        accept = false;
        pendingValid = false;
        if (!(interacted is FarmPiece piece) || cells == null) return Vector3.zero;

        // Mouse'un güverte üstündeki noktası → tarlanın yerel hücresi (parçanın pivot hücresi)
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        var plane = new Plane(transform.up, transform.position);
        if (!plane.Raycast(ray, out float enter)) return Vector3.zero;
        Vector3 local = transform.InverseTransformPoint(ray.GetPoint(enter));
        var cell = new Vector2Int(Mathf.RoundToInt(local.x + Radius), Mathf.RoundToInt(local.z + Radius));

        Hovered = this;
        if (!Fits(piece, cell)) return Vector3.zero;

        pendingValid = true;
        pendingCell = cell;
        pendingRotation = transform.rotation * piece.GridRotation;
        accept = true;
        return transform.TransformPoint(CellLocal(cell));
    }

    public Quaternion GetToolTargetRotation() => pendingRotation;

    // Sol tık: oturt (elde tutma biter)
    public bool OnToolUsed(IInteractable tool)
    {
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
