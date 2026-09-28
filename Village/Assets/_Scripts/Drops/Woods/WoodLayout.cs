using System.Collections.Generic;
using UnityEngine;

// Odun katmanlarının geometrisi. Hepsi base odunun pivot'una (alt hücresi) göre ve döndürülmemiş halde.
//
//        [0]          Katman 0      : tepe  → odunu uzatır (yeni prefab)
//       ┌───┐
//  y=1  │ B │ [1]     Katman 1..L   : sağ yan, yukarıdan aşağı
//  y=0  │ B │ [2]                   eklenen odun yatırılır ve sağa doğru uzanır
//       └───┘
public static class WoodLayout
{
    public const int TopLayer = 0;

    // Yan katmana eklenen odun yatırılır: odunun "yukarı" ekseni sağa döner
    private static readonly Quaternion SidePieceRotation = Quaternion.Euler(0, 0, -90);

    // Uzunluğu L olan bir odunun katman sayısı: 1 tepe + L yan
    public static int LayerCount(int baseLength) => baseLength + 1;

    // Yan katmanın yüksekliği (y). Katman 1 en üst hücre, katman L en alt hücre.
    public static int SideRow(int baseLength, int layer) => baseLength - layer;

    // Katmana eklenen odunun ilk hücresi = o odunun pivot'u
    public static Vector3Int PieceStart(int baseLength, int layer)
        => layer == TopLayer
            ? new Vector3Int(0, baseLength, 0)
            : new Vector3Int(1, SideRow(baseLength, layer), 0);

    // Katmana eklenen odunun rotasyonu (base'e göre)
    public static Quaternion PieceRotation(int layer)
        => layer == TopLayer ? Quaternion.identity : SidePieceRotation;

    private static Vector3Int PieceDirection(int layer)
        => layer == TopLayer ? Vector3Int.up : Vector3Int.right;

    // Tepeye odun eklenince ana odun uzar; yan odunlar olduğu yükseklikte kalır, katman numaraları kayar.
    // Yan katmanın yüksekliği (baseLength - layer) sabit kalsın diye: yeni katman = eski katman + eklenen uzunluk.
    // Örn. 2BR (katman 1: y=1, katman 2: y=0) + 1BR tepeye → 3BR: eski 1 → 2 (y=1), eski 2 → 3 (y=0), yeni 1 (y=2) boş.
    public static int[] LayersAfterTopMerge(IWoodStack stack, int addedLength)
    {
        var result = new int[LayerCount(stack.BaseLength + addedLength)];
        for (int layer = 1; layer < LayerCount(stack.BaseLength); layer++)
            result[layer + addedLength] = stack.PieceAt(layer);
        return result;
    }

    public static IEnumerable<Vector3Int> BaseCells(int baseLength)
    {
        for (int y = 0; y < baseLength; y++)
            yield return new Vector3Int(0, y, 0);
    }

    public static IEnumerable<Vector3Int> PieceCells(int baseLength, int layer, int pieceLength)
    {
        Vector3Int start = PieceStart(baseLength, layer);
        Vector3Int direction = PieceDirection(layer);
        for (int i = 0; i < pieceLength; i++)
            yield return start + direction * i;
    }
}
