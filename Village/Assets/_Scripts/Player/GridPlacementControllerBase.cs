using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

// Build mode'da bir menünün (base'ler, placeable'lar) yerleştirmesi. Akış: sayı tuşuna bir kez basınca o kutudaki obje
// seçilir (aynı tuşa tekrar basınca bırakılır), yarı saydam önizlemesi (PlacementGhost) mouse'un hücresini takip eder:
// konabilecek yerde yeşilimsi, konamayacak yerde kırmızımsı. R seçili objeyi 90° döndürür (blueprint'ler için önemli).
// Sol tık koyar; sol tık basılı sürükleyince baskın eksende (X ya da Z) hat boyunca dizer, önizleme hattın tamamını
// gösterir. Sağ tık basılı tutulurken mouse'un altındakiler silinir.
public abstract class GridPlacementControllerBase<TEntity> : GridPlacementControllerBase where TEntity : GridEntity
{
    private Plane plane;
    [SerializeField] private string menuName;

    [Header("Önizleme")]
    [Tooltip("Village/Placement Ghost. Referansla tutulur ki build'e girsin.")]
    [SerializeField] private Shader ghostShader;
    [Tooltip("Konabilecek yerde önizlemenin rengi (A: saydamlık)")]
    [SerializeField] private Color validColor = new Color(0.45f, 1f, 0.55f, 0.45f);
    [Tooltip("Konamayacak yerde önizlemenin rengi (A: saydamlık)")]
    [SerializeField] private Color invalidColor = new Color(1f, 0.35f, 0.3f, 0.45f);

    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private int selectedNumber = -1;               // seçili kutunun sayı tuşu (1..), yoksa -1
    private GridMaskRotator.Rotation rotation;     // seçimler arasında korunur
    private TEntity probe;                         // seçili objenin kapalı kopyası: şekli / konabilirliği bununla
    private PlacementGhost ghost;
    private Material validMaterial, invalidMaterial;
    private bool isDragging;
    private Vector3 dragStartPos;
    // Mouse UI'nin üstünde mi: Update'te okunur. Input olaylarının içinden EventSystem'e sorulamaz (Unity uyarır, bir
    // önceki karenin cevabını verir); tıklama gelince bu saklanan değer kullanılır.
    private bool pointerOverUi;
    private readonly List<Vector3> linePositions = new List<Vector3>();

    public override string MenuName
    {
        get => menuName;
        set => menuName = value;
    }

    protected virtual void Awake()
    {
        plane = new Plane(Vector3.up, transform.position);
    }

    protected abstract TEntity GetPrefab(int index);
    // Sayı tuşu (1..) → menüdeki sıra: o anki sayfanın başından (PlacementController sayfa değiştirince PageOffset'i yazar)
    private int EntryIndex(int number) => PageOffset + number - 1;
    // Menüdeki SO (yaratılan kopyaya verisi atanır)
    protected abstract GridEntitySOBase GetEntityData(int index);

    private TEntity CreateInstance(int index)
    {
        TEntity instance = Instantiate(GetPrefab(index));
        instance.AssignData(GetEntityData(index));
        instance.Rotation = rotation;
        return instance;
    }
    protected abstract bool CanPlace(TEntity entity, Vector3 position);
    protected abstract bool PlaceEntity(TEntity entity, Vector3 position);
    protected abstract bool RemoveEntity(TEntity entity);

    // --- Seçim ---

    private void OnNumber(int number, bool isPressed)
    {
        if (!isPressed) return;
        if (number == selectedNumber) ClearSelection();
        else Select(number);
    }

    private void Select(int number)
    {
        ClearSelection();
        if (GetPrefab(EntryIndex(number)) == null) return;

        selectedNumber = number;
        probe = CreateInstance(EntryIndex(number));
        probe.gameObject.SetActive(false); // sadece şekli ve konabilirliği için; oyunda yok
        EnsureMaterials();
        // Hayalet dönüşsüz kurulur (dönüş gösterirken verilir); görünüşü yoksa dönüşsüz hücreleri küp olur
        probe.Rotation = GridMaskRotator.Rotation.Deg0;
        IReadOnlyList<Vector3Int> cells = probe.GetFootprint().FilledCells();
        probe.Rotation = rotation;
        ghost = new PlacementGhost(probe.gameObject, cells, validMaterial, invalidMaterial);
        RaiseSelectionChanged(number - 1);
    }

    public override void ClearSelection()
    {
        isDragging = false;
        if (selectedNumber < 0) return;
        selectedNumber = -1;
        ghost?.Destroy();
        ghost = null;
        if (probe != null) Destroy(probe.gameObject);
        probe = null;
        RaiseSelectionChanged(-1);
    }

    private void EnsureMaterials()
    {
        if (validMaterial != null) return;
        Shader shader = ghostShader != null ? ghostShader : Shader.Find("Village/Placement Ghost");
        validMaterial = new Material(shader) { name = "Placement Ghost Valid (generated)" };
        invalidMaterial = new Material(shader) { name = "Placement Ghost Invalid (generated)" };
    }

    private void Rotate()
    {
        if (selectedNumber < 0) return;
        rotation = GridMaskRotator.Next(rotation);
        probe.Rotation = rotation;
    }

    // --- Koyma ---

    private void OnMouseLeft(bool isPressed)
    {
        if (selectedNumber < 0) return;
        if (isPressed)
        {
            // Menü butonlarına tıklamak bir şey koymasın
            if (pointerOverUi) return;
            if (!TryGetTargetPosition(out dragStartPos)) return;
            isDragging = true;
            return;
        }

        if (!isDragging) return;
        isDragging = false;
        if (!TryGetTargetPosition(out Vector3 endPos)) return;
        SpawnLine(dragStartPos, endPos);
    }

    // Varsayılan: baktığı objenin üstü, yoksa bu objenin yüksekliğindeki düzlem. Base yerleştirme kendi kuralını kullanır.
    protected virtual bool TryGetTargetPosition(out Vector3 position)
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            position = hit.point + Vector3.up / 2;
            return true;
        }
        else if (plane.Raycast(ray, out float enter))
        {
            position = ray.GetPoint(enter);
            return true;
        }

        position = Vector3.zero;
        return false;
    }

    // start → end hattı baskın eksende (X ya da Z; çapraz değil); objeler arası adım, footprint'in o eksendeki boyu
    private void LinePositions(Vector3 start, Vector3 end, List<Vector3> result)
    {
        result.Clear();
        Vector3 delta = end - start;
        bool alongX = Mathf.Abs(delta.x) >= Mathf.Abs(delta.z);
        if (alongX) delta.z = 0f;
        else delta.x = 0f;

        Vector3Int size = probe.GetFootprint().Size;
        float step = Mathf.Max(1, alongX ? size.x : size.z);
        float distance = delta.magnitude;
        Vector3 direction = distance > 0.001f ? delta.normalized : Vector3.zero;
        // Yarım adımdan az kayma tek obje (eskiden en az 2 sayıyordu: tıklarken fare biraz kayınca iki obje konuyordu)
        int count = Mathf.RoundToInt(distance / step);
        for (int i = 0; i <= count; i++)
            result.Add(start + direction * (step * i));
    }

    private void SpawnLine(Vector3 start, Vector3 end)
    {
        LinePositions(start, end, linePositions);
        TEntity instance = null;
        foreach (Vector3 pos in linePositions)
        {
            if (instance == null) instance = CreateInstance(EntryIndex(selectedNumber));
            if (!CanPlace(instance, pos)) continue;
            if (PlaceEntity(instance, pos)) instance = null;
        }
        if (instance != null) Destroy(instance.gameObject); // hiçbir yere konmayan son kopya
    }

    // --- Önizleme ---

    private void UpdatePreview()
    {
        if (ghost == null) return;
        if (pointerOverUi && !isDragging || !TryGetTargetPosition(out Vector3 target))
        {
            ghost.HideFrom(0);
            return;
        }

        validMaterial.SetColor(ColorId, validColor);
        invalidMaterial.SetColor(ColorId, invalidColor);
        LinePositions(isDragging ? dragStartPos : target, target, linePositions);
        Quaternion gridRotation = probe.GridRotation;
        for (int i = 0; i < linePositions.Count; i++)
        {
            Vector3 cell = GridManager.Instance.GetGridWorldPosition(linePositions[i], out bool onGrid);
            if (!onGrid) cell = Vector3Int.RoundToInt(linePositions[i]);
            ghost.Show(i, cell, gridRotation, CanPlace(probe, linePositions[i]));
        }
        ghost.HideFrom(linePositions.Count);
    }

    // --- Silme ---

    // Build mode'da sağ tık basılı tutulurken mouse'un altından geçen entity'ler silinir (tutma build mode'da kapalı).
    // Bir basışta her sütundan (x, z) sadece bir entity: mouse aynı yerde dururken üst üste katların hepsi gitmesin.
    private bool deleting;
    private readonly HashSet<Vector2Int> deletedColumns = new HashSet<Vector2Int>();

    private void DeleteOnRightClick(bool pressed)
    {
        deleting = pressed;
        deletedColumns.Clear();
        if (pressed) DeletePlaced();
    }

    private void Update()
    {
        pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (deleting) DeletePlaced();
        UpdatePreview();
    }

    // Collider child'da olabilir (örn. blueprint'in collider'ları slot objelerinde): entity collider'dan yukarı doğru aranır
    private void DeletePlaced()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        TEntity entity = hit.collider.GetComponentInParent<TEntity>();
        if (entity == null) return;

        var column = new Vector2Int(entity.OriginWorldPosition.x, entity.OriginWorldPosition.z);
        if (deletedColumns.Contains(column)) return;
        if (RemoveEntity(entity)) deletedColumns.Add(column);
    }

    protected virtual void OnEnable()
    {
        InputManager.OnNumbers += OnNumber;
        InputManager.OnMouseLeft += OnMouseLeft;
        InputManager.OnR += Rotate;
        InputManager.OnMouseRight += DeleteOnRightClick;
    }

    protected virtual void OnDisable()
    {
        InputManager.OnNumbers -= OnNumber;
        InputManager.OnMouseLeft -= OnMouseLeft;
        InputManager.OnR -= Rotate;
        InputManager.OnMouseRight -= DeleteOnRightClick;
        deleting = false; // kapalıyken bırakma olayını duyamayız, silme takılı kalmasın
        ClearSelection(); // menü değişti / build mode kapandı
    }

    protected virtual void OnDestroy()
    {
        if (validMaterial != null) Destroy(validMaterial);
        if (invalidMaterial != null) Destroy(invalidMaterial);
    }
}

public abstract class GridPlacementControllerBase : MonoBehaviour
{
    public abstract string MenuName { get; set; }
    public GridMenuBaseSO MenusSO;
    // Gösterilen sayfanın ilk elemanının menüdeki sırası (sayı tuşları buradan sayar)
    public int PageOffset { get; set; }

    // Seçili kutu değişince (kutunun sırası 0..; seçim yoksa -1): PlacementController kutuyu boyar
    public event Action<int> SelectionChanged;
    protected void RaiseSelectionChanged(int slot) => SelectionChanged?.Invoke(slot);

    // Seçimi bırakır (önizleme kalkar), örn. sayfa değişince
    public abstract void ClearSelection();
}
