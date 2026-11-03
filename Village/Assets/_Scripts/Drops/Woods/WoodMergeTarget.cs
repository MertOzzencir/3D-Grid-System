using System.Collections.Generic;
using UnityEngine;

// Wood ve MergedWood'un ortak birleştirme hedefi (IToolTarget'ı bununla uygularlar). Taşınan 1×1 (ya da dik yığın),
// yapının herhangi bir parçasına değen boş bir hücreye eklenir (kullanıcı 2026-11-03: küp yapabilecek sistem).
// Seçim kameraya göre, ana oduna göre bir ızgarada (sütun = sağ / sol, satır = yükseklik, derinlik = kameraya doğru):
//   A / D: bir sütun sola / sağa; o sütundaki odunun kameraya bakan yüzü (odun yoksa ana odunun yanı)
//   W / S: bir kat yukarı / aşağı (yine kameraya bakan yüz)
// Sadece kameradan görünen yer seçilir: hatta kameraya en yakın odunun gerisine geçilmez (kullanıcı: derinlik doluysa
// arkaya geçmesin; Q / E derinlik tuşları kaldırıldı). Önü dolu / uygun yeri olmayan sütun / kat atlanır (o yöndeki ilk
// uygun yere); hiç yoksa seçim yerinde kalır. İlk seçim ana odunun tepesi; birleştirince seçilen yer hatırlanır,
// sonraki odun oradan devam eder; vazgeçince (mouse uzaklaşınca) unutulur. Eklenen parça yapıştığı yüzün yönüne uzanır
// (üstte dik, yanda yatık). Sınır (kullanıcı): ana odundan her yana (sol, sağ, ön, arka) en fazla WoodLayout.MaxBaseLength
// (6) hücre, bir yana uzatmak öbür yanı kısıtlamaz; yükseklik ana odunun boyu kadar (en fazla 6); pivotun altına inmez.
// Nereye eklenir (kullanıcı: her yüze eklemek kaotikti, küp yapabilecek kadar olsun). Üç tür parça var:
//   ana odun: dört yanından, tepesinden ana odunu uzatma
//   kol (ana odunun kameraya göre sağından / solundan düz uzanan): ucundan uzar, önünden ve arkasından derinlik odunu
//   derinlik odunu (koldan çıkan, ya da ana odunun önünden / arkasından): sadece ucundan uzar, ondan dal çıkmaz
// Kol / derinlik ayrımı kameraya göre (kullanıcı: ana odunun sağındaki ve solundaki seviye baz; önündeki dizi derinliktir)
// Hiçbir parçanın üstüne / altına eklenmez: her kat ana odunun o katındaki kollardan yatay bir tabaka (tarak gibi), katlar
// ana odun uzadıkça üst üste.
// Birleştirirken kamera WASD ile kaymaz; Q / E ile dönebilir (eksenler her karede kameradan yeniden alınır).
public class WoodMergeTarget : IWoodSlotView
{
    private readonly GridPlaceable owner;
    private readonly IWoodStack stack;
    private readonly List<Vector3Int[]> candidateCells = new List<Vector3Int[]>(); // sadece seçili yerin hücreleri
    private readonly List<Vector3Int> candidateDirections = new List<Vector3Int>();
    private readonly HashSet<Vector3Int> cluster = new HashSet<Vector3Int>(); // yapının dünya hücreleri
    private readonly HashSet<Vector3Int> baseCells = new HashSet<Vector3Int>(); // ana odunun dünya hücreleri
    private readonly Dictionary<Vector3Int, Vector3Int> axes = new Dictionary<Vector3Int, Vector3Int>(); // parçanın uzandığı yön (dünya)

    // Taşınan odunun şu an yapıştığı hedef; grid göstergesi (HeldGridIndicator) seçili yerin hücrelerini gösterir
    public static WoodMergeTarget Active { get; private set; }

    // Son birleştirmede seçilen yer (sonuç yapının yerel uzayında): sonraki odun oradan devam eder
    private static GridPlaceable rememberedOwner;
    private static Vector3Int rememberedLocal;

    public IReadOnlyList<Vector3Int[]> CandidateCells => candidateCells;
    public IReadOnlyList<Vector3Int> CandidateDirections => candidateDirections;
    public bool IsAlive => owner != null; // sahibi birleştirmede silinmiş olabilir
    public int SelectedIndex => locked ? 0 : -1;

    // Kameranın ortalayacağı nokta: ana odunun ortası
    public Vector3 FocusPoint => (Vector3)owner.OriginWorldPosition + Vector3.up * ((stack.BaseLength - 1) * 0.5f);

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

    // Seçim: (sütun, satır, derinlik) ana odunun pivotuna göre kamera eksenlerinde; normal = eklenen parçanın uzanacağı yön
    private bool locked;
    private int column, row, depth;
    private Vector3Int selectedCell, selectedNormal;
    private Vector3Int right, toward; // kameranın sağı ve kameraya doğru (dünya ızgara eksenleri)
    private Vector3 attachMouse;
    private Vector2 lastInput;

    // Seçili yeri kameradan kapatan parçalar yarı saydam (bkz. DitherFade, FadeBlockers)
    private readonly List<Renderer> faded = new List<Renderer>();
    private readonly List<Renderer> fadeBuffer = new List<Renderer>();
    private readonly HashSet<Vector3Int> fadeCells = new HashSet<Vector3Int>();
    private const float FadeProbe = 0.35f; // köşe ışınlarının hücre ortasından uzaklığı
    private const float FadeStep = 0.1f;   // ışın boyunca adım

    public WoodMergeTarget(GridPlaceable owner)
    {
        this.owner = owner;
        stack = owner as IWoodStack;
        if (stack == null)
            Debug.LogError($"{owner.name} WoodMergeTarget kullanıyor ama IWoodStack değil", owner);
    }

    // Başka bir oduna eklenebilen parça ve boyu: tek 1×1 (Wood) ya da sadece dik sütun olan yığın (MergedWood.IsStack).
    // Yanında odun olan yapı eklenemez.
    public static bool TryGetPiece(IInteractable interacted, out GridPlaceable piece, out int length)
    {
        switch (interacted)
        {
            case Wood wood:
                piece = wood;
                length = wood.Length;
                return true;
            case MergedWood merged when merged.IsStack:
                piece = merged;
                length = merged.BaseLength;
                return true;
            default:
                piece = null;
                length = 0;
                return false;
        }
    }

    public Vector3 GetToolTargetPosition(IInteractable interacted, out bool accept)
    {
        accept = false;
        if (WoodMerger.IsLanding(owner)) return default; // havada uçarken hedef olmaz
        if (!TryGetPiece(interacted, out GridPlaceable carried, out int carriedLength) || ReferenceEquals(carried, owner)) return default;

        CollectCluster();
        CameraAxes();
        if (locked) SetSelection(selectedCell, selectedNormal); // kamera döndüyse sütun / satır yeni eksenlere göre

        if (!locked)
        {
            if (!SelectStart(carriedLength))
            {
                Clear();
                return default;
            }
            locked = true;
            attachMouse = Input.mousePosition;
            lastInput = InputManager.MovementVectorNormalized(); // basılı tuş hemen kaydırmasın
        }
        else
        {
            StepWithKeys(carriedLength);
            // Seçili yer bu arada kapandıysa (kamera döndü, yere bir şey kondu) en yakın uygun yere
            if (!IsValid(selectedCell, selectedNormal, carriedLength) && !SelectNearest(selectedCell, carriedLength))
            {
                Clear();
                return default;
            }
        }

        Active = this;
        UpdateView(carriedLength);
        FadeBlockers();
        accept = true;
        return selectedCell;
    }

    // Taşınan parçanın duruşu: sonuçtaki gibi (yapının dönüşü × yapıştığı yüze doğru yatırılmış)
    public Quaternion GetToolTargetRotation()
    {
        if (!locked) return owner.transform.rotation;
        return owner.GridRotation * Quaternion.FromToRotation(Vector3.up, ToLocal(selectedNormal, false));
    }

    // Yapışkan hedef: mouse yapıştığı andaki yerinden ekranda TargetReleasePixels'ten fazla uzaklaşınca bırakılır (vazgeçti)
    public bool IsStillTargeted(IInteractable interacted, Ray mouseRay)
    {
        if (!TryGetPiece(interacted, out GridPlaceable carried, out _) || ReferenceEquals(carried, owner) || !locked) return false;
        Vector2 moved = Input.mousePosition - attachMouse;
        return moved.magnitude <= WoodMerger.Instance.TargetReleasePixels;
    }

    // Vazgeçildi: hafıza silinir, sonraki seferde ana odundan başlar
    public void OnToolTargetExit()
    {
        if (rememberedOwner == owner) rememberedOwner = null;
        Clear();
    }

    public bool OnToolUsed(IInteractable tool)
    {
        if (!locked || !TryGetPiece(tool, out GridPlaceable carried, out int carriedLength)) return false;
        Vector3Int cell = selectedCell, normal = selectedNormal;
        Clear();

        GridPlaceable result = WoodMerger.Instance.Merge(owner, carried, carriedLength, cell, normal);
        if (result != null)
        {
            rememberedOwner = result;
            rememberedLocal = GridMaskRotator.RotateOffset(cell - result.OriginWorldPosition, Inverse(result.Rotation));
        }
        return true;
    }

    private void Clear()
    {
        if (Active == this) Active = null;
        locked = false;
        candidateCells.Clear();
        candidateDirections.Clear();
        RestoreFaded();
    }

    // --- Izgara ---

    private void CollectCluster()
    {
        cluster.Clear();
        baseCells.Clear();
        axes.Clear();
        if (owner is MergedWood merged)
            foreach (MergedWood.Cell cell in merged.Cells) Add(cell.Position, cell.Axis);
        else
            foreach (Vector3Int local in stack.LocalCells) Add(local, Vector3Int.up);

        void Add(Vector3Int local, Vector3Int axis)
        {
            Vector3Int world = owner.OriginWorldPosition + GridMaskRotator.RotateOffset(local, owner.Rotation);
            cluster.Add(world);
            axes[world] = GridMaskRotator.RotateOffset(axis, owner.Rotation);
            if (local.x == 0 && local.z == 0 && local.y < stack.BaseLength) baseCells.Add(world);
        }
    }

    // Bu parçaya bu yönde (yüzünden dışarı) parça eklenebilir mi: ana oduna yatay dört yandan ve tepeden; kola ucundan
    // (kendi yönünde) ve yatayda kendi yönüne dik iki yandan; derinlik oduna sadece ucundan
    private bool Allows(Vector3Int part, Vector3Int normal)
    {
        if (!cluster.Contains(part)) return false;
        if (baseCells.Contains(part)) return normal.y >= 0;
        if (normal.y != 0 || !axes.TryGetValue(part, out Vector3Int axis) || axis.y != 0) return false;
        if (normal == axis) return true;                         // ucundan uzatma
        return Dot(normal, axis) == 0 && IsArm(part, axis);      // koldan derinlik odunu
    }

    // Kol: kameranın sağ-sol ekseninde yatan ve aynı yönde dizili parçalarla geriye doğru gidince ana oduna varan parça
    private bool IsArm(Vector3Int part, Vector3Int axis)
    {
        if (Dot(axis, right) == 0) return false; // derinlik yönünde (kameraya doğru / ters) yatan: derinlik odunu
        for (Vector3Int q = part - axis; cluster.Contains(q); q -= axis)
        {
            if (baseCells.Contains(q)) return true;
            if (!axes.TryGetValue(q, out Vector3Int other) || other != axis) return false;
        }
        return false;
    }

    // Kameranın sağı ve kameraya doğru yön, en yakın ızgara eksenine oturtulmuş (yatay)
    private void CameraAxes()
    {
        Transform camera = Camera.main.transform;
        right = Snap(camera.right);
        toward = Snap(-camera.forward);
        if (right == toward || right == -toward) toward = new Vector3Int(-right.z, 0, right.x); // dik değilse düzelt
    }

    private static Vector3Int Snap(Vector3 direction)
    {
        direction.y = 0f;
        if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.z)) return new Vector3Int(direction.x >= 0f ? 1 : -1, 0, 0);
        return new Vector3Int(0, 0, direction.z >= 0f ? 1 : -1);
    }

    private Vector3Int CellAt(int c, int r, int d) => owner.OriginWorldPosition + right * c + Vector3Int.up * r + toward * d;

    private void SetSelection(Vector3Int cell, Vector3Int normal)
    {
        selectedCell = cell;
        selectedNormal = normal;
        Vector3Int offset = cell - owner.OriginWorldPosition;
        column = Dot(offset, right);
        row = offset.y;
        depth = Dot(offset, toward);
    }

    private static int Dot(Vector3Int a, Vector3Int b) => a.x * b.x + a.y * b.y + a.z * b.z;

    // Hücreye eklenebilecek parçanın yönü: yapıya değdiği yüzlerden, öncelik kameraya bakan yüz (önündeki boşluk), sonra
    // arka yüz, üst, yanlar, alt. Uygun değilse false.
    private bool TryNormal(Vector3Int cell, int length, out Vector3Int normal)
    {
        normal = default;
        if (cluster.Contains(cell)) return false;
        foreach (Vector3Int candidate in new[] { toward, -toward, Vector3Int.up, right, -right })
        {
            if (!Allows(cell - candidate, candidate)) continue; // bu yönden yapıya değmiyor ya da o yüze eklenmez
            if (!IsValid(cell, candidate, length)) continue;
            normal = candidate;
            return true;
        }
        return false;
    }

    // Parçanın bütün hücreleri boş ve sınırın içinde: ana odundan her yana en fazla 6, yükseklik pivottan en fazla 6 kat
    private bool IsValid(Vector3Int cell, Vector3Int normal, int length)
    {
        if (cluster.Contains(cell) || !Allows(cell - normal, normal)) return false;
        int limit = WoodLayout.MaxBaseLength;
        for (int i = 0; i < length; i++)
        {
            Vector3Int p = cell + normal * i;
            Vector3Int offset = p - owner.OriginWorldPosition;
            if (offset.y < 0 || offset.y >= limit || Mathf.Abs(offset.x) > limit || Mathf.Abs(offset.z) > limit) return false;
            if (cluster.Contains(p) || !GridManager.Instance.IsPlaceableCellFree(p)) return false;
        }
        return true;
    }

    // (sütun, satır) hattında kameradan görünen uygun yer: önden arkaya bakılır, ilk odunun gerisine geçilmez
    private bool Line(int c, int r, int length, out int lineDepth, out Vector3Int normal)
    {
        int reach = WoodLayout.MaxBaseLength + 1;
        for (lineDepth = reach; lineDepth >= -reach; lineDepth--)
        {
            Vector3Int cell = CellAt(c, r, lineDepth);
            if (cluster.Contains(cell)) break; // önü odunla dolu: arkası görünmüyor
            if (TryNormal(cell, length, out normal)) return true;
        }
        normal = default;
        return false;
    }

    // İlk seçim: hatırlanan yer (son birleştirme bu yapıdaysa) ya da ana odunun tepesi
    private bool SelectStart(int length)
    {
        if (rememberedOwner == owner && owner != null)
        {
            Vector3Int remembered = owner.OriginWorldPosition + GridMaskRotator.RotateOffset(rememberedLocal, owner.Rotation);
            if (SelectNearest(remembered, length)) return true;
        }

        Vector3Int top = owner.OriginWorldPosition + Vector3Int.up * stack.BaseLength;
        if (IsValid(top, Vector3Int.up, length))
        {
            SetSelection(top, Vector3Int.up);
            return true;
        }
        return SelectNearest(top, length);
    }

    // Yapıya değen bütün boş hücrelerden target'a en yakını
    private bool SelectNearest(Vector3Int target, int length)
    {
        float best = float.MaxValue;
        Vector3Int bestCell = default, bestNormal = default;
        foreach (Vector3Int part in cluster)
            foreach (Vector3Int direction in new[] { Vector3Int.up, Vector3Int.down, Vector3Int.right, Vector3Int.left,
                                                     new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) })
            {
                Vector3Int cell = part + direction;
                float distance = (cell - target).sqrMagnitude;
                if (distance >= best || !TryNormal(cell, length, out Vector3Int normal)) continue;
                best = distance;
                bestCell = cell;
                bestNormal = normal;
            }
        if (best == float.MaxValue) return false;
        SetSelection(bestCell, bestNormal);
        return true;
    }

    // --- Tuşlar ---

    private void StepWithKeys(int length)
    {
        Vector2 input = InputManager.MovementVectorNormalized();
        bool pressed = input.magnitude > 0.5f && lastInput.magnitude <= 0.5f;
        lastInput = input;
        if (!pressed) return;

        if (Mathf.Abs(input.y) >= Mathf.Abs(input.x)) MoveLine(0, input.y > 0f ? 1 : -1, length);
        else MoveLine(input.x > 0f ? 1 : -1, 0, length); // A sola, D sağa
    }

    // Bir sütun / kat ilerle; o hatta uygun yer yoksa aynı yönde devam (en fazla sınır kadar). Hatta kameraya bakan yüz seçilir.
    // Ana odunun tepesi (uzatma yeri) de bu gezinmeye katılır (kullanıcı: kollar varken tepeye çıkılamıyordu):
    //   A / D ile ana odunun sütununa gelince, en üst kattaysan (kolların hizası) tepe seçilir; tepedeyken A / D bir kat
    //   aşağıdaki (kolların) hattına iner. W ile yukarıda hiçbir yer yoksa tepeye çıkılır.
// W / S: o katta aynı sütunda yer yoksa o katın en yakın sütunundaki yer (kullanıcı: üst katta sadece ana odunun yanı
// uygunken uzaktaki sütundan yukarı çıkılamıyordu).
    private void MoveLine(int dc, int dr, int length)
    {
        int top = stack.BaseLength; // tepenin satırı (ana odunun üstündeki hücre)
        bool topValid = IsValid(CellAt(0, top, 0), Vector3Int.up, length);

        for (int step = 1; step <= WoodLayout.MaxBaseLength * 2 + 1; step++) // bir uçtan öbür uca atlayabilsin
        {
            int c = column + dc * step, r = row + dr * step;
            if (dc != 0 && c == 0 && topValid && row >= top - 1)
            {
                SetSelection(CellAt(0, top, 0), Vector3Int.up);
                return;
            }
            if (dr != 0)
            {
                if (r < 0 || r > top) break;
                if (TrySelectRow(r, length)) return;
                continue;
            }
            if (TrySelectLine(c, r, length)) return;
            if (dc != 0 && row == top && TrySelectLine(c, top - 1, length)) return; // tepeden yanlara: kolların hattı
        }

        if (dr > 0 && topValid && row < top) SetSelection(CellAt(0, top, 0), Vector3Int.up);
    }

    // r katında bulunulan sütuna en yakın sütundaki görünen yer (eşitse önce sağ)
    private bool TrySelectRow(int r, int length)
    {
        int reach = WoodLayout.MaxBaseLength * 2;
        for (int offset = 0; offset <= reach; offset++)
        {
            if (TrySelectLine(column + offset, r, length)) return true;
            if (offset > 0 && TrySelectLine(column - offset, r, length)) return true;
        }
        return false;
    }

    private bool TrySelectLine(int c, int r, int length)
    {
        if (!Line(c, r, length, out int lineDepth, out Vector3Int normal)) return false;
        SetSelection(CellAt(c, r, lineDepth), normal);
        return true;
    }

    // --- Gösterge ve saydamlık ---

    private void UpdateView(int length)
    {
        candidateCells.Clear();
        candidateDirections.Clear();
        var cells = new Vector3Int[length];
        for (int i = 0; i < length; i++) cells[i] = selectedCell + selectedNormal * i;
        candidateCells.Add(cells);
        candidateDirections.Add(selectedNormal);
    }

    // Seçili yeri kapatan parçalar yarı saydam (kullanıcı: önceki "önündeki ve çevresindeki" kuralı tutarsızdı): kameradan
    // seçili yerin hücrelerine (ortası + köşelere yakın dört nokta) ışınlar, yol üstünde çarptığı yapı hücreleri. Hücreler
    // grid'de adım adım yürünerek bulunur (collider'lar tek objede, hangi 1×1 olduğu ayırt edilemiyor).
    private void FadeBlockers()
    {
        if (!(owner is MergedWood merged)) return;
        fadeBuffer.Clear();
        fadeCells.Clear();
        Transform camera = Camera.main.transform;
        Vector3 eye = camera.position;
        foreach (Vector3Int[] cells in candidateCells)
            foreach (Vector3Int cell in cells)
                for (int k = 0; k < 5; k++)
                {
                    Vector3 target = cell;
                    if (k > 0) target += camera.right * ((k & 1) == 0 ? FadeProbe : -FadeProbe)
                                       + camera.up * (k <= 2 ? FadeProbe : -FadeProbe);
                    MarchToCamera(target, eye);
                }
        foreach (Vector3Int part in fadeCells)
            foreach (Renderer renderer in merged.RenderersAt(ToLocal(part, true)))
                if (!fadeBuffer.Contains(renderer)) fadeBuffer.Add(renderer); // ip birden çok hücrede

        bool same = fadeBuffer.Count == faded.Count;
        for (int i = 0; same && i < faded.Count; i++) same = faded[i] == fadeBuffer[i];
        if (same) return;

        RestoreFaded();
        faded.AddRange(fadeBuffer);
        if (faded.Count > 0) WoodMerger.Instance.FrontFade.FadeOut(faded.ToArray());
    }

    // target'tan kameraya doğru adım adım: geçtiği yapı hücreleri fadeCells'e. Yapının sınırını geçince durur.
    private void MarchToCamera(Vector3 target, Vector3 eye)
    {
        Vector3 toEye = eye - target;
        float distance = Mathf.Min(toEye.magnitude, WoodLayout.MaxBaseLength * 3f);
        Vector3 direction = toEye.normalized;
        Vector3Int start = Vector3Int.RoundToInt(target);
        for (float s = FadeStep; s < distance; s += FadeStep)
        {
            Vector3Int cell = Vector3Int.RoundToInt(target + direction * s);
            if (cell != start && cluster.Contains(cell)) fadeCells.Add(cell);
        }
    }

    private void RestoreFaded()
    {
        if (faded.Count == 0) return;
        DitherFade.Clear(faded.ToArray());
        faded.Clear();
    }

    // Dünyadaki hücre (point) ya da yön yapının yerel uzayına
    private Vector3Int ToLocal(Vector3Int world, bool point)
        => GridMaskRotator.RotateOffset(point ? world - owner.OriginWorldPosition : world, Inverse(owner.Rotation));

    private static GridMaskRotator.Rotation Inverse(GridMaskRotator.Rotation rotation)
        => (GridMaskRotator.Rotation)((4 - (int)rotation) % 4);

    // Kameraya en çok bakan yan "ön" sayılır; odunun kendi rotasyonu da hesaba katılır (sökme kullanıyor)
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
}
