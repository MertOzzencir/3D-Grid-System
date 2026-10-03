using UnityEngine;

// Havada uçuşanlar: gündüz polen / toz zerreleri, gece ateş böcekleri. İki parçacık sistemi kodla kurulur (asset'siz,
// "Village/Glow Particle" shader'ıyla); kameranın baktığı yerin çevresinde doğarlar, dünya uzayında süzülürler.
// NightAmount (0 gündüz, 1 gece) ileride gün saati sisteminden gelecek: polen geceleri azalır, ateş böcekleri çıkar.
// Ateş böcekleri HDR parlar (bloom yakalar), polen yakalamaz.
public class AmbientParticles : MonoBehaviour
{
    [Tooltip("Village/Glow Particle. Referansla tutulur ki build'e girsin.")]
    [SerializeField] private Shader glowShader;
    [Tooltip("0 gündüz, 1 gece (gün saati sistemi gelince o sürecek)")]
    [SerializeField, Range(0f, 1f)] private float nightAmount = 0f;
    [Tooltip("Parçacıkların doğduğu alan (kameranın baktığı yerin çevresinde): genişlik, yükseklik, derinlik")]
    [SerializeField] private Vector3 area = new Vector3(16f, 2.5f, 16f);
    [Tooltip("Alanın tabanı (dünya y): adanın üstü")]
    [SerializeField] private float groundHeight = 0.6f;

    [Header("Polen")]
    [SerializeField] private float pollenRate = 18f;
    [SerializeField] private Color pollenColor = new Color(1f, 0.97f, 0.85f, 0.55f);
    [SerializeField] private Vector2 pollenSize = new Vector2(0.03f, 0.06f);

    [Header("Ateş böceği")]
    [SerializeField] private float fireflyRate = 8f;
    [SerializeField] private Color fireflyColor = new Color(0.85f, 1f, 0.55f, 1f);
    [SerializeField] private Vector2 fireflySize = new Vector2(0.08f, 0.13f);
    [Tooltip("Parlaklık: 1'in üstü bloom'a girer")]
    [SerializeField] private float fireflyGlow = 3f;

    private ParticleSystem pollen, fireflies;
    private Camera cam;

    public float NightAmount
    {
        get => nightAmount;
        set => nightAmount = Mathf.Clamp01(value);
    }

    private void Awake()
    {
        if (glowShader == null)
        {
            Debug.LogWarning("AmbientParticles: Glow Shader atanmamış; uçuşanlar kapalı.", this);
            enabled = false;
            return;
        }
        pollen = Create("Pollen", 1f, pollenColor, pollenSize, new Vector2(6f, 10f), 0.12f, 0.25f, false);
        fireflies = Create("Fireflies", fireflyGlow, fireflyColor, fireflySize, new Vector2(4f, 7f), 0.3f, 0.5f, true);
    }

    private void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam != null)
        {
            // Kameranın baktığı yer (ekranın ortasının zemin yüksekliğindeki noktası)
            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            var plane = new Plane(Vector3.up, new Vector3(0f, groundHeight, 0f));
            if (plane.Raycast(ray, out float enter))
                transform.position = ray.GetPoint(enter) + Vector3.up * (area.y * 0.5f);
        }

        ParticleSystem.EmissionModule pollenEmission = pollen.emission;
        pollenEmission.rateOverTime = pollenRate * (1f - nightAmount * 0.9f);
        ParticleSystem.EmissionModule fireflyEmission = fireflies.emission;
        fireflyEmission.rateOverTime = fireflyRate * Mathf.Clamp01((nightAmount - 0.3f) / 0.7f);
    }

    private ParticleSystem Create(string systemName, float intensity, Color color, Vector2 size, Vector2 life,
                                  float noiseStrength, float noiseFrequency, bool blink)
    {
        var holder = new GameObject(systemName);
        holder.transform.SetParent(transform, false);
        ParticleSystem system = holder.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = color;
        main.maxParticles = 250;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = area;

        // Süzülme: yavaş, dolambaçlı
        ParticleSystem.NoiseModule noise = system.noise;
        noise.enabled = true;
        noise.strength = noiseStrength;
        noise.frequency = noiseFrequency;
        noise.scrollSpeed = 0.2f;

        // Doğarken belirir, ölürken söner; ateş böcekleri arada yanıp söner
        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        GradientAlphaKey[] alpha = blink
            ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.15f, 0.35f),
                      new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0.2f, 0.75f), new GradientAlphaKey(0f, 1f) }
            : new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) };
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, alpha);
        colorOverLifetime.color = gradient;

        var material = new Material(glowShader);
        material.SetFloat("_Intensity", intensity);
        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        system.Play();
        return system;
    }
}
