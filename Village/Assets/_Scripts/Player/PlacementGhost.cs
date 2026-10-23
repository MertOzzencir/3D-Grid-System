using System.Collections.Generic;
using UnityEngine;

// Build mode yerleştirme önizlemesi: bir objenin sadece görünüşünün kopyası ("Village/Placement Ghost" ile yarı saydam).
// Kaynak (probe) gerçek bir kopyadır ama kapalı durur; kodla kurulan görünüşler (placeholder'lar) de onda hazır olduğu
// için hayalet onun mesh'lerinden kurulur: her MeshRenderer / SkinnedMeshRenderer için mesh'i ve köke göre yeri kopyalanır,
// script'ler ve collider'lar gelmez (hayalet oyunda hiçbir şeye dokunmaz). Mesh'i olmayan objede (görünüşü hiç yoksa)
// kapladığı hücreler küp olarak gösterilir. Hat boyunca dizerken birden çok kopya gerekir: Count kadar klon tutulur.
public class PlacementGhost
{
    private readonly GameObject template;
    private readonly List<GameObject> copies = new List<GameObject>();
    private readonly List<bool> copyValid = new List<bool>();
    private readonly Material validMaterial, invalidMaterial;

    // probe: kaynak obje (kapalı olabilir); footprint: dönüşsüz hücreleri (görünüş yoksa küpler için). Malzemeler:
    // konabilir / konamaz yer (her kopya kendi yerine göre birini kullanır)
    public PlacementGhost(GameObject probe, IReadOnlyList<Vector3Int> footprint, Material validMaterial, Material invalidMaterial)
    {
        this.validMaterial = validMaterial;
        this.invalidMaterial = invalidMaterial;
        template = new GameObject("Placement Ghost");
        Transform root = probe.transform;
        Matrix4x4 toRoot = root.worldToLocalMatrix;
        bool any = false;
        foreach (Renderer renderer in probe.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsShownUnder(renderer.transform, root)) continue;
            Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                      : renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
            if (mesh == null) continue;
            Matrix4x4 local = toRoot * renderer.transform.localToWorldMatrix;
            AddPart(mesh, local.GetPosition(), local.rotation, local.lossyScale);
            any = true;
        }
        if (!any)
        {
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            foreach (Vector3Int cell in footprint)
                AddPart(cube, cell, Quaternion.identity, Vector3.one * 0.95f);
        }
        template.SetActive(false);
    }

    // Kaynak objede açık görünen bir parça mı (kapalı child'lar, örn. kesilmiş ağaç görünüşü, gelmesin). Kök kapalı olabilir.
    private static bool IsShownUnder(Transform part, Transform root)
    {
        for (Transform t = part; t != null && t != root; t = t.parent)
            if (!t.gameObject.activeSelf) return false;
        return part.TryGetComponent(out Renderer renderer) && renderer.enabled;
    }

    private void AddPart(Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var part = new GameObject(mesh.name);
        part.transform.SetParent(template.transform, false);
        part.transform.SetLocalPositionAndRotation(position, rotation);
        part.transform.localScale = scale;
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = part.AddComponent<MeshRenderer>();
        var materials = new Material[Mathf.Max(1, mesh.subMeshCount)];
        for (int i = 0; i < materials.Length; i++) materials[i] = validMaterial;
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // i. kopyayı bu yerde ve dönüşte gösterir (gerekirse klonlar); valid: konabilir yer rengi
    public void Show(int index, Vector3 position, Quaternion rotation, bool valid)
    {
        while (copies.Count <= index)
        {
            copies.Add(Object.Instantiate(template));
            copyValid.Add(true);
        }
        GameObject copy = copies[index];
        copy.transform.SetPositionAndRotation(position, rotation);
        if (!copy.activeSelf) copy.SetActive(true);
        if (copyValid[index] == valid) return;
        copyValid[index] = valid;
        foreach (MeshRenderer renderer in copy.GetComponentsInChildren<MeshRenderer>())
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = valid ? validMaterial : invalidMaterial;
            renderer.sharedMaterials = materials;
        }
    }

    // first'ten sonraki kopyaları gizler
    public void HideFrom(int first)
    {
        for (int i = first; i < copies.Count; i++)
            if (copies[i].activeSelf) copies[i].SetActive(false);
    }

    public void Destroy()
    {
        foreach (GameObject copy in copies) Object.Destroy(copy);
        copies.Clear();
        copyValid.Clear();
        Object.Destroy(template);
    }
}
