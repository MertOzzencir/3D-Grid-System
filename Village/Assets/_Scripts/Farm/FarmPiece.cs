using UnityEngine;

// Tarla parçası: dünyada normal bir placeable (adada durur, sağ tık basılı sürüklenir, R ile döner), ama sadece botun
// tarla grid'ine (FarmGrid) yerleşmek için var. Tarlanın üstüne getirilince orada yerini gösterir; bırakınca (sağ tıkı
// bırak ya da sol tık) tarlaya oturur: dünya grid'inden çıkar, botun tarlasının child'ı olur, botla birlikte gider.
// Tarladaki parça tekrar tutulup tarlada başka yere kaydırılabilir ya da adaya geri konabilir. Üst üste konmaz: tarla
// tek kat (sadece yatayda birleşir). Şekli SO'nun footprint'i (1×1, 2×1, 3×1, L...): SO'da Size.y hep 1.
// Görsel: tileModel atanırsa footprint'in her hücresine bir kopyası konur (1×1 modelden çok hücreli parça); atanmazsa
// prefab'ın kendi modeli kullanılır. Kökte footprint'i saran bir BoxCollider olmalı (tutmak için; yoksa kod ekler).
// Ekinler parçanın hücrelerinde durur (tohum FarmGrid üzerinden eker); parça taşınınca ekinleri de onunla gider.
public class FarmPiece : GridPlaceable, IInteractable
{
    [SerializeField] private float followSpeed = 15f;
    [Tooltip("1×1 tarla modeli: atanırsa parçanın her hücresine bir kopyası konur (2×1, 3×1, L... için tek model yeter)")]
    [SerializeField] private GameObject tileModel;
    [Tooltip("Ekinlerin dibinin yüksekliği, parçanın pivot'una göre (yerel Y). Pivot hücrenin ortasında, modelin dibi -0.5; " +
             "toprağın üst yüzü kaçtaysa o (fideler toprağa gömülmesin / havada durmasın). Tarla parçası 1 birim yüksek: üstü 0.5")]
    [SerializeField] private float soilHeight = 0.5f;

    private GridDragMotor drag;

    // Ekili hücreler: parçanın döndürmesiz yerel hücresi (footprint offset'i) → ekin. Görseli parçanın child'ı:
    // parça tarlada kaydırılınca ya da adaya konunca ekinler de onunla gider.
    private struct PlantedCrop
    {
        public CropSO crop;
        public GameObject visual;
    }
    private readonly System.Collections.Generic.Dictionary<Vector2Int, PlantedCrop> crops =
        new System.Collections.Generic.Dictionary<Vector2Int, PlantedCrop>();

    // Tarladaysa: hangi tarla, hangi hücrede (tarlanın yerel koordinatı, footprint'in pivot hücresi)
    public FarmGrid Farm { get; private set; }
    public Vector2Int FarmCell { get; private set; }

    private FarmGrid liftedFrom;   // tarladan tutulduysa: bırakılacak yer bulunamazsa oraya geri döner
    private Vector2Int liftedCell;
    private GridMaskRotator.Rotation liftedRotation;

    private void Awake()
    {
        drag = new GridDragMotor(this, followSpeed);
        if (tileModel != null) BuildTiles();
        EnsureCollider();
    }

    // Footprint'in her hücresine 1×1 model (pivot'a göre, Deg0; kök döndükçe hepsi döner)
    private void BuildTiles()
    {
        GridEntitySOBase data = GetData();
        if (data == null) return;
        foreach (Vector3Int offset in data.GetFootprint().FilledCells())
        {
            if (offset.y != 0) continue; // tarla tek kat
            Transform tile = Instantiate(tileModel, transform).transform;
            tile.localPosition = new Vector3(offset.x, 0f, offset.z);
            tile.localRotation = Quaternion.identity;
        }
    }

    // Tutmak ve eldivenin üstünde yürümesi için kökte collider (InteractableController collider'ın objesinde
    // IInteractable arar). Modelin mesh'lerini saran kutu: eldiven modelin üst yüzüne oturur, içine girmez.
    // Mesh yoksa footprint'i saran ince kutu.
    private void EnsureCollider()
    {
        if (GetComponent<Collider>() != null) return;
        var box = gameObject.AddComponent<BoxCollider>();
        if (TryGetModelBounds(out Bounds bounds))
        {
            box.center = bounds.center;
            box.size = bounds.size;
            return;
        }

        GridEntitySOBase data = GetData();
        Vector3 min = Vector3.zero, max = Vector3.zero;
        if (data != null)
            foreach (Vector3Int offset in data.GetFootprint().FilledCells())
            {
                min = Vector3.Min(min, offset);
                max = Vector3.Max(max, offset);
            }
        box.center = new Vector3((min.x + max.x) * 0.5f, -0.35f, (min.z + max.z) * 0.5f);
        box.size = new Vector3(max.x - min.x + 1f, 0.3f, max.z - min.z + 1f);
    }

    // Kökün yerel uzayında bütün mesh'lerin sınırı (kökün o anki dönüşünden bağımsız)
    private bool TryGetModelBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        Matrix4x4 toRoot = transform.worldToLocalMatrix;
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            Matrix4x4 matrix = toRoot * filter.transform.localToWorldMatrix;
            Bounds mesh = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mesh.center + Vector3.Scale(mesh.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 point = matrix.MultiplyPoint3x4(corner);
                if (!found)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    found = true;
                }
                else bounds.Encapsulate(point);
            }
        }
        return found;
    }

    // Dünyada sadece zemine konur: hücrenin hemen altında base (ada karosu) olmalı. Başka bir parçanın, odunun ya da
    // herhangi bir şeyin üstüne konmaz (tarla parçaları sadece yatayda birleşir); sürüklerken ray başka bir parçanın
    // collider'ının üstüne çarpınca bir üst kata yerleşmesin.
    public override bool CanOccupy(Vector3Int worldCell)
    {
        if (!base.CanOccupy(worldCell)) return false;
        GridManager grid = GridManager.Instance;
        if (grid == null) return true;
        return grid.TryGetCell(worldCell + Vector3Int.down, out GridData below) && below.Base != null;
    }

    // --- Ekin ---

    // Dünya noktasının düştüğü parça hücresi (döndürmesiz yerel offset); parçanın bir hücresi değilse false
    public bool TryGetTileAt(Vector3 worldPoint, out Vector2Int tile)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        tile = new Vector2Int(Mathf.RoundToInt(local.x), Mathf.RoundToInt(local.z));
        GridEntitySOBase data = GetData();
        if (data == null) return false;
        foreach (Vector3Int offset in data.GetFootprint().FilledCells())
            if (offset.y == 0 && offset.x == tile.x && offset.z == tile.y) return true;
        return false;
    }

    public bool HasCrop(Vector2Int tile) => crops.ContainsKey(tile);

    public CropSO CropAt(Vector2Int tile) => crops.TryGetValue(tile, out PlantedCrop planted) ? planted.crop : null;

    public bool Plant(CropSO crop, Vector2Int tile)
    {
        if (crop == null || crops.ContainsKey(tile)) return false;
        GameObject visual = crop.CreateSeedling(transform);
        // Her fide biraz farklı dursun (aynı yöne bakan sıra yapay görünür)
        visual.transform.SetLocalPositionAndRotation(new Vector3(tile.x, soilHeight, tile.y),
                                                     Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        crops[tile] = new PlantedCrop { crop = crop, visual = visual };
        return true;
    }

    // --- Tarla ---

    public void OnAttachedToFarm(FarmGrid farm, Vector2Int cell)
    {
        Farm = farm;
        FarmCell = cell;
    }

    public void OnDetachedFromFarm() => Farm = null;

    // --- IInteractable: elde taşınırken ---

    public void InteractContractBeginnig()
    {
        liftedFrom = null;
        if (Farm != null)
        {
            // Tarladan kaldır: tarla bırakılınca dolu sayılmasın, parça dünyada serbest
            liftedFrom = Farm;
            liftedCell = FarmCell;
            liftedRotation = Rotation;
            Farm.Detach(this);
            transform.SetParent(null, true);
        }
        drag.Begin();
    }

    public void InteractContract(out bool success)
    {
        success = true;
        drag.Tick();
    }

    // Sol tık: hedef (tarla) varsa oraya oturt
    public void Interact(out bool finished)
    {
        if (!drag.TryUseOnTarget(out finished)) finished = true;
    }

    // Sağ tık bırakıldı: tarlanın üstündeyse oraya oturur; değilse adaya bırakılır. Tarladan alınıp konacak yer yoksa
    // (su, dolu) tarladaki eski yerine döner.
    public void ContractCancel()
    {
        FarmGrid hovered = FarmGrid.Hovered;
        if (hovered != null && hovered.TryAttachHovered(this))
        {
            drag.Abort();
            return;
        }

        if (liftedFrom != null && !CanDropOnWorld())
        {
            drag.Abort();
            Rotation = liftedRotation;
            liftedFrom.Attach(this, liftedCell);
            return;
        }
        drag.Cancel();
    }

    // Mouse'un altındaki dünya hücresine konabilir mi (GridDragMotor.Cancel'ın deneyeceği yer)
    private bool CanDropOnWorld()
    {
        if (GridManager.Instance == null) return false;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        foreach (RaycastHit hit in Physics.RaycastAll(ray))
        {
            if (hit.transform.IsChildOf(transform)) continue;
            Vector3 cell = GridManager.Instance.GetGridWorldPosition(hit.point + Vector3.up * 0.5f, out bool onGrid);
            return onGrid && GridManager.Instance.CanPlaceablePlaceOn(this, cell);
        }
        return false;
    }
}
