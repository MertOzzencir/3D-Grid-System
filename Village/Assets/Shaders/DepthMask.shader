// Görünmez derinlik maskesi: renk çizmez, sadece derinlik yazar. Suyun (Transparent) hemen önünde çizilir; arkasında
// kalan su çizilmez. Örn. botun ağzındaki kapak: su botun içinden görünmesin. Botun içi daha önce (opaque) çizildiği
// için görünür kalır. Opaque sırasında değil: suyun okuduğu derinlik dokusuna (kıyı köpüğü) karışmasın.
Shader "Village/Depth Mask"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-1" "IgnoreProjector" = "True" }

        Pass
        {
            Name "DepthMask"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ColorMask 0
            ZWrite On
            ZTest LEqual
            Cull Off

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
    }
}
