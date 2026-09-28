using System.Collections.Generic;
using UnityEngine;

// Wood ve MergedWood'un ortak hedef davranışı (IToolTarget'ı bununla uygularlar):
// taşınan tek parça odun için geçerli bağlantı noktalarını hesaplar, mouse'a en yakınını seçtirir,
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

        WoodSlot slot = WoodSlot.FromId(snapSelector.Selected.Layer);
        snapSelector.Hide();
        WoodMerger.Instance.Merge(owner, carried, slot);
        return true;
    }

    // Taşınan odunun konabileceği yerler: tepe + kameraya bakan yüzün sağındaki ve solundaki yanlar.
    // Geçersizler (prefab yok / katman dolu / hücre dolu) hiç eklenmez.
    private void CollectSnapPoints(Wood carried, List<SnapPoint> result)
    {
        int baseLength = stack.BaseLength;

        if (WoodMerger.Instance.Catalog.GetWood(baseLength + carried.Length) != null)
            TryAddSnapPoint(WoodSlot.Top, baseLength, carried.Length, result);

        GetVisibleSides(out WoodSide first, out WoodSide second);
        foreach (WoodSide side in new[] { first, second })
            for (int layer = 1; layer <= baseLength; layer++)
                if (stack.PieceAt(side, layer) == 0)
                    TryAddSnapPoint(WoodSlot.At(side, layer), baseLength, carried.Length, result);
    }

    // Kameraya en çok bakan yan "ön" sayılır; onun sağındaki ve solundaki yanlar döner. Ön ve arka gösterilmez.
    // Odunun kendi rotasyonu da hesaba katılır (yanlar odunla birlikte döner).
    private void GetVisibleSides(out WoodSide first, out WoodSide second)
    {
        Vector3 toCamera = Vector3.ProjectOnPlane(-Camera.main.transform.forward, Vector3.up);
        WoodSide facing = WoodSide.Forward;
        float best = float.MinValue;

        for (int s = 0; s < WoodLayout.SideCount; s++)
        {
            Vector3 sideWorld = owner.GridRotation * (Vector3)WoodLayout.SideDirection((WoodSide)s);
            float alignment = Vector3.Dot(sideWorld, toCamera);
            if (alignment > best)
            {
                best = alignment;
                facing = (WoodSide)s;
            }
        }

        first = (WoodSide)(((int)facing + 1) % WoodLayout.SideCount);
        second = (WoodSide)(((int)facing + 3) % WoodLayout.SideCount);
    }

    private void TryAddSnapPoint(WoodSlot slot, int baseLength, int pieceLength, List<SnapPoint> result)
    {
        if (!ArePieceCellsFree(baseLength, slot, pieceLength)) return;

        Vector3Int start = GridMaskRotator.RotateOffset(WoodLayout.PieceStart(baseLength, slot), owner.Rotation);
        Quaternion rotation = owner.GridRotation * WoodLayout.PieceRotation(slot);
        result.Add(new SnapPoint(slot.ToId(), owner.OriginWorldPosition + start, rotation));
    }

    private bool ArePieceCellsFree(int baseLength, WoodSlot slot, int pieceLength)
    {
        foreach (Vector3Int cell in WoodLayout.PieceCells(baseLength, slot, pieceLength))
        {
            Vector3Int worldCell = owner.OriginWorldPosition + GridMaskRotator.RotateOffset(cell, owner.Rotation);
            if (!GridManager.Instance.IsPlaceableCellFree(worldCell)) return false;
        }
        return true;
    }
}