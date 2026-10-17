using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// Odun hücrelerini kaynaşmış tek bir şekle çeviren hesap. Şekil de görünüm de 1BR odun modelinden (Reference): her odun
// hücresine parçanın dönüşüyle 1BR'nin kendisi oturur (modelin mesafe alanı, SDF). Dolu bir komşuya bakan yönde model
// düz uzatılır: modelin o yöndeki orta kesiti (extrudeFrom'dan sonrası) komşu hücreye kadar çekilir, iki hücrenin
// uzatmaları birbirine biner. Böylece birleşim yerinde modelin yuvarlak kenarları kalkar (oluk yok), açıkta kalan
// yüzler ve uçlar 1BR'nin aynısı. Bütün hücrelerin birleşimi (min) ızgarada örneklenip mesh çıkarılır (surface nets).
//
// Görünüm: kilin her vertex'i sahibi olan hücredeki (mesafesi en küçük) 1BR'nin en yakın noktasının UV'sini ve
// tangent'ını alır. Uzatılan (kaynaşan) kısımda detay 0: shader orada dokunun boş, düz renkli yerini kullanır, normal
// map yok; uzatmanın başlangıcından fuseWidth içeriye doğru detay yumuşakça 1'e döner. Detay tangent.w'de:
// işaret × (1 + detay) ("Village/Clay Wood Lit" çözer).
//
// Hücre hücre (chunk): şekil 1×1×1'lik hücre parçalarına bölünür (hücre sınırları -0.5 / +0.5'te, grid'le aynı). Her
// hücrenin şekli sadece ona margin kadar yakın odun hücrelerine (ortası, dönüşü, dolu komşuları) bağlıdır; bunlardan
// bir parmak izi (anahtar) yapılır ve hücre şekli önbellekte bu anahtarla tutulur. Bir yapıya parça eklenince sadece
// parçanın çevresindeki hücrelerin izi değişir; diğerleri önbellekten gelir: ekleme maliyeti yapının büyüklüğünden
// bağımsız. Hücreler ortak bir ızgarada (adım = 1 / N) ve sınırın iki yanında aynı verilerle hesaplandığı için komşu
// hücrelerin sınırdaki vertex'leri birebir aynı: çatlak yok. Hepsi çağıranın yerel uzayında.
public static class ClayMesher
{
    // Bir odun parçası: start = ilk hücrenin ortası, rotation = parçanın dönüşü (1BR bu dönüşle hücrelere oturur,
    // uzandığı yön rotation * yukarı), length = hücre sayısı
    public struct Piece
    {
        public Vector3 start;
        public Quaternion rotation;
        public int length;
    }

    // Tek odun hücresi: ortası, 1BR'nin dönüşü, dolu komşular (bit: +x, -x, +y, -y, +z, -z)
    private struct Cell
    {
        public Vector3 center;
        public Quaternion rotation;
        public int neighbors;
    }

    private static readonly Vector3[] Directions =
        { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

    private const float Overlap = 0.05f; // uzatma komşu hücreye bu kadar taşar (iki uzatma birbirine binsin)

    // Hücre şekilleri: parmak izi → mesh (hücrenin kendi uzayında, [0, 1]); null = o hücrede yüzey yok
    private static readonly Dictionary<string, Mesh> chunkCache = new Dictionary<string, Mesh>();
    private const int MaxCachedChunks = 6000;

    // pieces: parçalar (çağıranın uzayında); reference: şeklin ve görünümün alındığı 1BR; voxelSize: ızgara aralığı
    // (1 / N'ye yuvarlanır); extrudeFrom: dolu komşuya doğru model hücre ortasından bu kadar uzakta uzatılmaya başlar;
    // fuseWidth: uzatmanın başından içeri doğru detayın geri geldiği genişlik
    public static Mesh Build(List<Piece> pieces, Reference reference, float voxelSize, float extrudeFrom, float fuseWidth)
    {
        if (reference == null) return null;
        var cells = new List<Cell>();
        foreach (Piece piece in pieces)
        {
            Vector3 axis = piece.rotation * Vector3.up;
            for (int i = 0; i < piece.length; i++)
                cells.Add(new Cell { center = piece.start + axis * i, rotation = piece.rotation });
        }
        if (cells.Count == 0) return null;

        // Dolu komşular
        var filled = new HashSet<Vector3Int>();
        foreach (Cell cell in cells) filled.Add(CellKey(cell.center));
        for (int i = 0; i < cells.Count; i++)
        {
            Cell cell = cells[i];
            for (int d = 0; d < 6; d++)
                if (filled.Contains(CellKey(cell.center + Directions[d]))) cell.neighbors |= 1 << d;
            cells[i] = cell;
        }

        int n = Mathf.Clamp(Mathf.RoundToInt(1f / Mathf.Max(voxelSize, 0.01f)), 4, 64);
        float step = 1f / n;
        extrudeFrom = Mathf.Clamp(extrudeFrom, 0.05f, 0.45f);
        fuseWidth = Mathf.Max(fuseWidth, 0.001f);
        // Bir noktanın değeri en fazla bir hücre ötedeki odunlara bağlı (model hücreden ~0.05 taşar)
        float margin = 0.6f + step * 3f;

        if (chunkCache.Count > MaxCachedChunks) chunkCache.Clear();

        Vector3 min = cells[0].center, max = cells[0].center;
        foreach (Cell cell in cells)
        {
            min = Vector3.Min(min, cell.center);
            max = Vector3.Max(max, cell.center);
        }
        // Model hücreden biraz taşar: bir hücre öteye kadar bak
        Vector3Int from = Vector3Int.FloorToInt(min - Vector3.one * 0.5f);
        Vector3Int to = Vector3Int.FloorToInt(max + Vector3.one * 1.5f);

        var combine = new List<CombineInstance>();
        var localCells = new List<Cell>();
        var key = new StringBuilder(256);
        for (int cz = from.z; cz <= to.z; cz++)
            for (int cy = from.y; cy <= to.y; cy++)
                for (int cx = from.x; cx <= to.x; cx++)
                {
                    Vector3 origin = new Vector3(cx, cy, cz) - Vector3.one * 0.5f; // hücrenin alt köşesi
                    Vector3 regionMin = origin - Vector3.one * margin, regionMax = origin + Vector3.one * (1f + margin);

                    // Bölgeye yakın odun hücreleri (hücrenin uzayında)
                    localCells.Clear();
                    foreach (Cell cell in cells)
                    {
                        Vector3 c = cell.center;
                        if (c.x < regionMin.x || c.x > regionMax.x || c.y < regionMin.y || c.y > regionMax.y ||
                            c.z < regionMin.z || c.z > regionMax.z) continue;
                        localCells.Add(new Cell { center = c - origin, rotation = cell.rotation, neighbors = cell.neighbors });
                    }
                    if (localCells.Count == 0) continue;

                    string chunkKey = Key(key, localCells, n, extrudeFrom, fuseWidth, reference);
                    if (!chunkCache.TryGetValue(chunkKey, out Mesh mesh))
                    {
                        mesh = ChunkMesh(localCells, reference, n, step, extrudeFrom, fuseWidth);
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

    private static Vector3Int CellKey(Vector3 center) => Vector3Int.RoundToInt(center * 4f);

    // Hücrenin parmak izi: ızgara, uzatma ayarları, referans ve odun hücreleri (sıralı, yuvarlanmış)
    private static string Key(StringBuilder sb, List<Cell> localCells, int n, float extrudeFrom, float fuseWidth, Reference reference)
    {
        localCells.Sort((a, b) =>
        {
            int c = a.center.x.CompareTo(b.center.x);
            if (c != 0) return c;
            c = a.center.y.CompareTo(b.center.y);
            return c != 0 ? c : a.center.z.CompareTo(b.center.z);
        });
        sb.Clear();
        sb.Append(n).Append('|').Append(Mathf.RoundToInt(extrudeFrom * 10000f)).Append('|').Append(Mathf.RoundToInt(fuseWidth * 10000f))
          .Append('|').Append(reference.Id);
        foreach (Cell cell in localCells)
        {
            Vector3 c = cell.center;
            Quaternion r = cell.rotation;
            sb.Append('|').Append(Mathf.RoundToInt(c.x * 1000f)).Append(',').Append(Mathf.RoundToInt(c.y * 1000f)).Append(',').Append(Mathf.RoundToInt(c.z * 1000f))
              .Append(':').Append(Mathf.RoundToInt(r.x * 1000f)).Append(',').Append(Mathf.RoundToInt(r.y * 1000f)).Append(',')
              .Append(Mathf.RoundToInt(r.z * 1000f)).Append(',').Append(Mathf.RoundToInt(r.w * 1000f)).Append(':').Append(cell.neighbors);
        }
        return sb.ToString();
    }

    // Noktanın bir hücreye göre modeldeki karşılığı: dolu komşulara doğru extrudeFrom'dan ötesi geri çekilir (uzatma),
    // sonuç 1BR'nin uzayında. limit: uzatmanın komşu hücrede bittiği düzleme uzaklık (pozitif = ötesinde)
    private static Vector3 ModelPoint(Cell cell, Vector3 p, float extrudeFrom, out float limit, out float extruded)
    {
        Vector3 offset = p - cell.center;
        limit = float.MinValue;
        extruded = float.MinValue; // uzatma başlangıcına göre en derin konum (pozitif = uzatılan kısımda)
        for (int d = 0; d < 6; d++)
        {
            if ((cell.neighbors & (1 << d)) == 0) continue;
            float t = Vector3.Dot(p - cell.center, Directions[d]);
            limit = Mathf.Max(limit, t - (0.5f + Overlap));
            extruded = Mathf.Max(extruded, t - extrudeFrom);
            if (t > extrudeFrom) offset -= Directions[d] * (t - extrudeFrom);
        }
        return Quaternion.Inverse(cell.rotation) * offset;
    }

    // Bir hücrenin (uzatılmış) 1BR'sinin mesafesi
    private static float CellDistance(Cell cell, Reference reference, Vector3 p, float extrudeFrom)
    {
        Vector3 local = ModelPoint(cell, p, extrudeFrom, out float limit, out _);
        return Mathf.Max(reference.Distance(local), limit);
    }

    // Mesafe alanı (eksi = içeride): bütün hücrelerin birleşimi
    private static float Sdf(List<Cell> cells, Reference reference, Vector3 p, float extrudeFrom)
    {
        float d = float.MaxValue;
        foreach (Cell cell in cells)
            d = Mathf.Min(d, CellDistance(cell, reference, p, extrudeFrom));
        return d;
    }

    private static Vector3 Gradient(List<Cell> cells, Reference reference, Vector3 p, float h, float extrudeFrom)
    {
        Vector3 g = new Vector3(
            Sdf(cells, reference, p + Vector3.right * h, extrudeFrom) - Sdf(cells, reference, p - Vector3.right * h, extrudeFrom),
            Sdf(cells, reference, p + Vector3.up * h, extrudeFrom) - Sdf(cells, reference, p - Vector3.up * h, extrudeFrom),
            Sdf(cells, reference, p + Vector3.forward * h, extrudeFrom) - Sdf(cells, reference, p - Vector3.forward * h, extrudeFrom));
        return g.sqrMagnitude > 1e-10f ? g.normalized : Vector3.up;
    }

    // Vertex'in görünümü: sahibi (mesafesi en küçük) hücredeki 1BR'nin en yakın noktasının UV'si ve tangent'ı; detay
    // uzatılan kısımda 0, uzatmanın başından fuseWidth içeride 1
    private static void Appearance(List<Cell> cells, Reference reference, Vector3 v, float extrudeFrom, float fuseWidth,
                                   out Vector2 uv, out Vector4 tangent)
    {
        int owner = -1;
        float best = float.MaxValue;
        for (int i = 0; i < cells.Count; i++)
        {
            float d = CellDistance(cells[i], reference, v, extrudeFrom);
            if (d < best)
            {
                best = d;
                owner = i;
            }
        }
        Cell cell = cells[owner];
        Vector3 local = ModelPoint(cell, v, extrudeFrom, out _, out float extruded);
        float detail = extruded == float.MinValue ? 1f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((extruded + fuseWidth) / fuseWidth));

        reference.Sample(local, out uv, out Vector4 localTangent);
        Vector3 t = cell.rotation * new Vector3(localTangent.x, localTangent.y, localTangent.z);
        float sign = localTangent.w < 0f ? -1f : 1f;
        tangent = new Vector4(t.x, t.y, t.z, sign * (1f + detail));
    }

    // Bir hücrenin (uzayı [0, 1]) şekli, surface nets. Noktalar -1..n+1 (komşu küplerin vertex'leri için bir sıra
    // fazla), küp vertex'leri -1..n-1, dörtgenler tabanı 0..n-1 olan ızgara kenarları için (her kenar tek hücrenin).
    // Izgara yarım adım kaydırılmış (noktalar (i + 0.5) * adım): hücre sınırına denk gelen yüzlerin sahibi kesin olsun.
    private static Mesh ChunkMesh(List<Cell> cells, Reference reference, int n, float step, float extrudeFrom, float fuseWidth)
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
                    float v = Sdf(cells, reference, Point(x, y, z), extrudeFrom);
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
        var uvs = new List<Vector2>();
        var tangents = new List<Vector4>();
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
                    normals.Add(Gradient(cells, reference, v, step * 0.5f, extrudeFrom));
                    Appearance(cells, reference, v, extrudeFrom, fuseWidth, out Vector2 uv, out Vector4 tangent);
                    uvs.Add(uv);
                    tangents.Add(tangent);
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
        mesh.SetUVs(0, uvs);
        mesh.SetTangents(tangents);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static readonly int[,] Edges =
        { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };

    // Şeklin ve görünümün alındığı model (1BR): mesh'in üçgenleri hücre uzayında (ortası 0, odun +Y yönünde).
    // - Distance: modelin mesafe alanı (eksi = içeride), bir kez hesaplanıp ızgarada tutulur (DistanceRes³, trilinear).
    // - Sample: bir noktanın en yakın yüzey noktasının UV'si ve tangent'ı.
    // Hız için üçgenler ızgara kovalarına dağıtılır (her kova kendisine yakın üçgenleri tutar).
    public sealed class Reference
    {
        private const int Grid = 16;
        private const int DistanceRes = 40;
        private const float DistanceRange = 0.8f; // mesafe ızgarası [-0.8, 0.8]³

        public int Id { get; }

        private readonly Vector3[] positions;
        private readonly Vector3[] normals;
        private readonly Vector2[] uvs;
        private readonly Vector4[] tangents;
        private readonly int[] triangles;
        private readonly List<int>[] buckets = new List<int>[Grid * Grid * Grid];
        private readonly Vector3 boundsMin, bucketSize, meshMin, meshMax;
        private readonly float pad;
        private float[] distances;

        // toCell: mesh uzayından hücre uzayına (1BR prefab'ının kökü = hücrenin ortası)
        public Reference(Mesh mesh, Matrix4x4 toCell, int id)
        {
            Id = id;
            Vector3[] source = mesh.vertices;
            Vector3[] sourceNormals = mesh.normals;
            Vector4[] sourceTangents = mesh.tangents;
            Vector2[] sourceUvs = mesh.uv;
            triangles = mesh.triangles;
            positions = new Vector3[source.Length];
            normals = new Vector3[source.Length];
            uvs = new Vector2[source.Length];
            tangents = new Vector4[source.Length];
            Matrix4x4 normalMatrix = toCell.inverse.transpose;
            for (int i = 0; i < source.Length; i++)
            {
                positions[i] = toCell.MultiplyPoint3x4(source[i]);
                normals[i] = i < sourceNormals.Length ? normalMatrix.MultiplyVector(sourceNormals[i]).normalized : Vector3.up;
                uvs[i] = i < sourceUvs.Length ? sourceUvs[i] : Vector2.zero;
                Vector4 t = i < sourceTangents.Length ? sourceTangents[i] : new Vector4(1f, 0f, 0f, 1f);
                Vector3 dir = toCell.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                tangents[i] = new Vector4(dir.x, dir.y, dir.z, t.w < 0f ? -1f : 1f);
            }

            meshMin = Vector3.one * float.MaxValue;
            meshMax = Vector3.one * float.MinValue;
            foreach (Vector3 p in positions)
            {
                meshMin = Vector3.Min(meshMin, p);
                meshMax = Vector3.Max(meshMax, p);
            }
            boundsMin = Vector3.one * -(DistanceRange + 0.05f);
            bucketSize = Vector3.one * (2f * (DistanceRange + 0.05f) / Grid);
            pad = bucketSize.x * 1.5f;
            for (int i = 0; i < buckets.Length; i++) buckets[i] = new List<int>();
            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 a = positions[triangles[t]], b = positions[triangles[t + 1]], c = positions[triangles[t + 2]];
                Vector3Int lo = BucketOf(Vector3.Min(a, Vector3.Min(b, c)) - Vector3.one * pad);
                Vector3Int hi = BucketOf(Vector3.Max(a, Vector3.Max(b, c)) + Vector3.one * pad);
                for (int z = lo.z; z <= hi.z; z++)
                    for (int y = lo.y; y <= hi.y; y++)
                        for (int x = lo.x; x <= hi.x; x++)
                            buckets[x + Grid * (y + Grid * z)].Add(t);
            }
        }

        private Vector3Int BucketOf(Vector3 p)
        {
            Vector3 f = p - boundsMin;
            return new Vector3Int(Mathf.Clamp((int)(f.x / bucketSize.x), 0, Grid - 1), Mathf.Clamp((int)(f.y / bucketSize.y), 0, Grid - 1),
                                  Mathf.Clamp((int)(f.z / bucketSize.z), 0, Grid - 1));
        }

        // En yakın üçgen (kovasından); kova boşsa -1 (yüzeyden pad'den uzak)
        private int Nearest(Vector3 p, out Vector3 bary, out float distanceSqr)
        {
            Vector3Int b = BucketOf(p);
            List<int> list = buckets[b.x + Grid * (b.y + Grid * b.z)];
            int bestTriangle = -1;
            bary = Vector3.zero;
            distanceSqr = float.MaxValue;
            foreach (int t in list)
            {
                Vector3 q = Closest(p, positions[triangles[t]], positions[triangles[t + 1]], positions[triangles[t + 2]], out Vector3 w);
                float d = (q - p).sqrMagnitude;
                if (d < distanceSqr)
                {
                    distanceSqr = d;
                    bestTriangle = t;
                    bary = w;
                }
            }
            return bestTriangle;
        }

        // Modelin mesafe alanı (hücre uzayında; eksi = içeride). Izgara dışında: ızgaraya en yakın noktanın değeri + uzaklık
        public float Distance(Vector3 p)
        {
            if (distances == null) BuildDistances();
            Vector3 clamped = Vector3.Max(Vector3.one * -DistanceRange, Vector3.Min(Vector3.one * DistanceRange, p));
            float cell = 2f * DistanceRange / (DistanceRes - 1);
            Vector3 f = (clamped + Vector3.one * DistanceRange) / cell;
            int x = Mathf.Min((int)f.x, DistanceRes - 2), y = Mathf.Min((int)f.y, DistanceRes - 2), z = Mathf.Min((int)f.z, DistanceRes - 2);
            float tx = f.x - x, ty = f.y - y, tz = f.z - z;
            float D(int i, int j, int k) => distances[i + DistanceRes * (j + DistanceRes * k)];
            float c00 = Mathf.Lerp(D(x, y, z), D(x + 1, y, z), tx), c10 = Mathf.Lerp(D(x, y + 1, z), D(x + 1, y + 1, z), tx);
            float c01 = Mathf.Lerp(D(x, y, z + 1), D(x + 1, y, z + 1), tx), c11 = Mathf.Lerp(D(x, y + 1, z + 1), D(x + 1, y + 1, z + 1), tx);
            float value = Mathf.Lerp(Mathf.Lerp(c00, c10, ty), Mathf.Lerp(c01, c11, ty), tz);
            return value + (p - clamped).magnitude;
        }

        // Bir kez: ızgaranın her noktasında en yakın üçgene uzaklık; işaret en yakın noktadaki (ara değerli) normalden.
        // Yüzeyden uzak noktalar (kova boş): modelin kutusunun içindeyse içeride sayılır.
        private void BuildDistances()
        {
            distances = new float[DistanceRes * DistanceRes * DistanceRes];
            float cell = 2f * DistanceRange / (DistanceRes - 1);
            for (int k = 0; k < DistanceRes; k++)
                for (int j = 0; j < DistanceRes; j++)
                    for (int i = 0; i < DistanceRes; i++)
                    {
                        Vector3 p = new Vector3(i, j, k) * cell - Vector3.one * DistanceRange;
                        int t = Nearest(p, out Vector3 bary, out float distanceSqr);
                        float value;
                        if (t < 0)
                        {
                            bool inside = p.x > meshMin.x && p.y > meshMin.y && p.z > meshMin.z && p.x < meshMax.x && p.y < meshMax.y && p.z < meshMax.z;
                            value = inside ? -pad : pad;
                        }
                        else
                        {
                            int i0 = triangles[t], i1 = triangles[t + 1], i2 = triangles[t + 2];
                            Vector3 q = positions[i0] * bary.x + positions[i1] * bary.y + positions[i2] * bary.z;
                            Vector3 normal = normals[i0] * bary.x + normals[i1] * bary.y + normals[i2] * bary.z;
                            float distance = Mathf.Sqrt(distanceSqr);
                            value = Vector3.Dot(p - q, normal) < 0f ? -distance : distance;
                        }
                        distances[i + DistanceRes * (j + DistanceRes * k)] = value;
                    }
        }

        // p: hücre uzayında; uv ve tangent (hücre uzayında, w işaret) en yakın yüzey noktasından
        public void Sample(Vector3 p, out Vector2 uv, out Vector4 tangent)
        {
            int t = Nearest(p, out Vector3 bary, out _);
            if (t < 0)
            {
                uv = Vector2.zero;
                tangent = new Vector4(1f, 0f, 0f, 1f);
                return;
            }
            int i0 = triangles[t], i1 = triangles[t + 1], i2 = triangles[t + 2];
            uv = uvs[i0] * bary.x + uvs[i1] * bary.y + uvs[i2] * bary.z;
            Vector3 dir = (Vector3)tangents[i0] * bary.x + (Vector3)tangents[i1] * bary.y + (Vector3)tangents[i2] * bary.z;
            dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : new Vector3(tangents[i0].x, tangents[i0].y, tangents[i0].z);
            tangent = new Vector4(dir.x, dir.y, dir.z, tangents[i0].w);
        }

        // Üçgenin p'ye en yakın noktası ve ağırlıkları (Ericson, Real-Time Collision Detection)
        private static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 bary)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) { bary = new Vector3(1f, 0f, 0f); return a; }
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) { bary = new Vector3(0f, 1f, 0f); return b; }
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                bary = new Vector3(1f - v, v, 0f);
                return a + ab * v;
            }
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) { bary = new Vector3(0f, 0f, 1f); return c; }
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                bary = new Vector3(1f - w, 0f, w);
                return a + ac * w;
            }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            {
                float w = (d4 - d3) / (d4 - d3 + (d5 - d6));
                bary = new Vector3(0f, 1f - w, w);
                return b + (c - b) * w;
            }
            float denom = 1f / (va + vb + vc);
            float vv = vb * denom, ww = vc * denom;
            bary = new Vector3(1f - vv - ww, vv, ww);
            return a + ab * vv + ac * ww;
        }
    }
}
