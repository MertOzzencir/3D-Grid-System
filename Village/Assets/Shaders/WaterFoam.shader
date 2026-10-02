// Su üstü köpük / sıçrama (URP, dokusuz, saydam). Şekil UV'den çizilir, renk ve saydamlık vertex renginden
// (particle'ın / trail'in renk geçişi) gelir. Köpük, dünya uzayında gürültüyle kırık kırık görünür.
//   _Shape 0: yumuşak disk (damla)   1: şerit, kenarları yumuşak (köpük izi, TrailRenderer)   2: halka (sudaki dalga halkası)
// BoatWakeFx.cs malzemeleri bu shader'dan kurar; shader prefab'da referansla tutulur (build'e girsin).
Shader "Village/Water Foam"
{
    Properties
    {
        _Shape ("Şekil (0 disk, 1 şerit, 2 halka)", Float) = 0
        _Softness ("Kenar Yumuşaklığı", Range(0.01, 1)) = 0.45
        _RingWidth ("Halka Kalınlığı", Range(0.05, 0.5)) = 0.18
        _Breakup ("Köpük Kırıklığı", Range(0, 1)) = 0.5
        _NoiseScale ("Gürültü Ölçeği (dünya)", Float) = 4
    }

    SubShader
    {
        // Suyun (Transparent) üstüne çizilsin
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Foam"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Shape;
                float _Softness;
                float _RingWidth;
                float _Breakup;
                float _NoiseScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            float Hash(float2 p)
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
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float mask;
                if (_Shape < 0.5)
                {
                    float d = length(input.uv - 0.5) * 2.0;
                    mask = 1.0 - smoothstep(1.0 - _Softness, 1.0, d);
                }
                else if (_Shape < 1.5)
                {
                    float d = abs(input.uv.y - 0.5) * 2.0;
                    mask = 1.0 - smoothstep(1.0 - _Softness, 1.0, d);
                }
                else
                {
                    float d = length(input.uv - 0.5) * 2.0;
                    float ring = abs(d - (1.0 - _RingWidth)) / _RingWidth;
                    mask = (1.0 - smoothstep(1.0 - _Softness, 1.0, ring)) * step(d, 1.0);
                }

                // Kırık köpük: iki katman gürültü (biri yavaşça kayar), saydamlığı lekeli yapar
                float2 p = input.positionWS.xz * _NoiseScale;
                float n = ValueNoise(p + _Time.y * 0.4) * 0.6 + ValueNoise(p * 2.3 - _Time.y * 0.2) * 0.4;
                float breakup = lerp(1.0, smoothstep(0.25, 0.65, n), _Breakup);

                return half4(input.color.rgb, saturate(mask * breakup * input.color.a));
            }
            ENDHLSL
        }
    }
}
