using UnityEngine;

// Yanlarına odun eklenebilen dik odun: tek parça Wood (yanları hep boş) ya da MergedWood.
// Yan ve katman numaralaması için bkz. WoodLayout / WoodSlot.
public interface IWoodStack
{
    // Dik duran ana odunun uzunluğu = her yandaki katman sayısı
    int BaseLength { get; }

    // Ana odunun parçaları, aşağıdan yukarı: hep 1×1'ler, aralarındaki ek yerleri iple bağlı
    int[] BaseSegments { get; }

    // O yandaki katmanda duran odunun uzunluğu, 0 = boş. layer: 1..BaseLength
    int PieceAt(WoodSide side, int layer);

    // O yandaki odunun parçaları (içten dışa; 1×1'ler, iple bağlı); boş ya da kayıt yoksa null
    int[] SideSegments(WoodSide side, int layer);

    // O yandaki yatık parçaların renderer'ları (örn. kamerayı kapatınca saydamlaştırmak için); yoksa boş dizi
    Renderer[] PieceRenderers(WoodSide side);
}
