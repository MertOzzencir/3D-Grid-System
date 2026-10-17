#ifndef VILLAGE_CLAY_WOOD_INCLUDED
#define VILLAGE_CLAY_WOOD_INCLUDED

// Kil odun yüzeyi: kil şeklinin (ClayMesher) şekli, UV'si ve tangent'ı her hücredeki 1BR modelinden alınır, yani Base
// Map ve Normal Map 1BR'nin kendi dokuları. Kaynaşma (uzatılan) yerlerde detay sıfıra iner: renk dokunun boş, düz
// renkli yerinden (CLAY_WOOD_EMPTY_UV), normal map yok. Detay mesh'te tangent.w'de: işaret × (1 + detay) (Lit'in Varyings'ine yeni
// alan eklenemediği için); burada okunup tangent.w tekrar ±1 yapılır, Lit normal map'i doğru çözsün.
// LitInput.hlsl'den sonra, pass dosyasından önce eklenir; pass'te InitializeStandardLitSurfaceData yerine
// CLAY_WOOD_SURFACE çağrılır (make_lit_variants.py, "surface").

// 1BR dokusunda hiçbir yüzün kullanmadığı, düz odun renkli bir nokta (newwoodcolor'ın sol üstü)
#define CLAY_WOOD_EMPTY_UV float2(0.2, 0.85)

// tangentWS.w'den detayı alır, w'yi işarete geri çevirir
half ClayWoodTakeDetail(inout half w)
{
    half detail = saturate(abs(w) - 1.0);
    w = w < 0.0 ? -1.0 : 1.0;
    return detail;
}

// Lit'in kendi hesabı yapılır, sonra kaynaşma yerinde renk düzleşir, normal map söner
void ClayWoodSurfaceData(float2 uv, half detail, out SurfaceData outSurfaceData)
{
    InitializeStandardLitSurfaceData(uv, outSurfaceData);
    half3 flat = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, CLAY_WOOD_EMPTY_UV, 0).rgb * _BaseColor.rgb;
    outSurfaceData.albedo = lerp(flat, outSurfaceData.albedo, detail);
    outSurfaceData.normalTS = normalize(lerp(half3(0.0, 0.0, 1.0), outSurfaceData.normalTS, detail));
}

// Varyings'te tangentWS sadece normal map (ya da detay) açıkken var (LitForwardPass / LitGBufferPass'teki koşul)
#if defined(_NORMALMAP) || defined(_DETAIL)
    #define CLAY_WOOD_SURFACE(uv, outSurfaceData) ClayWoodSurfaceData(uv, ClayWoodTakeDetail(input.tangentWS.w), outSurfaceData)
#else
    #define CLAY_WOOD_SURFACE(uv, outSurfaceData) ClayWoodSurfaceData(uv, 1.0, outSurfaceData)
#endif

#endif
