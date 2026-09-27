// Stilize su (URP). Pastel renk geçişi, kıyı köpüğü, kayan yüzey lekeleri, kırılma, hafif dalga.
// Gerekli: URP Asset'te Depth Texture ve Opaque Texture açık olmalı (PC_RPAsset'te açık).
// Doku kullanmaz; tüm desenler dünya uzayında üretilir, yan yana konan su parçaları dikişsiz birleşir.
Shader "Village/Stylized Water"
{
    Properties
    {
        [Header(Renk)]
        _ShallowColor ("Sığ Renk", Color) = (0.56, 0.86, 0.82, 1)
        _DeepColor ("Derin Renk", Color) = (0.24, 0.57, 0.67, 1)
        _DepthDistance ("Derinlik Mesafesi", Float) = 1.2
        _ShallowClarity ("Sığ Yerde Şeffaflık", Range(0, 1)) = 0.45
        _ColorBands ("Renk Bantları (0 = yumuşak)", Range(0, 8)) = 0

        [Header(Kiyi Kopugu)]
        _FoamColor ("Köpük Rengi", Color) = (0.97, 0.99, 0.96, 1)
        _FoamDistance ("Köpük Genişliği", Float) = 0.3
        _FoamNoiseScale ("Köpük Desen Ölçeği", Float) = 4
        _FoamNoiseStrength ("Köpük Dalgalılığı", Range(0, 1)) = 0.6
        _FoamSpeed ("Köpük Hızı", Float) = 0.25
        _FoamSoftness ("Köpük Kenar Yumuşaklığı", Range(0.001, 0.3)) = 0.04

        [Header(Yuzey Lekeleri)]
        _RippleColor ("Leke Rengi (A = güç)", Color) = (1, 1, 1, 0.3)
        _RippleScale ("Leke Ölçeği", Float) = 1.3
        _RippleSpeed ("Leke Hızı", Float) = 0.12
        _RippleThreshold ("Leke Eşiği", Range(0, 1)) = 0.68
        _RippleSoftness ("Leke Yumuşaklığı", Range(0.001, 0.2)) = 0.03

        [Header(Kirilma ve Normal)]
        _NormalStrength ("Yüzey Pürüzü", Range(0, 2)) = 0.6
        _RefractionStrength ("Kırılma", Range(0, 0.1)) = 0.015

        [Header(Dalga)]
        _WaveHeight ("Dalga Yüksekliği", Float) = 0.03
        _WaveLength ("Dalga Boyu", Float) = 3
        _WaveSpeed ("Dalga Hızı", Float) = 0.8

        [Header(Isik)]
        _ShadowStrength ("Gölge Gücü", Range(0, 1)) = 0.35
        _SpecularColor ("Parıltı Rengi", Color) = (1, 1, 1, 1)
        _SpecularGloss ("Parıltı Keskinliği", Float) = 180
        _SpecularStrength ("Parıltı Gücü", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "StylizedWater"
            Tags { "LightMode" = "UniversalForward" }

            // Arkadaki sahneyi Opaque Texture'dan kendimiz karıştırıyoruz, o yüzden blend yok
            Blend Off
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _DepthDistance;
                half _ShallowClarity;
                half _ColorBands;

                half4 _FoamColor;
                float _FoamDistance;
                float _FoamNoiseScale;
                half _FoamNoiseStrength;
                float _FoamSpeed;
                half _FoamSoftness;

                half4 _RippleColor;
                float _RippleScale;
                float _RippleSpeed;
                half _RippleThreshold;
                half _RippleSoftness;

                half _NormalStrength;
                half _RefractionStrength;

                float _WaveHeight;
                float _WaveLength;
                float _WaveSpeed;

                half _ShadowStrength;
                half4 _SpecularColor;
                float _SpecularGloss;
                half _SpecularStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 waveNormalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ---------- Gürültü (doku yok, prosedürel) ----------
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Zıt yönlere kayan iki katman: desen tekrar etmez, "akıyormuş" gibi görünür
            float FlowNoise(float2 p, float2 flow)
            {
                return (ValueNoise(p + flow) + ValueNoise(p * 1.7 - flow * 0.8 + 17.3)) * 0.5;
            }

            // ---------- Dalga ----------
            // İki sinüs dalgası; yükseklik ve ondan türeyen normal
            float WaveHeight(float3 positionWS, out float3 normalWS)
            {
                float k = TWO_PI / max(_WaveLength, 0.01);
                float t = _Time.y * _WaveSpeed;
                float phaseX = positionWS.x * k + t;
                float phaseZ = positionWS.z * k * 0.8 + t * 1.3;

                float amplitude = _WaveHeight * 0.5;
                float dx = cos(phaseX) * k * amplitude;
                float dz = cos(phaseZ) * k * 0.8 * amplitude;
                normalWS = normalize(float3(-dx, 1.0, -dz));

                return (sin(phaseX) + sin(phaseZ)) * amplitude;
            }

            // ---------- Derinlik ----------
            // Su yüzeyi ile o pikselde altında görünen zemin arasındaki dikey mesafe
            float WaterDepth(float2 screenUV, float3 surfaceWS)
            {
                float rawDepth = SampleSceneDepth(screenUV);
                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
                #endif
                float3 sceneWS = ComputeWorldSpacePosition(screenUV, rawDepth, UNITY_MATRIX_I_VP);
                return surfaceWS.y - sceneWS.y;
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS.y += WaveHeight(positionWS, output.waveNormalWS);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float t = _Time.y;

                // 1) Yüzey normali: dalga + kayan desenin eğimi
                float2 rippleUV = input.positionWS.xz * _RippleScale;
                float2 flow = float2(1.0, 0.6) * _RippleSpeed * t;
                const float e = 0.1;
                float h  = FlowNoise(rippleUV, flow);
                float hx = FlowNoise(rippleUV + float2(e, 0), flow);
                float hz = FlowNoise(rippleUV + float2(0, e), flow);
                float3 normalWS = normalize(input.waveNormalWS + float3(h - hx, 0, h - hz) * _NormalStrength * 10.0);

                // 2) Derinlik ve kırılma: arkadaki sahneyi normale göre biraz kaydır
                float depth = max(0, WaterDepth(screenUV, input.positionWS));
                float2 refractedUV = screenUV + normalWS.xz * _RefractionStrength;
                if (WaterDepth(refractedUV, input.positionWS) < 0)
                    refractedUV = screenUV; // kayan nokta suyun önündeki bir objeye düştüyse kaydırma
                half3 sceneColor = SampleSceneColor(refractedUV);

                // 3) Derinliğe göre renk; istenirse bantlı (toon) geçiş
                float depth01 = saturate(depth / max(_DepthDistance, 0.001));
                if (_ColorBands >= 1)
                    depth01 = floor(depth01 * _ColorBands) / _ColorBands;
                half3 color = lerp(_ShallowColor.rgb, _DeepColor.rgb, depth01);

                // Sığ yerde alttaki zemin görünür
                color = lerp(color, sceneColor, _ShallowClarity * (1.0 - depth01));

                // 4) Kayan açık lekeler
                half ripple = smoothstep(_RippleThreshold - _RippleSoftness, _RippleThreshold + _RippleSoftness, h);
                color = lerp(color, _RippleColor.rgb, ripple * _RippleColor.a);

                // 5) Kıyı köpüğü: sığlık + dalgalı kenar için gürültü
                float shore = 1.0 - saturate(depth / max(_FoamDistance, 0.001));
                float foamNoise = FlowNoise(input.positionWS.xz * _FoamNoiseScale, float2(0.7, 1.0) * _FoamSpeed * t);
                float foamValue = shore + (foamNoise - 0.5) * _FoamNoiseStrength;
                half foam = smoothstep(0.5 - _FoamSoftness, 0.5 + _FoamSoftness, foamValue);
                color = lerp(color, _FoamColor.rgb, foam * _FoamColor.a);

                // 6) Gölge ve stilize güneş parıltısı
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                color *= lerp(1.0, mainLight.shadowAttenuation, _ShadowStrength);

                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 halfDirWS = normalize(mainLight.direction + viewDirWS);
                float spec = pow(saturate(dot(normalWS, halfDirWS)), _SpecularGloss);
                spec = smoothstep(0.45, 0.55, spec) * _SpecularStrength * mainLight.shadowAttenuation * (1.0 - foam);
                color += _SpecularColor.rgb * spec;

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
