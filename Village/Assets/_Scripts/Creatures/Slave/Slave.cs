using System.Collections.Generic;
using UnityEngine;

// Kafeste bakılan insan (şimdilik kafessiz): grid'de rastgele bir hücre seçer, oraya yürür, bir süre bekler, tekrar.
// Tek hücrelik canlı: ayak izi datadaki gibi (1×1, boyu kadar kat). Kedinin iki hücreli GridWalker'ı yerine kendi basit
// adımlarını atar: her adımın BAŞINDA yeni hücreye geçer (GridManager.TryMovePlaceable), konum iki hücre arasında kayar.
// Pivot kuralı diğer objelerle aynı: kök hücrenin ortasında, model 0.5 aşağıda (tabanda). Yürüme animasyonu SlaveRig'de.
// Taşıma (kullanıcı 2026-11-02): sağ tıkla popodan tutulur, yüzüstü yatay sarkar (yüzü yere, kafası kameranın sağına)
// ve sallanır (eldiven popodaki
// tutma noktasına oturur: IGloveGrip). El hızlanınca gövde geride kalır, dururken de hafifçe sağa sola sallanır.
// Bırakınca mouse'un altındaki (yoksa en yakın) durulabilir hücreye iner, ayağa kalkar ve dolaşmaya devam eder.
// Boş bir kafesin üstüne bırakılırsa içine girer (EnterCage): kafesin child'ı olur, grid'de hücre kaplamaz, dört ayak
// üstünde (diz ve eller yerde) bekler (kullanıcı: hasat olmayı bekler). Sol tıkla (kendisine ya da kafese) kafesten
// çıkıp ele gelir: sol tık basılı kaldıkça taşınır, bırakınca normal bırakma (kullanıcı). Kıça sol tık şaplak (kedideki
// gibi: eldiven bastırır, kıçta vertex'lerle jöle dalgası yayılır (CreatureJiggle, shader'da), kafa sallanır); her yerde
// (kafeste de) çalışır.
// Collider'lar mesh'ten kemik başına ölçülür (kedideki gibi kapsül / küre, SlavePart): eldiven gövdenin içine girmez,
// dört ayak pozunda da gövdeyle birlikte durur. Mesh okunamazsa kökteki kaba kutu kalır. Hemen bırakılırsa (tıklayıp
// geçmek) kafese geri girmez, yanındaki en yakın boş hücreye iner. Sağ tıkla tutulursa da çıkar.
public class Slave : Creature, IInteractable, IGloveGrip, IClickable
{
    [Header("Dolaşma")]
    [Tooltip("Birim / saniye")]
    [SerializeField] private float walkSpeed = 0.7f;
    [Tooltip("Hedef, bulunduğu hücreye en fazla bu kadar hücre uzakta seçilir")]
    [SerializeField] private int wanderRadius = 4;
    [Tooltip("Hedefe varınca bekleme süresi (saniye, en az / en çok)")]
    [SerializeField] private Vector2 waitSeconds = new Vector2(1.5f, 4f);
    [Tooltip("Yürüme yönüne dönme hızı")]
    [SerializeField] private float turnSharpness = 8f;

    [SerializeField] private SlaveRig rig;

    [Header("Taşıma")]
    [Tooltip("Tutma noktası (popo), kökün yerel uzayında. Kök hücrenin ortasında, ayaklar 0.5 aşağıda.")]
    [SerializeField] private Vector3 gripLocalPosition = new Vector3(0f, 0.05f, -0.3f);
    [Tooltip("Eldivenin tutarken parmak kıvrılması (0 düz, 1 tam kıvrık)")]
    [SerializeField, Range(0f, 1f)] private float gripCurl = 0.75f;
    [Tooltip("Popo yerden bu kadar yukarıda taşınır (birim): sarkan kol ve bacaklar yere sürtünmesin")]
    [SerializeField] private float carryHeight = 1.5f;
    [Tooltip("Elin mouse'u takip hızı")]
    [SerializeField] private float carryFollowSharpness = 14f;
    [Tooltip("Sarkacın sertliği (yüksek: hızlı salınır)")]
    [SerializeField] private float swingStiffness = 40f;
    [Tooltip("Sarkacın sönümü (yüksek: çabuk durulur)")]
    [SerializeField] private float swingDamping = 3.5f;
    [Tooltip("El hızlanınca gövdenin geride kalma miktarı")]
    [SerializeField] private float swingFromMotion = 2.2f;
    [Tooltip("Sarkacın en büyük açısı (derece)")]
    [SerializeField] private float maxSwing = 55f;
    [Tooltip("Dururken sağa sola sallanma (derece, dikey eksen etrafında)")]
    [SerializeField] private float idleSwing = 12f;
    [Tooltip("Dururken sallanma hızı (saniyedeki salınım)")]
    [SerializeField] private float idleSwingRate = 0.7f;
    [Header("Şaplak")]
    [Tooltip("Kıçın titremesi (materyal Village/Creature Jiggle Lit olmalı)")]
    [SerializeField] private CreatureJiggle slapJiggle = new CreatureJiggle();
    [Tooltip("Eldivenin vurmadan önce kalkması (birim)")]
    [SerializeField] private float slapLift = 0.25f;
    [Tooltip("Eldivenin vuruş süresi (saniye)")]
    [SerializeField] private float slapDuration = 0.16f;

    [Header("Collider")]
    [Tooltip("Kemik collider'larının kalınlığı: vertex'lerin bu oranı içinde kalır (1 en dıştaki vertex'e kadar)")]
    [SerializeField, Range(0.5f, 1f)] private float colliderFitPercentile = 0.85f;
    [Tooltip("Şaplak alanı: kıçın arkasında (tutma noktasında) bu yarıçapta küre. Büyüdükçe kıça vurmak kolaylaşır.")]
    [SerializeField] private float rearZoneRadius = 0.32f;
    [Tooltip("Şaplak alanının tutma noktasına göre kayması (kökün yerel uzayında; -Z kıçın gerisi)")]
    [SerializeField] private Vector3 rearZoneOffset = new Vector3(0f, 0f, 0.05f);

    [Header("İniş")]
    [Tooltip("Bırakınca hücreye inip ayağa kalkma süresi (saniye)")]
    [SerializeField] private float landSeconds = 0.35f;
    [Tooltip("Kafese girme süresi (saniye): kısa ve sert, içine atılır gibi (kullanıcı)")]
    [SerializeField] private float cageEnterSeconds = 0.12f;

    private Vector3Int cell;
    private bool placed;
    private Transform gripPoint;

    // Taşıma durumu
    private bool held;
    private Vector3 holdPoint, holdVelocity;
    private Vector3 swing, swingVelocity; // sarkaç: başın yatayda kaçtığı yön (derece cinsinden vektör)
    private float swingTime;
    private Quaternion pickupRotation; // tutulduğu andaki duruş: yüzüstü duruşa yumuşakça geçilir
    private Coroutine landing;
    private Cage cage; // içinde olduğu kafes (yoksa null)
    private SkinnedMeshRenderer bodyRenderer;
    private Cage pickedFromCage; // bu taşımada çıkarıldığı kafes
    private float pickupTime;
    [Tooltip("Kafesten sol tıkla alınıp bu süreden kısa tutulursa (tıklayıp bırakmak) kafese geri girmez, yanına iner")]
    [SerializeField] private float quickClickSeconds = 0.25f;
    private readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    public Vector3Int Cell => cell;
    // Animasyon için: şu anki ilerleme hızı (dururken 0)
    public float CurrentSpeed { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        if (rig == null) rig = GetComponent<SlaveRig>();

        // Eldivenin oturacağı nokta: Z parmaklar (gövdenin yukarısı), Y avucun baktığı yön (popoya doğru, ileri)
        gripPoint = new GameObject("Butt Grip").transform;
        gripPoint.SetParent(transform, false);
        gripPoint.localPosition = gripLocalPosition;
        gripPoint.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
    }

    private void Start() => BuildColliders();

    public override void OnPlaced(Vector3Int origin)
    {
        base.OnPlaced(origin);
        cell = origin;
        placed = true;
        ChangeState(new WaitState(this));
    }

    protected override void Update()
    {
        if (!placed) return; // sadece grid'e konunca (önizleme kopyası yürümesin)
        base.Update();
        if (rig != null) rig.Tick(CurrentSpeed, held, holdVelocity, cage != null && landing == null);
    }

    // --- Taşıma (IInteractable, IGloveGrip) ---

    public Transform GripPoint => gripPoint;
    public float GripCurl => gripCurl;

    public void InteractContractBeginnig()
    {
        if (landing != null) { StopCoroutine(landing); landing = null; }
        pickedFromCage = cage;
        pickupTime = Time.time;
        LeaveCage();
        held = true;
        ChangeState(null);
        CurrentSpeed = 0f;
        GridManager.Instance.PlaceableRemoveOn(this, false);

        holdPoint = gripPoint.position;
        holdVelocity = Vector3.zero;
        swing = swingVelocity = Vector3.zero;
        swingTime = 0f;
        pickupRotation = transform.rotation;
    }

    public void InteractContract(out bool success)
    {
        success = true;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        // El: mouse'un altındaki yüzeyin carryHeight üstü, yumuşak takip
        Vector3 target = holdPoint;
        if (TryMouseGround(out Vector3 ground, out _)) target = ground + Vector3.up * carryHeight;
        Vector3 previous = holdPoint;
        holdPoint = Vector3.Lerp(holdPoint, target, 1f - Mathf.Exp(-carryFollowSharpness * dt));
        Vector3 velocity = (holdPoint - previous) / dt;
        Vector3 acceleration = (velocity - holdVelocity) / dt;
        holdVelocity = velocity;
        acceleration.y = 0f;

        // Sarkaç: el bir yöne hızlanınca baş ters yöne kaçar, yay ve sönümle geri gelir
        Vector3 force = -swingStiffness * swing - swingDamping * swingVelocity - acceleration * swingFromMotion;
        swingVelocity += force * dt;
        swing += swingVelocity * dt;
        swing.y = 0f;
        swing = Vector3.ClampMagnitude(swing, maxSwing);

        // Yüzüstü yatay: yüzü yere, kafası kameranın sağına (oyuncu yandan görsün), popo üstte
        Vector3 toCamera = Camera.main.transform.position - holdPoint;
        toCamera.y = 0f;
        if (toCamera.sqrMagnitude < 0.0001f) toCamera = Vector3.forward;
        Vector3 headSide = Vector3.Cross(Vector3.up, toCamera.normalized) * -1f;
        Quaternion hang = Quaternion.LookRotation(Vector3.down, headSide);

        // Dururken dikey eksen etrafında sağa sola sallanır (popodan tutulan çanta gibi)
        swingTime += dt;
        Quaternion idleSway = Quaternion.AngleAxis(Mathf.Sin(swingTime * idleSwingRate * Mathf.PI * 2f) * idleSwing, Vector3.up);
        Quaternion tilt = Quaternion.identity;
        if (swing.sqrMagnitude > 0.0001f)
            tilt = Quaternion.AngleAxis(swing.magnitude, Vector3.Cross(Vector3.down, swing.normalized));

        // Tutma noktası (popo) elde kalır, gövde onun etrafında döner
        Quaternion rotation = Quaternion.Slerp(pickupRotation, tilt * idleSway * hang, Mathf.SmoothStep(0f, 1f, swingTime / 0.3f));
        transform.SetPositionAndRotation(holdPoint - rotation * gripLocalPosition, rotation);
    }

    // Sol tık: şimdilik bir şey yapmaz (kafese koyma gelecek)
    public void Interact(out bool finished) => finished = false;

    public void ContractCancel()
    {
        held = false;
        bool overGround = TryMouseGround(out Vector3 ground, out Collider hitCollider);

        // Boş kafesin üstüne bırakıldı: içine girer
        Cage dropCage = hitCollider != null ? hitCollider.GetComponentInParent<Cage>() : null;
        Cage fromCage = pickedFromCage;
        pickedFromCage = null;
        if (dropCage != null && dropCage == fromCage && Time.time - pickupTime < quickClickSeconds)
        {
            LandBeside(dropCage);
            return;
        }
        if (dropCage != null && dropCage.IsEmpty)
        {
            EnterCage(dropCage, instant: false);
            return;
        }

        Vector3Int start = overGround ? Vector3Int.RoundToInt(ground + Vector3.up * 0.5f) : cell;
        if (!TryFindLandingCell(start, out Vector3Int target) && !TryFindLandingCell(cell, out target))
        {
            Debug.LogWarning($"{name} could not find a cell to land on", this);
            target = cell;
        }
        GridManager.Instance.TryMovePlaceable(this, target, GetFootprint());
        cell = target;
        landing = StartCoroutine(Land(target));
    }

    // Mouse'un altındaki ilk yüzey (kendisi hariç) ve collider'ı
    private bool TryMouseGround(out Vector3 point, out Collider hitCollider)
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        int count = Physics.RaycastNonAlloc(ray, hitBuffer, 500f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        point = default;
        hitCollider = null;
        for (int i = 0; i < count; i++)
        {
            if (hitBuffer[i].transform.IsChildOf(transform) || hitBuffer[i].distance >= best) continue;
            best = hitBuffer[i].distance;
            point = hitBuffer[i].point;
            hitCollider = hitBuffer[i].collider;
        }
        return best < float.MaxValue;
    }

    // --- Kafes ---

    // Kafese girer: kafesin ortasına iner, yüzü kafesin kapısına (kafesin +Z'si, kullanıcı), kafesin child'ı olur. instant: kayıttan yükleme (animasyonsuz)
    public void EnterCage(Cage target, bool instant)
    {
        cage = target;
        target.SetOccupant(this);
        placed = true;
        held = false;
        ChangeState(null);
        CurrentSpeed = 0f;
        GridManager.Instance.PlaceableRemoveOn(this, false); // kafesin hücrelerinde durur, kendi hücresi yok

        Vector3 localPosition = target.LocalCenter;
        Quaternion localRotation = Quaternion.identity; // kafesin önü (kapı) yönünde

        if (instant)
        {
            transform.SetParent(target.transform, false);
            transform.localPosition = localPosition;
            transform.localRotation = localRotation;
            return;
        }
        landing = StartCoroutine(LandInCage(target, localPosition, localRotation));
    }

    private System.Collections.IEnumerator LandInCage(Cage target, Vector3 localPosition, Quaternion localRotation)
    {
        Vector3 fromPosition = transform.position;
        Quaternion fromRotation = transform.rotation;
        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(cageEnterSeconds, 0.01f))
        {
            float eased = t * t; // hızlanarak düşer, yumuşak iniş yok
            transform.SetPositionAndRotation(Vector3.Lerp(fromPosition, target.transform.TransformPoint(localPosition), eased),
                                             Quaternion.Slerp(fromRotation, target.transform.rotation * localRotation, eased));
            yield return null;
        }
        transform.SetParent(target.transform, false);
        transform.localPosition = localPosition;
        transform.localRotation = localRotation;
        landing = null;
    }

    // Sol tık (elde bir şey yokken): kafesteyse çıkar
    public void OnClicked(RaycastHit hit) => TakeOutOfCage();

    // Bir collider'ına sol tık: kıçsa şaplak, değilse kafesten çıkar
    public void OnPartClicked(bool rear, RaycastHit hit)
    {
        if (rear) Slap(hit);
        else TakeOutOfCage();
    }

    private void Slap(RaycastHit hit)
    {
        if (GloveCursor.Instance != null) GloveCursor.Instance.PlayPress(slapLift, slapDuration);
        if (bodyRenderer != null) StartCoroutine(slapJiggle.Play(new Renderer[] { bodyRenderer }, hit.point, -hit.normal));
        if (rig != null) rig.PlaySlap();
    }

    // --- Collider'lar ---

    // Mesh'in her vertex'i en çok bağlı olduğu kemiğe sayılır, her kemiğin vertex'lerine kapsül (uzunsa) ya da küre
    // (yuvarlaksa) oturtulur; collider kemiğin child'ı olur, kemikle döner. Kalça (Root) kıç bölgesi.
    private void BuildColliders()
    {
        SkinnedMeshRenderer skin = null;
        foreach (SkinnedMeshRenderer candidate in GetComponentsInChildren<SkinnedMeshRenderer>())
            if (skin == null || candidate.sharedMesh.vertexCount > skin.sharedMesh.vertexCount) skin = candidate;
        bodyRenderer = skin;
        if (skin == null || skin.sharedMesh == null || skin.bones.Length == 0) return;
        if (!skin.sharedMesh.isReadable)
        {
            Debug.LogWarning("Slave: mesh okunamıyor; FBX import ayarlarında Model → Read/Write'ı aç. Kaba kutu collider kullanılıyor.", this);
            return;
        }

        var baked = new Mesh();
        skin.BakeMesh(baked, true);
        Vector3[] vertices = baked.vertices;
        BoneWeight[] weights = skin.sharedMesh.boneWeights;
        Destroy(baked);
        if (weights.Length != vertices.Length) return;

        Transform[] bones = skin.bones;
        var groups = new Dictionary<Transform, List<Vector3>>();
        Transform skinTransform = skin.transform;
        for (int i = 0; i < vertices.Length; i++)
        {
            Transform bone = bones[weights[i].boneIndex0];
            if (bone == null) continue;
            if (!groups.TryGetValue(bone, out List<Vector3> list)) groups[bone] = list = new List<Vector3>();
            list.Add(skinTransform.position + skinTransform.rotation * vertices[i]);
        }

        int built = 0;
        foreach (var pair in groups)
            if (pair.Value.Count >= 12 && FitCollider(pair.Key, pair.Value)) built++;

        // Kemik collider'ları kurulduysa kökteki kaba kutu kapanır (eldiven gövdenin içine girmesin)
        if (built > 0 && TryGetComponent(out BoxCollider box)) box.enabled = false;
        if (built > 0) AddRearZone(System.Array.Find(bones, b => b != null && string.Equals(b.name, "Root", System.StringComparison.OrdinalIgnoreCase)));
    }

    // Kıçın arkasında büyükçe bir küre: eldiven kıçın o yanındayken avuç çoğunlukla buna değer, şaplak rahat tutar
    // (kalça kemiğinin kendi collider'ı dar kalıyordu). Kalça kemiğinin child'ı: dört ayak pozunda da kıçla gider.
    private void AddRearZone(Transform hips)
    {
        if (hips == null || rearZoneRadius <= 0f) return;
        var zone = new GameObject("Rear Zone");
        zone.transform.SetParent(hips, false);
        zone.transform.position = transform.TransformPoint(gripLocalPosition + rearZoneOffset);
        var sphere = zone.AddComponent<SphereCollider>();
        sphere.radius = rearZoneRadius / Mathf.Max(hips.lossyScale.x, 0.0001f);
        zone.AddComponent<SlavePart>().Setup(this, true);
    }

    private bool FitCollider(Transform bone, List<Vector3> points)
    {
        // Ağırlık merkezi ve uzanma yönü (kovaryansın en büyük öz vektörü, kuvvet yinelemesiyle)
        Vector3 center = Vector3.zero;
        foreach (Vector3 p in points) center += p;
        center /= points.Count;

        float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (Vector3 p in points)
        {
            Vector3 d = p - center;
            xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
            yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
        }
        Vector3 axis = new Vector3(1f, 0.7f, 0.3f).normalized;
        for (int iteration = 0; iteration < 24; iteration++)
        {
            axis = new Vector3(xx * axis.x + xy * axis.y + xz * axis.z,
                               xy * axis.x + yy * axis.y + yz * axis.z,
                               xz * axis.x + yz * axis.y + zz * axis.z);
            if (axis.sqrMagnitude < 1e-12f) { axis = Vector3.up; break; }
            axis.Normalize();
        }

        // Eksen boyunca uzanma ve eksenden uzaklıklar
        float min = float.MaxValue, max = float.MinValue;
        var radial = new float[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 d = points[i] - center;
            float along = Vector3.Dot(d, axis);
            min = Mathf.Min(min, along);
            max = Mathf.Max(max, along);
            radial[i] = (d - axis * along).magnitude;
        }
        System.Array.Sort(radial);
        float radius = Mathf.Max(0.02f, radial[Mathf.Clamp(Mathf.RoundToInt(radial.Length * colliderFitPercentile) - 1, 0, radial.Length - 1)]);
        float length = max - min;
        Vector3 middlePoint = center + axis * ((min + max) * 0.5f);

        var part = new GameObject($"{bone.name} Collider");
        part.transform.SetParent(bone, false);
        part.transform.SetPositionAndRotation(middlePoint, Quaternion.LookRotation(axis, Mathf.Abs(Vector3.Dot(axis, transform.up)) > 0.95f ? transform.forward : transform.up));
        float scale = Mathf.Max(bone.lossyScale.x, 0.0001f);

        // Neredeyse yuvarlak (kafa gibi): küre; uzunsa kapsül
        if (length < radius * 2.6f)
        {
            var sphere = part.AddComponent<SphereCollider>();
            sphere.radius = Mathf.Max(radius, length * 0.5f) / scale;
        }
        else
        {
            var capsule = part.AddComponent<CapsuleCollider>();
            capsule.direction = 2; // yerel Z = eksen
            capsule.radius = radius / scale;
            capsule.height = Mathf.Max(length, radius * 2f) / scale;
        }

        part.AddComponent<SlavePart>().Setup(this, string.Equals(bone.name, "Root", System.StringComparison.OrdinalIgnoreCase));
        return true;
    }

    // Kafesten çıkıp ele gelir (sol tık basılı kaldıkça taşınır)
    public void TakeOutOfCage()
    {
        if (cage == null || held || InteractableController.Instance == null) return;
        InteractableController.Instance.BeginHoldWithLeft(this);
    }

    // Kafesin yanındaki en yakın durulabilir hücreye iner, sonra dolaşmaya döner
    private void LandBeside(Cage source)
    {
        Vector3 center = source.LocalCenter;
        Vector3Int start = Vector3Int.RoundToInt(source.transform.TransformPoint(new Vector3(center.x, 0f, center.z)));
        if (!TryFindLandingCell(start, out Vector3Int target))
        {
            Debug.LogWarning($"{name} has no free cell next to the cage: back inside", this);
            EnterCage(source, instant: false);
            return;
        }
        GridManager.Instance.TryMovePlaceable(this, target, GetFootprint());
        cell = target;
        landing = StartCoroutine(Land(target));
    }

    // Kafesten çıkar (eldivenle tutulunca, sol tıkla)
    private void LeaveCage()
    {
        if (cage == null) return;
        if (cage.Occupant == this) cage.SetOccupant(null);
        cage = null;
        transform.SetParent(null, true);
    }

    private void OnDestroy()
    {
        if (cage != null && cage.Occupant == this) cage.SetOccupant(null);
    }

    // start'a en yakın durulabilir hücre (2 hücre yarıçap, bir kat aşağı / yukarı)
    private bool TryFindLandingCell(Vector3Int start, out Vector3Int found)
    {
        found = start;
        float best = float.MaxValue;
        for (int y = 0; y >= -1; y--)
        for (int x = -2; x <= 2; x++)
        for (int z = -2; z <= 2; z++)
        {
            Vector3Int candidate = start + new Vector3Int(x, y, z);
            float distance = new Vector3(x, y * 0.5f, z).sqrMagnitude;
            if (distance >= best || !IsWalkable(candidate)) continue;
            best = distance;
            found = candidate;
        }
        if (best < float.MaxValue) return true;
        if (IsWalkable(start + Vector3Int.up)) { found = start + Vector3Int.up; return true; }
        return false;
    }

    // Hücreye iner ve doğrulur (yüzü kameraya dönük kalır), sonra dolaşmaya döner
    private System.Collections.IEnumerator Land(Vector3Int target)
    {
        Vector3 fromPosition = transform.position;
        Quaternion fromRotation = transform.rotation;
        Vector3 facing = Camera.main.transform.position - transform.position;
        facing.y = 0f;
        Quaternion toRotation = facing.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(facing.normalized, Vector3.up) : Quaternion.identity;

        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(landSeconds, 0.01f))
        {
            float eased = Mathf.SmoothStep(0f, 1f, t);
            transform.SetPositionAndRotation(Vector3.Lerp(fromPosition, target, eased), Quaternion.Slerp(fromRotation, toRotation, eased));
            yield return null;
        }
        transform.SetPositionAndRotation(target, toRotation);
        landing = null;
        ChangeState(new WaitState(this));
    }

    // Durabilir: altı zemin, kendi boyu kadar yukarısı boş (kendisi hariç)
    private bool IsWalkable(Vector3Int target)
    {
        if (!GridPathfinder.IsStandable(target, this)) return false;
        int height = GetFootprint().Size.y;
        for (int y = 1; y < height; y++)
        {
            if (!GridManager.Instance.TryGetCell(target + Vector3Int.up * y, out GridData data)) return false;
            if (data.Base != null || (data.Placeable != null && data.Placeable != this) || data.HasFine) return false;
        }
        return true;
    }

    // Yakında rastgele bir hücreye yol; birkaç deneme, bulamazsa null
    private List<Vector3Int> FindWanderPath()
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            Vector3Int goal = cell + new Vector3Int(Random.Range(-wanderRadius, wanderRadius + 1), Random.Range(-1, 2),
                                                    Random.Range(-wanderRadius, wanderRadius + 1));
            if (goal == cell || !IsWalkable(goal)) continue;
            List<Vector3Int> path = GridPathfinder.FindPath(cell, goal, IsWalkable, 400, this);
            if (path != null && path.Count > 0) return path;
        }
        return null;
    }

    // --- Durumlar ---

    private class WaitState : CreatureState<Slave>
    {
        private float duration;
        public WaitState(Slave owner) : base(owner) { }

        public override void Enter()
        {
            owner.CurrentSpeed = 0f;
            duration = Random.Range(owner.waitSeconds.x, owner.waitSeconds.y);
        }

        public override void Tick()
        {
            if (owner.StateTime < duration) return;
            List<Vector3Int> path = owner.FindWanderPath();
            if (path != null) owner.ChangeState(new WalkState(owner, path));
            else duration = owner.StateTime + 1f; // yer yok: biraz sonra tekrar dene
        }
    }

    private class WalkState : CreatureState<Slave>
    {
        private readonly List<Vector3Int> path;
        private Vector3 from, to;
        private float progress = 1f, stepDuration = 1f;

        public WalkState(Slave owner, List<Vector3Int> path) : base(owner) { this.path = path; }

        public override void Tick()
        {
            Transform t = owner.transform;
            if (progress >= 1f && !BeginStep())
            {
                owner.ChangeState(new WaitState(owner));
                return;
            }

            progress = Mathf.Min(1f, progress + Time.deltaTime / stepDuration);
            t.position = Vector3.Lerp(from, to, progress);
            owner.CurrentSpeed = owner.walkSpeed;

            Vector3 facing = to - from;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(facing.normalized, Vector3.up);
                t.rotation = Quaternion.Slerp(t.rotation, target, 1f - Mathf.Exp(-owner.turnSharpness * Time.deltaTime));
            }
        }

        // Sıradaki hücreye geç: yol bittiyse ya da arada kapandıysa (yeni obje kondu) false
        private bool BeginStep()
        {
            if (path.Count == 0) return false;
            Vector3Int next = path[0];
            path.RemoveAt(0);
            if (!owner.IsWalkable(next) || !GridManager.Instance.TryMovePlaceable(owner, next, owner.GetFootprint())) return false;

            from = owner.transform.position;
            to = next;
            owner.cell = next;
            stepDuration = Mathf.Max(0.01f, Vector3.Distance(from, to) / Mathf.Max(owner.walkSpeed, 0.01f));
            progress = 0f;
            return true;
        }
    }
}
