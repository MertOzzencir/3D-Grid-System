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
    }

    // Kayıttaki durumu: prefab'ı boş, görseli bu değerlerden yeniden kurulur
    [Serializable]
    private class State
    {
        public int baseLength;
        public Side[] sides;
        public List<int> layers; // eski format (sadece sağ yan): [0] = tepe, [k] = sağ yandaki k. katman
    }

    private const float ColliderThickness = 0.75f; // sadece görselde mesh bulunamazsa

    [SerializeField] private float followSpeed = 10f;

    [Header("Kil görünüm (deneme)")]
    [Tooltip("Açıksa parçaların ayrı modelleri gizlenir, hepsi kaynaşmış tek bir kil şekli olarak çizilir (ClayMesher). " +
             "Şekil kurulurken bir kez üretilir (birleştirme anında, kayıttan yüklenince).")]
    [SerializeField] private bool clayLook = true;
    [Tooltip("Şeklin ızgara aralığı (birim): küçük = daha pürüzsüz ama üretimi daha yavaş")]
    [SerializeField] private float clayVoxelSize = 0.07f;
    [Tooltip("Birleşim yerlerindeki kaynaşmanın genişliği (birim)")]
    [SerializeField] private float clayBlend = 0.12f;
    [Tooltip("Kenar yuvarlaklığı (birim)")]
    [SerializeField] private float clayCornerRadius = 0.1f;
    [Tooltip("Parça sınırlarını içe çekme (birim): şekil modelden şişman görünürse")]
    [SerializeField] private float clayInset = 0f;
    [Tooltip("Kil şeklinin malzemesi (URP Lit gibi düz bir malzeme; odunun WoodWobbleLit'i olmaz, vertex kaydırıyor)")]
    [SerializeField] private Material clayMaterial;
    [Tooltip("Clay Material boşken (sadece editörde) kullanılan düz malzemenin rengi")]
    [SerializeField] private Color clayColor = new Color(0.78f, 0.58f, 0.38f);

    [SerializeField] private int baseLength;
    [SerializeField] private Side[] sides = new Side[0];

    private GridDragMotor drag;
    private WoodMergeTarget mergeTarget;
    private GridFootprint[] footprints; // rotasyon başına bir kez hesaplanır
    private string signature;           // şekil değişmedikçe aynı
    private int signatureSteps;
    private readonly List<Renderer>[] sideRenderers = NewSideRenderers();
    private readonly List<Bounds> pieceBounds = new List<Bounds>(); // parçaların sınırları (yerel), kil şekli için
    private GameObject clay;

    public int BaseLength => baseLength;

    public Renderer[] PieceRenderers(WoodSide side) => sideRenderers[(int)side].ToArray();

    private static List<Renderer>[] NewSideRenderers()
    {
        var result = new List<Renderer>[WoodLayout.SideCount];
        for (int s = 0; s < result.Length; s++)
            result[s] = new List<Renderer>();
        return result;
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
    public void Build(int baseLength, int[][] sides, WoodCatalogSO catalog)
    {
        this.baseLength = baseLength;
        this.sides = new Side[WoodLayout.SideCount];
        for (int s = 0; s < WoodLayout.SideCount; s++)
            this.sides[s] = new Side { layers = (int[])sides[s].Clone() };
        footprints = null;
        signature = null;

        foreach (List<Renderer> list in sideRenderers)
            list.Clear();
        pieceBounds.Clear();

        AddPiece(catalog.GetWood(baseLength), Vector3Int.zero, Quaternion.identity, baseLength);
        foreach ((WoodSlot slot, int length) in FilledSlots())
        {
            GameObject visual = AddPiece(catalog.GetWood(length), WoodLayout.PieceStart(baseLength, slot), WoodLayout.PieceRotation(slot), length);
            if (visual != null)
                sideRenderers[(int)slot.Side].AddRange(visual.GetComponentsInChildren<Renderer>());
        }

        if (clayLook) BuildClay();
    }

    // Parçaların ayrı modelleri yerine kaynaşmış tek şekil (bir kez; yapı değişince MergedWood yeniden yaratılıyor)
    private void BuildClay()
    {
        if (clay != null) Destroy(clay);
        Mesh mesh = ClayMesher.Build(pieceBounds, clayVoxelSize, clayCornerRadius, clayBlend, clayInset);
        if (mesh == null) return;

        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        clay = new GameObject("Clay");
        clay.transform.SetParent(transform, false);
        clay.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = clay.AddComponent<MeshRenderer>();
        // Renk malzemede (MaterialPropertyBlock'la değil: birleşme sallanması kendi bloğunu yazıp siliyor)
        Material material = clayMaterial;
#if UNITY_EDITOR
        if (material == null)
        {
            if (fallbackClay == null)
            {
                fallbackClay = new Material(Shader.Find("Universal Render Pipeline/Lit")); // sadece editörde
                fallbackClay.SetColor("_BaseColor", clayColor);
            }
            material = fallbackClay;
        }
#endif
        if (material == null) Debug.LogWarning("MergedWood: Clay Material atanmamış (build'de gerekli).", this);
        renderer.sharedMaterial = material;
    }

    private static Material fallbackClay;

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
            pieceBounds.Add(bounds);
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
    public string CaptureState() => JsonUtility.ToJson(new State { baseLength = baseLength, sides = sides });

    public void RestoreState(string state)
    {
        State loaded = JsonUtility.FromJson<State>(state);
        int[][] loadedSides = WoodLayout.EmptySides(loaded.baseLength);

        if (loaded.sides != null && loaded.sides.Length == WoodLayout.SideCount)
        {
            for (int s = 0; s < WoodLayout.SideCount; s++)
                if (loaded.sides[s]?.layers != null)
                    Array.Copy(loaded.sides[s].layers, loadedSides[s], Mathf.Min(loaded.sides[s].layers.Length, loaded.baseLength));
        }
        else if (loaded.layers != null)
        {
            // Eski kayıt: sadece sağ yan vardı; [0] tepe (hep boş), [k] sağ yandaki k. katman
            for (int layer = 1; layer < loaded.layers.Count && layer <= loaded.baseLength; layer++)
                loadedSides[(int)WoodSide.Right][layer - 1] = loaded.layers[layer];
        }

        Build(loaded.baseLength, loadedSides, WoodMerger.Instance.Catalog);
    }

    // --- IToolTarget: boş bir katmana tek parça odun getirildiğinde ---
    public Vector3 GetToolTargetPosition(IInteractable interacted, out bool accept) => mergeTarget.GetToolTargetPosition(interacted, out accept);
    public Quaternion GetToolTargetRotation() => mergeTarget.GetToolTargetRotation();
    public void OnToolTargetExit() => mergeTarget.OnToolTargetExit();
    public bool OnToolUsed(IInteractable tool) => mergeTarget.OnToolUsed(tool);
    public bool IsStillTargeted(IInteractable interacted, Ray mouseRay) => mergeTarget.IsStillTargeted(interacted, mouseRay);
}
