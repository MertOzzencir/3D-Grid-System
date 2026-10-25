using System;
using System.Collections.Generic;
using UnityEngine;

// Kedinin beslenmesi (Cat'te bir alan, ayarları Cat'in Inspector'ında "Feeding"): dilimli besleme barı. Her balık yemi
// (FishBait) bir dilim doldurur; son beslemeden digestSeconds sonra kedi sindirir: dolu dilim kadar kaka (CatPoop, ağaç
// tohumu) yanındaki boş hücrelere zıplar, bar boşalır. Daha çok beslenen kedi tek seferde daha çok kaka yapar.
// Bar kedinin üstünde, kameraya dönük; elde balık varken ya da mouse kedinin üstündeyken görünür ("Village/Grid Fade").
// Dolu dilim sayısı ve sindirme süresi kayda girer (Cat.SaveState).
[Serializable]
public class CatFeeding
{
    [Tooltip("Barın dilim sayısı: kedi en fazla bu kadar balık yer, sindirince bu kadar kaka yapar")]
    [SerializeField, Min(1)] private int slices = 4;
    [Tooltip("Son beslemeden sonra sindirme süresi (saniye)")]
    [SerializeField] private float digestSeconds = 8f;
    [Tooltip("Kakanın verisi (CatPoop)")]
    [SerializeField] private GridPlaceableSO poopData;

    [Header("Bar")]
    [Tooltip("Barın kedinin pivotuna göre yeri (dünya ekseninde)")]
    [SerializeField] private Vector3 barOffset = new Vector3(0f, 1.2f, 0f);
    [SerializeField] private Vector2 barSize = new Vector2(0.9f, 0.14f);
    [SerializeField] private Color fillColor = new Color(1f, 0.72f, 0.3f, 1f);
    [SerializeField] private Color emptyColor = new Color(0.15f, 0.12f, 0.1f, 0.55f);
    [Tooltip("Belirme / sönme hızı (saniyede)")]
    [SerializeField] private float fadeSpeed = 6f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private int fed;
    private float digestTimer;
    private Transform bar;
    private Mesh barMesh;
    private Material barMaterial;
    private MeshRenderer barRenderer;
    private int shownFed = -1;
    private float visibility;

    public int Fed => fed;
    public float DigestTimer => digestTimer;
    public bool IsFull => fed >= slices;

    // Bir dilim doldurur (balık yendi). Doluysa yemez.
    public bool TryFeed()
    {
        if (IsFull) return false;
        fed++;
        digestTimer = digestSeconds;
        return true;
    }

    public void Restore(int savedFed, float savedTimer)
    {
        fed = Mathf.Clamp(savedFed, 0, slices);
        digestTimer = savedTimer > 0f ? savedTimer : digestSeconds;
    }

    // Her kare (kedi grid'deyken): sindirme ve bar
    public void Tick(Cat cat, bool showBar)
    {
        if (fed > 0)
        {
            digestTimer -= Time.deltaTime;
            if (digestTimer <= 0f) Poop(cat);
        }
        UpdateBar(cat, showBar);
    }

    // Dolu dilim kadar kaka, kedinin yanındaki boş hücrelere (her biri yerini kaplar, sonraki bir başkasına düşer)
    private void Poop(Cat cat)
    {
        int count = fed;
        fed = 0;
        if (poopData == null)
        {
            Debug.LogWarning("CatFeeding: Poop Data atanmamış, kaka çıkmadı.", cat);
            return;
        }
        Vector3 from = cat.transform.position + Vector3.up * 0.3f;
        for (int i = 0; i < count; i++)
        {
            Vector3 cell = GridManager.Instance.GetEmptyGridFromEntityPosition(cat, out bool found);
            if (!found) return;
            var poop = (GridPlaceable)poopData.Spawn();
            if (!GridManager.Instance.PlaceablePlaceOn(poop, cell))
            {
                UnityEngine.Object.Destroy(poop.gameObject);
                return;
            }
            if (poop is Carryable carryable) carryable.HopFrom(from);
        }
    }

    // --- Bar ---

    private void UpdateBar(Cat cat, bool show)
    {
        float target = show ? 1f : 0f;
        visibility = Mathf.MoveTowards(visibility, target, fadeSpeed * Time.deltaTime);
        if (visibility <= 0.001f)
        {
            if (barRenderer != null) barRenderer.enabled = false;
            return;
        }
        if (!EnsureBar(cat)) return;
        if (shownFed != fed) RebuildBar();
        barRenderer.enabled = true;
        barMaterial.SetColor(ColorId, new Color(1f, 1f, 1f, visibility));
        bar.position = cat.transform.position + barOffset;
        if (Camera.main != null) bar.rotation = Camera.main.transform.rotation;
    }

    private bool EnsureBar(Cat cat)
    {
        if (bar != null) return true;
        Shader shader = Shader.Find("Village/Grid Fade");
        if (shader == null) return false;
        var obj = new GameObject("Feed Bar");
        obj.transform.SetParent(cat.transform, false);
        bar = obj.transform;
        barMesh = new Mesh { name = "Feed Bar" };
        obj.AddComponent<MeshFilter>().sharedMesh = barMesh;
        barMaterial = new Material(shader) { name = "Feed Bar (generated)" };
        barRenderer = obj.AddComponent<MeshRenderer>();
        barRenderer.sharedMaterial = barMaterial;
        barRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        barRenderer.receiveShadows = false;
        return true;
    }

    // Arka plan şeridi + dilimler (dolu dilimler fillColor, boşlar emptyColor; aralarında küçük boşluk)
    private void RebuildBar()
    {
        shownFed = fed;
        var vertices = new List<Vector3>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        float pad = barSize.y * 0.18f;
        AddQuad(-barSize.x * 0.5f - pad, -barSize.y * 0.5f - pad, barSize.x + pad * 2f, barSize.y + pad * 2f, 0.01f,
                new Color(0f, 0f, 0f, 0.35f), vertices, colors, triangles);
        float gap = barSize.x * 0.03f;
        float width = (barSize.x - gap * (slices - 1)) / slices;
        for (int i = 0; i < slices; i++)
        {
            float x = -barSize.x * 0.5f + i * (width + gap);
            AddQuad(x, -barSize.y * 0.5f, width, barSize.y, 0f, i < fed ? fillColor : emptyColor, vertices, colors, triangles);
        }
        barMesh.Clear();
        barMesh.SetVertices(vertices);
        barMesh.SetUVs(0, new List<Vector2>(new Vector2[vertices.Count])); // UV.y 0: Grid Fade yukarı doğru söndürmesin
        barMesh.SetColors(colors);
        barMesh.SetTriangles(triangles, 0);
        barMesh.RecalculateBounds();
    }

    // z: kameradan uzağa (arka plan dilimlerin arkasında kalsın)
    private static void AddQuad(float x, float y, float w, float h, float z, Color color, List<Vector3> vertices, List<Color> colors, List<int> triangles)
    {
        int start = vertices.Count;
        vertices.Add(new Vector3(x, y, z));
        vertices.Add(new Vector3(x + w, y, z));
        vertices.Add(new Vector3(x, y + h, z));
        vertices.Add(new Vector3(x + w, y + h, z));
        for (int i = 0; i < 4; i++) colors.Add(color);
        triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
        triangles.Add(start + 1); triangles.Add(start + 2); triangles.Add(start + 3);
    }
}
