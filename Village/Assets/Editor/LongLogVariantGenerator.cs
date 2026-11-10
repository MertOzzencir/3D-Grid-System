using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Village → Uzun Kütük Parçalarını Üret (kullanıcı 2026-11-10: odunlarda dual grid, şimdiki görünüşle): 1×1 odunun
// mesh'inden 4 hal üretir (iki ucu kapalı = orijinal, alt ucu açık, üst ucu açık, iki ucu açık) ve WoodCatalog'a yazar.
// Açık uç: uçtaki halkanın (gövdenin pahtan / spiralli kapaktan önceki son köşe halkası) ötesindeki üçgenler silinir,
// halkanın her kenarı hücre sınırına (±0.5) düz uzatılır. Sınırdaki halka bütün parçalarda aynı: modelin üst ve alt uç
// halkalarının (aynı sayıda, aynı açılarda köşe) köşe köşe ortalaması. Böylece uç uca iki parça dikişsiz birleşir.
// Yeni yüzler halkadaki köşenin UV'sini (doku boyuna uzar) ve yataylaştırılmış normalini alır (sert kenarlar korunur).
// Hesap 1×1 odunun prefab'ındaki duruşunda (eksen = yukarı) yapılır, sonuç mesh'in kendi uzayına geri çevrilir:
// MergedWood sadece mesh'i değiştirir, transform aynı kalır. Model değişince tekrar çalıştır.
public static class LongLogVariantGenerator
{
    private const string CatalogPath = "Assets/SOData/WoodCatalog.asset";
    private const float RingSearch = 0.35f; // uç halkası bu yüksekliğin altındaki en uç köşe halkası
    private const float Epsilon = 0.015f;

    [MenuItem("Village/Uzun Kütük Parçalarını Üret")]
    public static void Generate()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<WoodCatalogSO>(CatalogPath);
        Wood wood = catalog != null ? catalog.GetWood(1) : null;
        MeshFilter filter = wood != null && wood.Visual != null ? wood.Visual.GetComponentInChildren<MeshFilter>() : null;
        if (filter == null || filter.sharedMesh == null)
        {
            EditorUtility.DisplayDialog("Uzun kütük", "WoodCatalog'da 1×1 odun ya da görselinde mesh bulunamadı.", "Tamam");
            return;
        }

        Mesh source = filter.sharedMesh;
        if (!source.isReadable && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(source)) is ModelImporter importer)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
            filter = wood.Visual.GetComponentInChildren<MeshFilter>();
            source = filter.sharedMesh;
        }

        // Odunun kökü (= MergedWood'daki parça tutucusu) ↔ mesh uzayı
        Matrix4x4 meshToHolder = wood.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
        Matrix4x4 holderToMesh = meshToHolder.inverse;

        var vertices = new List<Vector3>();
        foreach (Vector3 v in source.vertices) vertices.Add(meshToHolder.MultiplyPoint3x4(v));
        float topRing = float.MinValue, bottomRing = float.MaxValue;
        foreach (Vector3 v in vertices)
        {
            if (v.y < RingSearch) topRing = Mathf.Max(topRing, v.y);
            if (v.y > -RingSearch) bottomRing = Mathf.Min(bottomRing, v.y);
        }
        List<Vector2> top = RingPoints(vertices, topRing), bottom = RingPoints(vertices, bottomRing);
        if (top.Count != bottom.Count || top.Count < 3)
        {
            EditorUtility.DisplayDialog("Uzun kütük",
                $"Modelin üst ve alt uç halkası eşleşmiyor ({top.Count} / {bottom.Count} köşe). İki uçta gövde halkası aynı sayıda köşeli olmalı.", "Tamam");
            return;
        }
        List<Vector2> canonical = Canonical(top, bottom);

        // Eski haller (alt asset) silinip yenileri yazılır
        foreach (Object sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(CatalogPath))
            if (sub is Mesh) Object.DestroyImmediate(sub, true);
        var meshes = new Mesh[4];
        meshes[0] = source;
        for (int index = 1; index < 4; index++)
        {
            bool openBottom = (index & 1) != 0, openTop = (index & 2) != 0;
            Mesh variant = Build(source, meshToHolder, holderToMesh, openBottom, openTop, topRing, bottomRing, top, bottom, canonical);
            variant.name = $"{source.name} {(openBottom ? "open" : "cap")}-{(openTop ? "open" : "cap")}";
            AssetDatabase.AddObjectToAsset(variant, catalog);
            meshes[index] = variant;
        }
        catalog.SetPieceVariants(meshes);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Uzun kütük", "4 parça hali üretildi ve WoodCatalog'a yazıldı.", "Tamam");
    }

    // Halkanın tekil köşeleri (UV / sert kenar kopyaları bir sayılır), açıya göre sıralı, eksene dik düzlemde (x, z)
    private static List<Vector2> RingPoints(List<Vector3> vertices, float ringY)
    {
        var points = new List<Vector2>();
        foreach (Vector3 v in vertices)
        {
            if (Mathf.Abs(v.y - ringY) >= Epsilon) continue;
            var point = new Vector2(v.x, v.z);
            if (!points.Exists(p => (p - point).sqrMagnitude < 1e-8f)) points.Add(point);
        }
        points.Sort((a, b) => Angle(a).CompareTo(Angle(b)));
        return points;
    }

    // Sınır halkası: üst ve alt halkanın eşleşen köşelerinin ortası (eşleşme: toplam uzaklığı en az olan kaydırma)
    private static List<Vector2> Canonical(List<Vector2> top, List<Vector2> bottom)
    {
        int n = top.Count, bestShift = 0;
        float best = float.MaxValue;
        for (int shift = 0; shift < n; shift++)
        {
            float sum = 0f;
            for (int k = 0; k < n; k++) sum += (top[k] - bottom[(k + shift) % n]).sqrMagnitude;
            if (sum < best) { best = sum; bestShift = shift; }
        }
        var canonical = new List<Vector2>(n);
        for (int k = 0; k < n; k++) canonical.Add((top[k] + bottom[(k + bestShift) % n]) * 0.5f);
        // Alt halkanın köşeleri de aynı sıraya dizilir (Bridge iki halkada da aynı index'i kullanır)
        var reordered = new List<Vector2>(n);
        for (int k = 0; k < n; k++) reordered.Add(bottom[(k + bestShift) % n]);
        bottom.Clear();
        bottom.AddRange(reordered);
        return canonical;
    }

    private static Mesh Build(Mesh source, Matrix4x4 meshToHolder, Matrix4x4 holderToMesh, bool openBottom, bool openTop,
                              float topRing, float bottomRing, List<Vector2> top, List<Vector2> bottom, List<Vector2> canonical)
    {
        Vector3[] srcVertices = source.vertices;
        Vector3[] srcNormals = source.normals;
        Vector2[] srcUv = source.uv;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uv = new List<Vector2>();
        for (int i = 0; i < srcVertices.Length; i++)
        {
            vertices.Add(meshToHolder.MultiplyPoint3x4(srcVertices[i]));
            normals.Add(srcNormals.Length == srcVertices.Length ? meshToHolder.MultiplyVector(srcNormals[i]).normalized : Vector3.up);
            uv.Add(srcUv.Length == srcVertices.Length ? srcUv[i] : Vector2.zero);
        }

        var submeshes = new List<List<int>>();
        for (int s = 0; s < source.subMeshCount; s++)
        {
            int[] triangles = source.GetTriangles(s);
            var kept = new List<int>();
            for (int t = 0; t < triangles.Length; t += 3)
            {
                bool cut = false;
                for (int k = 0; k < 3; k++)
                {
                    float y = vertices[triangles[t + k]].y;
                    if ((openTop && y > topRing + Epsilon) || (openBottom && y < bottomRing - Epsilon)) cut = true;
                }
                if (!cut) kept.AddRange(new[] { triangles[t], triangles[t + 1], triangles[t + 2] });
            }
            submeshes.Add(kept);
        }

        if (openTop) Bridge(vertices, normals, uv, submeshes, topRing, 0.5f, top, canonical);
        if (openBottom) Bridge(vertices, normals, uv, submeshes, bottomRing, -0.5f, bottom, canonical);

        var mesh = new Mesh();
        var outVertices = new List<Vector3>(vertices.Count);
        var outNormals = new List<Vector3>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++)
        {
            outVertices.Add(holderToMesh.MultiplyPoint3x4(vertices[i]));
            outNormals.Add(holderToMesh.MultiplyVector(normals[i]).normalized);
        }
        mesh.SetVertices(outVertices);
        mesh.SetNormals(outNormals);
        mesh.SetUVs(0, uv);
        mesh.subMeshCount = submeshes.Count;
        for (int s = 0; s < submeshes.Count; s++) mesh.SetTriangles(submeshes[s], s);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Kesilen uçtaki halka kenarlarının (iki köşesi de halka yüksekliğinde olan kenarlar) her biri sınırdaki ortak halkaya
    // bir dörtgenle uzatılır. Her halka köşesi kopyası (UV / normal) kendi sınır köşesini alır: dikişler ve sert kenarlar korunur.
    private static void Bridge(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv, List<List<int>> submeshes,
                               float ringY, float endY, List<Vector2> ring, List<Vector2> canonical)
    {
        foreach (List<int> list in submeshes)
        {
            var edges = new HashSet<(int, int)>();
            for (int t = 0; t < list.Count; t += 3)
                for (int k = 0; k < 3; k++)
                {
                    int i = list[t + k], j = list[t + (k + 1) % 3];
                    if (Mathf.Abs(vertices[i].y - ringY) < Epsilon && Mathf.Abs(vertices[j].y - ringY) < Epsilon)
                        edges.Add((Mathf.Min(i, j), Mathf.Max(i, j)));
                }

            var end = new Dictionary<int, int>();
            int EndOf(int i)
            {
                if (end.TryGetValue(i, out int e)) return e;
                Vector2 point = canonical[Nearest(ring, new Vector2(vertices[i].x, vertices[i].z))];
                Vector3 normal = normals[i];
                normal.y = 0f;
                e = vertices.Count;
                vertices.Add(new Vector3(point.x, endY, point.y));
                normals.Add(normal.sqrMagnitude > 1e-6f ? normal.normalized : new Vector3(point.x, 0f, point.y).normalized);
                uv.Add(uv[i]);
                end[i] = e;
                return e;
            }

            foreach ((int i, int j) in edges)
            {
                int ei = EndOf(i), ej = EndOf(j);
                AddOutward(list, vertices, i, j, ej);
                AddOutward(list, vertices, i, ej, ei);
            }
        }
    }

    private static int Nearest(List<Vector2> ring, Vector2 point)
    {
        int nearest = 0;
        for (int k = 1; k < ring.Count; k++)
            if ((ring[k] - point).sqrMagnitude < (ring[nearest] - point).sqrMagnitude) nearest = k;
        return nearest;
    }

    // Üçgeni dışa bakacak sırayla ekler (Unity: önden bakınca saat yönü, Cross(b-a, c-a) bakana doğru)
    private static void AddOutward(List<int> list, List<Vector3> vertices, int i0, int i1, int i2)
    {
        Vector3 a = vertices[i0], b = vertices[i1], c = vertices[i2];
        Vector3 normal = Vector3.Cross(b - a, c - a);
        Vector3 center = (a + b + c) / 3f;
        var outward = new Vector3(center.x, 0f, center.z);
        if (Vector3.Dot(normal, outward) > 0f) list.AddRange(new[] { i0, i1, i2 });
        else list.AddRange(new[] { i0, i2, i1 });
    }

    private static float Angle(Vector2 v) => Mathf.Atan2(v.y, v.x);
}
