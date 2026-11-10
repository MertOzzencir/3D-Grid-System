using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Play mode'da grid'e yerleştirilmiş parçalardan blueprint prefab'ı ve onun placeable SO'sunu üretir.
// Parçalar dünya (0,0,0) noktasına göre kaydedilir; blueprint'in pivot'u orasıdır.
// Oyun sırasında koddan üretilen mesh / materyaller (örn. odunların bağlantı ipi, istasyonların geçici görünüşü) proje
// dosyası değil: prefab onlara referansı kaydedemez. Kopyalanırken SO'nun (Data) içine alt dosya olarak kaydedilir
// (Persist); aynı adla yeniden kaydedince eskileri silinir.
public static class BlueprintBuilder
{
    private const float CellColliderSize = 0.95f;

    // Koddan üretilmiş nesnelerin bu kayıttaki kopyaları (aynı ip mesh'i birden çok kez kaydedilmesin)
    private static readonly Dictionary<Object, Object> persisted = new Dictionary<Object, Object>();
    private static string persistPath;

    public static bool Build(string blueprintName, string folder, out string message)
    {
        var root = new GameObject(blueprintName);
        try
        {
            var blueprint = root.AddComponent<Blueprint>();
            var allCells = new List<Vector3Int>();
            int slotCount = 0;
            int skipped = 0;

            EnsureFolder(folder);
            string prefabPath = $"{folder}/{blueprintName}.prefab";
            string dataPath = $"{folder}/{blueprintName} Data.asset";
            // Prefab SO'yu, SO da prefab'ı gösteriyor: önce SO, sonra prefab, en son SO'ya prefab bağlanır.
            // SO önce oluşur ki koddan üretilmiş görünüşler onun içine kaydedilebilsin.
            GridPlaceableSO data = LoadOrCreateAsset<GridPlaceableSO>(dataPath);
            RemoveSubAssets(dataPath);
            persisted.Clear();
            persistPath = dataPath;

            foreach (GridPlaceable placeable in GridManager.Instance.GetAllPlaceables())
            {
                // Sadece blueprint parçası olabilenler (odun, ileride taş, çit...). Alet, ağaç vs. atlanır.
                if (!(placeable is IBlueprintPiece piece))
                {
                    skipped++;
                    continue;
                }
                CreateSlot(root.transform, placeable, piece, allCells, slotCount++);
            }

            if (slotCount == 0)
            {
                message = "Grid'de blueprint parçası yok, hiçbir şey kaydedilmedi.";
                return false;
            }

            blueprint.EditorSetCells(allCells);

            data.Name = blueprintName;
            SetEntityData(blueprint, data);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            data.Prefab = prefab.GetComponent<Blueprint>();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            message = $"{prefabPath} kaydedildi: {slotCount} slot" + (skipped > 0 ? $", {skipped} parça olmayan obje atlandı" : "");
            return true;
        }
        finally
        {
            Object.Destroy(root);
            persisted.Clear();
            persistPath = null;
        }
    }

    // Parçanın yerine bir slot: pozisyon + rotasyon + imza + görsel kopyası + hücre başına collider
    private static void CreateSlot(Transform root, GridPlaceable placeable, IBlueprintPiece piece, List<Vector3Int> allCells, int index)
    {
        string signature = piece.BlueprintSignature;
        var slotObject = new GameObject($"Slot {index} ({signature})");
        Transform slot = slotObject.transform;
        slot.SetParent(root, false);
        slot.localPosition = placeable.OriginWorldPosition - Vector3.up; // root (0,0,0)'da, dünya pozisyonu = yerel pozisyon
        slot.localRotation = placeable.GridRotation;
        // Parçanın standart (imzadaki) hali blueprint içinde hangi yöne bakıyor: kendi rotasyonu - standarda adımı
        var canonical = (GridMaskRotator.Rotation)(((int)placeable.Rotation - piece.BlueprintRotationSteps + 4) % 4);
        slotObject.AddComponent<BlueprintSlot>().EditorSetup(signature, placeable.Rotation, canonical);

        CopyRenderers(placeable.transform, slot);

        foreach (Vector3Int offset in placeable.PlacedFootprint.FilledCells())
        {
            Vector3Int cell = placeable.OriginWorldPosition + offset - Vector3Int.up;
            allCells.Add(cell);

            var box = slotObject.AddComponent<BoxCollider>();
            box.center = slot.InverseTransformPoint(cell);
            box.size = Vector3.one * CellColliderSize;
        }
    }

    // Sadece görseli kopyalar (MeshFilter + MeshRenderer): script ve collider gelmez,
    // kapalı objeler (örn. gizli oklar) atlanır. Her parça türü için ayrı kod gerekmez.
    private static void CopyRenderers(Transform source, Transform target)
    {
        if (source.TryGetComponent(out MeshFilter filter) && source.TryGetComponent(out MeshRenderer renderer))
        {
            target.gameObject.AddComponent<MeshFilter>().sharedMesh = Persist(filter.sharedMesh);
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = Persist(materials[i]);
            target.gameObject.AddComponent<MeshRenderer>().sharedMaterials = materials;
        }

        foreach (Transform child in source)
        {
            if (!child.gameObject.activeSelf) continue;

            var copy = new GameObject(child.name).transform;
            copy.SetParent(target, false);
            copy.localPosition = child.localPosition;
            copy.localRotation = child.localRotation;
            copy.localScale = child.localScale;
            CopyRenderers(child, copy);
        }
    }

    // Proje dosyası olmayan (koddan üretilmiş) mesh / materyal: kopyası SO'nun içine alt dosya olarak kaydedilir
    private static T Persist<T>(T source) where T : Object
    {
        if (source == null || AssetDatabase.Contains(source) || persistPath == null) return source;
        if (persisted.TryGetValue(source, out Object done)) return (T)done;
        T copy = Object.Instantiate(source);
        copy.name = source.name;
        AssetDatabase.AddObjectToAsset(copy, persistPath);
        persisted[source] = copy;
        return copy;
    }

    // Önceki kayıttan kalan alt dosyalar (aynı adla yeniden kaydedince birikmesin)
    private static void RemoveSubAssets(string path)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset != null && AssetDatabase.IsSubAsset(asset)) Object.DestroyImmediate(asset, true);
    }

    // GridEntity.data private olduğu için SerializedObject üzerinden atanıyor
    private static void SetEntityData(GridEntity entity, GridEntitySOBase data)
    {
        var serialized = new SerializedObject(entity);
        serialized.FindProperty("data").objectReferenceValue = data;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static T LoadOrCreateAsset<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;

        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    // "Assets/A/B" → eksik klasörleri sırayla oluşturur
    private static void EnsureFolder(string folder)
    {
        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
