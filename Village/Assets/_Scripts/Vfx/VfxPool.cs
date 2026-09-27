using System.Collections.Generic;
using UnityEngine;

// Particle efektlerini her seferinde Instantiate/Destroy etmek yerine yeniden kullanır.
// Her efekt prefab'ı için küçük bir havuz: bitmiş bir kopya varsa o oynatılır, yoksa yenisi eklenir.
public static class VfxPool
{
    private const int MaxPerPrefab = 8;

    private static readonly Dictionary<ParticleSystem, List<ParticleSystem>> pools = new Dictionary<ParticleSystem, List<ParticleSystem>>();
    private static Transform root;

    private static Transform Root => root != null ? root : root = new GameObject("VFX Pool").transform;

    public static void Play(ParticleSystem prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return;

        ParticleSystem instance = GetInstance(prefab);
        instance.transform.SetPositionAndRotation(position, rotation);
        instance.Play(true);
    }

    private static ParticleSystem GetInstance(ParticleSystem prefab)
    {
        if (!pools.TryGetValue(prefab, out List<ParticleSystem> pool))
            pools[prefab] = pool = new List<ParticleSystem>();

        pool.RemoveAll(p => p == null); // sahne değişince yok olan kopyalar

        foreach (ParticleSystem p in pool)
            if (!p.IsAlive(true)) return p;

        if (pool.Count >= MaxPerPrefab)
        {
            // Hepsi oynuyorsa en eskisini baştan başlat, sınırsız büyümesin
            ParticleSystem oldest = pool[0];
            pool.RemoveAt(0);
            pool.Add(oldest);
            oldest.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return oldest;
        }

        ParticleSystem created = Object.Instantiate(prefab, Root);
        var main = created.main;
        main.playOnAwake = false;
        main.stopAction = ParticleSystemStopAction.None; // Destroy/Disable olursa havuz bozulur
        created.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        pool.Add(created);
        return created;
    }
}
