using UnityEngine;

// Ekin türü (domates, havuç...). Ekinin yol bulmacasına etkisi yok, sadece ne çıkacağını belirler (DESIGN.md).
// Tohum (Seed) bir CropSO'yu gösterir; tarlaya ekilince parçanın o hücresinde fide olarak durur, büyüme düğmesinden
// sonra olgun haline geçer.
[CreateAssetMenu(fileName = "New Crop", menuName = "Farm/Crop")]
public class CropSO : ScriptableObject
{
    public string Name;
    [Tooltip("Ekilince görünen fide modeli. Pivot dibinde (toprağa değen nokta), 1 hücreye sığar. Boşsa kod yer tutucu fide kurar.")]
    public GameObject seedlingModel;
    [Tooltip("Olgunlaşınca (büyüme düğmesinden sonra) görünen model. Pivot dibinde, 1 hücreye sığar. Boşsa kod yer tutucu kurar.")]
    public GameObject matureModel;
    [Tooltip("Model yokken yer tutucu fidenin / bitkinin yaprak rengi")]
    public Color placeholderColor = new Color(0.55f, 0.8f, 0.4f);
    [Tooltip("Model yokken olgun yer tutucunun ürün (meyve) rengi")]
    public Color produceColor = new Color(0.9f, 0.3f, 0.25f);

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
