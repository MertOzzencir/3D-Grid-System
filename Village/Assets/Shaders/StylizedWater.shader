// Stilize su (URP): yüzey Voronoi tepeciklerle dolu; her tepeciğin güneşe bakan yanı aydınlanır ve parlar.
//   1) Dalgacıklar: iki katman hareketli Voronoi. Her hücrenin noktası bir tepecik (h = 1 - uzaklık²),
//      hücre sınırları çukur. Eğim analitik (2 × noktaya vektör): ucuz ve pürüzsüz normal.
//   2) Tepecik ışığı: güneşe bakan yamaç açık, arka yamaç koyu ton; üstüne güneş yansıması (yarı sert) ve gökyüzü (fresnel)
//   3) Hafif şeffaf: derinliğe göre opaklık, alttaki sahne tepecik normaline göre kırılarak görünür
//   4) Kıyı köpüğü: temasta ince çizgi + dışa doğru parça parça sönen bant
//   5) Hafif Gerstner-benzeri dalga (vertex; sık mesh için WaterSurface.cs)
// Gerekli: URP Asset'te Depth Texture ve Opaque Texture açık olmalı (PC_RPAsset'te açık).
// Doku kullanmaz; tüm desenler dünya uzayında üretilir, yan yana konan su parçaları dikişsiz birleşir.
Shader "Village/Stylized Water"
{
    Properties
    {
        [Header(Renk ve Seffaflik)]
        _ShallowColor ("Sığ Renk (A = opaklık)", Color) = (0.25, 0.88, 0.84, 0.55)
        _DeepColor ("Derin Renk (A = opaklık)", Color) = (0.02, 0.55, 0.7, 0.93)
        _DepthDistance ("Derinlik Mesafesi", Float) = 2.5
        _HorizonColor ("Uzak Renk (A = güç)", Color) = (0.03, 0.42, 0.62, 0.4)
        _HorizonPower ("Uzak Renk Geçişi", Range(0.5, 8)) = 3
        _RefractionStrength ("Kırılma", Range(0, 0.1)) = 0.035

        [Header(Renk Bolgeleri)]
        _PatchLightColor ("Açık Bölge Rengi (A = güç)", Color) = (0.2, 0.85, 0.82, 0.45)
        _PatchDarkColor ("Koyu Bölge Rengi (A = güç)", Color) = (0.02, 0.38, 0.62, 0.4)
        _PatchScale ("Bölge Boyu (birim)", Float) = 16
        _PatchSpeed ("Bölge Kayma Hızı", Float) = 0.15
        _PatchContrast ("Bölge Kenar Keskinliği", Range(0.5, 4)) = 1.6

        [Header(Dalgaciklar Voronoi)]
        _RippleScale ("Tepecik Sıklığı (birim başına)", Float) = 3.5
        _RippleHeight ("Tepecik Yüksekliği (eğim)", Range(0, 2)) = 0.4
        _RippleSecondLayer ("İkinci Katman Gücü", Range(0, 1)) = 0.4
        _RippleSpeed ("Kayma Hızı", Float) = 0.15
        _RippleMorph ("Kıpırdanma Hızı", Float) = 0.8
        _RippleDirection ("Kayma Yönü (derece)", Range(0, 360)) = 30

        [Header(Tepecik Isigi)]
        _LitColor ("Güneşe Bakan Yamaç (A = güç)", Color) = (0.55, 0.95, 0.92, 0.35)
        _ShadeColor ("Arka Yamaç (A = güç)", Color) = (0.0, 0.4, 0.55, 0.3)
        _SlopeContrast ("Yamaç Kontrastı", Range(0.5, 10)) = 2.5
        _CrestColor ("Tepecik Tepesi (A = güç)", Color) = (0.7, 1, 0.97, 0.15)

        [Header(Gunes Yansimasi)]
        _SpecularColor ("Yansıma Rengi", Color) = (1, 1, 0.95, 1)
        _SpecularPower ("Yansıma Keskinliği", Range(8, 1024)) = 200
        _SpecularHardness ("Yansıma Sertliği", Range(0, 1)) = 0.6
        _SpecularStrength ("Yansıma Gücü", Range(0, 3)) = 1.5
        _ReflectionStrength ("Gökyüzü Yansıması", Range(0, 1)) = 0.25
        _ShadowStrength ("Gölge Gücü", Range(0, 1)) = 0.35

        [Header(Kiyi Kopugu)]
        _FoamColor ("Köpük Rengi (A = güç)", Color) = (1, 1, 1, 1)
        _FoamWidth ("Köpük Bandı Genişliği (derinlik)", Float) = 0.4
        _FoamLineWidth ("Temas Çizgisi Kalınlığı", Range(0, 1)) = 0.2
        _FoamBreakup ("Bandın Dağınıklığı", Range(0, 1)) = 0.5
        _FoamNoiseScale ("Köpük Desen Ölçeği", Float) = 3
        _FoamSpeed ("Köpük Hızı", Float) = 0.3
        _FoamSoftness ("Köpük Kenar Yumuşaklığı", Range(0.001, 0.5)) = 0.06

        [Header(Kiyiya Gelen Dalgalar)]
        _ShoreWaveColor ("Dalga Çizgisi Rengi (A = güç)", Color) = (1, 1, 1, 0.9)
        _ShoreWaveDistance ("Başladığı Derinlik", Float) = 1.2
        _ShoreWaveCount ("Aynı Anda Çizgi Sayısı", Range(0, 6)) = 2
        _ShoreWaveSpeed ("Geliş Hızı", Float) = 0.25
        _ShoreWaveWidth ("Çizgi Kalınlığı", Range(0.01, 0.6)) = 0.18
        _ShoreWaveBreakup ("Çizgi Dağınıklığı", Range(0, 1)) = 0.45

        [Header(Dalga)]
        _WaveHeight ("Dalga Yüksekliği", Float) = 0.06
        _WaveLength ("Dalga Boyu", Float) = 6
        _WaveSpeed ("Dalga Hızı", Float) = 0.5
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
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
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
                half4 _HorizonColor;
                half _HorizonPower;
                half _RefractionStrength;

                half4 _PatchLightColor;
                half4 _PatchDarkColor;
                float _PatchScale;
                float _PatchSpeed;
                half _PatchContrast;

                float _RippleScale;
                half _RippleHeight;
                half _RippleSecondLayer;
                float _RippleSpeed;
                float _RippleMorph;
                float _RippleDirection;

                half4 _LitColor;
                half4 _ShadeColor;
                half _SlopeContrast;
                half4 _CrestColor;

                half4 _SpecularColor;
                float _SpecularPower;
                half _SpecularHardness;
                half _SpecularStrength;
                half _ReflectionStrength;
                half _ShadowStrength;

                half4 _FoamColor;
                float _FoamWidth;
                half _FoamLineWidth;
                half _FoamBreakup;
                float _FoamNoiseScale;
                float _FoamSpeed;
                half _FoamSoftness;

                half4 _ShoreWaveColor;
                float _ShoreWaveDistance;
                half _ShoreWaveCount;
                float _ShoreWaveSpeed;
                half _ShoreWaveWidth;
                half _ShoreWaveBreakup;

                float _WaveHeight;
                float _WaveLength;
                float _WaveSpeed;
            CBUFFER_END

            // Botların içi (WaterHullClip.cs gönderir, global): su noktası bir botun gövde profilinin içindeyse çizilmez.
            // Sabitler WaterHullClip.MaxBoats / Levels / Sectors ile aynı olmalı.
            #define BOAT_MAX 4
            #define BOAT_LEVELS 6
            #define BOAT_SECTORS 32
            float4x4 _BoatWorldToLocal[BOAT_MAX];
            float _BoatHullRadii[BOAT_MAX * BOAT_LEVELS * BOAT_SECTORS];
            float4 _BoatHullHeights[BOAT_MAX]; // x: en alt katman yüksekliği, y: katman aralığı (botun uzayında)
            float _BoatCount;

            // Katman: noktanın altındaki (gövde yukarı doğru genişlediği için küçük olan, güvenli taraf).
            // Dilim: noktanınki ve komşusunun küçüğü (yine güvenli taraf).
            bool InsideBoat(float3 positionWS)
            {
                for (int b = 0; b < BOAT_MAX; b++)
                {
                    if (b >= (int)_BoatCount) break;
                    float3 p = mul(_BoatWorldToLocal[b], float4(positionWS, 1.0)).xyz;
                    float level = clamp(floor((p.y - _BoatHullHeights[b].x) / max(_BoatHullHeights[b].y, 1e-4)), 0.0, BOAT_LEVELS - 1.0);
                    float a = frac(atan2(p.z, p.x) / (2.0 * PI)) * BOAT_SECTORS;
                    int s0 = min((int)floor(a), BOAT_SECTORS - 1);
                    int s1 = (s0 + 1) % BOAT_SECTORS;
                    int baseIndex = (b * BOAT_LEVELS + (int)level) * BOAT_SECTORS;
                    float radius = min(_BoatHullRadii[baseIndex + s0], _BoatHullRadii[baseIndex + s1]);
                    if (length(p.xz) < radius) return true;
                }
                return false;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ---------- Yardımcılar ----------
            float2 Hash22(float2 p)
            {
                float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                q += dot(q, q.yzx + 33.33);
                return frac((q.xx + q.yz) * q.zy);
            }

            // Yumuşak gürültü, yaklaşık -0.7..0.7 (sadece köpük kenarı için)
            float GradientNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = dot(Hash22(i) * 2.0 - 1.0, f);
                float b = dot(Hash22(i + float2(1, 0)) * 2.0 - 1.0, f - float2(1, 0));
                float c = dot(Hash22(i + float2(0, 1)) * 2.0 - 1.0, f - float2(0, 1));
                float d = dot(Hash22(i + float2(1, 1)) * 2.0 - 1.0, f - float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float2 AngleToDirection(float degrees)
            {
                float r = radians(degrees);
                return float2(cos(r), sin(r));
            }

            // ---------- Voronoi tepecikler ----------
            // Her hücrede yerinde gezinen bir nokta; r = piksel → nokta vektörü, d = |r|².
            // Yumuşak Voronoi: en yakın mesafe yerine mesafelerin yumuşak minimumu s = -log(Σ e^(-k·d)) / k.
            // Böylece hücre sınırları keskin kırışık değil yuvarlak çukur olur (kristal gibi görünmez).
            // Tepecik yüksekliği h = 1 - s; eğimi analitik: dh/duv = Σ w·2r / Σ w, w = e^(-k·d).
            // Dönen: x = yükseklik (0..1), yz = eğim (uv uzayında)
            float3 VoronoiBumps(float2 uv, float t)
            {
                const float k = 7.0; // büyüdükçe sınırlar keskinleşir
                float2 i = floor(uv);
                float2 f = frac(uv);
                float weightSum = 0;
                float2 slopeSum = 0;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y);
                    float2 o = 0.5 + 0.42 * sin(t + TWO_PI * Hash22(i + g));
                    float2 r = g + o - f;
                    float w = exp(-k * dot(r, r));
                    weightSum += w;
                    slopeSum += w * 2.0 * r;
                }
                weightSum = max(weightSum, 1e-6);
                float s = -log(weightSum) / k;
                return float3(saturate(1.0 - s), slopeSum / weightSum);
            }

            // ---------- Dalga ----------
            // İki çapraz sinüs; yüzey hafifçe kabarıp insin diye
            float WaveOffset(float2 xz, out float2 gradient)
            {
                float k = TWO_PI / max(_WaveLength, 0.01);
                float t = _Time.y * _WaveSpeed;
                float p1 = dot(xz, float2(0.8, 0.6)) * k + t;
                float p2 = dot(xz, float2(-0.5, 0.87)) * k * 1.37 + t * 1.2;
                gradient = float2(0.8, 0.6) * cos(p1) * k * _WaveHeight
                         + float2(-0.5, 0.87) * cos(p2) * k * 1.37 * _WaveHeight * 0.6;
                return (sin(p1) + sin(p2) * 0.6) * _WaveHeight;
            }

            // ---------- Derinlik ----------
            // Su yüzeyi ile o pikselde altında görünen zemin arasındaki dikey mesafe (kameradan bağımsız)
            float WaterDepth(float2 screenUV, float surfaceY)
            {
                float rawDepth = SampleSceneDepth(screenUV);
                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
                #endif
                float3 sceneWS = ComputeWorldSpacePosition(screenUV, rawDepth, UNITY_MATRIX_I_VP);
                return surfaceY - sceneWS.y;
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float2 gradient;
                positionWS.y += WaveOffset(positionWS.xz, gradient);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (_BoatCount > 0.5 && InsideBoat(input.positionWS)) discard;

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float2 xz = input.positionWS.xz;
                float t = _Time.y;
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));

                // 1) Tepecikler: iki katman, farklı sıklık/yön; ikincisi biraz döndürülmüş ki hücreler hizalanmasın
                float2 flow = AngleToDirection(_RippleDirection) * _RippleSpeed * t;
                float2 uv1 = xz * _RippleScale + flow;
                float2 uv2 = float2(xz.x * 0.8 - xz.y * 0.6, xz.x * 0.6 + xz.y * 0.8) * _RippleScale * 1.9 - flow * 1.3 + 17.0;
                float3 bumps1 = VoronoiBumps(uv1, t * _RippleMorph);
                float3 bumps2 = VoronoiBumps(uv2, t * _RippleMorph * 1.3 + 5.0);
                float2 slope2 = float2(bumps2.y * 0.8 + bumps2.z * 0.6, -bumps2.y * 0.6 + bumps2.z * 0.8); // uv2 dönüşünü geri al

                // Uzakta hücreler pikselden küçülünce titreşmesin diye tepecikler yumuşakça düzleşir
                float cellsPerPixel = max(fwidth(uv1.x), fwidth(uv1.y));
                half detailFade = 1.0 - smoothstep(0.15, 0.6, cellsPerPixel);
                half detailFade2 = 1.0 - smoothstep(0.15, 0.6, cellsPerPixel * 1.9);

                float2 slope = (bumps1.yz * detailFade + slope2 * _RippleSecondLayer * detailFade2) * _RippleHeight * 0.5;
                float crestHeight = bumps1.x * detailFade;

                float2 waveGradient;
                WaveOffset(xz, waveGradient);
                // Tepecik eğimi "piksel → nokta" yönünde yukarı çıkar; normal eğimin tersine yatar
                float3 normalWS = normalize(float3(-waveGradient.x - slope.x, 1.0, -waveGradient.y - slope.y));

                // 2) Kırılma + hafif şeffaflık: kayan nokta suyun önündeki bir objeye düştüyse kaydırma yok
                float2 refractedUV = screenUV + normalWS.xz * _RefractionStrength;
                float depth = WaterDepth(refractedUV, input.positionWS.y);
                if (depth < 0)
                {
                    refractedUV = screenUV;
                    depth = WaterDepth(screenUV, input.positionWS.y);
                }
                depth = max(depth, 0);
                half3 sceneColor = SampleSceneColor(refractedUV);

                float depth01 = saturate(depth / max(_DepthDistance, 0.001));
                half4 water = lerp(_ShallowColor, _DeepColor, depth01);
                half horizon = pow(1.0 - saturate(dot(viewDirWS, float3(0, 1, 0))), _HorizonPower);
                water.rgb = lerp(water.rgb, _HorizonColor.rgb, horizon * _HorizonColor.a);

                // Geniş renk bölgeleri: açık turkuaz ve koyu mavi alanlar, akıntı gibi bükülmüş kenarlarla yavaşça kayar
                float2 patchUV = xz / max(_PatchScale, 0.01) + float2(0.7, 0.4) * _PatchSpeed * t / max(_PatchScale, 0.01);
                float2 warp = float2(GradientNoise(patchUV * 0.7 + 3.1), GradientNoise(patchUV * 0.7 - 5.7));
                float patch = GradientNoise(patchUV + warp * 0.9) + GradientNoise(patchUV * 2.3 - warp * 0.5 + 11.0) * 0.35;
                patch = clamp(patch * _PatchContrast * 1.6, -1.0, 1.0); // -1 koyu, +1 açık
                water.rgb = lerp(water.rgb, _PatchLightColor.rgb, smoothstep(0.0, 1.0, patch) * _PatchLightColor.a);
                water.rgb = lerp(water.rgb, _PatchDarkColor.rgb, smoothstep(0.0, 1.0, -patch) * _PatchDarkColor.a);

                // 3) Tepecik ışığı: düz yüzeye göre güneşe daha çok bakan yamaç açık, daha az bakan koyu
                float slopeLight = (dot(normalWS, mainLight.direction) - mainLight.direction.y) * _SlopeContrast;
                water.rgb = lerp(water.rgb, _ShadeColor.rgb, saturate(-slopeLight) * _ShadeColor.a);
                water.rgb = lerp(water.rgb, _LitColor.rgb, saturate(slopeLight) * _LitColor.a);
                water.rgb = lerp(water.rgb, _CrestColor.rgb, smoothstep(0.75, 1.0, crestHeight) * _CrestColor.a);

                half3 color = lerp(sceneColor, water.rgb, water.a);

                // Gökyüzü: tepecikler her pikselde gökyüzünün başka yerini yansıtır (yatık bakınca daha çok)
                float3 reflectDirWS = reflect(-viewDirWS, normalWS);
                // Dik yamaçta yansıma aşağı dönüp gökyüzünün "yer" kısmını (kahverengi) almasın: hep yukarı bak
                reflectDirWS.y = max(reflectDirWS.y, 0.15);
                half3 sky = GlossyEnvironmentReflection(normalize(reflectDirWS), 0.1, 1.0);
                half fresnel = 0.02 + 0.98 * pow(1.0 - saturate(dot(normalWS, viewDirWS)), 5.0);
                color = lerp(color, sky, saturate(fresnel * _ReflectionStrength * 2.0));

                color *= lerp(1.0, mainLight.shadowAttenuation, _ShadowStrength);

                // Bulut gölgesi (güneşin cookie'si, bkz. CloudShadows.cs): hem renk hem güneş yansıması kararır
                half3 cloudShadow = 1.0;
                #if defined(_LIGHT_COOKIES)
                    cloudShadow = SampleMainLightCookie(input.positionWS);
                #endif
                color *= cloudShadow;
                mainLight.shadowAttenuation *= cloudShadow.r;

                // 4) Kıyı köpüğü: temasta ince dolu çizgi + dışa doğru parça parça sönen bant
                float shore = depth / max(_FoamWidth, 0.001); // 0 = temas, 1 = bandın sonu
                float foamNoise = GradientNoise(xz * _FoamNoiseScale + float2(0.3, 1.0) * t * _FoamSpeed) + 0.5;
                float foamNoise2 = GradientNoise(xz * _FoamNoiseScale * 2.3 - float2(0.8, 0.4) * t * _FoamSpeed + 9.7) + 0.5;
                half contactLine = 1.0 - smoothstep(_FoamLineWidth - _FoamSoftness, _FoamLineWidth + _FoamSoftness, shore + (foamNoise - 0.5) * 0.3);
                float bandValue = foamNoise * 0.6 + foamNoise2 * 0.4 + (1.0 - shore) * (1.0 - _FoamBreakup) - _FoamBreakup * 0.35;
                half band = smoothstep(0.55 - _FoamSoftness, 0.55 + _FoamSoftness, bandValue) * (1.0 - smoothstep(0.6, 1.0, shore));
                half foam = saturate(max(contactLine, band)) * _FoamColor.a;

                // Kıyıya gelen dalga çizgileri: derinliğin eş-değer çizgileri, zamanla sığa doğru ilerler.
                // Açıkta doğar (ince, dağınık), kıyıya yaklaştıkça kalınlaşıp bütünleşir ve temas köpüğüne karışır.
                float waveZone = depth / max(_ShoreWaveDistance, 0.001); // 0 = temas, 1 = doğduğu yer
                float wavePhase = frac(waveZone * _ShoreWaveCount + t * _ShoreWaveSpeed + (foamNoise - 0.5) * 0.35);
                float waveWidth = _ShoreWaveWidth * 0.5 * lerp(1.0, 0.35, saturate(waveZone));
                float waveAA = min(fwidth(waveZone * _ShoreWaveCount), 0.1);
                half waveLine = 1.0 - smoothstep(waveWidth - waveAA, waveWidth + waveAA, abs(wavePhase - 0.5));
                half waveFade = (1.0 - smoothstep(0.55, 1.0, waveZone)) * smoothstep(0.0, 0.12, waveZone);
                half waveSolid = smoothstep(0.3, 0.6, foamNoise2 + (1.0 - waveZone) * (1.0 - _ShoreWaveBreakup) + 0.3 - _ShoreWaveBreakup * 0.6);
                half shoreWave = waveLine * waveFade * waveSolid * step(0.01, _ShoreWaveCount) * _ShoreWaveColor.a;
                foam = saturate(max(foam, shoreWave));

                // 5) Güneş yansıması: her tepeciğin güneşe tam bakan noktasında parlak leke
                float3 halfDirWS = SafeNormalize(mainLight.direction + viewDirWS);
                float specularSoft = pow(saturate(dot(normalWS, halfDirWS)), _SpecularPower);
                float specularHard = smoothstep(0.3, 0.5, specularSoft);
                float specular = lerp(specularSoft, specularHard, _SpecularHardness) * _SpecularStrength;
                color += _SpecularColor.rgb * mainLight.color * specular * mainLight.shadowAttenuation * (1.0 - foam);

                color = lerp(color, lerp(_FoamColor.rgb, _ShoreWaveColor.rgb, saturate(shoreWave - contactLine)), foam);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
