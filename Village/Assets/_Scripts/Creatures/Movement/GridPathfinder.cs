using System;
using System.Collections.Generic;
using UnityEngine;

// Grid üstünde A* yol bulma (dünya hücreleri). Komşular 4 yön; her yönde aynı kat ya da bir kat yukarı/aşağı (zıplama).
// Hangi hücrenin yürünebilir olduğuna çağıran karar verir (canlıya göre değişir).
public static class GridPathfinder
{
    private static readonly Vector3Int[] Directions =
    {
        new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
    };

    // Kedi gibi yerde yürüyenler için: hücre boş (base ve placeable yok), altında base var
    public static bool IsStandable(Vector3Int cell)
    {
        GridManager grid = GridManager.Instance;
        if (grid == null || !grid.TryGetCell(cell, out GridData data)) return false;
        if (data.Base != null || data.Placeable != null) return false;
        return grid.TryGetCell(cell + Vector3Int.down, out GridData below) && below.Base != null;
    }

    // start'tan goal'a yol (start hariç, goal dahil). Bulunamazsa null. maxNodes: arama sınırı (büyük grid'de takılmasın).
    public static List<Vector3Int> FindPath(Vector3Int start, Vector3Int goal, Func<Vector3Int, bool> isWalkable, int maxNodes = 2000)
    {
        if (start == goal) return new List<Vector3Int>();
        if (!isWalkable(goal)) return null;

        var open = new List<Vector3Int> { start };
        var cameFrom = new Dictionary<Vector3Int, Vector3Int>();
        var cost = new Dictionary<Vector3Int, float> { [start] = 0f };
        var closed = new HashSet<Vector3Int>();

        while (open.Count > 0 && closed.Count < maxNodes)
        {
            // En düşük tahmini maliyetli düğüm (küçük alanlar için liste yeterli)
            int bestIndex = 0;
            float bestScore = float.MaxValue;
            for (int i = 0; i < open.Count; i++)
            {
                float score = cost[open[i]] + Heuristic(open[i], goal);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            Vector3Int current = open[bestIndex];
            if (current == goal) return Reconstruct(cameFrom, start, goal);

            open.RemoveAt(bestIndex);
            closed.Add(current);

            foreach (Vector3Int next in Neighbours(current, isWalkable))
            {
                if (closed.Contains(next)) continue;
                float nextCost = cost[current] + (next.y == current.y ? 1f : 1.5f); // zıplama biraz pahalı
                if (cost.TryGetValue(next, out float known) && known <= nextCost) continue;

                cost[next] = nextCost;
                cameFrom[next] = current;
                if (!open.Contains(next)) open.Add(next);
            }
        }
        return null;
    }

    public static IEnumerable<Vector3Int> Neighbours(Vector3Int cell, Func<Vector3Int, bool> isWalkable)
    {
        foreach (Vector3Int direction in Directions)
        {
            Vector3Int flat = cell + direction;
            if (isWalkable(flat)) { yield return flat; continue; }

            // Bir kat yukarı: önündeki hücre dolu ama üstü boş ve başının üstü açık
            Vector3Int up = flat + Vector3Int.up;
            if (isWalkable(up) && IsOpen(cell + Vector3Int.up)) { yield return up; continue; }

            // Bir kat aşağı
            Vector3Int down = flat + Vector3Int.down;
            if (isWalkable(down) && IsOpen(flat)) yield return down;
        }
    }

    // Hücre grid içinde ve boş (içinden geçilebilir)
    private static bool IsOpen(Vector3Int cell)
        => GridManager.Instance.TryGetCell(cell, out GridData data) && data.Base == null && data.Placeable == null;

    private static float Heuristic(Vector3Int a, Vector3Int b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z) + Mathf.Abs(a.y - b.y) * 1.5f;

    private static List<Vector3Int> Reconstruct(Dictionary<Vector3Int, Vector3Int> cameFrom, Vector3Int start, Vector3Int goal)
    {
        var path = new List<Vector3Int>();
        Vector3Int current = goal;
        while (current != start)
        {
            path.Add(current);
            current = cameFrom[current];
        }
        path.Reverse();
        return path;
    }
}
