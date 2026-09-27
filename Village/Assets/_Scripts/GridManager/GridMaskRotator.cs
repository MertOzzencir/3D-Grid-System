using UnityEngine;

public static class GridMaskRotator
{
    public enum Rotation { Deg0, Deg90, Deg180, Deg270 }

    public static Rotation Next(Rotation rotation) => (Rotation)(((int)rotation + 1) % 4);

    // Modele uygulanacak görsel rotasyon. RotateOffset ile birebir aynı yönde döner.
    public static Quaternion ToQuaternion(Rotation rotation) => Quaternion.Euler(0, 90 * (int)rotation, 0);

    // Her hücreyi pivot etrafında döndürür; y (katman) değişmez.
    public static GridFootprint Rotate(GridFootprint source, Rotation rotation)
    {
        if (rotation == Rotation.Deg0) return source;

        var cells = source.FilledCells();
        var result = new Vector3Int[cells.Count];
        for (int i = 0; i < cells.Count; i++)
            result[i] = RotateOffset(cells[i], rotation);

        return new GridFootprint(result);
    }

    // 4 rotasyonun hepsini bir kerede hesaplar (index = (int)Rotation). Footprint'i önbelleğe almak için.
    public static GridFootprint[] AllRotations(GridFootprint unrotated)
    {
        var result = new GridFootprint[4];
        for (int i = 0; i < 4; i++)
            result[i] = Rotate(unrotated, (Rotation)i);
        return result;
    }

    // Unity'nin Y rotasyonuyla aynı: yukarıdan bakınca saat yönünde.
    // Örn. Deg90: ileri (0,0,1) → sağ (1,0,0), sağ (1,0,0) → geri (0,0,-1)
    public static Vector3Int RotateOffset(Vector3Int c, Rotation rotation)
    {
        switch (rotation)
        {
            case Rotation.Deg90:  return new Vector3Int(c.z, c.y, -c.x);
            case Rotation.Deg180: return new Vector3Int(-c.x, c.y, -c.z);
            case Rotation.Deg270: return new Vector3Int(-c.z, c.y, c.x);
            default:              return c;
        }
    }
}
