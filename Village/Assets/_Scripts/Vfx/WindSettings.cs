using UnityEngine;

// Rüzgâr ayarları: her kare global shader değerlerine yazılır (Wind.hlsl okur; şimdilik ağaç tepeleri, ileride süs
// bitkileri). Esinti yavaşça değişir (Perlin), rüzgâr hep aynı şiddette esmesin. Sahnede bir tane.
[ExecuteAlways]
public class WindSettings : MonoBehaviour
{
    private static readonly int ParamsId = Shader.PropertyToID("_WindParams");
    private static readonly int DirectionId = Shader.PropertyToID("_WindDirection");

    [Tooltip("Tepenin en fazla kayması (birim)")]
    [SerializeField] private float amplitude = 0.06f;
    [Tooltip("Salınım hızı")]
    [SerializeField] private float speed = 1.4f;
    [Tooltip("Rüzgârın estiği yön (derece, yukarıdan bakınca)")]
    [SerializeField] private float directionAngle = 30f;
    [Tooltip("Etkinin başladığı yükseklik, objenin pivot'undan (birim): altı sabit kalır (ağaç gövdesi)")]
    [SerializeField] private float startHeight = 0.3f;
    [Tooltip("Başlangıçtan tam etkiye kadar yükseklik (birim)")]
    [SerializeField] private float fullHeight = 1.5f;
    [Tooltip("Esintinin değişme hızı")]
    [SerializeField] private float gustSpeed = 0.15f;

    private void Update()
    {
        float angle = directionAngle * Mathf.Deg2Rad;
        float gust = Mathf.PerlinNoise(Time.time * gustSpeed, 0.37f);
        Shader.SetGlobalVector(ParamsId, new Vector4(amplitude, speed, startHeight, fullHeight));
        Shader.SetGlobalVector(DirectionId, new Vector4(Mathf.Sin(angle), 0f, Mathf.Cos(angle), gust));
    }

    private void OnDisable() => Shader.SetGlobalVector(ParamsId, Vector4.zero); // kapalıyken sallanma dursun
}
