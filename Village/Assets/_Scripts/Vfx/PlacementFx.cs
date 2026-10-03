using System.Collections.Generic;
using UnityEngine;

// Yerleştirme cilası: grid'e bir obje konunca (build menüsü, sürükleyip bırakma, ağaçtan düşen odun...) obje önce
// basılıp yaylanarak oturur, tabanından küçük bir toz bulutu çıkar. GridManager.EntityPlaced'i dinler (kayıttan
// yüklerken o olay susturulur). Toz "Village/Water Foam" shader'ıyla (yumuşak disk) kodla kurulan tek parçacık sistemi.
// Yaylanma kökün ölçeğiyle yapılır, taban yerinde kalır; yürüyenlerde (GridWalker) konuma dokunulmaz.
public class PlacementFx : MonoBehaviour
{
    [Tooltip("Village/Water Foam. Referansla tutulur ki build'e girsin.")]
    [SerializeField] private Shader dustShader;

    [Header("Yaylanma")]
    [Tooltip("İlk basılma miktarı (0.2 = boyu %20 kısalır, eni yarısı kadar genişler)")]
    [SerializeField, Range(0f, 0.5f)] private float squash = 0.22f;
    [SerializeField] private float duration = 0.45f;
    [Tooltip("Süre boyunca kaç kez esnesin")]
    [SerializeField] private float bounces = 2.5f;

    [Header("Toz")]
    [SerializeField] private int puffs = 10;
    [SerializeField] private Vector2 puffSize = new Vector2(0.12f, 0.24f);
    [SerializeField] private float puffSpeed = 0.9f;
    [SerializeField] private Color dustColor = new Color(0.96f, 0.93f, 0.87f, 0.85f);

    private class Bounce
    {
        public Transform target;
        public Vector3 scale, position;
        public float bottomOffset; // kökten tabana (yükseklik): ölçek değişirken taban yerinde kalsın
        public bool keepPosition;  // yürüyenler: konumu hareket modülü sürer
        public float start;
    }

    private readonly List<Bounce> active = new List<Bounce>();
    private ParticleSystem dust;
    private GridManager subscribed;

    private void Awake()
    {
        if (dustShader != null) dust = CreateDust();
        else Debug.LogWarning("PlacementFx: Dust Shader atanmamış; toz kapalı, yaylanma çalışır.", this);
    }

    private void Start() => Subscribe();

    private void OnEnable() => Subscribe();

    private void OnDisable()
    {
        if (subscribed != null) subscribed.EntityPlaced -= OnPlaced;
        subscribed = null;
        foreach (Bounce bounce in active) Restore(bounce);
        active.Clear();
    }

    private void Subscribe()
    {
        if (subscribed != null || GridManager.Instance == null) return;
        subscribed = GridManager.Instance;
        subscribed.EntityPlaced += OnPlaced;
    }

    private void OnPlaced(GridEntity entity)
    {
        if (entity == null) return;
        Transform target = entity.transform;

        // Aynı obje hâlâ yaylanıyorsa (hemen tekrar kondu) önce eski haline
        for (int i = active.Count - 1; i >= 0; i--)
        {
            if (active[i].target != target) continue;
            Restore(active[i]);
            active.RemoveAt(i);
        }

        bool walker = entity.GetComponent<GridWalker>() != null;
        float bottom = walker ? target.position.y : entity.OriginWorldPosition.y - 0.5f; // hücrenin tabanı
        active.Add(new Bounce
        {
            target = target,
            scale = target.localScale,
            position = target.position,
            bottomOffset = target.position.y - bottom,
            keepPosition = walker,
            start = Time.time,
        });
        EmitDust(entity, bottom);
    }

    private void Update()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Bounce bounce = active[i];
            if (bounce.target == null) { active.RemoveAt(i); continue; }

            float k = (Time.time - bounce.start) / Mathf.Max(duration, 0.01f);
            if (k >= 1f)
            {
                Restore(bounce);
                active.RemoveAt(i);
                continue;
            }

            // Basılmış başlar, sönümlenerek esneyip oturur
            float amount = squash * Mathf.Exp(-k * 4f) * Mathf.Cos(k * bounces * Mathf.PI * 2f);
            float height = 1f - amount, width = 1f + amount * 0.5f;
            bounce.target.localScale = Vector3.Scale(bounce.scale, new Vector3(width, height, width));
            if (!bounce.keepPosition)
                bounce.target.position = bounce.position + Vector3.up * (bounce.bottomOffset * (height - 1f));
        }
    }

    private static void Restore(Bounce bounce)
    {
        if (bounce.target == null) return;
        bounce.target.localScale = bounce.scale;
        if (!bounce.keepPosition) bounce.target.position = bounce.position;
    }

    // Tabanın çevresinde halka: objenin genişliğine göre
    private void EmitDust(GridEntity entity, float bottom)
    {
        if (dust == null) return;
        Bounds bounds = default;
        bool any = false;
        foreach (Renderer renderer in entity.GetComponentsInChildren<Renderer>())
        {
            if (renderer is ParticleSystemRenderer) continue;
            if (!any) bounds = renderer.bounds;
            else bounds.Encapsulate(renderer.bounds);
            any = true;
        }
        Vector3 center = any ? bounds.center : entity.transform.position;
        float radius = any ? Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.8f, 0.3f, 1.5f) : 0.5f;

        var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false, startColor = dustColor };
        for (int i = 0; i < puffs; i++)
        {
            float angle = (i + Random.value * 0.5f) / puffs * Mathf.PI * 2f;
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            emit.position = new Vector3(center.x, bottom + 0.06f, center.z) + direction * radius;
            emit.velocity = (direction + Vector3.up * 0.35f) * (puffSpeed * Random.Range(0.6f, 1.1f));
            emit.startSize = Random.Range(puffSize.x, puffSize.y);
            emit.startLifetime = Random.Range(0.5f, 0.8f);
            dust.Emit(emit, 1);
        }
    }

    private ParticleSystem CreateDust()
    {
        var holder = new GameObject("Placement Dust");
        holder.transform.SetParent(transform, false);
        ParticleSystem system = holder.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.maxParticles = 600;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;

        // Hızla yavaşlar, büyüyerek söner
        ParticleSystem.LimitVelocityOverLifetimeModule limit = system.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.drag = 4f;
        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.6f)));
        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.4f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;

        var material = new Material(dustShader);
        material.SetFloat("_Shape", 0f);
        material.SetFloat("_Breakup", 0.3f);
        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        system.Play();
        return system;
    }
}
