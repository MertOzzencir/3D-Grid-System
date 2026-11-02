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
public class Slave : Creature, IInteractable, IGloveGrip
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
    [Tooltip("Bırakınca hücreye inip ayağa kalkma süresi (saniye)")]
    [SerializeField] private float landSeconds = 0.35f;

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
        if (rig != null) rig.Tick(CurrentSpeed, held, holdVelocity);
    }

    // --- Taşıma (IInteractable, IGloveGrip) ---

    public Transform GripPoint => gripPoint;
    public float GripCurl => gripCurl;

    public void InteractContractBeginnig()
    {
        if (landing != null) { StopCoroutine(landing); landing = null; }
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
        if (TryMouseGround(out Vector3 ground)) target = ground + Vector3.up * carryHeight;
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
        Vector3Int start = TryMouseGround(out Vector3 ground) ? Vector3Int.RoundToInt(ground + Vector3.up * 0.5f) : cell;
        if (!TryFindLandingCell(start, out Vector3Int target) && !TryFindLandingCell(cell, out target))
        {
            Debug.LogWarning($"{name} could not find a cell to land on", this);
            target = cell;
        }
        GridManager.Instance.TryMovePlaceable(this, target, GetFootprint());
        cell = target;
        landing = StartCoroutine(Land(target));
    }

    // Mouse'un altındaki ilk yüzey (kendisi hariç)
    private bool TryMouseGround(out Vector3 point)
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        int count = Physics.RaycastNonAlloc(ray, hitBuffer, 500f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        point = default;
        for (int i = 0; i < count; i++)
        {
            if (hitBuffer[i].transform.IsChildOf(transform) || hitBuffer[i].distance >= best) continue;
            best = hitBuffer[i].distance;
            point = hitBuffer[i].point;
        }
        return best < float.MaxValue;
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
