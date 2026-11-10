using System;
using UnityEngine;

// Tarlanın efektleri (asset'siz, kodla kurulur). Boat kurar (Setup), FarmGrid / FarmGrower çağırır.
//   ekim: toz bulutu + birkaç toprak topağı
//   hasat: toprak topakları + yaprak parçaları + toz (kombo büyüdükçe biraz daha çok)
//   kapak: kapanınca tarlanın kenarından dışarı toz halkası; açılınca ekinlerin üstünden yükselen parıltı
//   Full Harvest: parıltı + yaprak patlaması
//   hasat izi: yolun geçtiği karelerin ortasından toprağa yakın şerit; rengi tamamlama oranıyla açık yeşilden altına döner,
//   yol bitince söner (Full Harvest'ta önce parlar). Tarlanın child'ı: botla gider.
// Topak ve yapraklar ışık alan küçük mesh'ler (güvertenin malzemesinin renklendirilmiş kopyası: kil gibi), toz ve
// parıltı yumuşak diskler (botun efekt shader'ı, "Village/Water Foam", _Shape 0).
[Serializable]
public class FarmFx
{
    [Header("Renkler")]
    [SerializeField] private Color soilColor = new Color(0.47f, 0.32f, 0.22f);
    [SerializeField] private Color leafColor = new Color(0.45f, 0.72f, 0.32f);
    [SerializeField] private Color dustColor = new Color(0.78f, 0.66f, 0.52f, 0.6f);
    [SerializeField] private Color sparkleColor = new Color(1f, 0.95f, 0.7f, 0.95f);

    [Header("Miktar ve boy")]
    [Tooltip("Hasatta bir karede fırlayan toprak topağı ve yaprak sayısı (kombo ile biraz artar)")]
    [SerializeField] private int harvestClods = 5;
    [SerializeField] private int harvestLeaves = 4;
    [Tooltip("Topak ve yaprakların boyu (birim)")]
    [SerializeField] private float clodSize = 0.09f;
    [SerializeField] private float leafSize = 0.12f;
    [Tooltip("Toz bulutunun ve parıltıların boyu (birim)")]
    [SerializeField] private float dustSize = 0.3f;
    [SerializeField] private float sparkleSize = 0.09f;

    [Header("Hasat izi")]
    [SerializeField] private float trailWidth = 0.16f;
    [Tooltip("İzin rengi: yolun başında ve bütün olgun ekinler toplanınca (arası tamamlama oranıyla karışır)")]
    [SerializeField] private Color trailStartColor = new Color(0.92f, 1f, 0.82f, 0.75f);
    [SerializeField] private Color trailFullColor = new Color(1f, 0.82f, 0.3f, 0.95f);
    [Tooltip("Yol bitince sönme süresi (saniye)")]
    [SerializeField] private float trailFade = 0.7f;
    [Tooltip("Toprağın ne kadar üstünde (birim)")]
    [SerializeField] private float trailLift = 0.06f;

    private ParticleSystem clods, leaves, dust, sparkles;
    private bool ready;

    // Hasat izleri: biri çizilirken önceki sönüyor olabilir
    private class Trail
    {
        public LineRenderer line;
        public readonly System.Collections.Generic.List<Vector3> points = new System.Collections.Generic.List<Vector3>();
        public Color color;
        public bool drawing;
        public float fadeStart = -1f;
        public float holdUntil;
    }
    private readonly Trail[] trails = new Trail[3];
    private Trail currentTrail;

    private static readonly int ShapeId = Shader.PropertyToID("_Shape");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    // parent: efekt objelerinin duracağı yer (dünya uzayında simüle edilir); litMaterial: kopyalanıp renklendirilir;
    // softShader: "Village/Water Foam" (toz ve parıltı)
    public void Setup(Transform parent, Material litMaterial, Shader softShader)
    {
        if (litMaterial == null || softShader == null)
        {
            Debug.LogWarning("FarmFx: malzeme ya da efekt shader'ı yok (Boat → Effect Shader); tarla efektleri kapalı.", parent);
            return;
        }
        Mesh sphere = PrimitiveMesh(PrimitiveType.Sphere);
        clods = CreateSystem(parent, "Farm Clods", Colored(litMaterial, soilColor), sphere, 1.6f, true);
        leaves = CreateSystem(parent, "Farm Leaves", Colored(litMaterial, leafColor), sphere, 0.35f, true);
        dust = CreateSystem(parent, "Farm Dust", Soft(softShader), null, -0.05f, false);
        sparkles = CreateSystem(parent, "Farm Sparkles", Soft(softShader), null, -0.15f, false);
        var stripe = new Material(softShader);
        stripe.SetFloat(ShapeId, 1f); // şerit
        for (int i = 0; i < trails.Length; i++) trails[i] = new Trail { line = CreateTrailLine(parent, stripe, i) };
        ready = true;
    }

    // --- Hasat izi ---

    // Yeni yol başladı: boştaki (sönmüş) bir iz alınır
    public void BeginPath()
    {
        if (!ready) return;
        Trail free = null;
        foreach (Trail trail in trails)
            if (!trail.drawing && trail.fadeStart < 0f) { free = trail; break; }
        if (free == null) // hepsi sönüyor: en eskisi
        {
            free = trails[0];
            foreach (Trail trail in trails)
                if (!trail.drawing && trail.fadeStart < free.fadeStart) free = trail;
        }
        free.points.Clear();
        free.line.positionCount = 0;
        free.line.enabled = true;
        free.drawing = true;
        free.fadeStart = -1f;
        currentTrail = free;
    }

    // Yol bir kareye geldi. soil: karenin toprak yüzeyi (dünya), progress: hasat edilen / yol başındaki olgun ekin
    public void AddPathPoint(Vector3 soil, float progress)
    {
        if (!ready || currentTrail == null) return;
        Trail trail = currentTrail;
        trail.points.Add(trail.line.transform.InverseTransformPoint(soil + Vector3.up * trailLift));
        trail.line.positionCount = trail.points.Count;
        trail.line.SetPositions(trail.points.ToArray());
        trail.color = Color.Lerp(trailStartColor, trailFullColor, Mathf.Clamp01(progress));
        SetTrailColor(trail, 1f);
    }

    // Yol bitti: söner (full: bütün olgun ekinler toplandı, önce altın renginde parlar)
    public void EndPath(bool full)
    {
        if (!ready || currentTrail == null) return;
        Trail trail = currentTrail;
        currentTrail = null;
        trail.drawing = false;
        if (trail.points.Count == 0)
        {
            trail.line.enabled = false;
            return;
        }
        if (full)
        {
            trail.color = trailFullColor;
            trail.holdUntil = Time.time + 0.4f;
        }
        else trail.holdUntil = Time.time;
        trail.fadeStart = trail.holdUntil;
        SetTrailColor(trail, 1f);
    }

    // FarmGrid her kare: sönen izler
    public void Tick()
    {
        if (!ready) return;
        foreach (Trail trail in trails)
        {
            if (trail.drawing || trail.fadeStart < 0f || Time.time < trail.holdUntil) continue;
            float k = (Time.time - trail.fadeStart) / Mathf.Max(trailFade, 0.0001f);
            if (k >= 1f)
            {
                trail.fadeStart = -1f;
                trail.line.enabled = false;
                trail.line.positionCount = 0;
                continue;
            }
            SetTrailColor(trail, 1f - k);
        }
    }

    private static void SetTrailColor(Trail trail, float alpha)
    {
        Color c = trail.color;
        c.a *= alpha;
        Color tail = c;
        tail.a *= 0.55f; // yolun başı biraz daha soluk: yön okunsun
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                         new[] { new GradientAlphaKey(tail.a, 0f), new GradientAlphaKey(c.a, 1f) });
        trail.line.colorGradient = gradient;
    }

    // Toprağa yatık şerit: TransformZ hizası şeridi objenin Z'sine dik çizer, obje -90° X ile Z yukarı bakar
    private LineRenderer CreateTrailLine(Transform parent, Material material, int index)
    {
        var holder = new GameObject($"Harvest Trail {index}").transform;
        holder.SetParent(parent, false);
        holder.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        LineRenderer line = holder.gameObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.sharedMaterial = material;
        line.alignment = LineAlignment.TransformZ;
        line.textureMode = LineTextureMode.Stretch;
        line.widthMultiplier = trailWidth;
        line.numCornerVertices = 4;
        line.numCapVertices = 4;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.positionCount = 0;
        line.enabled = false;
        return line;
    }

    // Tohum ekildi (point: hücrenin toprak yüzeyi)
    public void Plant(Vector3 point)
    {
        if (!ready) return;
        Dust(point, 5, 0.6f);
        Clods(point, 3, 0.8f, 0.6f);
    }

    // Bir kare hasat edildi
    public void Harvest(Vector3 point, int combo)
    {
        if (!ready) return;
        float boost = 1f + Mathf.Min(combo, 10) * 0.08f;
        Clods(point, Mathf.RoundToInt(harvestClods * boost), 1.4f * boost, 1f);
        Leaves(point + Vector3.up * 0.25f, Mathf.RoundToInt(harvestLeaves * boost), 1f);
        Dust(point, 3, 0.8f);
    }

    // Kapak indi: tarlanın kenarı boyunca dışarı doğru toz (center: güvertenin üst yüzünün ortası, size: kenar uzunluğu)
    public void CoverClosed(Transform farm, float size)
    {
        if (!ready) return;
        float half = size * 0.5f;
        int perSide = Mathf.Max(4, Mathf.RoundToInt(size * 3f));
        var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false, startColor = dustColor };
        for (int side = 0; side < 4; side++)
        {
            Vector3 outward = Quaternion.Euler(0f, 90f * side, 0f) * Vector3.forward;
            Vector3 along = Vector3.Cross(Vector3.up, outward);
            for (int i = 0; i < perSide; i++)
            {
                float s = (i + UnityEngine.Random.value) / perSide * 2f - 1f;
                Vector3 local = outward * half + along * (s * half);
                emit.position = farm.TransformPoint(local) + Vector3.up * 0.1f;
                emit.velocity = farm.TransformDirection(outward) * UnityEngine.Random.Range(0.6f, 1.1f) + Vector3.up * 0.2f;
                emit.startSize = dustSize * UnityEngine.Random.Range(0.7f, 1.2f);
                emit.startLifetime = UnityEngine.Random.Range(0.5f, 0.8f);
                dust.Emit(emit, 1);
            }
        }
    }

    // Kapak açıldı: ekinlerin üstünden yükselen parıltı
    public void CoverOpened(Transform farm, float size)
    {
        if (!ready) return;
        Sparkles(farm, size, Mathf.RoundToInt(size * size * 3f), 0.4f);
    }

    // Bütün olgun ekinler tek yolda hasat edildi
    public void FullHarvest(Transform farm, float size)
    {
        if (!ready) return;
        Sparkles(farm, size, Mathf.RoundToInt(size * size * 5f), 0.9f);
        Leaves(farm.position + Vector3.up * 0.6f, Mathf.RoundToInt(size * 6f), 2.2f);
    }

    private void Clods(Vector3 point, int count, float speed, float sizeScale)
    {
        var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false, startColor = Color.white };
        for (int i = 0; i < count; i++)
        {
            Vector2 spread = UnityEngine.Random.insideUnitCircle;
            emit.position = point + new Vector3(spread.x, 0f, spread.y) * 0.15f;
            emit.velocity = new Vector3(spread.x, 0f, spread.y) * (speed * 0.6f) + Vector3.up * (speed * UnityEngine.Random.Range(0.8f, 1.3f));
            float s = clodSize * sizeScale * UnityEngine.Random.Range(0.6f, 1.3f);
            emit.startSize3D = new Vector3(s, s * UnityEngine.Random.Range(0.6f, 0.9f), s);
            emit.rotation3D = UnityEngine.Random.insideUnitSphere * 180f;
            emit.angularVelocity3D = UnityEngine.Random.insideUnitSphere * 400f;
            emit.startLifetime = UnityEngine.Random.Range(0.45f, 0.7f);
            clods.Emit(emit, 1);
        }
    }

    private void Leaves(Vector3 point, int count, float speed)
    {
        var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false, startColor = Color.white };
        for (int i = 0; i < count; i++)
        {
            Vector2 spread = UnityEngine.Random.insideUnitCircle;
            emit.position = point + new Vector3(spread.x, 0f, spread.y) * 0.12f;
            emit.velocity = new Vector3(spread.x, 0f, spread.y) * speed + Vector3.up * (speed * UnityEngine.Random.Range(0.6f, 1.1f));
            float s = leafSize * UnityEngine.Random.Range(0.7f, 1.2f);
            emit.startSize3D = new Vector3(s, s * 0.18f, s * 0.6f); // yassı yaprak
            emit.rotation3D = UnityEngine.Random.insideUnitSphere * 180f;
            emit.angularVelocity3D = UnityEngine.Random.insideUnitSphere * 300f;
            emit.startLifetime = UnityEngine.Random.Range(0.7f, 1.1f);
            leaves.Emit(emit, 1);
        }
    }

    private void Dust(Vector3 point, int count, float speed)
    {
        var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false, startColor = dustColor };
        for (int i = 0; i < count; i++)
        {
            Vector2 spread = UnityEngine.Random.insideUnitCircle;
            emit.position = point + new Vector3(spread.x, 0.05f, spread.y) * 0.15f;
            emit.velocity = new Vector3(spread.x, 0f, spread.y) * speed + Vector3.up * (speed * 0.4f);
            emit.startSize = dustSize * UnityEngine.Random.Range(0.6f, 1.1f);
            emit.startLifetime = UnityEngine.Random.Range(0.4f, 0.7f);
            dust.Emit(emit, 1);
        }
    }

    private void Sparkles(Transform farm, float size, int count, float speed)
    {
        float half = size * 0.5f;
        var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false, startColor = sparkleColor };
        for (int i = 0; i < count; i++)
        {
            Vector3 local = new Vector3(UnityEngine.Random.Range(-half, half), UnityEngine.Random.Range(0.6f, 1.1f), UnityEngine.Random.Range(-half, half));
            emit.position = farm.TransformPoint(local);
            emit.velocity = Vector3.up * (speed * UnityEngine.Random.Range(0.5f, 1.2f)) + UnityEngine.Random.insideUnitSphere * 0.15f;
            emit.startSize = sparkleSize * UnityEngine.Random.Range(0.6f, 1.4f);
            emit.startLifetime = UnityEngine.Random.Range(0.6f, 1.2f);
            sparkles.Emit(emit, 1);
        }
    }

    private static Material Colored(Material source, Color color)
    {
        var material = new Material(source);
        if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, color);
        if (material.HasProperty(ColorId)) material.SetColor(ColorId, color);
        return material;
    }

    private static Material Soft(Shader shader)
    {
        var material = new Material(shader);
        material.SetFloat(ShapeId, 0f); // disk
        return material;
    }

    private static Mesh PrimitiveMesh(PrimitiveType type)
    {
        GameObject temp = GameObject.CreatePrimitive(type);
        Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.Destroy(temp);
        return mesh;
    }

    // Elle (Emit) beslenen sistem: kendi yayımı ve şekli kapalı, dünya uzayında. mesh: ışık alan küçük parçalar (topak,
    // yaprak; sonunda küçülür), null: yumuşak disk (toz, parıltı; sonunda söner)
    private static ParticleSystem CreateSystem(Transform parent, string name, Material material, Mesh mesh, float gravity, bool shrink)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        ParticleSystem system = holder.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravity;
        main.startSpeed = 0f;
        main.maxParticles = 400;
        main.startSize3D = mesh != null;
        main.startRotation3D = mesh != null;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;

        // Hava sürtünmesi: yukarı fırlayan parçalar yavaşlasın
        ParticleSystem.LimitVelocityOverLifetimeModule drag = system.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.drag = mesh != null ? 0.6f : 2.5f;
        drag.limit = 100f;

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = shrink
            ? new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f)))
            : new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.2f)));

        if (mesh == null)
        {
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = fade;
        }

        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        if (mesh != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh;
            renderer.alignment = ParticleSystemRenderSpace.World;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        else
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        renderer.receiveShadows = mesh != null;
        system.Play();
        return system;
    }
}
