using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// PROTOTİP: birleştirilmiş odunları (MergedWood) ayrı parçalar yerine kaynaşmış tek bir kil şekli olarak çizer
// (Tiny Glade / Dreams yolu). Tek başına duran odunlara (Wood) dokunmaz. Her yapının görsel parçalarını kenarları
// yuvarlatılmış kutular olarak alır (renderer sınırları), aynı kesitte uç uca değenleri tek kutuya birleştirir (oluk
// kalmasın), mesafe alanını (SDF, yumuşak birleşim: iki hamuru bastırmak gibi) ızgarada örnekleyip tek mesh çıkarır
// (surface nets). Model yapı başına bir kez üretilir, sadece o yapı değişince yeniden (performans). Parçaların kendi
// renderer'ları gizlenir; collider'ları, tutma, birleştirme aynen çalışır. Elde tutulan yapı kendi modeliyle görünür.
// Doku yok (düz renk): önce şekli görmek için. Kapatınca (bileşeni disable) odunlar eski haline döner.
public class ClayWoodPrototype : MonoBehaviour
{
    [Header("Şekil")]
    [Tooltip("Izgara aralığı (birim): küçük = daha pürüzsüz ama daha yavaş üretim")]
    [SerializeField] private float voxelSize = 0.07f;
    [Tooltip("Yumuşak birleşim: iki odunun birleştiği yerdeki kaynaşmanın genişliği (birim)")]
    [SerializeField] private float blend = 0.12f;
    [Tooltip("Odun kutularının kenar yuvarlaklığı (birim)")]
    [SerializeField] private float cornerRadius = 0.1f;
    [Tooltip("Her parçanın sınırından içe çekme (birim): model sınırları gerçek odundan biraz büyükse")]
    [SerializeField] private float inset = 0f;

    [Header("Görünüm")]
    [Tooltip("Kaynaşmış modelin malzemesi; boşsa düz bir URP Lit (odunun kendi malzemesi kullanılmaz: WoodWobbleLit " +
             "vertex'leri kendisi kaydırıyor, üretilen modeli yanlış yere çiziyordu)")]
    [SerializeField] private Material material;
    [Tooltip("Hesaplamaya giren odun parçalarının kutularını Scene'de sarı tel kafes olarak çiz (hata ayıklama)")]
    [SerializeField] private bool showParts = true;
    [SerializeField] private Color color = new Color(0.78f, 0.58f, 0.38f);

    [Header("Güncelleme")]
    [Tooltip("Odunlarda değişiklik var mı diye bakma aralığı (saniye)")]
    [SerializeField] private float checkInterval = 0.3f;

    private struct Part
    {
        public Vector3 center;
        public Vector3 half;
    }

    // Her birleşik odun yapısının (MergedWood) kaynaşmış modeli bir kez üretilir, yapı değişince (imza) yeniden.
    // Tek başına duran odunlara (Wood) dokunulmaz: birleştirilmedikçe kendi modelleriyle kalırlar.
    private class Entry
    {
        public int signature;
        public GameObject mesh;
        public readonly List<Renderer> hidden = new List<Renderer>();
        public readonly List<Part> parts = new List<Part>();
    }

    private readonly Dictionary<MergedWood, Entry> entries = new Dictionary<MergedWood, Entry>();
    private readonly List<MergedWood> stale = new List<MergedWood>();
    private readonly HashSet<MergedWood> seen = new HashSet<MergedWood>();
    private Material fallback;
    private float nextCheck;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void OnDisable()
    {
        foreach (Entry entry in entries.Values) Release(entry);
        entries.Clear();
    }

    private void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + checkInterval;

        object held = InteractableController.Instance != null ? InteractableController.Instance.Held : null;
        seen.Clear();
        foreach (MergedWood merged in FindObjectsByType<MergedWood>(FindObjectsSortMode.None))
        {
            if (ReferenceEquals(merged, held)) continue; // elde tutulan kendi modeliyle görünür
            seen.Add(merged);
            List<Renderer> renderers = BodyRenderers(merged);
            int signature = Signature(renderers);
            if (entries.TryGetValue(merged, out Entry entry) && entry.signature == signature) continue;
            if (entry != null) Release(entry);
            else entries[merged] = entry = new Entry();
            entry.signature = signature;
            Build(entry, renderers);
        }

        // Silinen ya da elde tutulan yapılar: kendi modelleri geri gelir
        stale.Clear();
        foreach (KeyValuePair<MergedWood, Entry> pair in entries)
            if (pair.Key == null || !seen.Contains(pair.Key)) stale.Add(pair.Key);
        foreach (MergedWood key in stale)
        {
            Release(entries[key]);
            entries.Remove(key);
        }
    }

    // Yapının gövde parçaları: kapalı objeler ve küçük yardımcı modeller (birleştirme noktaları gibi) hariç
    private static List<Renderer> BodyRenderers(MergedWood merged)
    {
        var renderers = new List<Renderer>();
        foreach (Renderer r in merged.GetComponentsInChildren<Renderer>(false))
        {
            if (!(r is MeshRenderer)) continue;
            Vector3 size = BoundsOf(r).size;
            if (Mathf.Min(size.x, Mathf.Min(size.y, size.z)) < 0.15f) continue;
            renderers.Add(r);
        }
        return renderers;
    }

    // Parçaların yerleri ve boyları (yapı değişti mi)
    private static int Signature(List<Renderer> renderers)
    {
        unchecked
        {
            int hash = 17 + renderers.Count;
            foreach (Renderer r in renderers)
            {
                Bounds b = BoundsOf(r);
                hash = hash * 31 + Vector3Int.RoundToInt(b.center * 20f).GetHashCode();
                hash = hash * 31 + Vector3Int.RoundToInt(b.size * 20f).GetHashCode();
            }
            return hash;
        }
    }

    // Renderer gizliyken de sınırı doğru verir (enabled'dan bağımsız: mesh sınırı + dünya matrisi)
    private static Bounds BoundsOf(Renderer renderer)
    {
        var filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return renderer.bounds;
        Bounds local = filter.sharedMesh.bounds;
        Matrix4x4 m = renderer.transform.localToWorldMatrix;
        Bounds world = new Bounds(m.MultiplyPoint3x4(local.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = local.center + Vector3.Scale(local.extents,
                new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
            world.Encapsulate(m.MultiplyPoint3x4(corner));
        }
        return world;
    }

    private void Release(Entry entry)
    {
        if (entry.mesh != null) Destroy(entry.mesh);
        entry.mesh = null;
        foreach (Renderer r in entry.hidden)
            if (r != null) r.enabled = true;
        entry.hidden.Clear();
        entry.parts.Clear();
    }

    private void Build(Entry entry, List<Renderer> renderers)
    {
        if (material == null && fallback == null) fallback = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        Material source = material != null ? material : fallback;

        foreach (Renderer r in renderers)
        {
            Bounds b = BoundsOf(r);
            Vector3 half = Vector3.Max(b.extents - Vector3.one * inset, Vector3.one * 0.02f);
            entry.parts.Add(new Part { center = b.center, half = half });
        }
        MergeBoxes(entry.parts);

        Mesh mesh = BuildMesh(entry.parts);
        if (mesh == null) return;
        foreach (Renderer r in renderers)
        {
            r.enabled = false;
            entry.hidden.Add(r);
        }
        var go = new GameObject("Clay Wood");
        go.transform.SetParent(transform, false);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = source;
        var block = new MaterialPropertyBlock();
        block.SetColor(BaseColorId, color);
        block.SetColor(ColorId, color);
        renderer.SetPropertyBlock(block);
        entry.mesh = go;
    }

    // Aynı kesitte, uç uca değen kutular tek kutu olur (uzun kirişin 1'er birimlik parçaları, üst üste katlar): iki
    // yuvarlak kutu yan yana gelince yuvarlak kenarları birleşim yerinde oluk bırakıyordu
    private static void MergeBoxes(List<Part> parts)
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

                        float min = Mathf.Min(pa.center[axis] - pa.half[axis], pb.center[axis] - pb.half[axis]);
                        float max = Mathf.Max(pa.center[axis] + pa.half[axis], pb.center[axis] + pb.half[axis]);
                        Vector3 center = pa.center, half = pa.half;
                        center[axis] = (min + max) * 0.5f;
                        half[axis] = (max - min) * 0.5f;
                        parts[a] = new Part { center = center, half = half };
                        parts.RemoveAt(b);
                        changed = true;
                    }
        }
    }

    private void OnDrawGizmos()
    {
        if (!showParts) return;
        Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.9f);
        foreach (Entry entry in entries.Values)
            foreach (Part part in entry.parts) Gizmos.DrawWireCube(part.center, part.half * 2f);
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    // --- Mesafe alanı: yuvarlak kutuların yumuşak birleşimi (eksi = içeride) ---

    private float Sdf(List<Part> parts, Vector3 p)
    {
        float d = float.MaxValue;
        foreach (Part part in parts)
        {
            float r = Mathf.Min(cornerRadius, Mathf.Min(part.half.x, Mathf.Min(part.half.y, part.half.z)) * 0.95f);
            Vector3 q = Abs(p - part.center) - (part.half - Vector3.one * r);
            float box = Vector3.Max(q, Vector3.zero).magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f) - r;
            d = d == float.MaxValue ? box : SmoothMin(d, box, blend);
        }
        return d;
    }

    private static float SmoothMin(float a, float b, float k)
    {
        if (k <= 0f) return Mathf.Min(a, b);
        float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
        return Mathf.Lerp(b, a, h) - k * h * (1f - h);
    }

    // --- Surface nets: mesafe alanını ızgarada örnekle, işaret değişen her küpe bir vertex, kenarlara dörtgen ---

    private Mesh BuildMesh(List<Part> parts)
    {
        Bounds area = new Bounds(parts[0].center, parts[0].half * 2f);
        foreach (Part part in parts) area.Encapsulate(new Bounds(part.center, part.half * 2f));
        area.Expand(blend * 2f + voxelSize * 4f);

        float step = Mathf.Max(0.02f, voxelSize);
        int nx = Mathf.CeilToInt(area.size.x / step) + 1, ny = Mathf.CeilToInt(area.size.y / step) + 1, nz = Mathf.CeilToInt(area.size.z / step) + 1;
        if ((long)nx * ny * nz > 3_000_000)
        {
            Debug.LogWarning($"ClayWoodPrototype: grup çok büyük ({nx}×{ny}×{nz}); Voxel Size'ı büyüt.", this);
            return null;
        }
        Vector3 origin = area.min;
        int Index(int x, int y, int z) => x + nx * (y + ny * z);

        var values = new float[nx * ny * nz];
        for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                    values[Index(x, y, z)] = Sdf(parts, origin + new Vector3(x, y, z) * step);

        // Her küp (x..x+1) için vertex: işaret değişen kenarların kesişim noktalarının ortalaması
        var cellVertex = new int[nx * ny * nz];
        for (int i = 0; i < cellVertex.Length; i++) cellVertex[i] = -1;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        int[,] edges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
        var corner = new float[8];
        var cornerPos = new Vector3[8];
        for (int z = 0; z < nz - 1; z++)
            for (int y = 0; y < ny - 1; y++)
                for (int x = 0; x < nx - 1; x++)
                {
                    int inside = 0;
                    for (int c = 0; c < 8; c++)
                    {
                        int cx = x + (c & 1), cy = y + ((c >> 1) & 1), cz = z + ((c >> 2) & 1);
                        corner[c] = values[Index(cx, cy, cz)];
                        cornerPos[c] = origin + new Vector3(cx, cy, cz) * step;
                        if (corner[c] < 0f) inside++;
                    }
                    if (inside == 0 || inside == 8) continue;

                    Vector3 sum = Vector3.zero;
                    int count = 0;
                    for (int e = 0; e < 12; e++)
                    {
                        float a = corner[edges[e, 0]], b = corner[edges[e, 1]];
                        if ((a < 0f) == (b < 0f)) continue;
                        float t = a / (a - b);
                        sum += Vector3.Lerp(cornerPos[edges[e, 0]], cornerPos[edges[e, 1]], t);
                        count++;
                    }
                    Vector3 v = sum / count;
                    cellVertex[Index(x, y, z)] = vertices.Count;
                    vertices.Add(v);
                    normals.Add(Gradient(parts, v, step * 0.5f));
                }

        // İşaret değişen her ızgara kenarı için onu çevreleyen 4 küpün vertex'lerinden dörtgen
        var triangles = new List<int>();
        void Quad(int a, int b, int c, int d, bool flip)
        {
            if (a < 0 || b < 0 || c < 0 || d < 0) return;
            if (flip) { triangles.Add(a); triangles.Add(c); triangles.Add(b); triangles.Add(a); triangles.Add(d); triangles.Add(c); }
            else { triangles.Add(a); triangles.Add(b); triangles.Add(c); triangles.Add(a); triangles.Add(c); triangles.Add(d); }
        }
        for (int z = 1; z < nz - 1; z++)
            for (int y = 1; y < ny - 1; y++)
                for (int x = 0; x < nx - 1; x++)
                {
                    bool a = values[Index(x, y, z)] < 0f, b = values[Index(x + 1, y, z)] < 0f;
                    if (a == b) continue;
                    Quad(cellVertex[Index(x, y - 1, z - 1)], cellVertex[Index(x, y, z - 1)], cellVertex[Index(x, y, z)], cellVertex[Index(x, y - 1, z)], !a);
                }
        for (int z = 1; z < nz - 1; z++)
            for (int y = 0; y < ny - 1; y++)
                for (int x = 1; x < nx - 1; x++)
                {
                    bool a = values[Index(x, y, z)] < 0f, b = values[Index(x, y + 1, z)] < 0f;
                    if (a == b) continue;
                    Quad(cellVertex[Index(x - 1, y, z - 1)], cellVertex[Index(x - 1, y, z)], cellVertex[Index(x, y, z)], cellVertex[Index(x, y, z - 1)], !a);
                }
        for (int z = 0; z < nz - 1; z++)
            for (int y = 1; y < ny - 1; y++)
                for (int x = 1; x < nx - 1; x++)
                {
                    bool a = values[Index(x, y, z)] < 0f, b = values[Index(x, y, z + 1)] < 0f;
                    if (a == b) continue;
                    Quad(cellVertex[Index(x - 1, y - 1, z)], cellVertex[Index(x, y - 1, z)], cellVertex[Index(x, y, z)], cellVertex[Index(x - 1, y, z)], !a);
                }
        if (triangles.Count == 0) return null;

        // Yönü garanti et: üçgenin yüzü mesafe alanının dışa yönüyle (normal) aynı tarafa baksın
        for (int i = 0; i < triangles.Count; i += 3)
        {
            Vector3 p0 = vertices[triangles[i]], p1 = vertices[triangles[i + 1]], p2 = vertices[triangles[i + 2]];
            Vector3 face = Vector3.Cross(p1 - p0, p2 - p0);
            Vector3 outward = normals[triangles[i]] + normals[triangles[i + 1]] + normals[triangles[i + 2]];
            if (Vector3.Dot(face, outward) < 0f) (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
        }

        var mesh = new Mesh { name = "Clay Wood" };
        if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private Vector3 Gradient(List<Part> parts, Vector3 p, float h)
    {
        Vector3 g = new Vector3(
            Sdf(parts, p + Vector3.right * h) - Sdf(parts, p - Vector3.right * h),
            Sdf(parts, p + Vector3.up * h) - Sdf(parts, p - Vector3.up * h),
            Sdf(parts, p + Vector3.forward * h) - Sdf(parts, p - Vector3.forward * h));
        return g.sqrMagnitude > 1e-10f ? g.normalized : Vector3.up;
    }
}
