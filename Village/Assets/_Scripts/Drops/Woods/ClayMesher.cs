using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Kutuları kaynaşmış tek bir kil şekline çeviren hesap (Tiny Glade / Dreams yolu). Kutular kenarları yuvarlatılmış
// kabul edilir; aynı kesitte uç uca değenler önce tek kutuya birleştirilir (yoksa birleşim yerinde oluk kalıyordu),
// sonra mesafe alanı (SDF, yumuşak birleşim: iki hamuru bastırmak gibi) ızgarada örneklenip tek mesh çıkarılır
// (surface nets). Kutular ve sonuç aynı uzayda (çağıranın yerel uzayı). Bir kez çağrılır (örn. MergedWood kurulurken).
public static class ClayMesher
{
    public struct Part
    {
        public Vector3 center;
        public Vector3 half;
    }

    // boxes: kutular (çağıranın uzayında); voxelSize: ızgara aralığı; cornerRadius: kenar yuvarlaklığı; blend: kaynaşma
    public static Mesh Build(List<Bounds> boxes, float voxelSize, float cornerRadius, float blend, float inset = 0f)
    {
        var parts = new List<Part>();
        foreach (Bounds box in boxes)
            parts.Add(new Part { center = box.center, half = Vector3.Max(box.extents - Vector3.one * inset, Vector3.one * 0.02f) });
        if (parts.Count == 0) return null;
        MergeBoxes(parts);
        return BuildMesh(parts, voxelSize, cornerRadius, blend);
    }

    // Aynı kesitte, uç uca değen kutular tek kutu olur (uzun kirişin 1'er birimlik parçaları, üst üste katlar): iki
    // yuvarlak kutu yan yana gelince yuvarlak kenarları birleşim yerinde oluk bırakıyordu
    public static void MergeBoxes(List<Part> parts)
    {
        const float eps = 0.03f;
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int a = 0; a < parts.Count && !changed; a++)
                for (int b = a + 1; b < parts.Count && !changed; b++)
                    for (int axis = 0; axis < 3 && !changed; axis++)
                    {
                        Part pa = parts[a], pb = parts[b];
                        bool sameSection = true;
                        for (int other = 0; other < 3; other++)
                        {
                            if (other == axis) continue;
                            if (Mathf.Abs(pa.center[other] - pb.center[other]) > eps || Mathf.Abs(pa.half[other] - pb.half[other]) > eps)
                                sameSection = false;
                        }
                        if (!sameSection) continue;
                        float gap = Mathf.Abs(pa.center[axis] - pb.center[axis]) - pa.half[axis] - pb.half[axis];
                        if (gap > eps) continue;

                        float min = Mathf.Min(pa.center[axis] - pa.half[axis], pb.center[axis] - pb.half[axis]);
                        float max = Mathf.Max(pa.center[axis] + pa.half[axis], pb.center[axis] + pb.half[axis]);
                        Vector3 center = pa.center, half = pa.half;
                        center[axis] = (min + max) * 0.5f;
                        half[axis] = (max - min) * 0.5f;
                        parts[a] = new Part { center = center, half = half };
                        parts.RemoveAt(b);
                        changed = true;
                    }
        }
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    // --- Mesafe alanı: yuvarlak kutuların yumuşak birleşimi (eksi = içeride) ---

    private static float Sdf(List<Part> parts, Vector3 p, float cornerRadius, float blend)
    {
        float d = float.MaxValue;
        foreach (Part part in parts)
        {
            float r = Mathf.Min(cornerRadius, Mathf.Min(part.half.x, Mathf.Min(part.half.y, part.half.z)) * 0.95f);
            Vector3 q = Abs(p - part.center) - (part.half - Vector3.one * r);
            float box = Vector3.Max(q, Vector3.zero).magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f) - r;
            d = d == float.MaxValue ? box : SmoothMin(d, box, blend);
        }
        return d;
    }

    private static float SmoothMin(float a, float b, float k)
    {
        if (k <= 0f) return Mathf.Min(a, b);
        float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
        return Mathf.Lerp(b, a, h) - k * h * (1f - h);
    }

    // --- Surface nets: mesafe alanını ızgarada örnekle, işaret değişen her küpe bir vertex, kenarlara dörtgen ---

    private static Mesh BuildMesh(List<Part> parts, float voxelSize, float cornerRadius, float blend)
    {
        Bounds area = new Bounds(parts[0].center, parts[0].half * 2f);
        foreach (Part part in parts) area.Encapsulate(new Bounds(part.center, part.half * 2f));
        area.Expand(blend * 2f + voxelSize * 4f);

        float step = Mathf.Max(0.02f, voxelSize);
        int nx = Mathf.CeilToInt(area.size.x / step) + 1, ny = Mathf.CeilToInt(area.size.y / step) + 1, nz = Mathf.CeilToInt(area.size.z / step) + 1;
        if ((long)nx * ny * nz > 3_000_000)
        {
            Debug.LogWarning($"ClayMesher: şekil çok büyük ({nx}×{ny}×{nz}); Voxel Size'ı büyüt.");
            return null;
        }
        Vector3 origin = area.min;
        int Index(int x, int y, int z) => x + nx * (y + ny * z);

        var values = new float[nx * ny * nz];
        for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                    values[Index(x, y, z)] = Sdf(parts, origin + new Vector3(x, y, z) * step, cornerRadius, blend);

        // Her küp (x..x+1) için vertex: işaret değişen kenarların kesişim noktalarının ortalaması
        var cellVertex = new int[nx * ny * nz];
        for (int i = 0; i < cellVertex.Length; i++) cellVertex[i] = -1;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        int[,] edges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
        var corner = new float[8];
        var cornerPos = new Vector3[8];
        for (int z = 0; z < nz - 1; z++)
            for (int y = 0; y < ny - 1; y++)
                for (int x = 0; x < nx - 1; x++)
                {
                    int inside = 0;
                    for (int c = 0; c < 8; c++)
                    {
                        int cx = x + (c & 1), cy = y + ((c >> 1) & 1), cz = z + ((c >> 2) & 1);
                        corner[c] = values[Index(cx, cy, cz)];
                        cornerPos[c] = origin + new Vector3(cx, cy, cz) * step;
                        if (corner[c] < 0f) inside++;
                    }
                    if (inside == 0 || inside == 8) continue;

                    Vector3 sum = Vector3.zero;
                    int count = 0;
                    for (int e = 0; e < 12; e++)
                    {
                        float a = corner[edges[e, 0]], b = corner[edges[e, 1]];
                        if ((a < 0f) == (b < 0f)) continue;
                        float t = a / (a - b);
                        sum += Vector3.Lerp(cornerPos[edges[e, 0]], cornerPos[edges[e, 1]], t);
                        count++;
                    }
                    Vector3 v = sum / count;
                    cellVertex[Index(x, y, z)] = vertices.Count;
                    vertices.Add(v);
                    normals.Add(Gradient(parts, v, step * 0.5f, cornerRadius, blend));
                }

        // İşaret değişen her ızgara kenarı için onu çevreleyen 4 küpün vertex'lerinden dörtgen
        var triangles = new List<int>();
        void Quad(int a, int b, int c, int d, bool flip)
        {
            if (a < 0 || b < 0 || c < 0 || d < 0) return;
            if (flip) { triangles.Add(a); triangles.Add(c); triangles.Add(b); triangles.Add(a); triangles.Add(d); triangles.Add(c); }
            else { triangles.Add(a); triangles.Add(b); triangles.Add(c); triangles.Add(a); triangles.Add(c); triangles.Add(d); }
        }
        for (int z = 1; z < nz - 1; z++)
            for (int y = 1; y < ny - 1; y++)
                for (int x = 0; x < nx - 1; x++)
                {
                    bool a = values[Index(x, y, z)] < 0f, b = values[Index(x + 1, y, z)] < 0f;
                    if (a == b) continue;
                    Quad(cellVertex[Index(x, y - 1, z - 1)], cellVertex[Index(x, y, z - 1)], cellVertex[Index(x, y, z)], cellVertex[Index(x, y - 1, z)], !a);
                }
        for (int z = 1; z < nz - 1; z++)
            for (int y = 0; y < ny - 1; y++)
                for (int x = 1; x < nx - 1; x++)
                {
                    bool a = values[Index(x, y, z)] < 0f, b = values[Index(x, y + 1, z)] < 0f;
                    if (a == b) continue;
                    Quad(cellVertex[Index(x - 1, y, z - 1)], cellVertex[Index(x - 1, y, z)], cellVertex[Index(x, y, z)], cellVertex[Index(x, y, z - 1)], !a);
                }
        for (int z = 0; z < nz - 1; z++)
            for (int y = 1; y < ny - 1; y++)
                for (int x = 1; x < nx - 1; x++)
                {
                    bool a = values[Index(x, y, z)] < 0f, b = values[Index(x, y, z + 1)] < 0f;
                    if (a == b) continue;
                    Quad(cellVertex[Index(x - 1, y - 1, z)], cellVertex[Index(x, y - 1, z)], cellVertex[Index(x, y, z)], cellVertex[Index(x - 1, y, z)], !a);
                }
        if (triangles.Count == 0) return null;

        // Yönü garanti et: üçgenin yüzü mesafe alanının dışa yönüyle (normal) aynı tarafa baksın
        for (int i = 0; i < triangles.Count; i += 3)
        {
            Vector3 p0 = vertices[triangles[i]], p1 = vertices[triangles[i + 1]], p2 = vertices[triangles[i + 2]];
            Vector3 face = Vector3.Cross(p1 - p0, p2 - p0);
            Vector3 outward = normals[triangles[i]] + normals[triangles[i + 1]] + normals[triangles[i + 2]];
            if (Vector3.Dot(face, outward) < 0f) (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
        }

        var mesh = new Mesh { name = "Clay Wood" };
        if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Vector3 Gradient(List<Part> parts, Vector3 p, float h, float cornerRadius, float blend)
    {
        Vector3 g = new Vector3(
            Sdf(parts, p + Vector3.right * h, cornerRadius, blend) - Sdf(parts, p - Vector3.right * h, cornerRadius, blend),
            Sdf(parts, p + Vector3.up * h, cornerRadius, blend) - Sdf(parts, p - Vector3.up * h, cornerRadius, blend),
            Sdf(parts, p + Vector3.forward * h, cornerRadius, blend) - Sdf(parts, p - Vector3.forward * h, cornerRadius, blend));
        return g.sqrMagnitude > 1e-10f ? g.normalized : Vector3.up;
    }
}
