using System.Collections.Generic;
using UnityEngine;

// İnce grid'e yerleşen obje (yarım odun, süs eşyası...): hücrenin 2×2×2 küçük küplerinden (0.5 birim) kaplar.
// Bir hücrede birden çok ince obje olabilir (iki yarım odun yan yana); tam objelerle (Placeable) aynı hücreyi paylaşmaz.
// Boy: fineSize, küçük küp biriminde, Deg0'da (x, y, z). R ile 90° döner (x ve z yer değiştirir).
// Pivot: kapladığı kutunun ortası (model / kutu ortaya göre).
// Taşıma: sağ tık basılı tutulur, mouse'un baktığı yüzeye yarım hücre adımlarla yapışır (yüzeyin dışına, normali
// yönünde); en az bir küçük küpü alttan destekli olmalı. Bırakınca oturur; uygun yer yoksa aldığı yere döner.
// Build menüsü, silme ve kayıt GridManager üzerinden (PlaceablePlaceOn / PlaceableRemoveOn / GetAllPlaceables).
// Mouse tutan bir hedefin (IHoldTarget + IToolTarget, örn. kesme kütüğü) üstündeyse ve hedef kabul ediyorsa ona yapışır,
// bırakınca ona konur.
public class FinePlaceable : GridPlaceable, IInteractable, ISaveState
{
    [Tooltip("Boyu, küçük küp (0.5 birim) cinsinden, döndürülmemiş halde: (genişlik X, yükseklik Y, derinlik Z). " +
             "Yarım odun: (1, 2, 2).")]
    [SerializeField] private Vector3Int fineSize = new Vector3Int(1, 2, 2);
    [SerializeField] private float followSpeed = 18f;
    [Tooltip("İsteğe bağlı model (pivot kutunun ortasında). Atanırsa prefab'taki geçici görünüm (kutu) gizlenir.")]
    [SerializeField] private GameObject model;
    [Tooltip("Modelin pivota göre kayması (birim). Play'deyken değiştirince anında görünür: collider'a (yeşil kutu) göre " +
             "hizalanınca değeri Play'den çıkıp prefab'a yaz.")]
    [SerializeField] private Vector3 modelOffset;
    [Tooltip("Modelin dönüşü (derece)")]
    [SerializeField] private Vector3 modelRotation;
    [Tooltip("Modelin ölçeği (1 = olduğu gibi)")]
    [SerializeField] private Vector3 modelScale = Vector3.one;
    [Tooltip("Kesik (iç) yüzün yönü, döndürülmemiş halde (yerel). Kesme istasyonu bölünen parçaları bu yüz istasyonun " +
             "ortasına bakacak şekilde döndürür: ikiye ayrılmış gibi. Ters çıkıyorsa (-1, 0, 0) yap.")]
    [SerializeField] private Vector3 innerFace = Vector3.right;

    public Vector3 InnerFace => innerFace;
    [Tooltip("Model yokken geçici görünümün (kutu) rengi: prototipte parçalar birbirinden ayırt edilsin. Beyaz = malzemenin kendi rengi.")]
    [SerializeField] private Color placeholderColor = Color.white;

    private List<Vector3Int> occupiedCells;   // GridManager'da kapladığı hücreler (yerleşikken)
    private Vector3Int fineMin;               // kapladığı kutunun küçük küp olarak alt köşesi
    private bool hasSavedMin;                 // kayıttan geldi: yeri kayıttaki köşe

    // Taşıma
    private bool holding;
    private bool startPlaced;
    private Vector3Int startMin;
    private GridMaskRotator.Rotation startRotation;
    private bool hasTarget;
    private Vector3Int targetMin;
    private IHoldTarget holdTarget; // mouse'un üstünde olduğu, kabul eden tutucu (kesme kütüğü)
    private readonly RaycastHit[] hits = new RaycastHit[16];

    public bool IsPlaced => occupiedCells != null;

    // Yüksekliği (birim): küçük küp 0.5
    public float Height => fineSize.y * 0.5f;

    private void Awake()
    {
        if (model == null)
        {
            if (placeholderColor != Color.white)
                foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.material.color = placeholderColor;
            return;
        }
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false; // geçici kutu
        GameObject built = Instantiate(model, transform, false);
        built.name = "Model";
        modelInstance = built.transform;
        modelBase = (modelInstance.localPosition, modelInstance.localRotation, modelInstance.localScale);
        ApplyModelPose();
    }

    private Transform modelInstance;
    private (Vector3 position, Quaternion rotation, Vector3 scale) modelBase; // modelin kendi (prefab'taki) duruşu

    // Ofset / dönüş / ölçek modelin kendi duruşunun üstüne
    private void ApplyModelPose()
    {
        if (modelInstance == null) return;
        modelInstance.localPosition = modelBase.position + modelOffset;
        modelInstance.localRotation = Quaternion.Euler(modelRotation) * modelBase.rotation;
        modelInstance.localScale = Vector3.Scale(modelBase.scale, modelScale);
    }

    // Play'deyken Inspector'da değiştirince model anında yerine
    private void OnValidate() => ApplyModelPose();

    // Dönüşe göre boy (90° / 270°: X ile Z yer değiştirir)
    private Vector3Int Size => Rotation == GridMaskRotator.Rotation.Deg90 || Rotation == GridMaskRotator.Rotation.Deg270
        ? new Vector3Int(fineSize.z, fineSize.y, fineSize.x) : fineSize;

    private static GridManager Grid => GridManager.Instance;

    // Kutunun ortası (dünya)
    private Vector3 CenterOf(Vector3Int min, Vector3Int size) =>
        Grid.FineCenterWorld(min) + (Vector3)(size - Vector3Int.one) * 0.25f;

    // Bir noktaya göre kutunun köşesi: nokta tabandaki orta küçük küpün içinde
    private Vector3Int MinAround(Vector3 point, Vector3Int size)
    {
        Vector3Int fine = Grid.WorldToFine(point);
        return new Vector3Int(fine.x - (size.x - 1) / 2, fine.y, fine.z - (size.z - 1) / 2);
    }

    // --- Yerleşim (build menüsü, kayıt) ---

    // position: build menüsünün hedefi (yüzey + yarım hücre yukarısı); yüzeyin hemen üstündeki küçük küp
    public bool CanPlaceAt(Vector3 position)
    {
        if (Grid == null) return false;
        Vector3Int min = hasSavedMin ? fineMin : MinAround(position + Vector3.down * 0.25f, Size);
        return Grid.CanFineOccupy(min, Size, this);
    }

    public bool PlaceAt(Vector3 position)
    {
        if (Grid == null) return false;
        Vector3Int min = hasSavedMin ? fineMin : MinAround(position + Vector3.down * 0.25f, Size);
        hasSavedMin = false;
        if (!Grid.CanFineOccupy(min, Size, this)) return false;
        Occupy(min);
        transform.SetPositionAndRotation(CenterOf(min, Size), GridRotation);
        return true;
    }

    // Kutunun ortası center'da olacak şekilde konur (ince grid'e yuvarlanır). PlaceAt'ten farkı: çift boyutta orta bir
    // sınıra denk gelince bir yana kaymaz (örn. kesme kütüğünün tam yanına hizalı)
    public bool PlaceCentered(Vector3 center)
    {
        if (Grid == null) return false;
        Vector3Int min = Grid.WorldToFine(center - (Vector3)(Size - Vector3Int.one) * 0.25f);
        if (!Grid.CanFineOccupy(min, Size, this)) return false;
        hasSavedMin = false;
        Occupy(min);
        transform.SetPositionAndRotation(CenterOf(min, Size), GridRotation);
        return true;
    }

    private void Occupy(Vector3Int min)
    {
        if (occupiedCells != null) ReleaseCells();
        fineMin = min;
        occupiedCells = Grid.FineOccupy(this, min, Size);
        OriginWorldPosition = Grid.FineCellWorld(min); // kayıt kaydı için (asıl yer state'te)
    }

    public bool ReleaseCells()
    {
        if (occupiedCells == null || Grid == null) return false;
        Grid.FineRelease(this, occupiedCells);
        occupiedCells = null;
        return true;
    }

    private void OnDestroy() => ReleaseCells();

    // --- IInteractable: taşıma ---

    public void InteractContractBeginnig()
    {
        holding = true;
        holdTarget = null;
        startPlaced = IsPlaced;
        startMin = fineMin;
        startRotation = Rotation;
        hasTarget = false;
        ReleaseCells();
    }

    public void InteractContract(out bool success)
    {
        success = true;
        if (Grid == null) return;

        // Mouse'un baktığı ilk yüzey (kendisi hariç); yüzeyin dışına (normali yönünde) çeyrek birim
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        int count = Physics.RaycastNonAlloc(ray, hits, 200f, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        RaycastHit hit = default;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].transform.IsChildOf(transform) || hits[i].distance >= nearest) continue;
            nearest = hits[i].distance;
            hit = hits[i];
        }

        Vector3 goal;
        holdTarget = null;
        if (nearest < float.MaxValue && hit.collider.TryGetComponent(out IHoldTarget hold) &&
            hit.collider.TryGetComponent(out IToolTarget toolTarget))
        {
            Vector3 targetPosition = toolTarget.GetToolTargetPosition(this, out bool accept);
            if (accept)
            {
                holdTarget = hold;
                hasTarget = false;
                float k = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
                transform.SetPositionAndRotation(Vector3.Lerp(transform.position, targetPosition, k),
                                                 Quaternion.Slerp(transform.rotation, toolTarget.GetToolTargetRotation(), k));
                return;
            }
        }
        if (nearest < float.MaxValue)
        {
            Vector3Int size = Size;
            Vector3Int min = MinAround(hit.point + hit.normal * 0.25f, size);
            // Yandan bakınca: kutu yüzeyin yanına, normali yönünde tamamen dışarıda kalsın
            if (Mathf.Abs(hit.normal.x) > 0.5f && hit.normal.x < 0f) min.x -= size.x / 2;
            if (Mathf.Abs(hit.normal.x) > 0.5f && hit.normal.x > 0f) min.x += (size.x - 1) / 2;
            if (Mathf.Abs(hit.normal.z) > 0.5f && hit.normal.z < 0f) min.z -= size.z / 2;
            if (Mathf.Abs(hit.normal.z) > 0.5f && hit.normal.z > 0f) min.z += (size.z - 1) / 2;
            if (hit.normal.y < -0.5f) min.y -= size.y - 1;

            hasTarget = Grid.CanFineOccupy(min, size, this);
            if (hasTarget) targetMin = min;
            goal = hasTarget ? CenterOf(min, size) : hit.point + Vector3.up * (size.y * 0.25f + 0.1f);
        }
        else
        {
            hasTarget = false;
            goal = transform.position;
        }

        float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
        transform.SetPositionAndRotation(Vector3.Lerp(transform.position, goal, t),
                                         Quaternion.Slerp(transform.rotation, GridRotation, t));
    }

    public void Interact(out bool finished) => finished = false;

    // Bırakıldı: hedef varsa oraya oturur; yoksa aldığı yere (aldığı dönüşle) döner
    public void ContractCancel()
    {
        holding = false;
        if (holdTarget != null)
        {
            IHoldTarget hold = holdTarget;
            holdTarget = null;
            if (hold.TryHold(this)) return;
        }
        if (hasTarget && Grid.CanFineOccupy(targetMin, Size, this))
        {
            Occupy(targetMin);
            transform.SetPositionAndRotation(CenterOf(targetMin, Size), GridRotation);
            return;
        }
        if (!startPlaced) return;
        Rotation = startRotation;
        if (Grid.CanFineOccupy(startMin, Size, this))
        {
            Occupy(startMin);
            transform.SetPositionAndRotation(CenterOf(startMin, Size), GridRotation);
        }
        else Debug.LogWarning($"{name} aldığı yere geri konamadı", this);
    }

    // --- Kayıt: yeri küçük küp köşesi olarak (hücre + rotasyon yetmez) ---

    [System.Serializable]
    private class SaveState
    {
        public int x, y, z;
    }

    public string CaptureState() => JsonUtility.ToJson(new SaveState { x = fineMin.x, y = fineMin.y, z = fineMin.z });

    public void RestoreState(string state)
    {
        SaveState saved = JsonUtility.FromJson<SaveState>(state);
        if (saved == null) return;
        fineMin = new Vector3Int(saved.x, saved.y, saved.z);
        hasSavedMin = true;
    }

#if UNITY_EDITOR
    // Kapladığı kutu (yerleşikken yeşil, taşınırken hedef mavi)
    private void OnDrawGizmosSelected()
    {
        if (Grid == null) return;
        Vector3Int size = Size;
        Gizmos.color = holding ? new Color(0.2f, 0.5f, 1f) : new Color(0.2f, 0.8f, 0.3f);
        Vector3Int min = holding ? targetMin : fineMin;
        Gizmos.DrawWireCube(CenterOf(min, size), (Vector3)size * 0.5f);
    }
#endif
}
