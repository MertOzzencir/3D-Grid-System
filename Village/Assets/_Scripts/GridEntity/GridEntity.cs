using UnityEngine;

public abstract class GridEntity : MonoBehaviour
{
    // Pivot hücresinin dünya pozisyonu. Modelin pivot noktası tam burada durur.
    public Vector3Int OriginWorldPosition{get;set;}
    [SerializeField] private GridEntitySOBase data;
    [SerializeField] private GridMaskRotator.Rotation rotation = GridMaskRotator.Rotation.Deg0;

    // Yerleştirildiği andaki footprint. Sonradan rotasyon değişse bile bu sabit kalır.
    public GridFootprint PlacedFootprint { get; private set; }

    public GridMaskRotator.Rotation Rotation
    {
        get => rotation;
        set => rotation = value;
    }

    // Modelin olması gereken görsel rotasyonu
    public Quaternion GridRotation => GridMaskRotator.ToQuaternion(rotation);

    public virtual void OnPlaced(Vector3Int origin)
    {
        OriginWorldPosition = origin;
        PlacedFootprint = GetFootprint();
    }

    // Grid'de kendi kendine yer değiştiren entity'ler (canlılar) için; GridManager.TryMovePlaceable çağırır.
    // Transform'a dokunmaz: canlı kendi hareket eder, pivot kuralı (transform = origin) ona uygulanmaz.
    public void SetPlacement(Vector3Int origin, GridFootprint footprint)
    {
        OriginWorldPosition = origin;
        PlacedFootprint = footprint;
    }

    // Varsayılan olarak SO'dan gelir. Şekli runtime'da oluşan entity'ler (MergedWood) override eder.
    public virtual GridFootprint GetFootprint() => data.GetFootprint(rotation);

    // Grid'e yerleşikken çağırma: PlacedFootprint eski rotasyonda kalır. Elde tutarken kullan.
    public void RotateFootprint() => rotation = GridMaskRotator.Next(rotation);

    public GridEntitySOBase GetData() => data;

    // Bu obje hangi SO'dan yaratıldıysa onun verisi (aynı prefab'ı birden çok SO paylaşabilir: örn. bütün tarla parçası
    // şekilleri tek 1×1 prefab'tan). Yaratan her yer çağırır (GridEntitySOBase.Spawn); veri değişirse OnDataAssigned.
    public void AssignData(GridEntitySOBase so)
    {
        if (so == null || so == data) return;
        data = so;
        OnDataAssigned();
    }

    // Şekli / görseli veriye bağlı olanlar kendini yeniden kurar (örn. FarmPiece)
    protected virtual void OnDataAssigned() { }
}
