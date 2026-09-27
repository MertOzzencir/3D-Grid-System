#ifndef VILLAGE_WOBBLE_INCLUDED
#define VILLAGE_WOBBLE_INCLUDED

// Jöle gibi sallanma: taban sabit kalır, yukarı çıktıkça artan eğilme + yaylanan basılma/uzama.
// Değerleri JellyWobble.cs animasyon başında MaterialPropertyBlock ile, sadece o objenin renderer'larına gönderir.
// Hepsi renderer'ın object space'inde; birden çok parçadan oluşan obje (MergedWood) tek parça gibi sallanır.
// Hiç ayarlanmamışsa (animasyon yok) hepsi 0'dır ve hiçbir etki olmaz.
float4 _WobbleBase;  // xyz: objenin tabanı (hiç kıpırdamayan nokta), w: objenin yüksekliği
float4 _WobbleDir;   // xyz: ilk eğilme yönü; boyu = tepenin yana kayma miktarı (0 ise sadece basılır), w: başlangıç zamanı (_Time.y)
float4 _WobbleUp;    // xyz: yukarı yön, w: basılma miktarı (0.1 = %10)
float4 _WobbleShape; // x: süre, y: salınım sayısı

// 0'dan başlar, ilk önce pozitife (itilme yönüne / basılmaya) gider, süre boyunca sönümlenir
float WobbleAnimation()
{
    float duration = max(_WobbleShape.x, 0.0001);
    float t = (_Time.y - _WobbleDir.w) / duration;
    if (t <= 0.0 || t >= 1.0) return 0.0;
    return (1.0 - t) * (1.0 - t) * sin(t * _WobbleShape.y * TWO_PI);
}

// slope: eğilmenin yüksekliğe göre değişimi, stretch: dikey ölçek; ikisi de normal düzeltmesi için
float3 WobbleDeform(float3 positionOS, out float slope, out float stretch)
{
    slope = 0.0;
    stretch = 1.0;

    float anim = WobbleAnimation();
    if (anim == 0.0) return positionOS;

    float height = max(_WobbleBase.w, 0.0001);
    float along = dot(positionOS - _WobbleBase.xyz, _WobbleUp.xyz); // tabandan yükseklik
    float h = saturate(along / height);

    // Eğilme: tabanda 0, tepede tam; h² ile yumuşak bir kavis
    float3 bend = _WobbleDir.xyz * (anim * h * h);
    slope = (along > 0.0 && along < height) ? anim * 2.0 * h / height : 0.0;

    // Basılma/uzama: taban yerinde kalır, yukarısı dikeyde ölçeklenir
    stretch = 1.0 - _WobbleUp.w * anim;
    float3 squash = _WobbleUp.xyz * (along * (stretch - 1.0));

    return positionOS + bend + squash;
}

void ApplyWobble(inout float3 positionOS)
{
    float slope, stretch;
    positionOS = WobbleDeform(positionOS, slope, stretch);
}

// Normal de eğilmeye ve basılmaya göre düzeltilir; ışık eğilen yüzeyde doğru düşer
void ApplyWobble(inout float3 positionOS, inout float3 normalOS)
{
    float slope, stretch;
    positionOS = WobbleDeform(positionOS, slope, stretch);

    float3 n = normalOS - _WobbleUp.xyz * dot(_WobbleDir.xyz, normalOS) * slope;
    n += _WobbleUp.xyz * dot(_WobbleUp.xyz, n) * (1.0 / max(stretch, 0.0001) - 1.0);
    normalOS = normalize(n);
}

#endif
