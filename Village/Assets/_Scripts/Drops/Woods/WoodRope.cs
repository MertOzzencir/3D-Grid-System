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
// yüzeyine kaydırılır (ip oluğun içinde, odunlara oturur). Kenarlar tüp, köşeler küçük düğüm topu; hepsi tek mesh.
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

    private const int Sides = 8;

    private Material generatedMaterial;
    private Material generatedFrom;

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
        var kept = new List<(Vector3Int a, Vector3Int b)>();
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
        if (kept.Count == 0) return null;

        // Köşenin yeri: kafes noktası + değdiği her yön ekseninde odunun yüzeyi + ip kalınlığı kadar dışarı
        float inflate = surface + thickness;
        var positions = new Dictionary<Vector3Int, Vector3>();
        foreach (var pair in push)
        {
            Vector3 s = pair.Value;
            Vector3 sign = new Vector3(Sign(s.x), Sign(s.y), Sign(s.z));
            positions[pair.Key] = (Vector3)pair.Key * 0.5f + sign * inflate;
        }

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        foreach ((Vector3Int a, Vector3Int b) in kept)
            AddTube(positions[a], positions[b], vertices, normals, triangles);
        foreach (Vector3 p in positions.Values)
            AddKnot(p, vertices, normals, triangles);

        var mesh = new Mesh { name = "Wood Rope" };
        if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, new List<Vector2>(new Vector2[vertices.Count]));
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        var rope = new GameObject("Rope");
        rope.transform.SetParent(parent, false);
        rope.AddComponent<MeshFilter>().sharedMesh = mesh;
        rope.AddComponent<MeshRenderer>().sharedMaterial = Material(woodMaterial);
        return rope;
    }

    private static Vector3Int Lattice(Vector3 p) => Vector3Int.RoundToInt(p * 2f);

    private static int Compare(Vector3Int a, Vector3Int b)
    {
        if (a.x != b.x) return a.x.CompareTo(b.x);
        if (a.y != b.y) return a.y.CompareTo(b.y);
        return a.z.CompareTo(b.z);
    }

    private static float Sign(float value) => value > 0.5f ? 1f : value < -0.5f ? -1f : 0f;

    // a'dan b'ye tüp
    private void AddTube(Vector3 a, Vector3 b, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
    {
        Vector3 axis = (b - a).normalized;
        Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 other = Vector3.Cross(axis, side);
        int start = vertices.Count;
        for (int s = 0; s <= Sides; s++)
        {
            float angle = s * Mathf.PI * 2f / Sides;
            Vector3 normal = side * Mathf.Cos(angle) + other * Mathf.Sin(angle);
            vertices.Add(a + normal * thickness);
            normals.Add(normal);
            vertices.Add(b + normal * thickness);
            normals.Add(normal);
        }
        for (int s = 0; s < Sides; s++)
        {
            int a0 = start + s * 2, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
            AddFacing(a0, a1, b0, vertices, normals, triangles);
            AddFacing(a1, b1, b0, vertices, normals, triangles);
        }
    }

    // Köşede küçük top (iki kenarın birleşimini kapatır)
    private void AddKnot(Vector3 center, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
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
                vertices.Add(center + normal * thickness * 1.15f);
                normals.Add(normal);
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
