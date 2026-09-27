#ifndef VILLAGE_CHOP_DENT_INCLUDED
#define VILLAGE_CHOP_DENT_INCLUDED

// Balta vuruşunda gövdenin vurulan tarafının içeri göçüp esneyerek geri dönmesi.
// Değerleri Tree (ChopDent.cs) vuruş anında MaterialPropertyBlock ile, sadece o ağacın renderer'ına gönderir;
// paylaşılan materyalin ayarı olmadıkları için UnityPerMaterial CBUFFER'ında değiller.
// Hepsi renderer'ın object space'inde: ağaç sallanınca göçük de modelle beraber hareket eder.
// Hiç ayarlanmamışsa (vuruş yok) hepsi 0'dır ve hiçbir etki olmaz.
float4 _ChopHitPoint; // xyz: gövde yüzeyindeki vuruş noktası, w: göçük derinliği
float4 _ChopHitDir;   // xyz: içeri yön, w: vuruş zamanı (_Time.y cinsinden)
float4 _ChopUp;       // xyz: ağacın yukarı yönü (dikey yarıçap bu eksende), w: geri dönüşte esneme sayısı
float4 _ChopShape;    // x: yatay yarıçap, y: dikey yarıçap, z: içeri çökme süresi, w: geri dönüş süresi

// 0 → 1 → (sönümlenerek esneyip) → 0
float ChopDentAnimation()
{
    float t = _Time.y - _ChopHitDir.w;
    float pushIn = max(_ChopShape.z, 0.0001);
    float release = max(_ChopShape.w, 0.0001);

    if (t < 0.0) return 0.0;
    if (t < pushIn)
    {
        float x = t / pushIn;
        return 1.0 - (1.0 - x) * (1.0 - x); // hızlı başlayıp yavaşlayarak içeri
    }

    float r = (t - pushIn) / release;
    if (r >= 1.0) return 0.0;
    return (1.0 - r) * (1.0 - r) * cos(r * _ChopUp.w * TWO_PI); // sönümlenen esneme
}

// Vertex'in göçük offset'i; gradient normali düzeltmek için göçüğün eğimi
float3 ChopDentOffset(float3 positionOS, out float3 gradient)
{
    gradient = 0;

    float depth = _ChopHitPoint.w * ChopDentAnimation();
    if (depth == 0.0) return 0;

    // Vuruş noktası etrafında elips: yatayda ve dikeyde ayrı yarıçap
    float2 radius = max(_ChopShape.xy, 0.0001);
    float3 d = positionOS - _ChopHitPoint.xyz;
    float vertical = dot(d, _ChopUp.xyz);
    float3 horizontal = d - _ChopUp.xyz * vertical;

    float3 scaled = horizontal / radius.x + _ChopUp.xyz * (vertical / radius.y);
    float r = length(scaled);
    if (r >= 1.0) return 0;

    // Merkezde tam derinlik, kenara doğru yumuşakça sıfır
    float amount = depth * (1.0 - smoothstep(0.0, 1.0, r));

    if (r > 0.0001)
    {
        float dAmount_dr = -depth * 6.0 * r * (1.0 - r);
        float3 dr_dp = (horizontal / (radius.x * radius.x) + _ChopUp.xyz * (vertical / (radius.y * radius.y))) / r;
        gradient = dAmount_dr * dr_dp;
    }

    return _ChopHitDir.xyz * amount;
}

void ApplyChopDent(inout float3 positionOS)
{
    float3 gradient;
    positionOS += ChopDentOffset(positionOS, gradient);
}

// Normal de göçüğün eğimine göre yatırılır; ışık çukuru doğru gölgelendirir
void ApplyChopDent(inout float3 positionOS, inout float3 normalOS)
{
    float3 gradient;
    positionOS += ChopDentOffset(positionOS, gradient);
    normalOS = normalize(normalOS + gradient - normalOS * dot(gradient, normalOS));
}

#endif
