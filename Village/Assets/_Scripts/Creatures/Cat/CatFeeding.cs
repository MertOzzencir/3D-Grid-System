using System;
using System.Collections.Generic;
using UnityEngine;

// Kedinin beslenmesi (Cat'te bir alan, ayarları Cat'in Inspector'ında "Feeding"): dilimli besleme barı. Her balık
// (Fish) bir dilim doldurur. Sıçması şaplakla: midesi doluyken kıçına vurdukça (Cat ButtSlap → OnSlap) şaplak barı dolar,
// slapsToPoop şaplakta dolunca bir kaka (CatPoop, ağaç tohumu) yanındaki boş hücreye zıplar, besleme barından bir dilim
// gider, şaplak barı boşalır; midede dilim kaldıkça yeniden şaplakla bir tane daha. Midesi boşsa vurmak barı doldurmaz. Bir süre vurulmazsa (slapDecayDelay) şaplak barı yavaşça geri
// iner (slapDecay). Barlar kedinin üstünde üst üste, kameraya dönük: üstte besleme (dilimli), altında şaplak (düz
// dolan); elde balık varken ya da mouse kedinin üstündeyken görünür ("Village/Grid Fade"). Dolu dilim ve şaplak barı
// kayda girer (Cat.SaveState).
[Serializable]
public class CatFeeding
{
    [Tooltip("Besleme barının dilim sayısı: kedi en fazla bu kadar balık yer; her dilim bir kaka")]
    [SerializeField, Min(1)] private int slices = 4;
    [Tooltip("Kaç şaplakta sıçar (midesi doluyken)")]
    [SerializeField, Min(1)] private int slapsToPoop = 5;
    [Tooltip("Son şaplaktan bu kadar saniye sonra şaplak barı inmeye başlar")]
    [SerializeField] private float slapDecayDelay = 1.5f;
    [Tooltip("Şaplak barının inme hızı (saniyede şaplak)")]
    [SerializeField] private float slapDecay = 0.6f;
    [Tooltip("Kakanın verisi (CatPoop)")]
    [SerializeField] private GridPlaceableSO poopData;

    [Header("Bar")]
    [Tooltip("Barın kedinin pivotuna göre yeri (dünya ekseninde)")]
    [SerializeField] private Vector3 barOffset = new Vector3(0f, 1.2f, 0f);
    [SerializeField] private Vector2 barSize = new Vector2(0.9f, 0.14f);
    [SerializeField] private Color fillColor = new Color(1f, 0.72f, 0.3f, 1f);
    [SerializeField] private Color emptyColor = new Color(0.15f, 0.12f, 0.1f, 0.55f);
    [Tooltip("Şaplak barının rengi ve yüksekliği (besleme barının altında)")]
    [SerializeField] private Color slapColor = new Color(0.95f, 0.45f, 0.55f, 1f);
    [SerializeField] private float slapBarHeight = 0.07f;
    [Tooltip("Belirme / sönme hızı (saniyede)")]
    [SerializeField] private float fadeSpeed = 6f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private int fed;
    private float slaps;          // şaplak barı (0..slapsToPoop)
    private float lastSlapTime = float.MinValue;
    private Transform bar;
    private Mesh barMesh;
    private Material barMaterial;
    private MeshRenderer barRenderer;
    private int shownFed = -1;
    private float shownSlaps = -1f;
    private float visibility;

    public int Fed => fed;
    public float Slaps => slaps;
    public bool IsFull => fed >= slices;

    // Bir dilim doldurur (balık yendi). Doluysa yemez.
    public bool TryFeed()
    {
        if (IsFull) return false;
        fed++;
        return true;
    }

    // Kıçına şaplak: midesi doluysa şaplak barı bir dolar; dolunca sıçar
    public void OnSlap(Cat cat)
    {
        if (fed == 0) return;
        slaps += 1f;
        lastSlapTime = Time.time;
        if (slaps >= slapsToPoop) Poop(cat);
    }

    public void Restore(int savedFed, float savedSlaps)
    {
        fed = Mathf.Clamp(savedFed, 0, slices);
        slaps = fed > 0 ? Mathf.Clamp(savedSlaps, 0f, slapsToPoop - 1) : 0f;
    }

    // Her kare (kedi grid'deyken): şaplak barının inmesi ve barlar
    public void Tick(Cat cat, bool showBar)
    {
        if (slaps > 0f && Time.time - lastSlapTime > slapDecayDelay)
            slaps = Mathf.Max(0f, slaps - slapDecay * Time.deltaTime);
        UpdateBar(cat, showBar);
    }

    // Şaplak barı doldu: bir dilim harcanır, bir kaka kedinin yanındaki boş hücreye zıplar (midede dilim kaldıysa yeniden
    // şaplakla bir tane daha)
    private void Poop(Cat cat)
    {
        int count = 1;
        fed--;
        slaps = 0f;
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
        if (shownFed != fed || Mathf.Abs(shownSlaps - slaps) > 0.001f) RebuildBar();
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

    // Arka plan şeridi + besleme dilimleri (dolu dilimler fillColor, boşlar emptyColor; aralarında küçük boşluk) + altında
    // şaplak barı (boş şerit üstünde slapColor, şaplak oranında dolu)
    private void RebuildBar()
    {
        shownFed = fed;
        shownSlaps = slaps;
        var vertices = new List<Vector3>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        float pad = barSize.y * 0.18f;
        float slapTop = -barSize.y * 0.5f - pad; // şaplak barının üstü (besleme barının altında)
        float slapBottom = slapTop - slapBarHeight;
        AddQuad(-barSize.x * 0.5f - pad, slapBottom - pad, barSize.x + pad * 2f, barSize.y * 0.5f - slapBottom + pad * 2f, 0.01f,
                new Color(0f, 0f, 0f, 0.35f), vertices, colors, triangles);
        AddQuad(-barSize.x * 0.5f, slapBottom, barSize.x, slapBarHeight, 0.005f, emptyColor, vertices, colors, triangles);
        float slapFill = Mathf.Clamp01(slaps / slapsToPoop);
        if (slapFill > 0f)
            AddQuad(-barSize.x * 0.5f, slapBottom, barSize.x * slapFill, slapBarHeight, 0f, slapColor, vertices, colors, triangles);
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
