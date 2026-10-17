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

    [Header("Kil görünüm (deneme)")]
    [Tooltip("Açıksa bütün odunlar (tek odun ve birleştirilmiş yapılar) ayrı modelleri yerine kaynaşmış tek bir kil şekli " +
             "olarak çizilir (ClayMesher). Şekil odun kurulurken bir kez üretilir; tek odunlarda uzunluk başına önbellekte.")]
    [SerializeField] private bool clayLook = true;
    [Tooltip("Şeklin ızgara aralığı (birim): küçük = daha pürüzsüz ama üretimi daha yavaş")]
    [SerializeField] private float clayVoxelSize = 0.07f;
    [Tooltip("Dış kenarların yuvarlaklığı (birim)")]
    [SerializeField] private float clayCornerRadius = 0.12f;
    [Tooltip("Odunun kalınlığı (birim, 1 = hücre kadar). Şekil modelden değil grid'den hesaplanır: bütün parçalar aynı " +
             "kesitte, hücre sınırlarında (model sınırları biraz farklı olduğu için birleşim yerlerinde basamak kalıyordu).")]
    [SerializeField, Range(0.3f, 1f)] private float clayThickness = 0.9f;
    [Tooltip("Parça sınırlarını içe çekme (birim): şekil modelden şişman görünürse")]
    [SerializeField] private float clayInset = 0f;
    [Tooltip("Kil şeklinin malzemesi (düz URP Lit; odunun WoodWobbleLit'i olmaz, vertex kaydırıyor). Build'de gerekli.")]
    [SerializeField] private Material clayMaterial;
    [Tooltip("Clay Material boşken (sadece editörde) kullanılan düz malzemenin rengi")]
    [SerializeField] private Color clayColor = new Color(0.78f, 0.58f, 0.38f);

    private static readonly System.Collections.Generic.Dictionary<string, Mesh> clayCache =
        new System.Collections.Generic.Dictionary<string, Mesh>();
    private Material fallbackClay;

    // Kaynaşmış kil şeklini owner'ın çocuğu olarak takar, verilen renderer'ları gizler. boxes owner'ın uzayında.
    // cacheKey: aynı şekli tekrar hesaplamamak için (tek odunlar). Kil görünüm kapalıysa null.
    public GameObject AttachClay(Transform owner, System.Collections.Generic.List<Bounds> boxes, Renderer[] hide, string cacheKey = null)
    {
        if (!clayLook || boxes.Count == 0) return null;
        string key = cacheKey == null ? null : $"{cacheKey}|{clayVoxelSize}|{clayCornerRadius}|{clayInset}";
        if (key == null || !clayCache.TryGetValue(key, out Mesh mesh) || mesh == null)
        {
            mesh = ClayMesher.Build(boxes, clayVoxelSize, clayCornerRadius, clayInset);
            if (key != null && mesh != null) clayCache[key] = mesh;
        }
        if (mesh == null) return null;

        foreach (Renderer r in hide)
            if (r is MeshRenderer) r.enabled = false;
        var clay = new GameObject("Clay");
        clay.transform.SetParent(owner, false);
        clay.AddComponent<MeshFilter>().sharedMesh = mesh;
        clay.AddComponent<MeshRenderer>().sharedMaterial = ClayMaterial();
        return clay;
    }

    // Grid'den odun parçasının kutusu (çağıranın uzayında): start = ilk hücrenin ortası, axis = uzandığı yön (birim
    // vektör), length = hücre sayısı. Uzunluk boyunca hücre sınırlarında; kesit clayThickness. reachBack: bağlandığı
    // dik oduna kadar yarım hücre geri uzanır (yan parçalar: arada boşluk kalmasın).
    public Bounds PieceBox(Vector3 start, Vector3 axis, int length, bool reachBack)
    {
        Vector3 from = start - axis * (reachBack ? 1f : 0.5f);
        Vector3 to = start + axis * (length - 0.5f);
        Vector3 along = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
        Vector3 size = along * Vector3.Distance(from, to) + (Vector3.one - along) * clayThickness;
        return new Bounds((from + to) * 0.5f, size);
    }

    // Renk malzemede (MaterialPropertyBlock'la değil: birleşme sallanması kendi bloğunu yazıp siliyor)
    private Material ClayMaterial()
    {
        if (clayMaterial != null) return clayMaterial;
#if UNITY_EDITOR
        if (fallbackClay == null)
        {
            fallbackClay = new Material(Shader.Find("Universal Render Pipeline/Lit")); // sadece editörde
            fallbackClay.SetColor("_BaseColor", clayColor);
        }
        return fallbackClay;
#else
        Debug.LogWarning("WoodMerger: Clay Material atanmamış.", this);
        return null;
#endif
    }

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