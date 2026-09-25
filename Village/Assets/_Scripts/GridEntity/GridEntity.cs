using UnityEngine;

public abstract class GridEntity : MonoBehaviour
{
    public Vector3Int OriginWorldPosition{get;set;}
    [SerializeField] private GridEntitySOBase data;
    [SerializeField] private GridMaskRotator.Rotation rotation = GridMaskRotator.Rotation.Deg0;

    // Yerleştirildiği andaki footprint. Sonradan rotasyon değişse bile bu sabit kalır.
    public GridFootprint PlacedFootprint { get; private set; }

    public virtual void OnPlaced(Vector3Int origin)
    {
        OriginWorldPosition = origin;
        PlacedFootprint = GetFootprint();
    }

    public GridFootprint GetFootprint()
        => GridMaskRotator.Rotate(data.GetFootprint(), rotation);

    public void RotateFootprint()
        => rotation = (GridMaskRotator.Rotation)(((int)rotation + 1) % 4);

    public GridEntitySOBase GetData() => data;
}
