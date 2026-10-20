using System;
using System.Collections.Generic;
using UnityEngine;

// Elde tutulan odunun (Wood, MergedWood) çevresinde 3 boyutlu grid göstergesi: odunun şu an üstünde durduğu hücrelerden
// her yöne (yanlar, köşe çaprazları, yukarı, aşağı) `range` hücreye kadar, odunun konabileceği her hücre bir küp: içi çok
// şeffaf dolgu (`fillAlpha`, %80 şeffaf), kenarları belirgin çizgiler (`edgeAlpha`). Merkezden uzaklaştıkça ikisi de
// saydamlaşır (vertex rengi: odunun hücresinde tam, range + 1 uzakta 0). Konamayan hücre (içinde base, başka obje ya da
// ince parça var, ya da odunun hücre kuralı izin vermiyor: örn. su) çizilmez. Komşu küplerin ortak yüzü ve kenarı bir kez
// (en belirgin saydamlıkla), yoksa üst üste binip koyulaşırdı. Kenarlar çizgi (MeshTopology.Lines), yüzler üçgen; tek mesh,
// iki alt mesh, "Village/Grid Fade" (UV.y 0: yukarı doğru sönme yok). Odun hücre değiştirince mesh yeniden kurulur.
// Ayarları WoodMerger'da ("Held Grid"), WoodMerger.Update her kare Tick'i çağırır.
[Serializable]
public class HeldGridIndicator
{
    [Tooltip("Açıksa elde tutulan odunun çevresindeki hücreler gösterilir")]
    [SerializeField] private bool enabled = true;
    [Tooltip("Odunun hücrelerinden her yöne kaç hücre öteye kadar")]
    [SerializeField, Range(1, 4)] private int range = 2;
    [Tooltip("Renk (A kullanılmaz; saydamlık aşağıdaki iki ayardan)")]
    [SerializeField] private Color color = Color.white;
    [Tooltip("Küplerin içinin saydamlığı odunun hücresinde (0.2 = %80 şeffaf)")]
    [SerializeField, Range(0f, 1f)] private float fillAlpha = 0.2f;
    [Tooltip("Küp kenarlarının saydamlığı odunun hücresinde")]
    [SerializeField, Range(0f, 1f)] private float edgeAlpha = 0.8f;
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
            material.SetColor(ColorId, new Color(color.r, color.g, color.b, visibility));
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
        var faces = new Dictionary<Vector3Int, (Vector3Int axis, float alpha)>(); // yüzün ortası (× 2) → normal ekseni
        var edges = new Dictionary<(Vector3Int, Vector3Int), float>();
        Vector3Int[] axes = { Vector3Int.right, Vector3Int.up, new Vector3Int(0, 0, 1) };
        foreach (var pair in cells)
        {
            Vector3Int c2 = pair.Key * 2;
            foreach (Vector3Int axis in axes)
                foreach (int sign in new[] { -1, 1 })
                {
                    Vector3Int center = c2 + axis * sign;
                    float existing = faces.TryGetValue(center, out var face) ? face.alpha : 0f;
                    faces[center] = (axis, Mathf.Max(existing, pair.Value));
                }
            for (int i = 0; i < 8; i++)
            {
                Vector3Int corner = c2 + new Vector3Int((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1);
                foreach (Vector3Int axis in axes)
                {
                    Vector3Int other = corner + axis * 2;
                    if (Vector3.Dot(other - c2, axis) > 1.5f) continue; // küpün dışına çıkıyor
                    var key = (corner, other);
                    edges[key] = Mathf.Max(edges.TryGetValue(key, out float e) ? e : 0f, pair.Value);
                }
            }
        }

        var vertices = new List<Vector3>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        var lines = new List<int>();
        foreach (var pair in faces)
            AddFace(pair.Key, pair.Value.axis, fillAlpha * pair.Value.alpha, vertices, colors, triangles);
        foreach (var pair in edges)
        {
            lines.Add(vertices.Count);
            vertices.Add((Vector3)pair.Key.Item1 * 0.5f);
            lines.Add(vertices.Count);
            vertices.Add((Vector3)pair.Key.Item2 * 0.5f);
            Color c = new Color(1f, 1f, 1f, edgeAlpha * pair.Value);
            colors.Add(c);
            colors.Add(c);
        }
        var uvs = new List<Vector2>(new Vector2[vertices.Count]); // UV.y 0: yukarı doğru sönme yok

        mesh.Clear();
        if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.subMeshCount = 2;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetIndices(triangles, MeshTopology.Triangles, 0);
        mesh.SetIndices(lines, MeshTopology.Lines, 1);
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

    // Küpün bir yüzü: ortası center2 / 2, normali axis ekseninde (kenar uzunluğu 1)
    private static void AddFace(Vector3Int center2, Vector3Int axis, float alpha, List<Vector3> vertices, List<Color> colors, List<int> triangles)
    {
        Vector3 center = (Vector3)center2 * 0.5f;
        Vector3 u = axis.x != 0 ? Vector3.up : Vector3.right;
        Vector3 v = axis.z != 0 ? Vector3.up : Vector3.forward;
        if (axis.y != 0) { u = Vector3.right; v = Vector3.forward; }
        u *= 0.5f;
        v *= 0.5f;
        int start = vertices.Count;
        vertices.Add(center - u - v);
        vertices.Add(center + u - v);
        vertices.Add(center + u + v);
        vertices.Add(center - u + v);
        Color c = new Color(1f, 1f, 1f, alpha);
        for (int i = 0; i < 4; i++) colors.Add(c);
        triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
    }
}
