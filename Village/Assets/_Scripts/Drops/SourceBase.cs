using System.Collections;
using UnityEngine;

public class SourceBase : GridPlaceable
{
    [SerializeField] private SourcesSO sourceData;
    [SerializeField] private AnimationCurve yOffset;
    [SerializeField] private float duration = 0.5f;
    [SerializeField] private Transform visual;

    private Vector3 visualLocalPosition;
    public void Collect()
    {
        InventoryManager.Instance.AddSource(sourceData);
        Destroy(gameObject);
    }
    public void OnSpawned()
    {
        visualLocalPosition = visual.localPosition;
    }
    public void SpawnAnimation(Vector3 targetSpawnPosition, Vector3 start)
    {
        StartCoroutine(Animation(visual, start, targetSpawnPosition, duration, yOffset));
    }

    public IEnumerator Animation(Transform visual, Vector3 start, Vector3 target, float duration, AnimationCurve yOffset)
    {
        Vector3 startPosition = start;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float curveValue = yOffset.Evaluate(t);

            Vector3 horizontalPos = Vector3.Lerp(startPosition, target + visualLocalPosition, t);

            visual.transform.position = horizontalPos + Vector3.up * curveValue;

            yield return null;
        }
        visual.transform.position = target + visualLocalPosition;
    }


}
