using UnityEngine;

public class ToolController : MonoBehaviour
{
    public static ToolController Instance;

    private ToolBase[] tools = new ToolBase[9];
    private ToolBase CurrentTool;
    private bool isOpen;

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void Update()
    {
        if (CurrentTool == null) return;
    }

    // private void PickTool(int obj, bool performed)
    // {
    //     if (!performed) return; 

    //     int index = obj - 1;
    //     if (index < 0 || index >= tools.Length || tools[index] == null)
    //     {
    //         if (CurrentTool != null)
    //         {
    //             CurrentTool.DeEquip();
    //             CurrentTool = null;
    //         }
    //         return;
    //     }

    //     if (CurrentTool != null)
    //     {
    //         CurrentTool.DeEquip();
    //         if (CurrentTool == tools[index])
    //         {
    //             CurrentTool = null;
    //             return;
    //         }
    //     }

    //     CurrentTool = tools[index];
    //     CurrentTool.Equip();
    // }

    public void SetTools(ToolBase tool, int index)
    {
        if (index < 0 || index >= tools.Length) return;
        tools[index] = tool;
    }

    public void SetEnable()
    {
        isOpen = !isOpen;
        enabled = isOpen;
    }
    private void IsTool(IInteractable interactable)
    {
        if (interactable is ToolBase tool)
        {
            CurrentTool = tool;
        }
    }
    void OnEnable()
    {
        InteractableController.OnNewInteractable += IsTool;
    }

    void OnDisable()
    {
        InteractableController.OnNewInteractable -= IsTool;
        if (CurrentTool == null) return;
        CurrentTool = null;
    }
}