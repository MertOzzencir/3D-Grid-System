using System.Collections.Generic;
using UnityEngine;

// Bir odunun WASD ile gezilen yerleri (birleştirme: WoodMergeTarget'ın bağlantı yerleri, sökme: WoodDetachTarget'ın ek
// yerleri). Grid göstergesi (HeldGridIndicator) bunları çizer, kamera FocusPoint'i ortalar.
public interface IWoodSlotView
{
    // Her yer için hücreler (yerin değdiği yüzden dışarı doğru, sırayla) ve dışarı uzandığı yön (dünya)
    IReadOnlyList<Vector3Int[]> CandidateCells { get; }
    IReadOnlyList<Vector3Int> CandidateDirections { get; }
    int SelectedIndex { get; }
    // Hücreler ve seçim değişince değişir (gösterge yeniden kurulsun)
    int CandidatesKey { get; }
    bool IsAlive { get; }
    Vector3 FocusPoint { get; }
}
