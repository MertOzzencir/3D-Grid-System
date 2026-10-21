// Sabah ışık huzmeleri (GodRays): toplamalı, yarı saydam şerit. UV.x şeridin enine (kenarlara doğru söner, _EdgePower),
// UV.y boyuna (0 güneş tarafı, 1 zemin; iki uçta da söner). Zemine / objelere değdiği yerde sert çizgi olmasın diye
// sahne derinliğine yaklaştıkça söner (_DepthFade birim). Renk = _Color × vertex rengi, saydamlık _Color.a × vertex a
// (her huzmenin kendi belirip kaybolması ve gün saatine göre gücü GodRays'ten vertex rengiyle gelir).
Shader "Village/God Ray"
{
    Properties
    {
        _Color("Color", Color) = (1, 0.9, 0.7, 0.35)
        _EdgePower("Edge Power", Float) = 1.5
        _DepthFade("Depth Fade", Float) = 1
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
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float4 screenPos : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float across = pow(sin(PI * saturate(input.uv.x)), _EdgePower);
                float along = smoothstep(0.0, 0.4, input.uv.y) * (1.0 - smoothstep(0.8, 1.0, input.uv.y));

                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float soft = saturate((sceneDepth - input.screenPos.w) / max(_DepthFade, 0.001));

                half alpha = _Color.a * input.color.a * across * along * soft;
                return half4(_Color.rgb * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
