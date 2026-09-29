// Blueprint'e kaydedilebilen ve bir slot'u doldurabilen parça (odun, taş, çit...).
public interface IBlueprintPiece
{
    // Parçanın türünü ve şeklini anlatan metin. Aynı imza = aynı parça, slot uyumu bununla kontrol edilir.
    // Tür adıyla başlasın ki farklı türler çakışmasın. Döndürmeden bağımsız olmalı (aynı şeklin her yöndeki hali aynı imza).
    string BlueprintSignature { get; }

    // Parçanın şekli, imzanın anlattığı standart haline göre kaç 90° adım dönük (0..3), kendi (yerel) yönlerinde.
    // Slot'a yerleştirirken parçanın dünyada doğru yöne bakması için gerekir. Simetrik parçalar (dik tek odun) 0 döner.
    int BlueprintRotationSteps { get; }
}
