using UnityEngine;

// Hedef odunun (Wood ya da MergedWood) bir bağlantı yerine tek parça odun ekler:
//   tepe → ana odun uzar; dört yanın katmanları birlikte kayar. Yanlarda hiç odun yoksa sonuç tek parça Wood.
//   yan  → MergedWood (hedefin mevcut katmanları korunur, seçilen katman dolar)
// Sökme (Detach): MergedWood'un bir ek yerinden ayrılan kısım kalan odunun çevresindeki en uygun boş yere zıplar.
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
    [Tooltip("Taşınan odun bir hedefe yapıştıktan sonra (bağlantı yeri WASD ile seçilir) mouse, yapıştığı andaki yerinden " +
             "ekranda bu kadar (piksel) uzaklaşınca bırakılır: birleştirmekten vazgeçildi.")]
    [SerializeField] private float targetReleasePixels = 150f;
    [Tooltip("Yan parçaların ana oduna bağlandığı yere sarılan ip")]
    [SerializeField] private WoodRope rope = new WoodRope();
    [Tooltip("Kol ile derinlik odunu arasındaki 45° çivi (prototip; model atanabilir)")]
    [SerializeField] private WoodNail nail = new WoodNail();
    [Tooltip("Odun elde tutulurken çevresindeki hücrelerin 3 boyutlu göstergesi")]
    [SerializeField] private HeldGridIndicator heldGrid = new HeldGridIndicator();

    [Header("Sökme (balta)")]
    [Tooltip("Baltanın ek yerine göre yeri (birim, kameraya göre: X sağ, Y yukarı, Z kameradan uzağa / geriye)")]
    [SerializeField] private Vector3 detachAxeOffset = new Vector3(0f, 0.5f, -1f);
    [Tooltip("Baltanın ek yerindeki dönüşü (derece, kameraya göre: kamera döndükçe balta da döner). Play'deyken değiştirince " +
             "anında görünür; istenen duruş bulununca prefab'a yazılır.")]
    [SerializeField] private Vector3 detachAxeRotation = new Vector3(45f, 0f, 0f);
    [Tooltip("Ana odunun ek yerlerinde (üst üste 1×1'lerin arası, yatay çizgi) baltanın yeri ve dönüşü: balta yatay durur. " +
             "Üstteki iki ayar yan kolların ek yerleri (dikey çizgi) için. Play'deyken değiştirince anında görünür.")]
    [SerializeField] private Vector3 detachAxeBaseOffset = new Vector3(0f, 0f, -1f);
    [SerializeField] private Vector3 detachAxeBaseRotation = new Vector3(0f, 0f, 90f);
    [Tooltip("Sökülen parça, kalan odunun hücrelerinden bu kadar hücre öteye kadar konabileceği boş yer arar")]
    [SerializeField, Range(1, 5)] private int detachSearchRange = 3;
    [Tooltip("Sökülen parçanın yeni yerine zıplama süresi (saniye)")]
    [SerializeField] private float detachHopDuration = 0.5f;
    [Tooltip("Zıplamanın en yüksek noktası (birim)")]
    [SerializeField] private float detachHopHeight = 1f;

    public WoodCatalogSO Catalog => catalog;
    public DitherFade FrontFade => frontFade;
    public WoodRope Rope => rope;
    public WoodNail Nail => nail;
    public float TargetReleasePixels => targetReleasePixels;
    public GameObject SnapIndicatorPrefab => snapIndicatorPrefab;
    public Vector3 DetachAxeOffset => detachAxeOffset;
    public Vector3 DetachAxeRotation => detachAxeRotation;
    public Vector3 DetachAxeBaseOffset => detachAxeBaseOffset;
    public Vector3 DetachAxeBaseRotation => detachAxeBaseRotation;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void Update()
    {
        heldGrid.Tick(Time.deltaTime);
        // Birleştirirken / sökerken kamera hedef odunu yavaşça ortalar (E / Q onun etrafında döner)
        IWoodSlotView active = WoodMergeTarget.Active != null ? WoodMergeTarget.Active : (IWoodSlotView)WoodDetachTarget.Active;
        bool busy = active != null && active.IsAlive;
        CameraController.SetFocus(busy ? active.FocusPoint : (Vector3?)null);
        // Birleştirirken / sökerken WASD bağlantı / ek yerlerini gezer: kamera hareket etmesin
        CameraController.MovementLocked = busy;
    }

    // target hem GridPlaceable hem IWoodStack olmalı (Wood ya da MergedWood). carried: tek 1×1 (Wood) ya da dik yığın
    // (MergedWood.IsStack), carriedLength boyu. cell: eklenen parçanın ilk hücresi, normal: uzandığı yön (dünya; yapıştığı
    // yüzden dışarı). Sonuç (yeni yapı) aynı yerde ve dönüşte kurulur; döndürür (olmadıysa null).
    public GridPlaceable Merge(GridPlaceable target, GridPlaceable carried, int carriedLength, Vector3Int cell, Vector3Int normal)
    {
        var stack = (IWoodStack)target;

        // Sonucu yapının yerel uzayında hesapla
        Vector3Int position = target.OriginWorldPosition;
        GridMaskRotator.Rotation rotation = target.Rotation;
        var inverse = (GridMaskRotator.Rotation)((4 - (int)rotation) % 4);
        Vector3Int start = GridMaskRotator.RotateOffset(cell - position, inverse);
        Vector3Int direction = GridMaskRotator.RotateOffset(normal, inverse);

        var shape = new System.Collections.Generic.List<MergedWood.Cell>(); // sadece Wood hedefte kullanılır
        if (!(target is MergedWood))
            foreach (Vector3Int local in stack.LocalCells) shape.Add(new MergedWood.Cell(local, Vector3Int.up));
        for (int i = 0; i < carriedLength; i++)
            shape.Add(new MergedWood.Cell(start + direction * i, direction));


        // Taşınan odun yok olacak: geri yerleştirilmeye çalışılmasın diye iptal değil, sadece bırakıyoruz
        InteractableController.Instance.Release((IInteractable)carried);
        Destroy(carried.gameObject);

        GridPlaceable result;
        if (target is MergedWood grown)
        {
            // Artımlı (kullanıcı: baştan kurmak ms spike yapıyordu): aynı obje kalır, sadece yeni hücreler eklenir, grid'deki
            // footprint yerinde genişletilir
            var added = new System.Collections.Generic.List<MergedWood.Cell>();
            for (int i = 0; i < carriedLength; i++) added.Add(new MergedWood.Cell(start + direction * i, direction));
            grown.AddCells(added, catalog);
            grown.name = $"MergedWood {grown.Cells.Count}";
            if (!GridManager.Instance.TryMovePlaceable(grown, position, grown.GetFootprint()))
                Debug.LogWarning($"{grown.name} grid'de genişletilemedi", grown);
            result = grown;
        }
        else
        {
            // Tek odun / dik yığın (Wood) ilk birleşmede MergedWood'a dönüşür
            GridManager.Instance.PlaceableRemoveOn(target, true);
            result = shape.Count == 1 ? SpawnLongWood(1) : SpawnMergedWood(shape);
            result.Rotation = rotation;
            if (!GridManager.Instance.PlaceablePlaceOn(result, position))
            {
                Debug.LogWarning($"{result.name} grid'e yerleştirilemedi", result);
                return null;
            }
        }

        // Parçanın geldiği yönden itilmiş gibi: yandan eklenen parça o yandan iter → obje ters yöne eğilir.
        // Üstten gelen parça yukarıdan bastırır → eğilme yok, sadece basılıp yaylanır.
        Vector3 push = direction.y != 0 ? Vector3.zero : -(result.GridRotation * (Vector3)direction);
        PlayWobble(result, push);
        if (result is MergedWood merged)
        {
            // Sadece bu birleştirmede kurulan ip parçaları ve çiviler oynar
            merged.PlayRopeAnimation();
            merged.PlayNailAnimation();
        }
        return result;
    }

    // Ek yerinden söker: ana odunun parçaları arasıysa üstteki kısım (o yükseklikteki yan odunlarıyla) ayrılır, yan odunun
    // bağlandığı yerse o yan odun, yan odunun parçaları arasıysa dıştaki kısmı. Kalan yerinde kalır (yanı boş, tek parçaysa
    // sıradan Wood), ayrılan kısım dik olarak kalanın çevresindeki en uygun boş yere (TryFindDropCell) zıplar. Yer yoksa
    // hiçbir şey değişmez (false).
    public bool Detach(MergedWood target, WoodJoint joint)
    {
        int baseLength = target.BaseLength;
        int[] segments = target.BaseSegments;
        int[][] sides = WoodLayout.CopySides(target);
        int[][][] sideSegments = WoodLayout.CopySideSegments(target);

        int keepLength = baseLength;
        int[] keepSegments = segments;
        int[][] keepSides = sides;
        int[][][] keepSideSegments = sideSegments;
        int dropLength;
        int[] dropSegments;
        int[][] dropSides;
        int[][][] dropSideSegments = null;
        Vector3Int dropStart;       // ayrılan kısmın şu anki ilk hücresi (yerel)
        Quaternion dropRotation;    // ve dönüşü (yerel)
        Vector3 dropAxis;           // uzandığı yön (yerel)

        if (joint.IsBase)
        {
            // Yükseklik h'nin altı kalır, üstü ayrılır. Kat numarası = ana odun boyu - satır.
            int h = 0;
            for (int k = 0; k < joint.Index; k++) h += segments[k];
            keepLength = h;
            dropLength = baseLength - h;
            keepSegments = segments[..joint.Index];
            dropSegments = segments[joint.Index..];
            keepSides = WoodLayout.EmptySides(keepLength);
            dropSides = WoodLayout.EmptySides(dropLength);
            keepSideSegments = new int[WoodLayout.SideCount][][];
            dropSideSegments = new int[WoodLayout.SideCount][][];
            for (int s = 0; s < WoodLayout.SideCount; s++)
            {
                keepSideSegments[s] = new int[keepLength][];
                dropSideSegments[s] = new int[dropLength][];
                for (int layer = 1; layer <= baseLength; layer++)
                {
                    int row = WoodLayout.SideRow(baseLength, layer);
                    if (row < h)
                    {
                        int keepLayer = keepLength - row;
                        keepSides[s][keepLayer - 1] = sides[s][layer - 1];
                        keepSideSegments[s][keepLayer - 1] = sideSegments[s][layer - 1];
                    }
                    else
                    {
                        int dropLayer = dropLength - (row - h);
                        dropSides[s][dropLayer - 1] = sides[s][layer - 1];
                        dropSideSegments[s][dropLayer - 1] = sideSegments[s][layer - 1];
                    }
                }
            }
            dropStart = new Vector3Int(0, h, 0);
            dropRotation = Quaternion.identity;
            dropAxis = Vector3.up;
        }
        else
        {
            int s = (int)joint.Side, l = joint.Layer - 1;
            int length = sides[s][l];
            int[] pieceSegments = WoodLayout.PieceSegments(target, joint.Side, joint.Layer);
            int offset = 0;
            for (int k = 0; k < joint.Index; k++) offset += pieceSegments[k];
            dropLength = length - offset;
            dropSegments = pieceSegments[joint.Index..];
            dropSides = WoodLayout.EmptySides(dropLength);
            sides[s][l] = offset;
            sideSegments[s][l] = joint.Index > 1 ? pieceSegments[..joint.Index] : null;

            WoodSlot slot = WoodSlot.At(joint.Side, joint.Layer);
            dropStart = WoodLayout.PieceStart(baseLength, slot) + WoodLayout.SideDirection(joint.Side) * offset;
            dropRotation = WoodLayout.PieceRotation(slot);
            dropAxis = WoodLayout.SideDirection(joint.Side);
        }

        GridManager grid = GridManager.Instance;
        Vector3Int position = target.OriginWorldPosition;
        GridMaskRotator.Rotation rotation = target.Rotation;
        Vector3 fromPosition = (Vector3)position + target.GridRotation * (Vector3)dropStart;
        Quaternion fromRotation = target.GridRotation * dropRotation;
        Vector3 fromCenter = fromPosition + target.GridRotation * dropAxis * ((dropLength - 1) * 0.5f);

        grid.PlaceableRemoveOn(target, false);
        GridPlaceable keep = Spawn(keepLength, keepSides, keepSegments, keepSideSegments);
        GridPlaceable drop = Spawn(dropLength, dropSides, dropSegments, dropSideSegments);
        keep.Rotation = rotation;
        drop.Rotation = rotation;

        bool keepPlaced = grid.PlaceablePlaceOn(keep, position);
        if (!keepPlaced || !TryFindDropCell(drop, keep, fromCenter, out Vector3Int cell) || !grid.PlaceablePlaceOn(drop, cell))
        {
            // Geri al: hedef olduğu gibi kalır
            if (keepPlaced) grid.PlaceableRemoveOn(keep, false);
            Destroy(keep.gameObject);
            Destroy(drop.gameObject);
            grid.PlaceablePlaceOn(target, position);
            return false;
        }

        Destroy(target.gameObject);
        StartCoroutine(Hop(drop.transform, fromPosition, fromRotation));
        return true;
    }

    // Yanlarda odun yoksa ve ana odun tek parçaysa sıradan (tek parça) odun, yoksa MergedWood
    private GridPlaceable Spawn(int baseLength, int[][] sides, int[] segments, int[][][] sideSegments)
        => WoodLayout.IsEmpty(sides) && segments.Length == 1
            ? SpawnLongWood(baseLength)
            : SpawnMergedWood(baseLength, sides, segments, sideSegments);

    // Ayrılan kısmın konacağı yer: kalan odunun hücrelerinden detachSearchRange öteye kadar, konabildiği (bütün şekli
    // sığar, base'e girmez, altı dolu) hücrelerden ayrılan kısmın şu anki yerine yatayda en yakın olanı; kalanın tabanıyla
    // aynı kat tercih edilir (bir kat aşağı / yukarı da olur)
    private bool TryFindDropCell(GridPlaceable drop, GridPlaceable keep, Vector3 from, out Vector3Int cell)
    {
        cell = default;
        Vector3Int min = keep.OriginWorldPosition + keep.PlacedFootprint.Min;
        Vector3Int max = keep.OriginWorldPosition + keep.PlacedFootprint.Max;
        Vector3 dropCenter = drop.GetFootprint().Center;
        float best = float.MaxValue;
        for (int x = min.x - detachSearchRange; x <= max.x + detachSearchRange; x++)
            for (int z = min.z - detachSearchRange; z <= max.z + detachSearchRange; z++)
                for (int y = min.y - 1; y <= min.y + 1; y++)
                {
                    var candidate = new Vector3Int(x, y, z);
                    if (!HeldGridIndicator.CanHoldAt(drop, candidate, true)) continue;
                    Vector3 center = candidate + dropCenter;
                    float score = new Vector2(center.x - from.x, center.z - from.z).magnitude + Mathf.Abs(y - min.y);
                    if (score < best)
                    {
                        best = score;
                        cell = candidate;
                    }
                }
        return best < float.MaxValue;
    }

    // Ayrılan kısım eski yerinden (yatıksa yatık) yeni yerine kavis çizerek zıplar, yolda dikleşir (ağaçtan düşen odun gibi)
    // Grid'e yerleşmiş bir parçayı fromPosition / fromRotation'dan şu anki yerine zıplatır (örn. ağaçtan düşen odun yığını)
    public void PlayHop(Transform piece, Vector3 fromPosition, Quaternion fromRotation)
        => StartCoroutine(Hop(piece, fromPosition, fromRotation));

    // Havada uçan (zıplama / doğma animasyonundaki) odunlar: birleştirme ve sökme hedefi olmaz, kamera onlara odaklanmaz
    // (kullanıcı: ağaç keserken çıkan odun havadayken kamera ona kayıyordu)
    private static readonly System.Collections.Generic.HashSet<Transform> hopping = new System.Collections.Generic.HashSet<Transform>();

    public static bool IsLanding(GridPlaceable piece)
        => piece != null && (hopping.Contains(piece.transform) || (piece is SourceBase source && source.IsSpawning));

    private System.Collections.IEnumerator Hop(Transform piece, Vector3 fromPosition, Quaternion fromRotation)
    {
        hopping.Add(piece);
        try
        {
            yield return HopMotion(piece, fromPosition, fromRotation);
        }
        finally
        {
            hopping.Remove(piece);
        }
    }

    private System.Collections.IEnumerator HopMotion(Transform piece, Vector3 fromPosition, Quaternion fromRotation)
    {
        Vector3 toPosition = piece.position;
        Quaternion toRotation = piece.rotation;
        for (float elapsed = 0f; elapsed < detachHopDuration; elapsed += Time.deltaTime)
        {
            if (piece == null) yield break;
            float t = elapsed / detachHopDuration;
            Vector3 arc = Vector3.up * (detachHopHeight * 4f * t * (1f - t));
            piece.SetPositionAndRotation(Vector3.Lerp(fromPosition, toPosition, t) + arc, Quaternion.Slerp(fromRotation, toRotation, t));
            yield return null;
        }
        if (piece != null) piece.SetPositionAndRotation(toPosition, toRotation);
    }

    private void PlayWobble(GridPlaceable result, Vector3 push)
    {
        GridFootprint footprint = result.PlacedFootprint;
        // Pivot alt hücrenin ortasında; taban yarım hücre aşağıda
        Vector3 basePoint = result.transform.position + Vector3.up * (footprint.Min.y - 0.5f);
        StartCoroutine(mergeWobble.Play(result.GetComponentsInChildren<Renderer>(), basePoint, footprint.Size.y, push));
    }

    // length kadar dik odun: 1 ise tek 1×1 Wood, daha uzunsa iple bağlı 1×1'lerden yığın (yanları boş MergedWood).
    // Grid'e konmamış döner.
    public GridPlaceable SpawnWoodStack(int length)
        => length <= 1 ? SpawnLongWood(1) : SpawnMergedWood(length, WoodLayout.EmptySides(length), null, null);

    private GridPlaceable SpawnLongWood(int length)
    {
        Wood wood = Instantiate(catalog.GetWood(length));
        wood.OnSpawned();
        return wood;
    }

    private GridPlaceable SpawnMergedWood(System.Collections.Generic.IEnumerable<MergedWood.Cell> shape)
    {
        MergedWood merged = NewMergedWood();
        merged.BuildCells(shape, catalog);
        merged.name = $"MergedWood {merged.Cells.Count}";
        return merged;
    }

    private MergedWood NewMergedWood()
    {
        if (mergedWoodPrefab != null) return Instantiate(mergedWoodPrefab);
        Debug.LogWarning("WoodMerger: Merged Wood Prefab atanmamış; oluşan obje kaydedilemeyecek.", this);
        return new GameObject().AddComponent<MergedWood>();
    }

    // Eski kol yapısından (sökme ve dik yığın kullanır)
    private GridPlaceable SpawnMergedWood(int baseLength, int[][] sides, int[] segments, int[][][] sideSegments)
    {
        MergedWood merged = NewMergedWood();
        merged.name = $"MergedWood {baseLength}BR";
        merged.Build(baseLength, sides, catalog, segments, sideSegments);
        return merged;
    }
}