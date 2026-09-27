using System.Collections.Generic;
using UnityEngine;

// Tek parça odun. Uzunluğu grid yüksekliğidir (SO Size.y). Katmanlar için bkz. WoodLayout.
public class Wood : SourceBase, IInteractable, IToolTarget, IBlueprintPiece
{
    [SerializeField] private float followSpeed;

    public int Length => GetData().Size.y;

    public string BlueprintSignature => $"Wood:{Length}";

    private GridDragMotor drag;
    private SnapPointSelector snapSelector;
    private readonly List<SnapPoint> snapPoints = new List<SnapPoint>();

    // WoodMerger'ın Awake'i bizimkinden sonra çalışabilir, o yüzden ilk ihtiyaçta oluşturuyoruz
    private SnapPointSelector SnapSelector
        => snapSelector ??= new SnapPointSelector(WoodMerger.Instance.SnapIndicatorPrefab, transform);

    private void Awake() => drag = new GridDragMotor(this, followSpeed);

    // --- IInteractable: bu odun elde taşınırken ---
    public void Interact(out bool finished)
    {
        if (!drag.TryUseOnTarget(out finished))
            finished = true;
    }

    public void InteractContractBeginnig() => drag.Begin();
    public void InteractContract(out bool success) { success = true; drag.Tick(); }
    public void ContractCancel() => drag.Cancel();

    // --- IToolTarget: başka bir odun bu oduna getirildiğinde ---
    public Vector3 GetToolTargetPosition(IInteractable interacted, out bool accept)
    {
        accept = false;
        if (!(interacted is Wood carried) || carried == this) return default;

        snapPoints.Clear();
        CollectSnapPoints(carried, snapPoints);

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!SnapSelector.UpdateSelection(snapPoints, ray)) return default;

        accept = true;
        return SnapSelector.Selected.Position;
    }

    public Quaternion GetToolTargetRotation()
        => snapSelector != null && snapSelector.HasSelection ? snapSelector.Selected.Rotation : transform.rotation;

    public void OnToolTargetExit() => snapSelector?.Hide();

    public bool OnToolUsed(IInteractable tool)
    {
        if (!(tool is Wood carried) || snapSelector == null || !snapSelector.HasSelection) return false;

        int layer = snapSelector.Selected.Layer;
        snapSelector.Hide();
        WoodMerger.Instance.Merge(this, carried, layer);
        return true;
    }

    // Taşınan odunun konabileceği katmanlar. Geçersiz olanlar (prefab yok / hücre dolu) hiç eklenmez.
    private void CollectSnapPoints(Wood carried, List<SnapPoint> result)
    {
        bool topPrefabExists = WoodMerger.Instance.Catalog.GetWood(Length + carried.Length) != null;

        for (int layer = 0; layer < WoodLayout.LayerCount(Length); layer++)
        {
            if (layer == WoodLayout.TopLayer && !topPrefabExists) continue;
            if (!ArePieceCellsFree(layer, carried.Length)) continue;

            Vector3Int start = GridMaskRotator.RotateOffset(WoodLayout.PieceStart(Length, layer), Rotation);
            Quaternion rotation = GridRotation * WoodLayout.PieceRotation(layer);
            result.Add(new SnapPoint(layer, OriginWorldPosition + start, rotation));
        }
    }

    private bool ArePieceCellsFree(int layer, int pieceLength)
    {
        foreach (Vector3Int cell in WoodLayout.PieceCells(Length, layer, pieceLength))
        {
            Vector3Int worldCell = OriginWorldPosition + GridMaskRotator.RotateOffset(cell, Rotation);
            if (!GridManager.Instance.IsPlaceableCellFree(worldCell)) return false;
        }
        return true;
    }
}
