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
    private readonly List<Vector3Int[]> candidateCells = new List<Vector3Int[]>(); // snapPoints ile aynı sırada: parçanın hücreleri (başından ucuna)
    private readonly List<Vector3Int> candidateDirections = new List<Vector3Int>(); // parçanın ana odundan dışarı uzandığı yön (dünya)

    // Taşınan odunun şu an üstünde durduğu (bağlantı yeri seçili) hedef; grid göstergesi (HeldGridIndicator) bağlantı
    // yerlerinin hücrelerini gösterir (eskiden ok göstergeleri vardı)
    public static WoodMergeTarget Active { get; private set; }
    public IReadOnlyList<Vector3Int[]> CandidateCells => candidateCells;
    public IReadOnlyList<Vector3Int> CandidateDirections => candidateDirections;
    public bool IsAlive => owner != null; // sahibi birleştirmede silinmiş olabilir

    // Kameranın ortalayacağı nokta: ana odunun ortası
    public Vector3 FocusPoint => (Vector3)owner.OriginWorldPosition + Vector3.up * ((stack.BaseLength - 1) * 0.5f);
    public int SelectedIndex => snapSelector != null ? snapSelector.SelectedIndex : -1;

    // Hücreler ve seçim değişince değişir (gösterge yeniden kurulsun)
    public int CandidatesKey
    {
        get
        {
            int key = SelectedIndex + 17;
            foreach (Vector3Int[] cells in candidateCells)
                foreach (Vector3Int cell in cells)
                    key = key * 31 + cell.GetHashCode();
            return key;
        }
    }
    private SnapPointSelector snapSelector;

    // Yapışma: ilk bağlantı yeri mouse'a en yakın olan, sonrası WASD ile (mouse seçimi değiştirmez); mouse yapıştığı
    // andaki yerinden TargetReleasePixels uzaklaşınca bırakılır
    private int lockedId = -1;
    private Vector3 attachMouse;
    private Vector2 lastInput;

    // Kameraya bakan yandaki parçalar noktaları kapatmasın diye yarı saydam yapılır (bkz. DitherFade)
    private bool frontFaded;
    private WoodSide fadedSide;
    private Renderer[] fadedRenderers;  // saydam olan ya da geri gelmekte olan parçalar
    private Coroutine restoreRoutine;

    // WoodMerger'ın Awake'i sahibinkinden sonra çalışabilir, o yüzden ilk ihtiyaçta oluşturuyoruz
    private SnapPointSelector SnapSelector
        => snapSelector ??= new SnapPointSelector(null, owner.transform); // ok yok: yerler grid göstergesiyle

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
        candidateCells.Clear();
        candidateDirections.Clear();
        CollectSnapPoints(carried, facing, snapPoints);

        int index = lockedId >= 0 ? snapPoints.FindIndex(p => p.Layer == lockedId) : -1;
        if (index >= 0)
        {
            index = StepWithKeys(index);
            SnapSelector.Select(snapPoints, index);
        }
        else
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (!SnapSelector.UpdateSelection(snapPoints, ray))
            {
                if (Active == this) Active = null;
                lockedId = -1;
                return default;
            }
            attachMouse = Input.mousePosition;
            lastInput = InputManager.MovementVectorNormalized(); // basılı tuş hemen kaydırmasın
        }
        lockedId = SnapSelector.Selected.Layer;
        Active = this;

        // Seçim yokken GridDragMotor her kare OnToolTargetExit çağırır; o yüzden sadece seçim varken saydamlaştır
        FadeFrontPieces(facing);
        accept = true;
        return SnapSelector.Selected.Position;
    }

    // Yapışkan hedef: taşınan odun bu oduna bir kez yapıştıysa, mouse collider'dan çıksa da bırakılmaz; mouse yapıştığı
    // andaki yerinden ekranda TargetReleasePixels'ten fazla uzaklaşınca bırakılır (birleştirmekten vazgeçildi)
    public bool IsStillTargeted(IInteractable interacted, Ray mouseRay)
    {
        if (!(interacted is Wood carried) || ReferenceEquals(carried, owner) || lockedId < 0) return false;
        Vector2 moved = Input.mousePosition - attachMouse;
        return moved.magnitude <= WoodMerger.Instance.TargetReleasePixels;
    }

    // WASD (kameraya göre: A/D ekranda sol/sağ, W/S yukarı/aşağı) bir basışta o yöndeki en yakın bağlantı yerine geçer
    private int StepWithKeys(int index)
    {
        Vector2 input = InputManager.MovementVectorNormalized();
        bool pressed = input.magnitude > 0.5f && lastInput.magnitude <= 0.5f;
        lastInput = input;
        if (!pressed) return index;

        Camera camera = Camera.main;
        Vector2 from = camera.WorldToScreenPoint(snapPoints[index].Position);
        Vector2 direction = input.normalized;
        int best = index;
        float bestScore = float.MaxValue;
        for (int i = 0; i < snapPoints.Count; i++)
        {
            if (i == index) continue;
            Vector2 delta = (Vector2)camera.WorldToScreenPoint(snapPoints[i].Position) - from;
            float along = Vector2.Dot(delta, direction);
            if (along <= 1f) continue; // o yönde değil
            float sideways = Mathf.Abs(delta.x * direction.y - delta.y * direction.x);
            if (sideways > along * 1.5f) continue; // çok yanda
            float score = along + sideways * 2f; // yöne yakın ve yakın olan
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return best;
    }

    public Quaternion GetToolTargetRotation()
        => snapSelector != null && snapSelector.HasSelection ? snapSelector.Selected.Rotation : owner.transform.rotation;

    public void OnToolTargetExit()
    {
        if (Active == this) Active = null;
        lockedId = -1;
        snapSelector?.Hide();
        RestoreFrontPieces();
    }

    public bool OnToolUsed(IInteractable tool)
    {
        if (!(tool is Wood carried) || snapSelector == null || !snapSelector.HasSelection) return false;
        if (Active == this) Active = null;

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

        // Tepe: toplam boy WoodLayout.MaxBaseLength'i geçmesin (3BR'i geçen kısım iple bağlanmış parça olur)
        if (baseLength + carried.Length <= WoodLayout.MaxBaseLength)
            TryAddSnapPoint(WoodSlot.Top, baseLength, carried.Length, result);

        // Ön yanın sağındaki ve solundaki yanlar; ön ve arka gösterilmez
        WoodSide first = (WoodSide)(((int)facing + 1) % WoodLayout.SideCount);
        WoodSide second = (WoodSide)(((int)facing + 3) % WoodLayout.SideCount);
        foreach (WoodSide side in new[] { first, second })
            for (int layer = 1; layer <= baseLength; layer++)
                if (stack.PieceAt(side, layer) == 0)
                    TryAddSnapPoint(WoodSlot.At(side, layer), baseLength, carried.Length, result);

        // Yan odunların dış uçları (dört yan): toplam boy WoodLayout.MaxBaseLength'i geçmesin
        for (int s = 0; s < WoodLayout.SideCount; s++)
            for (int layer = 1; layer <= baseLength; layer++)
            {
                int length = stack.PieceAt((WoodSide)s, layer);
                if (length > 0 && length + carried.Length <= WoodLayout.MaxBaseLength)
                    TryAddEndSnapPoint((WoodSide)s, layer, baseLength, length, carried.Length, result);
            }
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
        var cells = new List<Vector3Int>();
        foreach (Vector3Int cell in WoodLayout.PieceCells(baseLength, slot, pieceLength))
            cells.Add(owner.OriginWorldPosition + GridMaskRotator.RotateOffset(cell, owner.Rotation));
        candidateCells.Add(cells.ToArray());
        candidateDirections.Add(slot.IsTop ? Vector3Int.up : GridMaskRotator.RotateOffset(WoodLayout.SideDirection(slot.Side), owner.Rotation));
    }

    // Yan odunun ucu: eklenen odun o yan odunun devamında, aynı dönüşle
    private void TryAddEndSnapPoint(WoodSide side, int layer, int baseLength, int length, int pieceLength, List<SnapPoint> result)
    {
        WoodSlot pieceSlot = WoodSlot.At(side, layer);
        Vector3Int outward = WoodLayout.SideDirection(side);
        Vector3Int startLocal = WoodLayout.PieceStart(baseLength, pieceSlot) + outward * length;
        var cells = new List<Vector3Int>();
        for (int i = 0; i < pieceLength; i++)
        {
            Vector3Int world = owner.OriginWorldPosition + GridMaskRotator.RotateOffset(startLocal + outward * i, owner.Rotation);
            if (!GridManager.Instance.IsPlaceableCellFree(world)) return;
            cells.Add(world);
        }
        Quaternion rotation = owner.GridRotation * WoodLayout.PieceRotation(pieceSlot);
        result.Add(new SnapPoint(WoodSlot.EndOf(side, layer).ToId(), cells[0], rotation));
        candidateCells.Add(cells.ToArray());
        candidateDirections.Add(GridMaskRotator.RotateOffset(outward, owner.Rotation));
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