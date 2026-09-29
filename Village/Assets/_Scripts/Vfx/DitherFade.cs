using System;
using System.Collections;
using UnityEngine;

// Noktalı (dither) yarı saydamlık: obje opak kalır, piksellerin bir kısmı düzenli bir desenle atılır (Fade.hlsl).
// Geçişi GPU zamana bakarak kendisi oynatır; bu sınıf sadece başlangıç/hedef değerlerini gönderir.
// Renderer'ların materyali "Village/Wood Wobble Lit" olmalı.
[Serializable]
public class DitherFade
{
    private static readonly int FadeId = Shader.PropertyToID("_Fade");

    private static MaterialPropertyBlock block;

    [Tooltip("Saydamken ne kadarı görünsün (0.3 = %30)")]
    [SerializeField, Range(0f, 1f)] private float visibleAmount = 0.3f;
    [Tooltip("Saydamlaşma / geri gelme süresi (saniye)")]
    [SerializeField] private float duration = 0.15f;

    // Kaldığı yerden yarı saydama geçer
    public void FadeOut(Renderer[] renderers) => AnimateTo(renderers, 1f - visibleAmount);

    // Kaldığı yerden tam görünüre döner, bitince veriyi kaldırır.
    // Bu sırada aynı renderer'lar tekrar FadeOut edilecekse önce bu coroutine durdurulmalı.
    public IEnumerator FadeIn(Renderer[] renderers)
    {
        AnimateTo(renderers, 0f);
        yield return new WaitForSeconds(duration);
        Clear(renderers);
    }

    private void AnimateTo(Renderer[] renderers, float target)
    {
        block ??= new MaterialPropertyBlock();
        float now = Time.timeSinceLevelLoad; // shader'daki _Time.y ile aynı saat

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;

            // Diğer efektlerin (örn. JellyWobble) verisi korunsun diye mevcut blok okunup üzerine yazılır
            renderer.GetPropertyBlock(block);
            float current = Evaluate(block.GetVector(FadeId), now);
            block.SetVector(FadeId, new Vector4(current, target, now, duration));
            renderer.SetPropertyBlock(block);
        }
    }

    // Fade.hlsl'deki FadeAmount'ın aynısı
    private static float Evaluate(Vector4 fade, float now)
    {
        float t = Mathf.Clamp01((now - fade.z) / Mathf.Max(fade.w, 0.0001f));
        return Mathf.Lerp(fade.x, fade.y, t);
    }

    // Veriyi kaldır: renderer tekrar paylaşılan materyalle toplu çizilebilsin (SRP Batcher)
    public static void Clear(Renderer[] renderers)
    {
        foreach (Renderer renderer in renderers)
            if (renderer != null) renderer.SetPropertyBlock(null);
    }
}
