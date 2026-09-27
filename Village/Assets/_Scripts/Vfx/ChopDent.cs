using System;
using System.Collections;
using UnityEngine;

// Vuruşta gövdenin içeri göçüp esneyerek geri dönmesi. Asıl deformasyon shader'da (ChopDent.hlsl);
// bu sınıf vuruş anında renderer'lara veri gönderir, animasyonu GPU zamana bakarak kendisi oynatır.
// Renderer'ın materyali "Village/Tree Chop Lit" olmalı.
[Serializable]
public class ChopDent
{
    private static readonly int HitPointId = Shader.PropertyToID("_ChopHitPoint");
    private static readonly int HitDirId = Shader.PropertyToID("_ChopHitDir");
    private static readonly int UpId = Shader.PropertyToID("_ChopUp");
    private static readonly int ShapeId = Shader.PropertyToID("_ChopShape");

    private static MaterialPropertyBlock block;

    [Tooltip("Göçüğün içeri gideceği mesafe (dünya birimi)")]
    [SerializeField] private float depth = 0.08f;
    [Tooltip("Göçüğün yatay yarıçapı (dünya birimi)")]
    [SerializeField] private float horizontalRadius = 0.3f;
    [Tooltip("Göçüğün dikey yarıçapı (dünya birimi)")]
    [SerializeField] private float verticalRadius = 0.2f;
    [Tooltip("İçeri çökme süresi (saniye)")]
    [SerializeField] private float pushInDuration = 0.1f;
    [Tooltip("Esneyerek geri dönme süresi (saniye)")]
    [SerializeField] private float returnDuration = 0.35f;
    [Tooltip("Geri dönerken kaç kez esnesin")]
    [SerializeField] private float springCount = 1.5f;

    // surfacePointWS: gövde yüzeyindeki vuruş noktası, inwardWS: baltadan gövdeye doğru yön
    public IEnumerator Play(Renderer[] renderers, Vector3 surfacePointWS, Vector3 inwardWS)
    {
        block ??= new MaterialPropertyBlock();
        float hitTime = Time.timeSinceLevelLoad; // shader'daki _Time.y ile aynı saat

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue; // bu arada silinmiş olabilir

            // Shader object space'te çalışıyor: noktaları, yönleri ve uzunlukları renderer'ın uzayına çevir
            Matrix4x4 toLocal = renderer.transform.worldToLocalMatrix;
            Vector3 inward = toLocal.MultiplyVector(inwardWS).normalized;
            Vector3 up = toLocal.MultiplyVector(Vector3.up).normalized;
            Vector3 sideways = Vector3.Cross(Vector3.up, inwardWS).normalized;

            float localDepth = toLocal.MultiplyVector(inwardWS.normalized * depth).magnitude;
            float localHorizontal = toLocal.MultiplyVector(sideways * horizontalRadius).magnitude;
            float localVertical = toLocal.MultiplyVector(Vector3.up * verticalRadius).magnitude;

            renderer.GetPropertyBlock(block);
            block.SetVector(HitPointId, (Vector4)toLocal.MultiplyPoint3x4(surfacePointWS) + new Vector4(0, 0, 0, localDepth));
            block.SetVector(HitDirId, new Vector4(inward.x, inward.y, inward.z, hitTime));
            block.SetVector(UpId, new Vector4(up.x, up.y, up.z, springCount));
            block.SetVector(ShapeId, new Vector4(localHorizontal, localVertical, pushInDuration, returnDuration));
            renderer.SetPropertyBlock(block);
        }

        yield return new WaitForSeconds(pushInDuration + returnDuration);
        Clear(renderers);
    }

    // Animasyon bitince veriyi kaldır: renderer tekrar paylaşılan materyalle toplu çizilebilsin (SRP Batcher)
    public static void Clear(Renderer[] renderers)
    {
        foreach (Renderer renderer in renderers)
            if (renderer != null) renderer.SetPropertyBlock(null);
    }
}
