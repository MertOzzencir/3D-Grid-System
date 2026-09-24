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


    void OnEnable()
    {
        InputManager.OnSpace += OpenBuildMode;
    }
  
    public void OpenBuildMode()
    {
        isOpen = !isOpen;

        SetDeActiveMenus();
        enabled = isOpen;
        placementMenu.SetActive(isOpen);
        openBuildModeImage.color = isOpen ? Color.green : Color.red;
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