// Toon denemesinin kontur çizgileri: ekran üzerinden (URP "Full Screen Pass Renderer Feature" ile çizilir; Requirements:
// Depth + Normal, Fetch Color Buffer açık, Injection Point: Before Rendering Transparents). Komşu piksellerin derinlik
// farkı (objenin dış hattı) ya da normal farkı (keskin köşe) eşiği geçince piksel o yerin renginin koyusu olur
// (renk × _VillageOutlineColor; siyah değil, Arc System Works'teki gibi renkli çizgi). Toon kapalıyken
// (_VillageToon = 0, ToonStyle) görüntüye dokunmaz. Ayarlar global (ToonStyle yazar).
Shader "Village/Toon Outline"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "ToonOutline"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float _VillageToon;
            float _VillageOutlineWidth;   // piksel (1080p'de; ekran yüksekliğiyle ölçeklenir)
            float _VillageOutlineFadeDistance; // bu uzaklıkta (birim) çizgi yarı kalınlıkta
            float _VillageOutlineDepth;   // derinlik farkı eşiği (kameraya uzaklığa oranla)
            float _VillageOutlineNormal;  // normal farkı eşiği (0-2)
            half4 _VillageOutlineColor;   // rgb: çizgi = renk × bu, a: güç

            float EyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
                if (_VillageToon < 0.5 || _VillageOutlineColor.a <= 0.0) return color;

                // Kalınlık her çözünürlükte aynı görünsün (1080p'ye göre), uzaktakiler incelsin
                float center = EyeDepth(uv);
                float width = _VillageOutlineWidth * (_ScreenParams.y / 1080.0);
                width *= lerp(1.0, 0.5, saturate(center / max(_VillageOutlineFadeDistance, 0.001)));

                // Çapraz komşular (Roberts cross)
                float2 texel = max(width, 0.5) / _ScreenParams.xy;
                float2 a = uv + float2(-1.0, -1.0) * texel, b = uv + float2(1.0, 1.0) * texel;
                float2 c = uv + float2(1.0, -1.0) * texel, d = uv + float2(-1.0, 1.0) * texel;

                float depthDifference = (abs(EyeDepth(a) - EyeDepth(b)) + abs(EyeDepth(c) - EyeDepth(d))) / max(center, 0.001);

                float3 normal = SampleSceneNormals(uv);
                float normalDifference = length(SampleSceneNormals(a) - SampleSceneNormals(b)) +
                                         length(SampleSceneNormals(c) - SampleSceneNormals(d));

                // Kameraya yatık yüzeylerde (uzaktan görülen zemin) derinlik doğal olarak hızlı değişir: eşik orada büyür
                float3 forward = -UNITY_MATRIX_V[2].xyz;
                float facing = abs(dot(normal, forward));
                float depthThreshold = _VillageOutlineDepth * (1.0 + 4.0 * (1.0 - facing));

                float edge = max(step(depthThreshold, depthDifference), step(_VillageOutlineNormal, normalDifference));
                color.rgb = lerp(color.rgb, color.rgb * _VillageOutlineColor.rgb, edge * _VillageOutlineColor.a);
                return color;
            }
            ENDHLSL
        }
    }
}
