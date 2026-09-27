using UnityEngine;

// İki odunu birleştirir. Tepe (katman 0) → daha uzun tek parça odun, yan katman → MergedWood.
public class WoodMerger : MonoBehaviour
{
    public static WoodMerger Instance { get; private set; }

    [SerializeField] private WoodCatalogSO catalog;
    [SerializeField] private GameObject snapIndicatorPrefab;

    public WoodCatalogSO Catalog => catalog;
    public GameObject SnapIndicatorPrefab => snapIndicatorPrefab;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    public void Merge(Wood target, Wood carried, int layer)
    {
        Vector3 position = target.OriginWorldPosition;
        GridMaskRotator.Rotation rotation = target.Rotation;
        int targetLength = target.Length;
        int carriedLength = carried.Length;

        // Taşınan odun yok olacak: geri yerleştirilmeye çalışılmasın diye iptal değil, sadece bırakıyoruz
        InteractableController.Instance.Release(carried);
        Destroy(carried.gameObject);
        GridManager.Instance.PlaceableRemoveOn(target, true);

        GridPlaceable result = layer == WoodLayout.TopLayer
            ? SpawnLongWood(targetLength + carriedLength)
            : SpawnMergedWood(targetLength, layer, carriedLength);

        result.Rotation = rotation;
        if (!GridManager.Instance.PlaceablePlaceOn(result, position))
            Debug.LogWarning($"{result.name} grid'e yerleştirilemedi", result);
    }

    private GridPlaceable SpawnLongWood(int length)
    {
        Wood wood = Instantiate(catalog.GetWood(length));
        wood.OnSpawned();
        return wood;
    }

    private GridPlaceable SpawnMergedWood(int baseLength, int layer, int pieceLength)
    {
        var merged = new GameObject($"MergedWood {baseLength}BR + {pieceLength}BR").AddComponent<MergedWood>();
        merged.Build(baseLength, layer, pieceLength, catalog);
        return merged;
    }
}
