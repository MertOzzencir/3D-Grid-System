using UnityEngine;

public static class GridMaskRotator
{
    public enum Rotation { Deg0, Deg90, Deg180, Deg270 }

    // Sadece yatay düzlemde döndürür; y (katman) değişmez.
    public static GridFootprint Rotate(GridFootprint source, Rotation rotation)
    {
        if (rotation == Rotation.Deg0) return source;

        Vector3Int s = source.Size;
        bool swapXZ = rotation == Rotation.Deg90 || rotation == Rotation.Deg270;
        Vector3Int newSize = swapXZ ? new Vector3Int(s.z, s.y, s.x) : s;
        bool[] result = new bool[s.x * s.y * s.z];

        for (int y = 0; y < s.y; y++)
            for (int z = 0; z < s.z; z++)
                for (int x = 0; x < s.x; x++)
                {
                    Vector2Int n = RotateXZ(x, z, s.x, s.z, rotation);
                    result[GridFootprint.Index(newSize, n.x, y, n.y)] = source.IsFilled(x, y, z);
                }

        return new GridFootprint(newSize, result);
    }

    private static Vector2Int RotateXZ(int x, int z, int width, int depth, Rotation rotation)
    {
        switch (rotation)
        {
            case Rotation.Deg90:  return new Vector2Int(depth - 1 - z, x);
            case Rotation.Deg180: return new Vector2Int(width - 1 - x, depth - 1 - z);
            case Rotation.Deg270: return new Vector2Int(z, width - 1 - x);
            default:              return new Vector2Int(x, z);
        }
    }
}
