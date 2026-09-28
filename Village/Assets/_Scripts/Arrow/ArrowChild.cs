using System;
using System.Collections;
using UnityEngine;

// Ağacın üst kısmını kesen ok. Kesilen parça (animatedPart, pivot'u altında) baltadan uzağa devrilir,
// yere çarpıp seker, biraz yatar, küçülerek kaybolur ve odun düştüğü yerden çıkar.
public class ArrowChild : ArrowBase
{
    [Header("Devrilme")]
    [Tooltip("Parçanın devrileceği açı (90 = tam yatay)")]
    [SerializeField] private float fallAngle = 85f;
    [Tooltip("Devrilme süresi; yavaş başlayıp yerçekimi gibi hızlanır")]
    [SerializeField] private float fallDuration = 0.8f;
    [Tooltip("Baltanın itişiyle parçanın devrilme yönünde kayacağı mesafe (dünya birimi)")]
    [SerializeField] private float pushDistance = 0.35f;
    [Tooltip("Kaymanın süresi; hızlı başlar, sürtünmeyle yavaşlar. Devrilmeyle aynı anda başlar.")]
    [SerializeField] private float pushDuration = 0.4f;
    [Tooltip("Yere çarpınca geri sekme açısı")]
    [SerializeField] private float bounceAngle = 8f;
    [SerializeField] private float bounceDuration = 0.3f;
    [Tooltip("Yerde yatma süresi")]
    [SerializeField] private float lingerDuration = 0.3f;
    [Tooltip("Küçülerek kaybolma süresi; bitince odun çıkar")]
    [SerializeField] private float shrinkDuration = 0.25f;
    [Tooltip("Yere çarpınca oynar (toz, yaprak). Boş bırakılabilir.")]
    [SerializeField] private ParticleSystem landingEffect;

    private Vector3 spawnOrigin;

    // Odun kütüğün tepesinden değil, parçanın düştüğü yerden çıksın
    public override Vector3 SpawnOrigin => spawnOrigin;

    public override void Logic(Action<ArrowBase> logicCallBack)
    {
        base.Logic(logicCallBack);
        animatedPart.parent = null;
        spawnOrigin = animatedPart.position;
        StartCoroutine(FallRoutine(logicCallBack));
    }

    private IEnumerator FallRoutine(Action<ArrowBase> logicCallBack)
    {
        Vector3 direction = Vector3.ProjectOnPlane(FallDirection, Vector3.up);
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.ProjectOnPlane(transform.forward, Vector3.up); // yön verilmediyse
        direction.Normalize();

        // Bu eksen etrafında pozitif dönüş, parçanın yukarısını direction'a doğru yatırır
        Vector3 axis = Vector3.Cross(Vector3.up, direction);
        Quaternion startRotation = animatedPart.rotation;

        yield return Fall(startRotation, axis, direction);
        PlayLandingEffect();
        yield return Tilt(startRotation, axis, fallAngle, fallAngle - bounceAngle, bounceDuration * 0.5f, EaseOut);
        yield return Tilt(startRotation, axis, fallAngle - bounceAngle, fallAngle, bounceDuration * 0.5f, EaseIn);
        yield return new WaitForSeconds(lingerDuration);

        spawnOrigin = GetVisualCenter();
        yield return Shrink(shrinkDuration);

        Destroy(animatedPart.gameObject);
        logicCallBack?.Invoke(this);
    }

    // Devrilme + itilme aynı anda: dönüş yerçekimi gibi hızlanır (EaseIn),
    // kayma baltanın darbesi gibi hızlı başlayıp yavaşlar (EaseOut)
    private IEnumerator Fall(Quaternion startRotation, Vector3 axis, Vector3 direction)
    {
        Vector3 startPosition = animatedPart.position;
        float duration = Mathf.Max(fallDuration, pushDuration);

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float tiltT = fallDuration > 0f ? Mathf.Clamp01(elapsed / fallDuration) : 1f;
            float pushT = pushDuration > 0f ? Mathf.Clamp01(elapsed / pushDuration) : 1f;

            animatedPart.rotation = Quaternion.AngleAxis(fallAngle * EaseIn(tiltT), axis) * startRotation;
            animatedPart.position = startPosition + direction * (pushDistance * EaseOut(pushT));
            yield return null;
        }

        animatedPart.rotation = Quaternion.AngleAxis(fallAngle, axis) * startRotation;
        animatedPart.position = startPosition + direction * pushDistance;
    }

    // Pivot etrafında from → to açısına döner
    private IEnumerator Tilt(Quaternion startRotation, Vector3 axis, float from, float to, float duration, Func<float, float> ease)
    {
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float angle = Mathf.LerpUnclamped(from, to, ease(elapsed / duration));
            animatedPart.rotation = Quaternion.AngleAxis(angle, axis) * startRotation;
            yield return null;
        }
        animatedPart.rotation = Quaternion.AngleAxis(to, axis) * startRotation;
    }

    private IEnumerator Shrink(float duration)
    {
        Vector3 startScale = animatedPart.localScale;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            animatedPart.localScale = startScale * (1f - EaseIn(elapsed / duration));
            yield return null;
        }
        animatedPart.localScale = Vector3.zero;
    }

    private void PlayLandingEffect()
    {
        if (landingEffect != null)
            VfxPool.Play(landingEffect, GetVisualCenter(), Quaternion.identity);
    }

    // Parçanın görselinin (tüm renderer'larının) ortası
    private Vector3 GetVisualCenter()
    {
        Renderer[] renderers = animatedPart.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return animatedPart.position;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds.center;
    }

    private static float EaseIn(float t) => t * t;                       // yavaş başla, hızlan
    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);   // hızlı başla, yavaşla
}
