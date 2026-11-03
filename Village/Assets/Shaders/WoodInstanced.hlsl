#ifndef VILLAGE_WOOD_INSTANCED_INCLUDED
#define VILLAGE_WOOD_INSTANCED_INCLUDED

// 1×1 odunların GPU instancing'li çizimi için (Village/Wood Instanced Lit; çizen WoodInstancer.cs, Graphics.RenderMeshInstanced).
// Wobble.hlsl + Fade.hlsl'in aynısı ama değerler instance başına (UNITY_INSTANCING_BUFFER) ve sallanma dünya uzayında:
// instance'lar ayrı objeler, aynı yapının (MergedWood) parçalarına aynı dünya değerleri gider, yapı tek parça gibi sallanır.
// Hiç ayarlanmamışsa hepsi 0'dır: sallanma ve saydamlık yok.
#include "Fade.hlsl" // BayerThreshold (oradaki _Fade burada kullanılmaz)

UNITY_INSTANCING_BUFFER_START(VillageWood)
    UNITY_DEFINE_INSTANCED_PROP(float4, _WoodWobbleBase)  // xyz: taban (dünya), w: yükseklik
    UNITY_DEFINE_INSTANCED_PROP(float4, _WoodWobbleDir)   // xyz: eğilme (dünya), w: başlangıç zamanı (_Time.y)
    UNITY_DEFINE_INSTANCED_PROP(float4, _WoodWobbleUp)    // xyz: yukarı (dünya), w: basılma miktarı
    UNITY_DEFINE_INSTANCED_PROP(float4, _WoodWobbleShape) // x: süre, y: salınım sayısı
    UNITY_DEFINE_INSTANCED_PROP(float4, _WoodFade)        // Fade.hlsl'deki _Fade gibi
UNITY_INSTANCING_BUFFER_END(VillageWood)

#define VILLAGE_WOOD_PROP(name) UNITY_ACCESS_INSTANCED_PROP(VillageWood, name)

// Wobble.hlsl'deki WobbleDeform'un dünya uzayındaki hali; normal de düzeltilir
void ApplyWoodInstanced(inout float3 positionOS, inout float3 normalOS)
{
    float4 shape = VILLAGE_WOOD_PROP(_WoodWobbleShape);
    float4 dir = VILLAGE_WOOD_PROP(_WoodWobbleDir);
    float t = (_Time.y - dir.w) / max(shape.x, 0.0001);
    if (t <= 0.0 || t >= 1.0) return;
    float anim = (1.0 - t) * (1.0 - t) * sin(t * shape.y * TWO_PI);
    if (anim == 0.0) return;

    float4 base = VILLAGE_WOOD_PROP(_WoodWobbleBase);
    float4 up = VILLAGE_WOOD_PROP(_WoodWobbleUp);
    float3 positionWS = TransformObjectToWorld(positionOS);
    float height = max(base.w, 0.0001);
    float along = dot(positionWS - base.xyz, up.xyz); // tabandan yükseklik
    float h = saturate(along / height);

    // Eğilme: tabanda 0, tepede tam; basılma: taban yerinde, yukarısı dikeyde ölçeklenir
    float slope = (along > 0.0 && along < height) ? anim * 2.0 * h / height : 0.0;
    float stretch = 1.0 - up.w * anim;
    positionWS += dir.xyz * (anim * h * h) + up.xyz * (along * (stretch - 1.0));
    positionOS = TransformWorldToObject(positionWS);

    float3 n = TransformObjectToWorldNormal(normalOS);
    n -= up.xyz * dot(dir.xyz, n) * slope;
    n += up.xyz * dot(up.xyz, n) * (1.0 / max(stretch, 0.0001) - 1.0);
    normalOS = TransformWorldToObjectNormal(normalize(n));
}

void ApplyWoodInstanced(inout float3 positionOS)
{
    float3 normalOS = float3(0.0, 1.0, 0.0);
    ApplyWoodInstanced(positionOS, normalOS);
}

// positionCS: fragment'taki SV_POSITION (piksel koordinatı)
void ApplyWoodFade(float4 positionCS)
{
    float4 fadeData = VILLAGE_WOOD_PROP(_WoodFade);
    float t = saturate((_Time.y - fadeData.z) / max(fadeData.w, 0.0001));
    float fade = lerp(fadeData.x, fadeData.y, t);
    if (fade <= 0.0) return;
    clip((1.0 - fade) - BayerThreshold(positionCS.xy));
}

#endif
