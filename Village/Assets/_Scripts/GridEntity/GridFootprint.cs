using System.Collections.Generic;
using UnityEngine;

public class GridFootprint
{
    public readonly Vector3Int Size;
    private readonly bool[] cells;

    public GridFootprint(Vector3Int size, bool[] cells)
    {
        Size = size;
        this.cells = cells;
    }

    public static int Index(Vector3Int size, int x, int y, int z) => y * size.x * size.z + z * size.x + x;

    public bool IsFilled(int x, int y, int z) => cells[Index(Size, x, y, z)];

  
    public IEnumerable<Vector3Int> FilledCells()
    {
        for (int y = 0; y < Size.y; y++)
            for (int z = 0; z < Size.z; z++)
                for (int x = 0; x < Size.x; x++)
                    if (IsFilled(x, y, z))
                        yield return new Vector3Int(x, y, z);
    }
}
