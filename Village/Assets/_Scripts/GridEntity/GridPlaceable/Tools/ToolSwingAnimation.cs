using System;
using System.Collections;
using UnityEngine;

// Aletin vurma animasyonu: geri çek (wind-up) → vur (strike) → eski haline dön.
// Sadece görseli (child) döndürür; aletin root'u GridDragMotor'a ait, ona dokunmaz.
[Serializable]
public class ToolSwingAnimation
{
    [Tooltip("Görselin yerel ekseni: alet bu eksen etrafında sallanır. Yanlış yöne sallanıyorsa değiştir.")]
    [SerializeField] private Vector3 axis = Vector3.right;
    [SerializeField] private float windUpAngle = -50f;
    [SerializeField] private float strikeAngle = 30f;
    [SerializeField] private float windUpDuration = 0.15f;
    [SerializeField] private float strikeDuration = 0.07f;
    [SerializeField] private float returnDuration = 0.2f;

    // onImpact vuruş anında çağrılır; asıl iş (ağacı kesmek) o an yapılır
    public IEnumerator Play(Transform visual, Quaternion restRotation, Action onImpact)
    {
        yield return Tween(visual, restRotation, 0f, windUpAngle, windUpDuration, EaseOut);  // yavaşça geri çek
        yield return Tween(visual, restRotation, windUpAngle, strikeAngle, strikeDuration, EaseIn); // hızla vur
        onImpact?.Invoke();
        yield return Tween(visual, restRotation, strikeAngle, 0f, returnDuration, EaseOut);  // yerine dön
    }

    private IEnumerator Tween(Transform visual, Quaternion restRotation, float from, float to, float duration, Func<float, float> ease)
    {
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float angle = Mathf.LerpUnclamped(from, to, ease(elapsed / duration));
            visual.localRotation = restRotation * Quaternion.AngleAxis(angle, axis);
            yield return null;
        }
        visual.localRotation = restRotation * Quaternion.AngleAxis(to, axis);
    }

    private static float EaseIn(float t) => t * t;                       // yavaş başla, hızlan
    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);   // hızlı başla, yavaşla
}
