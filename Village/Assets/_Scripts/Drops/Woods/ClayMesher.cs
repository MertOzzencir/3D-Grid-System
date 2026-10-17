using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// Odun hücrelerinden tek mesh: her hücreye 1BR odunun gerçek mesh'i aynen konur (parçanın dönüşüyle; kendi UV'si,
// normal map'i, şekli), kod sadece değme yerlerine dolgu üretir. 2BR = üst üste iki 1BR + aralarındaki dolgu; bütün
// birleşimler (yan parça, üst üste katlar) aynı mantık.
//
// Dolgu: hücrelerdeki 1BR'lerin mesafe alanları (SDF, Reference) yumuşak birleşimle (polinom smooth min, genişlik blend)
// birleştirilir; yumuşak yüzeyin modellerin dışına taştığı yerler (iki modelin arasındaki oluk) dolgudur. Dolgu ızgarada
// örneklenip mesh çıkarılır (surface nets), sadece dolgu kalınlığı FillEpsilon'dan büyük olan dörtgenler tutulur
// (diğer yerlerde gerçek model görünür). Dolgunun UV'si dokunun boş, düz renkli bir noktası (fillUv): düz odun rengi,
// normal map yok.
//
// Hücre hücre (chunk): dolgu 1×1×1'lik hücre parçalarına bölünür (hücre sınırları -0.5 / +0.5'te, grid'le aynı). Her
// parçanın dolgusu sadece ona margin kadar yakın odun hücrelerine (ortası, dönüşü) bağlıdır; bunlardan bir parmak izi
// yapılır ve dolgu mesh'i önbellekte tutulur. Bir yapıya parça eklenince sadece parçanın çevresi yeniden hesaplanır:
// ekleme maliyeti yapının büyüklüğünden bağımsız. Uzaktaki hücreler yumuşak birleşimi değiştirmez, bu yüzden sınırın
// iki yanında değerler birebir aynı. Hepsi çağıranın yerel uzayında.
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

    // Tek odun hücresi: ortası, 1BR'nin dönüşü
    private struct Cell
    {
        public Vector3 center;
        public Quaternion rotation;
    }

    private const float FillEpsilon = 0.004f; // bundan ince dolgu çizilmez (orada gerçek model görünür)

    // Dolgu parçaları: parmak izi → mesh (hücrenin kendi uzayında, [0, 1]); null = o hücrede dolgu yok
    private static readonly Dictionary<string, Mesh> chunkCache = new Dictionary<string, Mesh>();
    private const int MaxCachedChunks = 6000;

    // pieces: parçalar (çağıranın uzayında); reference: 1BR; voxelSize: dolgu ızgarasının aralığı (1 / N'ye yuvarlanır);
    // blend: değme yerindeki yumuşak birleşimin genişliği; fillUv: dolgunun dokudaki yeri (boş, düz renkli)
    public static Mesh Build(List<Piece> pieces, Reference reference, float voxelSize, float blend, Vector2 fillUv)
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

        // Her hücreye 1BR'nin kendisi
        var combine = new List<CombineInstance>();
        foreach (Cell cell in cells)
            combine.Add(new CombineInstance
            {
                mesh = reference.Mesh,
                transform = Matrix4x4.TRS(cell.center, cell.rotation, Vector3.one) * reference.ToCell
            });

        // Değme yerlerine dolgu (tek hücrede değme yok)
        if (cells.Count > 1)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(1f / Mathf.Max(voxelSize, 0.01f)), 4, 64);
            float step = 1f / n;
            blend = Mathf.Clamp(blend, 0.001f, 0.4f);
            // Bir noktanın değeri sadece modeli (hücreden ~0.05 taşar) blend kadar yakına gelen odunlara bağlı
            float margin = 0.55f + blend + step * 3f;

            if (chunkCache.Count > MaxCachedChunks) chunkCache.Clear();

            Vector3 min = cells[0].center, max = cells[0].center;
            foreach (Cell cell in cells)
            {
                min = Vector3.Min(min, cell.center);
                max = Vector3.Max(max, cell.center);
            }
            Vector3Int from = Vector3Int.FloorToInt(min - Vector3.one * 0.5f);
            Vector3Int to = Vector3Int.FloorToInt(max + Vector3.one * 1.5f);

            var localCells = new List<Cell>();
            var key = new StringBuilder(256);
            for (int cz = from.z; cz <= to.z; cz++)
                for (int cy = from.y; cy <= to.y; cy++)
                    for (int cx = from.x; cx <= to.x; cx++)
                    {
                        Vector3 origin = new Vector3(cx, cy, cz) - Vector3.one * 0.5f; // hücrenin alt köşesi
                        Vector3 regionMin = origin - Vector3.one * margin, regionMax = origin + Vector3.one * (1f + margin);

                        // Bölgeye yakın odun hücreleri (hücrenin uzayında); ikiden azsa değme yok
                        localCells.Clear();
                        foreach (Cell cell in cells)
                        {
                            Vector3 c = cell.center;
                            if (c.x < regionMin.x || c.x > regionMax.x || c.y < regionMin.y || c.y > regionMax.y ||
                                c.z < regionMin.z || c.z > regionMax.z) continue;
                            localCells.Add(new Cell { center = c - origin, rotation = cell.rotation });
                        }
                        if (localCells.Count < 2) continue;

                        string chunkKey = Key(key, localCells, n, blend, fillUv, reference);
                        if (!chunkCache.TryGetValue(chunkKey, out Mesh mesh))
                        {
                            mesh = FillMesh(localCells, reference, n, step, blend, fillUv);
                            chunkCache[chunkKey] = mesh;
                        }
                        if (mesh != null) combine.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.Translate(origin) });
                    }
        }

        var result = new Mesh { name = "Clay", indexFormat = IndexFormat.UInt32 };
        result.CombineMeshes(combine.ToArray(), true, true);
        result.RecalculateBounds();
        return result;
    }

    // Parçanın parmak izi: ızgara, birleşim ayarları, referans ve odun hücreleri (sıralı, yuvarlanmış)
    private static string Key(StringBuilder sb, List<Cell> localCells, int n, float blend, Vector2 fillUv, Reference reference)
    {
        localCells.Sort((a, b) =>
        {
            int c = a.center.x.CompareTo(b.center.x);
            if (c != 0) return c;
            c = a.center.y.CompareTo(b.center.y);
            return c != 0 ? c : a.center.z.CompareTo(b.center.z);
        });
        sb.Clear();
        sb.Append(n).Append('|').Append(Mathf.RoundToInt(blend * 10000f)).Append('|').Append(Mathf.RoundToInt(fillUv.x * 10000f))
          .Append(',').Append(Mathf.RoundToInt(fillUv.y * 10000f)).Append('|').Append(reference.Id);
        foreach (Cell cell in localCells)
        {
            Vector3 c = cell.center;
            Quaternion r = cell.rotation;
            sb.Append('|').Append(Mathf.RoundToInt(c.x * 1000f)).Append(',').Append(Mathf.RoundToInt(c.y * 1000f)).Append(',').Append(Mathf.RoundToInt(c.z * 1000f))
              .Append(':').Append(Mathf.RoundToInt(r.x * 1000f)).Append(',').Append(Mathf.RoundToInt(r.y * 1000f)).Append(',')
              .Append(Mathf.RoundToInt(r.z * 1000f)).Append(',').Append(Mathf.RoundToInt(r.w * 1000f));
        }
        return sb.ToString();
    }

    // Bir hücrenin 1BR'sinin mesafesi
    private static float CellDistance(Cell cell, Reference reference, Vector3 p) =>
        reference.Distance(Quaternion.Inverse(cell.rotation) * (p - cell.center));

    // Yumuşak birleşim (polinom smooth min): iki değer blend'den uzaksa düz min, yakınsa aradaki oluk dolar
    private static float SmoothMin(float a, float b, float blend)
    {
        float h = Mathf.Max(blend - Mathf.Abs(a - b), 0f) / blend;
        return Mathf.Min(a, b) - h * h * blend * 0.25f;
    }

    // Yumuşak birleşimin (dolgulu şekil) ve düz birleşimin (modellerin kendisi) mesafesi; hücreler sıralı: her parçada
    // aynı sonuç
    private static float Sdf(List<Cell> cells, Reference reference, Vector3 p, float blend, out float hard)
    {
        float d = float.MaxValue;
        hard = float.MaxValue;
        foreach (Cell cell in cells)
        {
            float c = CellDistance(cell, reference, p);
            hard = Mathf.Min(hard, c);
            d = d == float.MaxValue ? c : SmoothMin(d, c, blend);
        }
        return d;
    }

    private static Vector3 Gradient(List<Cell> cells, Reference reference, Vector3 p, float h, float blend)
    {
        Vector3 g = new Vector3(
            Sdf(cells, reference, p + Vector3.right * h, blend, out _) - Sdf(cells, reference, p - Vector3.right * h, blend, out _),
            Sdf(cells, reference, p + Vector3.up * h, blend, out _) - Sdf(cells, reference, p - Vector3.up * h, blend, out _),
            Sdf(cells, reference, p + Vector3.forward * h, blend, out _) - Sdf(cells, reference, p - Vector3.forward * h, blend, out _));
        return g.sqrMagnitude > 1e-10f ? g.normalized : Vector3.up;
    }

    // Bir parçanın (uzayı [0, 1]) dolgusu: yumuşak birleşimin yüzeyi (surface nets), sadece dolgu kalınlığı (düz
    // birleşim - yumuşak birleşim) FillEpsilon'dan büyük olan dörtgenler. Noktalar -1..n+1, küp vertex'leri -1..n-1,
    // dörtgenler tabanı 0..n-1 olan ızgara kenarları için (her kenar tek parçanın). Izgara yarım adım kaydırılmış.
    private static Mesh FillMesh(List<Cell> cells, Reference reference, int n, float step, float blend, Vector2 fillUv)
    {
        int m = n + 3;                        // nokta sayısı (eksen başına), indeks i ↔ ızgara -1 + i
        int Index(int x, int y, int z) => x + m * (y + m * z);
        Vector3 Point(int x, int y, int z) => new Vector3(x - 0.5f, y - 0.5f, z - 0.5f) * step;

        var values = new float[m * m * m];
        bool anyFill = false;
        for (int z = 0; z < m; z++)
            for (int y = 0; y < m; y++)
                for (int x = 0; x < m; x++)
                {
                    float v = Sdf(cells, reference, Point(x, y, z), blend, out float hard);
                    values[Index(x, y, z)] = v;
                    if (v < 0f && hard >= 0f) anyFill = true; // yumuşak şeklin içinde, modellerin dışında
                }
        if (!anyFill) return null;

        int c = m - 1; // küp sayısı (eksen başına), küp indeksi i: noktalar i..i+1
        var cellVertex = new int[c * c * c];
        int CellIndex(int x, int y, int z) => x + c * (y + c * z);
        for (int i = 0; i < cellVertex.Length; i++) cellVertex[i] = -1;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var isFill = new List<bool>();
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
                    float smooth = Sdf(cells, reference, v, blend, out float hardAtVertex);
                    cellVertex[CellIndex(x, y, z)] = vertices.Count;
                    vertices.Add(v);
                    normals.Add(Gradient(cells, reference, v, step * 0.5f, blend));
                    isFill.Add(hardAtVertex - smooth > FillEpsilon);
                }

        var triangles = new List<int>();
        void Quad(int a, int b, int cc, int d)
        {
            if (a < 0 || b < 0 || cc < 0 || d < 0) return;
            if (!isFill[a] || !isFill[b] || !isFill[cc] || !isFill[d]) return; // dolgu değil: orada gerçek model görünür
            triangles.Add(a); triangles.Add(b); triangles.Add(cc);
            triangles.Add(a); triangles.Add(cc); triangles.Add(d);
        }
        // Taban noktası parçanın içinde (ızgara 0..n-1 ↔ nokta indeksi 1..n) olan kenarlar
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

        // Dolgu düz renk: hepsi dokunun boş yerinden; tangent normal'e dik herhangi bir yön (normal map orada düz)
        var uvs = new List<Vector2>(vertices.Count);
        var tangents = new List<Vector4>(vertices.Count);
        foreach (Vector3 normal in normals)
        {
            uvs.Add(fillUv);
            Vector3 t = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            tangents.Add(new Vector4(t.x, t.y, t.z, 1f));
        }

        var mesh = new Mesh { name = "Clay Fill" };
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

    // 1BR: hücrelere konan mesh ve dolgu için mesafe alanı. Mesh'in üçgenleri hücre uzayında (ortası 0, odun +Y
    // yönünde). Distance: modelin mesafe alanı (eksi = içeride), bir kez hesaplanıp ızgarada tutulur (DistanceRes³,
    // trilinear). Hız için üçgenler ızgara kovalarına dağıtılır (her kova kendisine yakın üçgenleri tutar).
    public sealed class Reference
    {
        private const int Grid = 16;
        private const int DistanceRes = 40;
        private const float DistanceRange = 0.8f; // mesafe ızgarası [-0.8, 0.8]³
        private const float MaxBlend = 0.4f;      // smooth min bu kadar uzaktaki değerlere bakar

        public int Id { get; }
        public Mesh Mesh { get; }
        public Matrix4x4 ToCell { get; }

        private readonly Vector3[] positions;
        private readonly Vector3[] normals;
        private readonly int[] triangles;
        private readonly List<int>[] buckets = new List<int>[Grid * Grid * Grid];
        private readonly Vector3 boundsMin, bucketSize, meshMin, meshMax;
        private readonly float pad;
        private float[] distances;

        // toCell: mesh uzayından hücre uzayına (1BR prefab'ının kökü = hücrenin ortası)
        public Reference(Mesh mesh, Matrix4x4 toCell, int id)
        {
            Id = id;
            Mesh = mesh;
            ToCell = toCell;
            Vector3[] source = mesh.vertices;
            Vector3[] sourceNormals = mesh.normals;
            triangles = mesh.triangles;
            positions = new Vector3[source.Length];
            normals = new Vector3[source.Length];
            Matrix4x4 normalMatrix = toCell.inverse.transpose;
            for (int i = 0; i < source.Length; i++)
            {
                positions[i] = toCell.MultiplyPoint3x4(source[i]);
                normals[i] = i < sourceNormals.Length ? normalMatrix.MultiplyVector(sourceNormals[i]).normalized : Vector3.up;
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
            pad = Mathf.Max(bucketSize.x * 1.5f, MaxBlend + 0.05f); // mesafe yüzeyden bu kadar uzağa kadar doğru
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
