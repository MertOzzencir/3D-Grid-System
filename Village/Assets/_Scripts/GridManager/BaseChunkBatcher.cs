using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Base'lerin görselini bölge bölge (ChunkSize × ChunkSize sütun, bütün katlar) malzeme başına tek mesh'te birleştirir
// (kullanıcı: base'ler batch'lensin). Prefab'daki Static işareti işe yaramıyordu: base'ler runtime'da spawn oluyor (kayıt,
// build modu), static batching sadece sahnede baştan duranları birleştirir. Birleşen base'in kendi renderer'ları kapanır;
// collider'ı ve GridBase'i yerinde kalır (eldiven, raycast'ler değişmez). Base eklenip silinince sadece o bölge kare
// sonunda (Flush) yeniden kurulur. Mesh'lerin Read/Write'ı açık olmalı (CombineMeshes runtime'da okur).
public class BaseChunkBatcher
{
    private const int ChunkSize = 16;

    private class Chunk
    {
        public readonly HashSet<GridBase> Bases = new HashSet<GridBase>();
        public readonly List<GameObject> Batches = new List<GameObject>();
        public bool Dirty;
    }

    private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    private readonly Dictionary<GridBase, Vector2Int> chunkOf = new Dictionary<GridBase, Vector2Int>();
    private Transform root;

    public void Add(GridBase entity)
    {
        if (entity == null || chunkOf.ContainsKey(entity)) return;
        Vector3 position = entity.transform.position;
        var key = new Vector2Int(Mathf.FloorToInt(position.x / ChunkSize), Mathf.FloorToInt(position.z / ChunkSize));
        if (!chunks.TryGetValue(key, out Chunk chunk)) chunks[key] = chunk = new Chunk();
        chunk.Bases.Add(entity);
        chunk.Dirty = true;
        chunkOf[entity] = key;
    }

    // Grid'den çıkarılırken (silinmeden önce): renderer'ları geri açılır (tekrar konacaksa / taşınacaksa görünsün)
    public void Remove(GridBase entity)
    {
        if (entity == null || !chunkOf.TryGetValue(entity, out Vector2Int key)) return;
        chunkOf.Remove(entity);
        Chunk chunk = chunks[key];
        chunk.Bases.Remove(entity);
        chunk.Dirty = true;
        foreach (MeshRenderer renderer in entity.GetComponentsInChildren<MeshRenderer>(true)) renderer.enabled = true;
    }

    // Değişen bölgeleri yeniden kurar (kare sonunda bir kez: yüklemede yüzlerce base tek seferde)
    public void Flush()
    {
        foreach (Chunk chunk in chunks.Values)
        {
            if (!chunk.Dirty) continue;
            chunk.Dirty = false;
            Rebuild(chunk);
        }
    }

    private void Rebuild(Chunk chunk)
    {
        foreach (GameObject batch in chunk.Batches)
        {
            if (batch == null) continue;
            Object.Destroy(batch.GetComponent<MeshFilter>().sharedMesh);
            Object.Destroy(batch);
        }
        chunk.Batches.Clear();
        chunk.Bases.RemoveWhere(b => b == null);
        if (chunk.Bases.Count == 0) return;

        if (root == null) root = new GameObject("Base Chunks").transform; // dünya uzayında, kimlik dönüşümü

        // Malzeme + gölge ayarı başına bir mesh
        var groups = new Dictionary<(Material, ShadowCastingMode), List<CombineInstance>>();
        var vertexCounts = new Dictionary<(Material, ShadowCastingMode), int>();
        foreach (GridBase entity in chunk.Bases)
            foreach (MeshRenderer renderer in entity.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                Mesh mesh = filter.sharedMesh;
                if (!mesh.isReadable)
                {
                    Debug.LogWarning($"BaseChunkBatcher: '{mesh.name}' okunamıyor (modelde Read/Write kapalı), birleştirilmedi.", renderer);
                    continue;
                }
                Material[] materials = renderer.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount && sub < materials.Length; sub++)
                {
                    var key = (materials[sub], renderer.shadowCastingMode);
                    if (!groups.TryGetValue(key, out List<CombineInstance> list))
                    {
                        groups[key] = list = new List<CombineInstance>();
                        vertexCounts[key] = 0;
                    }
                    list.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub, transform = renderer.transform.localToWorldMatrix });
                    vertexCounts[key] += mesh.vertexCount;
                }
                renderer.enabled = false;
            }

        foreach (var pair in groups)
        {
            var mesh = new Mesh { name = "Base Chunk" };
            if (vertexCounts[pair.Key] > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.CombineMeshes(pair.Value.ToArray(), true, true);
            mesh.RecalculateBounds();

            var batch = new GameObject("Base Chunk");
            batch.transform.SetParent(root, false);
            batch.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = batch.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = pair.Key.Item1;
            renderer.shadowCastingMode = pair.Key.Item2;
            chunk.Batches.Add(batch);
        }
    }
}
