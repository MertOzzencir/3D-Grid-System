using System;
using System.Collections;
using UnityEngine;

// Vuruşta yerel jöle titreşimi (örn. kedinin kıçına şaplak). Asıl deformasyon shader'da (Jiggle.hlsl);
// bu sınıf vuruş anında renderer'lara veri gönderir, animasyonu GPU zamana bakarak kendisi oynatır.
// Renderer'ın materyali "Village/Creature Jiggle Lit" olmalı. Skinned mesh'te de çalışır (kemiklerden sonra uygulanır).
[Serializable]
public class CreatureJiggle
{
    private static readonly int CenterId = Shader.PropertyToID("_JiggleCenter");
    private static readonly int DirId = Shader.PropertyToID("_JiggleDir");
    private static readonly int ShapeId = Shader.PropertyToID("_JiggleShape");
    private static readonly int SwellId = Shader.PropertyToID("_JiggleSwell");

    private static MaterialPropertyBlock block;

    [Tooltip("İçeri göçme derinliği (dünya birimi)")]
    [SerializeField] private float depth = 0.11f;
    [Tooltip("Etkinin vuruş noktasından yayıldığı yarıçap (dünya birimi)")]
    [SerializeField] private float radius = 0.5f;
    [Tooltip("Toplam süre (saniye)")]
    [SerializeField] private float duration = 0.8f;
    [Tooltip("Süre boyunca kaç kez titresin")]
    [SerializeField] private float oscillations = 3.5f;
    [Tooltip("Dışa yayılan dalganın boyu (dünya birimi)")]
    [SerializeField] private float waveLength = 0.15f;
    [Tooltip("Dalganın miktarı (göçme derinliğine oranla)")]
    [SerializeField, Range(0f, 1f)] private float waveAmount = 0.35f;
    [Tooltip("Bölgenin şişip inme miktarı (dünya birimi); 0 = kapalı")]
    [SerializeField] private float swellAmount = 0.06f;
    [Tooltip("Süre boyunca kaç kez şişip insin")]
    [SerializeField] private float swellOscillations = 2f;
    [Tooltip("Şişme merkezinin yüzeyden içeride derinliği (dünya birimi); bölge bu noktadan dışa doğru şişer")]
    [SerializeField] private float swellCenterDepth = 0.25f;

    public float Duration => duration;

    // hitPointWS: vuruş noktası (yüzeyde), inwardWS: içeri doğru yön (eldivenden gövdeye)
    public IEnumerator Play(Renderer[] renderers, Vector3 hitPointWS, Vector3 inwardWS)
    {
        block ??= new MaterialPropertyBlock();
        float startTime = Time.timeSinceLevelLoad; // shader'daki _Time.y ile aynı saat
        Vector3 push = inwardWS.normalized * depth;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;

            // Dünya uzayında: shader kendisi çevirir (skinned mesh'in object space'i kök kemiğe göre değişir)
            renderer.GetPropertyBlock(block);
            block.SetVector(CenterId, new Vector4(hitPointWS.x, hitPointWS.y, hitPointWS.z, radius));
            block.SetVector(DirId, new Vector4(push.x, push.y, push.z, startTime));
            block.SetVector(ShapeId, new Vector4(duration, oscillations, waveLength, waveAmount));
            block.SetVector(SwellId, new Vector4(swellAmount, swellOscillations, swellCenterDepth, 0f));
            renderer.SetPropertyBlock(block);
        }

        yield return new WaitForSeconds(duration);

        // Arada yeni bir vuruş başladıysa (başlangıç zamanı değiştiyse) onun verisine dokunma, yoksa yarıda kesilir
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(block);
            if (Mathf.Approximately(block.GetVector(DirId).w, startTime)) renderer.SetPropertyBlock(null);
        }
    }

    // Animasyon bitince veriyi kaldır: renderer tekrar paylaşılan materyalle toplu çizilebilsin (SRP Batcher)
    public static void Clear(Renderer[] renderers)
    {
        foreach (Renderer renderer in renderers)
            if (renderer != null) renderer.SetPropertyBlock(null);
    }
}
