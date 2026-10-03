#ifndef VILLAGE_WIND_INCLUDED
#define VILLAGE_WIND_INCLUDED

// Rüzgâr: objenin yukarıdaki kısımları (ağaç tepesi) dünya uzayında yavaşça salınır. Etki objenin pivot'undan
// yüksekliğe göre artar: dip sabit, tepe en çok. Her obje kendi dünya konumuna göre farklı fazda sallanır (hepsi
// birlikte sallanmasın). Değerler global, WindSettings.cs her kare gönderir; hiç ayarlanmamışsa (genlik 0) etkisiz.
// Hesap dünya uzayında yapılıp object space'e çevrilir (ağaç 90° dönük de olsa rüzgâr aynı yönden eser).
float4 _WindParams;    // x: genlik (birim), y: hız, z: etkinin başladığı yükseklik (pivot'tan), w: tam etkiye kadar yükseklik
float4 _WindDirection; // xz: yön (dünya), w: esinti (0..1, yavaşça değişir)

float3 WindOffsetWS(float3 positionWS, float3 pivotWS)
{
    if (_WindParams.x <= 0.0) return 0;

    float height = positionWS.y - pivotWS.y;
    float mask = saturate((height - _WindParams.z) / max(_WindParams.w, 0.001));
    mask *= mask;

    float phase = dot(pivotWS.xz, float2(0.9, 0.6));
    float t = _Time.y * _WindParams.y;
    float gust = 0.75 + 0.25 * _WindDirection.w;
    float2 along = normalize(_WindDirection.xz + 1e-5);
    float2 across = float2(-along.y, along.x);

    // İki dalga (düzenli görünmesin) + yana küçük salınım: tepe hafif daire çizer
    float sway = sin(t + phase) * 0.7 + sin(t * 2.3 + phase * 1.7) * 0.3;
    float side = cos(t * 1.3 + phase) * 0.35;
    float2 offset = (along * sway + across * side) * _WindParams.x * gust * mask;
    return float3(offset.x, 0, offset.y);
}

void ApplyWind(inout float3 positionOS)
{
    float3 positionWS = TransformObjectToWorld(positionOS);
    float3 pivotWS = TransformObjectToWorld(float3(0, 0, 0));
    positionOS += mul((float3x3)GetWorldToObjectMatrix(), WindOffsetWS(positionWS, pivotWS));
}

#endif
