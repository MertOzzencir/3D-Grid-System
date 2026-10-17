using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// Kutuları kaynaşmış tek bir kil şekline çeviren hesap (Tiny Glade / Dreams yolu). Bütün kutular tek bir dolu hacim
// sayılır (ana odun, yan parçalar, üst/alt katlar: hangisi olduğu fark etmez), sonra bu hacmin sadece dış kenarları
// yuvarlatılır: hacim "en büyük kutular"ına ayrılır (hacmin içinde kalıp hiçbir yöne büyüyemeyen bütün kutular,
// birbirine binerler), her biri cornerRadius kadar küçültülüp birleşimin mesafe alanından (SDF) cornerRadius çıkarılır.
// Değen iki parçanın ortak yüzü hacmin içinde kaldığı için oluk/çizgi olmaz; aynı düzlemdeki yüzeyler düz, iç köşeler
// keskin kalır. (Eskiden her parça ayrı küçültülüyordu: üst üste farklı uzunlukta katlar birleşim yerinde oluk
// bırakıyordu.) Mesafe alanı ızgarada örneklenip mesh çıkarılır (surface nets).
//
// Lif yönü: her vertex'e hangi yönde uzanan oduna ait olduğu yazılır (UV0 = (X ağırlığı, Z ağırlığı), Y = 1 - ikisi;
// "Village/Clay Wood Lit" shader'ı dokuyu bu yöne göre döşer). Ağırlık parçanın kendi hücrelerine (lif kutusu,
// dik oduna uzanan kısım hariç) uzaklıktan: birleşim yerinde grainBlend genişliğinde iki yön yumuşakça karışır.
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
        public int axis; // lif yönü (0 = X, 1 = Y, 2 = Z); şekil kutularında kullanılmaz
        public Vector3 Min => center - half;
        public Vector3 Max => center + half;
    }

    // Bir odun parçası: shape = şekle giren kutu (dik oduna uzanan kısım dahil), grain = parçanın kendi hücreleri
    // (lif yönü için), axis = uzandığı eksen (0 = X, 1 = Y, 2 = Z)
    public struct Piece
    {
        public Bounds shape;
        public Bounds grain;
        public int axis;
    }

    // Hücre şekilleri: parmak izi → mesh (hücrenin kendi uzayında, [0, 1]); null = o hücrede yüzey yok
    private static readonly Dictionary<string, Mesh> chunkCache = new Dictionary<string, Mesh>();
    private const int MaxCachedChunks = 6000;

    // pieces: parçalar (çağıranın uzayında); voxelSize: ızgara aralığı (1 / N'ye yuvarlanır); cornerRadius: dış kenar
    // yuvarlaklığı; grainBlend: birleşimde iki lif yönünün karıştığı genişlik
    public static Mesh Build(List<Piece> pieces, float voxelSize, float cornerRadius, float grainBlend, float inset = 0f)
    {
        var parts = new List<Part>();
        var grains = new List<Part>();
        foreach (Piece piece in pieces)
        {
            parts.Add(new Part { center = piece.shape.center, half = Vector3.Max(piece.shape.extents - Vector3.one * inset, Vector3.one * 0.02f) });
            grains.Add(new Part { center = piece.grain.center, half = piece.grain.extents, axis = piece.axis });
        }
        if (parts.Count == 0) return null;

        int n = Mathf.Clamp(Mathf.RoundToInt(1f / Mathf.Max(voxelSize, 0.01f)), 4, 64);
        float step = 1f / n;
        // Yuvarlaklık yapının en ince parçasıyla sınırlı (bütün hücrelerde aynı olmalı ki sınırlar uyuşsun)
        float radius = cornerRadius;
        foreach (Part part in parts)
            radius = Mathf.Min(radius, Mathf.Min(part.half.x, Mathf.Min(part.half.y, part.half.z)) * 0.95f);
        radius = Mathf.Max(0f, radius);
        grainBlend = Mathf.Max(grainBlend, 0.001f);
        // Küçült + şişir iki kez radius uzağa bakar: hücrenin şekli 2 × radius yakınındaki kutulara bağlı; lif ağırlığı
        // grainBlend yakınındaki lif kutularına
        float margin = Mathf.Max(radius * 2f, grainBlend) + step * 3f;

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
        var clippedGrains = new List<Part>();
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
                        if (!Clip(part, regionMin, regionMax, origin, out Part c)) continue;
                        if (Inside(part, origin - Vector3.one * (radius + step * 2f), origin + Vector3.one * (1f + radius + step * 2f)))
                            buried = true;
                        clipped.Add(c);
                    }
                    if (buried || clipped.Count == 0) continue;
                    clippedGrains.Clear();
                    foreach (Part grain in grains)
                        if (Clip(grain, regionMin, regionMax, origin, out Part c)) clippedGrains.Add(c);

                    string chunkKey = Key(key, clipped, clippedGrains, n, radius, grainBlend);
                    if (!chunkCache.TryGetValue(chunkKey, out Mesh mesh))
                    {
                        mesh = ChunkMesh(MaximalBoxes(clipped, radius), clippedGrains, n, step, radius, grainBlend);
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

    // Kutuyu bölgeye kırpar, hücrenin uzayına (alt köşe 0) çevirir; bölgeye değmiyorsa false
    private static bool Clip(Part part, Vector3 regionMin, Vector3 regionMax, Vector3 origin, out Part clipped)
    {
        Vector3 pMin = Vector3.Max(part.Min, regionMin), pMax = Vector3.Min(part.Max, regionMax);
        clipped = new Part { center = (pMin + pMax) * 0.5f - origin, half = (pMax - pMin) * 0.5f, axis = part.axis };
        return pMin.x < pMax.x && pMin.y < pMax.y && pMin.z < pMax.z;
    }

    private static bool Inside(Part part, Vector3 min, Vector3 max) =>
        part.Min.x <= min.x && part.Min.y <= min.y && part.Min.z <= min.z &&
        part.Max.x >= max.x && part.Max.y >= max.y && part.Max.z >= max.z;

    // Hücrenin parmak izi: ızgara, yarıçap, karışma ve kırpılmış kutular (sıralı, yuvarlanmış)
    private static string Key(StringBuilder sb, List<Part> clipped, List<Part> grains, int n, float radius, float grainBlend)
    {
        Sort(clipped);
        Sort(grains);
        sb.Clear();
        sb.Append(n).Append('|').Append(Mathf.RoundToInt(radius * 10000f)).Append('|').Append(Mathf.RoundToInt(grainBlend * 10000f));
        foreach (Part part in clipped) AppendBox(sb, part, 's');
        foreach (Part part in grains) AppendBox(sb, part, (char)('x' + part.axis));
        return sb.ToString();
    }

    private static void Sort(List<Part> list) => list.Sort((a, b) =>
    {
        int c = a.center.x.CompareTo(b.center.x);
        if (c != 0) return c;
        c = a.center.y.CompareTo(b.center.y);
        if (c != 0) return c;
        c = a.center.z.CompareTo(b.center.z);
        return c != 0 ? c : a.axis.CompareTo(b.axis);
    });

    private static void AppendBox(StringBuilder sb, Part part, char tag)
    {
        Vector3 lo = part.Min, hi = part.Max;
        sb.Append('|').Append(tag).Append(Mathf.RoundToInt(lo.x * 1000f)).Append(',').Append(Mathf.RoundToInt(lo.y * 1000f)).Append(',').Append(Mathf.RoundToInt(lo.z * 1000f))
          .Append(':').Append(Mathf.RoundToInt(hi.x * 1000f)).Append(',').Append(Mathf.RoundToInt(hi.y * 1000f)).Append(',').Append(Mathf.RoundToInt(hi.z * 1000f));
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

    // Noktanın lif yönü ağırlıkları (X, Z; Y = 1 - ikisi): her eksen için o yöndeki lif kutularına en yakın uzaklık;
    // en yakın eksen tam, ondan grainBlend kadar uzaktaki eksen sıfır, arası yumuşak geçiş. Hiç lif kutusu yoksa dikey.
    private static Vector2 GrainWeights(List<Part> grains, Vector3 p, float grainBlend)
    {
        float dx = float.MaxValue, dy = float.MaxValue, dz = float.MaxValue;
        foreach (Part grain in grains)
        {
            float d = Vector3.Max(Abs(p - grain.center) - grain.half, Vector3.zero).magnitude;
            if (grain.axis == 0) dx = Mathf.Min(dx, d);
            else if (grain.axis == 2) dz = Mathf.Min(dz, d);
            else dy = Mathf.Min(dy, d);
        }
        float nearest = Mathf.Min(dx, Mathf.Min(dy, dz));
        if (nearest == float.MaxValue) return Vector2.zero;
        float wx = Falloff(dx - nearest, grainBlend), wy = Falloff(dy - nearest, grainBlend), wz = Falloff(dz - nearest, grainBlend);
        float total = wx + wy + wz;
        return new Vector2(wx / total, wz / total);
    }

    private static float Falloff(float distance, float width) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance / width));

    // Bir hücrenin (uzayı [0, 1]) şekli, surface nets. Noktalar -1..n+1 (komşu küplerin vertex'leri için bir sıra
    // fazla), küp vertex'leri -1..n-1, dörtgenler tabanı 0..n-1 olan ızgara kenarları için (her kenar tek hücrenin).
    // Izgara yarım adım kaydırılmış (noktalar (i + 0.5) * adım): odun yüzleri hücre sınırlarında (kalınlık 1) ve sınır
    // bir ızgara noktasına denk gelince iki komşu hücre küçük bir hesap farkıyla yüzü birbirine bırakıp ikisi de
    // çizmiyordu (delikler). Kaydırınca yüz hep bir kenarın ortasında, sahibi kesin.
    private static Mesh ChunkMesh(List<Part> parts, List<Part> grains, int n, float step, float radius, float grainBlend)
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
        var grainUv = new List<Vector2>();
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
                    grainUv.Add(GrainWeights(grains, v, grainBlend));
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
        mesh.SetUVs(0, grainUv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static readonly int[,] Edges =
        { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
}
