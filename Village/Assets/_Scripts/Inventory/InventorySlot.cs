using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InventorySlot : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI sourceName;
    [SerializeField] private TextMeshProUGUI sourceAmount;
    public SourcesSO SourceOnSlot { get; set; }
    private Color originalColor;
    private Image ownImage;
    private Coroutine slotAnim;

    void Awake()
    {

        ownImage = GetComponent<Image>();
        originalColor = ownImage.color;
    }
    public void Add(SourcesSO source, string amount, string name, Sprite Icon)
    {
        SourceOnSlot = source;
        sourceAmount.text = amount;
        sourceName.text = name;
        icon.sprite = Icon;
        icon.color = new Color(icon.color.r, icon.color.b, icon.color.g, 1);
        if (gameObject.activeInHierarchy)
        {
            if (slotAnim != null)
            {
                StopAllCoroutines();
                ownImage.color = originalColor;
            }
            slotAnim = StartCoroutine(SlotAnimation());
        }
    }
    public void Reset()
    {
        SourceOnSlot = null;
        sourceAmount.text = "";
        sourceName.text = "";
        icon.sprite = default;
    }
    private IEnumerator SlotAnimation()
    {
        ownImage.color = Color.green;
        yield return new WaitForSeconds(0.1f);
        ownImage.color = originalColor;
        slotAnim = null;
    }
}
