// Kedi gözü (URP, dokusuz). İki mod:
//   Küre göz (varsayılan): göz, kürenin kameraya bakan yüzüne görüş uzayında (normalden) çizilir; UV gerekmez.
//     Kürenin görünen yüzünün tamamı göz; kapak inerken üstü şeffaflaşır, arkadaki kafa kapak gibi görünür.
//   Yama: göz yamasının UV'sine (0..1, ortası göz) çizilir, dışı şeffaf.
//   koyu kahve, hafif dikey oval göz (tepeye doğru koyulaşır) + büyük ve küçük iki beyaz parlama
//   ifadeler (CatEyes.cs MaterialPropertyBlock ile sürer): _Open (kapak/kırpma, kapalıyken ‿ çizgisi),
//   _Happy (^ ^), _Angry (kapak içe eğik iner), _Surprise (göz büyür), _Look (göz kayar, parlamalar yarı hızla)
// _Side: sağ/sol göz UV'si aynalıysa -1; parlamalar ve kızgın kapak her iki gözde aynı ekran yönüne bakar.
Shader "Village/Cat Eyes"
{
    Properties
    {
        [Header(Renk)]
        _EyeColor ("Göz Rengi", Color) = (0.22, 0.12, 0.07, 1)
        _EyeColorTop ("Göz Rengi (tepe)", Color) = (0.09, 0.05, 0.03, 1)
        _HighlightColor ("Parlama Rengi", Color) = (1, 1, 1, 1)
        _LineColor ("Çizgi Rengi (kapalı/mutlu)", Color) = (0.12, 0.07, 0.04, 1)

        [Header(Sekil)]
        _EyeSize ("Göz Yarıçapı (UV)", Vector) = (0.3, 0.38, 0, 0)
        _HighlightBig ("Büyük Parlama (x, y, yarıçap)", Vector) = (-0.08, 0.13, 0.09, 0)
        _HighlightSmall ("Küçük Parlama (x, y, yarıçap)", Vector) = (0.09, -0.13, 0.045, 0)
        _LineWidth ("Çizgi Kalınlığı", Range(0.01, 0.15)) = 0.045
        _LookRange ("Bakış Kayma Miktarı", Range(0, 0.3)) = 0.09

        [Header(Ifade CatEyes surer)]
        _Open ("Açıklık", Range(0, 1)) = 1
        _Happy ("Mutlu", Range(0, 1)) = 0
        _Angry ("Kızgın", Range(0, 1)) = 0
        _Surprise ("Şaşkın", Range(0, 1)) = 0
        _Look ("Bakış (xy)", Vector) = (0, 0, 0, 0)
        _Side ("Taraf (1 / -1)", Float) = 1
        [Toggle] _SphereEyes ("Küre Göz (görüş uzayı, UV gerekmez)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "CatEyes"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _EyeColor, _EyeColorTop, _HighlightColor, _LineColor;
                float4 _EyeSize, _HighlightBig, _HighlightSmall;
                float _LineWidth, _LookRange;
                float _Open, _Happy, _Angry, _Surprise, _Side, _SphereEyes;
                float4 _Look;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fogFactor : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // İşaretli mesafeden yumuşak kenarlı maske (kenar piksel boyunda)
            float Fill(float distance)
            {
                float w = max(fwidth(distance), 0.0001);
                return 1.0 - smoothstep(-w, w, distance);
            }

            // Yay (çember parçası) çizgisi: merkez, yarıçap; upper = 1 üst yarı (^), 0 alt yarı (‿)
            float Arc(float2 p, float2 center, float radius, float upper)
            {
                float d = abs(length(p - center) - radius) - _LineWidth * 0.5;
                float half_ = upper > 0.5 ? step(center.y, p.y) : step(p.y, center.y);
                return Fill(d) * half_;
            }

            half4 frag(Varyings input) : SV_Target
            {
                bool sphere = _SphereEyes > 0.5;
                float2 look = clamp(_Look.xy, -1.0, 1.0) * _LookRange;
                float2 p, size, c;
                if (sphere)
                {
                    // Kürenin görünen diski: normalin kameraya göre xy'si (-1..1) → -0.5..0.5. Göz diskin tamamı;
                    // bakış gözü kaydıramaz (kenarda boşluk açılırdı), sadece parlamaları hafifçe kaydırır.
                    p = TransformWorldToViewDir(normalize(input.normalWS)).xy * 0.5;
                    size = float2(0.52, 0.52); // kenar yumuşatması kürenin silüetinde şeffaf halka bırakmasın
                    c = 0.0;
                }
                else
                {
                    p = input.uv - 0.5;
                    size = _EyeSize.xy * (1.0 + _Surprise * 0.15);
                    c = look;
                }

                // Göz: oval (yaklaşık işaretli mesafe)
                float2 q = (p - c) / size;
                float eyeD = (length(q) - 1.0) * min(size.x, size.y);

                // Kapak: yukarıdan iner (açıklık), kızgınken içe doğru eğik iner
                float top = c.y + size.y;
                float lidY = c.y - size.y + 2.0 * size.y * _Open;
                // Kızgın: kapak iner ve bir kenara doğru daha çok iner (çatık kaş gibi eğik)
                lidY -= _Angry * size.y * (0.35 + 0.25 * (p.x * _Side) / size.x);
                lidY = min(lidY, top);
                float lidD = p.y - lidY;
                float eye = Fill(max(eyeD, lidD));

                // Renk: tepeye doğru koyulaşır
                float yn = saturate((p.y - c.y) / size.y * 0.5 + 0.5);
                half3 color = lerp(_EyeColor.rgb, _EyeColorTop.rgb, smoothstep(0.35, 1.0, yn));

                // Parlamalar göze göre yarı hızla kayar (ıslak göz hissi), kapak kapanınca kaybolur
                // Kürede parlamalar kameraya göre sabit (iki gözde aynı yönde), bakışla çok az kayar
                float2 hc = sphere ? look * 0.25 : look * 0.5;
                float highlightSide = sphere ? 1.0 : _Side;
                float2 big = hc + float2(_HighlightBig.x * highlightSide, _HighlightBig.y);
                float2 small = hc + float2(_HighlightSmall.x * highlightSide, _HighlightSmall.y);
                float shine = max(Fill(length(p - big) - _HighlightBig.z), Fill(length(p - small) - _HighlightSmall.z));
                shine *= Fill(lidD + 0.02) * smoothstep(0.35, 0.6, _Open);
                color = lerp(color, _HighlightColor.rgb, shine);

                // Çizgiler: mutlu (^) ve kapalı (‿)
                float happyLine = Arc(p, float2(0.0, -0.12), 0.22, 1.0) * _Happy;
                float closedLine = Arc(p, float2(0.0, 0.1), 0.22, 0.0) * (1.0 - smoothstep(0.05, 0.25, _Open)) * (1.0 - _Happy);
                float line_ = max(happyLine, closedLine);

                float eyeAlpha = eye * (1.0 - _Happy) * smoothstep(0.02, 0.15, _Open);
                float alpha = max(eyeAlpha, line_);
                half3 finalColor = lerp(color, _LineColor.rgb, saturate(line_ / max(alpha, 0.0001)));
                finalColor = MixFog(finalColor, input.fogFactor);
                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
