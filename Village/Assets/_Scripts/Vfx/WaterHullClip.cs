using System;
using System.Collections.Generic;
using UnityEngine;

// Su, botların içinde çizilmesin. Her bot gövdesinin profilini verir (yükseklik katmanları × açı dilimleri, modelin
// uzayında: o yükseklikte merkeze en yakın duvarın uzaklığı). Su shader'ı (StylizedWater) her pikselde suyun kendi
// noktasını botun uzayına çevirir, profilin içindeyse çizmez. Ekrandaki izdüşüme değil suyun gerçek yerine
// bakıldığı için kameranın açısından bağımsız, ayar gerektirmez (eski görünmez kapak yandan bakınca taşıyordu).
// Profil "en yakın duvar": döşemenin üstünde iç duvar, altında dış duvar; ikisi de gövdenin dışına taşmaz.
public static class WaterHullClip
{
    // StylizedWater.shader'daki BOAT_* sabitleriyle aynı olmalı
    public const int MaxBoats = 4;
    public const int Levels = 8;
    public const int Sectors = 28; // MaxBoats × Levels × Sectors ≤ 1023 (Unity global dizi sınırı)

    public class Entry
    {
        public Transform Space;   // profilin uzayı (bot modeli; sallandıkça birlikte)
        public float[] Radii;     // Levels × Sectors
        public float Bottom;      // en alttaki katmanın yüksekliği (Space'in uzayında)
        public float Step;        // katmanlar arası yükseklik
    }

    private static readonly List<Entry> entries = new List<Entry>();
    private static readonly Matrix4x4[] matrices = new Matrix4x4[MaxBoats];
    private static readonly float[] radii = new float[MaxBoats * Levels * Sectors];
    private static readonly Vector4[] heights = new Vector4[MaxBoats];
    private static int uploadedFrame = -1;

    private static readonly int MatricesId = Shader.PropertyToID("_BoatWorldToLocal");
    private static readonly int RadiiId = Shader.PropertyToID("_BoatHullRadii");
    private static readonly int HeightsId = Shader.PropertyToID("_BoatHullHeights");
    private static readonly int CountId = Shader.PropertyToID("_BoatCount");

    // Önceki Play oturumundan kalan global değerler suyu boşuna kesmesin
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        entries.Clear();
        uploadedFrame = -1;
        Shader.SetGlobalFloat(CountId, 0f);
    }

    public static Entry Register(Transform space, float[] profile, float bottom, float step)
    {
        var entry = new Entry { Space = space, Radii = profile, Bottom = bottom, Step = step };
        entries.Add(entry);
        return entry;
    }

    public static void Unregister(Entry entry)
    {
        if (entry == null || !entries.Remove(entry)) return;
        uploadedFrame = -1;
        Upload(); // son bot gidince de suyun kesilmesi bitsin
    }

    // Her kare bir kez (botların LateUpdate'i çağırır; bot sallandıktan sonra)
    public static void Upload()
    {
        if (uploadedFrame == Time.frameCount) return;
        uploadedFrame = Time.frameCount;

        int count = 0;
        foreach (Entry entry in entries)
        {
            if (count >= MaxBoats) break;
            if (entry.Space == null || !entry.Space.gameObject.activeInHierarchy) continue;
            matrices[count] = entry.Space.worldToLocalMatrix;
            heights[count] = new Vector4(entry.Bottom, entry.Step, 0f, 0f);
            Array.Copy(entry.Radii, 0, radii, count * Levels * Sectors, Levels * Sectors);
            count++;
        }

        // Diziler hep tam boy gönderilir (Unity global dizinin boyunu ilk gönderimde sabitler)
        Shader.SetGlobalMatrixArray(MatricesId, matrices);
        Shader.SetGlobalFloatArray(RadiiId, radii);
        Shader.SetGlobalVectorArray(HeightsId, heights);
        Shader.SetGlobalFloat(CountId, count);
    }

    // Gövde mesh'inden profil: her katmanda, o yüksekliğe yakın DUVAR vertex'leri (normali yataya yakın; döşeme ve
    // kenarın tepesi hariç) açı dilimlerine ayrılır, dilimde merkeze en yakını alınır. Boş dilimler komşuların
    // küçüğüyle dolar; hiç duvar yoksa 0 (o katmanda su kesilmez). toSpace: mesh uzayından profil uzayına.
    public static float[] MeasureProfile(Mesh mesh, Matrix4x4 toSpace, float bottom, float step)
    {
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        bool hasNormals = normals != null && normals.Length == vertices.Length;
        var profile = new float[Levels * Sectors];
        var sector = new float[Sectors];
        float band = step * 0.6f;

        for (int level = 0; level < Levels; level++)
        {
            float height = bottom + level * step;
            for (int s = 0; s < Sectors; s++) sector[s] = float.MaxValue;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = toSpace.MultiplyPoint3x4(vertices[i]);
                if (Mathf.Abs(p.y - height) > band) continue;
                if (hasNormals && Mathf.Abs(toSpace.MultiplyVector(normals[i]).normalized.y) > 0.7f) continue;
                float r = new Vector2(p.x, p.z).magnitude;
                if (r < 0.0001f) continue;
                int s = Mathf.FloorToInt(Mathf.Repeat(Mathf.Atan2(p.z, p.x) / (Mathf.PI * 2f), 1f) * Sectors) % Sectors;
                sector[s] = Mathf.Min(sector[s], r);
            }

            for (int s = 0; s < Sectors; s++)
            {
                float r = sector[s];
                if (r == float.MaxValue)
                {
                    float left = float.MaxValue, right = float.MaxValue;
                    for (int k = 1; k < Sectors && (left == float.MaxValue || right == float.MaxValue); k++)
                    {
                        if (left == float.MaxValue) left = sector[(s - k + Sectors) % Sectors];
                        if (right == float.MaxValue) right = sector[(s + k) % Sectors];
                    }
                    r = Mathf.Min(left, right);
                }
                profile[level * Sectors + s] = r == float.MaxValue ? 0f : r;
            }
        }
        return profile;
    }
}
