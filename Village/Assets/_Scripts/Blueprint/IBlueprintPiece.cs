// Blueprint'e kaydedilebilen ve ileride bir slot'u doldurabilen parça (odun, taş, çit...).
public interface IBlueprintPiece
{
    // Parçanın türünü ve şeklini anlatan metin. Aynı imza = aynı parça, slot uyumu bununla kontrol edilir.
    // Tür adıyla başlasın ki farklı türler çakışmasın. Örn. "Wood:2", "MergedWood:2:0,1,0"
    string BlueprintSignature { get; }
}
