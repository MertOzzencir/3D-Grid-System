using TMPro;
using UnityEngine;

// Dünyada kısa süren yazı (hasat kombosu, "Tam tur!"): belirdiği yerden yükselir, büyüyüp söner, kameraya bakar.
public class FarmPopup : MonoBehaviour
{
    private const float Duration = 0.9f;
    private const float Rise = 0.8f;

    private TextMeshPro text;
    private Vector3 start;
    private Color color;
    private float startTime;

    public static void Show(string message, Vector3 position, Color color, float size)
    {
        var popup = new GameObject("Popup").AddComponent<FarmPopup>();
        popup.text = popup.gameObject.AddComponent<TextMeshPro>();
        popup.text.text = message;
        popup.text.fontSize = size;
        popup.text.alignment = TextAlignmentOptions.Center;
        popup.text.fontStyle = FontStyles.Bold;
        popup.text.textWrappingMode = TextWrappingModes.NoWrap;
        popup.text.rectTransform.sizeDelta = new Vector2(4f, 1f);
        popup.text.color = color;
        popup.color = color;
        popup.start = position;
        popup.startTime = Time.time;
        popup.LateUpdate();
    }

    private void LateUpdate()
    {
        float t = (Time.time - startTime) / Duration;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }
        Quaternion facing = Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity;
        transform.SetPositionAndRotation(start + Vector3.up * (Rise * (1f - (1f - t) * (1f - t))), facing);
        transform.localScale = Vector3.one * (t < 0.15f ? Mathf.Lerp(0.5f, 1.15f, t / 0.15f) : Mathf.Lerp(1.15f, 1f, (t - 0.15f) / 0.85f));
        Color c = color;
        c.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
        text.color = c;
    }
}
