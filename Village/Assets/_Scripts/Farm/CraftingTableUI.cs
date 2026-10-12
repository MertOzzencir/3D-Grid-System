using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Çalışma masasının basit ekran arayüzü (geçici; ileride dünyada bir seçim sistemi olabilir). Kodla kurulur, asset yok.
// Ekranın altında: ◀  blueprint adı / maliyet / gemideki toprak  ▶ ve kısa mesaj. Eldiven bir masanın yanındayken
// görünür (SoilCraftingTable.Update). Oyuncunun gördüğü yazılar İngilizce.
public static class CraftingTableUI
{
    private static GameObject root;
    private static RectTransform panel;
    private static TextMeshProUGUI title, details, message;
    private static SoilCraftingTable current;

    public static bool IsPointerOver { get; private set; }

    public static void Show(SoilCraftingTable table)
    {
        if (root == null) Build();
        current = table;
        root.SetActive(true);
        Refresh();
    }

    public static void Hide()
    {
        current = null;
        IsPointerOver = false;
        if (root != null) root.SetActive(false);
    }

    private static void Refresh()
    {
        if (current == null) return;
        GridPlaceableSO data = current.SelectedBlueprint;
        string name = data == null ? "No blueprints" : string.IsNullOrEmpty(data.Name) ? data.name : data.Name;
        title.text = current.HasFinishedPiece ? $"{name} is ready" : name;
        int soil = current.Ship != null ? current.Ship.Soil : 0;
        details.text = $"Cost: {current.SelectedCost} soil     Soil: {soil}";
        message.text = current.Message ?? (current.HasFinishedPiece ? "Right-click and hold to take it" : "Click the cells to fill them");
    }

    private static void Build()
    {
        if (EventSystem.current == null)
            Debug.LogWarning("CraftingTableUI: sahnede EventSystem yok; düğmeler tıklanmaz.");

        root = new GameObject("Crafting Table UI");
        Object.DontDestroyOnLoad(root);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
        root.AddComponent<UiUpdater>();

        panel = NewRect("Panel", root.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.sizeDelta = new Vector2(620f, 130f);
        panel.anchoredPosition = new Vector2(0f, 40f);
        var background = panel.gameObject.AddComponent<Image>();
        background.color = new Color(0.98f, 0.94f, 0.86f, 0.92f);
        panel.gameObject.AddComponent<PointerTracker>();

        title = NewText("Title", panel, new Vector2(0f, 28f), 34f, FontStyles.Bold);
        details = NewText("Details", panel, new Vector2(0f, -10f), 24f, FontStyles.Normal);
        message = NewText("Message", panel, new Vector2(0f, -42f), 20f, FontStyles.Italic);
        message.color = new Color(0.45f, 0.35f, 0.28f);

        NewButton("Previous", panel, new Vector2(-260f, 0f), "<", () => { current?.Cycle(-1); Refresh(); });
        NewButton("Next", panel, new Vector2(260f, 0f), ">", () => { current?.Cycle(1); Refresh(); });
        root.SetActive(false);
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static TextMeshProUGUI NewText(string name, RectTransform parent, Vector2 position, float size, FontStyles style)
    {
        RectTransform rect = NewRect(name, parent);
        rect.sizeDelta = new Vector2(440f, 40f);
        rect.anchoredPosition = position;
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(0.3f, 0.22f, 0.16f);
        text.raycastTarget = false;
        return text;
    }

    private static void NewButton(string name, RectTransform parent, Vector2 position, string label, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = NewRect(name, parent);
        rect.sizeDelta = new Vector2(70f, 90f);
        rect.anchoredPosition = position;
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.85f, 0.55f, 0.38f);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);
        TextMeshProUGUI text = NewText("Label", rect, Vector2.zero, 44f, FontStyles.Bold);
        text.rectTransform.sizeDelta = rect.sizeDelta;
        text.text = label;
        text.color = Color.white;
    }

    // Mouse panelin üstünde mi (eldiven o sırada gizli, masa paneli kapatmasın)
    private class PointerTracker : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public void OnPointerEnter(PointerEventData eventData) => IsPointerOver = true;
        public void OnPointerExit(PointerEventData eventData) => IsPointerOver = false;
        private void OnDisable() => IsPointerOver = false;
    }

    // Toprak ve mesaj değişince yazılar güncellensin
    private class UiUpdater : MonoBehaviour
    {
        private void Update() => Refresh();
    }
}
