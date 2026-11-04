// Toon ışığının Lit'e bağlı olmayan kısmı: ToonStyle'ın globalleri + sert geçişli ışık / gölge rengi. Toon.hlsl (Lit
// kopyaları) ve StylizedWater.shader (su dünya gibi ışık alsın) kullanır. Önce URP Lighting.hlsl include edilmiş olmalı.
#ifndef VILLAGE_TOON_CORE_INCLUDED
#define VILLAGE_TOON_CORE_INCLUDED

float _VillageToon;
float _VillageToonThreshold;
float _VillageToonSoftness;
half4 _VillageToonShadowColor;
float _VillageToonShadowSaturation;
float _VillageToonSpecMinSmoothness;
float _VillageToonSpecSize;
float _VillageToonSpecStrength;
float _VillageToonAmbient;
float _VillageToonShadowSoftness;
float _VillageToonNormalDetail;
float _VillageToonMidOffset;
float _VillageToonMidBrightness;

half ToonStep(half value, half threshold)
{
    half soft = max(_VillageToonSoftness, 0.001);
    return smoothstep(threshold - soft, threshold + soft, value);
}

// Ekran türeviyle yumuşayan eşik: değer piksel başına hızlı değişiyorsa (uzakta) geçiş en az o kadar geniş
half ToonStepAA(half value, half threshold)
{
    half soft = max(_VillageToonSoftness, fwidth(value));
    return smoothstep(threshold - soft, threshold + soft, value);
}

// Bir ışığın aydınlık payı (0 gölge, 1 aydınlık): yüzeyin ışığa bakması (sert) ve gerçek zamanlı gölge (biraz yumuşak).
// tone: üç tonlu hali (aydınlık ama ışığa yan bakan yer ara tonda)
half ToonLit(Light light, half3 lightNormal, out half tone)
{
    half ndl = dot(lightNormal, light.direction);
    half facing = ToonStepAA(ndl, _VillageToonThreshold);
    half shadowSoft = max(_VillageToonShadowSoftness, 0.001);
    half shadow = smoothstep(0.5 - shadowSoft, 0.5 + shadowSoft, light.shadowAttenuation);
    half lit = facing * shadow;
    half full = ToonStepAA(ndl, _VillageToonThreshold + _VillageToonMidOffset);
    tone = lit * lerp(_VillageToonMidBrightness, 1.0, full);
    return lit;
}

// Gölgedeki renk: gölge rengiyle çarpılır, sonra doygunluğu artırılır (gri değerden uzaklaştırılır)
half3 ToonShadowAlbedo(half3 albedo)
{
    half3 dark = albedo * _VillageToonShadowColor.rgb;
    half luma = dot(dark, half3(0.299, 0.587, 0.114));
    return max(0.0, lerp(luma.xxx, dark, 1.0 + _VillageToonShadowSaturation));
}

#endif
