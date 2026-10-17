using System.Collections.Generic;
using UnityEngine;

// Bağlantı ipinin sarılma animasyonu (WoodRope kurar, MergedWood birleştirme anında Play'i çağırır). Sadece birleştirmede
// eklenen kenarlar oynar: her örnek noktası (ip yolundaki merkez) en yakın kenar yeniyse yenidir; bir zincirdeki art
// arda yeni noktalar bir "koşu" olur ve eski ipe bağlandığı uçtan uzar (koşunun içindeki yeri 0..1, ilerleyen uçtan
// öndeyse merkezine çekilir: görünmez, uca yakınken incelir: sivri uç). Eski ip sabit. Koşular bitince yeni düğümler
// zıplayarak (ease-out-back) büyür. Bittiğinde son hali yazılıp bileşen kapanır; oynatılmazsa ip hazır görünür.
public class RopeGrow : MonoBehaviour
{
    private const float TipLength = 0.08f; // ilerleyen ucun sivrildiği uzunluk (koşuya oranla)
    private const float Static = -2f, Knot = -1f;

    private Mesh mesh;
    private Vector3[] final;
    private Vector3[] centers;
    private int[] chains;      // düğümde -1
    private float[] distances; // zincir boyunca
    private float[] progress;  // koşu içindeki yer (0..1), Knot ya da Static
    private Vector3[] buffer;
    private float growDuration, delay, knotDuration;
    private float time;

    public void Setup(Mesh mesh, Vector3[] centers, int[] chains, float[] distances, float growDuration, float delay, float knotDuration)
    {
        this.mesh = mesh;
        final = mesh.vertices;
        this.centers = centers;
        this.chains = chains;
        this.distances = distances;
        this.growDuration = Mathf.Max(growDuration, 0.01f);
        this.delay = Mathf.Max(delay, 0f);
        this.knotDuration = Mathf.Max(knotDuration, 0.01f);
        enabled = false;
    }

    // added: birleştirmede eklenen kenarlar, existing: önceden olan kenarlar (ipin uzayında, uç noktalarıyla)
    public void Play(List<(Vector3 a, Vector3 b)> added, List<(Vector3 a, Vector3 b)> existing)
    {
        if (mesh == null || final.Length != centers.Length || added.Count == 0) return;
        progress = new float[final.Length];
        bool IsNew(Vector3 p) => Nearest(added, p) < Nearest(existing, p) - 1e-4f;

        // Örnek noktaları: vertex'ler zincir zincir, nokta nokta sırayla (aynı noktanın halkası aynı merkezde)
        var samples = new List<(int first, int count, float distance, bool isNew)>();
        int i = 0;
        while (i < final.Length)
        {
            int chain = chains[i];
            if (chain < 0)
            {
                // Düğüm: yeni bir kenara değiyorsa (eskiyle eşitse de: kollara yeni ayrılan köşe) zıplayarak gelir
                progress[i] = Nearest(added, centers[i]) <= Nearest(existing, centers[i]) + 1e-4f ? Knot : Static;
                i++;
                continue;
            }
            samples.Clear();
            while (i < final.Length && chains[i] == chain)
            {
                int first = i;
                float distance = distances[i];
                while (i < final.Length && chains[i] == chain && distances[i] == distance) i++;
                samples.Add((first, i - first, distance, IsNew(centers[first])));
            }
            // Koşular: art arda yeni noktalar; zincirin sonuna kadar gidip başında eski ipe bağlıysa sondan başa uzar
            int k = 0;
            while (k < samples.Count)
            {
                if (!samples[k].isNew)
                {
                    SetProgress(samples[k], Static);
                    k++;
                    continue;
                }
                int runStart = k;
                while (k < samples.Count && samples[k].isNew) k++;
                int runEnd = k - 1;
                float from = samples[runStart].distance, to = samples[runEnd].distance;
                float length = Mathf.Max(to - from, 0.0001f);
                bool reverse = runStart == 0 && runEnd < samples.Count - 1; // başta, sonu eski ipe bağlı: sondan uzar
                for (int s = runStart; s <= runEnd; s++)
                {
                    float x = (samples[s].distance - from) / length;
                    SetProgress(samples[s], reverse ? 1f - x : x);
                }
            }
        }

        buffer ??= new Vector3[final.Length];
        time = 0f;
        enabled = true;
        Apply(); // ilk karede yeni ip görünmesin
    }

    private void SetProgress((int first, int count, float distance, bool isNew) sample, float value)
    {
        for (int v = sample.first; v < sample.first + sample.count; v++) progress[v] = value;
    }

    // Noktanın kenarlardan en yakınına uzaklığı
    private static float Nearest(List<(Vector3 a, Vector3 b)> segments, Vector3 p)
    {
        float best = float.MaxValue;
        foreach ((Vector3 a, Vector3 b) in segments)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
            best = Mathf.Min(best, (a + ab * t - p).sqrMagnitude);
        }
        return best == float.MaxValue ? float.MaxValue : Mathf.Sqrt(best);
    }

    private void Update()
    {
        time += Time.deltaTime;
        Apply();
        if (time >= delay + growDuration + knotDuration)
        {
            mesh.SetVertices(final);
            enabled = false;
        }
    }

    private void Apply()
    {
        float t = Mathf.Max(0f, time - delay);
        float reveal = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / growDuration)) * (1f + TipLength);
        float knot = EaseOutBack(Mathf.Clamp01((t - growDuration) / knotDuration));
        for (int i = 0; i < final.Length; i++)
        {
            float p = progress[i];
            float scale = p == Static ? 1f : p == Knot ? knot : Mathf.Clamp01((reveal - p) / TipLength);
            buffer[i] = centers[i] + (final[i] - centers[i]) * scale;
        }
        mesh.SetVertices(buffer);
    }

    // 0'dan 1'e, biraz aşıp geri gelir (zıplama)
    private static float EaseOutBack(float x)
    {
        if (x <= 0f) return 0f;
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}
