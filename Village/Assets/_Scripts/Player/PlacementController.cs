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
        SetBuildMode(isOpen); // sahnedeki başlangıç durumları ne olursa olsun hepsi uyumlu başlasın
    }

    void OnDestroy()
    {
        InputManager.OnSpace -= OpenBuildMode;
    }

    // Space ve build mode butonu bunu çağırır
    public void OpenBuildMode() => SetBuildMode(!isOpen);

    // Build mode'un tek yetkilisi: yerleştirme açıkken obje tutma ve aletler kapalı, kapalıyken açık.
    // Tersine çevirmek (toggle) yerine açıkça set edilir, böylece iki taraf birbirinden kopamaz.
    public void SetBuildMode(bool open)
    {
        isOpen = open;

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
        SetMenuElements(index);

        currentIndex = index;
        UpdateButtonHighlight();
    }

    public void SetDeActiveMenus()
    {
        foreach (var a in placementBases) a.enabled = false;
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
        int timer = 0;
        foreach (var a in currentMenu.MenusSO.Entities)
        {
            placementSlots[timer].sprite = a.Icon;
            placementNames[timer].text = a.Name;
            timer++;
            if (timer >= placementSlots.Length || timer >= placementNames.Length) break;
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