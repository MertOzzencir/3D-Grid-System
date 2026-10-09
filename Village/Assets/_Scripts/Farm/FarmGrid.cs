using System.Collections.Generic;
using UnityEngine;

// Botun tarla grid'i: botun arkasından gelen tek sayılı kare (size × size), tek kat. Dünya grid'i bütün tarlayı botun
// placeable'ı olarak görür; içeride hangi hücrede hangi parça var, bunu bu sınıf tutar (tarlanın yerel koordinatı:
// x, z 0..size-1). Kök = güvertenin üst yüzünün ortası (Boat kurar ve her kare yerini yazar); parçalar onun child'ı.
// Tarla parçası (FarmPiece) tarlanın üstüne getirilince IToolTarget olarak yerini gösterir; bırakılınca oturur.
// Ekin: hücredeki parçanın o hücresinde durur (FarmPiece tutar, parça kaydırılınca ekin de gider). Tohum (Seed)
// tarlanın üstüne getirilince hücrenin üstünde süzülür; sol tık (basılı tutup sürükleyerek de) ekilebilir hücrelere eker.
// Eldiven tarlanın üstünde base gibi yürür (IGloveWalkable; bot geçirgen kalır).
// Hasat yolu: orak (Sickle) tutulurken sol tık basılı geçilen olgun kareler hasat edilir; kesintisiz yol kombo sayar.
// Boş / hasat edilmiş kareye girmek yolu (komboyu) bitirir, ürün kaybolmaz; sonraki olgun karede yeni yol başlar.
// Bir yolda bütün olgun ekinler hasat edilirse "Tam tur": yoldaki her ekin için bir ürün daha.
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
    // Tarla büyüyor (kapalı, FarmGrower): ekilmez, parça konmaz / alınmaz
    public bool Locked { get; set; }
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
    public bool CanPlant(Vector2Int cell) => !Locked && TryGetPlot(cell, out FarmPiece piece, out Vector2Int tile) && !piece.HasCrop(tile);

    public bool TryPlant(CropSO crop, Vector2Int cell) =>
        TryGetPlot(cell, out FarmPiece piece, out Vector2Int tile) && piece.Plant(crop, tile);

    // Büyütülecek fide var mı
    public bool HasSeedlings
    {
        get
        {
            foreach (FarmPiece piece in pieces)
                if (piece.HasSeedlings) return true;
            return false;
        }
    }

    public void MatureAll()
    {
        foreach (FarmPiece piece in pieces) piece.MatureAll();
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

    // --- Hasat yolu ---

    private static FarmGrid activeHarvest;
    private bool harvesting;
    private Vector2Int harvestLast;
    private int combo;
    private int matureAtStart;
    private readonly List<CropSO> harvestedInPath = new List<CropSO>();

    private static readonly Color ComboColor = new Color(1f, 0.95f, 0.75f);
    private static readonly Color FullTourColor = new Color(1f, 0.82f, 0.3f);

    // Orak bırakıldı / sol tık kalktı: süren yol biter
    public static void EndActiveHarvest()
    {
        if (activeHarvest != null) activeHarvest.EndHarvest();
    }

    private bool TryGetMature(Vector2Int cell) => TryGetPlot(cell, out FarmPiece piece, out Vector2Int tile) && piece.IsMature(tile);

    private int MatureCount
    {
        get
        {
            int count = 0;
            foreach (FarmPiece piece in pieces) count += piece.MatureCount;
            return count;
        }
    }

    private Vector3 CellWorld(Vector2Int cell) => transform.TransformPoint(CellLocal(cell));

    // Orak bu kareye geldi (sol tık basılı). Mouse hızlı gidip kare atladıysa ya da çapraz geçtiyse aradaki kareler
    // dört komşuluk adımlarıyla tek tek yürünür.
    private bool HarvestTo(Sickle sickle, Vector2Int cell)
    {
        if (Locked) return false;
        if (!harvesting)
        {
            if (!TryGetMature(cell)) return false;
            if (activeHarvest != null && activeHarvest != this) activeHarvest.EndHarvest();
            harvesting = true;
            activeHarvest = this;
            combo = 0;
            matureAtStart = MatureCount;
            harvestedInPath.Clear();
            HarvestCell(sickle, cell);
            return true;
        }

        for (int guard = size * size * 2; harvestLast != cell && guard > 0; guard--)
        {
            Vector2Int d = cell - harvestLast;
            Vector2Int step = Mathf.Abs(d.x) >= Mathf.Abs(d.y)
                ? new Vector2Int(System.Math.Sign(d.x), 0)
                : new Vector2Int(0, System.Math.Sign(d.y));
            Vector2Int next = harvestLast + step;
            if (!TryGetMature(next))
            {
                EndHarvest(); // boş ya da az önce hasat edilmiş kare: kombo biter
                return true;
            }
            HarvestCell(sickle, next);
        }
        return true;
    }

    private void HarvestCell(Sickle sickle, Vector2Int cell)
    {
        if (!TryGetPlot(cell, out FarmPiece piece, out Vector2Int tile)) return;
        CropSO crop = piece.Harvest(tile);
        if (crop == null) return;
        harvestLast = cell;
        combo++;
        harvestedInPath.Add(crop);
        GiveProduce(crop);
        sickle?.OnHarvested();
        if (combo >= 2)
            FarmPopup.Show($"×{combo}", CellWorld(cell) + Vector3.up * 0.9f, ComboColor, 3f + Mathf.Min(combo, 10) * 0.15f);
    }

    private void EndHarvest()
    {
        if (!harvesting) return;
        harvesting = false;
        if (activeHarvest == this) activeHarvest = null;

        // Bütün olgun ekinler tek yolda: yoldaki her ekin için bir ürün daha
        if (combo >= 2 && combo == matureAtStart)
        {
            foreach (CropSO crop in harvestedInPath) GiveProduce(crop);
            FarmPopup.Show($"Full Harvest! +{combo}", transform.position + Vector3.up * 1.6f, FullTourColor, 5f);
        }
        harvestedInPath.Clear();
    }

    private static void GiveProduce(CropSO crop)
    {
        if (crop.produce != null && InventoryManager.Instance != null) InventoryManager.Instance.AddSource(crop.produce);
    }

    // --- IToolTarget: elde parça, tohum ya da orak varken mouse tarlanın üstünde ---

    public Vector3 GetToolTargetPosition(IInteractable interacted, out bool accept)
    {
        accept = false;
        pendingValid = false;
        if (cells == null || !TryGetMouseCell(out Vector2Int cell)) return Vector3.zero;

        // Tohum / orak: tarlanın her hücresinde üstte süzülür (ekilemeyen hücrede de, tarladan düşmesin).
        // Tohumda pendingValid = ekilebilir; orakta tarlanın içinde (hasat kuralı HarvestTo'da).
        if (interacted is Seed || interacted is Sickle)
        {
            if (!InBounds(cell)) return Vector3.zero;
            pendingCell = cell;
            pendingValid = interacted is Sickle || CanPlant(cell);
            pendingRotation = transform.rotation * ((GridEntity)interacted).GridRotation;
            accept = true;
            return CellWorld(cell) + transform.up * SeedHoverHeight;
        }

        if (!(interacted is FarmPiece piece) || Locked) return Vector3.zero;
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
        if (tool is Sickle sickle) return pendingValid && HarvestTo(sickle, pendingCell);

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

    public void OnToolTargetExit()
    {
        ClearHover();
        EndHarvest(); // orak tarladan çıktı: yol biter
    }

    private void ClearHover()
    {
        pendingValid = false;
        if (Hovered == this) Hovered = null;
    }

    private void OnDisable()
    {
        ClearHover();
        EndHarvest();
    }

    // --- Eldiven ekinlere dokunuyor ---

    private const float TouchRadius = 0.55f;
    private Vector3 lastGlovePoint;
    private bool hasLastGlovePoint;

    // Boş el tarlanın üstünde gezerken yakınındaki ekinler sallanır (CropSway)
    private void Update()
    {
        GloveCursor glove = GloveCursor.Instance;
        bool onFarm = glove != null && glove.HasSurface && glove.SurfaceCollider != null &&
                      glove.SurfaceCollider.transform.IsChildOf(transform);
        if (!onFarm || pieces.Count == 0)
        {
            hasLastGlovePoint = false;
            return;
        }

        Vector3 point = glove.SurfacePoint;
        Vector3 velocity = hasLastGlovePoint ? (point - lastGlovePoint) / Mathf.Max(Time.deltaTime, 0.0001f) : Vector3.zero;
        lastGlovePoint = point;
        hasLastGlovePoint = true;
        if (velocity.sqrMagnitude < 0.04f) return; // durunca sallamaz

        foreach (FarmPiece piece in pieces) piece.PokeCrops(point, velocity, TouchRadius);
    }
}
