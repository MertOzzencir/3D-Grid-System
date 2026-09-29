using UnityEngine;
using UnityEngine.Rendering;

// Su için sık bir düz grid mesh üretir. Dalgalar shader'da vertex kaydırılarak yapıldığı için (StylizedWater, Gerstner)
// dalga boyuna göre yeterince sık vertex gerekir; FBX'teki seyrek düzlemde dalga şekli oluşmaz.
// Mesh her açılışta yeniden üretilir, sahneye/asset'e kaydedilmez. Pivot ortada, düzlem y = 0'da.
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WaterSurface : MonoBehaviour
{
    [Tooltip("Suyun genişliği ve uzunluğu (dünya birimi)")]
    [SerializeField] private Vector2 size = new Vector2(80f, 80f);
    [Tooltip("Vertex aralığı. Dalga boyunun en az ~1/8'i olmalı (dalga boyu 6 ise 0.5 ya da daha küçük).")]
    [SerializeField, Range(0.1f, 2f)] private float cellSize = 0.35f;
    [Tooltip("Dalgalar yukarı/aşağı taşar; kamera kenardayken su kesilmesin diye bounds bu kadar büyütülür")]
    [SerializeField] private float boundsPadding = 2f;

    private Mesh mesh;

    private void OnEnable() => Build();

#if UNITY_EDITOR
    // OnValidate içinde mesh değiştirmek uyarı verdiriyor, bir sonraki editör karesine erteleniyor
    private void OnValidate() => UnityEditor.EditorApplication.delayCall += () => { if (this != null) Build(); };
#endif

    private void OnDisable()
    {
        if (mesh == null) return;
        if (Application.isPlaying) Destroy(mesh);
        else DestroyImmediate(mesh);
        mesh = null;
    }

    private void Build()
    {
        int countX = Mathf.Max(1, Mathf.CeilToInt(size.x / cellSize));
        int countZ = Mathf.Max(1, Mathf.CeilToInt(size.y / cellSize));

        var vertices = new Vector3[(countX + 1) * (countZ + 1)];
        var normals = new Vector3[vertices.Length];
        var uvs = new Vector2[vertices.Length];
        for (int z = 0, i = 0; z <= countZ; z++)
            for (int x = 0; x <= countX; x++, i++)
            {
                float u = (float)x / countX;
                float v = (float)z / countZ;
                vertices[i] = new Vector3((u - 0.5f) * size.x, 0f, (v - 0.5f) * size.y);
                normals[i] = Vector3.up;
                uvs[i] = new Vector2(u, v);
            }

        var triangles = new int[countX * countZ * 6];
        for (int z = 0, t = 0; z < countZ; z++)
            for (int x = 0; x < countX; x++, t += 6)
            {
                int a = z * (countX + 1) + x;
                int b = a + countX + 1;
                triangles[t] = a;
                triangles[t + 1] = b;
                triangles[t + 2] = a + 1;
                triangles[t + 3] = a + 1;
                triangles[t + 4] = b;
                triangles[t + 5] = b + 1;
            }

        if (mesh == null)
            mesh = new Mesh { name = "Water Surface (üretilen)", hideFlags = HideFlags.DontSave };
        mesh.Clear();
        mesh.indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(size.x + boundsPadding * 2f, boundsPadding * 2f, size.y + boundsPadding * 2f));

        GetComponent<MeshFilter>().sharedMesh = mesh;
    }
}
