using System.Collections.Generic;
using UnityEngine;

// MergedWood'un bir ek yeri: ana odunun iki parçası arası (IsBase, Index = üstteki parçanın sırası, aşağıdan 1..) ya da
// bir yan odunun ana oduna bağlandığı yer (Index 0) / o yan odunun iki parçası arası (Index = dıştaki parçanın sırası, içten 1..)
public readonly struct WoodJoint
{
    public readonly bool IsBase;
    public readonly WoodSide Side;
    public readonly int Layer;
    public readonly int Index;

    public WoodJoint(bool isBase, WoodSide side, int layer, int index)
    {
        IsBase = isBase;
        Side = side;
        Layer = layer;
        Index = index;
    }

    // Kare kare seçimi korumak için tek sayı
    public int Id => IsBase ? Index : (((int)Side + 1) * 64 + Layer) * 64 + Index;
}

// MergedWood'a balta getirilince ek yerlerini gösterir (grid göstergesiyle, birleştirmedeki gibi), WASD ile seçtirir,
// vuruşta WoodMerger.Detach ile söker. Ek yerleri: ana odunun parçaları arası + kameraya göre sol ve sağ yandaki yan
// odunların bağlandığı yer ve kendi parçaları arası (ön ve arka yan için kamera Q / E ile çevrilir).
// Başlangıç en üstteki ek yeri; mouse yapıştığı andaki yerinden TargetReleasePixels uzaklaşınca bırakılır.
public class WoodDetachTarget : IWoodSlotView
{
    private struct Entry
    {
        public WoodJoint Joint;
        public int Column;      // yan (int), ana odun -1
        public int Order;       // sütun içinde aşağıdan yukarı (yanda: kat, aynı katta içten dışa)
        public float Height;    // ek yerinin yüksekliği (yerel y)
        public int Row;         // yanda kat (yükseklik); ana odunda kullanılmaz
        public Vector3 Position; // ek yerinin ortası (dünya)
    }

    private readonly MergedWood owner;
    private readonly List<Entry> entries = new List<Entry>();
    private readonly List<Vector3Int[]> candidateCells = new List<Vector3Int[]>();
    private readonly List<Vector3Int> candidateDirections = new List<Vector3Int>();

    private int selected = -1;
    private int lockedId = -1;
    private int lastSideColumn = -1; // en son bulunulan yan (ana odundan S ile oraya inilir)
    private Vector3 attachMouse;
    private Vector2 lastInput;

    // Baltanın şu an üstünde durduğu (ek yeri seçili) odun
    public static WoodDetachTarget Active { get; private set; }

    public IReadOnlyList<Vector3Int[]> CandidateCells => candidateCells;
    public IReadOnlyList<Vector3Int> CandidateDirections => candidateDirections;
    public int SelectedIndex => selected;
    public bool IsAlive => owner != null; // sahibi sökmede silinmiş olabilir
    public bool HasSelection => lockedId >= 0 && selected >= 0 && selected < entries.Count;
    public Vector3 FocusPoint => (Vector3)owner.OriginWorldPosition + Vector3.up * ((owner.BaseLength - 1) * 0.5f);

    public int CandidatesKey
    {
        get
        {
            int key = selected + 23;
            foreach (Vector3Int[] cells in candidateCells)
                foreach (Vector3Int cell in cells)
                    key = key * 31 + cell.GetHashCode();
            return key;
        }
    }

    public WoodDetachTarget(MergedWood owner) => this.owner = owner;

    // Sadece balta; ek yeri yoksa kabul edilmez
    public Vector3 GetToolTargetPosition(IInteractable interacted, out bool accept)
    {
        accept = false;
        if (!(interacted is Axe)) return default;

        Collect(WoodMergeTarget.FacingSide(owner));
        if (entries.Count == 0)
        {
            Clear();
            return default;
        }

        int index = lockedId >= 0 ? entries.FindIndex(e => e.Joint.Id == lockedId) : -1;
        if (index >= 0)
        {
            index = StepWithKeys(index);
        }
        else
        {
            index = Highest();
            lastSideColumn = -1;
            attachMouse = Input.mousePosition;
            lastInput = InputManager.MovementVectorNormalized(); // basılı tuş hemen kaydırmasın
        }
        selected = index;
        lockedId = entries[index].Joint.Id;
        Active = this;
        accept = true;
        return entries[index].Position + AxeFrame() * AxeOffset(entries[index].Joint);
    }

    public Quaternion GetToolTargetRotation()
        => AxeFrame() * Quaternion.Euler(HasSelection ? AxeRotation(entries[selected].Joint) : WoodMerger.Instance.DetachAxeRotation);

    // Ana odunun ek yeri yatay çizgi: balta yatay (ayrı ayar); yan kolların ek yerleri dikey: dik duruş
    private static Vector3 AxeOffset(WoodJoint joint)
        => joint.IsBase ? WoodMerger.Instance.DetachAxeBaseOffset : WoodMerger.Instance.DetachAxeOffset;

    private static Vector3 AxeRotation(WoodJoint joint)
        => joint.IsBase ? WoodMerger.Instance.DetachAxeBaseRotation : WoodMerger.Instance.DetachAxeRotation;

    // Ağaçtaki oklar gibi kamerayla 90° adımlarla döner (balta kameraya göre aynı duruşta kalır)
    private static Quaternion AxeFrame() => Quaternion.Euler(0f, (float)CameraController.CurrentFacing, 0f);

    public bool IsStillTargeted(IInteractable interacted, Ray mouseRay)
    {
        if (!(interacted is Axe) || lockedId < 0) return false;
        Vector2 moved = Input.mousePosition - attachMouse;
        return moved.magnitude <= WoodMerger.Instance.TargetReleasePixels;
    }

    public void OnToolTargetExit() => Clear();

    // Tek vuruşta söker (yer bulunamazsa bir şey olmaz)
    public bool OnToolUsed(IInteractable tool)
    {
        if (!(tool is Axe) || !HasSelection) return false;
        WoodJoint joint = entries[selected].Joint;
        Clear();
        WoodMerger.Instance.Detach(owner, joint);
        return true;
    }

    private void Clear()
    {
        if (Active == this) Active = null;
        lockedId = -1;
        selected = -1;
    }

    // --- Ek yerleri ---

    private void Collect(WoodSide facing)
    {
        entries.Clear();
        candidateCells.Clear();
        candidateDirections.Clear();
        int baseLength = owner.BaseLength;

        // Ana odunun parçaları arası: üstteki kısım (yan odunlarıyla) ayrılır
        int[] segments = owner.BaseSegments;
        int height = 0;
        for (int k = 1; k < segments.Length; k++)
        {
            height += segments[k - 1];
            var cells = new List<Vector3Int>();
            for (int y = height; y < baseLength; y++)
                cells.Add(new Vector3Int(0, y, 0));
            Add(new WoodJoint(true, default, 0, k), -1, k, height - 0.5f, 0, new Vector3(0f, height - 0.5f, 0f), cells, Vector3Int.up);
        }

        // Sol ve sağ yandaki odunlar: ana oduna bağlandığı yer + kendi parçaları arası (dıştaki kısım ayrılır)
        WoodSide first = (WoodSide)(((int)facing + 1) % WoodLayout.SideCount);
        WoodSide second = (WoodSide)(((int)facing + 3) % WoodLayout.SideCount);
        foreach (WoodSide side in new[] { first, second })
            for (int layer = 1; layer <= baseLength; layer++)
            {
                int length = owner.PieceAt(side, layer);
                if (length == 0) continue;
                int row = WoodLayout.SideRow(baseLength, layer);
                Vector3Int start = WoodLayout.PieceStart(baseLength, WoodSlot.At(side, layer));
                Vector3Int outward = WoodLayout.SideDirection(side);
                int[] pieceSegments = WoodLayout.PieceSegments(owner, side, layer);
                int offset = 0;
                for (int k = 0; k < pieceSegments.Length; k++)
                {
                    var cells = new List<Vector3Int>();
                    for (int i = offset; i < length; i++)
                        cells.Add(start + outward * i);
                    Add(new WoodJoint(false, side, layer, k), (int)side, row * 16 + k, row, row,
                        (Vector3)(start + outward * offset) - (Vector3)outward * 0.5f, cells, outward);
                    offset += pieceSegments[k];
                }
            }
    }

    private void Add(WoodJoint joint, int column, int order, float height, int row, Vector3 localPosition, List<Vector3Int> localCells, Vector3Int localDirection)
    {
        entries.Add(new Entry
        {
            Joint = joint,
            Column = column,
            Order = order,
            Height = height,
            Row = row,
            Position = (Vector3)owner.OriginWorldPosition + owner.GridRotation * localPosition
        });
        var cells = new Vector3Int[localCells.Count];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = owner.OriginWorldPosition + GridMaskRotator.RotateOffset(localCells[i], owner.Rotation);
        candidateCells.Add(cells);
        candidateDirections.Add(GridMaskRotator.RotateOffset(localDirection, owner.Rotation));
    }

    // En üstteki ek yeri (aynı yükseklikte en içteki)
    private int Highest()
    {
        int best = 0;
        for (int i = 1; i < entries.Count; i++)
        {
            if (entries[i].Height > entries[best].Height ||
                (Mathf.Approximately(entries[i].Height, entries[best].Height) && entries[i].Joint.Index < entries[best].Joint.Index))
                best = i;
        }
        return best;
    }

    // Sütundaki en üstteki (sütun -2: herhangi bir yan); yoksa -1
    private int HighestIn(int column)
    {
        int best = -1;
        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (column == -2 ? e.Column < 0 : e.Column != column) continue;
            if (best < 0 || e.Order > entries[best].Order) best = i;
        }
        return best;
    }

    // WASD bir basışta bir adım (birleştirmedeki gibi; bkz. WoodMergeTarget.StepWithKeys). Sütun = yan ya da ana odun.
    // W / S: aynı sütunda bir üst / alt ek yeri (yanda kat kat, aynı katta içten dışa); yanın en üstünden W ana odunun en
    // üst ek yerine, ana odunun en altından S en son bulunulan yanın (yoksa herhangi bir yanın) en üstüne.
    // A / D: karşı tarafa doğru basınca karşı yanda aynı kat (yoksa yukarı doğru, o da yoksa ana odunun en üst ek yeri),
    // kendi tarafına doğru basınca hiçbir şey. Ana odundayken A ekranda soldaki, D sağdaki yanın en üstüne.
    private int StepWithKeys(int index)
    {
        Vector2 input = InputManager.MovementVectorNormalized();
        bool pressed = input.magnitude > 0.5f && lastInput.magnitude <= 0.5f;
        lastInput = input;
        if (!pressed) return index;

        Entry current = entries[index];
        if (current.Column >= 0) lastSideColumn = current.Column;

        if (Mathf.Abs(input.y) >= Mathf.Abs(input.x))
        {
            int up = input.y > 0f ? 1 : -1;
            int next = -1, nextDistance = int.MaxValue;
            for (int i = 0; i < entries.Count; i++)
            {
                int distance = (entries[i].Order - current.Order) * up;
                if (entries[i].Column == current.Column && distance > 0 && distance < nextDistance) { nextDistance = distance; next = i; }
            }
            if (next >= 0) return next;
            if (current.Column >= 0 && up > 0)
            {
                int top = HighestIn(-1);
                return top >= 0 ? top : index;
            }
            if (current.Column < 0 && up < 0)
            {
                int side = lastSideColumn >= 0 ? HighestIn(lastSideColumn) : -1;
                if (side < 0) side = HighestIn(-2);
                return side >= 0 ? side : index;
            }
            return index;
        }

        Camera camera = Camera.main;
        if (current.Column < 0)
        {
            float x = camera.WorldToScreenPoint(current.Position).x;
            float direction = input.x > 0f ? 1f : -1f;
            int best = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (e.Column < 0 || (camera.WorldToScreenPoint(e.Position).x - x) * direction <= 1f) continue;
                if (best < 0 || e.Order > entries[best].Order) best = i;
            }
            return best >= 0 ? best : index;
        }

        // Kendi tarafına doğru: hiçbir şey
        float sideX = camera.WorldToScreenPoint(current.Position).x - camera.WorldToScreenPoint(FocusPoint).x;
        if (sideX * input.x > 0f) return index;

        // Karşı tarafa doğru: karşı yanda aynı kat (en içteki), yoksa yukarı doğru
        int opposite = (current.Column + 2) % WoodLayout.SideCount;
        int found = -1;
        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (e.Column != opposite || e.Row < current.Row) continue;
            if (found < 0 || e.Order < entries[found].Order) found = i;
        }
        if (found >= 0) return found;
        int baseTop = HighestIn(-1);
        return baseTop >= 0 ? baseTop : index;
    }
}
