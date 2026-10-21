using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Dik odunun dört yanı; odunun kendi yönlerine göre (odun R ile dönünce yanlar da döner).
// Sıra önemli: bir 90° dönüş (Deg0 → Deg90) forward'daki parçayı right'a taşır, yani F→R→B→L.
public enum WoodSide { Forward = 0, Right = 1, Back = 2, Left = 3 }

// Odunun bir bağlantı yeri: tepe (uzatır), bir yandaki katman ya da yandaki odunun dış ucu (o yan odunu uzatır).
// Yan katmanlar yukarıdan aşağı numaralanır: katman 1 en üst hücre, katman L en alt hücre.
public readonly struct WoodSlot
{
    private const int MaxLayers = 64;

    public readonly bool IsTop;
    public readonly bool IsEnd; // yandaki (Side, Layer) odunun dış ucu
    public readonly WoodSide Side;
    public readonly int Layer;

    public static WoodSlot Top => new WoodSlot(true, false, default, 0);
    public static WoodSlot At(WoodSide side, int layer) => new WoodSlot(false, false, side, layer);
    public static WoodSlot EndOf(WoodSide side, int layer) => new WoodSlot(false, true, side, layer);

    private WoodSlot(bool isTop, bool isEnd, WoodSide side, int layer)
    {
        IsTop = isTop;
        IsEnd = isEnd;
        Side = side;
        Layer = layer;
    }

    // SnapPoint'e tek bir sayı olarak koyup geri okumak için (uçlar katmanlardan sonra)
    public int ToId() => IsTop ? 0 : 1 + (IsEnd ? WoodLayout.SideCount * MaxLayers : 0) + (int)Side * MaxLayers + (Layer - 1);

    public static WoodSlot FromId(int id)
    {
        if (id == 0) return Top;
        int rest = id - 1;
        bool end = rest >= WoodLayout.SideCount * MaxLayers;
        if (end) rest -= WoodLayout.SideCount * MaxLayers;
        var side = (WoodSide)(rest / MaxLayers);
        int layer = rest % MaxLayers + 1;
        return end ? EndOf(side, layer) : At(side, layer);
    }
}

// Odun katmanlarının geometrisi ve şekil kuralları. Hepsi ana odunun pivot'una (alt hücresi) göre, döndürülmemiş.
//
//   yukarıdan bakış:        yandan bakış (2BR, bir yan):
//          F                        [tepe]
//        L B R                 y=1  │B│[katman 1]
//          B(ack)              y=0  │B│[katman 2]
//
// Yan katmana eklenen odun yatırılır ve o yanın yönünde uzanır.
public static class WoodLayout
{
    public const int SideCount = 4;

    // --- Geometri ---

    public static Vector3Int SideDirection(WoodSide side) => side switch
    {
        WoodSide.Forward => new Vector3Int(0, 0, 1),
        WoodSide.Right => new Vector3Int(1, 0, 0),
        WoodSide.Back => new Vector3Int(0, 0, -1),
        _ => new Vector3Int(-1, 0, 0),
    };

    // Yan katmanın yüksekliği (y). Katman 1 en üst hücre, katman L en alt hücre.
    public static int SideRow(int baseLength, int layer) => baseLength - layer;

    // Eklenen odunun ilk hücresi (= o odunun pivot'u)
    public static Vector3Int PieceStart(int baseLength, WoodSlot slot)
        => slot.IsTop
            ? new Vector3Int(0, baseLength, 0)
            : SideDirection(slot.Side) + Vector3Int.up * SideRow(baseLength, slot.Layer);

    // Eklenen odunun rotasyonu: tepede dik, yanda "yukarı" ekseni o yana yatırılır
    public static Quaternion PieceRotation(WoodSlot slot)
        => slot.IsTop ? Quaternion.identity : Quaternion.FromToRotation(Vector3.up, SideDirection(slot.Side));

    private static Vector3Int PieceDirection(WoodSlot slot) => slot.IsTop ? Vector3Int.up : SideDirection(slot.Side);

    public static IEnumerable<Vector3Int> BaseCells(int baseLength)
    {
        for (int y = 0; y < baseLength; y++)
            yield return new Vector3Int(0, y, 0);
    }

    public static IEnumerable<Vector3Int> PieceCells(int baseLength, WoodSlot slot, int pieceLength)
    {
        Vector3Int start = PieceStart(baseLength, slot);
        Vector3Int direction = PieceDirection(slot);
        for (int i = 0; i < pieceLength; i++)
            yield return start + direction * i;
    }

    // --- Veri ---

    // Her yan için katman dizisi: sides[yan][katman - 1] = o katmandaki odunun uzunluğu, 0 = boş
    public static int[][] EmptySides(int baseLength)
    {
        var sides = new int[SideCount][];
        for (int s = 0; s < SideCount; s++)
            sides[s] = new int[baseLength];
        return sides;
    }

    public static int[][] CopySides(IWoodStack stack)
    {
        int[][] sides = EmptySides(stack.BaseLength);
        for (int s = 0; s < SideCount; s++)
            for (int layer = 1; layer <= stack.BaseLength; layer++)
                sides[s][layer - 1] = stack.PieceAt((WoodSide)s, layer);
        return sides;
    }

    // Tepeye odun eklenince ana odun uzar; yan odunlar olduğu yükseklikte kalır, katman numaraları kayar.
    // Yüksekliğin (baseLength - layer) sabit kalması için: yeni katman = eski katman + eklenen uzunluk. Dört yan birlikte.
    // Örn. 2BR (katman 1: y=1, katman 2: y=0) + 1BR tepeye → 3BR: eski 1 → 2, eski 2 → 3, yeni 1 (y=2) boş.
    // Ana odunun toplam boyu en fazla bu (doğal modeller 3BR'e kadar; üstü iple bağlanmış parçalar)
    public const int MaxBaseLength = 6;
    public const int MaxNaturalLength = 3;

    // Ana odunun parçaları (aşağıdan yukarı): parça kaydı yoksa doğal modellerden en uzun olanlarla (örn. 5 → 3, 2)
    public static int[] DefaultSegments(int baseLength)
    {
        var segments = new List<int>();
        for (int left = baseLength; left > 0; left -= MaxNaturalLength)
            segments.Add(Mathf.Min(left, MaxNaturalLength));
        return segments.ToArray();
    }

    // Tepeye odun eklenince parçalar: üstteki parçayla toplamı 3'ü geçmiyorsa ve o boyda model varsa (1 + 2 = 3BR) tek
    // parçaya birleşir (doğal model), yoksa üstüne yeni parça olur (aradaki ek yeri iple bağlanır)
    public static int[] SegmentsAfterTopMerge(int[] segments, int addedLength, WoodCatalogSO catalog)
    {
        var result = new List<int>(segments);
        int last = result.Count > 0 ? result[result.Count - 1] : 0;
        if (last > 0 && last + addedLength <= MaxNaturalLength && catalog.GetWood(last + addedLength) != null)
            result[result.Count - 1] = last + addedLength;
        else
            result.Add(addedLength);
        return result.ToArray();
    }

    // Yan odunların parçaları: segs[yan][katman - 1] = o yan odunun parçaları (içten dışa) ya da null (tek parça / boş)
    public static int[][][] CopySideSegments(IWoodStack stack)
    {
        var segs = new int[SideCount][][];
        for (int s = 0; s < SideCount; s++)
        {
            segs[s] = new int[stack.BaseLength][];
            for (int layer = 1; layer <= stack.BaseLength; layer++)
                segs[s][layer - 1] = stack.SideSegments((WoodSide)s, layer);
        }
        return segs;
    }

    // Yan odunun parçaları (içten dışa): kayıtlı ve toplamı uzunluğa eşitse o, yoksa doğal modellerden
    public static int[] PieceSegments(IWoodStack stack, WoodSide side, int layer)
    {
        int length = stack.PieceAt(side, layer);
        int[] stored = stack.SideSegments(side, layer);
        if (stored != null)
        {
            int sum = 0;
            foreach (int segment in stored) sum += segment;
            if (sum == length) return stored;
        }
        return DefaultSegments(length);
    }

    // Tepeye odun eklenince katman numaraları kayar (yan odunlar aynı yükseklikte kalır), parçaları da onlarla
    public static int[][][] SideSegmentsAfterTopMerge(IWoodStack stack, int addedLength)
    {
        int[][][] old = CopySideSegments(stack);
        var segs = new int[SideCount][][];
        for (int s = 0; s < SideCount; s++)
        {
            segs[s] = new int[stack.BaseLength + addedLength][];
            for (int layer = 1; layer <= stack.BaseLength; layer++)
                segs[s][layer + addedLength - 1] = old[s][layer - 1];
        }
        return segs;
    }

    public static int[][] SidesAfterTopMerge(IWoodStack stack, int addedLength)
    {
        int[][] sides = EmptySides(stack.BaseLength + addedLength);
        for (int s = 0; s < SideCount; s++)
            for (int layer = 1; layer <= stack.BaseLength; layer++)
                sides[s][layer + addedLength - 1] = stack.PieceAt((WoodSide)s, layer);
        return sides;
    }

    public static bool IsEmpty(int[][] sides)
    {
        foreach (int[] side in sides)
            foreach (int length in side)
                if (length != 0) return false;
        return true;
    }

    // --- Şekil imzası ---

    // Döndürmeden bağımsız imza. Odunu 90° döndürmek yanları bütün halinde kaydırmak demek (F→R→B→L);
    // 4 dönüşün imzasından hep aynı kuralla (sıralamada en küçüğü) biri seçilir. Böylece aynı şeklin her yöndeki
    // hali aynı imzayı üretir. Ayna görüntüsü hiçbir dönüşle eşleşmediği için farklı imza üretir.
    // steps: standart hale gelmek için kaç adım döndürüldüğü (blueprint'e yerleştirirken gerekecek).
    public static string CanonicalSignature(int baseLength, int[][] sides, out int steps)
    {
        string best = null;
        steps = 0;
        for (int k = 0; k < SideCount; k++)
        {
            string candidate = SidesToString(baseLength, sides, k);
            if (best == null || string.CompareOrdinal(candidate, best) < 0)
            {
                best = candidate;
                steps = k;
            }
        }
        return best;
    }

    // k adım döndürülmüş hal: i. yandaki parça (i + k). yana geçer
    private static string SidesToString(int baseLength, int[][] sides, int k)
    {
        var builder = new StringBuilder();
        builder.Append(baseLength);
        for (int s = 0; s < SideCount; s++)
        {
            int[] side = sides[(s - k + SideCount) % SideCount];
            builder.Append('|').Append(string.Join(",", side));
        }
        return builder.ToString();
    }
}
