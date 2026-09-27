using System;
using System.Collections;
using UnityEngine;

// Jöle gibi sallanma: taban sabit, yukarı çıktıkça artan eğilme + yaylanan basılma. Asıl deformasyon shader'da
// (Wobble.hlsl); bu sınıf başlangıçta renderer'lara veri gönderir, animasyonu GPU zamana bakarak kendisi oynatır.
// Renderer'ların materyali "Village/Wood Wobble Lit" olmalı.
[Serializable]
public class JellyWobble
{
    private static readonly int BaseId = Shader.PropertyToID("_WobbleBase");
    private static readonly int DirId = Shader.PropertyToID("_WobbleDir");
    private static readonly int UpId = Shader.PropertyToID("_WobbleUp");
    private static readonly int ShapeId = Shader.PropertyToID("_WobbleShape");

    private static MaterialPropertyBlock block;

    [Tooltip("Yandan itilince tepenin yana kayma miktarı (dünya birimi)")]
    [SerializeField] private float bendAmount = 0.15f;
    [Tooltip("Basılma/uzama miktarı (0.1 = %10)")]
    [SerializeField, Range(0f, 0.5f)] private float squashAmount = 0.12f;
    [Tooltip("Sallanmanın toplam süresi (saniye)")]
    [SerializeField] private float duration = 0.6f;
    [Tooltip("Süre boyunca kaç kez gidip gelsin")]
    [SerializeField] private float oscillations = 2.5f;

    // baseWS: objenin tabanının ortası (hiç kıpırdamaz), heightWS: objenin yüksekliği,
    // pushDirectionWS: ilk eğilme yönü (yatay). Sıfırsa eğilmez, sadece basılıp yaylanır.
    public IEnumerator Play(Renderer[] renderers, Vector3 baseWS, float heightWS, Vector3 pushDirectionWS)
    {
        block ??= new MaterialPropertyBlock();
        float startTime = Time.timeSinceLevelLoad; // shader'daki _Time.y ile aynı saat
        Vector3 bendWS = pushDirectionWS.sqrMagnitude > 0.0001f ? pushDirectionWS.normalized * bendAmount : Vector3.zero;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;

            // Shader object space'te çalışıyor. Her parça kendi uzayına çevrilir ama hepsi aynı dünya
            // tabanını ve yönünü kullandığı için birden çok parçalı obje tek parça gibi sallanır.
            Matrix4x4 toLocal = renderer.transform.worldToLocalMatrix;
            Vector3 up = toLocal.MultiplyVector(Vector3.up);
            float localHeight = up.magnitude * heightWS;
            up.Normalize();
            Vector3 bend = toLocal.MultiplyVector(bendWS);
            Vector3 basePoint = toLocal.MultiplyPoint3x4(baseWS);

            renderer.GetPropertyBlock(block);
            block.SetVector(BaseId, new Vector4(basePoint.x, basePoint.y, basePoint.z, localHeight));
            block.SetVector(DirId, new Vector4(bend.x, bend.y, bend.z, startTime));
            block.SetVector(UpId, new Vector4(up.x, up.y, up.z, squashAmount));
            block.SetVector(ShapeId, new Vector4(duration, oscillations, 0f, 0f));
            renderer.SetPropertyBlock(block);
        }

        yield return new WaitForSeconds(duration);
        Clear(renderers);
    }

    // Animasyon bitince veriyi kaldır: renderer tekrar paylaşılan materyalle toplu çizilebilsin (SRP Batcher)
    public static void Clear(Renderer[] renderers)
    {
        foreach (Renderer renderer in renderers)
            if (renderer != null) renderer.SetPropertyBlock(null);
    }
}
