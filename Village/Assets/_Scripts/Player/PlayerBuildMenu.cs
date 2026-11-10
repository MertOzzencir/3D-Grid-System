using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Oyuncunun oyun içi inşa menüsü: ekranın altında hep açık duran bir bar. Üstünde kategori sekmeleri (BuildCatalogSO'nun
// kategorileri), içinde o sekmenin yapıları kart olarak (ikon + ad). Geliştirici build mode'undan (PlacementController,
// Space) ayrı; o açıkken bu bar gizlenir.
// Karta tıklayınca yapı seçilir: yarı saydam önizlemesi (PlacementGhost) mouse'un hücresini takip eder, konabilecek yerde
// yeşilimsi, konamayacak yerde kırmızımsı. R kendi ortası etrafında döndürür, sol tık koyar (seçim kalır, art arda konabilir), kısa sağ tık / Esc ya da
// aynı karta tekrar tıklamak bırakır. Seçiliyken obje tutma ve aletler kapalıdır, eldiven gizlenir.
// Arayüz kodla kurulur: sahnede bu component'in olması ve kataloğun verilmesi yeter (EventSystem sahnede zaten var).
public class PlayerBuildMenu : MonoBehaviour
{
    [SerializeField] private BuildCatalogSO catalog;

    [Header("Seçiliyken kapananlar")]
    [SerializeField] private InteractableController interactableController;
    [SerializeField] private ToolController toolController;

    [Header("Önizleme")]
    [Tooltip("Village/Placement Ghost. Referansla tutulur ki build'e girsin.")]
    [SerializeField] private Shader ghostShader;
    [Tooltip("Konabilecek yerde önizlemenin rengi (A: saydamlık)")]
    [SerializeField] private Color validColor = new Color(0.45f, 1f, 0.55f, 0.45f);
    [Tooltip("Konamayacak yerde önizlemenin rengi (A: saydamlık)")]
    [SerializeField] private Color invalidColor = new Color(1f, 0.35f, 0.3f, 0.45f);

    [Header("Görünüm")]
    [Tooltip("Bar, sekme ve kartların arka planı (isteğe bağlı, 9-slice'lı bir sprite olursa köşeler korunur). Boşsa düz kutu.")]
    [SerializeField] private Sprite panelSprite;
    [SerializeField] private Color barColor = new Color(0.12f, 0.1f, 0.09f, 0.85f);
    [SerializeField] private Color tabColor = new Color(0.25f, 0.21f, 0.18f, 0.95f);
    [SerializeField] private Color tabSelectedColor = new Color(0.95f, 0.75f, 0.35f, 1f);
    [SerializeField] private Color cardColor = new Color(0.32f, 0.27f, 0.23f, 1f);
    [SerializeField] private Color cardSelectedColor = new Color(0.55f, 0.85f, 0.45f, 1f);
    [SerializeField] private Color textColor = Color.white;
    [Tooltip("Referans çözünürlük 1920x1080'e göre piksel")]
    [SerializeField] private float barHeight = 150f;
    [SerializeField] private float tabHeight = 44f;
    [SerializeField] private float tabWidth = 150f;
    [SerializeField] private Vector2 cardSize = new Vector2(120f, 120f);
    [SerializeField] private float spacing = 10f;
    [SerializeField] private int sortingOrder = 10;

    private static readonly int ColorId = Shader.PropertyToID("_Color");

    // Bir yapı seçili, yerleştiriliyor (eldiven gizlenir: GloveCursor)
    public static bool IsPlacing { get; private set; }

    private GameObject root;
    private RectTransform cardRow;
    private readonly List<Image> tabImages = new List<Image>();
    private readonly List<(Image image, GridEntitySOBase data)> cards = new List<(Image, GridEntitySOBase)>();
    private int category;

    private GridEntitySOBase selected;
    private GridMaskRotator.Rotation rotation; // seçimler arasında korunur
    private GridPlaceable probe;               // seçili yapının kapalı kopyası: şekli / konabilirliği bununla
    private PlacementGhost ghost;
    private Material validMaterial, invalidMaterial;
    private Plane plane;
    // Mouse UI'nin üstünde mi: Update'te okunur (input olaylarının içinden EventSystem'e sorulamaz)
    private bool pointerOverUi;

    void Awake()
    {
        plane = new Plane(Vector3.up, transform.position);
        BuildUi();
        ShowCategory(0);
    }

    void OnEnable()
    {
        InputManager.OnMouseLeft += OnMouseLeft;
        InputManager.OnMouseRight += OnMouseRight;
        InputManager.OnR += Rotate;
    }

    void OnDisable()
    {
        InputManager.OnMouseLeft -= OnMouseLeft;
        InputManager.OnMouseRight -= OnMouseRight;
        InputManager.OnR -= Rotate;
        ClearSelection();
    }

    void OnDestroy()
    {
        IsPlacing = false;
        if (validMaterial != null) Destroy(validMaterial);
        if (invalidMaterial != null) Destroy(invalidMaterial);
    }

    void Update()
    {
        // Geliştirici build mode'u açıkken bar gizli, seçim bırakılır
        bool show = !PlacementController.IsBuildMode;
        if (root.activeSelf != show)
        {
            root.SetActive(show);
            if (!show) ClearSelection();
        }
        if (!show) return;

        pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (selected != null && !InputManager.Locked && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            ClearSelection();
        UpdatePreview();
    }

    // --- Arayüz ---

    private void BuildUi()
    {
        root = new GameObject("Player Build Menu", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();

        // Bar: altta, ekran genişliğinde
        RectTransform bar = NewRect("Bar", root.transform);
        bar.anchorMin = new Vector2(0f, 0f);
        bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.sizeDelta = new Vector2(0f, barHeight);
        bar.anchoredPosition = Vector2.zero;
        AddPanel(bar.gameObject, barColor);

        // Sekmeler barın sol üst köşesinin üstünde
        RectTransform tabRow = NewRect("Tabs", bar);
        tabRow.anchorMin = tabRow.anchorMax = new Vector2(0f, 1f);
        tabRow.pivot = new Vector2(0f, 0f);
        tabRow.anchoredPosition = new Vector2(spacing, 0f);
        tabRow.sizeDelta = new Vector2(0f, tabHeight);
        var tabLayout = tabRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabLayout.spacing = spacing * 0.5f;
        tabLayout.childForceExpandWidth = tabLayout.childForceExpandHeight = false;
        tabLayout.childControlWidth = tabLayout.childControlHeight = true;
        var tabFitter = tabRow.gameObject.AddComponent<ContentSizeFitter>();
        tabFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        List<BuildCatalogSO.Category> categories = catalog != null ? catalog.Categories : new List<BuildCatalogSO.Category>();
        for (int i = 0; i < categories.Count; i++)
        {
            int index = i;
            Button tab = NewButton(categories[i].Name, tabRow, new Vector2(tabWidth, tabHeight), tabColor, out Image image);
            tab.onClick.AddListener(() => ShowCategory(index));
            NewLabel(tab.transform, categories[i].Name, 22f, stretch: true);
            tabImages.Add(image);
        }

        // Kartlar barın içinde, soldan dizilir
        cardRow = NewRect("Cards", bar);
        cardRow.anchorMin = Vector2.zero;
        cardRow.anchorMax = Vector2.one;
        cardRow.offsetMin = new Vector2(spacing, spacing);
        cardRow.offsetMax = new Vector2(-spacing, -spacing);
        var cardLayout = cardRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        cardLayout.spacing = spacing;
        cardLayout.childAlignment = TextAnchor.MiddleLeft;
        cardLayout.childForceExpandWidth = cardLayout.childForceExpandHeight = false;
        cardLayout.childControlWidth = cardLayout.childControlHeight = true;
    }

    private void ShowCategory(int index)
    {
        category = index;
        for (int i = 0; i < tabImages.Count; i++)
            tabImages[i].color = i == category ? tabSelectedColor : tabColor;

        foreach (var card in cards) Destroy(card.image.gameObject);
        cards.Clear();
        if (catalog == null || category < 0 || category >= catalog.Categories.Count) return;

        foreach (GridEntitySOBase data in catalog.Categories[category].Items)
        {
            if (data == null) continue;
            GridEntitySOBase item = data;
            Button card = NewButton(item.Name, cardRow, cardSize, cardColor, out Image image);
            card.onClick.AddListener(() => OnCardClicked(item));

            if (item.Icon != null)
            {
                RectTransform icon = NewRect("Icon", card.transform);
                icon.anchorMin = new Vector2(0.1f, 0.3f);
                icon.anchorMax = new Vector2(0.9f, 0.95f);
                icon.offsetMin = icon.offsetMax = Vector2.zero;
                var iconImage = icon.gameObject.AddComponent<Image>();
                iconImage.sprite = item.Icon;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
            }
            // İkon yoksa ad kartın ortasında, varsa altında
            TextMeshProUGUI label = NewLabel(card.transform, item.Name, 18f, stretch: item.Icon == null);
            if (item.Icon != null)
            {
                RectTransform rect = label.rectTransform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0.3f);
                rect.offsetMin = new Vector2(4f, 2f);
                rect.offsetMax = new Vector2(-4f, 0f);
            }
            cards.Add((image, item));
        }
        RefreshCards();
    }

    private void RefreshCards()
    {
        foreach (var card in cards)
            card.image.color = card.data == selected ? cardSelectedColor : cardColor;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private Image AddPanel(GameObject go, Color color)
    {
        var image = go.AddComponent<Image>();
        image.color = color;
        if (panelSprite != null)
        {
            image.sprite = panelSprite;
            image.type = Image.Type.Sliced;
        }
        return image;
    }

    private Button NewButton(string name, Transform parent, Vector2 size, Color color, out Image image)
    {
        RectTransform rect = NewRect(name, parent);
        var layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = size.x;
        layout.preferredHeight = size.y;
        image = AddPanel(rect.gameObject, color);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        return button;
    }

    private TextMeshProUGUI NewLabel(Transform parent, string text, float fontSize, bool stretch)
    {
        RectTransform rect = NewRect("Label", parent);
        if (stretch)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(4f, 2f);
            rect.offsetMax = new Vector2(-4f, -2f);
        }
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.enableAutoSizing = true;
        label.fontSizeMin = 10f;
        label.fontSizeMax = fontSize;
        label.color = textColor;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }

    // --- Seçim ---

    private void OnCardClicked(GridEntitySOBase data)
    {
        if (data == selected) ClearSelection();
        else Select(data);
    }

    private void Select(GridEntitySOBase data)
    {
        ClearSelection();
        if (!(data.GetPrefabBase() is GridPlaceable)) return;

        selected = data;
        probe = CreateInstance();
        probe.gameObject.SetActive(false); // sadece şekli ve konabilirliği için; oyunda yok
        EnsureMaterials();
        // Hayalet dönüşsüz kurulur (dönüş gösterirken verilir)
        probe.Rotation = GridMaskRotator.Rotation.Deg0;
        IReadOnlyList<Vector3Int> cells = probe.GetFootprint().FilledCells();
        probe.Rotation = rotation;
        ghost = new PlacementGhost(probe.gameObject, cells, validMaterial, invalidMaterial);
        SetPlacing(true);
        RefreshCards();
    }

    private void ClearSelection()
    {
        if (selected == null) return;
        selected = null;
        ghost?.Destroy();
        ghost = null;
        if (probe != null) Destroy(probe.gameObject);
        probe = null;
        SetPlacing(false);
        RefreshCards();
    }

    // Seçiliyken tıklamalar yerleştirmeye gider: obje tutma ve aletler kapanır. Bırakınca geri açılır (geliştirici build
    // mode'u açıksa açılmaz, onları o yönetiyor)
    private void SetPlacing(bool placing)
    {
        IsPlacing = placing;
        if (!placing && PlacementController.IsBuildMode) return;
        if (interactableController != null) interactableController.enabled = !placing;
        if (toolController != null) toolController.enabled = !placing;
    }

    private GridPlaceable CreateInstance()
    {
        GridPlaceable instance = Instantiate((GridPlaceable)selected.GetPrefabBase());
        instance.AssignData(selected);
        instance.Rotation = rotation;
        return instance;
    }

    private void EnsureMaterials()
    {
        if (validMaterial != null) return;
        Shader shader = ghostShader != null ? ghostShader : Shader.Find("Village/Placement Ghost");
        validMaterial = new Material(shader) { name = "Build Menu Ghost Valid (generated)" };
        invalidMaterial = new Material(shader) { name = "Build Menu Ghost Invalid (generated)" };
    }

    private void Rotate()
    {
        if (selected == null) return;
        rotation = GridMaskRotator.Next(rotation);
        probe.Rotation = rotation;
    }

    // --- Koyma ---

    private void OnMouseLeft(bool pressed)
    {
        if (!pressed || selected == null || pointerOverUi) return;
        if (!TryGetTargetPosition(out Vector3 position)) return;

        GridPlaceable instance = CreateInstance();
        position += instance.RotationCenterShift;
        if (!GridManager.Instance.CanPlaceablePlaceOn(instance, position) || !GridManager.Instance.PlaceablePlaceOn(instance, position))
            Destroy(instance.gameObject);
    }

    // Sağ tık seçimi bırakır, ama sadece kısa tıkta: sağ tıkla sürükleyip kamerayı döndürünce seçim kalır
    private Vector3 rightPressMouse;

    private void OnMouseRight(bool pressed)
    {
        if (pressed)
        {
            rightPressMouse = Input.mousePosition;
            return;
        }
        if ((Input.mousePosition - rightPressMouse).magnitude < 6f) ClearSelection();
    }

    // Baktığı objenin üstü, yoksa bu objenin yüksekliğindeki düzlem (geliştirici menüsüyle aynı kural)
    private bool TryGetTargetPosition(out Vector3 position)
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            position = hit.point + Vector3.up / 2;
            return true;
        }
        if (plane.Raycast(ray, out float enter))
        {
            position = ray.GetPoint(enter);
            return true;
        }
        position = Vector3.zero;
        return false;
    }

    private void UpdatePreview()
    {
        if (ghost == null) return;
        if (pointerOverUi || !TryGetTargetPosition(out Vector3 target))
        {
            ghost.HideFrom(0);
            return;
        }

        validMaterial.SetColor(ColorId, validColor);
        invalidMaterial.SetColor(ColorId, invalidColor);
        target += probe.RotationCenterShift; // dönünce ortası yerinde kalsın
        Vector3 cell = GridManager.Instance.GetGridWorldPosition(target, out bool onGrid);
        if (!onGrid) cell = Vector3Int.RoundToInt(target);
        ghost.Show(0, cell, probe.GridRotation, GridManager.Instance.CanPlaceablePlaceOn(probe, target));
        ghost.HideFrom(1);
    }
}
