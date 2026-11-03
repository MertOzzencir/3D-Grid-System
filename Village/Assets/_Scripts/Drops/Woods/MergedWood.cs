using System;
using System.Collections.Generic;
using UnityEngine;

// Birbirine değen 1×1 odunlardan yapı (kullanıcı 2026-11-03: küp yapabilecek sistem). Her hücrede yatık ya da dik bir 1×1
// durur (Cell: yeri + uzandığı yön). Referans ana odun: pivottan (0, 0, 0) yukarı kesintisiz dik sütun; kamera odağı,
// birleştirmenin başlangıcı ve ip ona göre. Boş komşu hücrelere odun eklenir (WoodMergeTarget); kendisi taşınıp döndürülür.
// İp sadece ana oduna doğrudan değen parçaların değme yüzünde (küpün içinde ip yok, kullanıcı).
// Baltayla sökme şimdilik kapalı (kullanıcı: yeni sisteme göre sonra yazılacak; WoodDetachTarget eski kol yapısına göre).
public class MergedWood : GridPlaceable, IInteractable, IStickyToolTarget, IBlueprintPiece, IWoodStack, ISaveState
{
    // Bir hücredeki 1×1: yeri (pivota göre, döndürülmemiş) ve uzandığı yön (yukarı = dik; yapıştığı yüzün yönü)
    public struct Cell
    {
        public Vector3Int Position;
        public Vector3Int Axis;

        public Cell(Vector3Int position, Vector3Int axis)
        {
            Position = position;
            Axis = axis;
        }
    }

    // Eski kayıt (ana odun + dört yanın kolları): yüklenirken hücrelere çevrilir
    [Serializable]
    private class Side
    {
        public int[] layers; // [katman - 1] = o katmandaki odunun uzunluğu, 0 = boş
        public string[] segments;
    }

    // Kayıttaki durumu: prefab'ı boş, görseli bu değerlerden yeniden kurulur
    [Serializable]
    private class State
    {
        public int[] cells; // hücre başına 6 sayı: x, y, z, yön x, yön y, yön z
        public int baseLength;
        public int[] segments;
        public Side[] sides;
        public List<int> layers; // en eski format (sadece sağ yan)
    }

    private const float ColliderThickness = 0.75f; // sadece görselde mesh bulunamazsa

    [SerializeField] private float followSpeed = 10f;

    private readonly List<Cell> cells = new List<Cell>();
    private readonly Dictionary<Vector3Int, Renderer[]> cellRenderers = new Dictionary<Vector3Int, Renderer[]>();
    private int baseLength;

    private GridDragMotor drag;
    private WoodMergeTarget mergeTarget;
    private WoodDetachTarget detachTarget;
    private GridFootprint[] footprints; // rotasyon başına bir kez hesaplanır
    private string signature;           // şekil değişmedikçe aynı
    private int signatureSteps;
    private GameObject rope;
    private List<WoodRope.Face> ropeFaces = new List<WoodRope.Face>(); // ipin değme yüzleri (yerel)

    public List<WoodRope.Face> RopeFaces => ropeFaces;
    public IReadOnlyList<Cell> Cells => cells;

    // Ana odun: pivottan yukarı kesintisiz dik sütunun boyu
    public int BaseLength => baseLength;

    // Sadece dik sütun (iple bağlı 1×1'lerden yığın): eski 2BR / 3BR odun gibi taşınıp başka bir oduna tek parça olarak
    // eklenebilir (WoodMergeTarget.TryGetPiece)
    public bool IsStack
    {
        get
        {
            foreach (Cell cell in cells)
                if (cell.Position.x != 0 || cell.Position.z != 0) return false;
            return true;
        }
    }

    public int[] BaseSegments => WoodLayout.DefaultSegments(baseLength);

    public IEnumerable<Vector3Int> LocalCells
    {
        get
        {
            foreach (Cell cell in cells) yield return cell.Position;
        }
    }

    // O hücredeki parçanın renderer'ları (kamerayı kapatanları saydamlaştırmak için); yoksa boş
    public Renderer[] RenderersAt(Vector3Int local) => cellRenderers.TryGetValue(local, out Renderer[] found) ? found : Array.Empty<Renderer>();

    // Eski kol yapısının sorguları (sökme kodu derlensin diye; sökme şimdilik kapalı): o yandaki kattan dışa kesintisiz uzunluk
    public int PieceAt(WoodSide side, int layer)
    {
        if (layer < 1 || layer > baseLength) return 0;
        Vector3Int direction = WoodLayout.SideDirection(side);
        Vector3Int start = direction + Vector3Int.up * WoodLayout.SideRow(baseLength, layer);
        int length = 0;
        while (Contains(start + direction * length)) length++;
        return length;
    }

    public int[] SideSegments(WoodSide side, int layer) => null;
    public Renderer[] PieceRenderers(WoodSide side) => Array.Empty<Renderer>();

    private bool Contains(Vector3Int position)
    {
        foreach (Cell cell in cells)
            if (cell.Position == position) return true;
        return false;
    }

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

    // Döndürmeden bağımsız: aynı şeklin her yöndeki hali aynı imzayı verir, ayna görüntüsü farklı verir. Sadece dik sütunsa
    // aynı boydaki dik odunun yerine geçer ("Wood:2" slot'u)
    public string BlueprintSignature
    {
        get
        {
            if (signature == null)
                signature = IsStack ? $"Wood:{baseLength}" : "MergedWood:" + CanonicalSignature(cells, out signatureSteps);
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

    // 4 dönüşün (pivot etrafında, F→R yönünde) imzasından sıralamada en küçüğü; parçanın yönü eksen olarak (x / y / z)
    private static string CanonicalSignature(List<Cell> shape, out int steps)
    {
        string best = null;
        steps = 0;
        var parts = new List<string>();
        for (int k = 0; k < WoodLayout.SideCount; k++)
        {
            parts.Clear();
            foreach (Cell cell in shape)
            {
                Vector3Int p = GridMaskRotator.RotateOffset(cell.Position, (GridMaskRotator.Rotation)k);
                Vector3Int a = GridMaskRotator.RotateOffset(cell.Axis, (GridMaskRotator.Rotation)k);
                char axis = a.y != 0 ? 'y' : a.x != 0 ? 'x' : 'z';
                parts.Add($"{p.x},{p.y},{p.z}{axis}");
            }
            parts.Sort(string.CompareOrdinal);
            string candidate = string.Join(";", parts);
            if (best == null || string.CompareOrdinal(candidate, best) < 0)
            {
                best = candidate;
                steps = k;
            }
        }
        return best;
    }

    private void Awake()
    {
        drag = new GridDragMotor(this, followSpeed);
        mergeTarget = new WoodMergeTarget(this);
        detachTarget = new WoodDetachTarget(this);
    }

    // Eski kol yapısından (ana odun + dört yanın katmanlarındaki kollar) kurar: hücrelere çevirip BuildCells.
    // sides: WoodLayout.EmptySides biçiminde, [yan][katman - 1] = uzunluk (0 = boş). segments / sideSegments eski kayıt
    // uyumu için duruyor, kullanılmaz (her şey 1×1)
    public void Build(int length, int[][] sides, WoodCatalogSO catalog, int[] segments = null, int[][][] sideSegments = null)
    {
        var shape = new List<Cell>();
        for (int y = 0; y < length; y++)
            shape.Add(new Cell(new Vector3Int(0, y, 0), Vector3Int.up));
        for (int s = 0; s < WoodLayout.SideCount && s < sides.Length; s++)
            for (int layer = 1; layer <= length && layer <= sides[s].Length; layer++)
            {
                Vector3Int direction = WoodLayout.SideDirection((WoodSide)s);
                Vector3Int start = direction + Vector3Int.up * WoodLayout.SideRow(length, layer);
                for (int i = 0; i < sides[s][layer - 1]; i++)
                    shape.Add(new Cell(start + direction * i, direction));
            }
        BuildCells(shape, catalog);
    }

    public void BuildCells(IEnumerable<Cell> shape, WoodCatalogSO catalog)
    {
        cells.Clear();
        var seen = new HashSet<Vector3Int>();
        foreach (Cell cell in shape)
            if (seen.Add(cell.Position)) cells.Add(cell);

        baseLength = 0;
        while (seen.Contains(new Vector3Int(0, baseLength, 0))) baseLength++;

        footprints = null;
        signature = null;
        cellRenderers.Clear();

        // Her hücreye bir 1×1, uzandığı yöne yatırılmış (dik olanlar dik)
        ropeFaces = new List<WoodRope.Face>();
        Material woodMaterial = null;
        Wood prefab = catalog.GetWood(1);
        foreach (Cell cell in cells)
        {
            GameObject visual = AddPiece(prefab, cell.Position, Quaternion.FromToRotation(Vector3.up, cell.Axis));
            if (visual == null) continue;
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
            cellRenderers[cell.Position] = renderers;
            if (woodMaterial == null && renderers.Length > 0) woodMaterial = renderers[0].sharedMaterial;
        }

        // İp: ana odunun yanına doğrudan değen parçaların değme yüzü (küpün içinde, parçaların arasında ip yok)
        foreach (Cell cell in cells)
        {
            Vector3Int p = cell.Position;
            if (p.x == 0 && p.z == 0 && p.y < baseLength) continue; // ana odunun kendisi
            for (int s = 0; s < WoodLayout.SideCount; s++)
            {
                Vector3Int direction = WoodLayout.SideDirection((WoodSide)s);
                Vector3Int inner = p - direction;
                if (inner.x != 0 || inner.z != 0 || inner.y < 0 || inner.y >= baseLength) continue;
                ropeFaces.Add(new WoodRope.Face { center = (Vector3)p - (Vector3)direction * 0.5f, normal = direction, up = Vector3.up });
            }
        }

        if (WoodMerger.Instance != null) rope = WoodMerger.Instance.Rope.Build(transform, ropeFaces, woodMaterial);
    }

    public override GridFootprint GetFootprint()
    {
        if (footprints == null)
        {
            var positions = new Vector3Int[cells.Count];
            for (int i = 0; i < cells.Count; i++) positions[i] = cells[i].Position;
            footprints = GridMaskRotator.AllRotations(new GridFootprint(positions));
        }
        return footprints[(int)Rotation];
    }

    // Parçanın modelini child olarak ekler, root'a da parçayı kaplayan bir collider koyar
    // (InteractableController collider'ın kendi objesinde IInteractable arıyor). Eklenen modeli döndürür.
    private GameObject AddPiece(Wood woodPrefab, Vector3Int start, Quaternion rotation)
    {
        if (woodPrefab == null)
        {
            Debug.LogWarning("WoodCatalog'da 1×1 odun yok", this);
            return null;
        }

        var holder = new GameObject("Piece 1BR").transform;
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
            Vector3 size = rotation * new Vector3(ColliderThickness, 1f, ColliderThickness);
            box.center = start;
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
    public string CaptureState()
    {
        var data = new int[cells.Count * 6];
        for (int i = 0; i < cells.Count; i++)
        {
            Cell cell = cells[i];
            data[i * 6] = cell.Position.x;
            data[i * 6 + 1] = cell.Position.y;
            data[i * 6 + 2] = cell.Position.z;
            data[i * 6 + 3] = cell.Axis.x;
            data[i * 6 + 4] = cell.Axis.y;
            data[i * 6 + 5] = cell.Axis.z;
        }
        return JsonUtility.ToJson(new State { cells = data, baseLength = baseLength });
    }

    public void RestoreState(string state)
    {
        State loaded = JsonUtility.FromJson<State>(state);
        if (loaded.cells != null && loaded.cells.Length >= 6)
        {
            var shape = new List<Cell>();
            for (int i = 0; i + 5 < loaded.cells.Length; i += 6)
                shape.Add(new Cell(new Vector3Int(loaded.cells[i], loaded.cells[i + 1], loaded.cells[i + 2]),
                                   new Vector3Int(loaded.cells[i + 3], loaded.cells[i + 4], loaded.cells[i + 5])));
            BuildCells(shape, WoodMerger.Instance.Catalog);
            return;
        }

        // Eski kayıt: ana odun + kollar
        int[][] loadedSides = WoodLayout.EmptySides(loaded.baseLength);
        if (loaded.sides != null && loaded.sides.Length == WoodLayout.SideCount)
        {
            for (int s = 0; s < WoodLayout.SideCount; s++)
                if (loaded.sides[s]?.layers != null)
                    Array.Copy(loaded.sides[s].layers, loadedSides[s], Mathf.Min(loaded.sides[s].layers.Length, loaded.baseLength));
        }
        else if (loaded.layers != null)
        {
            // En eski kayıt: sadece sağ yan vardı; [0] tepe (hep boş), [k] sağ yandaki k. katman
            for (int layer = 1; layer < loaded.layers.Count && layer <= loaded.baseLength; layer++)
                loadedSides[(int)WoodSide.Right][layer - 1] = loaded.layers[layer];
        }
        Build(loaded.baseLength, loadedSides, WoodMerger.Instance.Catalog);
    }

    // --- IToolTarget: boş bir yere odun getirildiğinde (birleştirme). Balta (sökme) şimdilik kapalı. ---
    public Vector3 GetToolTargetPosition(IInteractable interacted, out bool accept)
    {
        if (interacted is Axe)
        {
            accept = false;
            return default;
        }
        return mergeTarget.GetToolTargetPosition(interacted, out accept);
    }
    public Quaternion GetToolTargetRotation() => mergeTarget.GetToolTargetRotation();
    public void OnToolTargetExit()
    {
        mergeTarget.OnToolTargetExit();
        detachTarget.OnToolTargetExit();
    }
    public bool OnToolUsed(IInteractable tool) => !(tool is Axe) && mergeTarget.OnToolUsed(tool);
    public bool IsStillTargeted(IInteractable interacted, Ray mouseRay)
        => !(interacted is Axe) && mergeTarget.IsStillTargeted(interacted, mouseRay);
}
