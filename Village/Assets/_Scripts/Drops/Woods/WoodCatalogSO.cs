using System.Collections.Generic;
using UnityEngine;

// Oyundaki tüm tek parça odun prefab'ları. Uzunluğa göre prefab bulmak için.
// Uzun kütük (kullanıcı 2026-11-10: odunlarda dual grid, şimdiki görünüşle): 1×1 odun mesh'inin 4 hali (index: bit 0 alt
// uç açık, bit 1 üst uç açık; 0 = iki ucu kapalı, yani orijinal). Açık uçta spiralli kapak ve pah yok, gövde hücre
// sınırına kadar düz uzar; bütün açık uçlar aynı kesitle bittiği için uç uca iki parça dikişsiz birleşir.
// Village → Uzun Kütük Parçalarını Üret kurar (alt asset olarak bu asset'e yazar). Boşsa odunlar hep orijinal görünür.
[CreateAssetMenu(fileName = "WoodCatalog", menuName = "Wood/Wood Catalog")]
public class WoodCatalogSO : ScriptableObject
{
    [SerializeField] private List<Wood> woods = new List<Wood>();
    [SerializeField] private Mesh[] pieceVariants = new Mesh[4];

    public bool HasPieceVariants => pieceVariants != null && pieceVariants.Length == 4 && pieceVariants[3] != null;

    // Uçları açık / kapalı 1×1 (1×1 odun görselinin kendi mesh uzayında: mesh değiştirilir, transform aynı kalır)
    public Mesh PieceVariant(bool openBottom, bool openTop) => pieceVariants[(openBottom ? 1 : 0) | (openTop ? 2 : 0)];

#if UNITY_EDITOR
    public void SetPieceVariants(Mesh[] meshes) => pieceVariants = meshes;
#endif

    // Bu uzunlukta odun yoksa null
    public Wood GetWood(int length) => woods.Find(w => w != null && w.Length == length);
}
