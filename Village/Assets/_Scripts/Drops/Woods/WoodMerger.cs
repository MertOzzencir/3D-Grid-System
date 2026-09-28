using System.Linq;
using UnityEngine;

// Hedef odunun (Wood ya da MergedWood) bir katmanına tek parça odun ekler:
//   tepe (katman 0) → ana odun uzar; yan katmanlarda odun yoksa tek parça Wood, varsa MergedWood (katmanlar kayar)
//   yan katman       → MergedWood (hedefin mevcut katmanları korunur)
public class WoodMerger : MonoBehaviour
{
    public static WoodMerger Instance { get; private set; }

    [SerializeField] private WoodCatalogSO catalog;
    [SerializeField] private GameObject snapIndicatorPrefab;
    [Tooltip("Birleşince sonuç jöle gibi sallanır. Odun materyali 'Village/Wood Wobble Lit' shader'ını kullanmalı.")]
    [SerializeField] private JellyWobble mergeWobble = new JellyWobble();

    public WoodCatalogSO Catalog => catalog;
    public GameObject SnapIndicatorPrefab => snapIndicatorPrefab;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    // target hem GridPlaceable hem IWoodStack olmalı (Wood ya da MergedWood)
    public void Merge(GridPlaceable target, Wood carried, int layer)
    {
        var stack = (IWoodStack)target;

        // Hedef silinmeden önce sonucu hesapla
        Vector3 position = target.OriginWorldPosition;
        GridMaskRotator.Rotation rotation = target.Rotation;
        int baseLength = stack.BaseLength;
        int[] layers;

        if (layer == WoodLayout.TopLayer)
        {
            baseLength += carried.Length;
            layers = WoodLayout.LayersAfterTopMerge(stack, carried.Length);
        }
        else
        {
            layers = new int[WoodLayout.LayerCount(baseLength)];
            for (int i = 0; i < layers.Length; i++)
                layers[i] = stack.PieceAt(i);
            layers[layer] = carried.Length;
        }

        // Taşınan odun yok olacak: geri yerleştirilmeye çalışılmasın diye iptal değil, sadece bırakıyoruz
        InteractableController.Instance.Release(carried);
        Destroy(carried.gameObject);
        GridManager.Instance.PlaceableRemoveOn(target, true);

        // Yan katmanlarda hiç odun yoksa sonuç sıradan (tek parça) bir odun
        GridPlaceable result = layers.All(length => length == 0)
            ? SpawnLongWood(baseLength)
            : SpawnMergedWood(baseLength, layers);

        result.Rotation = rotation;
        if (!GridManager.Instance.PlaceablePlaceOn(result, position))
        {
            Debug.LogWarning($"{result.name} grid'e yerleştirilemedi", result);
            return;
        }

        // Parçanın geldiği yönden itilmiş gibi: yan katmandaki parça sağdan gelir → obje önce sola eğilir.
        // Tepeden gelen parça yukarıdan bastırır → eğilme yok, sadece basılıp yaylanır.
        Vector3 push = layer == WoodLayout.TopLayer ? Vector3.zero : result.GridRotation * Vector3.left;
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

    private GridPlaceable SpawnMergedWood(int baseLength, int[] layers)
    {
        var merged = new GameObject($"MergedWood {baseLength}BR [{string.Join(",", layers)}]").AddComponent<MergedWood>();
        merged.Build(baseLength, layers, catalog);
        return merged;
    }
}
