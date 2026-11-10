using System.Collections.Generic;
using UnityEngine;

// Adanın dış kenarından suyun altına eğimli inen etek (yamaç). Sadece görsel: collider yok, grid'e dokunmaz.
// Base hücrelerinden otomatik kurulur; base eklenip silinince (GridManager.BasesChanged) bir sonraki karede yeniden kurulur.
// Su yamaca değdiği yerde köpük çıkar, yamaç aşağı indikçe su sığdan derine geçer (StylizedWater derinlikle çalışıyor).
//
// Her sütunun (x, z) en alttaki base hücresi esas alınır. Komşu sütunu boş olan her yana bir yamaç parçası (trapez) konur:
//   üst kenar hücre kenarında (karonun altına gizlenecek kadar içeride), alt kenar dışarı doğru "flare" kadar açılır.
//   Parçanın uçları köşe türüne göre ayarlanır, böylece komşu parçalar köşegen boyunca boşluksuz birleşir:
//     dış köşe (yandaki hücre yok)            → alt uç +flare uzar
//     düz kenar (yandaki var, çaprazı yok)     → değişmez
//     iç köşe (yandaki de çaprazı da var)      → alt uç -flare kısalır
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class IslandSkirt : MonoBehaviour
{
    [Tooltip("Su yüzeyi (y'si okunur). Boşsa waterLevel kullanılır.")]
    [SerializeField] private Transform water;
    [SerializeField] private float waterLevel = 0f;

    [Tooltip("Yamacın üst kenarı, en alttaki base hücresinin merkezine göre (karonun altında kalmalı)")]
    [SerializeField] private float topOffset = -0.3f;
    [Tooltip("Üst kenar hücre kenarından ne kadar içeride (karonun yuvarlak kenarının altına gizlensin)")]
    [SerializeField] private float inset = 0.08f;
    [Tooltip("Yamaç suyun ne kadar altına insin")]
    [SerializeField] private float depthBelowWater = 1.5f;
    [Tooltip("Alt kenar dışarı doğru ne kadar açılsın (büyüdükçe yamaç yatıklaşır, sığ su şeridi genişler)")]
    [SerializeField] private float flare = 1.2f;

    private static readonly Vector2Int[] Sides = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

    private Mesh mesh;
    private bool dirty = true;
    private GridManager subscribedGrid;

    private float WaterY => water != null ? water.position.y : waterLevel;

    private void Awake()
    {
        mesh = new Mesh { name = "Island Skirt (üretilen)" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    // GridManager.Instance, Awake sırasına bağlı; Start'ta kesin hazır
    private void Start()
    {
        subscribedGrid = GridManager.Instance;
        if (subscribedGrid != null) subscribedGrid.BasesChanged += MarkDirty;
    }

    private void OnDestroy()
    {
        if (subscribedGrid != null) subscribedGrid.BasesChanged -= MarkDirty;
        if (mesh != null) Destroy(mesh);
    }

    private void OnValidate() => dirty = true;

    private void MarkDirty() => dirty = true;

    // Bir karede çok base değişse de (kayıt yükleme) tek sefer kurulur
    private void LateUpdate()
    {
        if (!dirty || GridManager.Instance == null) return;
        dirty = false;
        Rebuild();
    }

    private void Rebuild()
    {
        // Sütun → en alttaki base hücresinin y'si (dünya)
        var columns = new Dictionary<Vector2Int, int>();
        foreach (GridData data in GridManager.Instance.Grids.Values)
        {
            if (data.Base == null) continue;
            var column = new Vector2Int(data.WorldPosition.x, data.WorldPosition.z);
            if (!columns.TryGetValue(column, out int lowest) || data.WorldPosition.y < lowest)
                columns[column] = data.WorldPosition.y;
        }

        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var uvs = new List<Vector2>();
        float bottomY = WaterY - depthBelowWater;

        foreach (KeyValuePair<Vector2Int, int> pair in columns)
        {
            Vector2Int cell = pair.Key;
            float topY = pair.Value + topOffset;

            for (int s = 0; s < Sides.Length; s++)
            {
                Vector2Int outward = Sides[s];
                if (columns.ContainsKey(cell + outward)) continue;

                // Kenar boyunca iki yön: sağ ve sol uç
                Vector2Int along = new Vector2Int(outward.y, -outward.x);
                float endA = EndFlare(columns, cell, outward, -along);
                float endB = EndFlare(columns, cell, outward, along);
                AddSlope(vertices, triangles, uvs, cell, outward, along, topY, bottomY, endA, endB);
            }
        }

        mesh.Clear();
        mesh.SetVertices(ConvertToLocal(vertices));
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    // Parçanın alt kenarının o uçta ne kadar uzayacağı (+flare dış köşe, 0 düz, -flare iç köşe)
    private float EndFlare(Dictionary<Vector2Int, int> columns, Vector2Int cell, Vector2Int outward, Vector2Int direction)
    {
        Vector2Int next = cell + direction;
        if (!columns.ContainsKey(next)) return flare;
        return columns.ContainsKey(next + outward) ? -flare : 0f;
    }

    // Tek bir yana yamaç: üst kenar hücre kenarında (içeride), alt kenar dışarıda ve suyun altında
    private void AddSlope(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs,
                          Vector2Int cell, Vector2Int outward, Vector2Int along,
                          float topY, float bottomY, float endA, float endB)
    {
        Vector3 center = new Vector3(cell.x, 0f, cell.y);
        Vector3 outDir = new Vector3(outward.x, 0f, outward.y);
        Vector3 alongDir = new Vector3(along.x, 0f, along.y);

        Vector3 edgeCenter = center + outDir * (0.5f - inset);
        float halfTop = 0.5f - inset;

        Vector3 topA = edgeCenter - alongDir * halfTop + Vector3.up * topY;
        Vector3 topB = edgeCenter + alongDir * halfTop + Vector3.up * topY;

        Vector3 bottomCenter = center + outDir * (0.5f + flare);
        Vector3 bottomA = bottomCenter - alongDir * (0.5f + endA) + Vector3.up * bottomY;
        Vector3 bottomB = bottomCenter + alongDir * (0.5f + endB) + Vector3.up * bottomY;

        int start = vertices.Count;
        vertices.Add(topA);
        vertices.Add(topB);
        vertices.Add(bottomB);
        vertices.Add(bottomA);

        // Dünya uzayında UV: yan yana parçalarda doku kesintisiz akar
        foreach (Vector3 v in new[] { topA, topB, bottomB, bottomA })
            uvs.Add(new Vector2(Vector3.Dot(v, alongDir), v.y));

        // Dışarıdan bakınca görünen yüz (saat yönü)
        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);

        // Sarmal yönü yana göre değişir; normal dışarı bakmıyorsa ters çevir
        Vector3 normal = Vector3.Cross(topB - topA, bottomB - topA);
        if (Vector3.Dot(normal, outDir) < 0f)
        {
            int count = triangles.Count;
            (triangles[count - 5], triangles[count - 4]) = (triangles[count - 4], triangles[count - 5]);
            (triangles[count - 2], triangles[count - 1]) = (triangles[count - 1], triangles[count - 2]);
        }
    }

    private List<Vector3> ConvertToLocal(List<Vector3> worldVertices)
    {
        for (int i = 0; i < worldVertices.Count; i++)
            worldVertices[i] = transform.InverseTransformPoint(worldVertices[i]);
        return worldVertices;
    }
}
