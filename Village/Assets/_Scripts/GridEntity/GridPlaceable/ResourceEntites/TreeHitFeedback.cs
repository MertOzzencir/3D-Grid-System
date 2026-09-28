using System.Collections;
using UnityEngine;

// Ağaca balta vurunca oynayan geri bildirimler: talaş efekti, göçük, sarsılma.
// Ayarları Tree'de durur (Inspector'da tek yerden ayarlansın diye); bu sınıf sadece oynatır
// ve üst üste gelen vuruşlarda coroutine'lerin birbirine karışmamasını sağlar.
public class TreeHitFeedback
{
    private readonly MonoBehaviour runner;
    private readonly HitShake hitShake;
    private readonly ChopDent chopDent;

    private Coroutine shakeRoutine;
    private Transform shakingVisual;
    private Quaternion shakeRestRotation;
    private Coroutine dentRoutine;

    // hitShake ve chopDent Tree'nin kendi alanları: Inspector'da değiştirilince buraya da yansır
    public TreeHitFeedback(MonoBehaviour runner, HitShake hitShake, ChopDent chopDent)
    {
        this.runner = runner;
        this.hitShake = hitShake;
        this.chopDent = chopDent;
    }

    // surfacePoint: gövde yüzeyindeki vuruş noktası, outward: gövdeden baltaya doğru yatay yön
    // visual: sallanacak (şu an görünen) ağaç görseli, dentRenderers: göçecek renderer'lar
    public void Play(Vector3 surfacePoint, Vector3 outward, ParticleSystem chipEffect, Transform visual, Renderer[] dentRenderers)
    {
        if (chipEffect != null)
            VfxPool.Play(chipEffect, surfacePoint, Quaternion.LookRotation(outward, Vector3.up)); // +Z gövdeden dışarı

        StopShake(); // göçük noktası, ağacın düz (sallanmayan) haline göre hesaplansın

        if (dentRoutine != null) runner.StopCoroutine(dentRoutine); // yeni vuruş öncekinin yerine geçer
        dentRoutine = runner.StartCoroutine(chopDent.Play(dentRenderers, surfacePoint, -outward));

        if (visual == null) return;
        shakingVisual = visual;
        shakeRestRotation = visual.localRotation;
        shakeRoutine = runner.StartCoroutine(ShakeRoutine(-outward)); // baltadan uzağa doğru yatar
    }

    // Önceki sallanma bitmeden yeni vuruş gelirse görseli düz haline döndürür
    private void StopShake()
    {
        if (shakeRoutine == null) return;
        runner.StopCoroutine(shakeRoutine);
        shakeRoutine = null;
        shakingVisual.localRotation = shakeRestRotation;
    }

    private IEnumerator ShakeRoutine(Vector3 pushDirection)
    {
        yield return hitShake.Play(shakingVisual, shakeRestRotation, pushDirection);
        shakeRoutine = null;
    }
}
