// Sabah ışık huzmeleri (GodRays): toplamalı, çok hafif, yumuşak şerit. UV.x şeridin enine (kenarlara doğru söner,
// _EdgePower), UV.y boyuna (0 güneş tarafı, 1 zemin; iki uçta da söner). İçinde yavaşça aşağı süzülen toz çizgileri
// (değer gürültüsü, _DustAmount / _DustScale / _DustSpeed; UV2.x her düzlemin tohumu, hepsi aynı görünmesin).
// Zemine / objelere değdiği yerde sert çizgi olmasın diye sahne derinliğine yaklaştıkça söner (_DepthFade birim).
// Renk = _Color × vertex rengi, saydamlık _Color.a × vertex a (gün saatine göre güç, nefes, düzlemin kameraya bakması
// GodRays'ten vertex rengiyle gelir).
Shader "Village/God Ray"
{
    Properties
    {
        _Color("Color", Color) = (1, 0.92, 0.75, 0.12)
        _EdgePower("Edge Power", Float) = 1.5
        _DepthFade("Depth Fade", Float) = 1.5
        _DustAmount("Dust Amount", Range(0, 1)) = 0.5
        _DustScale("Dust Scale (across, along)", Vector) = (6, 2, 0, 0)
        _DustSpeed("Dust Speed", Float) = 0.04
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "GodRay"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _EdgePower;
                float _DepthFade;
                float _DustAmount;
                float4 _DustScale;
                float _DustSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 seed : TEXCOORD1;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 uv : TEXCOORD0; // xy uv, z tohum
                half4 color : COLOR;
                float4 screenPos : TEXCOORD1;
            };

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash(i), b = Hash(i + float2(1, 0)), c = Hash(i + float2(0, 1)), d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = float3(input.uv, input.seed.x);
                output.color = input.color;
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv.xy;
                float across = pow(sin(PI * saturate(uv.x)), _EdgePower);
                float along = smoothstep(0.0, 0.45, uv.y) * (1.0 - smoothstep(0.75, 1.0, uv.y));

                // Toz çizgileri: enine sık, boyuna seyrek gürültü, zamanla aşağı (güneşten zemine) süzülür
                float2 dustUV = float2(uv.x * _DustScale.x + input.uv.z * 17.0, uv.y * _DustScale.y - _Time.y * _DustSpeed);
                float dust = lerp(1.0 - _DustAmount, 1.0, ValueNoise(dustUV));

                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float soft = saturate((sceneDepth - input.screenPos.w) / max(_DepthFade, 0.001));

                half alpha = _Color.a * input.color.a * across * along * dust * soft;
                return half4(_Color.rgb * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
