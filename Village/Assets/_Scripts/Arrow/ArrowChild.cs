using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public class ArrowChild : ArrowBase
{
    [SerializeField] private AnimationCurve sizeAnimationCurve;
    [SerializeField] private float sizeAnimationDuration = 0.3f;
    [SerializeField] private float animationHeightTarget = 1.5f;
    [SerializeField] private float animationSpeed = 12f;

    public override void Logic(Action<ArrowBase> logicCallBack)
    {
        base.Logic(logicCallBack);
        animatedPart.parent = null;
        StartCoroutine(OnDestroyAnimation(logicCallBack));
    }
 
    private IEnumerator OnDestroyAnimation(Action<ArrowBase> logicCallBack)
    {
        yield return AnimateSize();
        yield return AnimateMovement(logicCallBack);
    }

    private IEnumerator AnimateSize()
    {
        Vector3 originalScale = animatedPart.localScale;
        float elapsed = 0f;

        while (elapsed < sizeAnimationDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / sizeAnimationDuration;
            float curveValue = sizeAnimationCurve.Evaluate(t);
            animatedPart.localScale = originalScale * curveValue;
            yield return null;
        }

        animatedPart.localScale = originalScale * sizeAnimationCurve.Evaluate(1f);
    }

    private IEnumerator AnimateMovement(Action<ArrowBase> logicCallBack)
    {
        Vector3 target = animatedPart.position + Vector3.up * animationHeightTarget;

        while (Vector3.Distance(animatedPart.position, target) > 0.1f)
        {
            animatedPart.position = Vector3.MoveTowards(animatedPart.position, target, animationSpeed * Time.deltaTime);
            yield return null;
        }

        Destroy(animatedPart.gameObject);
        logicCallBack?.Invoke(this);
        //Destroy(gameObject);
    }
}