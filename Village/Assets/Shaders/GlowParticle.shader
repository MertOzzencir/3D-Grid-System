// Parlayan yumuşak nokta (URP, dokusuz, toplamalı): polen, toz zerresi, ateş böceği. Renk vertex renginden (particle'ın
// rengi ve saydamlığı), _Intensity ile HDR'a çıkar ki bloom yakalasın (ateş böceği). Şekil UV'den: ortası dolu, kenarı
// yumuşak disk. AmbientParticles.cs malzemeyi bundan kurar; shader sahnede referansla tutulur (build'e girsin).
Shader "Village/Glow Particle"
{
    Properties
    {
        _Intensity ("Parlaklık (1'in üstü bloom'a girer)", Float) = 1
        _Softness ("Kenar Yumuşaklığı", Range(0.05, 1)) = 0.7
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+20" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _Softness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float fog : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float d = length(input.uv - 0.5) * 2.0;
                float mask = 1.0 - smoothstep(1.0 - _Softness, 1.0, d);
                // Sis: toplamalı karışımda rengi sise doğru değil sıfıra doğru söndür (uzaktakiler kaybolsun)
                float fade = saturate(input.fog);
                #if !defined(FOG_LINEAR) && !defined(FOG_EXP) && !defined(FOG_EXP2)
                    fade = 1.0;
                #endif
                return half4(input.color.rgb * _Intensity, input.color.a * mask * fade);
            }
            ENDHLSL
        }
    }
}
