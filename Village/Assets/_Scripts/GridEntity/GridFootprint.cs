using System.Collections.Generic;
using UnityEngine;

// Bir entity'nin kapladığı hücreler, pivot hücresine (0,0,0) göre offset olarak.
// Pivot = modelin pivot noktası = OriginWorldPosition. Döndürülünce offsetler negatif olabilir.
public class GridFootprint
{
    private readonly Vector3Int[] cells;

    public Vector3Int Min { get; }
    public Vector3Int Max { get; }
    public Vector3Int Size => Max - Min + Vector3Int.one;

    // Footprint'in merkezi, pivot'a göre (dünya birimiyle)
    public Vector3 Center => (Vector3)(Min + Max) / 2f;

    public GridFootprint(Vector3Int[] cells)
    {
        this.cells = cells;
        if (cells.Length == 0) return;

        Min = cells[0];
        Max = cells[0];
        foreach (Vector3Int c in cells)
        {
            Min = Vector3Int.Min(Min, c);
            Max = Vector3Int.Max(Max, c);
        }
    }

    public IReadOnlyList<Vector3Int> FilledCells() => cells;
}
