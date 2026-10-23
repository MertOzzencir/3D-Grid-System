using System;
using System.Collections.Generic;
using UnityEngine;

// Elde tutulan odunun (Wood, MergedWood) çevresinde 3 boyutlu grid göstergesi: odunun şu an üstünde durduğu hücrelerden
// her yöne (yanlar, köşe çaprazları, yukarı, aşağı) `range` hücreye kadar, odunun konabileceği her hücrenin sadece yatay
// kenarları çizgi (`edgeAlpha`, `lineWidth` kalınlığında yatay şerit; içi boş, dikey kenar yok, dış çerçeve yok: çizgiler açık
// uçlu söner). Saydamlık çizgi boyunca sürekli (vertex rengi): odunun hücrelerinin kutusunda tam, kutudan range uzakta
// tamamen şeffaf (`fadePower` eğrisi). Sadece odunun konabileceği hücreler çizilir: odun
// o hücreye (origin olarak) konunca bütün şekli sığar (GridManager.CanPlaceablePlaceOn: hücreler boş, CanOccupy, ince
// parça yok), hiçbir hücresi base'in içinde değil ve (requireSupport) en alt hücrelerinden birinin altında base ya da
// obje var (sürükleyerek havaya konamaz). Komşu hücrelerin ortak kenarı bir kez
// (en belirgin saydamlıkla), yoksa üst üste binip koyulaşırdı. Tek mesh, "Village/Grid Fade" (UV.y 0: yukarı doğru sönme yok). Odun hücre değiştirince mesh yeniden kurulur.
// Hız (büyük MergedWood'da ms patlıyordu): Rebuild başında bölgedeki her hücre grid'de bir kez okunur (Region), konabilirlik
// ve uzaklık sadece bu dizilerden hesaplanır; eskiden her aday hücre için odunun bütün hücreleri grid'de tek tek aranıyordu
// (aday × odun hücresi × birkaç sözlük araması). Listeler / sözlükler yeniden kullanılır (GC yok).
// Birleştirme: taşınan odun başka bir oduna yapışınca (WoodMergeTarget.Active) çevre yerine sadece bağlantı yerlerinin
// hücreleri gösterilir (eski ok göstergelerinin yerine), dışarı doğru açık: parçanın ana oduna değdiği yüzde kapalı bir
// çerçeve (dört kenar; yan parçada dikey kenarlar dahil), çerçevenin köşelerinden parçanın uzandığı yönde ucuna kadar
// çizgiler (ucunda tamamen şeffaf, `fadePower`); dışarı bakan uç ve parçanın içindeki hücre sınırları çizilmez. Mouse'un
// seçtiği yer `selectedAlpha`, diğerleri `optionAlpha`. Dikey çizgiler kameraya dönük şerit, yataylar yatay şerit.
// Sökme: elde balta bir MergedWood'a yapışınca (WoodDetachTarget.Active) aynı şekilde ek yerleri gösterilir: çerçeve ek
// yerinde, çizgiler ayrılacak kısım boyunca.
// Ayarları WoodMerger'da ("Held Grid"), WoodMerger.Update her kare Tick'i çağırır.
[Serializable]
public class HeldGridIndicator
{
    [Tooltip("Açıksa elde tutulan odunun çevresindeki hücreler gösterilir")]
    [SerializeField] private bool enabled = true;
    [Tooltip("Odunun hücrelerinden her yöne kaç hücre öteye kadar")]
    [SerializeField, Range(1, 4)] private int range = 2;
    [Tooltip("Açıksa sadece altı dolu (base ya da obje) yerler gösterilir: odun sürüklenerek havaya konamıyor")]
    [SerializeField] private bool requireSupport = true;
    [Tooltip("Renk (A kullanılmaz; saydamlık aşağıdaki iki ayardan)")]
    [SerializeField] private Color color = Color.white;
    [Tooltip("Küp kenarlarının saydamlığı odunun hücresinde")]
    [SerializeField, Range(0f, 1f)] private float edgeAlpha = 1f;
    [Tooltip("Çizgilerin kalınlığı (birim; yatay şerit)")]
    [SerializeField] private float lineWidth = 0.05f;
    [Tooltip("Sönme eğrisi: çizgi odunun hücrelerinden uzaklaştıkça söner, ucunda tamamen şeffaf. 1 doğrusal, büyük = " +
             "merkezden çıkınca daha çabuk söner.")]
    [SerializeField, Range(0.3f, 5f)] private float fadePower = 1.5f;
    [Tooltip("Birleştirmede seçili bağlantı yerinin çizgilerinin saydamlığı")]
    [SerializeField, Range(0f, 1f)] private float selectedAlpha = 1f;
    [Tooltip("Birleştirmede diğer bağlantı yerlerinin çizgilerinin saydamlığı")]
    [SerializeField, Range(0f, 1f)] private float optionAlpha = 0.4f;
    [Tooltip("Belirme / sönme hızı (saniyede)")]
    [SerializeField] private float fadeSpeed = 6f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private GameObject root;
    private Mesh mesh;
    private Material material;
    private MeshRenderer renderer;
    private float visibility;
    private GridPlaceable shownFor;
    private Vector3Int shownOrigin;
    private GridMaskRotator.Rotation shownRotation;
    private int? shownMergeKey; // birleştirme gösteriliyorsa hücrelerin / seçimin anahtarı

    // Rebuild'in tekrar kullanılan tamponları
    private readonly Dictionary<Vector3Int, float> cellBuffer = new Dictionary<Vector3Int, float>();
    private readonly Dictionary<(Vector3Int, Vector3Int), float> edgeBuffer = new Dictionary<(Vector3Int, Vector3Int), float>();
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color> colors = new List<Color>();
    private readonly List<int> triangles = new List<int>();
    private readonly List<Vector2> uvs = new List<Vector2>();
    private readonly List<int> bottomOffsets = new List<int>(); // odunun en alt katındaki hücrelerinin indisleri
    // Bölge: aday hücreler + odunun şekli + bir alt kat (destek). free: odunun bir hücresi oraya girebilir (grid içinde,
    // placeable yok, ince parça yok, base yok, CanOccupy); support: üstündeki hücreyi taşır (base, ince parça ya da başka obje)
    private bool[] regionFree = new bool[0];
    private bool[] regionSupport = new bool[0];
    private int[] candidateDistance = new int[0];

    // Her kare: elde odun varsa onun çevresi gösterilir, yoksa söner
    public void Tick(float deltaTime)
    {
        InteractableController controller = InteractableController.Instance;
        GridPlaceable held = enabled && controller != null ? controller.Held as GridPlaceable : null;
        // Birleştirme (elde odun) ya da sökme (elde balta): sadece bağlantı / ek yerleri
        IWoodSlotView slots = held is Wood ? WoodMergeTarget.Active : held is Axe ? WoodDetachTarget.Active : (IWoodSlotView)null;
        if (slots != null && (!slots.IsAlive || slots.CandidateCells.Count == 0)) slots = null;
        if (!(held is Wood) && !(held is MergedWood) && slots == null) held = null;

        if (held != null && EnsureRoot())
        {
            if (slots != null)
            {
                int key = slots.CandidatesKey;
                if (shownMergeKey != key)
                {
                    RebuildMerge(slots);
                    shownMergeKey = key;
                    shownFor = null;
                }
            }
            else
            {
                Vector3Int origin = Vector3Int.RoundToInt(held.transform.position);
                if (shownMergeKey != null || held != shownFor || origin != shownOrigin || held.Rotation != shownRotation)
                {
                    Rebuild(held, origin);
                    shownMergeKey = null;
                    shownFor = held;
                    shownOrigin = origin;
                    shownRotation = held.Rotation;
                }
            }
        }
        if (material == null) return;

        float target = held != null ? 1f : 0f;
        if (!Mathf.Approximately(visibility, target))
        {
            visibility = Mathf.MoveTowards(visibility, target, fadeSpeed * deltaTime);
            material.SetColor(ColorId, new Color(color.r, color.g, color.b, visibility));
        }
        renderer.enabled = visibility > 0.001f;
        if (held == null && visibility <= 0.001f)
        {
            shownFor = null; // sonraki tutuşta yeniden kurulsun
            shownMergeKey = null;
        }
    }

    private bool EnsureRoot()
    {
        if (root != null) return true;
        Shader shader = Shader.Find("Village/Grid Fade");
        if (shader == null)
        {
            Debug.LogWarning("HeldGridIndicator: 'Village/Grid Fade' shader'ı bulunamadı.");
            enabled = false;
            return false;
        }
        root = new GameObject("Held Grid");
        mesh = new Mesh { name = "Held Grid" };
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        material = new Material(shader) { name = "Held Grid (generated)" };
        material.SetColor(ColorId, new Color(color.r, color.g, color.b, 0f));
        renderer = root.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return true;
    }

    // Odun origin'deyken kapladığı hücrelerin çevresi (Chebyshev uzaklığı range'e kadar)
    private void Rebuild(GridPlaceable held, Vector3Int origin)
    {
        GridManager grid = GridManager.Instance;
        IReadOnlyList<Vector3Int> offsets = held.GetFootprint().FilledCells();
        if (grid == null || offsets.Count == 0) return;
        Vector3Int offsetMin = offsets[0], offsetMax = offsets[0];
        foreach (Vector3Int offset in offsets)
        {
            offsetMin = Vector3Int.Min(offsetMin, offset);
            offsetMax = Vector3Int.Max(offsetMax, offset);
        }
        bottomOffsets.Clear();
        for (int i = 0; i < offsets.Count; i++)
            if (offsets[i].y == offsetMin.y) bottomOffsets.Add(i);
        Vector3Int min = origin + offsetMin, max = origin + offsetMax; // odunun şu anki hücrelerinin kutusu

        // Aday hücreler (odunun konacağı origin): kutudan her yöne range
        Vector3Int candMin = min - Vector3Int.one * range;
        Vector3Int candSize = max + Vector3Int.one * range - candMin + Vector3Int.one;
        // Bölge: adaylara odunun şekli eklenince kaplanan her hücre + destek için bir alt kat
        Vector3Int regionMin = candMin + offsetMin + Vector3Int.down;
        Vector3Int regionSize = candMin + candSize - Vector3Int.one + offsetMax - regionMin + Vector3Int.one;
        int regionCount = regionSize.x * regionSize.y * regionSize.z;
        if (regionFree.Length < regionCount)
        {
            regionFree = new bool[regionCount];
            regionSupport = new bool[regionCount];
        }
        for (int x = 0; x < regionSize.x; x++)
            for (int y = 0; y < regionSize.y; y++)
                for (int z = 0; z < regionSize.z; z++)
                {
                    var cell = new Vector3Int(regionMin.x + x, regionMin.y + y, regionMin.z + z);
                    int i = (x * regionSize.y + y) * regionSize.z + z;
                    if (!grid.TryGetCell(cell, out GridData data))
                    {
                        regionFree[i] = false;
                        regionSupport[i] = false;
                        continue;
                    }
                    regionFree[i] = data.Placeable == null && !data.HasFine && data.Base == null && held.CanOccupy(cell);
                    regionSupport[i] = data.Base != null || data.HasFine || (data.Placeable != null && data.Placeable != held);
                }

        // Adayların odunun hücrelerine uzaklığı: odunun her hücresinden dışarı bir kez yayılır
        int candCount = candSize.x * candSize.y * candSize.z;
        if (candidateDistance.Length < candCount) candidateDistance = new int[candCount];
        for (int i = 0; i < candCount; i++) candidateDistance[i] = int.MaxValue;
        foreach (Vector3Int offset in offsets)
        {
            Vector3Int c = origin + offset - candMin;
            for (int dx = -range; dx <= range; dx++)
                for (int dy = -range; dy <= range; dy++)
                    for (int dz = -range; dz <= range; dz++)
                    {
                        int x = c.x + dx, y = c.y + dy, z = c.z + dz;
                        if (x < 0 || y < 0 || z < 0 || x >= candSize.x || y >= candSize.y || z >= candSize.z) continue;
                        int i = (x * candSize.y + y) * candSize.z + z;
                        int distance = Mathf.Max(Mathf.Abs(dx), Mathf.Max(Mathf.Abs(dy), Mathf.Abs(dz)));
                        if (distance < candidateDistance[i]) candidateDistance[i] = distance;
                    }
        }

        // Hücre → saydamlık (yakın = 1); sadece odunun konabileceği adaylar
        Dictionary<Vector3Int, float> cells = cellBuffer;
        cells.Clear();
        for (int x = 0; x < candSize.x; x++)
            for (int y = 0; y < candSize.y; y++)
                for (int z = 0; z < candSize.z; z++)
                {
                    int distance = candidateDistance[(x * candSize.y + y) * candSize.z + z];
                    if (distance > range) continue;
                    var cell = new Vector3Int(candMin.x + x, candMin.y + y, candMin.z + z);
                    if (!CanHoldInRegion(cell, offsets, regionMin, regionSize)) continue;
                    cells[cell] = 1f - distance / (float)(range + 1);
                }

        // Yüzler ve kenarlar (köşe koordinatları × 2); ortak olanlar bir kez, en belirgin saydamlıkla
        Dictionary<(Vector3Int, Vector3Int), float> edges = edgeBuffer;
        edges.Clear();
        foreach (var pair in cells)
        {
            Vector3Int c2 = pair.Key * 2;
            // Sadece yatay kenarlar (alt ve üst yüzün kenarları; dikey kenar yok). Odunun hücrelerinin dışındaki hücrede,
            // merkezden uzak taraftaki (merkeze paralel) kenar çizilmez: çizgiler dışarı doğru açık uçlu uzanıp söner,
            // dış çerçeve kapanmaz (klasik yapı göstergesi). Ötesinde gösterilen hücre varsa o kenar onun yakın kenarı olur.
            Vector3Int c = pair.Key;
            int outX = c.x < min.x ? -1 : c.x > max.x ? 1 : 0; // merkezden hangi yana dışarıda
            int outZ = c.z < min.z ? -1 : c.z > max.z ? 1 : 0;
            for (int y = -1; y <= 1; y += 2)
                for (int s = -1; s <= 1; s += 2)
                {
                    // X boyunca uzanan kenar (z = c.z + s / 2) ve Z boyunca uzanan kenar (x = c.x + s / 2)
                    if (outZ == 0 || s != outZ)
                        AddEdge(edges, c2 + new Vector3Int(-1, y, s), c2 + new Vector3Int(1, y, s), pair.Value);
                    if (outX == 0 || s != outX)
                        AddEdge(edges, c2 + new Vector3Int(s, y, -1), c2 + new Vector3Int(s, y, 1), pair.Value);
                }
        }

        // Çizgiler yatay şerit; saydamlık çizgi boyunca sürekli: odunun hücrelerinin kutusunda tam, kutudan range uzakta 0
        Vector3 boxMin = (Vector3)min - Vector3.one * 0.5f, boxMax = (Vector3)max + Vector3.one * 0.5f;
        vertices.Clear();
        colors.Clear();
        triangles.Clear();
        Func<Vector3, float> fade = p => Fade(p, boxMin, boxMax);
        foreach (var pair in edges)
            AddRibbon((Vector3)pair.Key.Item1 * 0.5f, (Vector3)pair.Key.Item2 * 0.5f, fade, vertices, colors, triangles);
        SetMesh(vertices, colors, triangles);
    }

    // Odun bu hücreye (origin olarak) konabilir mi (CanHoldAt'in aynısı, bölge dizilerinden)
    private bool CanHoldInRegion(Vector3Int cell, IReadOnlyList<Vector3Int> offsets, Vector3Int regionMin, Vector3Int regionSize)
    {
        foreach (Vector3Int offset in offsets)
            if (!regionFree[RegionIndex(cell + offset - regionMin, regionSize)]) return false;
        if (!requireSupport) return true;
        foreach (int i in bottomOffsets)
            if (regionSupport[RegionIndex(cell + offsets[i] + Vector3Int.down - regionMin, regionSize)]) return true;
        return false;
    }

    private static int RegionIndex(Vector3Int local, Vector3Int size) => (local.x * size.y + local.y) * size.z + local.z;

    // Odun bu hücreye (origin olarak) konabilir mi: bütün şekli sığar, base'e girmez, (requireSupport) altı dolu.
    // Sökülen parçanın yerini ararken WoodMerger de kullanır.
    public static bool CanHoldAt(GridPlaceable held, Vector3Int cell, bool requireSupport)
    {
        GridManager grid = GridManager.Instance;
        if (grid == null || !grid.CanPlaceablePlaceOn(held, cell)) return false;
        IReadOnlyList<Vector3Int> footprint = held.GetFootprint().FilledCells();
        int bottom = int.MaxValue;
        foreach (Vector3Int offset in footprint)
        {
            if (!grid.TryGetCell(cell + offset, out GridData data) || data.Base != null) return false;
            bottom = Mathf.Min(bottom, offset.y);
        }
        if (!requireSupport) return true;
        foreach (Vector3Int offset in footprint)
        {
            if (offset.y != bottom) continue;
            if (!grid.TryGetCell(cell + offset + Vector3Int.down, out GridData below)) continue;
            if (below.Base != null || below.HasFine || (below.Placeable != null && below.Placeable != held)) return true;
        }
        return false;
    }

    // Birleştirme: her bağlantı yerinin hücrelerinin bütün yatay kenarları (ortak olan bir kez, en belirgin saydamlıkla)
    private void RebuildMerge(IWoodSlotView merge)
    {
        vertices.Clear();
        colors.Clear();
        triangles.Clear();
        int selected = merge.SelectedIndex;
        for (int i = 0; i < merge.CandidateCells.Count; i++)
        {
            Vector3Int[] cells = merge.CandidateCells[i];
            if (cells.Length == 0) continue;
            float alpha = i == selected ? selectedAlpha : optionAlpha;
            Vector3 direction = merge.CandidateDirections[i];
            // Yüzün iki kenar yönü (uzanma yönüne dik)
            Vector3 u = Mathf.Abs(direction.y) > 0.5f ? Vector3.right : Vector3.up;
            Vector3 v = Vector3.Cross(direction, u).normalized;
            Vector3 nearCenter = (Vector3)cells[0] - direction * 0.5f;
            Vector3[] corners =
            {
                nearCenter + (u + v) * 0.5f, nearCenter + (u - v) * 0.5f, nearCenter + (-u - v) * 0.5f, nearCenter + (-u + v) * 0.5f
            };
            // Başlangıçta kapalı çerçeve
            for (int k = 0; k < 4; k++)
                AddRibbon(corners[k], corners[(k + 1) % 4], _ => alpha, vertices, colors, triangles);
            // Köşelerden dışarı, ucunda tamamen şeffaf
            float length = cells.Length;
            foreach (Vector3 corner in corners)
            {
                Vector3 start = corner;
                AddRibbon(start, start + direction * length,
                    p => alpha * Mathf.Pow(Mathf.Clamp01(1f - Vector3.Dot(p - start, direction) / length), fadePower),
                    vertices, colors, triangles);
            }
        }
        SetMesh(vertices, colors, triangles);
    }

    private void SetMesh(List<Vector3> vertices, List<Color> colors, List<int> triangles)
    {
        mesh.Clear();
        if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        uvs.Clear();
        for (int i = 0; i < vertices.Count; i++) uvs.Add(Vector2.zero); // UV.y 0: yukarı doğru sönme yok
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    // a → b boyunca yatay şerit, saydamlık uzaklığa göre (eğri sönsün diye 4 parçaya bölünür)
    private void AddRibbon(Vector3 a, Vector3 b, Func<Vector3, float> alphaAt, List<Vector3> vertices, List<Color> colors, List<int> triangles)
    {
        const int parts = 4;
        // Yatay çizgi yatay şerit; dikey çizgi kameraya dönük şerit (yoksa genişliği sıfır olurdu)
        Vector3 along = (b - a).normalized;
        Vector3 across = Mathf.Abs(along.y) > 0.9f
            ? Vector3.Cross(along, Camera.main != null ? -Camera.main.transform.forward : Vector3.back).normalized
            : Vector3.Cross(Vector3.up, along).normalized;
        across *= lineWidth * 0.5f;
        int start = vertices.Count;
        for (int i = 0; i <= parts; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, i / (float)parts);
            Color c = new Color(1f, 1f, 1f, alphaAt(p));
            vertices.Add(p - across);
            vertices.Add(p + across);
            colors.Add(c);
            colors.Add(c);
        }
        for (int i = 0; i < parts; i++)
        {
            int k = start + i * 2;
            triangles.Add(k);
            triangles.Add(k + 1);
            triangles.Add(k + 3);
            triangles.Add(k);
            triangles.Add(k + 3);
            triangles.Add(k + 2);
        }
    }

    // Noktanın odunun hücrelerinin kutusuna uzaklığına (her eksende en büyüğü) göre saydamlık: kutuda edgeAlpha, range'de 0
    private float Fade(Vector3 p, Vector3 boxMin, Vector3 boxMax)
    {
        Vector3 outside = Vector3.Max(Vector3.zero, Vector3.Max(boxMin - p, p - boxMax));
        float distance = Mathf.Max(outside.x, Mathf.Max(outside.y, outside.z));
        return edgeAlpha * Mathf.Pow(Mathf.Clamp01(1f - distance / range), fadePower);
    }

    private static void AddEdge(Dictionary<(Vector3Int, Vector3Int), float> edges, Vector3Int a, Vector3Int b, float alpha)
    {
        var key = (a, b);
        edges[key] = Mathf.Max(edges.TryGetValue(key, out float e) ? e : 0f, alpha);
    }
}
