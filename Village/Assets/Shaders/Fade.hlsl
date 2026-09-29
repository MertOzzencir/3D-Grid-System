#ifndef VILLAGE_FADE_INCLUDED
#define VILLAGE_FADE_INCLUDED

// Noktalı (dither) saydamlık: obje opak kalır, ekranda düzenli bir desenle piksellerin bir kısmı atılır.
// Sıralama sorunu yok, derinliğe yazmaya devam eder; gölge tam kalır (ShadowCaster'a eklenmez).
// Değerleri DitherFade.cs MaterialPropertyBlock ile sadece ilgili renderer'lara gönderir; geçişi GPU zamana bakarak oynatır.
// Hiç ayarlanmamışsa hepsi 0'dır: obje tamamen görünür.
float4 _Fade; // x: başlangıç saydamlığı, y: hedef saydamlık (0 = tam görünür, 1 = görünmez), z: başlangıç zamanı (_Time.y), w: süre

float FadeAmount()
{
    float t = saturate((_Time.y - _Fade.z) / max(_Fade.w, 0.0001));
    return lerp(_Fade.x, _Fade.y, t);
}

// 2x2 Bayer: (0,0)=0 (1,0)=2 (0,1)=3 (1,1)=1
float Bayer2(float2 p)
{
    return fmod(2.0 * p.x + 3.0 * p.y, 4.0);
}

// 4x4 Bayer eşiği (0..1): komşu pikseller birbirinden olabildiğince farklı eşik alır, desen düzgün dağılır.
// Sadece float işlem; target 2.0 pass'lerinde de derlenir.
float BayerThreshold(float2 pixel)
{
    float2 p = floor(fmod(floor(pixel), 4.0));
    float value = 4.0 * Bayer2(fmod(p, 2.0)) + Bayer2(floor(p * 0.5));
    return (value + 0.5) / 16.0;
}

// positionCS: fragment'taki SV_POSITION (piksel koordinatı)
void ApplyFade(float4 positionCS)
{
    float fade = FadeAmount();
    if (fade <= 0.0) return;
    clip((1.0 - fade) - BayerThreshold(positionCS.xy));
}

#endif
