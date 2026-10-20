// Tarla grid göstergesi (URP, dokusuz, saydam): hücre kenarlarından yükselen ince duvarlar ve tabandaki çizgiler.
// Saydamlık yüksekliğe göre: UV.y = 0 tabanda en belirgin (_Color.a), UV.y = 1 tepede tamamen saydam; _Falloff geçişin
// eğrisi (1 doğrusal, büyük = tabana daha yakın söner). Vertex rengi de çarpılır (A: hücre başına saydamlık, örn. elde
// tutulan odunun çevresinde uzaklaştıkça söner; vertex rengi olmayan mesh'te beyaz). FarmGridIndicator ve
// HeldGridIndicator mesh'leri kurar; shader botun prefab'ında referansla tutulur (build'e girsin).
Shader "Village/Grid Fade"
{
    Properties
    {
        _Color ("Renk (A = tabandaki saydamlık)", Color) = (1, 1, 1, 0.6)
        _Falloff ("Yukarı Doğru Sönme Eğrisi", Range(0.2, 6)) = 1.6
    }

    SubShader
    {
        // Suyun (Transparent) üstüne çizilsin
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+20" "IgnoreProjector" = "True" }

        Pass
        {
            Name "GridFade"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Falloff;
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
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half fade = pow(saturate(1.0 - input.uv.y), _Falloff);
                return half4(_Color.rgb * input.color.rgb, _Color.a * input.color.a * fade);
            }
            ENDHLSL
        }
    }
}
