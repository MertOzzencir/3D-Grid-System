using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Village → Otomasyon Prototipini Kur: otomasyon prototipinin bütün asset'lerini tek seferde kurar (gri kutu prefab'lar,
// parça verileri, yerleştirme verileri), oyuncu build menüsüne "Automation" sekmesini ekler ve hepsini SaveRegistry'ye
// yazar. Klasör zaten varsa sorar: yeniden kurarken eski klasörü siler, menüdeki ve kayıttaki eski girişleri temizler.
public static class AutomationPrototypeSetup
{
    private const string Root = "Assets/Automation Prototype";

    [MenuItem("Village/Otomasyon Prototipini Kur")]
    public static void Setup()
    {
        if (AssetDatabase.IsValidFolder(Root))
        {
            if (!EditorUtility.DisplayDialog("Otomasyon prototipi",
                    $"{Root} zaten var. Silinip baştan kurulsun mu? (Kayıtta duran prototip objeleri aynı adlarla yeniden bağlanır.)",
                    "Yeniden kur", "Vazgeç"))
                return;
            AssetDatabase.DeleteAsset(Root);
            CleanReferences();
        }
        materials.Clear();
        AssetDatabase.CreateFolder("Assets", "Automation Prototype");
        foreach (string sub in new[] { "Items", "Materials", "Prefabs", "Data" })
            AssetDatabase.CreateFolder(Root, sub);

        Shader lit = Shader.Find("Village/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");

        // Parçalar
        ItemSO ore = Item("Iron Ore", new Color(0.45f, 0.32f, 0.26f));
        ItemSO ingot = Item("Iron Ingot", new Color(0.78f, 0.8f, 0.84f));
        ItemSO robot = Item("Robot", new Color(0.2f, 0.75f, 0.95f));

        // Cevher topağı: matkapla kazılınca yere düşer, elle makineye götürülür (yığınlanır)
        GameObject chunkGo = NewRoot("Iron Ore Chunk");
        OreChunk chunk = chunkGo.AddComponent<OreChunk>();
        Set(chunk, "item", ore);
        Set(chunk, "placeholderShader", lit);
        GridPlaceableSO chunkData = SaveEntity<GridPlaceableSO, OreChunk>(chunkGo, "Iron Ore Chunk", "Iron Ore");

        // Kazılabilir maden bloğu: zemin (base) türü, 27 parça (kodla kurulur)
        GameObject depositGo = NewRoot("Iron Deposit");
        depositGo.AddComponent<BoxCollider>().size = Vector3.one;
        MineableDeposit deposit = depositGo.AddComponent<MineableDeposit>();
        Set(deposit, "chunkData", chunkData);
        Set(deposit, "rockMaterial", Mat("Iron Rock", Color.Lerp(ore.Color, new Color(0.25f, 0.22f, 0.2f), 0.4f)));
        GridBaseSO depositData = SaveEntity<GridBaseSO, MineableDeposit>(depositGo, "Iron Deposit", "Iron Deposit");

        // Matkap: elde tutulan alet, ucu aşağı bakar
        GameObject drillGo = NewRoot("Drill");
        var drillVisual = new GameObject("Visual").transform;
        drillVisual.SetParent(drillGo.transform, false);
        Cylinder(drillVisual, "Body", new Vector3(0f, 0.15f, 0f), new Vector3(0.18f, 0.35f, 0.18f), Mat("Drill Body", new Color(0.9f, 0.6f, 0.15f)));
        Cylinder(drillVisual, "Handle", new Vector3(0f, 0.42f, 0f), new Vector3(0.08f, 0.2f, 0.08f), Mat("Dark", new Color(0.2f, 0.2f, 0.22f)));
        Cylinder(drillVisual, "Bit", new Vector3(0f, -0.12f, 0f), new Vector3(0.07f, 0.22f, 0.07f), Mat("Drill Bit", new Color(0.75f, 0.78f, 0.82f)));
        Collider(drillGo, new Vector3(0f, 0.15f, 0f), new Vector3(0.3f, 0.7f, 0.3f));
        Drill drill = drillGo.AddComponent<Drill>();
        Set(drill, "VisualTransform", drillVisual);
        Set(drill, "cooldown", 0.3f);
        Set(drill, "swing.windUpAngle", -6f);
        Set(drill, "swing.strikeAngle", 4f);
        Set(drill, "swing.windUpDuration", 0.06f);
        Set(drill, "swing.strikeDuration", 0.04f);
        Set(drill, "swing.returnDuration", 0.08f);
        GridPlaceableSO drillData = SaveEntity<GridPlaceableSO, Drill>(drillGo, "Drill", "Drill");

        // Eritici: 1 cevher → 1 külçe
        GameObject smelterGo = NewRoot("Smelter");
        Box(smelterGo, "Body", new Vector3(0f, -0.1f, 0f), new Vector3(0.85f, 0.8f, 0.85f), Mat("Smelter", new Color(0.85f, 0.35f, 0.2f)));
        Box(smelterGo, "Chimney", new Vector3(-0.25f, 0.45f, -0.25f), new Vector3(0.18f, 0.35f, 0.18f), Mat("Dark", new Color(0.2f, 0.2f, 0.22f)));
        Collider(smelterGo, new Vector3(0f, -0.1f, 0f), new Vector3(0.85f, 0.8f, 0.85f));
        Processor smelter = smelterGo.AddComponent<Processor>();
        Recipe(smelter, "Smelter", new[] { (ore, 1) }, ingot, 1, 2f);
        Set(smelter, "stackHeight", 0.35f);
        GridPlaceableSO smelterData = SaveEntity<GridPlaceableSO, Processor>(smelterGo, "Smelter", "Smelter");

        // Montaj: 3 külçe → 1 robot
        GameObject assemblerGo = NewRoot("Assembler");
        Box(assemblerGo, "Body", new Vector3(0f, -0.2f, 0f), new Vector3(0.9f, 0.6f, 0.9f), Mat("Assembler", new Color(0.35f, 0.45f, 0.6f)));
        Box(assemblerGo, "Frame", new Vector3(0f, 0.2f, -0.3f), new Vector3(0.8f, 0.25f, 0.1f), Mat("Dark", new Color(0.2f, 0.2f, 0.22f)));
        Collider(assemblerGo, new Vector3(0f, -0.2f, 0f), new Vector3(0.9f, 0.6f, 0.9f));
        Processor assembler = assemblerGo.AddComponent<Processor>();
        Recipe(assembler, "Assembler", new[] { (ingot, 3) }, robot, 1, 4f);
        Set(assembler, "stackHeight", 0.2f);
        GridPlaceableSO assemblerData = SaveEntity<GridPlaceableSO, Processor>(assemblerGo, "Assembler", "Assembler");

        // Gönderim rampası: robotları alır
        GameObject padGo = NewRoot("Imperial Shipping");
        Box(padGo, "Pad", new Vector3(0f, -0.43f, 0f), new Vector3(0.95f, 0.14f, 0.95f), Mat("Pad", new Color(0.25f, 0.25f, 0.28f)));
        Box(padGo, "Eagle", new Vector3(0f, -0.3f, 0f), new Vector3(0.4f, 0.12f, 0.4f), Mat("Gold", new Color(0.9f, 0.7f, 0.25f)));
        Collider(padGo, new Vector3(0f, -0.38f, 0f), new Vector3(0.95f, 0.24f, 0.95f));
        ShippingPad pad = padGo.AddComponent<ShippingPad>();
        SetList(pad, "accepts", new Object[] { robot });
        GridPlaceableSO padData = SaveEntity<GridPlaceableSO, ShippingPad>(padGo, "Imperial Shipping", "Imperial Shipping");

        // Kol: girişinden alır, çıkışına koyar (yönler oyunda tıklayarak ayarlanır)
        GameObject armGo = NewRoot("Arm");
        Cylinder(armGo.transform, "Base", new Vector3(0f, -0.38f, 0f), new Vector3(0.4f, 0.12f, 0.4f), Mat("Arm", new Color(0.55f, 0.55f, 0.6f)));
        var pivot = new GameObject("Pivot").transform;
        pivot.SetParent(armGo.transform, false);
        pivot.localPosition = new Vector3(0f, -0.15f, 0f);
        Cylinder(pivot, "Joint", Vector3.zero, new Vector3(0.18f, 0.12f, 0.18f), Mat("Arm", new Color(0.55f, 0.55f, 0.6f)));
        Box(pivot.gameObject, "Bar", new Vector3(0f, 0.05f, 0.25f), new Vector3(0.1f, 0.08f, 0.55f), Mat("Arm Bar", new Color(0.95f, 0.55f, 0.15f)));
        Renderer held = Box(pivot.gameObject, "Held", new Vector3(0f, 0.05f, 0.55f), Vector3.one * 0.2f, Mat("Ore", ore.Color));
        held.enabled = false;
        Collider(armGo, new Vector3(0f, -0.3f, 0f), new Vector3(0.5f, 0.4f, 0.5f));
        Arm arm = armGo.AddComponent<Arm>();
        Set(arm, "pivot", pivot);
        Set(arm, "heldCube", held);
        GridPlaceableSO armData = SaveEntity<GridPlaceableSO, Arm>(armGo, "Arm", "Arm");

        // Oyuncu build menüsü: "Automation" sekmesi
        var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalogSO>("Assets/Build Catalog.asset");
        if (catalog != null)
        {
            catalog.Categories.RemoveAll(c => c.Name == "Automation");
            catalog.Categories.Insert(0, new BuildCatalogSO.Category
            {
                Name = "Automation",
                Items = new List<GridEntitySOBase> { drillData, armData, smelterData, assemblerData, padData }
            });
            EditorUtility.SetDirty(catalog);
        }
        else Debug.LogWarning("Assets/Build Catalog.asset bulunamadı; Automation sekmesi eklenmedi.");

        // Geliştirici build mode'u: Base menüsüne maden bloğu
        var baseMenu = AssetDatabase.LoadAssetAtPath<GridBasePlacementMenuSO>("Assets/SOData/GridPlacementMenu/Base.asset");
        if (baseMenu != null)
        {
            baseMenu.Entities ??= new List<GridEntitySOBase>();
            baseMenu.Entities.Add(depositData);
            EditorUtility.SetDirty(baseMenu);
        }
        else Debug.LogWarning("Base yerleştirme menüsü bulunamadı; maden bloğu eklenmedi.");

        // Kayıt listesi
        var registry = AssetDatabase.LoadAssetAtPath<SaveRegistrySO>("Assets/SOData/SaveRegistry.asset");
        if (registry != null)
        {
            var serialized = new SerializedObject(registry);
            SerializedProperty entities = serialized.FindProperty("entities");
            foreach (GridEntitySOBase data in new GridEntitySOBase[] { chunkData, depositData, drillData, armData, smelterData, assemblerData, padData })
            {
                entities.arraySize++;
                entities.GetArrayElementAtIndex(entities.arraySize - 1).objectReferenceValue = data;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Otomasyon prototipi",
            "Kuruldu. Makineler alt bardaki Automation sekmesinde, Iron Deposit build mode'un (Space) Base menüsünde.\n\n" +
            "• E ile Drill'i al, sağ tık basılıyken bloğa nişan al, sol tıkla kaz\n" +
            "• Düşen cevheri E ile al, sağ tık basılıyken eriticiye nişan al, sol tıkla içine koy\n" +
            "• Kollar: sol tık çıkış, Shift + sol tık giriş yönü", "Tamam");
    }

    // --- Yardımcılar ---

    // Silinen eski prototip asset'lerinin menü ve kayıttaki boş kalan girişleri
    private static void CleanReferences()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalogSO>("Assets/Build Catalog.asset");
        if (catalog != null)
        {
            catalog.Categories.RemoveAll(c => c.Name == "Automation");
            EditorUtility.SetDirty(catalog);
        }
        var baseMenu = AssetDatabase.LoadAssetAtPath<GridBasePlacementMenuSO>("Assets/SOData/GridPlacementMenu/Base.asset");
        if (baseMenu != null && baseMenu.Entities != null)
        {
            baseMenu.Entities.RemoveAll(e => e == null);
            EditorUtility.SetDirty(baseMenu);
        }
        var registry = AssetDatabase.LoadAssetAtPath<SaveRegistrySO>("Assets/SOData/SaveRegistry.asset");
        if (registry != null)
        {
            var serialized = new SerializedObject(registry);
            SerializedProperty entities = serialized.FindProperty("entities");
            for (int i = entities.arraySize - 1; i >= 0; i--)
                if (entities.GetArrayElementAtIndex(i).objectReferenceValue == null) entities.DeleteArrayElementAtIndex(i);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static ItemSO Item(string name, Color color)
    {
        var item = ScriptableObject.CreateInstance<ItemSO>();
        item.Name = name;
        item.Color = color;
        AssetDatabase.CreateAsset(item, $"{Root}/Items/{name}.asset");
        return item;
    }

    private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    private static Material Mat(string name, Color color)
    {
        if (materials.TryGetValue(name, out Material existing) && existing != null) return existing;
        Shader shader = Shader.Find("Village/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
        var material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", color);
        material.color = color;
        AssetDatabase.CreateAsset(material, $"{Root}/Materials/{name}.mat");
        materials[name] = material;
        return material;
    }

    private static GameObject NewRoot(string name) => new GameObject(name);

    private static Renderer Box(GameObject parent, string name, Vector3 position, Vector3 scale, Material material)
        => Primitive(PrimitiveType.Cube, parent.transform, name, position, scale, material);

    private static Renderer Cylinder(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        => Primitive(PrimitiveType.Cylinder, parent, name, position, new Vector3(scale.x, scale.y * 0.5f, scale.z), material);

    private static Renderer Primitive(PrimitiveType type, Transform parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(go.GetComponent<Collider>()); // tutma / raycast kökteki collider'a gitsin
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        Renderer renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        return renderer;
    }

    private static void Collider(GameObject root, Vector3 center, Vector3 size)
    {
        var box = root.AddComponent<BoxCollider>();
        box.center = center;
        box.size = size;
    }

    private static void Set(Object target, string field, object value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            Debug.LogError($"{target.GetType().Name}.{field} bulunamadı");
            return;
        }
        switch (value)
        {
            case Object o: property.objectReferenceValue = o; break;
            case float f: property.floatValue = f; break;
            case int i: property.intValue = i; break;
            case string s: property.stringValue = s; break;
            case Vector3 v: property.vector3Value = v; break;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetList(Object target, string field, Object[] values)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty list = serialized.FindProperty(field);
        list.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Recipe(Processor processor, string displayName, (ItemSO item, int count)[] inputs, ItemSO output, int outputCount, float seconds)
    {
        var serialized = new SerializedObject(processor);
        serialized.FindProperty("displayName").stringValue = displayName;
        SerializedProperty list = serialized.FindProperty("inputs");
        list.arraySize = inputs.Length;
        for (int i = 0; i < inputs.Length; i++)
        {
            SerializedProperty element = list.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("Item").objectReferenceValue = inputs[i].item;
            element.FindPropertyRelative("Count").intValue = inputs[i].count;
        }
        serialized.FindProperty("output").objectReferenceValue = output;
        serialized.FindProperty("outputPerCraft").intValue = outputCount;
        serialized.FindProperty("craftSeconds").floatValue = seconds;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // Prefab'ı kaydeder, 1×1×1 yerleştirme verisini (SO) oluşturur, ikisini birbirine bağlar
    private static TData SaveEntity<TData, TComponent>(GameObject go, string fileName, string displayName)
        where TData : GridEntitySOBase where TComponent : GridEntity
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/{fileName}.prefab");
        Object.DestroyImmediate(go);

        var data = ScriptableObject.CreateInstance<TData>();
        data.Name = displayName;
        var serializedData = new SerializedObject(data);
        serializedData.FindProperty("size").vector3IntValue = Vector3Int.one;
        serializedData.FindProperty("Prefab").objectReferenceValue = prefab.GetComponent<TComponent>();
        serializedData.ApplyModifiedPropertiesWithoutUndo();
        data.EnsureLayout();
        AssetDatabase.CreateAsset(data, $"{Root}/Data/{fileName}.asset");

        var serializedPrefab = new SerializedObject(prefab.GetComponent<TComponent>());
        serializedPrefab.FindProperty("data").objectReferenceValue = data;
        serializedPrefab.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SavePrefabAsset(prefab);
        return data;
    }
}
