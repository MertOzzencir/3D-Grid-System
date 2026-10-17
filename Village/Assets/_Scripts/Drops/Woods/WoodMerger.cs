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
    [Tooltip("Açıksa bütün odunlar (tek odun ve birleştirilmiş yapılar) her hücreye konan 1BR modelinden ve değme " +
             "yerlerindeki yumuşak dolgudan oluşan tek mesh olarak çizilir (ClayMesher). Odun kurulurken bir kez üretilir.")]
    [SerializeField] private bool clayLook = true;
    [Tooltip("Dolgunun ızgara aralığı (birim): küçük = daha pürüzsüz dolgu ama üretimi daha yavaş (0.04 önerilir)")]
    [SerializeField] private float clayVoxelSize = 0.04f;
    [Tooltip("Değen iki odun arasındaki oluğun ne kadar dolacağı (birim): yumuşak birleşimin genişliği. Dolgu en fazla " +
             "bunun dörtte biri kadar kalınlaşır (oluğun derinliği ~0.1 ise ~0.4 gerekir). Büyük = kalın, yumuşak dolgu.")]
    [SerializeField, Range(0.01f, 0.4f)] private float clayBlend = 0.25f;
    [Tooltip("Dolgunun 1BR dokusundaki yeri (UV, 0..1): dokunun hiçbir yüzün kullanmadığı, düz odun renkli bir noktası. " +
             "Dolgu buradan renk alır, normal map'i düz olmalı.")]
    [SerializeField] private Vector2 clayFillUv = new Vector2(0.2f, 0.85f);
    [Tooltip("Boşsa 1BR odunun kendi malzemesi kullanılır (önerilen). Shader'ı 'Village/Wood Wobble Lit' olmalı ki " +
             "birleşince sallansın.")]
    [SerializeField] private Material clayMaterial;

    private static readonly System.Collections.Generic.Dictionary<string, Mesh> clayCache =
        new System.Collections.Generic.Dictionary<string, Mesh>();
    private bool warnedClayShader;
    private ClayMesher.Reference clayReference;
    private bool clayReferenceTried;

    // Kaynaşmış kil şeklini owner'ın çocuğu olarak takar, verilen renderer'ları gizler. boxes owner'ın uzayında.
    // cacheKey: aynı şekli tekrar hesaplamamak için (tek odunlar). Kil görünüm kapalıysa null.
    public GameObject AttachClay(Transform owner, System.Collections.Generic.List<ClayMesher.Piece> pieces, Renderer[] hide, string cacheKey = null)
    {
        if (!clayLook || pieces.Count == 0) return null;
        string key = cacheKey == null ? null : $"{cacheKey}|{clayVoxelSize}|{clayBlend}|{clayFillUv}";
        if (key == null || !clayCache.TryGetValue(key, out Mesh mesh) || mesh == null)
        {
            mesh = ClayMesher.Build(pieces, ClayReference(), clayVoxelSize, clayBlend, clayFillUv);
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

    // Hücrelere konan ve dolgunun hesaplandığı 1BR: katalogdaki 1 boyluk odunun modeli, kökünün (= hücrenin ortası) uzayında. Mesh'in
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

    // Clay Material ya da 1BR odunun kendi malzemesi
    private Material ClayMaterial()
    {
        Material material = clayMaterial;
        if (material == null)
        {
            Wood wood = catalog != null ? catalog.GetWood(1) : null;
            Renderer renderer = wood != null ? wood.GetComponentInChildren<MeshRenderer>(true) : null;
            material = renderer != null ? renderer.sharedMaterial : null;
        }
        if (material != null && material.shader.name != "Village/Wood Wobble Lit" && !warnedClayShader)
        {
            warnedClayShader = true;
            Debug.LogWarning("WoodMerger: odun malzemesinin shader'ı 'Village/Wood Wobble Lit' değil; birleşince sallanmaz.", this);
        }
        return material;
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