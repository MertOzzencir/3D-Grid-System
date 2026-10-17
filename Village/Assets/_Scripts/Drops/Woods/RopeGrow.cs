using System;
using System.Collections.Generic;
using UnityEngine;

// Bağlantı ipinin sarılma animasyonu (WoodRope kurar, MergedWood birleştirme anında Play'i çağırır). Sadece birleştirmede
// eklenen kenarlar oynar: her örnek noktası (ip yolundaki merkez) en yakın kenar yeniyse yenidir; bir zincirdeki art
// arda yeni noktalar bir "koşu" olur ve eski ipe bağlandığı uçtan uzar. Eski ip sabit.
// - Koşular sırayla başlar (stagger), her biri uzunluğuna göre sabit hızla (growSpeed), sona doğru yavaşlayarak uzar.
// - İlerleyen ucun önündeki noktalar ip yolundaki merkezine çekilir (görünmez); uç hafif şişkin (çekilen ipin ucu).
// - Açılan ip önce gevşek (loose kadar kalın), sonra yaylanarak (sönümlü salınım) odunun üstüne sıkıca oturur.
// - Düğümler kendilerine bağlanan koşu bitince zıplayarak (ease-out-back) büyür, etrafa ip lifleri saçılır.
// Bittiğinde son hali yazılıp bileşen kapanır; oynatılmazsa ip hazır görünür.
public class RopeGrow : MonoBehaviour
{
    [Serializable]
    public struct Settings
    {
        public float speed;        // uzama hızı (birim / saniye)
        public float delay;        // birleştirmeden sonra bekleme
        public float stagger;      // koşular arası
        public float loose;        // açılırken kalınlık fazlası (oran)
        public float knotDuration; // düğüm zıplama süresi
        public int fibers;         // düğüm başına saçılan lif
        public Material material;  // ipin malzemesi (lifler için)
    }

    private const float Static = -2f, Knot = -1f;
    private const float TipLength = 0.1f;  // ilerleyen ucun sivrilip şiştiği uzunluk (birim)
    private const float Settle = 0.6f;     // gevşekten sıkıya oturma süresi (saniye, kabaca)

    private Mesh mesh;
    private Vector3[] final;
    private Vector3[] centers;
    private int[] chains;      // düğümde -1
    private float[] distances; // zincir boyunca
    private Settings settings;

    // Play'de hesaplanan: vertex başına koşu (düğümde düğüm indeksi) ve koşudaki yeri (birim)
    private int[] runOf;
    private float[] along;
    private readonly List<(float start, float duration, float length)> runs = new List<(float, float, float)>();
    private readonly List<(Vector3 center, float time, bool popped)> knots = new List<(Vector3, float, bool)>();
    private Vector3[] buffer;
    private float time, endTime;
    private ParticleSystem fiberSystem;
    private float[] fronts; // koşuların ilerleyen ucu (birim)

    public void Setup(Mesh mesh, Vector3[] centers, int[] chains, float[] distances, Settings settings)
    {
        this.mesh = mesh;
        final = mesh.vertices;
        this.centers = centers;
        this.chains = chains;
        this.distances = distances;
        this.settings = settings;
        enabled = false;
    }

    // added: birleştirmede eklenen kenarlar, existing: önceden olan kenarlar (ipin uzayında, uç noktalarıyla)
    public void Play(List<(Vector3 a, Vector3 b)> added, List<(Vector3 a, Vector3 b)> existing)
    {
        if (mesh == null || final.Length != centers.Length || added.Count == 0) return;
        runOf = new int[final.Length];
        along = new float[final.Length];
        runs.Clear();
        knots.Clear();
        bool IsNew(Vector3 p) => Nearest(added, p) < Nearest(existing, p) - 1e-4f;
        float speed = Mathf.Max(settings.speed, 0.1f);
        var runEnds = new List<Vector3>(); // koşuların bittiği nokta (düğüm zamanı için)

        // Örnek noktaları: vertex'ler zincir zincir, nokta nokta sırayla (aynı noktanın halkası aynı merkezde)
        var samples = new List<(int first, int count, float distance, bool isNew)>();
        var knotVertices = new List<int>();
        int i = 0;
        while (i < final.Length)
        {
            int chain = chains[i];
            if (chain < 0)
            {
                knotVertices.Add(i);
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
            // Koşular: art arda yeni noktalar; zincirin başında ve sonu eski ipe bağlıysa sondan başa uzar
            int k = 0;
            while (k < samples.Count)
            {
                if (!samples[k].isNew)
                {
                    Mark(samples[k], -1, Static);
                    k++;
                    continue;
                }
                int runStart = k;
                while (k < samples.Count && samples[k].isNew) k++;
                int runEnd = k - 1;
                float from = samples[runStart].distance, to = samples[runEnd].distance;
                float length = Mathf.Max(to - from, 0.0001f);
                bool reverse = runStart == 0 && runEnd < samples.Count - 1;
                int run = runs.Count;
                float start = settings.delay + run * settings.stagger;
                runs.Add((start, Mathf.Max(length / speed, 0.15f), length));
                for (int s = runStart; s <= runEnd; s++)
                {
                    float x = samples[s].distance - from;
                    Mark(samples[s], run, reverse ? length - x : x);
                }
                runEnds.Add(centers[samples[reverse ? runStart : runEnd].first]);
            }
        }

        // Düğümler: yeni bir kenara değiyorsa (eskiyle eşitse de: kollara yeni ayrılan köşe) en yakın koşu bitince zıplar
        float lastRunEnd = 0f;
        foreach (var run in runs) lastRunEnd = Mathf.Max(lastRunEnd, run.start + run.duration);
        var knotIndex = new Dictionary<Vector3, int>();
        foreach (int v in knotVertices)
        {
            Vector3 center = centers[v];
            if (Nearest(added, center) > Nearest(existing, center) + 1e-4f)
            {
                runOf[v] = -1;
                along[v] = Static;
                continue;
            }
            if (!knotIndex.TryGetValue(center, out int index))
            {
                float when = lastRunEnd;
                float best = float.MaxValue;
                for (int r = 0; r < runEnds.Count; r++)
                {
                    float d = (runEnds[r] - center).sqrMagnitude;
                    if (d < best && d < 0.09f)
                    {
                        best = d;
                        when = runs[r].start + runs[r].duration;
                    }
                }
                index = knots.Count;
                knots.Add((center, when, false));
                knotIndex[center] = index;
            }
            runOf[v] = index;
            along[v] = Knot;
        }

        endTime = lastRunEnd + settings.knotDuration + Settle;
        foreach (var knot in knots) endTime = Mathf.Max(endTime, knot.time + settings.knotDuration + 0.1f);
        buffer ??= new Vector3[final.Length];
        time = 0f;
        enabled = true;
        Apply(); // ilk karede yeni ip görünmesin
    }

    private void Mark((int first, int count, float distance, bool isNew) sample, int run, float value)
    {
        for (int v = sample.first; v < sample.first + sample.count; v++)
        {
            runOf[v] = run;
            along[v] = value;
        }
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
        if (time >= endTime)
        {
            mesh.SetVertices(final);
            enabled = false;
        }
    }

    private void Apply()
    {
        // Koşuların ucu: başta hızlı, sona doğru yavaş (ease-out); uç birim cinsinden
        if (fronts == null || fronts.Length < runs.Count) fronts = new float[runs.Count];
        for (int r = 0; r < runs.Count; r++)
        {
            float x = Mathf.Clamp01((time - runs[r].start) / runs[r].duration);
            float eased = 1f - (1f - x) * (1f - x);
            fronts[r] = eased * (runs[r].length + TipLength);
        }

        for (int k = 0; k < knots.Count; k++)
        {
            (Vector3 center, float when, bool popped) = knots[k];
            if (!popped && time >= when)
            {
                knots[k] = (center, when, true);
                EmitFibers(center);
            }
        }

        for (int i = 0; i < final.Length; i++)
        {
            float p = along[i];
            float scale;
            if (p == Static) scale = 1f;
            else if (p == Knot) scale = EaseOutBack(Mathf.Clamp01((time - knots[runOf[i]].time) / Mathf.Max(settings.knotDuration, 0.01f)));
            else
            {
                (float start, float duration, float length) run = runs[runOf[i]];
                float front = fronts[runOf[i]];
                float tip = Mathf.Clamp01((front - p) / TipLength); // 0 ucun önünde, 1 açılmış
                // Bu noktanın açıldığı an (ease-out'un tersi) ve o andan beri geçen süre: gevşekten sıkıya yaylanma
                float reached = Mathf.Clamp01((p + TipLength) / (run.length + TipLength));
                float openedAt = run.start + run.duration * (1f - Mathf.Sqrt(1f - reached));
                float since = Mathf.Max(0f, time - openedAt);
                float spring = Mathf.Exp(-6f * since) * Mathf.Cos(14f * since);
                float bulge = 1f + 0.6f * Mathf.Sin(Mathf.PI * tip) * (tip < 1f ? 1f : 0f);
                scale = tip * (1f + settings.loose * spring) * bulge;
            }
            buffer[i] = centers[i] + (final[i] - centers[i]) * scale;
        }
        mesh.SetVertices(buffer);
    }

    // Düğüm oturunca saçılan ip lifleri (ipin malzemesiyle ince kapsüller, dünya uzayında, düşerek küçülür)
    private void EmitFibers(Vector3 localCenter)
    {
        if (settings.fibers <= 0 || settings.material == null) return;
        if (fiberSystem == null) fiberSystem = CreateFiberSystem();
        Vector3 world = transform.TransformPoint(localCenter);
        var emit = new ParticleSystem.EmitParams();
        for (int n = 0; n < settings.fibers; n++)
        {
            Vector3 dir = UnityEngine.Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y) * 0.8f + 0.3f;
            emit.position = world + dir * 0.03f;
            emit.velocity = dir.normalized * UnityEngine.Random.Range(0.8f, 1.6f);
            emit.startSize3D = new Vector3(0.015f, UnityEngine.Random.Range(0.04f, 0.08f), 0.015f);
            emit.rotation3D = new Vector3(UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(0f, 360f));
            emit.angularVelocity3D = UnityEngine.Random.insideUnitSphere * 400f;
            emit.startLifetime = UnityEngine.Random.Range(0.4f, 0.7f);
            fiberSystem.Emit(emit, 1);
        }
    }

    private ParticleSystem CreateFiberSystem()
    {
        var holder = new GameObject("Rope Fibers");
        holder.transform.SetParent(transform, false);
        ParticleSystem system = holder.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 1.2f;
        main.startSpeed = 0f;
        main.maxParticles = 200;
        main.startSize3D = true;
        main.startRotation3D = true;
        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;
        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 1f), new Keyframe(1f, 0f)));
        ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
        rotation.enabled = false;

        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = settings.material;
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = CapsuleMesh();
        renderer.alignment = ParticleSystemRenderSpace.World;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        system.Play();
        return system;
    }

    private static Mesh capsule;

    private static Mesh CapsuleMesh()
    {
        if (capsule != null) return capsule;
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule = temp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(temp);
        return capsule;
    }

    // 0'dan 1'e, biraz aşıp geri gelir (zıplama)
    private static float EaseOutBack(float x)
    {
        if (x <= 0f) return 0f;
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}
