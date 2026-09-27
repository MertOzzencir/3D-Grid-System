using System.Collections.Generic;
using UnityEngine;

// Oyundaki tüm tek parça odun prefab'ları. Uzunluğa göre prefab bulmak için.
[CreateAssetMenu(fileName = "WoodCatalog", menuName = "Wood/Wood Catalog")]
public class WoodCatalogSO : ScriptableObject
{
    [SerializeField] private List<Wood> woods = new List<Wood>();

    // Bu uzunlukta odun yoksa null
    public Wood GetWood(int length) => woods.Find(w => w != null && w.Length == length);
}
