#ifndef VILLAGE_JIGGLE_INCLUDED
#define VILLAGE_JIGGLE_INCLUDED

// Vuruşta yerel jöle titreşimi (örn. kedinin kıçına şaplak): vuruş noktası çevresi önce içeri göçer, sonra
// sönümlenerek birkaç kez titrer; üstünde noktadan dışa yayılan hafif bir dalga. Aynı anda bölge şişip iner
// (içerideki bir merkezden dışa doğru büyüyüp küçülür). Etki noktadan uzaklaştıkça söner.
// Değerleri CreatureJiggle.cs vuruş anında MaterialPropertyBlock ile DÜNYA uzayında gönderir; hesap dünyada yapılıp
// object space'e geri çevrilir. Skinned mesh'ler kök kemiğin uzayında çizildiği için object space'e güvenilmez.
// Animasyonu GPU _Time.y ile kendisi oynatır. Hiç ayarlanmamışsa (yarıçap 0) etkisizdir.
// Kayma hep itme yönünde (normale bakılmaz): derinlik pass'i ile renk pass'i birebir aynı yeri bulur.
float4 _JiggleCenter; // xyz: vuruş noktası (dünya), w: etki yarıçapı
float4 _JiggleDir;    // xyz: içeri itme yönü × derinlik (dünya), w: başlangıç zamanı (_Time.y)
float4 _JiggleShape;  // x: süre, y: titreşim sayısı, z: dalga boyu, w: dalga miktarı (derinliğe oranla)
float4 _JiggleSwell;  // x: şişme miktarı (dünya birimi), y: şişip inme sayısı, z: şişme merkezinin yüzeyden derinliği

float3 JiggleOffsetWS(float3 positionWS)
{
    float duration = max(_JiggleShape.x, 0.0001);
    float t = (_Time.y - _JiggleDir.w) / duration;
    if (_JiggleCenter.w <= 0.0 || t <= 0.0 || t >= 1.0) return 0.0;

    float distanceToHit = distance(positionWS, _JiggleCenter.xyz);
    float falloff = saturate(1.0 - distanceToHit / _JiggleCenter.w);
    falloff = falloff * falloff * (3.0 - 2.0 * falloff);
    if (falloff <= 0.0) return 0.0;

    float decay = (1.0 - t) * (1.0 - t);
    // İlk yarım titreşim içeri (vuruş), sonra geri sekip söner
    float wobble = sin(t * _JiggleShape.y * TWO_PI) * decay;
    // Dışa yayılan dalga: merkezden uzaklaştıkça gecikmeli
    float wave = sin((distanceToHit / max(_JiggleShape.z, 0.0001) - t * _JiggleShape.y) * TWO_PI) * _JiggleShape.w * decay;

    float3 offset = _JiggleDir.xyz * (wobble + wave) * falloff;

    // Şişme: yüzeyin içindeki bir merkezden dışa doğru büyüyüp küçülür (önce büyür, sönümlenerek normale döner)
    if (_JiggleSwell.x > 0.0)
    {
        float3 inward = normalize(_JiggleDir.xyz + 1e-6);
        float3 swellCenter = _JiggleCenter.xyz + inward * _JiggleSwell.z;
        float3 outward = normalize(positionWS - swellCenter + 1e-6);
        float swell = sin(t * _JiggleSwell.y * TWO_PI) * decay;
        offset += outward * _JiggleSwell.x * swell * falloff;
    }
    return offset;
}

void ApplyJiggle(inout float3 positionOS)
{
    float3 positionWS = TransformObjectToWorld(positionOS);
    float3 offsetWS = JiggleOffsetWS(positionWS);
    // Normalize etmeden (ölçek dahil) object space'e
    positionOS += mul((float3x3)GetWorldToObjectMatrix(), offsetWS);
}

void ApplyJiggle(inout float3 positionOS, inout float3 normalOS)
{
    ApplyJiggle(positionOS);
}

#endif
