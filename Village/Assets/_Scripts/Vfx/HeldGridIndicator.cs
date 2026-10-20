using System;
using System.Collections.Generic;
using UnityEngine;

// Elde tutulan odunun (Wood, MergedWood) çevresinde 3 boyutlu grid göstergesi: odunun şu an üstünde durduğu hücrelerden
// her yöne (yanlar, köşe çaprazları, yukarı, aşağı) `range` hücreye kadar, odunun konabileceği hücrelerin kenarlarından
// yükselen ince saydam duvarlar + tabanda çizgiler ("Village/Grid Fade": tabanda belirgin, yukarı doğru söner; tarladaki
// gibi). Hücreler uzaklaştıkça saydamlaşır (vertex rengi: odunun hücresinde tam, range + 1 uzakta 0). Konamayan hücre
// (içinde base, başka obje ya da ince parça var, ya da odunun hücre kuralı izin vermiyor: örn. su) gösterilmez. Ortak
// duvar / çizgi bir kez (en belirgin hücrenin saydamlığıyla). Odun hücre değiştirince mesh yeniden kurulur.
// Ayarları WoodMerger'da ("Tutarken grid"), WoodMerger.Update her kare Tick'i çağırır.
[Serializable]
public class HeldGridIndicator
{
    [Tooltip("Açıksa elde tutulan odunun çevresindeki hücreler gösterilir")]
    [SerializeField] private bool enabled = true;
    [Tooltip("Odunun hücrelerinden her yöne kaç hücre öteye kadar")]
    [SerializeField, Range(1, 4)] private int range = 2;
    [Tooltip("Renk; A = odunun yanındaki hücrelerin tabandaki saydamlığı")]
    [SerializeField] private Color color = new Color(1f, 1f, 1f, 0.55f);
    [Tooltip("Duvarların yüksekliği (birim, hücre 1)")]
    [SerializeField] private float height = 0.5f;
    [Tooltip("Yukarı doğru sönmenin eğrisi (1 doğrusal, büyük = tabana daha yakın söner)")]
    [SerializeField, Range(0.2f, 6f)] private float falloff = 1.6f;
    [Tooltip("Tabandaki çizgilerin genişliği (birim; 0 = çizgi yok)")]
    [SerializeField] private float lineWidth = 0.035f;
    [Tooltip("Belirme / sönme hızı (saniyede)")]
    [SerializeField] private float fadeSpeed = 6f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int FalloffId = Shader.PropertyToID("_Falloff");

    private GameObject root;
    private Mesh mesh;
    private Material material;
    private MeshRenderer renderer;
    private float visibility;
    private GridPlaceable shownFor;
    private Vector3Int shownOrigin;
    private GridMaskRotator.Rotation shownRotation;

    // Her kare: elde odun varsa onun çevresi gösterilir, yoksa söner
    public void Tick(float deltaTime)
    {
        InteractableController controller = InteractableController.Instance;
        GridPlaceable held = enabled && controller != null ? controller.Held as GridPlaceable : null;
        if (!(held is Wood) && !(held is MergedWood)) held = null;

        if (held != null && EnsureRoot())
        {
            Vector3Int origin = Vector3Int.RoundToInt(held.transform.position);
            if (held != shownFor || origin != shownOrigin || held.Rotation != shownRotation)
            {
                Rebuild(held, origin);
                shownFor = held;
                shownOrigin = origin;
                shownRotation = held.Rotation;
            }
        }
        if (material == null) return;

        float target = held != null ? 1f : 0f;
        if (!Mathf.Approximately(visibility, target))
        {
            visibility = Mathf.MoveTowards(visibility, target, fadeSpeed * deltaTime);
            Color c = color;
            c.a *= visibility;
            material.SetColor(ColorId, c);
        }
        renderer.enabled = visibility > 0.001f;
        if (held == null && visibility <= 0.001f) shownFor = null; // sonraki tutuşta yeniden kurulsun
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
        material.SetFloat(FalloffId, falloff);
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

        // Duvarlar (hücrenin dört yan yüzü) ve tabandaki çizgiler; ortak olanlar bir kez, en belirgin saydamlıkla
        var walls = new Dictionary<(Vector3Int, Vector3Int), float>();
        var lines = new Dictionary<(Vector3Int, Vector3Int), float>();
        foreach (var pair in cells)
        {
            Vector3Int c = pair.Key;
            // Köşeler (koordinat × 2, tabanın dört köşesi)
            Vector3Int a = new Vector3Int(c.x * 2 - 1, c.y * 2 - 1, c.z * 2 - 1);
            Vector3Int b = a + new Vector3Int(2, 0, 0), d = a + new Vector3Int(0, 0, 2), e = a + new Vector3Int(2, 0, 2);
            foreach ((Vector3Int p, Vector3Int q) in new[] { (a, b), (d, e), (a, d), (b, e) })
            {
                var key = Compare(p, q) <= 0 ? (p, q) : (q, p);
                walls[key] = Mathf.Max(walls.TryGetValue(key, out float w) ? w : 0f, pair.Value);
                lines[key] = Mathf.Max(lines.TryGetValue(key, out float l) ? l : 0f, pair.Value);
            }
        }

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        foreach (var pair in walls)
            AddWall((Vector3)pair.Key.Item1 * 0.5f, (Vector3)pair.Key.Item2 * 0.5f, pair.Value, vertices, uvs, colors, triangles);
        if (lineWidth > 0f)
            foreach (var pair in lines)
                AddLine((Vector3)pair.Key.Item1 * 0.5f, (Vector3)pair.Key.Item2 * 0.5f, pair.Value, vertices, uvs, colors, triangles);

        mesh.Clear();
        if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    // Odun bu hücreye konabilir mi: grid'de, base yok, başka obje / ince parça yok, odunun hücre kuralı izin veriyor
    private static bool CanHoldAt(GridPlaceable held, Vector3Int cell)
    {
        GridManager grid = GridManager.Instance;
        if (grid == null || !grid.TryGetCell(cell, out GridData data)) return false;
        if (data.Base != null || data.HasFine) return false;
        if (data.Placeable != null && data.Placeable != held) return false;
        return held.CanOccupy(cell);
    }

    private static int Compare(Vector3Int a, Vector3Int b)
    {
        if (a.x != b.x) return a.x.CompareTo(b.x);
        if (a.y != b.y) return a.y.CompareTo(b.y);
        return a.z.CompareTo(b.z);
    }

    // a → b (hücrenin tabanındaki kenar) boyunca dik duvar (UV.y: tabanda 0, tepede 1)
    private void AddWall(Vector3 a, Vector3 b, float alpha, List<Vector3> vertices, List<Vector2> uvs, List<Color> colors, List<int> triangles)
    {
        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(b + Vector3.up * height);
        vertices.Add(a + Vector3.up * height);
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(1f, 0f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(0f, 1f));
        Color c = new Color(1f, 1f, 1f, alpha);
        for (int i = 0; i < 4; i++) colors.Add(c);
        triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
    }

    // Tabanda a → b boyunca ince yatay şerit (tam belirgin: UV.y 0), zeminle çakışmasın diye biraz yukarıda
    private void AddLine(Vector3 a, Vector3 b, float alpha, List<Vector3> vertices, List<Vector2> uvs, List<Color> colors, List<int> triangles)
    {
        Vector3 along = (b - a).normalized;
        Vector3 across = Vector3.Cross(Vector3.up, along) * (lineWidth * 0.5f), lift = Vector3.up * 0.005f;
        int start = vertices.Count;
        vertices.Add(a - across + lift);
        vertices.Add(b - across + lift);
        vertices.Add(b + across + lift);
        vertices.Add(a + across + lift);
        Color c = new Color(1f, 1f, 1f, alpha);
        for (int i = 0; i < 4; i++)
        {
            uvs.Add(Vector2.zero);
            colors.Add(c);
        }
        triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
    }
}
