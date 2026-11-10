using UnityEngine;

// Otomasyonda akan parça türü (cevher, külçe, robot...). Parçalar sahnede obje değil, makinelerin yuvalarında sayı olarak
// durur (kullanıcı: yerde bekleyen parça yok, sadece yuvadan yuvaya). Görünüşü: renkli küçük küp (prototip).
[CreateAssetMenu(fileName = "New Item", menuName = "Village/Automation/Item")]
public class ItemSO : ScriptableObject
{
    [Tooltip("Oyuncunun gördüğü ad (İngilizce)")]
    public string Name = "Item";
    [Tooltip("Prototip küpün rengi")]
    public Color Color = Color.gray;
}
