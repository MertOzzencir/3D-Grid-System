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
    [Tooltip("Odun elde tutulurken çevresindeki hücrelerin 3 boyutlu göstergesi")]
    [SerializeField] private HeldGridIndicator heldGrid = new HeldGridIndicator();

    [Header("Sökme (balta)")]
    [Tooltip("Baltanın ek yerine göre yeri (birim, kameraya göre: X sağ, Y yukarı, Z kameradan uzağa / geriye)")]
    [SerializeField] private Vector3 detachAxeOffset = new Vector3(0f, 0.5f, -1f);
    [Tooltip("Baltanın ek yerindeki dönüşü (derece, kameraya göre: kamera döndükçe balta da döner). Play'deyken değiştirince " +
             "anında görünür; istenen duruş bulununca prefab'a yazılır.")]
    [SerializeField] private Vector3 detachAxeRotation = new Vector3(45f, 0f, 0f);
    [Tooltip("Sökülen parça, kalan odunun hücrelerinden bu kadar hücre öteye kadar konabileceği boş yer arar")]
    [SerializeField, Range(1, 5)] private int detachSearchRange = 3;
    [Tooltip("Sökülen parçanın yeni yerine zıplama süresi (saniye)")]
    [SerializeField] private float detachHopDuration = 0.5f;
    [Tooltip("Zıplamanın en yüksek noktası (birim)")]
    [SerializeField] private float detachHopHeight = 1f;

    public WoodCatalogSO Catalog => catalog;
    public DitherFade FrontFade => frontFade;
    public WoodRope Rope => rope;
    public float TargetReleasePixels => targetReleasePixels;
    public GameObject SnapIndicatorPrefab => snapIndicatorPrefab;
    public Vector3 DetachAxeOffset => detachAxeOffset;
    public Vector3 DetachAxeRotation => detachAxeRotation;

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

    // target hem GridPlaceable hem IWoodStack olmalı (Wood ya da MergedWood)
    // carried: tek 1×1 (Wood) ya da yanları boş dik yığın (MergedWood.IsStack), carriedLength boyu
    public void Merge(GridPlaceable target, GridPlaceable carried, int carriedLength, WoodSlot slot)
    {
        var stack = (IWoodStack)target;

        // Hedef silinmeden önce sonucu hesapla
        Vector3 position = target.OriginWorldPosition;
        GridMaskRotator.Rotation rotation = target.Rotation;
        int baseLength = stack.BaseLength;
        int[] segments = stack.BaseSegments;
        int[][] sides;
        int[][][] sideSegments;

        if (slot.IsTop)
        {
            baseLength += carriedLength;
            sides = WoodLayout.SidesAfterTopMerge(stack, carriedLength);
            sideSegments = WoodLayout.SideSegmentsAfterTopMerge(stack, carriedLength);
            segments = WoodLayout.SegmentsAfterTopMerge(segments, carriedLength);
        }
        else if (slot.IsEnd)
        {
            // Yan odunun ucuna: o yan odun uzar (tepedeki gibi: eklenen 1×1 ucuna iple bağlanır)
            sides = WoodLayout.CopySides(stack);
            sideSegments = WoodLayout.CopySideSegments(stack);
            int length = sides[(int)slot.Side][slot.Layer - 1];
            int[] old = sideSegments[(int)slot.Side][slot.Layer - 1] ?? new[] { length };
            sideSegments[(int)slot.Side][slot.Layer - 1] = WoodLayout.SegmentsAfterTopMerge(old, carriedLength);
            sides[(int)slot.Side][slot.Layer - 1] = length + carriedLength;
        }
        else
        {
            sides = WoodLayout.CopySides(stack);
            sideSegments = WoodLayout.CopySideSegments(stack);
            sides[(int)slot.Side][slot.Layer - 1] = carriedLength;
        }

        // Önceden bağlı olan ip (sonuç aynı yerde, aynı dönüşte kurulur): hedefin ipi + taşınan yığının kendi ek yerleri (yan
        // kola yatınca sonuçta yatay ek yeri olurlar ama zaten bağlıydılar). Sadece parçanın hedefe bağlandığı ip sarılarak
        // gelir. Tepeye eklenen yığının ek yerleri dikey: ip yok.
        var previousRope = new System.Collections.Generic.List<WoodRope.Face>();
        if (target is MergedWood oldMerged) previousRope.AddRange(oldMerged.RopeFaces);
        if (!slot.IsTop && carriedLength > 1)
        {
            Vector3 outward = WoodLayout.SideDirection(slot.Side);
            Vector3 start = WoodLayout.PieceStart(stack.BaseLength, WoodSlot.At(slot.Side, slot.Layer));
            int first = slot.IsEnd ? stack.PieceAt(slot.Side, slot.Layer) : 0; // taşınan parçanın kol üstündeki ilk yeri
            for (int k = 1; k < carriedLength; k++)
                previousRope.Add(new WoodRope.Face { center = start + outward * (first + k - 0.5f), normal = outward, up = Vector3.up });
        }

        // Taşınan odun yok olacak: geri yerleştirilmeye çalışılmasın diye iptal değil, sadece bırakıyoruz
        InteractableController.Instance.Release((IInteractable)carried);
        Destroy(carried.gameObject);
        GridManager.Instance.PlaceableRemoveOn(target, true);

        // Yanlarda hiç odun yoksa ve ana odun tek parçaysa sonuç sıradan (tek parça) bir odun
        GridPlaceable result = WoodLayout.IsEmpty(sides) && segments.Length == 1
            ? SpawnLongWood(baseLength)
            : SpawnMergedWood(baseLength, sides, segments, sideSegments);

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
        if (result is MergedWood merged) merged.PlayRopeAnimation(previousRope);
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

    private System.Collections.IEnumerator Hop(Transform piece, Vector3 fromPosition, Quaternion fromRotation)
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

    private GridPlaceable SpawnMergedWood(int baseLength, int[][] sides, int[] segments, int[][][] sideSegments)
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
        merged.Build(baseLength, sides, catalog, segments, sideSegments);
        return merged;
    }
}