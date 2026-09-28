// Yanlarına odun eklenebilen dik odun: tek parça Wood (yanları hep boş) ya da MergedWood.
// Yan ve katman numaralaması için bkz. WoodLayout / WoodSlot.
public interface IWoodStack
{
    // Dik duran ana odunun uzunluğu = her yandaki katman sayısı
    int BaseLength { get; }

    // O yandaki katmanda duran odunun uzunluğu, 0 = boş. layer: 1..BaseLength
    int PieceAt(WoodSide side, int layer);
}
