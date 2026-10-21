using System;
using System.Collections.Generic;
using UnityEngine;

// Yanlarında odun olan dik odun: ana (base) odun + dört yanın katmanlarındaki yatık parçalar.
// Boş katmanlarına tek parça odun eklenebilir (hedef); kendisi başka bir oduna eklenemez ama taşınıp döndürülebilir.
public class MergedWood : GridPlaceable, IInteractable, IStickyToolTarget, IBlueprintPiece, IWoodStack, ISaveState
{
    // Unity iç içe dizi (int[][]) kaydedemediği için her yan bu sınıfla tutulur
    [Serializable]
    private class Side
    {
        public int[] layers; // [katman - 1] = o katmandaki odunun uzunluğu, 0 = boş
        public string[] segments; // [katman - 1] = o odunun parçaları "3,2" (içten dışa); boş = tek parça (eski kayıtta yok)
    }

    // Kayıttaki durumu: prefab'ı boş, görseli bu değerlerden yeniden kurulur
    [Serializable]
    private class State
    {
        public int baseLength;
        public int[] segments; // ana odunun parçaları (aşağıdan yukarı); eski kayıtta yok
        public Side[] sides;
        public List<int> layers; // eski format (sadece sağ yan): [0] = tepe, [k] = sağ yandaki k. katman
    }

    private const float ColliderThickness = 0.75f; // sadece görselde mesh bulunamazsa

    [SerializeField] private float followSpeed = 10f;

    [SerializeField] private int baseLength;
    [SerializeField] private int[] segments = new int[0]; // ana odunun parçaları (aşağıdan yukarı)
    [SerializeField] private Side[] sides = new Side[0];

    private GridDragMotor drag;
    private WoodMergeTarget mergeTarget;
    private GridFootprint[] footprints; // rotasyon başına bir kez hesaplanır
    private string signature;           // şekil değişmedikçe aynı
    private int signatureSteps;
    private readonly List<Renderer>[] sideRenderers = NewSideRenderers();
    private GameObject rope;
    private List<WoodRope.Face> ropeFaces = new List<WoodRope.Face>(); // ipin değme yüzleri (yerel)

    public List<WoodRope.Face> RopeFaces => ropeFaces;

    public int BaseLength => baseLength;
    public int[] BaseSegments => segments != null && segments.Length > 0 ? (int[])segments.Clone() : WoodLayout.DefaultSegments(baseLength);

    public Renderer[] PieceRenderers(WoodSide side) => sideRenderers[(int)side].ToArray();

    // İp sarılarak gelsin (birleştirme anında; kayıttan yüklenince ip hazır). previous: birleşmeden önceki yapının değme
    // yüzleri (aynı uzayda; tek odunsa null): sadece yeni eklenen kenarlar oynar
    public void PlayRopeAnimation(List<WoodRope.Face> previous)
    {
        if (rope == null || !rope.TryGetComponent(out RopeGrow grow) || WoodMerger.Instance == null) return;
        var added = new List<(Vector3, Vector3)>();
        var existing = new List<(Vector3, Vector3)>();
        WoodMerger.Instance.Rope.Changes(ropeFaces, previous, added, existing);
        grow.Play(added, existing);
    }

    private static List<Renderer>[] NewSideRenderers()
    {
        var result = new List<Renderer>[WoodLayout.SideCount];
        for (int s = 0; s < result.Length; s++)
            result[s] = new List<Renderer>();
        return result;
    }

    public int[] SideSegments(WoodSide side, int layer)
    {
        int s = (int)side;
        if (s >= sides.Length || sides[s]?.segments == null || layer < 1 || layer > sides[s].segments.Length) return null;
        return ParseSegments(sides[s].segments[layer - 1]);
    }

    private static int[] ParseSegments(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        string[] parts = text.Split(',');
        var result = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], out result[i])) return null;
        return result;
    }

    // Yan odunun parçaları: kayıtlı ve toplamı uzunluğa eşitse o, yoksa doğal modellerden
    private static int[] SegmentsFor(int[] stored, int length)
    {
        if (stored != null)
        {
            int sum = 0;
            foreach (int segment in stored) sum += segment;
            if (sum == length) return stored;
        }
        return WoodLayout.DefaultSegments(length);
    }

    public int PieceAt(WoodSide side, int layer)
    {
        int s = (int)side;
        if (s >= sides.Length || sides[s]?.layers == null || layer < 1 || layer > sides[s].layers.Length) return 0;
        return sides[s].layers[layer - 1];
    }

    // Döndürmeden bağımsız: aynı şeklin her yöndeki hali aynı imzayı verir, ayna görüntüsü farklı verir.
    // Örn. 2BR, forward'ın 2. katmanında 1BR → "MergedWood:2|0,1|0,0|0,0|0,0" (standart dönüşte)
    public string BlueprintSignature
    {
        get
        {
            if (signature == null)
                signature = "MergedWood:" + WoodLayout.CanonicalSignature(baseLength, ToArrays(), out signatureSteps);
            return signature;
        }
    }

    // Standart hale gelmek için kaç adım döndürüldüğü (imzayla birlikte hesaplanır)
    public int BlueprintRotationSteps
    {
        get
        {
            _ = BlueprintSignature;
            return signatureSteps;
        }
    }

    private void Awake()
    {
        drag = new GridDragMotor(this, followSpeed);
        mergeTarget = new WoodMergeTarget(this);
    }

    // sides: WoodLayout.EmptySides biçiminde, [yan][katman - 1] = uzunluk (0 = boş)
    // segments: ana odunun parçaları, aşağıdan yukarı (toplamı baseLength); null ise doğal modellerden
    // sideSegments: [yan][katman - 1] = yan odunun parçaları (içten dışa) ya da null (doğal modellerden)
    public void Build(int baseLength, int[][] sides, WoodCatalogSO catalog, int[] segments = null, int[][][] sideSegments = null)
    {
        this.baseLength = baseLength;
        this.segments = segments != null && segments.Length > 0 ? (int[])segments.Clone() : WoodLayout.DefaultSegments(baseLength);
        this.sides = new Side[WoodLayout.SideCount];
        for (int s = 0; s < WoodLayout.SideCount; s++)
        {
            this.sides[s] = new Side { layers = (int[])sides[s].Clone(), segments = new string[sides[s].Length] };
            for (int layer = 1; layer <= sides[s].Length; layer++)
            {
                int[] stored = sideSegments != null && s < sideSegments.Length && sideSegments[s] != null && layer <= sideSegments[s].Length
                    ? sideSegments[s][layer - 1] : null;
                if (sides[s][layer - 1] > 0 && stored != null && stored.Length > 1)
                    this.sides[s].segments[layer - 1] = string.Join(",", SegmentsFor(stored, sides[s][layer - 1]));
            }
        }
        footprints = null;
        signature = null;

        foreach (List<Renderer> list in sideRenderers)
            list.Clear();

        // Ana odun: parçalar üst üste; parçaların ek yerleri ipe bağlanır (yatay değme yüzü)
        ropeFaces = new List<WoodRope.Face>();
        Material woodMaterial = null;
        int height = 0;
        foreach (int segment in this.segments)
        {
            GameObject segmentVisual = AddPiece(catalog.GetWood(segment), new Vector3Int(0, height, 0), Quaternion.identity, segment);
            if (height > 0)
                ropeFaces.Add(new WoodRope.Face { center = new Vector3(0f, height - 0.5f, 0f), normal = Vector3.up, up = Vector3.forward });
            if (woodMaterial == null && segmentVisual != null)
            {
                Renderer renderer = segmentVisual.GetComponentInChildren<Renderer>();
                if (renderer != null) woodMaterial = renderer.sharedMaterial;
            }
            height += segment;
        }
        foreach ((WoodSlot slot, int length) in FilledSlots())
        {
            // Yan odun parçalardan (içten dışa); parçaların ek yerleri de ipe bağlanır
            Vector3Int start = WoodLayout.PieceStart(baseLength, slot);
            Vector3Int outward = WoodLayout.SideDirection(slot.Side);
            GameObject visual = null;
            int offset = 0;
            foreach (int segment in SegmentsFor(SideSegments(slot.Side, slot.Layer), length))
            {
                GameObject segmentVisual = AddPiece(catalog.GetWood(segment), start + outward * offset, WoodLayout.PieceRotation(slot), segment);
                if (segmentVisual != null)
                {
                    visual ??= segmentVisual;
                    sideRenderers[(int)slot.Side].AddRange(segmentVisual.GetComponentsInChildren<Renderer>());
                }
                if (offset > 0)
                    ropeFaces.Add(new WoodRope.Face { center = start + (Vector3)outward * (offset - 0.5f), normal = outward, up = Vector3.up });
                offset += segment;
            }
            if (visual == null) continue;

            // Ana odunla değdiği yüz (ip buraya)
            Vector3 direction = WoodLayout.SideDirection(slot.Side);
            ropeFaces.Add(new WoodRope.Face
            {
                center = (Vector3)WoodLayout.PieceStart(baseLength, slot) - direction * 0.5f,
                normal = direction,
                up = Vector3.up
            });
            if (woodMaterial == null)
            {
                Renderer renderer = visual.GetComponentInChildren<Renderer>();
                if (renderer != null) woodMaterial = renderer.sharedMaterial;
            }
        }

        // Bütün değme yerlerine tek parça ip (üst üste binmez)
        if (WoodMerger.Instance != null) rope = WoodMerger.Instance.Rope.Build(transform, ropeFaces, woodMaterial);
    }

    public override GridFootprint GetFootprint()
    {
        if (footprints == null)
        {
            var cells = new List<Vector3Int>(WoodLayout.BaseCells(baseLength));
            foreach ((WoodSlot slot, int length) in FilledSlots())
                cells.AddRange(WoodLayout.PieceCells(baseLength, slot, length));

            footprints = GridMaskRotator.AllRotations(new GridFootprint(cells.ToArray()));
        }
        return footprints[(int)Rotation];
    }

    private IEnumerable<(WoodSlot slot, int length)> FilledSlots()
    {
        for (int s = 0; s < WoodLayout.SideCount; s++)
            for (int layer = 1; layer <= baseLength; layer++)
            {
                int length = PieceAt((WoodSide)s, layer);
                if (length > 0) yield return (WoodSlot.At((WoodSide)s, layer), length);
            }
    }

    private int[][] ToArrays()
    {
        int[][] result = WoodLayout.EmptySides(baseLength);
        for (int s = 0; s < WoodLayout.SideCount; s++)
            for (int layer = 1; layer <= baseLength; layer++)
                result[s][layer - 1] = PieceAt((WoodSide)s, layer);
        return result;
    }

    // Parçanın modelini child olarak ekler, root'a da parçayı kaplayan bir collider koyar
    // (InteractableController collider'ın kendi objesinde IInteractable arıyor). Eklenen modeli döndürür.
    private GameObject AddPiece(Wood woodPrefab, Vector3Int start, Quaternion rotation, int length)
    {
        if (woodPrefab == null)
        {
            Debug.LogWarning($"WoodCatalog'da {length} uzunluğunda odun yok", this);
            return null;
        }

        var holder = new GameObject($"Piece {length}BR").transform;
        holder.SetParent(transform, false);
        holder.localPosition = start;
        holder.localRotation = rotation;
        GameObject visual = Instantiate(woodPrefab.Visual.gameObject, holder, false);

        // Collider görselin gerçek sınırlarında (parmaklar ve raycast'ler yüzeye otursun). Görsel yoksa hücre boyunda kutu.
        var box = gameObject.AddComponent<BoxCollider>();
        if (TryGetLocalBounds(visual, out Bounds bounds))
        {
            box.center = bounds.center;
            box.size = bounds.size;
        }
        else
        {
            Vector3 axis = rotation * Vector3.up;
            Vector3 size = rotation * new Vector3(ColliderThickness, length, ColliderThickness);
            box.center = (Vector3)start + axis * (length - 1) / 2f;
            box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        }
        return visual;
    }

    // Görseldeki bütün mesh'lerin köşeleri bu objenin yerel uzayında. Parçalar 90° katlarında döndüğü için
    // eksene hizalı kutu görsele tam oturur.
    private bool TryGetLocalBounds(GameObject visual, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            Bounds meshBounds = filter.sharedMesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = meshBounds.center + Vector3.Scale(meshBounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                Vector3 point = transform.InverseTransformPoint(filter.transform.TransformPoint(local));
                if (any) bounds.Encapsulate(point);
                else bounds = new Bounds(point, Vector3.zero);
                any = true;
            }
        }
        return any;
    }

    // --- IInteractable ---
    public void Interact(out bool finished)
    {
        if (!drag.TryUseOnTarget(out finished))
            finished = true;
    }

    public void InteractContractBeginnig() => drag.Begin();
    public void InteractContract(out bool success) { success = true; drag.Tick(); }
    public void ContractCancel() => drag.Cancel();

    // --- ISaveState ---
    public string CaptureState() => JsonUtility.ToJson(new State { baseLength = baseLength, segments = segments, sides = sides });

    public void RestoreState(string state)
    {
        State loaded = JsonUtility.FromJson<State>(state);
        int[][] loadedSides = WoodLayout.EmptySides(loaded.baseLength);
        var loadedSegments = new int[WoodLayout.SideCount][][];

        if (loaded.sides != null && loaded.sides.Length == WoodLayout.SideCount)
        {
            for (int s = 0; s < WoodLayout.SideCount; s++)
            {
                if (loaded.sides[s]?.layers != null)
                    Array.Copy(loaded.sides[s].layers, loadedSides[s], Mathf.Min(loaded.sides[s].layers.Length, loaded.baseLength));
                if (loaded.sides[s]?.segments != null)
                {
                    loadedSegments[s] = new int[loaded.baseLength][];
                    for (int layer = 1; layer <= Mathf.Min(loaded.sides[s].segments.Length, loaded.baseLength); layer++)
                        loadedSegments[s][layer - 1] = ParseSegments(loaded.sides[s].segments[layer - 1]);
                }
            }
        }
        else if (loaded.layers != null)
        {
            // Eski kayıt: sadece sağ yan vardı; [0] tepe (hep boş), [k] sağ yandaki k. katman
            for (int layer = 1; layer < loaded.layers.Count && layer <= loaded.baseLength; layer++)
                loadedSides[(int)WoodSide.Right][layer - 1] = loaded.layers[layer];
        }

        Build(loaded.baseLength, loadedSides, WoodMerger.Instance.Catalog, loaded.segments, loadedSegments);
    }

    // --- IToolTarget: boş bir katmana tek parça odun getirildiğinde ---
    public Vector3 GetToolTargetPosition(IInteractable interacted, out bool accept) => mergeTarget.GetToolTargetPosition(interacted, out accept);
    public Quaternion GetToolTargetRotation() => mergeTarget.GetToolTargetRotation();
    public void OnToolTargetExit() => mergeTarget.OnToolTargetExit();
    public bool OnToolUsed(IInteractable tool) => mergeTarget.OnToolUsed(tool);
    public bool IsStillTargeted(IInteractable interacted, Ray mouseRay) => mergeTarget.IsStillTargeted(interacted, mouseRay);
}
