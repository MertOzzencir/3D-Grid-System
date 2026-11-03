using System;
using System.Collections.Generic;
using UnityEngine;

// Birbirine değen 1×1 odunlardan yapı (kullanıcı 2026-11-03: küp yapabilecek sistem). Her hücrede yatık ya da dik bir 1×1
// durur (Cell: yeri + uzandığı yön). Referans ana odun: pivottan (0, 0, 0) yukarı kesintisiz dik sütun; kamera odağı,
// birleştirmenin başlangıcı ve ip ona göre. Boş komşu hücrelere odun eklenir (WoodMergeTarget); kendisi taşınıp döndürülür.
// İp: ana oduna doğrudan değen parçaların değme yüzünde + sal bağı bantları (kullanıcı: her ek yerine kalas / çivi
// koymak kaotikti): uç uca dizili parçaların (ikisi de ek yerinin yönünde uzanan) ek yerlerinde, ana odundan her
// WoodRope.BandSpacing ek yerinde bir, o düzlemdeki bütün ek yerlerini (yan yana parçaların hepsini) saran tek bant.
// Yan yana parçaların arası (ekleri parçaların boyunca uzanan) bağlanmaz. Bant, aynı düzlemdeki ana oduna bağlanma ipiyle
// birleşip tek halka olur. Kol ile ondan çıkan derinlik odunu arasına ayrıca 45° çivi (WoodNail).
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
    private readonly HashSet<Vector3Int> occupied = new HashSet<Vector3Int>();
    private readonly Dictionary<Vector3Int, Renderer[]> pieceRenderers = new Dictionary<Vector3Int, Renderer[]>(); // sadece 1×1
    private readonly Dictionary<Vector3Int, Renderer[]> cellRenderers = new Dictionary<Vector3Int, Renderer[]>();  // + ip, çivi
    private Material woodMaterial;
    private int baseLength;

    private GridDragMotor drag;
    private WoodMergeTarget mergeTarget;
    private WoodDetachTarget detachTarget;
    private GridFootprint[] footprints; // rotasyon başına bir kez hesaplanır
    private string signature;           // şekil değişmedikçe aynı
    private int signatureSteps;
    // İp parçalara bölünür (kullanıcı: tek mesh her birleştirmede baştan kuruluyordu, spike): kenar paylaşan değme yüzleri
    // bir parça (ana odunun bağlantısı + ona değen bantlar, her ayrı bant). Parça yüzlerine göre anahtarlanır: değişmeyen
    // parça yerinde kalır, sadece yeni / değişen parça kurulur ve sarılarak gelir.
    private class RopePiece
    {
        public List<WoodRope.Face> Faces;
        public GameObject Object;
    }

    private Dictionary<string, RopePiece> ropePieces = new Dictionary<string, RopePiece>();
    private readonly List<RopePiece> freshRope = new List<RopePiece>();                 // son kurulumda yeni olanlar
    private readonly List<WoodRope.Face> replacedRopeFaces = new List<WoodRope.Face>(); // onların yerine silinenler
    private List<WoodNail.Joint> nailJoints = new List<WoodNail.Joint>(); // çivili ek yerleri (yerel)
    private NailGrow nailRoot;
    private Dictionary<(Vector3Int, Vector3Int, Vector3Int), Transform[]> nailObjects =
        new Dictionary<(Vector3Int, Vector3Int, Vector3Int), Transform[]>();
    private readonly List<Transform> freshNails = new List<Transform>(); // son kurulumda eklenenler
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

    // O hücredeki parçanın renderer'ları (kamerayı kapatanları saydamlaştırmak için); yoksa boş. Hücreye değen çiviler
    // ve ip parçaları da dahil.
    public Renderer[] RenderersAt(Vector3Int local) => cellRenderers.TryGetValue(local, out Renderer[] found) ? found : Array.Empty<Renderer>();

    // RebuildJoints sonunda: her hücrenin 1×1'i + değen çivilerin ve ipin renderer'ları
    private void AttachExtraRenderers()
    {
        cellRenderers.Clear();
        foreach (KeyValuePair<Vector3Int, Renderer[]> pair in pieceRenderers) cellRenderers[pair.Key] = pair.Value;
        var extra = new Dictionary<Vector3Int, List<Renderer>>();
        void Add(Vector3Int cell, IEnumerable<Renderer> renderers)
        {
            if (!cellRenderers.ContainsKey(cell)) return;
            if (!extra.TryGetValue(cell, out List<Renderer> list)) extra[cell] = list = new List<Renderer>();
            list.AddRange(renderers);
        }

        foreach (WoodNail.Joint joint in nailJoints)
        {
            if (!nailObjects.TryGetValue(NailKey(joint), out Transform[] made)) continue;
            var renderers = new List<Renderer>();
            foreach (Transform nail in made)
                if (nail != null) renderers.AddRange(nail.GetComponentsInChildren<Renderer>());
            Vector3 half = (Vector3)joint.Along * 0.5f;
            Add(Vector3Int.RoundToInt(joint.Center - half), renderers);
            Add(Vector3Int.RoundToInt(joint.Center + half), renderers);
        }

        // İp: her parça sadece kendi değdiği hücrelerle saydamlaşır
        foreach (RopePiece piece in ropePieces.Values)
        {
            if (piece.Object == null) continue;
            Renderer[] renderers = piece.Object.GetComponentsInChildren<Renderer>();
            var touched = new HashSet<Vector3Int>();
            foreach (WoodRope.Face face in piece.Faces)
            {
                touched.Add(Vector3Int.RoundToInt(face.center - face.normal * 0.5f));
                touched.Add(Vector3Int.RoundToInt(face.center + face.normal * 0.5f));
            }
            foreach (Vector3Int cell in touched) Add(cell, renderers);
        }

        foreach (KeyValuePair<Vector3Int, List<Renderer>> pair in extra)
        {
            pair.Value.InsertRange(0, cellRenderers[pair.Key]);
            cellRenderers[pair.Key] = pair.Value.ToArray();
        }
    }

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

    private bool Contains(Vector3Int position) => occupied.Contains(position);

    // Son kurulumda (AddCells) yeni gelen ip parçaları sarılarak gelsin (birleştirme anında; kayıttan yüklenince ip hazır).
    // Değişen parçada sadece yerine geçtiği eski parçalarda olmayan kenarlar oynar.
    public void PlayRopeAnimation()
    {
        if (WoodMerger.Instance == null) return;
        foreach (RopePiece piece in freshRope)
        {
            if (piece.Object == null || !piece.Object.TryGetComponent(out RopeGrow grow)) continue;
            var added = new List<(Vector3, Vector3)>();
            var existing = new List<(Vector3, Vector3)>();
            WoodMerger.Instance.Rope.Changes(piece.Faces, replacedRopeFaces, added, existing);
            grow.Play(added, existing);
        }
    }

    // Son kurulumda eklenen çiviler çakılarak gelsin
    public void PlayNailAnimation()
    {
        if (nailRoot == null || WoodMerger.Instance == null || freshNails.Count == 0) return;
        nailRoot.Play(freshNails, WoodMerger.Instance.Nail.Animation);
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

    // Sıfırdan kurar (yeni obje, kayıttan yükleme). Birleştirmede AddCells kullanılır.
    public void BuildCells(IEnumerable<Cell> shape, WoodCatalogSO catalog)
    {
        cells.Clear();
        occupied.Clear();
        pieceRenderers.Clear();
        AddCells(shape, catalog);
    }

    // Artımlı ekleme (kullanıcı: her birleştirmede bütün yapıyı baştan kurmak ms spike yapıyordu): sadece yeni hücrelere
    // 1×1 + collider eklenir, mevcut parçalar yerinde kalır. İp ve çiviler yeni şekle göre yeniden kurulur. Obje grid'e
    // yerleşikse footprint'i çağıran günceller (WoodMerger.Merge). Eklenen hücre sayısını döndürür.
    public int AddCells(IEnumerable<Cell> shape, WoodCatalogSO catalog)
    {
        var fresh = new List<Cell>();
        foreach (Cell cell in shape)
            if (occupied.Add(cell.Position))
            {
                cells.Add(cell);
                fresh.Add(cell);
            }

        baseLength = 0;
        while (occupied.Contains(new Vector3Int(0, baseLength, 0))) baseLength++;
        footprints = null;
        signature = null;

        // Her yeni hücreye bir 1×1, uzandığı yöne yatırılmış (dik olanlar dik)
        Wood prefab = catalog.GetWood(1);
        foreach (Cell cell in fresh)
        {
            GameObject visual = AddPiece(prefab, cell.Position, Quaternion.FromToRotation(Vector3.up, cell.Axis));
            if (visual == null) continue;
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
            pieceRenderers[cell.Position] = renderers;
            if (woodMaterial == null && renderers.Length > 0) woodMaterial = renderers[0].sharedMaterial;
        }

        RebuildJoints();
        UpdateHiddenPieces();
        return fresh.Count;
    }

    // Görünmeyen parçalar çizilmez (kullanıcı: sahnede çok üçgen): dört yanı ve üstü dolu olan 1×1 hiçbir açıdan
    // görünmüyor (kamera hep yukarıda, alt yüz kameraya bakmaz). Renderer'ı kapanır (WoodInstancer kapalıları atlar);
    // komşusu gidince (sökme) yeniden açılır.
    private static readonly Vector3Int[] CoverNeighbours =
        { Vector3Int.up, Vector3Int.right, Vector3Int.left, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };

    private void UpdateHiddenPieces()
    {
        foreach (KeyValuePair<Vector3Int, Renderer[]> pair in pieceRenderers)
        {
            bool hidden = true;
            foreach (Vector3Int offset in CoverNeighbours)
                if (!occupied.Contains(pair.Key + offset))
                {
                    hidden = false;
                    break;
                }
            foreach (Renderer renderer in pair.Value)
                if (renderer != null) renderer.enabled = !hidden;
        }
    }

    // İp (ana odunun yanına doğrudan değen parçaların değme yüzü + sal bağı bantları) ve çiviler: yeni şekle göre hesaplanır,
    // değişmeyen ip parçası ve çivi yerinde kalır, sadece yeniler kurulur, artık olmayanlar silinir
    private void RebuildJoints()
    {
        var faces = new List<WoodRope.Face>();
        foreach (Cell cell in cells)
        {
            Vector3Int p = cell.Position;
            if (p.x == 0 && p.z == 0 && p.y < baseLength) continue; // ana odunun kendisi
            for (int s = 0; s < WoodLayout.SideCount; s++)
            {
                Vector3Int direction = WoodLayout.SideDirection((WoodSide)s);
                Vector3Int inner = p - direction;
                if (inner.x != 0 || inner.z != 0 || inner.y < 0 || inner.y >= baseLength) continue;
                faces.Add(new WoodRope.Face { center = (Vector3)p - (Vector3)direction * 0.5f, normal = direction, up = Vector3.up });
            }
        }
        AddBandFaces(occupied, faces);
        RebuildRope(faces);

        CollectNailJoints(occupied);
        RebuildNails();
        AttachExtraRenderers();
    }

    private void RebuildRope(List<WoodRope.Face> faces)
    {
        freshRope.Clear();
        replacedRopeFaces.Clear();
        var next = new Dictionary<string, RopePiece>();
        foreach (List<WoodRope.Face> group in GroupFaces(faces))
        {
            string key = PieceKey(group);
            if (ropePieces.TryGetValue(key, out RopePiece kept))
            {
                next[key] = kept;
                continue;
            }
            var piece = new RopePiece
            {
                Faces = group,
                Object = WoodMerger.Instance != null ? WoodMerger.Instance.Rope.Build(transform, group, woodMaterial) : null
            };
            next[key] = piece;
            freshRope.Add(piece);
        }
        foreach (KeyValuePair<string, RopePiece> old in ropePieces)
        {
            if (next.ContainsKey(old.Key)) continue;
            replacedRopeFaces.AddRange(old.Value.Faces);
            DestroyRope(old.Value.Object);
        }
        ropePieces = next;
    }

    private static void DestroyRope(GameObject piece)
    {
        if (piece == null) return;
        if (piece.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null) Destroy(filter.sharedMesh);
        Destroy(piece);
    }

    // Kenar paylaşan yüzler aynı gruba (birleşim-bul); her grup ayrı ip parçası. Paylaşılan kenar aynı parçada kalsın diye
    // (iki parçada iki kez çizilmesin)
    private static List<List<WoodRope.Face>> GroupFaces(List<WoodRope.Face> faces)
    {
        var parent = new int[faces.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int i)
        {
            while (parent[i] != i) i = parent[i] = parent[parent[i]];
            return i;
        }

        var owner = new Dictionary<(Vector3Int, Vector3Int), int>();
        for (int i = 0; i < faces.Count; i++)
        {
            WoodRope.Face face = faces[i];
            Vector3 u = face.up * 0.5f, v = Vector3.Cross(face.normal, face.up).normalized * 0.5f;
            Vector3Int[] corners =
            {
                Vector3Int.RoundToInt((face.center + u + v) * 2f), Vector3Int.RoundToInt((face.center + u - v) * 2f),
                Vector3Int.RoundToInt((face.center - u - v) * 2f), Vector3Int.RoundToInt((face.center - u + v) * 2f)
            };
            for (int c = 0; c < 4; c++)
            {
                Vector3Int a = corners[c], b = corners[(c + 1) % 4];
                var edge = Less(a, b) ? (a, b) : (b, a);
                if (owner.TryGetValue(edge, out int other)) parent[Find(i)] = Find(other);
                else owner[edge] = i;
            }
        }

        var groups = new Dictionary<int, List<WoodRope.Face>>();
        for (int i = 0; i < faces.Count; i++)
        {
            int root = Find(i);
            if (!groups.TryGetValue(root, out List<WoodRope.Face> group)) groups[root] = group = new List<WoodRope.Face>();
            group.Add(faces[i]);
        }
        return new List<List<WoodRope.Face>>(groups.Values);
    }

    private static bool Less(Vector3Int a, Vector3Int b)
        => a.x != b.x ? a.x < b.x : a.y != b.y ? a.y < b.y : a.z < b.z;

    // Grubun yüzlerinden sıradan bağımsız anahtar
    private static string PieceKey(List<WoodRope.Face> group)
    {
        var parts = new List<string>(group.Count);
        foreach (WoodRope.Face face in group)
        {
            Vector3Int c = Vector3Int.RoundToInt(face.center * 2f), n = Vector3Int.RoundToInt(face.normal);
            parts.Add($"{c.x},{c.y},{c.z}/{n.x},{n.y},{n.z}");
        }
        parts.Sort(string.CompareOrdinal);
        return string.Join(";", parts);
    }

    private static (Vector3Int, Vector3Int, Vector3Int) NailKey(WoodNail.Joint joint)
        => (Vector3Int.RoundToInt(joint.Center * 2f), joint.Normal, joint.Along);

    private void RebuildNails()
    {
        freshNails.Clear();
        if (WoodMerger.Instance == null || !WoodMerger.Instance.Nail.Enabled) return;
        if (nailRoot == null) nailRoot = WoodMerger.Instance.Nail.CreateRoot(transform);

        var next = new Dictionary<(Vector3Int, Vector3Int, Vector3Int), Transform[]>();
        foreach (WoodNail.Joint joint in nailJoints)
        {
            var key = NailKey(joint);
            if (next.ContainsKey(key)) continue;
            if (nailObjects.TryGetValue(key, out Transform[] kept))
            {
                next[key] = kept;
                continue;
            }
            Transform[] made = WoodMerger.Instance.Nail.BuildJoint(nailRoot, joint, woodMaterial);
            next[key] = made;
            freshNails.AddRange(made);
        }
        foreach (var old in nailObjects)
        {
            if (next.ContainsKey(old.Key)) continue;
            foreach (Transform nail in old.Value)
            {
                if (nail == null) continue;
                nailRoot.Remove(nail);
                Destroy(nail.gameObject);
            }
        }
        nailObjects = next;
    }

    // Kol (ana odundan yatay, aynı yönde dizili parçalarla ana oduna varan) ile ona yandan bağlı derinlik odunu (kolun
    // yönüne dik uzanan) arasındaki ek yerleri. Çivi açıkta kalan bir yüzde: önce üst, yoksa yapının dışına en çok bakan yan.
    private void CollectNailJoints(HashSet<Vector3Int> filled)
    {
        nailJoints = new List<WoodNail.Joint>();
        var axisOf = new Dictionary<Vector3Int, Vector3Int>();
        foreach (Cell cell in cells) axisOf[cell.Position] = cell.Axis;
        Vector3Int[] horizontal = { Vector3Int.right, Vector3Int.left, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };

        foreach (Cell cell in cells)
        {
            Vector3Int arm = cell.Position;
            if (!IsArm(arm, axisOf)) continue;
            foreach (Vector3Int along in horizontal)
            {
                Vector3Int depth = arm + along;
                if (!axisOf.TryGetValue(depth, out Vector3Int depthAxis) || !Parallel(depthAxis, along)) continue;
                if (Parallel(cell.Axis, along)) continue; // kolun kendi ucu (uç uca), derinlik değil

                // Açık yüz: üst, yoksa yanlar (kolun ekseni boyunca); dışa en çok bakan
                Vector3Int normal = default;
                bool found = false;
                if (!filled.Contains(arm + Vector3Int.up) && !filled.Contains(depth + Vector3Int.up))
                {
                    normal = Vector3Int.up;
                    found = true;
                }
                else
                {
                    Vector3 middle = ((Vector3)arm + (Vector3)depth) * 0.5f;
                    float best = float.MinValue;
                    foreach (Vector3Int side in new[] { cell.Axis, -cell.Axis })
                    {
                        if (filled.Contains(arm + side) || filled.Contains(depth + side)) continue;
                        float score = Vector3.Dot(new Vector3(middle.x, 0f, middle.z), side);
                        if (score <= best) continue;
                        best = score;
                        normal = side;
                        found = true;
                    }
                }
                if (found)
                    nailJoints.Add(new WoodNail.Joint { Center = ((Vector3)arm + (Vector3)depth) * 0.5f, Normal = normal, Along = along });
            }
        }
    }

    // Kol: yatay uzanan ve kendi yönünün tersine aynı yönde dizili parçalarla ana oduna varan parça
    private bool IsArm(Vector3Int position, Dictionary<Vector3Int, Vector3Int> axisOf)
    {
        if (!axisOf.TryGetValue(position, out Vector3Int axis) || axis.y != 0) return false;
        for (Vector3Int q = position - axis; axisOf.TryGetValue(q, out Vector3Int other); q -= axis)
        {
            if (IsBaseCell(q)) return true;
            if (other != axis) return false;
        }
        return false;
    }

    private bool IsBaseCell(Vector3Int p) => p.x == 0 && p.z == 0 && p.y >= 0 && p.y < baseLength;

    // Sal bağı: uç uca iki parçanın (ikisinin de ekseni ek yerinin yönünde) ek yeri, ana odundan sayılan ek yeri sırası
    // (0 = ana odunun hemen yanı / dik sütunda en alt ek yeri) BandSpacing'in katıysa bant. Aynı düzlemdeki bütün bu ek
    // yerleri WoodRope'da birleşip kesitin çevresini saran tek halka olur.
    private void AddBandFaces(HashSet<Vector3Int> filled, List<WoodRope.Face> ropeFaces)
    {
        int spacing = WoodMerger.Instance != null ? WoodMerger.Instance.Rope.BandSpacing : 0;
        if (spacing <= 0) return;

        var axisOf = new Dictionary<Vector3Int, Vector3Int>();
        foreach (Cell cell in cells) axisOf[cell.Position] = cell.Axis;

        foreach (Cell cell in cells)
            for (int k = 0; k < 3; k++)
            {
                Vector3Int along = k == 0 ? Vector3Int.right : k == 1 ? Vector3Int.up : new Vector3Int(0, 0, 1);
                Vector3Int a = cell.Position, b = a + along;
                if (!filled.Contains(b)) continue;
                if (!Parallel(axisOf[a], along) || !Parallel(axisOf[b], along)) continue; // yan yana: bağlanmaz

                // Ek yerinin ana odundan sırası: pozitif yanda küçük hücrenin, negatif yanda büyük hücrenin uzaklığı
                int ca = a[k], cb = b[k];
                int index = ca >= 0 ? ca : -cb;
                if (index % spacing != 0) continue;

                Vector3Int normal = ca >= 0 ? along : -along; // ana odundan dışarı
                Vector3 up = normal.y != 0 ? new Vector3(0f, 0f, 1f) : Vector3.up;
                ropeFaces.Add(new WoodRope.Face { center = ((Vector3)a + (Vector3)b) * 0.5f, normal = normal, up = up });
            }
    }

    private static bool Parallel(Vector3Int axis, Vector3Int direction) => axis == direction || axis == -direction;

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
        WoodInstancer.Register(visual); // GPU instancing ile çizilir

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
