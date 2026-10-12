using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Toprak çalışma masası: topraktan tarla parçası yapılan yer (DESIGN.md "Taslak: toprak ve tarla parçası üretimi").
// Dünyada ayrı bir placeable (adada durur, sağ tık basılı taşınır, R ile döner). Üstünde 3×3 tepsi var.
// - Blueprint = tarla parçasının kendi SO'su (şekli SO'daki hücre işaretlerinden). Seçili blueprint'in hücreleri tepsinin
//   ortasına yerleşir, küçük ve nabız atan toprak blokları olarak görünür (doldurulacak yerler).
// - Hücreye sol tık: gemideki depodan soilPerCell toprak düşer, blok küçükten tam boyuna büyür.
// - Hepsi dolunca bloklar tek parçaya dönüşür: tarla parçası masanın üstünde durur, oyuncu sağ tıkla alana kadar orada
//   kalır (o sırada yenisi yapılmaz). Alınan parça bir yere konamazsa masaya geri döner.
// - Masanın yanına gelince ekranın altında basit UI (CraftingTableUI): blueprint seçimi, maliyet, toprak miktarı.
// Toprak şimdilik sahnedeki gemiden (Boat.Soil) otomatik çekilir.
public class SoilCraftingTable : GridPlaceable, IInteractable, IClickable
{
    public const int TraySize = 3;

    [Header("Blueprint'ler")]
    [Tooltip("Yapılabilecek tarla parçaları: tarla parçalarının SO'ları (Prefab'ı FarmPiece olan). Şekil SO'dan okunur; 3×3'e sığmalı.")]
    [SerializeField] private List<GridPlaceableSO> blueprints = new List<GridPlaceableSO>();
    [Tooltip("Bir hücrenin toprak maliyeti (parçanın maliyeti = hücre sayısı × bu)")]
    [SerializeField, Min(0)] private int soilPerCell = 1;

    [Header("Tepsi")]
    [Tooltip("Tepsinin ortası, masanın uzayında (orta slotun üst yüzü). Pivot 3×3 alanın köşedeki ilk hücresinde, alan +X ve +Z " +
             "yönünde: ortası (1, y, 1). Scene'de kahverengi kareler.")]
    [SerializeField] private Vector3 trayCenter = new Vector3(1f, 1.2f, 1f);
    [Tooltip("İki slotun ortası arası mesafe (birim)")]
    [SerializeField] private float slotSpacing = 1f;
    [Tooltip("Tepsideki blokların boyu (1 = tam hücre). Slotlar hücreden küçükse küçült.")]
    [SerializeField] private float tileScale = 0.8f;
    [Tooltip("Doldurulmamış hücrelerdeki bloğun boyu (tileScale'e göre oran)")]
    [SerializeField] private float ghostScale = 0.35f;
    [Tooltip("Tepsideki blokların modeli: 1×1 toprak bloğu (pivot hücrenin ortasında, dibi -0.5'te; tarla parçasıyla aynı)")]
    [SerializeField] private GameObject tileModel;
    [Tooltip("Doldurulmamış hücrelerdeki bloğun malzemesi (saydam). Boşsa bloğun kendi malzemesi kullanılır.")]
    [SerializeField] private Material ghostMaterial;

    [Header("Taşıma")]
    [SerializeField] private float followSpeed = 15f;

    private GridDragMotor drag;
    private int selected;
    private readonly List<Vector2Int> blueprintCells = new List<Vector2Int>(); // tepsi indeksleri (0..2, 0..2)
    private Vector2Int pivotSlot;                                               // parçanın pivot hücresinin tepsi indeksi
    private readonly Dictionary<Vector2Int, Transform> tiles = new Dictionary<Vector2Int, Transform>();
    private readonly HashSet<Vector2Int> filled = new HashSet<Vector2Int>();
    private FarmPiece finished;
    private Boat ship;
    private float messageUntil;
    private string message;

    public static SoilCraftingTable Active { get; private set; }

    public int SoilPerCell => soilPerCell;
    public bool HasFinishedPiece => finished != null;
    public GridPlaceableSO SelectedBlueprint => blueprints.Count > 0 ? blueprints[Mathf.Clamp(selected, 0, blueprints.Count - 1)] : null;
    public int SelectedCost => blueprintCells.Count * soilPerCell;
    public string Message => Time.time < messageUntil ? message : null;

    public Boat Ship
    {
        get
        {
            if (ship == null) ship = FindFirstObjectByType<Boat>();
            return ship;
        }
    }

    private void Awake()
    {
        drag = new GridDragMotor(this, followSpeed);
        EnsureCollider();
        BuildSlots();
        Select(0);
    }

    private void OnDisable()
    {
        if (Active == this)
        {
            Active = null;
            CraftingTableUI.Hide();
        }
    }

    // --- Tepsi ---

    private Vector3 SlotLocal(Vector2Int slot) =>
        trayCenter + new Vector3((slot.x - 1) * slotSpacing, 0f, (slot.y - 1) * slotSpacing);

    // Tıklanabilir slotlar: tepsinin üstünde ince kutular (masanın collider'ından önce çarpılsın)
    private void BuildSlots()
    {
        for (int z = 0; z < TraySize; z++)
            for (int x = 0; x < TraySize; x++)
            {
                var slot = new GameObject($"Slot {x},{z}");
                slot.transform.SetParent(transform, false);
                slot.transform.localPosition = SlotLocal(new Vector2Int(x, z));
                var box = slot.AddComponent<BoxCollider>();
                box.size = new Vector3(slotSpacing * 0.95f, 0.12f, slotSpacing * 0.95f);
                box.center = new Vector3(0f, 0.06f, 0f);
                slot.AddComponent<CraftingTableSlot>().Setup(this, new Vector2Int(x, z));
            }
    }

    // Tutmak ve eldivenin üstünde yürümesi için masanın gövdesi: modelin mesh'lerini saran kutu (slotlar hariç)
    private void EnsureCollider()
    {
        if (GetComponent<Collider>() != null) return;
        Bounds bounds = default;
        bool found = false;
        Matrix4x4 toRoot = transform.worldToLocalMatrix;
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            Matrix4x4 matrix = toRoot * filter.transform.localToWorldMatrix;
            Bounds mesh = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mesh.center + Vector3.Scale(mesh.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 point = matrix.MultiplyPoint3x4(corner);
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                else bounds.Encapsulate(point);
            }
        }
        var box = gameObject.AddComponent<BoxCollider>();
        if (found)
        {
            // Tepesi tepsinin yüzeyinde kesilir: slotlar (tıklanan ince kutular) gövdenin içinde kalmasın
            float top = Mathf.Min(bounds.max.y, trayCenter.y - 0.01f);
            if (top > bounds.min.y + 0.05f) bounds.SetMinMax(bounds.min, new Vector3(bounds.max.x, top, bounds.max.z));
            box.center = bounds.center;
            box.size = bounds.size;
        }
        else
        {
            box.center = new Vector3(trayCenter.x, trayCenter.y * 0.5f - 0.25f, trayCenter.z);
            box.size = new Vector3(TraySize * slotSpacing, trayCenter.y + 0.5f, TraySize * slotSpacing);
        }
    }

    // --- Blueprint seçimi ---

    public void Cycle(int step)
    {
        if (blueprints.Count == 0 || finished != null) return;
        Select((selected + step + blueprints.Count) % blueprints.Count);
    }

    // Yeni blueprint: doldurulmuş hücrelerin toprağı geri verilir, tepsi yeni şekle göre kurulur
    private void Select(int index)
    {
        if (filled.Count > 0) Ship?.AddSoil(filled.Count * soilPerCell);
        filled.Clear();
        ClearTiles();
        blueprintCells.Clear();
        selected = index;

        GridPlaceableSO data = SelectedBlueprint;
        if (data == null) return;
        if (!(data.Prefab is FarmPiece))
        {
            Debug.LogWarning($"SoilCraftingTable: '{data.name}' bir tarla parçası değil (Prefab'ında FarmPiece yok).", this);
            return;
        }

        // Şeklin hücreleri (döndürmesiz, tek kat), tepsinin ortasına
        var offsets = new List<Vector3Int>();
        foreach (Vector3Int offset in data.GetFootprint().FilledCells())
            if (offset.y == 0) offsets.Add(offset);
        if (offsets.Count == 0) return;
        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        foreach (Vector3Int o in offsets)
        {
            minX = Mathf.Min(minX, o.x); maxX = Mathf.Max(maxX, o.x);
            minZ = Mathf.Min(minZ, o.z); maxZ = Mathf.Max(maxZ, o.z);
        }
        int width = maxX - minX + 1, depth = maxZ - minZ + 1;
        if (width > TraySize || depth > TraySize)
        {
            Debug.LogWarning($"SoilCraftingTable: '{data.name}' 3×3 tepsiye sığmıyor ({width}×{depth}).", this);
            return;
        }
        int shiftX = (TraySize - width) / 2 - minX, shiftZ = (TraySize - depth) / 2 - minZ;
        foreach (Vector3Int o in offsets) blueprintCells.Add(new Vector2Int(o.x + shiftX, o.z + shiftZ));
        pivotSlot = new Vector2Int(shiftX, shiftZ);
        BuildGhosts();
    }

    // --- Doldurma ---

    private void BuildGhosts()
    {
        if (tileModel == null)
        {
            Debug.LogWarning("SoilCraftingTable: Tile Model atanmamış; doldurulacak hücreler görünmez (1×1 toprak bloğunu ata).", this);
            return;
        }
        foreach (Vector2Int cell in blueprintCells)
        {
            Transform tile = CreateTile(cell, ghostScale);
            if (ghostMaterial != null)
                foreach (Renderer renderer in tile.GetComponentsInChildren<Renderer>())
                {
                    var materials = new Material[renderer.sharedMaterials.Length];
                    for (int i = 0; i < materials.Length; i++) materials[i] = ghostMaterial;
                    renderer.sharedMaterials = materials;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            tiles[cell] = tile;
        }
    }

    // Tepsiye bir blok (collider'sız)
    private Transform CreateTile(Vector2Int cell, float scale)
    {
        Transform tile = Instantiate(tileModel, transform).transform;
        foreach (Collider collider in tile.GetComponentsInChildren<Collider>()) Destroy(collider);
        tile.localRotation = Quaternion.identity;
        tile.localPosition = TilePosition(cell, scale);
        tile.localScale = Vector3.one * (tileScale * scale);
        return tile;
    }

    // Bloğun yeri: slotun üst yüzü + bloğun yarı boyu (pivot ortada, dibi -0.5'te)
    private Vector3 TilePosition(Vector2Int cell, float scale) => SlotLocal(cell) + Vector3.up * (0.5f * tileScale * scale);

    private void ClearTiles()
    {
        foreach (Transform tile in tiles.Values)
            if (tile != null) Destroy(tile.gameObject);
        tiles.Clear();
    }

    // Masanın gövdesine sol tık (eldiven slotun değil gövdenin üstündeyse): tıklanan noktanın altındaki slot
    public void OnClicked(RaycastHit hit)
    {
        Vector3 local = transform.InverseTransformPoint(hit.point) - trayCenter;
        var cell = new Vector2Int(Mathf.RoundToInt(local.x / slotSpacing) + 1, Mathf.RoundToInt(local.z / slotSpacing) + 1);
        if (cell.x < 0 || cell.y < 0 || cell.x >= TraySize || cell.y >= TraySize) return;
        OnSlotClicked(cell);
    }

    // Slota sol tık
    public void OnSlotClicked(Vector2Int cell)
    {
        if (finished != null)
        {
            Say("Take the finished piece first");
            return;
        }
        if (!blueprintCells.Contains(cell) || filled.Contains(cell)) return;
        if (Ship == null)
        {
            Say("No ship to take soil from");
            return;
        }
        if (!Ship.TrySpendSoil(soilPerCell))
        {
            Say("Not enough soil");
            if (tiles.TryGetValue(cell, out Transform empty)) StartCoroutine(Shake(empty));
            return;
        }

        filled.Add(cell);
        if (tiles.TryGetValue(cell, out Transform ghost))
        {
            // Saydam blok gider, yerine bloğun kendi malzemesiyle bir blok büyür
            if (ghost != null) Destroy(ghost.gameObject);
            Transform tile = CreateTile(cell, ghostScale);
            tiles[cell] = tile;
            StartCoroutine(GrowTile(tile, cell));
        }
        if (filled.Count == blueprintCells.Count) StartCoroutine(CompleteAfter(0.25f));
    }

    private IEnumerator GrowTile(Transform tile, Vector2Int cell)
    {
        const float duration = 0.22f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            if (tile == null) yield break;
            float k = Mathf.SmoothStep(0f, 1f, t);
            float scale = Mathf.Lerp(ghostScale, 1f, k) * (1f + Mathf.Sin(t * Mathf.PI) * 0.18f);
            tile.localScale = Vector3.one * (tileScale * scale);
            tile.localPosition = TilePosition(cell, scale);
            yield return null;
        }
        if (tile == null) yield break;
        tile.localScale = Vector3.one * tileScale;
        tile.localPosition = TilePosition(cell, 1f);
    }

    private static IEnumerator Shake(Transform tile)
    {
        Vector3 rest = tile.localPosition;
        for (float t = 0f; t < 0.25f; t += Time.deltaTime)
        {
            if (tile == null) yield break;
            tile.localPosition = rest + Vector3.right * (Mathf.Sin(t * 60f) * 0.04f * (1f - t / 0.25f));
            yield return null;
        }
        if (tile != null) tile.localPosition = rest;
    }

    // Bütün hücreler doldu: bloklar tek tarla parçasına dönüşür, masanın üstünde bekler
    private IEnumerator CompleteAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        GridPlaceableSO data = SelectedBlueprint;
        if (data == null || !(data.Prefab is FarmPiece prefab)) yield break;
        ClearTiles();
        filled.Clear();
        FarmPiece piece = Instantiate(prefab);
        piece.AssignData(data); // bütün şekiller tek prefab'ı paylaşabilir: şekil blueprint'ten
        PutOnTable(piece);
        StartCoroutine(Pop(piece.transform));
    }

    private void PutOnTable(FarmPiece piece)
    {
        finished = piece;
        piece.Rotation = GridMaskRotator.Rotation.Deg0;
        piece.transform.SetParent(transform, false);
        piece.transform.localRotation = Quaternion.identity;
        piece.transform.localScale = Vector3.one * tileScale;
        piece.transform.localPosition = TilePosition(pivotSlot, 1f);
        piece.OnPlacedOnTable(this);
    }

    private IEnumerator Pop(Transform target)
    {
        Vector3 rest = target.localScale;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
        {
            if (target == null || target.parent != transform) yield break;
            target.localScale = rest * (1f + Mathf.Sin(t * Mathf.PI) * 0.15f);
            yield return null;
        }
        if (target != null && target.parent == transform) target.localScale = rest;
    }

    // Parça masadan alındı: tepsi yeni parça için hazır (aynı blueprint)
    public void OnPieceTaken(FarmPiece piece)
    {
        if (piece != finished) return;
        finished = null;
        piece.transform.SetParent(null, true);
        piece.transform.localScale = Vector3.one;
        BuildGhosts();
    }

    // Alınan parça bir yere konamadı: masaya geri
    public void ReturnPiece(FarmPiece piece)
    {
        if (finished != null) return;
        ClearTiles();
        PutOnTable(piece);
    }

    private void Say(string text)
    {
        message = text;
        messageUntil = Time.time + 1.6f;
    }

    // --- UI: eldiven masanın yanındayken ---

    private void Update()
    {
        GloveCursor glove = GloveCursor.Instance;
        bool near = false;
        if (glove != null && glove.HasSurface && !glove.IsRiding)
        {
            Vector3 local = transform.InverseTransformPoint(glove.SurfacePoint);
            float reach = TraySize * slotSpacing * 0.5f + 1.5f;
            near = Mathf.Abs(local.x) < reach && Mathf.Abs(local.z) < reach;
        }
        if (Active == this && CraftingTableUI.IsPointerOver) near = true; // mouse UI'dayken eldiven gizli, panel kalsın

        if (near && (Active == null || Active == this))
        {
            Active = this;
            CraftingTableUI.Show(this);
        }
        else if (!near && Active == this)
        {
            Active = null;
            CraftingTableUI.Hide();
        }

        // Doldurulacak hücreler hafifçe nabız atar
        float pulse = 1f + Mathf.Sin(Time.time * 4f) * 0.12f;
        foreach (KeyValuePair<Vector2Int, Transform> pair in tiles)
        {
            if (filled.Contains(pair.Key) || pair.Value == null) continue;
            pair.Value.localScale = Vector3.one * (tileScale * ghostScale * pulse);
        }
    }

    // --- IInteractable: masa taşınırken (slotlar da buraya yönlendirir) ---

    public void InteractContractBeginnig() => drag.Begin();
    public void InteractContract(out bool success)
    {
        success = true;
        drag.Tick();
    }
    public void Interact(out bool finished)
    {
        if (!drag.TryUseOnTarget(out finished)) finished = true;
    }
    public void ContractCancel() => drag.Cancel();

#if UNITY_EDITOR
    // Slotların yeri (kahverengi kareler): tepsinin çukurlarına oturt
    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.6f, 0.38f, 0.2f, 0.9f);
        for (int z = 0; z < TraySize; z++)
            for (int x = 0; x < TraySize; x++)
            {
                Vector3 center = SlotLocal(new Vector2Int(x, z));
                Gizmos.DrawWireCube(center + Vector3.up * 0.5f * tileScale, Vector3.one * tileScale);
                Gizmos.DrawWireCube(center, new Vector3(slotSpacing * 0.95f, 0.01f, slotSpacing * 0.95f));
            }
        Gizmos.matrix = Matrix4x4.identity;
    }
#endif
}
