using UnityEngine;

// Bağlantı ipinin sarılma animasyonu (WoodRope kurar, MergedWood birleştirme anında Play'i çağırır). Her zincir
// başından uzar: vertex'in zincirdeki yeri (0..1) ilerleyen uçtan önündeyse ip yolundaki merkezine çekilir (görünmez),
// uca yakınken incelir (sivri uç). Zincirler bitince düğümler zıplayarak (ease-out-back) büyür. Bittiğinde son hali
// yazılıp bileşen kapanır; oynatılmazsa hiçbir şey yapmaz (ip hazır görünür).
public class RopeGrow : MonoBehaviour
{
    private const float TipLength = 0.08f; // ilerleyen ucun sivrildiği uzunluk (zincire oranla)

    private Mesh mesh;
    private Vector3[] final;
    private Vector3[] centers;
    private float[] progress; // düğümde -1
    private Vector3[] buffer;
    private float growDuration, delay, knotDuration;
    private float time;

    public void Setup(Mesh mesh, Vector3[] centers, float[] progress, float growDuration, float delay, float knotDuration)
    {
        this.mesh = mesh;
        final = mesh.vertices;
        this.centers = centers;
        this.progress = progress;
        this.growDuration = Mathf.Max(growDuration, 0.01f);
        this.delay = Mathf.Max(delay, 0f);
        this.knotDuration = Mathf.Max(knotDuration, 0.01f);
        enabled = false;
    }

    public void Play()
    {
        if (mesh == null || final.Length != centers.Length) return;
        buffer ??= new Vector3[final.Length];
        time = 0f;
        enabled = true;
        Apply(); // ilk karede ip görünmesin
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
            float scale = progress[i] < 0f ? knot : Mathf.Clamp01((reveal - progress[i]) / TipLength);
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
