using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// Kutuları kaynaşmış tek bir kil şekline çeviren hesap (Tiny Glade / Dreams yolu). Bütün kutular tek bir dolu hacim
// sayılır (ana odun, yan parçalar, üst/alt katlar: hangisi olduğu fark etmez), sonra bu hacmin sadece dış kenarları
// yuvarlatılır: hacim "en büyük kutular"ına ayrılır (hacmin içinde kalıp hiçbir yöne büyüyemeyen bütün kutular,
// birbirine binerler), her biri cornerRadius kadar küçültülüp birleşimin mesafe alanından (SDF) cornerRadius çıkarılır.
// Değen iki parçanın ortak yüzü hacmin içinde kaldığı için oluk/çizgi olmaz; aynı düzlemdeki yüzeyler düz, iç köşeler
// keskin kalır. Mesafe alanı ızgarada örneklenip mesh çıkarılır (surface nets).
//
// Görünüm 1BR odundan: her odun hücresine (parçanın yönünde döndürülmüş) hayali bir 1BR modeli oturtulur; kilin her
// vertex'i kendi hücresindeki 1BR'nin en yakın noktasının UV'sini ve tangent'ını alır (Reference). Böylece renk ve
// normal map 1BR'deki gibi, açıkta kalan uçlarda halka. Kaynaşma yerleri (hücrenin dolu bir komşuyla paylaştığı
// yüzlerin çevresi, fuseWidth) sade: vertex'e "detay" değeri yazılır (1 = 1BR'nin aynısı, 0 = düz renk, normal yok);
// tangent.w = işaret × (1 + detay) olarak taşınır, "Village/Clay Wood Lit" shader'ı çözer.
//
// Hücre hücre (chunk): şekil 1×1×1'lik hücre parçalarına bölünür (hücre sınırları -0.5 / +0.5'te, grid'le aynı). Her
// hücrenin şekli sadece o hücreye margin kadar yakın kutulara ve odun hücrelerine bağlıdır; bunlar hücrenin etki
// alanına kırpılıp bir parmak izi (anahtar) yapılır ve hücre şekli önbellekte bu anahtarla tutulur. Bir yapıya parça
// eklenince sadece parçanın dokunduğu hücrelerin parmak izi değişir; diğerleri önbellekten gelir: ekleme maliyeti
// yapının büyüklüğünden bağımsız. Hücreler ortak bir ızgarada (adım = 1 / N) ve sınırın iki yanında aynı kutularla
// hesaplandığı için komşu hücrelerin sınırdaki vertex'leri birebir aynı: çatlak yok.
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

    // Bir odun parçası: shape = şekle giren kutu (dik oduna uzanan kısım dahil); start = ilk hücrenin ortası,
    // rotation = parçanın dönüşü (1BR modeli bu dönüşle hücrelere oturur, uzandığı yön rotation * yukarı),
    // length = hücre sayısı
    public struct Piece
    {
        public Bounds shape;
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

    // Hücre şekilleri: parmak izi → mesh (hücrenin kendi uzayında, [0, 1]); null = o hücrede yüzey yok
    private static readonly Dictionary<string, Mesh> chunkCache = new Dictionary<string, Mesh>();
    private const int MaxCachedChunks = 6000;

    // pieces: parçalar (çağıranın uzayında); reference: görünümün alındığı 1BR (null ise UV yok); voxelSize: ızgara
    // aralığı (1 / N'ye yuvarlanır); cornerRadius: dış kenar yuvarlaklığı; fuseWidth: kaynaşma yerinin sade kalan genişliği
    public static Mesh Build(List<Piece> pieces, Reference reference, float voxelSize, float cornerRadius, float fuseWidth, float inset = 0f)
    {
        var parts = new List<Part>();
        var cells = new List<Cell>();
        foreach (Piece piece in pieces)
        {
            parts.Add(new Part { center = piece.shape.center, half = Vector3.Max(piece.shape.extents - Vector3.one * inset, Vector3.one * 0.02f) });
            Vector3 axis = piece.rotation * Vector3.up;
            for (int i = 0; i < piece.length; i++)
                cells.Add(new Cell { center = piece.start + axis * i, rotation = piece.rotation });
        }
        if (parts.Count == 0) return null;

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
        // Yuvarlaklık yapının en ince parçasıyla sınırlı (bütün hücrelerde aynı olmalı ki sınırlar uyuşsun)
        float radius = cornerRadius;
        foreach (Part part in parts)
            radius = Mathf.Min(radius, Mathf.Min(part.half.x, Mathf.Min(part.half.y, part.half.z)) * 0.95f);
        radius = Mathf.Max(0f, radius);
        fuseWidth = Mathf.Max(fuseWidth, 0.001f);
        // Küçült + şişir iki kez radius uzağa bakar: hücrenin şekli 2 × radius yakınındaki kutulara bağlı
        float margin = radius * 2f + step * 3f;

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
        var localCells = new List<Cell>();
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

                    // Bölgeye değen odun hücreleri (hücrenin uzayında)
                    localCells.Clear();
                    foreach (Cell cell in cells)
                    {
                        Vector3 c = cell.center;
                        if (c.x + 0.5f < regionMin.x || c.x - 0.5f > regionMax.x || c.y + 0.5f < regionMin.y || c.y - 0.5f > regionMax.y ||
                            c.z + 0.5f < regionMin.z || c.z - 0.5f > regionMax.z) continue;
                        localCells.Add(new Cell { center = c - origin, rotation = cell.rotation, neighbors = cell.neighbors });
                    }

                    string chunkKey = Key(key, clipped, localCells, n, radius, fuseWidth, reference);
                    if (!chunkCache.TryGetValue(chunkKey, out Mesh mesh))
                    {
                        mesh = ChunkMesh(MaximalBoxes(clipped, radius), localCells, reference, n, step, radius, fuseWidth);
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

    private static bool Inside(Part part, Vector3 min, Vector3 max) =>
        part.Min.x <= min.x && part.Min.y <= min.y && part.Min.z <= min.z &&
        part.Max.x >= max.x && part.Max.y >= max.y && part.Max.z >= max.z;

    // Hücrenin parmak izi: ızgara, yarıçap, kaynaşma, referans, kırpılmış kutular ve odun hücreleri (sıralı, yuvarlanmış)
    private static string Key(StringBuilder sb, List<Part> clipped, List<Cell> localCells, int n, float radius, float fuseWidth, Reference reference)
    {
        clipped.Sort((a, b) =>
        {
            int c = a.center.x.CompareTo(b.center.x);
            if (c != 0) return c;
            c = a.center.y.CompareTo(b.center.y);
            return c != 0 ? c : a.center.z.CompareTo(b.center.z);
        });
        localCells.Sort((a, b) =>
        {
            int c = a.center.x.CompareTo(b.center.x);
            if (c != 0) return c;
            c = a.center.y.CompareTo(b.center.y);
            return c != 0 ? c : a.center.z.CompareTo(b.center.z);
        });
        sb.Clear();
        sb.Append(n).Append('|').Append(Mathf.RoundToInt(radius * 10000f)).Append('|').Append(Mathf.RoundToInt(fuseWidth * 10000f))
          .Append('|').Append(reference != null ? reference.Id : 0);
        foreach (Part part in clipped)
        {
            Vector3 lo = part.Min, hi = part.Max;
            sb.Append('|').Append(Mathf.RoundToInt(lo.x * 1000f)).Append(',').Append(Mathf.RoundToInt(lo.y * 1000f)).Append(',').Append(Mathf.RoundToInt(lo.z * 1000f))
              .Append(':').Append(Mathf.RoundToInt(hi.x * 1000f)).Append(',').Append(Mathf.RoundToInt(hi.y * 1000f)).Append(',').Append(Mathf.RoundToInt(hi.z * 1000f));
        }
        foreach (Cell cell in localCells)
        {
            Vector3 c = cell.center;
            Quaternion r = cell.rotation;
            sb.Append("|c").Append(Mathf.RoundToInt(c.x * 1000f)).Append(',').Append(Mathf.RoundToInt(c.y * 1000f)).Append(',').Append(Mathf.RoundToInt(c.z * 1000f))
              .Append(':').Append(Mathf.RoundToInt(r.x * 1000f)).Append(',').Append(Mathf.RoundToInt(r.y * 1000f)).Append(',')
              .Append(Mathf.RoundToInt(r.z * 1000f)).Append(',').Append(Mathf.RoundToInt(r.w * 1000f)).Append(':').Append(cell.neighbors);
        }
        return sb.ToString();
    }

    // Kutuların birleşimini (tek hacim) "en büyük kutular"ına ayırır: hacmin içinde kalan ve hiçbir yöne büyüyemeyen
    // bütün kutular. Kutuların bütün yüz koordinatlarıyla sıkıştırılmış bir ızgarada (her eksende farklı yüz
    // konumları) dolu hücreler işaretlenir, sonra x / y aralıkları için z boyunca dolu sütunlar taranır. Küçültülünce
    // boş kalan (radius'tan ince) kutular atlanır.
    private static List<Part> MaximalBoxes(List<Part> parts, float radius)
    {
        var coords = new float[3][];
        var sorted = new List<float>();
        for (int a = 0; a < 3; a++)
        {
            sorted.Clear();
            foreach (Part part in parts)
            {
                sorted.Add(part.Min[a]);
                sorted.Add(part.Max[a]);
            }
            sorted.Sort();
            var unique = new List<float>();
            foreach (float v in sorted)
                if (unique.Count == 0 || v - unique[unique.Count - 1] > 1e-4f) unique.Add(v);
            coords[a] = unique.ToArray();
        }
        int nx = coords[0].Length - 1, ny = coords[1].Length - 1, nz = coords[2].Length - 1;
        var result = new List<Part>();
        if (nx <= 0 || ny <= 0 || nz <= 0) return result;

        // Dolu hücreler ve toplamlar (prefix sum: bir aralığın tamamen dolu olup olmadığı tek adımda)
        int sx = nx + 1, sy = ny + 1;
        var sum = new int[sx * sy * (nz + 1)];
        int S(int x, int y, int z) => sum[x + sx * (y + sy * z)];
        for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    Vector3 c = new Vector3((coords[0][x] + coords[0][x + 1]) * 0.5f, (coords[1][y] + coords[1][y + 1]) * 0.5f,
                                            (coords[2][z] + coords[2][z + 1]) * 0.5f);
                    int filled = 0;
                    foreach (Part part in parts)
                        if (c.x > part.Min.x && c.x < part.Max.x && c.y > part.Min.y && c.y < part.Max.y && c.z > part.Min.z && c.z < part.Max.z)
                        {
                            filled = 1;
                            break;
                        }
                    sum[(x + 1) + sx * ((y + 1) + sy * (z + 1))] = filled
                        + S(x, y + 1, z + 1) + S(x + 1, y, z + 1) + S(x + 1, y + 1, z)
                        - S(x, y, z + 1) - S(x, y + 1, z) - S(x + 1, y, z) + S(x, y, z);
                }
        // [x0, x1] × [y0, y1] × [z0, z1] (hücre indeksleri, dahil) tamamen dolu mu; ızgara dışı = boş
        bool Full(int x0, int x1, int y0, int y1, int z0, int z1)
        {
            if (x0 < 0 || y0 < 0 || z0 < 0 || x1 >= nx || y1 >= ny || z1 >= nz) return false;
            int count = S(x1 + 1, y1 + 1, z1 + 1) - S(x0, y1 + 1, z1 + 1) - S(x1 + 1, y0, z1 + 1) - S(x1 + 1, y1 + 1, z0)
                      + S(x0, y0, z1 + 1) + S(x0, y1 + 1, z0) + S(x1 + 1, y0, z0) - S(x0, y0, z0);
            return count == (x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1);
        }

        for (int x0 = 0; x0 < nx; x0++)
            for (int x1 = x0; x1 < nx; x1++)
            {
                bool anyX = false;
                for (int y0 = 0; y0 < ny; y0++)
                    for (int y1 = y0; y1 < ny; y1++)
                    {
                        bool any = false;
                        for (int z = 0; z < nz; z++)
                        {
                            if (!Full(x0, x1, y0, y1, z, z)) continue;
                            int z0 = z;
                            while (z + 1 < nz && Full(x0, x1, y0, y1, z + 1, z + 1)) z++;
                            any = true;
                            // x ve y'de de büyüyemiyorsa en büyük kutu
                            if (Full(x0 - 1, x0 - 1, y0, y1, z0, z) || Full(x1 + 1, x1 + 1, y0, y1, z0, z) ||
                                Full(x0, x1, y0 - 1, y0 - 1, z0, z) || Full(x0, x1, y1 + 1, y1 + 1, z0, z)) continue;
                            Vector3 lo = new Vector3(coords[0][x0], coords[1][y0], coords[2][z0]);
                            Vector3 hi = new Vector3(coords[0][x1 + 1], coords[1][y1 + 1], coords[2][z + 1]);
                            Vector3 half = (hi - lo) * 0.5f;
                            if (half.x < radius - 1e-5f || half.y < radius - 1e-5f || half.z < radius - 1e-5f) continue;
                            result.Add(new Part { center = (lo + hi) * 0.5f, half = Vector3.Max(half, Vector3.one * radius) });
                        }
                        if (!any) break; // y1 büyüdükçe dolu sütun çıkmaz
                        anyX = true;
                    }
                if (!anyX) break; // x1 büyüdükçe de
            }
        return result;
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    // Mesafe alanı (eksi = içeride): radius kadar küçültülmüş en büyük kutuların birleşimi, sonra hepsi birlikte radius
    // kadar şişirilir (dış kenarlar yuvarlak, aynı düzlemdeki yüzeyler düz, değen parçalar arasında oluk yok)
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

    // Vertex'in görünümü: sahibi olan odun hücresi (küpüne en yakın), o hücredeki 1BR'nin en yakın noktasının UV'si ve
    // tangent'ı; detay = dolu komşuyla paylaşılan en yakın yüze uzaklıktan (fuseWidth'te tam)
    private static void Appearance(List<Cell> cells, Reference reference, Vector3 v, float fuseWidth, out Vector2 uv, out Vector4 tangent)
    {
        uv = Vector2.zero;
        tangent = new Vector4(1f, 0f, 0f, 1f);
        int owner = -1;
        float best = float.MaxValue;
        for (int i = 0; i < cells.Count; i++)
        {
            float d = Vector3.Max(Abs(v - cells[i].center) - Vector3.one * 0.5f, Vector3.zero).sqrMagnitude;
            if (d < best)
            {
                best = d;
                owner = i;
            }
        }
        if (owner < 0) return;

        Cell cell = cells[owner];
        Vector3 offset = v - cell.center;
        float fuse = float.MaxValue;
        for (int d = 0; d < 6; d++)
            if ((cell.neighbors & (1 << d)) != 0)
                fuse = Mathf.Min(fuse, Mathf.Max(0f, 0.5f - Vector3.Dot(offset, Directions[d])));
        float detail = fuse == float.MaxValue ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fuse / fuseWidth));

        float sign = 1f;
        if (reference != null)
        {
            reference.Sample(Quaternion.Inverse(cell.rotation) * offset, out uv, out Vector4 local);
            Vector3 t = cell.rotation * new Vector3(local.x, local.y, local.z);
            tangent = new Vector4(t.x, t.y, t.z, 1f);
            sign = local.w < 0f ? -1f : 1f;
        }
        tangent.w = sign * (1f + detail);
    }

    // Bir hücrenin (uzayı [0, 1]) şekli, surface nets. Noktalar -1..n+1 (komşu küplerin vertex'leri için bir sıra
    // fazla), küp vertex'leri -1..n-1, dörtgenler tabanı 0..n-1 olan ızgara kenarları için (her kenar tek hücrenin).
    // Izgara yarım adım kaydırılmış (noktalar (i + 0.5) * adım): odun yüzleri hücre sınırlarında (kalınlık 1) ve sınır
    // bir ızgara noktasına denk gelince iki komşu hücre küçük bir hesap farkıyla yüzü birbirine bırakıp ikisi de
    // çizmiyordu (delikler). Kaydırınca yüz hep bir kenarın ortasında, sahibi kesin.
    private static Mesh ChunkMesh(List<Part> parts, List<Cell> cells, Reference reference, int n, float step, float radius, float fuseWidth)
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
                    normals.Add(Gradient(parts, v, step * 0.5f, radius));
                    Appearance(cells, reference, v, fuseWidth, out Vector2 uv, out Vector4 tangent);
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

    // Görünümün alındığı model (1BR): mesh'in üçgenleri hücre uzayında (ortası 0, odun +Y yönünde). Bir noktanın en
    // yakın yüzey noktasının UV'si ve tangent'ı. Hız için üçgenler ızgara kovalarına dağıtılır (her kova kendisine
    // yakın üçgenleri tutar; kil yüzeyi modele yakın olduğu için en yakın üçgen hep kovada).
    public sealed class Reference
    {
        private const int Grid = 8;

        public int Id { get; }

        private readonly Vector3[] positions;
        private readonly Vector2[] uvs;
        private readonly Vector4[] tangents;
        private readonly int[] triangles;
        private readonly List<int>[] buckets = new List<int>[Grid * Grid * Grid];
        private readonly Vector3 boundsMin, bucketSize;

        // toCell: mesh uzayından hücre uzayına (1BR prefab'ının kökü = hücrenin ortası)
        public Reference(Mesh mesh, Matrix4x4 toCell, int id)
        {
            Id = id;
            Vector3[] source = mesh.vertices;
            Vector4[] sourceTangents = mesh.tangents;
            Vector2[] sourceUvs = mesh.uv;
            triangles = mesh.triangles;
            positions = new Vector3[source.Length];
            uvs = new Vector2[source.Length];
            tangents = new Vector4[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                positions[i] = toCell.MultiplyPoint3x4(source[i]);
                uvs[i] = i < sourceUvs.Length ? sourceUvs[i] : Vector2.zero;
                Vector4 t = i < sourceTangents.Length ? sourceTangents[i] : new Vector4(1f, 0f, 0f, 1f);
                Vector3 dir = toCell.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                tangents[i] = new Vector4(dir.x, dir.y, dir.z, t.w < 0f ? -1f : 1f);
            }

            Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
            foreach (Vector3 p in positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            boundsMin = min - Vector3.one * 0.25f;
            bucketSize = (max + Vector3.one * 0.25f - boundsMin) / Grid;
            float pad = Mathf.Max(bucketSize.x, Mathf.Max(bucketSize.y, bucketSize.z)) * 1.5f;
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

        // p: hücre uzayında; uv ve tangent (hücre uzayında, w işaret) en yakın yüzey noktasından
        public void Sample(Vector3 p, out Vector2 uv, out Vector4 tangent)
        {
            Vector3Int b = BucketOf(p);
            List<int> list = buckets[b.x + Grid * (b.y + Grid * b.z)];
            int bestTriangle = -1;
            Vector3 bestBary = Vector3.zero;
            float best = float.MaxValue;
            int count = list.Count > 0 ? list.Count : triangles.Length / 3;
            for (int k = 0; k < count; k++)
            {
                int t = list.Count > 0 ? list[k] : k * 3;
                Vector3 q = Closest(p, positions[triangles[t]], positions[triangles[t + 1]], positions[triangles[t + 2]], out Vector3 bary);
                float d = (q - p).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    bestTriangle = t;
                    bestBary = bary;
                }
            }
            if (bestTriangle < 0)
            {
                uv = Vector2.zero;
                tangent = new Vector4(1f, 0f, 0f, 1f);
                return;
            }
            int i0 = triangles[bestTriangle], i1 = triangles[bestTriangle + 1], i2 = triangles[bestTriangle + 2];
            uv = uvs[i0] * bestBary.x + uvs[i1] * bestBary.y + uvs[i2] * bestBary.z;
            Vector3 dir = (Vector3)tangents[i0] * bestBary.x + (Vector3)tangents[i1] * bestBary.y + (Vector3)tangents[i2] * bestBary.z;
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
