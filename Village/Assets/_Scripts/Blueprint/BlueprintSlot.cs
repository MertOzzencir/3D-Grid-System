using UnityEngine;

// Blueprint'teki tek bir parça yeri: hangi parçayı istediğini (imza) ve parçanın rotasyonunu tutar.
// Pozisyonu ve rotasyonu kendi transform'unda; görseli ve collider'ları child/component olarak Blueprint Creator ekler.
public class BlueprintSlot : MonoBehaviour
{
    [SerializeField] private string signature;
    [SerializeField] private GridMaskRotator.Rotation rotation;

    public string Signature => signature;
    public GridMaskRotator.Rotation Rotation => rotation;

    // Bu parça slot'a uyar mı
    public bool Accepts(IBlueprintPiece piece) => piece != null && piece.BlueprintSignature == signature;

#if UNITY_EDITOR
    public void EditorSetup(string signature, GridMaskRotator.Rotation rotation)
    {
        this.signature = signature;
        this.rotation = rotation;
    }
#endif
}
