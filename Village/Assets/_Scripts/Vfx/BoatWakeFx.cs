using System;
using UnityEngine;

// Botun su efektleri (asset'siz, kodla kurulur; hepsi "Village/Water Foam" shader'ıyla):
//   köpük izi: kıçta su yüzeyine yatık TrailRenderer; geride kaldıkça genişleyip söner (bot giderken)
//   kürek sıçraması: kürek suya girerken damlalar fırlar + suda halka yayılır, çıkarken birkaç damla düşer
//   burun dalgası: giderken burunda arada bir halka
// Boat sürer: Setup bir kez, Tick her kare, OarSplash kürek suya girip çıkarken.
[Serializable]
public class BoatWakeFx
{
    [Header("Köpük izi")]
    [Tooltip("İzin kıçtan başladığı yer (botun merkezinden geriye, birim)")]
    [SerializeField] private float wakeStern = 0.9f;
    [Tooltip("İzin ne kadar sürede söndüğü (saniye): uzunluğu = süre × hız")]
    [SerializeField] private float wakeTime = 1.4f;
    [Tooltip("İzin genişliği: botun arkasında ve en sonda (birim)")]
    [SerializeField] private float wakeWidthStart = 0.55f;
    [SerializeField] private float wakeWidthEnd = 1.6f;
    [SerializeField] private Color wakeColor = new Color(1f, 1f, 1f, 0.7f);

    [Header("Kürek sıçraması")]
    [Tooltip("Kürek suya girerken fırlayan damla sayısı (çıkarken yarısı)")]
    [SerializeField] private int splashDrops = 10;
    [SerializeField] private float splashSpeed = 1.6f;
    [SerializeField] private float splashSize = 0.07f;
    [Tooltip("Küreğin suya girdiği yer: tutturulduğu yerden yana (birim)")]
    [SerializeField] private float splashReach = 0.55f;
    [Tooltip("Küreğin suya girdiği yer: tutturulduğu yerden ileri (girerken) / geri (çıkarken) (birim)")]
    [SerializeField] private float splashSweep = 0.25f;
    [SerializeField] private Color splashColor = new Color(0.92f, 0.97f, 1f, 0.9f);

    [Header("Halkalar")]
    [SerializeField] private float rippleSize = 0.7f;
    [SerializeField] private float rippleLife = 0.9f;
    [Tooltip("Giderken burunda halka sıklığı (saniye)")]
    [SerializeField] private float bowRippleEvery = 0.3f;
    [Tooltip("Burnun yeri: botun merkezinden ileri (birim)")]
    [SerializeField] private float bowForward = 0.95f;
    [SerializeField] private Color rippleColor = new Color(1f, 1f, 1f, 0.55f);

    private Transform boat;
    private float waterHeight; // botun köküne göre su seviyesi
    private TrailRenderer wake;
    private ParticleSystem drops, ripples;
    private float bowTimer;
    private bool ready;

    private static readonly int ShapeId = Shader.PropertyToID("_Shape");

    // boatRoot: botun kökü (sadece yatayda döner); waterLevel: kökün üstünde su seviyesi
    public void Setup(Transform boatRoot, Shader shader, float waterLevel)
    {
        if (shader == null)
        {
            Debug.LogWarning("BoatWakeFx: 'Village/Water Foam' shader'ı atanmamış (Boat → Effect Shader); su efektleri kapalı.", boatRoot);
            return;
        }
        boat = boatRoot;
        waterHeight = waterLevel;

        wake = CreateWake(MakeMaterial(shader, 1f));
        drops = CreateSystem("Splash Drops", MakeMaterial(shader, 0f), false, 1.6f,
                             new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.4f)));
        ripples = CreateSystem("Ripples", MakeMaterial(shader, 2f), true, 0f,
                               new AnimationCurve(new Keyframe(0f, 0.25f), new Keyframe(1f, 1f, 0.6f, 0f)));
        ready = true;
    }

    // moving: bot gidiyor mu (iz ve burun halkaları)
    public void Tick(float dt, bool moving)
    {
        if (!ready) return;
        wake.emitting = moving;

        if (!moving)
        {
            bowTimer = 0f;
            return;
        }
        bowTimer += dt;
        if (bowTimer < bowRippleEvery) return;
        bowTimer = 0f;
        Ripple(WaterPoint(boat.position + boat.forward * bowForward), rippleSize * 0.7f);
    }

    // Kürek suya giriyor (entering) ya da çıkıyor. pivot: küreğin tutturulduğu yer, side: sağ +1, sol -1
    public void OarSplash(Vector3 pivot, float side, bool entering)
    {
        if (!ready) return;
        Vector3 point = WaterPoint(pivot + boat.right * (side * splashReach) + boat.forward * (entering ? splashSweep : -splashSweep));

        int count = entering ? splashDrops : Mathf.Max(1, splashDrops / 2);
        float speed = entering ? splashSpeed : splashSpeed * 0.5f;
        var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false, startColor = splashColor };
        for (int i = 0; i < count; i++)
        {
            Vector2 spread = UnityEngine.Random.insideUnitCircle * 0.7f;
            emit.position = point + new Vector3(spread.x, 0f, spread.y) * 0.08f;
            emit.velocity = new Vector3(spread.x, 1f, spread.y) * (speed * UnityEngine.Random.Range(0.6f, 1.1f));
            emit.startSize = splashSize * UnityEngine.Random.Range(0.6f, 1.3f);
            emit.startLifetime = UnityEngine.Random.Range(0.35f, 0.6f);
            drops.Emit(emit, 1);
        }
        Ripple(point, entering ? rippleSize : rippleSize * 0.6f);
    }

    private void Ripple(Vector3 point, float size)
    {
        var emit = new ParticleSystem.EmitParams
        {
            applyShapeToPosition = false,
            position = point + Vector3.up * 0.01f,
            velocity = Vector3.zero,
            startSize = size,
            startLifetime = rippleLife,
            startColor = rippleColor,
        };
        ripples.Emit(emit, 1);
    }

    private Vector3 WaterPoint(Vector3 point)
    {
        point.y = boat.position.y + waterHeight;
        return point;
    }

    private static Material MakeMaterial(Shader shader, float shape)
    {
        var material = new Material(shader);
        material.SetFloat(ShapeId, shape);
        return material;
    }

    // Kıçta, su yüzeyine yatık iz: TransformZ hizası şeridi objenin Z eksenine dik çizer, Z yukarı bakar
    private TrailRenderer CreateWake(Material material)
    {
        var anchor = new GameObject("Wake").transform;
        anchor.SetParent(boat, false);
        anchor.localPosition = new Vector3(0f, waterHeight + 0.02f, -wakeStern);
        anchor.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        TrailRenderer trail = anchor.gameObject.AddComponent<TrailRenderer>();
        trail.sharedMaterial = material;
        trail.alignment = LineAlignment.TransformZ;
        trail.textureMode = LineTextureMode.Stretch;
        trail.time = wakeTime;
        trail.minVertexDistance = 0.08f;
        trail.numCapVertices = 4;
        trail.widthMultiplier = 1f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, wakeWidthStart), new Keyframe(1f, wakeWidthEnd));
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(wakeColor, 0f), new GradientColorKey(wakeColor, 1f) },
            new[] { new GradientAlphaKey(wakeColor.a, 0f), new GradientAlphaKey(wakeColor.a * 0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = gradient;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.emitting = false;
        return trail;
    }

    // Elle (Emit) beslenen parçacık sistemi: kendi yayımı ve şekli kapalı, dünya uzayında, sürekli oynar
    private ParticleSystem CreateSystem(string name, Material material, bool flat, float gravity, AnimationCurve size)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(boat, false);
        ParticleSystem system = holder.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravity;
        main.startSpeed = 0f;
        main.maxParticles = 300;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, size);

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = fade;

        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = flat ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        system.Play();
        return system;
    }
}
