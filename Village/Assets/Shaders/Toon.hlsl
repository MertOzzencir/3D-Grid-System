// Toon (cel) ışıklandırma denemesi. Lit kopyalarının (Village/Lit, Wood Wobble Lit, Tree Chop Lit, Creature Jiggle Lit)
// ForwardLit fragment'ı LitPassFragment yerine ToonLitPassFragment'ı çağırır:
//   _VillageToon = 0 (ToonStyle kapalı ya da sahnede yok) → birebir URP'nin LitPassFragment'ı (eski görünüm)
//   _VillageToon = 1 → yüzey aynı (doku, renk, normal map), ışık sert geçişli: ışık eşikte birden gölgeye döner
//   (_VillageToonThreshold, geçiş genişliği _VillageToonSoftness), gölgede albedo × _VillageToonShadowColor ve doygunluğu
//   artırılmış (_VillageToonShadowSaturation: gölge kirli griye değil canlı, tonu kaymış renge döner), parlama keskin
//   kenarlı leke (_VillageToonSpecSize, _VillageToonSpecStrength; sadece smoothness'ı _VillageToonSpecMinSmoothness'tan
//   büyük materyallerde: çim, toprak, odun mat kalır), ortam ışığı (SH) payı _VillageToonAmbient. Gerçek zamanlı gölgeler de aynı eşikle sertleşir. SSAO toon'da kullanılmaz (yumuşak leke yapar).
// Uzakta temiz kalsın diye: aydınlık-gölge eşiği ekran türeviyle yumuşar (fwidth: uzakta piksel başına değişim büyük,
// eşik pürüzlü bantlara dönmesin), ışığın yönü için yüzeyin kendi normali normal map'le sadece _VillageToonNormalDetail
// kadar karışır (uzakta küçülen normal map yüzeyi bulandırıp koyu lekeler yapıyordu), gerçek zamanlı gölgenin kenarı
// _VillageToonShadowSoftness kadar yumuşak (uzaktaki düşük çözünürlüklü gölge pürüzlü çıkmasın). Üç ton: ışığa tam
// bakan yer tam aydınlık, eşiği az geçen yer _VillageToonMidBrightness kadar (ara ton, _VillageToonMidOffset ötede biter).
// Ayarların hepsi global (materyal başına değil): ToonStyle bileşeni yazar. Globaller ve Lit'e bağlı olmayan ışık
// fonksiyonları ToonCore.hlsl'de (su da kullanır: StylizedWater.shader).
#ifndef VILLAGE_TOON_INCLUDED
#define VILLAGE_TOON_INCLUDED

#include "ToonCore.hlsl"

half3 ToonSpecular(Light light, InputData inputData, SurfaceData surfaceData, half lit)
{
    half3 halfDir = SafeNormalize(light.direction + inputData.viewDirectionWS);
    half highlight = ToonStep(dot(inputData.normalWS, halfDir), 1.0 - _VillageToonSpecSize);
    half glossy = smoothstep(_VillageToonSpecMinSmoothness, _VillageToonSpecMinSmoothness + 0.1, surfaceData.smoothness);
    return light.color * (highlight * lit * glossy * _VillageToonSpecStrength);
}

// lightNormal: ışığın yönü için kullanılan normal (yüzeyin kendi normali + biraz normal map)
half4 ToonFragment(InputData inputData, SurfaceData surfaceData, half3 lightNormal)
{
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor noOcclusion;
    noOcclusion.directAmbientOcclusion = 1.0;
    noOcclusion.indirectAmbientOcclusion = 1.0;
    uint meshRenderingLayers = GetMeshRenderingLayer();

    half3 albedo = surfaceData.albedo;
    half3 color = inputData.bakedGI * albedo * surfaceData.occlusion * _VillageToonAmbient;

    // Ana ışık: gölgede albedo × gölge rengi, aydınlıkta albedo
    Light mainLight = GetMainLight(inputData, shadowMask, noOcclusion);
#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        half tone;
        half lit = ToonLit(mainLight, lightNormal, tone);
        half3 light = mainLight.color * mainLight.distanceAttenuation;
        color += light * lerp(ToonShadowAlbedo(albedo), albedo, tone);
        color += ToonSpecular(mainLight, inputData, surfaceData, lit) * mainLight.distanceAttenuation;
    }

    // Ek ışıklar (lamba vb.): sadece aydınlık kısmı eklenir
#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, noOcclusion);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            half tone;
            half lit = ToonLit(light, lightNormal, tone);
            color += albedo * light.color * light.distanceAttenuation * tone;
            color += ToonSpecular(light, inputData, surfaceData, lit) * light.distanceAttenuation;
        }
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, noOcclusion);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            half tone;
            half lit = ToonLit(light, lightNormal, tone);
            color += albedo * light.color * light.distanceAttenuation * tone;
            color += ToonSpecular(light, inputData, surfaceData, lit) * light.distanceAttenuation;
        }
    LIGHT_LOOP_END
#endif

    color += surfaceData.emission;
    return half4(color, surfaceData.alpha);
}

// LitPassFragment'ın aynısı, sadece ışıklandırma ToonFragment (toon kapalıysa doğrudan LitPassFragment)
void ToonLitPassFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    if (_VillageToon < 0.5)
    {
        LitPassFragment(input, outColor
#ifdef _WRITE_RENDERING_LAYERS
            , outRenderingLayers
#endif
        );
        return;
    }

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    SurfaceData surfaceData;
    InitializeStandardLitSurfaceData(input.uv, surfaceData);

#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));

#if defined(_DBUFFER)
    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
#endif

    InitializeBakedGIData(input, inputData);

    half3 geometryNormal = NormalizeNormalPerPixel(input.normalWS);
    half3 lightNormal = normalize(lerp(geometryNormal, inputData.normalWS, _VillageToonNormalDetail));
    half4 color = ToonFragment(inputData, surfaceData, lightNormal);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));

    outColor = color;

#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif
