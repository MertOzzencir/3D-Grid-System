// Katmanlarına odun eklenebilen dik odun: tek parça Wood (katmanları hep boş) ya da MergedWood.
// Katman numaralaması için bkz. WoodLayout.
public interface IWoodStack
{
    // Dik duran ana odunun uzunluğu
    int BaseLength { get; }

    // O katmandaki odunun uzunluğu, 0 = boş. Katman 0 (tepe) her zaman boştur; tepeye eklenen odun ana odunu uzatır.
    int PieceAt(int layer);
}
