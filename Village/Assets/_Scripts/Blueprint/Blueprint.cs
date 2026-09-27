using System.Collections.Generic;
using UnityEngine;

// Blueprint Creator'ın ürettiği "boyama kitabı" objesi. Grid'de tüm slot'larının hücrelerini kaplar,
// böylece üstüne başka bir parça konamaz. Slot'lar child olarak durur.
public class Blueprint : GridPlaceable
{
    // Tüm slot'ların kapladığı hücreler, blueprint pivot'una göre, döndürülmemiş
    [SerializeField] private List<Vector3Int> cells = new List<Vector3Int>();

    private GridFootprint[] footprints;
    private BlueprintSlot[] slots;

    public IReadOnlyList<BlueprintSlot> Slots => slots ??= GetComponentsInChildren<BlueprintSlot>(true);

    public override GridFootprint GetFootprint()
    {
        footprints ??= GridMaskRotator.AllRotations(new GridFootprint(cells.ToArray()));
        return footprints[(int)Rotation];
    }

#if UNITY_EDITOR
    public void EditorSetCells(IEnumerable<Vector3Int> newCells)
    {
        cells = new List<Vector3Int>(newCells);
        footprints = null;
    }
#endif
}
