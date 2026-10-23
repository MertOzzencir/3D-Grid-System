using System.Collections.Generic;
using UnityEngine;

// Wood ve MergedWood'un ortak hedef davranışı (IToolTarget'ı bununla uygularlar):
// taşınan tek parça odun için geçerli bağlantı noktalarını hesaplar, mouse'a en yakınını seçtirir,
// tıklanınca WoodMerger'a iletir. Sahibi hem GridPlaceable hem IWoodStack olmalı.
public class WoodMergeTarget : IWoodSlotView
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

    // Yapışma: ilk bağlantı yeri ana odunun tepesi (yoksa mouse'a en yakın olan), sonrası WASD ile (mouse seçimi
    // değiştirmez); mouse yapıştığı andaki yerinden TargetReleasePixels uzaklaşınca bırakılır
    private int lockedId = -1;
    private int lastSideColumn = -1; // en son bulunulan yan (tepeden S ile oraya inilir)
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

    // Başka bir oduna eklenebilen parça ve boyu: tek 1×1 (Wood) ya da yanları boş dik yığın (MergedWood.IsStack: iple bağlı
    // 1×1'ler, eski 2BR / 3BR odun gibi). Yanında odun olan MergedWood eklenemez.
    public static bool TryGetPiece(IInteractable interacted, out GridPlaceable piece, out int length)
    {
        switch (interacted)
        {
            case Wood wood:
                piece = wood;
                length = wood.Length;
                return true;
            case MergedWood stack when stack.IsStack:
                piece = stack;
                length = stack.BaseLength;
                return true;
            default:
                piece = null;
                length = 0;
                return false;
        }
    }

    // Taşınan parça: tek 1×1 ya da yanları boş dik yığın (TryGetPiece)
    public Vector3 GetToolTargetPosition(IInteractable interacted, out bool accept)
    {
        accept = false;
        if (!TryGetPiece(interacted, out GridPlaceable carried, out int carriedLength) || ReferenceEquals(carried, owner)) return default;

        WoodSide facing = FacingSide(owner);
        snapPoints.Clear();
        candidateCells.Clear();
        candidateDirections.Clear();
        CollectSnapPoints(carriedLength, facing, snapPoints);

        int index = lockedId >= 0 ? snapPoints.FindIndex(p => p.Layer == lockedId) : -1;
        if (index >= 0)
        {
            index = StepWithKeys(index);
            SnapSelector.Select(snapPoints, index);
        }
        else
        {
            int top = snapPoints.FindIndex(p => p.Layer == WoodSlot.Top.ToId());
            if (top >= 0)
            {
                SnapSelector.Select(snapPoints, top); // hep ana odunun tepesinden (uzunlamasına) başlar
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
            }
            lastSideColumn = -1;
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
        if (!TryGetPiece(interacted, out GridPlaceable carried, out _) || ReferenceEquals(carried, owner) || lockedId < 0) return false;
        Vector2 moved = Input.mousePosition - attachMouse;
        return moved.magnitude <= WoodMerger.Instance.TargetReleasePixels;
    }

    // WASD bir basışta bir adım. Bağlantı yerleri mantıksal ızgarada: sütun = yan (tepe ayrı sütun), satır = yükseklik.
    // W / S: aynı yanda bir üst / alt kat (kat atlamaz; o katta yan odun varsa yer o odunun ucudur); en üst kattan W tepeye
    // çıkar, tepeden S en son bulunulan yanın (yoksa en yakın yanın) en üst katına iner. A / D: karşı tarafa doğru basınca aynı
    // katta karşı yan (doluysa karşı yanda yukarı doğru, o da yoksa tepe), kendi tarafına doğru basınca hiçbir şey.
    private int StepWithKeys(int index)
    {
        Vector2 input = InputManager.MovementVectorNormalized();
        bool pressed = input.magnitude > 0.5f && lastInput.magnitude <= 0.5f;
        lastInput = input;
        if (!pressed) return index;

        Slot(index, out int column, out int row);
        if (column >= 0) lastSideColumn = column;

        if (Mathf.Abs(input.y) >= Mathf.Abs(input.x))
        {
            int up = input.y > 0f ? 1 : -1;
            if (column < 0) // tepeden aşağı
            {
                if (up > 0) return index;
                int best = -1, bestRow = int.MinValue;
                foreach (int candidate in new[] { lastSideColumn, -2 })
                {
                    for (int i = 0; i < snapPoints.Count; i++)
                    {
                        Slot(i, out int c, out int r);
                        if (c < 0 || (candidate >= 0 && c != candidate)) continue;
                        if (r > bestRow) { bestRow = r; best = i; }
                    }
                    if (best >= 0) return best;
                }
                return index;
            }
            return VerticalStep(index, column, row, up);
        }

        // A / D (ekrana göre yönlü): karşı tarafa doğru basınca bulunduğu katta karşı yana; orası doluysa karşı yanda bir üst
        // kattan yukarı doğru; o da yoksa tepe; o da yoksa bir şey yapma. Kendi tarafına doğru basınca hiçbir şey yapma.
        // Tepedeyken: A ekranda soldaki, D sağdaki yanın en üst katına.
        if (column < 0)
        {
            Camera camera = Camera.main;
            float x = camera.WorldToScreenPoint(snapPoints[index].Position).x;
            float direction = input.x > 0f ? 1f : -1f;
            int best = -1, bestRow = int.MinValue;
            for (int i = 0; i < snapPoints.Count; i++)
            {
                Slot(i, out int c, out int r);
                if (c < 0) continue;
                if ((camera.WorldToScreenPoint(snapPoints[i].Position).x - x) * direction <= 1f) continue;
                if (r > bestRow) { bestRow = r; best = i; }
            }
            return best >= 0 ? best : index;
        }

        // Kendi tarafına doğru (soldayken A, sağdayken D): hiçbir şey yapma (yukarı / aşağı sadece W / S ile)
        float side = Camera.main.WorldToScreenPoint(snapPoints[index].Position).x - Camera.main.WorldToScreenPoint(FocusPoint).x;
        if (side * input.x > 0f) return index;

        // Karşı tarafa doğru: aynı katta karşı yan
        int opposite = (column + 2) % WoodLayout.SideCount;
        int found = -1, foundRow = int.MaxValue;
        for (int i = 0; i < snapPoints.Count; i++)
        {
            Slot(i, out int c, out int r);
            if (c == opposite && r >= row && r < foundRow) { foundRow = r; found = i; }
        }
        if (found >= 0) return found;
        int topIndex = snapPoints.FindIndex(p => p.Layer == WoodSlot.Top.ToId());
        return topIndex >= 0 ? topIndex : index;
    }

    // Aynı yanda bir sonraki kat (en yakın, kat atlamaz); en üst kattan yukarı tepeye
    private int VerticalStep(int index, int column, int row, int up)
    {
        int next = -1, nextDistance = int.MaxValue;
        for (int i = 0; i < snapPoints.Count; i++)
        {
            Slot(i, out int c, out int r);
            int distance = (r - row) * up;
            if (c == column && distance > 0 && distance < nextDistance) { nextDistance = distance; next = i; }
        }
        if (next >= 0) return next;
        if (up > 0)
        {
            int top = snapPoints.FindIndex(p => p.Layer == WoodSlot.Top.ToId());
            if (top >= 0) return top;
        }
        return index;
    }

    // Bağlantı yerinin sütunu (yan; tepe -1) ve satırı (yüksekliği)
    private void Slot(int index, out int column, out int row)
    {
        WoodSlot slot = WoodSlot.FromId(snapPoints[index].Layer);
        if (slot.IsTop)
        {
            column = -1;
            row = stack.BaseLength;
            return;
        }
        column = (int)slot.Side;
        row = WoodLayout.SideRow(stack.BaseLength, slot.Layer);
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
        if (!TryGetPiece(tool, out GridPlaceable carried, out int carriedLength) || snapSelector == null || !snapSelector.HasSelection) return false;
        if (Active == this) Active = null;

        WoodSlot slot = WoodSlot.FromId(snapSelector.Selected.Layer);
        snapSelector.Hide();
        WoodMerger.Instance.Merge(owner, carried, carriedLength, slot);
        return true;
    }

    // Taşınan odunun konabileceği yerler: tepe + kameraya bakan yüzün sağındaki ve solundaki yanlar.
    // Geçersizler (prefab yok / katman dolu / hücre dolu) hiç eklenmez.
    private void CollectSnapPoints(int carriedLength, WoodSide facing, List<SnapPoint> result)
    {
        int baseLength = stack.BaseLength;

        // Tepe: toplam boy WoodLayout.MaxBaseLength'i geçmesin (eklenen 1×1'ler iple bağlanır)
        if (baseLength + carriedLength <= WoodLayout.MaxBaseLength)
            TryAddSnapPoint(WoodSlot.Top, baseLength, carriedLength, result);

        // Ön yanın sağındaki ve solundaki yanlar; ön ve arka gösterilmez
        WoodSide first = (WoodSide)(((int)facing + 1) % WoodLayout.SideCount);
        WoodSide second = (WoodSide)(((int)facing + 3) % WoodLayout.SideCount);
        foreach (WoodSide side in new[] { first, second })
            for (int layer = 1; layer <= baseLength; layer++)
                if (stack.PieceAt(side, layer) == 0)
                    TryAddSnapPoint(WoodSlot.At(side, layer), baseLength, carriedLength, result);

        // Yan odunların dış uçları (dört yan): toplam boy WoodLayout.MaxBaseLength'i geçmesin
        for (int s = 0; s < WoodLayout.SideCount; s++)
            for (int layer = 1; layer <= baseLength; layer++)
            {
                int length = stack.PieceAt((WoodSide)s, layer);
                if (length > 0 && length + carriedLength <= WoodLayout.MaxBaseLength)
                    TryAddEndSnapPoint((WoodSide)s, layer, baseLength, length, carriedLength, result);
            }
    }

    // Kameraya en çok bakan yan "ön" sayılır.
    // Odunun kendi rotasyonu da hesaba katılır (yanlar odunla birlikte döner).
    public static WoodSide FacingSide(GridPlaceable owner)
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