using UnityEngine;

public abstract class GridEntity : MonoBehaviour
{
    public Vector3Int OriginWorldPosition{get;set;}
    [SerializeField] private GridEntitySOBase data;
    [SerializeField] private GridMaskRotator.Rotation rotation = GridMaskRotator.Rotation.Deg0;

    public Vector2 PlacedSize { get; private set; }
    public bool[] PlacedMask { get; private set; }

    public virtual void OnPlaced(Vector3Int origin)
    {
        OriginWorldPosition = origin;
        var (size, mask) = GetFootprint();
        PlacedSize = size;
        PlacedMask = mask; 
    }

    public (Vector2 size, bool[] mask) GetFootprint()
        => GridMaskRotator.Rotate(data.Size, data.mask, rotation);

    public void RotateFootprint()
        => rotation = (GridMaskRotator.Rotation)(((int)rotation + 1) % 4);

    public GridEntitySOBase GetData() => data;
}