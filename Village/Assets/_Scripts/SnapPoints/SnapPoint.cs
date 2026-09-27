using UnityEngine;

// Bir objeye başka bir objenin bağlanabileceği nokta (dünya uzayında)
public readonly struct SnapPoint
{
    public readonly int Layer;
    public readonly Vector3 Position;
    public readonly Quaternion Rotation;

    public SnapPoint(int layer, Vector3 position, Quaternion rotation)
    {
        Layer = layer;
        Position = position;
        Rotation = rotation;
    }
}
