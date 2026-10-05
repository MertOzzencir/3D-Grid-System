using UnityEngine;

// Tarla parçası: dünyada normal bir placeable (adada durur, sağ tık basılı sürüklenir, R ile döner), ama sadece botun
// tarla grid'ine (FarmGrid) yerleşmek için var. Tarlanın üstüne getirilince orada yerini gösterir; bırakınca (sağ tıkı
// bırak ya da sol tık) tarlaya oturur: dünya grid'inden çıkar, botun tarlasının child'ı olur, botla birlikte gider.
// Tarladaki parça tekrar tutulup tarlada başka yere kaydırılabilir ya da adaya geri konabilir. Üst üste konmaz: tarla
// tek kat (sadece yatayda birleşir). Şekli SO'nun footprint'i (1×1, 2×1, 3×1, L...): SO'da Size.y hep 1.
// Görsel: tileModel atanırsa footprint'in her hücresine bir kopyası konur (1×1 modelden çok hücreli parça); atanmazsa
// prefab'ın kendi modeli kullanılır. Kökte footprint'i saran bir BoxCollider olmalı (tutmak için; yoksa kod ekler).
public class FarmPiece : GridPlaceable, IInteractable
{
    [SerializeField] private float followSpeed = 15f;
    [Tooltip("1×1 tarla modeli: atanırsa parçanın her hücresine bir kopyası konur (2×1, 3×1, L... için tek model yeter)")]
    [SerializeField] private GameObject tileModel;

    private GridDragMotor drag;

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

    // Tutmak için kökte collider (InteractableController collider'ın objesinde IInteractable arar)
    private void EnsureCollider()
    {
        if (GetComponent<Collider>() != null) return;
        GridEntitySOBase data = GetData();
        Vector3 min = Vector3.zero, max = Vector3.zero;
        if (data != null)
            foreach (Vector3Int offset in data.GetFootprint().FilledCells())
            {
                min = Vector3.Min(min, offset);
                max = Vector3.Max(max, offset);
            }
        var box = gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3((min.x + max.x) * 0.5f, -0.35f, (min.z + max.z) * 0.5f);
        box.size = new Vector3(max.x - min.x + 1f, 0.3f, max.z - min.z + 1f);
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
