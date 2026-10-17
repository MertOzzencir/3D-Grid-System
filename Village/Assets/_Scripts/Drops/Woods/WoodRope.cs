using System;
using System.Collections.Generic;
using UnityEngine;

// Bağlantı ipi: yan parçanın ana oduna değdiği yerin hemen dışında, parçanın etrafına sarılı ip halkası. Ayarları
// WoodMerger'da; MergedWood her yanda art arda dolu katlar için bir kez Attach'i çağırır: üst üste duran parçalar tek
// uzun halkayla birlikte bağlanır (her parçaya ayrı halka komşu katın halkasıyla üst üste biniyordu). Model atanırsa o konur, yoksa koddan yuvarlak köşeli
// kare bir halka (tüp) üretilir. Malzeme atanmazsa odunun malzemesinin kopyası ip rengiyle kullanılır (aynı shader:
// birleşince odunla birlikte sallanır, ön parça saydamlaşmasına katılır).
[Serializable]
public class WoodRope
{
    [Tooltip("Açıksa yan parçaların ana oduna bağlandığı yere ip sarılır")]
    [SerializeField] private bool enabled = true;
    [Tooltip("İsteğe bağlı ip modeli. Pivot halkanın ortasında, halka XZ düzleminde (yan parçanın ekseni modelin Y'si). " +
             "Boşsa koddan basit bir halka üretilir.")]
    [SerializeField] private GameObject model;
    [Tooltip("İsteğe bağlı malzeme ('Village/Wood Wobble Lit' olmalı ki sallansın). Boşsa odunun malzemesi ip rengiyle.")]
    [SerializeField] private Material material;
    [SerializeField] private Color color = new Color(0.86f, 0.74f, 0.52f);
    [Tooltip("Halkanın ana odunla yan parçanın değdiği yerden yan parçanın içine doğru uzaklığı (birim)")]
    [SerializeField] private float offset = 0.2f;
    [Tooltip("Halkanın yarı genişliği (birim): odunun yarı kalınlığından biraz büyük olmalı (1BR ~0.545)")]
    [SerializeField] private float halfSize = 0.56f;
    [Tooltip("Halkanın köşe yuvarlaklığı (birim)")]
    [SerializeField] private float cornerRadius = 0.18f;
    [Tooltip("İpin kalınlığı (yarıçap, birim)")]
    [SerializeField] private float thickness = 0.045f;

    private static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();
    private Material generatedMaterial;
    private Material generatedFrom;

    // holder: yığındaki bir uçtaki yan parçanın kökü (ilk hücrenin ortası, yerel Y = parçanın uzandığı yön; ana odunla
    // değme yeri y = -0.5). span: holder'ın uzayında bu parçadan yığının öbür ucundaki parçaya (tek parçada sıfır; halka
    // ikisini birlikte sarar). woodMaterial: parçanın malzemesi (ip malzemesi atanmamışsa kopyalanır). Eklenen ipi
    // döndürür (kapalıysa null).
    public GameObject Attach(Transform holder, Vector3 span, Material woodMaterial)
    {
        span -= Vector3.up * Vector3.Dot(span, Vector3.up); // parçanın ekseni boyunca kayma olmaz
        float stretch = span.magnitude;                       // halkanın uzun kenarı bu kadar uzar
        // Halkanın uzun kenarının yönü: yığının yönü; tek parçada parçaya dik herhangi bir yön
        Vector3 stackDir = stretch > 0.001f ? span / stretch : Vector3.Cross(Vector3.up, Vector3.right).normalized;
        if (stackDir.sqrMagnitude < 0.5f) stackDir = Vector3.right;

        if (!enabled) return null;
        GameObject rope;
        if (model != null)
        {
            rope = UnityEngine.Object.Instantiate(model, holder, false);
        }
        else
        {
            rope = new GameObject("Rope");
            rope.transform.SetParent(holder, false);
            rope.AddComponent<MeshFilter>().sharedMesh = RingMesh(halfSize + stretch * 0.5f, halfSize);
            rope.AddComponent<MeshRenderer>().sharedMaterial = Material(woodMaterial);
        }
        rope.name = "Rope";
        // Halkanın X'i yığının yönünde (uzun kenar), Y'si parçanın ekseninde; ortası sardığı parçaların ortasında
        rope.transform.localPosition = Vector3.up * (-0.5f + offset) + span * 0.5f;
        rope.transform.localRotation = Quaternion.LookRotation(Vector3.Cross(stackDir, Vector3.up), Vector3.up);
        if (model != null) rope.transform.localScale = new Vector3(1f + stretch / (2f * halfSize), 1f, 1f);
        return rope;
    }

    private Material Material(Material woodMaterial)
    {
        if (material != null) return material;
        if (woodMaterial == null) return null;
        if (generatedMaterial == null || generatedFrom != woodMaterial)
        {
            // Odunun malzemesinin kopyası (aynı shader), dokusuz, ip renginde
            generatedMaterial = new Material(woodMaterial) { name = "Rope (generated)" };
            generatedMaterial.SetTexture("_BaseMap", null);
            generatedMaterial.SetTexture("_BumpMap", null);
            generatedMaterial.DisableKeyword("_NORMALMAP");
            generatedMaterial.SetColor("_BaseColor", color);
            generatedFrom = woodMaterial;
        }
        return generatedMaterial;
    }

    // XZ düzleminde yuvarlak köşeli dikdörtgen yol boyunca tüp (yarı boyları halfX, halfZ; u: yol boyunca, v: tüpün çevresi)
    private Mesh RingMesh(float halfX, float halfZ)
    {
        string key = $"{halfX}|{halfZ}|{cornerRadius}|{thickness}";
        if (meshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

        const int cornerSteps = 6, sides = 8;
        float corner = Mathf.Clamp(cornerRadius, 0.001f, Mathf.Min(halfX, halfZ));
        float innerX = halfX - corner, innerZ = halfZ - corner; // köşe yaylarının merkezleri

        // Yol: dört köşe yayı (düz kenarlar yaylar arasında kendiliğinden oluşur)
        var path = new List<Vector3>();
        var outward = new List<Vector3>();
        Vector2[] centers = { new Vector2(innerX, innerZ), new Vector2(-innerX, innerZ), new Vector2(-innerX, -innerZ), new Vector2(innerX, -innerZ) };
        for (int c = 0; c < 4; c++)
            for (int s = 0; s <= cornerSteps; s++)
            {
                float angle = (c * 90f + s * 90f / cornerSteps) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                path.Add(new Vector3(centers[c].x, 0f, centers[c].y) + dir * corner);
                outward.Add(dir);
            }

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        int rings = path.Count + 1; // ilk halka sonda tekrar (UV dikişi)
        float length = 0f;
        for (int i = 0; i < rings; i++)
        {
            int p = i % path.Count;
            if (i > 0) length += Vector3.Distance(path[p], path[(i - 1) % path.Count]);
            for (int s = 0; s <= sides; s++)
            {
                float angle = s * Mathf.PI * 2f / sides;
                Vector3 normal = outward[p] * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle);
                vertices.Add(path[p] + normal * thickness);
                normals.Add(normal);
                uvs.Add(new Vector2(length, (float)s / sides));
            }
        }
        var triangles = new List<int>();
        int stride = sides + 1;
        for (int i = 0; i < rings - 1; i++)
            for (int s = 0; s < sides; s++)
            {
                int a = i * stride + s, b = a + 1, c = a + stride, d = c + 1;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(b); triangles.Add(d); triangles.Add(c);
            }

        var mesh = new Mesh { name = "Wood Rope" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        meshCache[key] = mesh;
        return mesh;
    }
}
