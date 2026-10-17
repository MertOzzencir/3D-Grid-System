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
    [Tooltip("Dolu bir komşuya bakan yönde 1BR modeli hücre ortasından bu kadar (birim) uzakta düz uzatılmaya başlar: " +
             "modelin o yöndeki orta kesiti komşuya kadar çekilir, iki odunun yuvarlak kenarları kalkar. Modelin yan yüzünün " +
             "düz kısmında olmalı (küçük = kaynaşma geniş, büyük = modelin kenar yuvarlağı birleşime yaklaşır).")]
    [SerializeField, Range(0.05f, 0.45f)] private float clayExtrudeFrom = 0.3f;
    [Tooltip("Uzatmanın başından hücrenin ortasına doğru 1BR'nin detayının (renk + normal map) geri geldiği genişlik " +
             "(birim). Uzatılan (kaynaşan) kısım dokunun boş, düz renkli yerinden; normal map yok.")]
    [SerializeField, Range(0.01f, 0.4f)] private float clayFuseWidth = 0.1f;
    [Tooltip("Kil şeklinin malzemesi. Shader'ı 'Village/Clay Wood Lit' olmalı; dokular 1BR odunun dokuları (Base Map = renk, " +
             "Normal Map = normal), görünüm her hücredeki hayali 1BR'den alınır. Build'de gerekli.")]
    [SerializeField] private Material clayMaterial;
    [Tooltip("Clay Material boşken (sadece editörde) kullanılan düz malzemenin rengi")]
    [SerializeField] private Color clayColor = new Color(0.78f, 0.58f, 0.38f);

    private static readonly System.Collections.Generic.Dictionary<string, Mesh> clayCache =
        new System.Collections.Generic.Dictionary<string, Mesh>();
    private Material fallbackClay;
    private bool warnedClayShader;
    private ClayMesher.Reference clayReference;
    private bool clayReferenceTried;

    // Kaynaşmış kil şeklini owner'ın çocuğu olarak takar, verilen renderer'ları gizler. boxes owner'ın uzayında.
    // cacheKey: aynı şekli tekrar hesaplamamak için (tek odunlar). Kil görünüm kapalıysa null.
    public GameObject AttachClay(Transform owner, System.Collections.Generic.List<ClayMesher.Piece> pieces, Renderer[] hide, string cacheKey = null)
    {
        if (!clayLook || pieces.Count == 0) return null;
        string key = cacheKey == null ? null : $"{cacheKey}|{clayVoxelSize}|{clayExtrudeFrom}|{clayFuseWidth}";
        if (key == null || !clayCache.TryGetValue(key, out Mesh mesh) || mesh == null)
        {
            mesh = ClayMesher.Build(pieces, ClayReference(), clayVoxelSize, clayExtrudeFrom, clayFuseWidth);
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

    // Kil şekli için parça: hücreleri (1BR bunlara rotation ile oturur)
    public ClayMesher.Piece ClayPiece(Vector3 start, Quaternion rotation, int length) =>
        new ClayMesher.Piece { start = start, rotation = rotation, length = length };

    // Şeklin ve görünümün alındığı 1BR: katalogdaki 1 boyluk odunun modeli, kökünün (= hücrenin ortası) uzayında. Mesh'in
    // Read/Write'ı açık olmalı (build'de okunabilsin).
    private ClayMesher.Reference ClayReference()
    {
        if (clayReferenceTried) return clayReference;
        clayReferenceTried = true;
        Wood wood = catalog != null ? catalog.GetWood(1) : null;
        MeshFilter filter = wood != null ? wood.GetComponentInChildren<MeshFilter>(true) : null;
        if (filter == null || filter.sharedMesh == null)
        {
            Debug.LogWarning("WoodMerger: katalogda 1BR odun ya da modeli yok; kil şekli kurulamaz.", this);
            return null;
        }
        if (!filter.sharedMesh.isReadable)
        {
            Debug.LogWarning($"WoodMerger: 1BR modelinin ({filter.sharedMesh.name}) Read/Write'ı kapalı; fbx'in Import ayarlarında aç (kil şekli kurulamaz).", this);
            return null;
        }
        Matrix4x4 toCell = wood.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
        clayReference = new ClayMesher.Reference(filter.sharedMesh, toCell, filter.sharedMesh.GetInstanceID());
        return clayReference;
    }

    // Renk malzemede (MaterialPropertyBlock'la değil: birleşme sallanması kendi bloğunu yazıp siliyor)
    private Material ClayMaterial()
    {
        if (clayMaterial != null)
        {
            if (clayMaterial.shader.name != "Village/Clay Wood Lit" && !warnedClayShader)
            {
                warnedClayShader = true;
                Debug.LogWarning("WoodMerger: Clay Material'ın shader'ı 'Village/Clay Wood Lit' değil; kaynaşma yerleri sade " +
                                 "olmaz (Wood Wobble Lit değilse birleşince sallanmaz da).", this);
            }
            return clayMaterial;
        }
#if UNITY_EDITOR
        if (fallbackClay == null)
        {
            // Sadece editörde: kil odun shader'ı, düz renk (yoksa düz URP Lit)
            Shader shader = Shader.Find("Village/Clay Wood Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            fallbackClay = new Material(shader);
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

    private void PlayWobble(GridPlaceable result, Vector3 push) => StartCoroutine(PlayWobbleNextFrame(result, push));

    // Bir kare sonra: tek parça Wood'un kil şekli Start'ta kuruluyor, sallanacak renderer'lar arasında olsun
    private System.Collections.IEnumerator PlayWobbleNextFrame(GridPlaceable result, Vector3 push)
    {
        yield return null;
        if (result == null) yield break;
        GridFootprint footprint = result.PlacedFootprint;
        // Pivot alt hücrenin ortasında; taban yarım hücre aşağıda
        Vector3 basePoint = result.transform.position + Vector3.up * (footprint.Min.y - 0.5f);
        yield return mergeWobble.Play(result.GetComponentsInChildren<Renderer>(), basePoint, footprint.Size.y, push);
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