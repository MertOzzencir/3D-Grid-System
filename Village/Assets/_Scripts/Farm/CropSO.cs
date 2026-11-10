using UnityEngine;

// Ekin türü (domates, havuç...). Ekinin yol bulmacasına etkisi yok, sadece ne çıkacağını belirler (DESIGN.md).
// Tohum (Seed) bir CropSO'yu gösterir; tarlaya ekilince parçanın o hücresinde fide olarak durur, büyüme düğmesinden
// sonra olgun haline geçer.
[CreateAssetMenu(fileName = "New Crop", menuName = "Farm/Crop")]
public class CropSO : ScriptableObject
{
    public string Name;
    [Tooltip("Hasat edilince envantere giren ürün (Create Source → New Source). Boşsa hasat edilir ama envantere bir şey girmez.")]
    public SourcesSO produce;
    [Tooltip("Ekilince görünen fide modeli. Pivot dibinde (toprağa değen nokta), 1 hücreye sığar. Boşsa kod yer tutucu fide kurar.")]
    public GameObject seedlingModel;
    [Tooltip("Olgunlaşınca (büyüme düğmesinden sonra) görünen model. Pivot dibinde, 1 hücreye sığar. Boşsa kod yer tutucu kurar.")]
    public GameObject matureModel;
    [Tooltip("Model yokken yer tutucu fidenin / bitkinin yaprak rengi")]
    public Color placeholderColor = new Color(0.55f, 0.8f, 0.4f);
    [Tooltip("Model yokken olgun yer tutucunun ürün (meyve) rengi")]
    public Color produceColor = new Color(0.9f, 0.3f, 0.25f);

    [Header("Animasyon (CropSway)")]
    [Tooltip("Olgun modelde meyve objelerinin adı. Bu adı taşıyan ya da \"ad.001\" gibi devam eden objeler meyve sayılır: " +
             "eldiven geçince sapından sallanır, hasatta savrulur. Pivot'ları meyvenin tepesinde (sapa bağlandığı yerde) olmalı. " +
             "Boşsa meyve yok, sadece bitki eğilir.")]
    public string fruitName = "Tomato";
    [Tooltip("Eldiven geçince bitkinin tabanından eğilme miktarı (0 = hiç)")]
    [Range(0f, 2f)] public float touchSway = 1f;
    [Tooltip("Eldiven geçince meyvelerin sallanma miktarı (0 = hiç)")]
    [Range(0f, 2f)] public float fruitSwing = 1f;
    [Tooltip("Hasatta meyvelerin savrulma gücü")]
    [Range(0.2f, 3f)] public float scatterForce = 1f;

    // Fide görseli (collider'sız): model varsa onun kopyası, yoksa küçük yer tutucu (sap + iki yaprak)
    public GameObject CreateSeedling(Transform parent)
    {
        if (seedlingModel != null) return CopyModel(seedlingModel, parent);

        var root = new GameObject($"{name} (fide)");
        root.transform.SetParent(parent, false);
        AddPart(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.09f, 0f), new Vector3(0.04f, 0.09f, 0.04f),
                Quaternion.identity, new Color(0.45f, 0.65f, 0.3f));
        AddPart(root.transform, PrimitiveType.Sphere, new Vector3(0.07f, 0.17f, 0f), new Vector3(0.14f, 0.05f, 0.08f),
                Quaternion.Euler(0f, 0f, 25f), placeholderColor);
        AddPart(root.transform, PrimitiveType.Sphere, new Vector3(-0.07f, 0.17f, 0f), new Vector3(0.14f, 0.05f, 0.08f),
                Quaternion.Euler(0f, 0f, -25f), placeholderColor);
        return root;
    }

    // Olgun bitki görseli (collider'sız): model varsa onun kopyası, yoksa yer tutucu (uzun sap, dört yaprak, üç meyve)
    public GameObject CreateMature(Transform parent)
    {
        if (matureModel != null) return CopyModel(matureModel, parent);

        var root = new GameObject($"{name} (olgun)");
        root.transform.SetParent(parent, false);
        Color stem = new Color(0.45f, 0.65f, 0.3f);
        AddPart(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.17f, 0f), new Vector3(0.05f, 0.17f, 0.05f), Quaternion.identity, stem);
        for (int i = 0; i < 4; i++)
        {
            Quaternion around = Quaternion.Euler(0f, 90f * i + 20f, 0f);
            AddPart(root.transform, PrimitiveType.Sphere, around * new Vector3(0.1f, 0.22f, 0f), new Vector3(0.2f, 0.06f, 0.1f),
                    around * Quaternion.Euler(0f, 0f, 20f), placeholderColor);
        }
        for (int i = 0; i < 3; i++)
        {
            Quaternion around = Quaternion.Euler(0f, 120f * i, 0f);
            AddPart(root.transform, PrimitiveType.Sphere, around * new Vector3(0.09f, 0.3f + 0.04f * i, 0f), Vector3.one * 0.12f,
                    Quaternion.identity, produceColor);
        }
        return root;
    }

    private static GameObject CopyModel(GameObject prefab, Transform parent)
    {
        GameObject model = Instantiate(prefab, parent);
        foreach (Collider collider in model.GetComponentsInChildren<Collider>()) Destroy(collider);
        return model;
    }

    private static void AddPart(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Quaternion rotation, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.SetLocalPositionAndRotation(position, rotation);
        part.transform.localScale = scale;
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        block.SetColor("_Color", color);
        part.GetComponent<Renderer>().SetPropertyBlock(block);
    }
}
