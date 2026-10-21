// Toon (cel) ışıklandırma denemesi. Lit kopyalarının (Village/Lit, Wood Wobble Lit, Tree Chop Lit, Creature Jiggle Lit)
// ForwardLit fragment'ı LitPassFragment yerine ToonLitPassFragment'ı çağırır:
//   _VillageToon = 0 (ToonStyle kapalı ya da sahnede yok) → birebir URP'nin LitPassFragment'ı (eski görünüm)
//   _VillageToon = 1 → yüzey aynı (doku, renk, normal map), ışık sert geçişli: ışık eşikte birden gölgeye döner
//   (_VillageToonThreshold, geçiş genişliği _VillageToonSoftness), gölgede albedo × _VillageToonShadowColor, parlama
//   keskin kenarlı leke (_VillageToonSpecSize, _VillageToonSpecStrength × smoothness), ortam ışığı (SH) payı
//   _VillageToonAmbient. Gerçek zamanlı gölgeler de aynı eşikle sertleşir. SSAO toon'da kullanılmaz (yumuşak leke yapar).
// Ayarların hepsi global (materyal başına değil): ToonStyle bileşeni yazar.
#ifndef VILLAGE_TOON_INCLUDED
#define VILLAGE_TOON_INCLUDED

float _VillageToon;
float _VillageToonThreshold;
float _VillageToonSoftness;
half4 _VillageToonShadowColor;
float _VillageToonSpecSize;
float _VillageToonSpecStrength;
float _VillageToonAmbient;

half ToonStep(half value, half threshold)
{
    half soft = max(_VillageToonSoftness, 0.001);
    return smoothstep(threshold - soft, threshold + soft, value);
}

// Bir ışığın aydınlık payı (0 gölge, 1 aydınlık): yüzeyin ışığa bakması ve gerçek zamanlı gölge, ikisi de sert
half ToonLit(Light light, InputData inputData)
{
    half facing = ToonStep(dot(inputData.normalWS, light.direction), _VillageToonThreshold);
    half shadow = ToonStep(light.shadowAttenuation, 0.5);
    return facing * shadow;
}

half3 ToonSpecular(Light light, InputData inputData, SurfaceData surfaceData, half lit)
{
    half3 halfDir = SafeNormalize(light.direction + inputData.viewDirectionWS);
    half highlight = ToonStep(dot(inputData.normalWS, halfDir), 1.0 - _VillageToonSpecSize);
    return light.color * (highlight * lit * _VillageToonSpecStrength * surfaceData.smoothness);
}

half4 ToonFragment(InputData inputData, SurfaceData surfaceData)
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
        half lit = ToonLit(mainLight, inputData);
        half3 light = mainLight.color * mainLight.distanceAttenuation;
        color += albedo * light * lerp(_VillageToonShadowColor.rgb, 1.0, lit);
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
            half lit = ToonLit(light, inputData);
            color += albedo * light.color * light.distanceAttenuation * lit;
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
            half lit = ToonLit(light, inputData);
            color += albedo * light.color * light.distanceAttenuation * lit;
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

    half4 color = ToonFragment(inputData, surfaceData);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));

    outColor = color;

#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif
