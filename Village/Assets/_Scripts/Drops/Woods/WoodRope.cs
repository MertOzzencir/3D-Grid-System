using System;
using System.Collections.Generic;
using UnityEngine;

// Bağlantı ipi: yan parçaların ana oduna değdiği yüzlerin (kaynak yerleri) kenarları boyunca tek parça ip. Ayarları
// WoodMerger'da; MergedWood bütün değme yüzleriyle bir kez Build'i çağırır.
//
// İp üst üste binmez: bütün yüzlerin kenarları ortak bir kafeste toplanır (köşeler yarım birim katlarında). İki farklı
// yüzün paylaştığı kenar (ana odunun köşesinde iki yanın ortak dikey kenarı) tek kenar sayılır, ip oradan bir kez geçer;
// aynı düzlemde iki yüzün paylaştığı kenar (aynı yanda üst üste katların arası) iki parçanın arasında kaldığı için hiç
// çizilmez, ip yığının dış çevresini dolanır. Her köşe, değdiği yüzlerin dışına ve kenarların dışa yönüne doğru odunun
// yüzeyine kaydırılır (ip oluğun içinde, odunlara oturur). Kenarlar uç uca zincirlere bağlanır (kollara ayrılan köşede
// zincir biter), köşeler yayla yuvarlatılır, zincir boyunca örgülü (sarmal lifli) tek tüp; kollara ayrılan köşelerde,
// uçlarda ve kapalı halkanın başında düğüm. Hepsi tek mesh. Birleştirme anında ip sarılarak gelir (RopeGrow): her
// zincir başından ucu sivrilerek uzar, bitince düğümler zıplayarak oturur. Sadece birleştirmede eklenen kenarlar
// (Changes) oynar; önceden olan ip sabit kalır, yeni kısım ona bağlandığı yerden uzar.
// Malzeme atanmazsa odunun malzemesinin dokusuz kopyası ip rengiyle (aynı shader: birleşince odunla birlikte sallanır).
[Serializable]
public class WoodRope
{
    [Tooltip("Açıksa yan parçaların ana oduna bağlandığı yerlere ip sarılır")]
    [SerializeField] private bool enabled = true;
    [Tooltip("İsteğe bağlı malzeme ('Village/Wood Wobble Lit' olmalı ki sallansın). Boşsa odunun malzemesi ip rengiyle.")]
    [SerializeField] private Material material;
    [SerializeField] private Color color = new Color(0.86f, 0.74f, 0.52f);
    [Tooltip("İpin kalınlığı (yarıçap, birim)")]
    [SerializeField] private float thickness = 0.045f;
    [Tooltip("Odunun hücreden taşan kısmı (birim, 1BR ~0.045): ip hücre sınırından bu kadar + kalınlığı kadar dışarıda, " +
             "odunun yüzeyine oturur")]
    [SerializeField] private float surface = 0.045f;
    [Tooltip("Köşelerin yuvarlaklığı (birim): ip köşede bu yarıçapta bir yayla döner")]
    [SerializeField] private float cornerRadius = 0.12f;
    [Tooltip("Örgüdeki lif sayısı")]
    [SerializeField, Range(0, 6)] private int strands = 3;
    [Tooltip("Liflerin kabarıklığı (kalınlığa oranla; 0 = düz tüp)")]
    [SerializeField, Range(0f, 0.5f)] private float strandDepth = 0.22f;
    [Tooltip("Bir lifin ipin etrafında bir tam tur attığı uzunluk (birim)")]
    [SerializeField] private float strandPitch = 0.35f;
    [Tooltip("Düğüm topunun büyüklüğü (kalınlığa oranla)")]
    [SerializeField] private float knotSize = 1.7f;
    [Header("Sarılma animasyonu (birleştirme anında, sadece yeni kısım)")]
    [Tooltip("İpin uzama hızı (birim / saniye)")]
    [SerializeField] private float growSpeed = 3.5f;
    [Tooltip("İp sarılmaya birleşmeden bu kadar sonra başlar (saniye)")]
    [SerializeField] private float growDelay = 0.1f;
    [Tooltip("Birden çok yeni ip parçası bu arayla sırayla sarılır (saniye)")]
    [SerializeField] private float growStagger = 0.08f;
    [Tooltip("Açılan ip önce bu oranda kalın (gevşek) gelir, sonra yaylanarak oturur (0 = yok)")]
    [SerializeField, Range(0f, 1f)] private float looseness = 0.45f;
    [Tooltip("Düğümlerin zıplayarak oturma süresi (saniye)")]
    [SerializeField] private float knotPopDuration = 0.25f;
    [Tooltip("Düğüm oturunca saçılan ip lifi sayısı (0 = yok)")]
    [SerializeField, Range(0, 30)] private int knotFibers = 10;

    private const int Sides = 12;
    private const float Step = 0.02f; // ipin boyunca örnek aralığı (örgü için sık)

    private Material generatedMaterial;
    private Material generatedFrom;

    // Animasyon verisi (Build sırasında vertex'lerle aynı sırada): vertex'in ip yolundaki merkezi, zinciri (düğümde -1)
    // ve zincir boyunca uzaklığı
    [NonSerialized] private readonly List<Vector3> animCenters = new List<Vector3>();
    [NonSerialized] private readonly List<int> animChain = new List<int>();
    [NonSerialized] private readonly List<float> animDistance = new List<float>();
    [NonSerialized] private int chainCount;

    // Bir değme yüzü: center = yüzün ortası, normal = ana odundan yan parçaya, up = yüzün bir kenar yönü (diğeri
    // normal × up); yüz 1 × 1
    public struct Face
    {
        public Vector3 center;
        public Vector3 normal;
        public Vector3 up;
    }

    // parent'ın uzayındaki değme yüzlerinden tek ip. Eklenen ipi döndürür (kapalıysa ya da yüz yoksa null).
    public GameObject Build(Transform parent, List<Face> faces, Material woodMaterial)
    {
        if (!enabled || faces.Count == 0) return null;
        animCenters.Clear();
        animChain.Clear();
        animDistance.Clear();
        chainCount = 0;

        Layout(faces, out List<(Vector3Int a, Vector3Int b)> kept, out Dictionary<Vector3Int, Vector3> positions);
        if (kept.Count == 0) return null;

        // Zincirler: derecesi 2 olmayan köşeler (uç, kollara ayrılma) zinciri böler; kalanlar kapalı halka
        var adjacency = new Dictionary<Vector3Int, List<Vector3Int>>();
        foreach ((Vector3Int a, Vector3Int b) in kept)
        {
            if (!adjacency.TryGetValue(a, out var la)) adjacency[a] = la = new List<Vector3Int>();
            if (!adjacency.TryGetValue(b, out var lb)) adjacency[b] = lb = new List<Vector3Int>();
            la.Add(b);
            lb.Add(a);
        }
        var used = new HashSet<(Vector3Int, Vector3Int)>();
        (Vector3Int, Vector3Int) EdgeKey(Vector3Int a, Vector3Int b) => Compare(a, b) <= 0 ? (a, b) : (b, a);
        List<Vector3Int> Walk(Vector3Int from, Vector3Int next)
        {
            var chain = new List<Vector3Int> { from };
            Vector3Int previous = from, current = next;
            used.Add(EdgeKey(from, next));
            while (true)
            {
                chain.Add(current);
                if (current == from || adjacency[current].Count != 2) return chain;
                Vector3Int following = adjacency[current][0] == previous ? adjacency[current][1] : adjacency[current][0];
                if (!used.Add(EdgeKey(current, following))) return chain;
                previous = current;
                current = following;
            }
        }

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        var knots = new List<Vector3>();
        foreach (var node in adjacency)
        {
            if (node.Value.Count == 2) continue;
            knots.Add(positions[node.Key]); // uç ya da kollara ayrılma: düğüm
            foreach (Vector3Int next in node.Value)
                if (!used.Contains(EdgeKey(node.Key, next)))
                    AddRope(ToPoints(Walk(node.Key, next), positions), false, vertices, normals, uvs, triangles);
        }
        foreach ((Vector3Int a, Vector3Int b) in kept)
        {
            if (used.Contains(EdgeKey(a, b))) continue;
            // Kapalı halka: ilk kenarın ortasından başlar (bütün köşeler yuvarlansın), başında düğüm
            List<Vector3> loop = ToPoints(Walk(a, b), positions);
            loop.RemoveAt(loop.Count - 1);
            Vector3 start = (loop[0] + loop[1]) * 0.5f;
            var rotated = new List<Vector3> { start };
            for (int i = 1; i < loop.Count; i++) rotated.Add(loop[i]);
            rotated.Add(loop[0]);
            rotated.Add(start);
            knots.Add(start);
            AddRope(rotated, true, vertices, normals, uvs, triangles);
        }
        foreach (Vector3 knot in knots)
            AddKnot(knot, vertices, normals, uvs, triangles);

        var mesh = new Mesh { name = "Wood Rope" };
        if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals(); // örgü kabartmaları ışık alsın
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        var rope = new GameObject("Rope");
        rope.transform.SetParent(parent, false);
        rope.AddComponent<MeshFilter>().sharedMesh = mesh;
        Material ropeMaterial = Material(woodMaterial);
        rope.AddComponent<MeshRenderer>().sharedMaterial = ropeMaterial;
        rope.AddComponent<RopeGrow>().Setup(mesh, animCenters.ToArray(), animChain.ToArray(), animDistance.ToArray(), new RopeGrow.Settings
        {
            speed = growSpeed,
            delay = growDelay,
            stagger = growStagger,
            loose = looseness,
            knotDuration = knotPopDuration,
            fibers = knotFibers,
            material = ropeMaterial
        });
        return rope;
    }

    // Yüzlerden ipin kenarları (kafeste) ve köşelerin yerleri
    private void Layout(List<Face> faces, out List<(Vector3Int a, Vector3Int b)> kept, out Dictionary<Vector3Int, Vector3> positions)
    {
        // Kenarlar (kafeste, koordinatlar × 2 tam sayı): her kenara değen yüzlerin normali ve kenarın o yüzdeki dışa yönü
        var edges = new Dictionary<(Vector3Int, Vector3Int), List<(Vector3 normal, Vector3 outward)>>();
        foreach (Face face in faces)
        {
            Vector3 u = face.up * 0.5f, v = Vector3.Cross(face.normal, face.up).normalized * 0.5f;
            Vector3[] corners = { face.center + u + v, face.center + u - v, face.center - u - v, face.center - u + v };
            Vector3[] outwards = { face.up, -v.normalized, -face.up, v.normalized }; // kenar i: köşe i → i + 1
            for (int i = 0; i < 4; i++)
            {
                Vector3Int a = Lattice(corners[i]), b = Lattice(corners[(i + 1) % 4]);
                var key = Compare(a, b) <= 0 ? (a, b) : (b, a);
                if (!edges.TryGetValue(key, out var list)) edges[key] = list = new List<(Vector3, Vector3)>();
                list.Add((face.normal, outwards[i]));
            }
        }

        // Aynı düzlemde iki yüzün paylaştığı kenar iki parçanın arasında: çizilmez. Kalan kenarların köşeleri dışa doğru
        kept = new List<(Vector3Int a, Vector3Int b)>();
        var push = new Dictionary<Vector3Int, Vector3>();
        foreach (var pair in edges)
        {
            bool inner = false;
            for (int i = 0; i < pair.Value.Count && !inner; i++)
                for (int j = i + 1; j < pair.Value.Count; j++)
                    if (Vector3.Dot(pair.Value[i].normal, pair.Value[j].normal) > 0.9f) inner = true;
            if (inner) continue;

            kept.Add(pair.Key);
            foreach ((Vector3 normal, Vector3 outward) in pair.Value)
            {
                Vector3 add = normal + outward;
                push[pair.Key.Item1] = (push.TryGetValue(pair.Key.Item1, out Vector3 p1) ? p1 : Vector3.zero) + add;
                push[pair.Key.Item2] = (push.TryGetValue(pair.Key.Item2, out Vector3 p2) ? p2 : Vector3.zero) + add;
            }
        }
        // Köşenin yeri: kafes noktası + değdiği her yön ekseninde odunun yüzeyi + ip kalınlığı kadar dışarı
        float inflate = surface + thickness;
        positions = new Dictionary<Vector3Int, Vector3>();
        foreach (var pair in push)
        {
            Vector3 s = pair.Value;
            Vector3 sign = new Vector3(Sign(s.x), Sign(s.y), Sign(s.z));
            positions[pair.Key] = (Vector3)pair.Key * 0.5f + sign * inflate;
        }
    }

    // Birleştirmede değişen kısım: şimdiki ipin kenarlarından öncekinde (previous, aynı uzayda) olmayanlar added, olanlar
    // existing (şimdiki yerleriyle). previous null ya da boşsa hepsi yeni.
    public void Changes(List<Face> current, List<Face> previous, List<(Vector3, Vector3)> added, List<(Vector3, Vector3)> existing)
    {
        Layout(current, out var kept, out var positions);
        var before = new HashSet<(Vector3Int, Vector3Int)>();
        if (previous != null && previous.Count > 0)
        {
            Layout(previous, out var oldKept, out _);
            foreach (var edge in oldKept) before.Add(edge);
        }
        foreach ((Vector3Int a, Vector3Int b) in kept)
            (before.Contains((a, b)) ? existing : added).Add((positions[a], positions[b]));
    }

    private static Vector3Int Lattice(Vector3 p) => Vector3Int.RoundToInt(p * 2f);

    private static int Compare(Vector3Int a, Vector3Int b)
    {
        if (a.x != b.x) return a.x.CompareTo(b.x);
        if (a.y != b.y) return a.y.CompareTo(b.y);
        return a.z.CompareTo(b.z);
    }

    private static float Sign(float value) => value > 0.5f ? 1f : value < -0.5f ? -1f : 0f;

    private static List<Vector3> ToPoints(List<Vector3Int> chain, Dictionary<Vector3Int, Vector3> positions)
    {
        var points = new List<Vector3>(chain.Count);
        foreach (Vector3Int node in chain) points.Add(positions[node]);
        return points;
    }

    // Kırık çizgiyi ip yoluna çevirir: köşeler yayla yuvarlanır (iki yandan cornerRadius kadar kesilip arası eğri), sonra
    // Step aralıkla örneklenir. closed: ilk ve son nokta aynı (köşeleri yuvarlanan kapalı halka)
    private List<Vector3> SmoothPath(List<Vector3> points)
    {
        var rounded = new List<Vector3> { points[0] };
        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector3 a = points[i - 1], p = points[i], b = points[i + 1];
            Vector3 toA = a - p, toB = b - p;
            if (Vector3.Dot(toA.normalized, toB.normalized) < -0.99f) { rounded.Add(p); continue; } // düz devam
            float cut = Mathf.Min(cornerRadius, toA.magnitude * 0.5f, toB.magnitude * 0.5f);
            Vector3 p1 = p + toA.normalized * cut, p2 = p + toB.normalized * cut;
            for (int k = 0; k <= 8; k++)
            {
                float s = k / 8f;
                rounded.Add((1 - s) * (1 - s) * p1 + 2 * (1 - s) * s * p + s * s * p2); // ikinci derece Bezier
            }
        }
        rounded.Add(points[points.Count - 1]);

        // Eşit aralıkla yeniden örnekle
        var result = new List<Vector3> { rounded[0] };
        float carry = 0f;
        for (int i = 1; i < rounded.Count; i++)
        {
            Vector3 from = rounded[i - 1], to = rounded[i];
            float length = Vector3.Distance(from, to);
            float d = Step - carry;
            while (d <= length)
            {
                result.Add(Vector3.Lerp(from, to, d / length));
                d += Step;
            }
            carry = length - (d - Step);
        }
        if ((result[result.Count - 1] - rounded[rounded.Count - 1]).sqrMagnitude > 1e-6f) result.Add(rounded[rounded.Count - 1]);
        return result;
    }

    // Yol boyunca örgülü tüp: halkalar paralel taşınan çerçeveyle (bükülmeden), yarıçap lifler boyunca sarmal kabarır
    private void AddRope(List<Vector3> points, bool closed, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
    {
        if (points.Count < 2) return;
        List<Vector3> path = SmoothPath(points);
        if (path.Count < 2) return;

        Vector3 tangent = (path[1] - path[0]).normalized;
        Vector3 side = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        int start = vertices.Count;
        int chain = chainCount++;
        float distance = 0f;
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 next = i + 1 < path.Count ? path[i + 1] - path[i] : path[i] - path[i - 1];
            Vector3 newTangent = next.normalized;
            if (i > 0)
            {
                distance += Vector3.Distance(path[i], path[i - 1]);
                // Paralel taşıma: çerçeve teğetin dönüşü kadar döner
                side = (Quaternion.FromToRotation(tangent, newTangent) * side).normalized;
            }
            tangent = newTangent;
            Vector3 other = Vector3.Cross(tangent, side);
            float twist = strandPitch > 0.001f ? distance / strandPitch * Mathf.PI * 2f : 0f;
            for (int s = 0; s <= Sides; s++)
            {
                float angle = s * Mathf.PI * 2f / Sides;
                Vector3 normal = side * Mathf.Cos(angle) + other * Mathf.Sin(angle);
                float radius = thickness * (1f + strandDepth * Mathf.Cos(strands * angle - twist));
                vertices.Add(path[i] + normal * radius);
                normals.Add(normal);
                uvs.Add(new Vector2(distance, (float)s / Sides));
                animCenters.Add(path[i]);
                animChain.Add(chain);
                animDistance.Add(distance);
            }
        }
        int stride = Sides + 1;
        for (int i = 0; i < path.Count - 1; i++)
            for (int s = 0; s < Sides; s++)
            {
                int a = start + i * stride + s, b = a + 1, c = a + stride, d = c + 1;
                AddFacing(a, b, c, vertices, normals, triangles);
                AddFacing(b, d, c, vertices, normals, triangles);
            }
    }

    // Köşede küçük top (iki kenarın birleşimini kapatır)
    private void AddKnot(Vector3 center, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
    {
        const int rings = 6;
        int start = vertices.Count;
        for (int r = 0; r <= rings; r++)
        {
            float phi = r * Mathf.PI / rings;
            for (int s = 0; s <= Sides; s++)
            {
                float theta = s * Mathf.PI * 2f / Sides;
                Vector3 normal = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                vertices.Add(center + normal * thickness * knotSize);
                normals.Add(normal);
                uvs.Add(new Vector2(theta, phi));
                animCenters.Add(center);
                animChain.Add(-1);
                animDistance.Add(0f);
            }
        }
        int stride = Sides + 1;
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < Sides; s++)
            {
                int a = start + r * stride + s, b = a + 1, c = a + stride, d = c + 1;
                AddFacing(a, b, c, vertices, normals, triangles);
                AddFacing(b, d, c, vertices, normals, triangles);
            }
    }

    // Üçgeni yüzü normallerle aynı tarafa bakacak sırayla ekler
    private static void AddFacing(int a, int b, int c, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
    {
        Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
        if (Vector3.Dot(face, normals[a] + normals[b] + normals[c]) < 0f) (b, c) = (c, b);
        triangles.Add(a); triangles.Add(b); triangles.Add(c);
    }

    private Material Material(Material woodMaterial)
    {
        if (material != null) return material;
        if (woodMaterial == null) return null;
        if (generatedMaterial == null || generatedFrom != woodMaterial)
        {
            // Odunun malzemesinin kopyası (aynı shader), dokusuz, ip renginde
            generatedMaterial = new Material(woodMaterial) { name = "Rope (generated)" };
            generatedMaterial.SetTexture("_BaseMap", null);
            generatedMaterial.SetTexture("_BumpMap", null);
            generatedMaterial.DisableKeyword("_NORMALMAP");
            generatedMaterial.SetColor("_BaseColor", color);
            generatedFrom = woodMaterial;
        }
        return generatedMaterial;
    }
}
