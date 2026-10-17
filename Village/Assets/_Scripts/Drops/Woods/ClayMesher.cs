using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// Kutuları kaynaşmış tek bir kil şekline çeviren hesap (Tiny Glade / Dreams yolu). Kutular önce birleştirilir
// (aynı kesitte uç uca değenler tek kutu), sonra bütün şeklin dış kenarları birlikte yuvarlatılır: her kutu
// cornerRadius kadar küçültülüp birleşimin mesafe alanından (SDF) cornerRadius çıkarılır. Aynı düzlemdeki yüzeyler
// kusursuz düz birleşir; iç köşeler keskin kalır. Mesafe alanı ızgarada örneklenip mesh çıkarılır (surface nets).
//
// Hücre hücre (chunk): şekil 1×1×1'lik hücre parçalarına bölünür (hücre sınırları -0.5 / +0.5'te, grid'le aynı). Her
// hücrenin şekli sadece o hücreye margin kadar yakın kutulara bağlıdır; o kutular hücrenin etki alanına kırpılıp bir
// parmak izi (anahtar) yapılır ve hücre şekli önbellekte bu anahtarla tutulur. Bir yapıya parça eklenince sadece parçanın
// dokunduğu hücrelerin parmak izi değişir; diğerleri önbellekten gelir: ekleme maliyeti yapının büyüklüğünden bağımsız.
// Aynı şekilli hücreler bütün yapılar arasında ortak. Hücreler ortak bir ızgarada (adım = 1 / N) ve sınırın iki yanında
// aynı kutularla hesaplandığı için komşu hücrelerin sınırdaki vertex'leri birebir aynı: çatlak yok.
// Kutular ve sonuç aynı uzayda (çağıranın yerel uzayı).
public static class ClayMesher
{
    public struct Part
    {
        public Vector3 center;
        public Vector3 half;
        public Vector3 Min => center - half;
        public Vector3 Max => center + half;
    }

    // Hücre şekilleri: parmak izi → mesh (hücrenin kendi uzayında, [0, 1]); null = o hücrede yüzey yok
    private static readonly Dictionary<string, Mesh> chunkCache = new Dictionary<string, Mesh>();
    private const int MaxCachedChunks = 6000;

    // boxes: kutular (çağıranın uzayında); voxelSize: ızgara aralığı (1 / N'ye yuvarlanır); cornerRadius: dış kenar yuvarlaklığı
    public static Mesh Build(List<Bounds> boxes, float voxelSize, float cornerRadius, float inset = 0f)
    {
        var parts = new List<Part>();
        foreach (Bounds box in boxes)
            parts.Add(new Part { center = box.center, half = Vector3.Max(box.extents - Vector3.one * inset, Vector3.one * 0.02f) });
        if (parts.Count == 0) return null;
        MergeBoxes(parts);

        int n = Mathf.Clamp(Mathf.RoundToInt(1f / Mathf.Max(voxelSize, 0.01f)), 4, 64);
        float step = 1f / n;
        // Yuvarlaklık yapının en ince parçasıyla sınırlı (bütün hücrelerde aynı olmalı ki sınırlar uyuşsun)
        float radius = cornerRadius;
        foreach (Part part in parts)
            radius = Mathf.Min(radius, Mathf.Min(part.half.x, Mathf.Min(part.half.y, part.half.z)) * 0.95f);
        radius = Mathf.Max(0f, radius);
        float margin = radius + step * 3f;

        if (chunkCache.Count > MaxCachedChunks) chunkCache.Clear();

        // Kutuların dokunduğu hücreler
        Vector3 min = parts[0].Min, max = parts[0].Max;
        foreach (Part part in parts)
        {
            min = Vector3.Min(min, part.Min);
            max = Vector3.Max(max, part.Max);
        }
        Vector3Int from = Vector3Int.FloorToInt(min + Vector3.one * (0.5f - margin));
        Vector3Int to = Vector3Int.FloorToInt(max + Vector3.one * (0.5f + margin));

        var combine = new List<CombineInstance>();
        var clipped = new List<Part>();
        var key = new StringBuilder(256);
        for (int cz = from.z; cz <= to.z; cz++)
            for (int cy = from.y; cy <= to.y; cy++)
                for (int cx = from.x; cx <= to.x; cx++)
                {
                    Vector3 origin = new Vector3(cx, cy, cz) - Vector3.one * 0.5f; // hücrenin alt köşesi
                    Vector3 regionMin = origin - Vector3.one * margin, regionMax = origin + Vector3.one * (1f + margin);

                    // Hücrenin içi tamamen bir kutunun derinindeyse yüzey yok
                    bool buried = false;
                    clipped.Clear();
                    foreach (Part part in parts)
                    {
                        Vector3 pMin = Vector3.Max(part.Min, regionMin), pMax = Vector3.Min(part.Max, regionMax);
                        if (pMin.x >= pMax.x || pMin.y >= pMax.y || pMin.z >= pMax.z) continue;
                        if (Inside(part, origin - Vector3.one * (radius + step * 2f), origin + Vector3.one * (1f + radius + step * 2f)))
                            buried = true;
                        // Hücrenin uzayında (alt köşe 0)
                        clipped.Add(new Part { center = (pMin + pMax) * 0.5f - origin, half = (pMax - pMin) * 0.5f });
                    }
                    if (buried || clipped.Count == 0) continue;

                    string chunkKey = Key(key, clipped, n, radius);
                    if (!chunkCache.TryGetValue(chunkKey, out Mesh mesh))
                    {
                        mesh = ChunkMesh(clipped, n, step, radius);
                        chunkCache[chunkKey] = mesh;
                    }
                    if (mesh != null) combine.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.Translate(origin) });
                }
        if (combine.Count == 0) return null;

        var result = new Mesh { name = "Clay", indexFormat = IndexFormat.UInt32 };
        result.CombineMeshes(combine.ToArray(), true, true);
        result.RecalculateBounds();
        return result;
    }

    private static bool Inside(Part part, Vector3 min, Vector3 max) =>
        part.Min.x <= min.x && part.Min.y <= min.y && part.Min.z <= min.z &&
        part.Max.x >= max.x && part.Max.y >= max.y && part.Max.z >= max.z;

    // Hücrenin parmak izi: ızgara, yarıçap ve kırpılmış kutular (sıralı, yuvarlanmış)
    private static string Key(StringBuilder sb, List<Part> clipped, int n, float radius)
    {
        clipped.Sort((a, b) =>
        {
            int c = a.center.x.CompareTo(b.center.x);
            if (c != 0) return c;
            c = a.center.y.CompareTo(b.center.y);
            return c != 0 ? c : a.center.z.CompareTo(b.center.z);
        });
        sb.Clear();
        sb.Append(n).Append('|').Append(Mathf.RoundToInt(radius * 10000f));
        foreach (Part part in clipped)
        {
            Vector3 lo = part.Min, hi = part.Max;
            sb.Append('|').Append(Mathf.RoundToInt(lo.x * 1000f)).Append(',').Append(Mathf.RoundToInt(lo.y * 1000f)).Append(',').Append(Mathf.RoundToInt(lo.z * 1000f))
              .Append(':').Append(Mathf.RoundToInt(hi.x * 1000f)).Append(',').Append(Mathf.RoundToInt(hi.y * 1000f)).Append(',').Append(Mathf.RoundToInt(hi.z * 1000f));
        }
        return sb.ToString();
    }

    // Aynı kesitte, uç uca değen kutular tek kutu olur (uzun kirişin 1'er birimlik parçaları, üst üste katlar): iki
    // yuvarlak kutu yan yana gelince yuvarlak kenarları birleşim yerinde oluk bırakıyordu
    public static void MergeBoxes(List<Part> parts)
    {
        const float eps = 0.03f;
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int a = 0; a < parts.Count && !changed; a++)
                for (int b = a + 1; b < parts.Count && !changed; b++)
                    for (int axis = 0; axis < 3 && !changed; axis++)
                    {
                        Part pa = parts[a], pb = parts[b];
                        bool sameSection = true;
                        for (int other = 0; other < 3; other++)
                        {
                            if (other == axis) continue;
                            if (Mathf.Abs(pa.center[other] - pb.center[other]) > eps || Mathf.Abs(pa.half[other] - pb.half[other]) > eps)
                                sameSection = false;
                        }
                        if (!sameSection) continue;
                        float gap = Mathf.Abs(pa.center[axis] - pb.center[axis]) - pa.half[axis] - pb.half[axis];
                        if (gap > eps) continue;

                        float lo = Mathf.Min(pa.center[axis] - pa.half[axis], pb.center[axis] - pb.half[axis]);
                        float hi = Mathf.Max(pa.center[axis] + pa.half[axis], pb.center[axis] + pb.half[axis]);
                        Vector3 center = pa.center, half = pa.half;
                        center[axis] = (lo + hi) * 0.5f;
                        half[axis] = (hi - lo) * 0.5f;
                        parts[a] = new Part { center = center, half = half };
                        parts.RemoveAt(b);
                        changed = true;
                    }
        }
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    // Mesafe alanı (eksi = içeride): radius kadar küçültülmüş kutuların birleşimi, sonra hepsi birlikte radius kadar
    // şişirilir (dış kenarlar yuvarlak, aynı düzlemdeki yüzeyler düz)
    private static float Sdf(List<Part> parts, Vector3 p, float radius)
    {
        float d = float.MaxValue;
        foreach (Part part in parts)
        {
            Vector3 q = Abs(p - part.center) - (part.half - Vector3.one * radius);
            float box = Vector3.Max(q, Vector3.zero).magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
            if (box < d) d = box;
        }
        return d - radius;
    }

    private static Vector3 Gradient(List<Part> parts, Vector3 p, float h, float radius)
    {
        Vector3 g = new Vector3(
            Sdf(parts, p + Vector3.right * h, radius) - Sdf(parts, p - Vector3.right * h, radius),
            Sdf(parts, p + Vector3.up * h, radius) - Sdf(parts, p - Vector3.up * h, radius),
            Sdf(parts, p + Vector3.forward * h, radius) - Sdf(parts, p - Vector3.forward * h, radius));
        return g.sqrMagnitude > 1e-10f ? g.normalized : Vector3.up;
    }

    // Bir hücrenin (uzayı [0, 1]) şekli, surface nets. Noktalar -1..n+1 (komşu küplerin vertex'leri için bir sıra
    // fazla), küp vertex'leri -1..n-1, dörtgenler tabanı 0..n-1 olan ızgara kenarları için (her kenar tek hücrenin).
    // Izgara yarım adım kaydırılmış (noktalar (i + 0.5) * adım): odun yüzleri hücre sınırlarında (kalınlık 1) ve sınır
    // bir ızgara noktasına denk gelince iki komşu hücre küçük bir hesap farkıyla yüzü birbirine bırakıp ikisi de
    // çizmiyordu (delikler). Kaydırınca yüz hep bir kenarın ortasında, sahibi kesin.
    private static Mesh ChunkMesh(List<Part> parts, int n, float step, float radius)
    {
        int m = n + 3;                        // nokta sayısı (eksen başına), indeks i ↔ ızgara -1 + i
        int Index(int x, int y, int z) => x + m * (y + m * z);
        Vector3 Point(int x, int y, int z) => new Vector3(x - 0.5f, y - 0.5f, z - 0.5f) * step;

        var values = new float[m * m * m];
        bool anyInside = false, anyOutside = false;
        for (int z = 0; z < m; z++)
            for (int y = 0; y < m; y++)
                for (int x = 0; x < m; x++)
                {
                    float v = Sdf(parts, Point(x, y, z), radius);
                    values[Index(x, y, z)] = v;
                    if (v < 0f) anyInside = true; else anyOutside = true;
                }
        if (!anyInside || !anyOutside) return null;

        int c = m - 1; // küp sayısı (eksen başına), küp indeksi i: noktalar i..i+1
        var cellVertex = new int[c * c * c];
        int CellIndex(int x, int y, int z) => x + c * (y + c * z);
        for (int i = 0; i < cellVertex.Length; i++) cellVertex[i] = -1;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var corner = new float[8];
        var cornerPos = new Vector3[8];
        for (int z = 0; z < c; z++)
            for (int y = 0; y < c; y++)
                for (int x = 0; x < c; x++)
                {
                    int inside = 0;
                    for (int k = 0; k < 8; k++)
                    {
                        int px = x + (k & 1), py = y + ((k >> 1) & 1), pz = z + ((k >> 2) & 1);
                        corner[k] = values[Index(px, py, pz)];
                        cornerPos[k] = Point(px, py, pz);
                        if (corner[k] < 0f) inside++;
                    }
                    if (inside == 0 || inside == 8) continue;

                    Vector3 sum = Vector3.zero;
                    int count = 0;
                    for (int e = 0; e < 12; e++)
                    {
                        int a = Edges[e, 0], b = Edges[e, 1];
                        if ((corner[a] < 0f) == (corner[b] < 0f)) continue;
                        float t = corner[a] / (corner[a] - corner[b]);
                        sum += Vector3.Lerp(cornerPos[a], cornerPos[b], t);
                        count++;
                    }
                    Vector3 v = sum / count;
                    cellVertex[CellIndex(x, y, z)] = vertices.Count;
                    vertices.Add(v);
                    normals.Add(Gradient(parts, v, step * 0.5f, radius));
                }

        var triangles = new List<int>();
        void Quad(int a, int b, int cc, int d)
        {
            if (a < 0 || b < 0 || cc < 0 || d < 0) return;
            triangles.Add(a); triangles.Add(b); triangles.Add(cc);
            triangles.Add(a); triangles.Add(cc); triangles.Add(d);
        }
        // Taban noktası hücrenin içinde (ızgara 0..n-1 ↔ nokta indeksi 1..n) olan kenarlar
        for (int z = 1; z <= n; z++)
            for (int y = 1; y <= n; y++)
                for (int x = 1; x <= n; x++)
                {
                    bool a = values[Index(x, y, z)] < 0f;
                    if (a != values[Index(x + 1, y, z)] < 0f)
                        Quad(cellVertex[CellIndex(x, y - 1, z - 1)], cellVertex[CellIndex(x, y, z - 1)], cellVertex[CellIndex(x, y, z)], cellVertex[CellIndex(x, y - 1, z)]);
                    if (a != values[Index(x, y + 1, z)] < 0f)
                        Quad(cellVertex[CellIndex(x - 1, y, z - 1)], cellVertex[CellIndex(x - 1, y, z)], cellVertex[CellIndex(x, y, z)], cellVertex[CellIndex(x, y, z - 1)]);
                    if (a != values[Index(x, y, z + 1)] < 0f)
                        Quad(cellVertex[CellIndex(x - 1, y - 1, z)], cellVertex[CellIndex(x, y - 1, z)], cellVertex[CellIndex(x, y, z)], cellVertex[CellIndex(x - 1, y, z)]);
                }
        if (triangles.Count == 0) return null;

        // Yön: üçgenin yüzü mesafe alanının dışa yönüyle (normal) aynı tarafa
        for (int i = 0; i < triangles.Count; i += 3)
        {
            Vector3 p0 = vertices[triangles[i]], p1 = vertices[triangles[i + 1]], p2 = vertices[triangles[i + 2]];
            Vector3 face = Vector3.Cross(p1 - p0, p2 - p0);
            Vector3 outward = normals[triangles[i]] + normals[triangles[i + 1]] + normals[triangles[i + 2]];
            if (Vector3.Dot(face, outward) < 0f) (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
        }

        var mesh = new Mesh { name = "Clay Chunk" };
        if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static readonly int[,] Edges =
        { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
}
