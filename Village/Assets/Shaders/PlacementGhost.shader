// Build mode yerleştirme önizlemesi (PlacementGhost): objenin yarı saydam hayaleti. Düz renk (_Color; konabilir yerde
// yeşilimsi, konamaz yerde kırmızımsı, a = saydamlık), şekli okunsun diye ana ışığa göre hafif gölgeli, kenarları biraz
// daha belirgin (_RimStrength). Önce derinlik yazılır (kendi içindeki yüzler üst üste binip koyulaşmasın), sonra renk.
Shader "Village/Placement Ghost"
{
    Properties
    {
        _Color("Color", Color) = (0.4, 1, 0.5, 0.45)
        _RimStrength("Rim Strength", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        // Derinlik ön geçişi: sadece en öndeki yüz boyanır
        Pass
        {
            Name "GhostDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 Vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 Frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Ghost"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _RimStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 view = normalize(GetWorldSpaceViewDir(input.positionWS));
                half shade = 0.65 + 0.35 * saturate(dot(normal, GetMainLight().direction));
                half rim = pow(1.0 - saturate(dot(normal, view)), 3.0) * _RimStrength;
                return half4(_Color.rgb * shade + rim, saturate(_Color.a + rim));
            }
            ENDHLSL
        }
    }
}
