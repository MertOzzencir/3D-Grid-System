// Kaydı için ID + hücre + rotasyon yetmeyen objeler (örn. MergedWood) kendi durumunu metin olarak yazar ve okur.
// Save sistemi bu metnin içeriğini bilmez, olduğu gibi saklar.
public interface ISaveState
{
    string CaptureState();

    // Yüklemede, obje grid'e yerleştirilmeden ÖNCE çağrılır: footprint'i durumdan hesaplanan objeler hazır olsun
    void RestoreState(string state);
}
