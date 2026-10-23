using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlacementController : MonoBehaviour
{
    [SerializeField] private GridPlacementControllerBase[] placementBases;
    [SerializeField] private GameObject placementMenu;
    [SerializeField] private Image[] placementSlots;
    [SerializeField] private TextMeshProUGUI[] placementNames;
    [SerializeField] private Button openBuildMode;
    [SerializeField] private Button[] menuButtons;
    [SerializeField] private TextMeshProUGUI menuName;

    [SerializeField] private Color selectedColor = Color.green;
    [SerializeField] private Color defaultColor = Color.white;

    private GridPlacementControllerBase currentMenu;
    private int currentIndex = -1;
    private int page; // menüler kutu sayısı (9) kadarlık sayfalara bölünür, build mode'da mouse scroll ile değişir
    private bool isOpen = true;
    private Image openBuildModeImage => openBuildMode.GetComponent<Image>();


    [Header("Build mode'da kapananlar")]
    [SerializeField] private InteractableController interactableController;
    [SerializeField] private ToolController toolController;

    // OnEnable'da değil: SetBuildMode bu component'i kapatıp açıyor, her açılışta yeniden abone olunur
    // ve bir süre sonra tek Space basışı birden çok kez tetiklenirdi (build mode kapanmıyordu)
    void Awake()
    {
        if (interactableController == null) interactableController = GetComponent<InteractableController>();
        if (toolController == null) toolController = GetComponent<ToolController>();

        InputManager.OnSpace += OpenBuildMode;
        foreach (GridPlacementControllerBase menu in placementBases)
            menu.SelectionChanged += HighlightSlot;
        SetBuildMode(isOpen); // sahnedeki başlangıç durumları ne olursa olsun hepsi uyumlu başlasın
    }

    void OnDestroy()
    {
        InputManager.OnSpace -= OpenBuildMode;
        foreach (GridPlacementControllerBase menu in placementBases)
            if (menu != null) menu.SelectionChanged -= HighlightSlot;
    }

    // Seçili objenin kutusu yanar (sayı tuşuyla seçilir; -1: seçim yok)
    private void HighlightSlot(int slot)
    {
        for (int i = 0; i < placementSlots.Length; i++)
            placementSlots[i].color = i == slot ? selectedColor : Color.white;
    }

    // Build mode açık mı (eldiven gizlenir, mouse imleci görünür: GloveCursor)
    public static bool IsBuildMode { get; private set; }

    // Build mode'da bir menü seçili: mouse scroll sayfa değiştirir, kamera zoom yapmaz (CameraController)
    public static bool ScrollChangesPage { get; private set; }

    private int PageSize => Mathf.Min(placementSlots.Length, placementNames.Length);

    private void Update()
    {
        if (currentMenu == null || PageSize == 0) return;
        float scroll = InputManager.ScrollDelta();
        if (Mathf.Abs(scroll) < 0.001f) return;

        int pageCount = Mathf.Max(1, Mathf.CeilToInt(currentMenu.MenusSO.Entities.Count / (float)PageSize));
        if (pageCount <= 1) return;
        // Aşağı scroll sonraki sayfa; sondan başa döner. Seçim önceki sayfanındı: bırakılır
        page = (page + (scroll < 0f ? 1 : -1) + pageCount) % pageCount;
        currentMenu.ClearSelection();
        SetMenuElements(currentIndex);
    }

    // Space ve build mode butonu bunu çağırır
    public void OpenBuildMode() => SetBuildMode(!isOpen);

    // Build mode'un tek yetkilisi: yerleştirme açıkken obje tutma ve aletler kapalı, kapalıyken açık.
    // Tersine çevirmek (toggle) yerine açıkça set edilir, böylece iki taraf birbirinden kopamaz.
    public void SetBuildMode(bool open)
    {
        isOpen = open;
        IsBuildMode = open;

        SetDeActiveMenus();
        enabled = open;
        placementMenu.SetActive(open);
        openBuildModeImage.color = open ? Color.green : Color.red;

        if (interactableController != null) interactableController.enabled = !open;
        if (toolController != null) toolController.enabled = !open;
    }

    public void SelectMenu(int index)
    {
        SetDeActiveMenus();
        placementBases[index].enabled = true;
        page = 0;
        SetMenuElements(index);
        ScrollChangesPage = true;

        currentIndex = index;
        UpdateButtonHighlight();
    }

    public void SetDeActiveMenus()
    {
        foreach (var a in placementBases) a.enabled = false;
        currentMenu = null;
        ScrollChangesPage = false;
        ResetSpritesAndNames();
        ResetButtonHighlight();
    }

    private void UpdateButtonHighlight()
    {
        for (int i = 0; i < menuButtons.Length; i++)
        {
            Image img = menuButtons[i].targetGraphic as Image;
            if (img == null) continue;
            img.color = (i == currentIndex) ? selectedColor : defaultColor;
        }
    }
    private void ResetButtonHighlight()
    {
        for (int i = 0; i < menuButtons.Length; i++)
        {
            Image img = menuButtons[i].targetGraphic as Image; 
            if (img == null) continue;
            img.color = defaultColor;
        }
    }
    private void SetMenuElements(int index)
    {
        ResetSpritesAndNames();
        currentMenu = placementBases[index];
        // Sadece bu sayfanın elemanları; sayı tuşları da bu sayfadan sayar
        int start = page * PageSize;
        currentMenu.PageOffset = start;
        var entities = currentMenu.MenusSO.Entities;
        for (int slot = 0; slot < PageSize && start + slot < entities.Count; slot++)
        {
            placementSlots[slot].sprite = entities[start + slot].Icon;
            placementNames[slot].text = entities[start + slot].Name;
        }
    }

    private void ResetSpritesAndNames()
    {
        foreach (var a in placementSlots)
        {
            a.color = Color.white;
            a.sprite = null;
        }
        foreach (var a in placementNames) a.text = "";
    }
}