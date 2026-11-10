using System;
using System.Collections.Generic;
using UnityEngine;

// Tarlanın grid göstergesi (eski düz güverte yerine): her hücre kenarından yükselen ince saydam duvarlar + tabanda ince
// çizgiler, "Village/Grid Fade" shader'ıyla tabanda belirgin, yukarı doğru saydamlaşır. Her hücre aynı (bottan bağımsız);
// ortak kenarlar bir kez çizilir. Ayarları Boat'ta ("Tarla grid göstergesi"), Boat.CreateDeck kurar. Collider'ı yok
// (parçaların bırakıldığı yüzey güvertenin kendi collider'ı). Hep görünmez: Boat her kare SetVisible ile söyler (bota
// binilince ya da elde tarla parçası varken); fadeSpeed hızıyla belirir / söner.
[Serializable]
public class FarmGridIndicator
{
    [Tooltip("Açıksa tarlanın hücreleri gösterilir")]
    [SerializeField] private bool enabled = true;
    [Tooltip("Renk; A = tabandaki saydamlık (yukarı doğru tamamen söner)")]
    [SerializeField] private Color color = new Color(1f, 1f, 1f, 0.6f);
    [Tooltip("Duvarların yüksekliği (birim)")]
    [SerializeField] private float height = 0.5f;
    [Tooltip("Yukarı doğru sönmenin eğrisi (1 doğrusal, büyük = tabana daha yakın söner)")]
    [SerializeField, Range(0.2f, 6f)] private float falloff = 1.6f;
    [Tooltip("Tabandaki çizgilerin genişliği (birim; 0 = çizgi yok)")]
    [SerializeField] private float lineWidth = 0.04f;
    [Tooltip("Belirme / sönme hızı (saniyede, 1 = bir saniyede tam)")]
    [SerializeField] private float fadeSpeed = 4f;

    private Material material;
    private Renderer renderer;
    private float visibility;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int FalloffId = Shader.PropertyToID("_Falloff");

    // parent'ın uzayında: tarlanın üst yüzünün ortası 0, size × size hücre
    public GameObject Build(Transform parent, int size, Shader shader)
    {
        if (!enabled || size <= 0) return null;
        if (shader == null) shader = Shader.Find("Village/Grid Fade");
        if (shader == null)
        {
            Debug.LogWarning("FarmGridIndicator: 'Village/Grid Fade' shader'ı yok (Boat → Grid Shader).");
            return null;
        }

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        float half = size * 0.5f;
        for (int i = 0; i <= size; i++)
        {
            float k = -half + i;
            // X boyunca ve Z boyunca uzanan kenarlar (ortak kenar bir kez)
            AddWall(new Vector3(-half, 0f, k), new Vector3(half, 0f, k), vertices, uvs, triangles);
            AddWall(new Vector3(k, 0f, -half), new Vector3(k, 0f, half), vertices, uvs, triangles);
            if (lineWidth > 0f)
            {
                AddLine(new Vector3(-half, 0f, k), new Vector3(half, 0f, k), Vector3.forward, vertices, uvs, triangles);
                AddLine(new Vector3(k, 0f, -half), new Vector3(k, 0f, half), Vector3.right, vertices, uvs, triangles);
            }
        }

        var mesh = new Mesh { name = "Farm Grid" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        var colors = new List<Color>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++) colors.Add(Color.white); // vertex rengi beyaz (shader çarpar)
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();

        var grid = new GameObject("Farm Grid");
        grid.transform.SetParent(parent, false);
        grid.AddComponent<MeshFilter>().sharedMesh = mesh;
        material = new Material(shader) { name = "Farm Grid (generated)" };
        material.SetFloat(FalloffId, falloff);
        renderer = grid.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        visibility = 0f;
        Apply();
        return grid;
    }

    // visible: görünmesi gerekiyor mu; yavaşça oraya gider
    public void SetVisible(bool visible, float deltaTime)
    {
        if (material == null) return;
        float target = visible ? 1f : 0f;
        if (Mathf.Approximately(visibility, target)) return;
        visibility = Mathf.MoveTowards(visibility, target, fadeSpeed * deltaTime);
        Apply();
    }

    private void Apply()
    {
        Color c = color;
        c.a *= visibility;
        material.SetColor(ColorId, c);
        renderer.enabled = visibility > 0.001f;
    }

    // a → b boyunca dik duvar (UV.y: tabanda 0, tepede 1)
    private void AddWall(Vector3 a, Vector3 b, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
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
        triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
    }

    // Tabanda a → b boyunca ince yatay şerit (tam belirgin: UV.y 0), suyla çakışmasın diye biraz yukarıda
    private void AddLine(Vector3 a, Vector3 b, Vector3 across, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
    {
        int start = vertices.Count;
        Vector3 w = across * (lineWidth * 0.5f), lift = Vector3.up * 0.005f;
        vertices.Add(a - w + lift);
        vertices.Add(b - w + lift);
        vertices.Add(b + w + lift);
        vertices.Add(a + w + lift);
        for (int i = 0; i < 4; i++) uvs.Add(Vector2.zero);
        triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
    }
}
