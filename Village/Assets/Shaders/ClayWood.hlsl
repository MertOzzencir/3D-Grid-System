#ifndef VILLAGE_CLAY_WOOD_INCLUDED
#define VILLAGE_CLAY_WOOD_INCLUDED

// Kil odun dokusu: koddan üretilen kil şeklinin (ClayMesher) UV açılımı yok. Base Map'teki odun dokusu objenin kendi
// uzayında üç yandan yansıtılır (triplanar) ve lifler her noktada o noktanın ait olduğu odunun yönünde akar.
// Lif yönü mesh'in UV0'ında: (X ağırlığı, Z ağırlığı), Y = 1 - ikisi (birleşim yerlerinde karışık).
// Dokuda lifler soldan sağa (U yönünde) çizilmiş olmalı. Base Map Tiling: X = lif boyunca, Y = lif boyunca değil
// (1 = her hücrede bir tekrar); Offset kaydırır. Uç yüzeylere de aynı doku düz yansır (halka yok).
// LitInput.hlsl'den sonra, pass dosyasından önce eklenir; shader InitializeStandardLitSurfaceData yerine
// ClayWoodSurfaceData'yı çağırır (make_lit_variants.py, "surface").

#define CLAY_WOOD_SHARPNESS 4.0 // yüzey yönüne göre üç yansıtmanın ne kadar keskin geçeceği

half3 ClayWoodSample(float along, float across)
{
    // along: lif boyunca koordinat, across: lif boyunca değil
    float2 uv = float2(along * _BaseMap_ST.x, across * _BaseMap_ST.y) + _BaseMap_ST.zw;
    return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
}

// Bir lif ekseni için triplanar: lif eksenine paralel iki yüzde doku lif boyunca, uçta (eksene dik yüz) düz
half3 ClayWoodGrain(float3 p, float3 w, int axis)
{
    if (axis == 0) // X boyunca
        return ClayWoodSample(p.x, p.z) * w.y + ClayWoodSample(p.x, p.y) * w.z + ClayWoodSample(p.z, p.y) * w.x;
    if (axis == 2) // Z boyunca
        return ClayWoodSample(p.z, p.x) * w.y + ClayWoodSample(p.z, p.y) * w.x + ClayWoodSample(p.x, p.y) * w.z;
    // Y boyunca (dik)
    return ClayWoodSample(p.y, p.x) * w.z + ClayWoodSample(p.y, p.z) * w.x + ClayWoodSample(p.x, p.z) * w.y;
}

// uv: vertex'in TRANSFORM_TEX'ten geçmiş UV0'ı (lif ağırlıkları), Lit'in kendi hesabı yapılıp sadece renk değiştirilir
void ClayWoodSurfaceData(float2 uv, float3 positionWS, float3 normalWS, out SurfaceData outSurfaceData)
{
    InitializeStandardLitSurfaceData(uv, outSurfaceData);

    float2 tiling = (abs(_BaseMap_ST.xy) < 0.0001) ? 0.0001 : _BaseMap_ST.xy; // vertex'teki TRANSFORM_TEX geri alınır
    float2 grain = saturate((uv - _BaseMap_ST.zw) / tiling);
    float gx = grain.x, gz = grain.y, gy = saturate(1.0 - gx - gz);

    float3 p = TransformWorldToObject(positionWS);
    float3 n = TransformWorldToObjectNormal(normalWS);
    float3 w = pow(abs(n), CLAY_WOOD_SHARPNESS);
    w /= max(w.x + w.y + w.z, 0.0001);

    // Dallanma yok (doku örneklemesi dallanmada türev sorunu çıkarıyor); 9 örnek, PC için ucuz
    half3 color = ClayWoodGrain(p, w, 0) * gx + ClayWoodGrain(p, w, 1) * gy + ClayWoodGrain(p, w, 2) * gz;
    outSurfaceData.albedo = color * _BaseColor.rgb;
}

#endif
