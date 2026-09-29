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

    // Kameraya bakan yandaki parçalar noktaları kapatmasın diye yarı saydam yapılır (bkz. DitherFade)
    private bool frontFaded;
    private WoodSide fadedSide;
    private Renderer[] fadedRenderers;  // saydam olan ya da geri gelmekte olan parçalar
    private Coroutine restoreRoutine;

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

        WoodSide facing = GetFacingSide();
        snapPoints.Clear();
        CollectSnapPoints(carried, facing, snapPoints);

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!SnapSelector.UpdateSelection(snapPoints, ray)) return default;

        // Seçim yokken GridDragMotor her kare OnToolTargetExit çağırır; o yüzden sadece seçim varken saydamlaştır
        FadeFrontPieces(facing);
        accept = true;
        return SnapSelector.Selected.Position;
    }

    // Yapışkan hedef: taşınan odun bu oduna bir kez yapıştıysa, mouse collider'dan çıksa da bırakılmaz.
    // Mouse, hedefin kameraya bakan düzlemine yansıtılır; nokta ana odunun ekseninden (dikey çizgi) belli
    // mesafeden fazla uzaklaşınca bırakılır. Noktalar zaten mouse ışınına göre seçildiği için collider gerekmez.
    public bool IsStillTargeted(IInteractable interacted, Ray mouseRay)
    {
        if (!(interacted is Wood carried) || ReferenceEquals(carried, owner)) return false;

        Transform cameraTransform = Camera.main.transform;
        Vector3 bottom = owner.OriginWorldPosition;
        Vector3 top = bottom + Vector3.up * (stack.BaseLength - 1);
        var plane = new Plane(-cameraTransform.forward, (bottom + top) * 0.5f);
        if (!plane.Raycast(mouseRay, out float enter)) return false;

        Vector3 point = mouseRay.GetPoint(enter);
        Vector3 closestOnAxis = bottom + Vector3.up * Mathf.Clamp(point.y - bottom.y, 0f, top.y - bottom.y);
        return Vector3.Distance(point, closestOnAxis) <= WoodMerger.Instance.TargetReleaseDistance;
    }

    public Quaternion GetToolTargetRotation()
        => snapSelector != null && snapSelector.HasSelection ? snapSelector.Selected.Rotation : owner.transform.rotation;

    public void OnToolTargetExit()
    {
        snapSelector?.Hide();
        RestoreFrontPieces();
    }

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
    private void CollectSnapPoints(Wood carried, WoodSide facing, List<SnapPoint> result)
    {
        int baseLength = stack.BaseLength;

        if (WoodMerger.Instance.Catalog.GetWood(baseLength + carried.Length) != null)
            TryAddSnapPoint(WoodSlot.Top, baseLength, carried.Length, result);

        // Ön yanın sağındaki ve solundaki yanlar; ön ve arka gösterilmez
        WoodSide first = (WoodSide)(((int)facing + 1) % WoodLayout.SideCount);
        WoodSide second = (WoodSide)(((int)facing + 3) % WoodLayout.SideCount);
        foreach (WoodSide side in new[] { first, second })
            for (int layer = 1; layer <= baseLength; layer++)
                if (stack.PieceAt(side, layer) == 0)
                    TryAddSnapPoint(WoodSlot.At(side, layer), baseLength, carried.Length, result);
    }

    // Kameraya en çok bakan yan "ön" sayılır.
    // Odunun kendi rotasyonu da hesaba katılır (yanlar odunla birlikte döner).
    private WoodSide GetFacingSide()
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

        return facing;
    }

    // Ön yandaki parçaları yarı saydam yapar; zaten o yan saydamsa bir şey yapmaz.
    private void FadeFrontPieces(WoodSide facing)
    {
        if (frontFaded && fadedSide == facing) return;

        StopRestore();
        // Başka bir yan saydam kaldıysa (ön yan değiştiyse) o hemen eski haline döner
        if (fadedRenderers != null && fadedSide != facing)
            DitherFade.Clear(fadedRenderers);

        fadedSide = facing;
        fadedRenderers = stack.PieceRenderers(facing);
        frontFaded = true;
        if (fadedRenderers.Length > 0)
            WoodMerger.Instance.FrontFade.FadeOut(fadedRenderers);
    }

    private void RestoreFrontPieces()
    {
        if (!frontFaded) return;
        frontFaded = false;
        if (fadedRenderers.Length > 0 && owner != null && owner.isActiveAndEnabled)
            restoreRoutine = owner.StartCoroutine(WoodMerger.Instance.FrontFade.FadeIn(fadedRenderers));
    }

    private void StopRestore()
    {
        if (restoreRoutine == null) return;
        if (owner != null) owner.StopCoroutine(restoreRoutine);
        restoreRoutine = null;
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