using UnityEngine;

// Hedef odunun (Wood ya da MergedWood) bir bağlantı yerine tek parça odun ekler:
//   tepe → ana odun uzar; dört yanın katmanları birlikte kayar. Yanlarda hiç odun yoksa sonuç tek parça Wood.
//   yan  → MergedWood (hedefin mevcut katmanları korunur, seçilen katman dolar)
public class WoodMerger : MonoBehaviour
{
    public static WoodMerger Instance { get; private set; }

    [SerializeField] private WoodCatalogSO catalog;
    [SerializeField] private GameObject snapIndicatorPrefab;
    [Tooltip("İçi boş MergedWood prefab'ı (SO'su olmalı ki kaydedilebilsin). Görselini Build kurar.")]
    [SerializeField] private MergedWood mergedWoodPrefab;
    [Tooltip("Birleşince sonuç jöle gibi sallanır. Odun materyali 'Village/Wood Wobble Lit' shader'ını kullanmalı.")]
    [SerializeField] private JellyWobble mergeWobble = new JellyWobble();
    [Tooltip("Hedef odunun kameraya bakan yanındaki parçalar, odun getirilince yarı saydam olur (aynı shader).")]
    [SerializeField] private DitherFade frontFade = new DitherFade();
    [Tooltip("Taşınan odun bir hedefe yapıştıktan sonra mouse, hedefin ekseninden bu kadar (birim) uzaklaşınca bırakılır. " +
             "Mouse collider'dan çıksa da hedef kaybolmaz.")]
    [SerializeField] private float targetReleaseDistance = 5f;

    public WoodCatalogSO Catalog => catalog;
    public DitherFade FrontFade => frontFade;
    public float TargetReleaseDistance => targetReleaseDistance;
    public GameObject SnapIndicatorPrefab => snapIndicatorPrefab;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    // target hem GridPlaceable hem IWoodStack olmalı (Wood ya da MergedWood)
    public void Merge(GridPlaceable target, Wood carried, WoodSlot slot)
    {
        var stack = (IWoodStack)target;

        // Hedef silinmeden önce sonucu hesapla
        Vector3 position = target.OriginWorldPosition;
        GridMaskRotator.Rotation rotation = target.Rotation;
        int baseLength = stack.BaseLength;
        int[][] sides;

        if (slot.IsTop)
        {
            baseLength += carried.Length;
            sides = WoodLayout.SidesAfterTopMerge(stack, carried.Length);
        }
        else
        {
            sides = WoodLayout.CopySides(stack);
            sides[(int)slot.Side][slot.Layer - 1] = carried.Length;
        }

        // Taşınan odun yok olacak: geri yerleştirilmeye çalışılmasın diye iptal değil, sadece bırakıyoruz
        InteractableController.Instance.Release(carried);
        Destroy(carried.gameObject);
        GridManager.Instance.PlaceableRemoveOn(target, true);

        // Yanlarda hiç odun yoksa sonuç sıradan (tek parça) bir odun
        GridPlaceable result = WoodLayout.IsEmpty(sides)
            ? SpawnLongWood(baseLength)
            : SpawnMergedWood(baseLength, sides);

        result.Rotation = rotation;
        if (!GridManager.Instance.PlaceablePlaceOn(result, position))
        {
            Debug.LogWarning($"{result.name} grid'e yerleştirilemedi", result);
            return;
        }

        // Parçanın geldiği yönden itilmiş gibi: yandan eklenen parça o yandan iter → obje ters yöne eğilir.
        // Tepeden gelen parça yukarıdan bastırır → eğilme yok, sadece basılıp yaylanır.
        Vector3 push = slot.IsTop
            ? Vector3.zero
            : -(result.GridRotation * (Vector3)WoodLayout.SideDirection(slot.Side));
        PlayWobble(result, push);
    }

    private void PlayWobble(GridPlaceable result, Vector3 push)
    {
        GridFootprint footprint = result.PlacedFootprint;
        // Pivot alt hücrenin ortasında; taban yarım hücre aşağıda
        Vector3 basePoint = result.transform.position + Vector3.up * (footprint.Min.y - 0.5f);
        StartCoroutine(mergeWobble.Play(result.GetComponentsInChildren<Renderer>(), basePoint, footprint.Size.y, push));
    }

    private GridPlaceable SpawnLongWood(int length)
    {
        Wood wood = Instantiate(catalog.GetWood(length));
        wood.OnSpawned();
        return wood;
    }

    private GridPlaceable SpawnMergedWood(int baseLength, int[][] sides)
    {
        MergedWood merged;
        if (mergedWoodPrefab != null)
        {
            merged = Instantiate(mergedWoodPrefab);
        }
        else
        {
            Debug.LogWarning("WoodMerger: Merged Wood Prefab atanmamış; oluşan obje kaydedilemeyecek.", this);
            merged = new GameObject().AddComponent<MergedWood>();
        }

        merged.name = $"MergedWood {baseLength}BR";
        merged.Build(baseLength, sides, catalog);
        return merged;
    }
}