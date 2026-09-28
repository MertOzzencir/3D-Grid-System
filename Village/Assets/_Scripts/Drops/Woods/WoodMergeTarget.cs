using System.Collections.Generic;
using UnityEngine;

// Wood ve MergedWood'un ortak hedef davranışı (IToolTarget'ı bununla uygularlar):
// taşınan tek parça odun için geçerli katman noktalarını hesaplar, mouse'a en yakınını seçtirir,
// tıklanınca WoodMerger'a iletir. Sahibi hem GridPlaceable hem IWoodStack olmalı.
public class WoodMergeTarget
{
    private readonly GridPlaceable owner;
    private readonly IWoodStack stack;
    private readonly List<SnapPoint> snapPoints = new List<SnapPoint>();
    private SnapPointSelector snapSelector;

    // WoodMerger'ın Awake'i sahibinkinden sonra çalışabilir, o yüzden ilk ihtiyaçta oluşturuyoruz
    private SnapPointSelector SnapSelector
        => snapSelector ??= new SnapPointSelector(WoodMerger.Instance.SnapIndicatorPrefab, owner.transform);

    public WoodMergeTarget(GridPlaceable owner)
    {
        this.owner = owner;
        stack = owner as IWoodStack;
        if (stack == null)
            Debug.LogError($"{owner.name} WoodMergeTarget kullanıyor ama IWoodStack değil", owner);
    }

    // Sadece tek parça Wood kabul edilir; MergedWood başka bir oduna eklenemez
    public Vector3 GetToolTargetPosition(IInteractable interacted, out bool accept)
    {
        accept = false;
        if (!(interacted is Wood carried) || ReferenceEquals(carried, owner)) return default;

        snapPoints.Clear();
        CollectSnapPoints(carried, snapPoints);

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!SnapSelector.UpdateSelection(snapPoints, ray)) return default;

        accept = true;
        return SnapSelector.Selected.Position;
    }

    public Quaternion GetToolTargetRotation()
        => snapSelector != null && snapSelector.HasSelection ? snapSelector.Selected.Rotation : owner.transform.rotation;

    public void OnToolTargetExit() => snapSelector?.Hide();

    public bool OnToolUsed(IInteractable tool)
    {
        if (!(tool is Wood carried) || snapSelector == null || !snapSelector.HasSelection) return false;

        int layer = snapSelector.Selected.Layer;
        snapSelector.Hide();
        WoodMerger.Instance.Merge(owner, carried, layer);
        return true;
    }

    // Taşınan odunun konabileceği katmanlar. Geçersizler (prefab yok / katman dolu / hücre dolu) hiç eklenmez.
    private void CollectSnapPoints(Wood carried, List<SnapPoint> result)
    {
        int baseLength = stack.BaseLength;
        bool topPrefabExists = WoodMerger.Instance.Catalog.GetWood(baseLength + carried.Length) != null;

        for (int layer = 0; layer < WoodLayout.LayerCount(baseLength); layer++)
        {
            if (layer == WoodLayout.TopLayer && !topPrefabExists) continue;
            if (stack.PieceAt(layer) > 0) continue;
            if (!ArePieceCellsFree(baseLength, layer, carried.Length)) continue;

            Vector3Int start = GridMaskRotator.RotateOffset(WoodLayout.PieceStart(baseLength, layer), owner.Rotation);
            Quaternion rotation = owner.GridRotation * WoodLayout.PieceRotation(layer);
            result.Add(new SnapPoint(layer, owner.OriginWorldPosition + start, rotation));
        }
    }

    private bool ArePieceCellsFree(int baseLength, int layer, int pieceLength)
    {
        foreach (Vector3Int cell in WoodLayout.PieceCells(baseLength, layer, pieceLength))
        {
            Vector3Int worldCell = owner.OriginWorldPosition + GridMaskRotator.RotateOffset(cell, owner.Rotation);
            if (!GridManager.Instance.IsPlaceableCellFree(worldCell)) return false;
        }
        return true;
    }
}
