using System;
using System.Collections;
using UnityEngine;

// Vuruşla sarsılma: obje itildiği yöne yatar, sonra sönümlenerek sallanıp durur.
// Objenin pivot'u etrafında döner; pivot gövdenin dibindeyse ağaç kökünden sallanır.
[Serializable]
public class HitShake
{
    [Tooltip("İlk yatma açısı (derece)")]
    [SerializeField] private float angle = 5f;
    [SerializeField] private float duration = 0.45f;
    [Tooltip("Saniyedeki sallanma sayısı")]
    [SerializeField] private float frequency = 5f;

    // pushDirection: objenin yatacağı yön (dünya uzayında, yatay)
    public IEnumerator Play(Transform target, Quaternion restRotation, Vector3 pushDirection)
    {
        // Yukarı ekseni pushDirection'a doğru yatıran dönme ekseni, parent uzayına çevrilmiş
        Vector3 axis = Vector3.Cross(Vector3.up, pushDirection).normalized;
        if (target.parent != null)
            axis = target.parent.InverseTransformDirection(axis);

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float remaining = 1f - elapsed / duration;
            float current = angle * remaining * remaining * Mathf.Cos(elapsed * frequency * 2f * Mathf.PI);
            target.localRotation = Quaternion.AngleAxis(current, axis) * restRotation;
            yield return null;
        }
        target.localRotation = restRotation;
    }
}
