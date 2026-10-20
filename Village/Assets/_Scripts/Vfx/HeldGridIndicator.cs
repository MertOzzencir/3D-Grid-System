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
// Birleştirme: taşınan odun başka bir oduna yapışınca (WoodMergeTarget.Active) çevre yerine sadece bağlantı yerlerinin
// hücreleri gösterilir (eski ok göstergelerinin yerine): her uygun yerin parçasının kaplayacağı hücrelerin yatay kenarları,
// mouse'un seçtiği yer `selectedAlpha`, diğerleri `optionAlpha`, uzaklıkla sönme yok.
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

    // Her kare: elde odun varsa onun çevresi gösterilir, yoksa söner
    public void Tick(float deltaTime)
    {
        InteractableController controller = InteractableController.Instance;
        GridPlaceable held = enabled && controller != null ? controller.Held as GridPlaceable : null;
        if (!(held is Wood) && !(held is MergedWood)) held = null;

        if (held != null && EnsureRoot())
        {
            WoodMergeTarget merge = held is Wood ? WoodMergeTarget.Active : null;
            if (merge != null && merge.IsAlive && merge.CandidateCells.Count > 0)
            {
                // Birleştirme: sadece bağlantı yerlerinin hücreleri
                int key = merge.CandidatesKey;
                if (shownMergeKey != key)
                {
                    RebuildMerge(merge);
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
        var footprint = new List<Vector3Int>();
        foreach (Vector3Int offset in held.GetFootprint().FilledCells())
            footprint.Add(origin + offset);
        Vector3Int min = footprint[0], max = footprint[0];
        foreach (Vector3Int cell in footprint)
        {
            min = Vector3Int.Min(min, cell);
            max = Vector3Int.Max(max, cell);
        }

        // Hücre → saydamlık (yakın = 1)
        var cells = new Dictionary<Vector3Int, float>();
        for (int x = min.x - range; x <= max.x + range; x++)
            for (int y = min.y - range; y <= max.y + range; y++)
                for (int z = min.z - range; z <= max.z + range; z++)
                {
                    var cell = new Vector3Int(x, y, z);
                    int distance = int.MaxValue;
                    foreach (Vector3Int f in footprint)
                        distance = Mathf.Min(distance, Mathf.Max(Mathf.Abs(x - f.x), Mathf.Max(Mathf.Abs(y - f.y), Mathf.Abs(z - f.z))));
                    if (distance > range || !CanHoldAt(held, cell)) continue;
                    cells[cell] = 1f - distance / (float)(range + 1);
                }

        // Yüzler ve kenarlar (köşe koordinatları × 2); ortak olanlar bir kez, en belirgin saydamlıkla
        var edges = new Dictionary<(Vector3Int, Vector3Int), float>();
        foreach (var pair in cells)
        {
            Vector3Int c2 = pair.Key * 2;
            // Sadece yatay kenarlar (alt ve üst yüzün kenarları; dikey kenar yok). Odunun hücrelerinin dışındaki hücrede,
            // merkezden uzak taraftaki (merkeze paralel) kenar çizilmez: çizgiler dışarı doğru açık uçlu uzanıp söner,
            // dış çerçeve kapanmaz (klasik yapı göstergesi). Ötesinde gösterilen hücre varsa o kenar onun yakın kenarı olur.
            Vector3Int c = pair.Key;
            int outX = c.x < min.x ? -1 : c.x > max.x ? 1 : 0; // merkezden hangi yana dışarıda
            int outZ = c.z < min.z ? -1 : c.z > max.z ? 1 : 0;
            foreach (int y in new[] { -1, 1 })
                foreach (int s in new[] { -1, 1 })
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
        var vertices = new List<Vector3>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        foreach (var pair in edges)
            AddRibbon((Vector3)pair.Key.Item1 * 0.5f, (Vector3)pair.Key.Item2 * 0.5f, p => Fade(p, boxMin, boxMax), vertices, colors, triangles);
        SetMesh(vertices, colors, triangles);
    }

    // Odun bu hücreye (origin olarak) konabilir mi: bütün şekli sığar, base'e girmez, (requireSupport) altı dolu
    private bool CanHoldAt(GridPlaceable held, Vector3Int cell)
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
    private void RebuildMerge(WoodMergeTarget merge)
    {
        var edges = new Dictionary<(Vector3Int, Vector3Int), float>();
        int selected = merge.SelectedIndex;
        for (int i = 0; i < merge.CandidateCells.Count; i++)
        {
            float alpha = i == selected ? selectedAlpha : optionAlpha;
            foreach (Vector3Int cell in merge.CandidateCells[i])
            {
                Vector3Int c2 = cell * 2;
                foreach (int y in new[] { -1, 1 })
                    foreach (int s in new[] { -1, 1 })
                    {
                        AddEdge(edges, c2 + new Vector3Int(-1, y, s), c2 + new Vector3Int(1, y, s), alpha);
                        AddEdge(edges, c2 + new Vector3Int(s, y, -1), c2 + new Vector3Int(s, y, 1), alpha);
                    }
            }
        }

        var vertices = new List<Vector3>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        foreach (var pair in edges)
            AddRibbon((Vector3)pair.Key.Item1 * 0.5f, (Vector3)pair.Key.Item2 * 0.5f, _ => pair.Value, vertices, colors, triangles);
        SetMesh(vertices, colors, triangles);
    }

    private void SetMesh(List<Vector3> vertices, List<Color> colors, List<int> triangles)
    {
        mesh.Clear();
        if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, new List<Vector2>(new Vector2[vertices.Count])); // UV.y 0: yukarı doğru sönme yok
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    // a → b boyunca yatay şerit, saydamlık uzaklığa göre (eğri sönsün diye 4 parçaya bölünür)
    private void AddRibbon(Vector3 a, Vector3 b, Func<Vector3, float> alphaAt, List<Vector3> vertices, List<Color> colors, List<int> triangles)
    {
        const int parts = 4;
        Vector3 across = Vector3.Cross(Vector3.up, (b - a).normalized) * (lineWidth * 0.5f);
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
            triangles.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 });
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
