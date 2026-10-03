using UnityEngine;

// Su üstünde giden 2×1 bot (yürüyen placeable, kedi gibi): sadece suya konur (CanOccupy), su hücrelerinde
// GridWalker ile gider (Medium = Water); grid'deki yerini GridWalker her adımda günceller.
// Binme: eldiven kıyıdayken (mouse suda) botun yanındaysa biner, koltuğa (Seat) oturur. Binmişken WASD botu kameranın
// açısına göre hücre hücre sürer (W ileri, S geri, A/D sola/sağa; yol bulma yok), kamera botu takip eder.
// WASD ile gidilecek hücre kara (base) ise eldiven oraya iner, mouse imleci de oraya taşınır. Binerken oyuncu girişi
// kilitli (şimdilik sadece gezme).
// Görsel: model kodla kurulur; giderken kürekler yol başına çekilir, bot suda hafif inip kalkar, yalpalar, dönüşte yatar.
[RequireComponent(typeof(GridWalker))]
[DefaultExecutionOrder(10)] // GridWalker'dan (0) sonra: görsel ve koltuk bu karenin konumuyla, eldiven (LateUpdate) okumadan önce
public class Boat : GridPlaceable, ISaveState, IGlovePassThrough
{
    [Header("Model")]
    [Tooltip("Bot modeli (FBX): gövde + iki kürek (kürek origin'leri tutturuldukları yerde)")]
    [SerializeField] private GameObject modelPrefab;
    [SerializeField] private Vector3 modelRotation = Vector3.zero;
    [SerializeField] private float modelScale = 1f;
    [Tooltip("Modelin origin'i su seviyesinin bu kadar üstünde (artı = daha az batar)")]
    [SerializeField] private float waterline = 0.15f;
    [SerializeField] private string leftOarName = "KUREK-LEFT";
    [SerializeField] private string rightOarName = "KUREK-RIGHT";
    [Tooltip("Eldivenin oturduğu yer, modelin uzayında (botun tabanı)")]
    [SerializeField] private Vector3 seatOffset = new Vector3(0f, -0.2f, 0f);

    [Header("Su efektleri")]
    [Tooltip("Köpük izi ve sıçrama shader'ı (Village/Water Foam). Referansla tutulur ki build'e girsin.")]
    [SerializeField] private Shader effectShader;

    [SerializeField] private BoatWakeFx waterFx = new BoatWakeFx();

    [Header("Binme / inme")]
    [Tooltip("Eldiven kıyıdayken (mouse suda) botun bir hücresine yataydan bu kadar yakınsa biner")]
    [SerializeField] private float boardDistance = 1.3f;
    [Tooltip("İndikten sonra tekrar binmek için en az bekleme (saniye)")]
    [SerializeField] private float boardCooldown = 0.8f;

    [Header("Hız")]
    [Tooltip("Botun hızı (birim/sn); GridWalker'daki hızın yerine geçer")]
    [SerializeField] private float moveSpeed = 3f;
    [Tooltip("Yerinde geri dönme süresi (saniye)")]
    [SerializeField] private float turnAroundSeconds = 0.3f;

    [Header("Kürek")]
    [Tooltip("Bir kürek çekişinde alınan yol (birim)")]
    [SerializeField] private float strokeLength = 1.2f;
    [Tooltip("Küreğin öne-arkaya süpürmesi (derece)")]
    [SerializeField] private float oarSweep = 28f;
    [Tooltip("Geri dönüşte küreğin sudan kalkması (derece)")]
    [SerializeField] private float oarLift = 14f;

    [Header("Suda sallanma")]
    [SerializeField] private float bobAmount = 0.025f;
    [Tooltip("Sallanma hızı (salınım/sn)")]
    [SerializeField] private float bobSpeed = 0.5f;
    [SerializeField] private float rockAngle = 2.5f;
    [Tooltip("Dönüş hızı (derece/sn) başına yana yatma (derece)")]
    [SerializeField] private float turnLean = 0.09f;
    [Tooltip("Giderken burnun kalkması (derece)")]
    [SerializeField] private float surgePitch = 2f;
    [Tooltip("Her kürek çekişinde botun ileri atılması (birim) ve burnunun kalkması (derece); dönüşte geri süzülür")]
    [SerializeField] private float strokeLunge = 0.08f;
    [SerializeField] private float strokePitch = 3f;

    private GridWalker walker;
    private GloveCursor glove;
    private Transform visual, seat, leftOar, rightOar;
    private Quaternion leftOarRest, rightOarRest;
    private Vector3 visualRest;
    private Vector3 lastPosition;
    private float lastYaw, rowWeight, rowPhase, lean;
    private bool riding;
    private float lastRideEnd = float.MinValue;
    private WaterHullClip.Entry hullClip; // su bu botun içinde çizilmesin
    private Vector3Int? restoredHeadOffset; // kayıttan: arka hücreden ön hücreye

    public bool IsRidden => riding;

    private GloveCursor Glove => glove != null ? glove : glove = FindFirstObjectByType<GloveCursor>();

    private void Awake()
    {
        walker = GetComponent<GridWalker>();
        walker.WalkMedium = GridWalker.Medium.Water; // bot hep suda gider
        walker.Speed = moveSpeed;
        walker.TurnAroundDuration = turnAroundSeconds;
        if (modelPrefab != null) BuildModel();
    }

    // --- Grid ve kayıt ---

    // Sadece su hücreleri
    public override bool CanOccupy(Vector3Int worldCell) => GridManager.Instance != null && GridManager.Instance.IsWater(worldCell);

    // Arka hücre (origin) + ön hücre. Yerleşikken yürüyüşten, yerleşmeden önce kayıttan ya da rotasyondan (Deg0'da ön +Z).
    public override GridFootprint GetFootprint()
    {
        Vector3Int front = walker != null && walker.IsPlaced ? walker.HeadCell - walker.BodyCell
                         : restoredHeadOffset ?? GridMaskRotator.RotateOffset(Vector3Int.forward, Rotation);
        return new GridFootprint(new[] { Vector3Int.zero, front });
    }

    public override void OnPlaced(Vector3Int origin)
    {
        base.OnPlaced(origin);
        walker.Place(origin + PlacedFootprint.FilledCells()[1], origin);
        lastPosition = transform.position;
        lastYaw = transform.eulerAngles.y;
    }

    [System.Serializable]
    private class SaveState
    {
        public Vector3Int front; // arka hücreden ön hücreye
    }

    public string CaptureState() => JsonUtility.ToJson(new SaveState
    {
        front = walker.IsPlaced ? walker.HeadCell - walker.BodyCell : Vector3Int.forward,
    });

    public void RestoreState(string state)
    {
        SaveState saved = JsonUtility.FromJson<SaveState>(state);
        if (saved != null && saved.front != Vector3Int.zero) restoredHeadOffset = saved.front;
    }

    // --- Binme, sürme, inme ---

    private void Update()
    {
        if (!walker.IsPlaced)
        {
            // Grid'e konan bot OnPlaced'da yerini alır; sahneye elle konmuşsa (test) yakındaki suya yerleşir
            if (PlacedFootprint != null || !walker.TryPlaceNear(transform.position)) return;
        }

        if (riding) Steer();
        else TryBoard();
        Animate(Time.deltaTime);
    }

    private void TryBoard()
    {
        GloveCursor g = Glove;
        if (g == null || g.IsRiding || g.IsCaptured || !g.IsAtShore) return;
        if (Time.time - lastRideEnd < boardCooldown || DistanceToBoat(g.SurfacePoint) > boardDistance) return;

        riding = true;
        walker.Stop();
        InputManager.SetLocked(this, true);
        CameraController.Follow(transform);
        g.BeginRide(seat != null ? seat : transform);
    }

    private void Steer()
    {
        GloveCursor g = Glove;
        if (g == null || !g.IsRiding)
        {
            EndRide();
            return;
        }

        // WASD: tuş bırakılınca o anki adım biter ve durur. Basılıyken adım bitmeden bir sonraki sıraya girer (kesintisiz).
        Vector2 input = InputManager.MovementVectorNormalized();
        if (input.sqrMagnitude < 0.01f || walker.HasQueuedSteps) return;

        Vector3Int next = walker.HeadCell + GridDirection(input);
        if (next == walker.BodyCell || walker.IsWalkable(next))
        {
            walker.StepTo(next); // gövdenin hücresi = yerinde dön
            return;
        }

        // Gidilecek yer kara: eldiven o karonun üstüne iner (mouse da oraya taşınır), bot kalır
        if (GridManager.Instance != null && GridManager.Instance.TryGetTopBase(next.x, next.z, out GridData land))
            EndRide(land.WorldPosition + Vector3.up * 0.5f);
    }

    // Kameraya göre girdi → grid yönü (kamera 90° adımlarla döndüğü için eksenler grid'e denk).
    // Çapraz basılıysa (W+D) bot zaten o yönlerden birine gidiyorsa ona devam eder, yoksa baskın eksen.
    private Vector3Int GridDirection(Vector2 input)
    {
        Transform cam = Camera.main.transform;
        Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
        Vector3 world = forward * input.y + right * input.x;

        var alongX = new Vector3Int(world.x > 0f ? 1 : -1, 0, 0);
        var alongZ = new Vector3Int(0, 0, world.z > 0f ? 1 : -1);
        if (Mathf.Abs(world.x) > 0.3f && Mathf.Abs(world.z) > 0.3f)
        {
            Vector3Int facing = walker.HeadCell - walker.BodyCell;
            facing.y = 0;
            if (facing == alongX) return alongX;
            if (facing == alongZ) return alongZ;
        }
        return Mathf.Abs(world.x) >= Mathf.Abs(world.z) ? alongX : alongZ;
    }

    private void EndRide() => EndRide(null);

    // landing: eldivenin ineceği nokta (karonun üstü); yoksa eldiven mouse'un olduğu yere döner
    private void EndRide(Vector3? landing)
    {
        if (!riding) return;
        riding = false;
        lastRideEnd = Time.time;
        walker.Stop();
        InputManager.SetLocked(this, false);
        CameraController.Follow(null);
        if (Glove == null) return;
        if (landing.HasValue) Glove.EndRideAt(landing.Value);
        else Glove.EndRide();
    }

    private void OnDisable() => EndRide(); // binilmişken silinirse eldiven ve giriş takılı kalmasın

    // Noktanın botun iki hücresinden en yakınına yatay uzaklığı
    private float DistanceToBoat(Vector3 point)
    {
        Vector2 p = new Vector2(point.x, point.z);
        float head = Vector2.Distance(p, new Vector2(walker.HeadCell.x, walker.HeadCell.z));
        float body = Vector2.Distance(p, new Vector2(walker.BodyCell.x, walker.BodyCell.z));
        return Mathf.Min(head, body);
    }

    // --- Görsel ---

    private void BuildModel()
    {
        visual = new GameObject("Visual").transform;
        visual.SetParent(transform, false);
        // Kök hücrenin tabanında (GridWalker.FeetPosition), su seviyesi en alt kat hücresinin ortası
        visualRest = Vector3.up * (0.5f + waterline);
        visual.localPosition = visualRest;

        Transform model = Instantiate(modelPrefab, visual).transform;
        model.localPosition = Vector3.zero;
        model.localRotation = Quaternion.Euler(modelRotation);
        model.localScale = Vector3.one * modelScale;

        leftOar = Find(model, leftOarName);
        rightOar = Find(model, rightOarName);
        if (leftOar != null) leftOarRest = leftOar.localRotation;
        if (rightOar != null) rightOarRest = rightOar.localRotation;

        seat = new GameObject("Seat").transform;
        seat.SetParent(model, false);
        seat.localPosition = seatOffset;

        // Tıklama / build mode'da silme için gövdeyi saran kutu (kürekler hariç)
        bool any = false;
        Bounds bounds = default;
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
        {
            if (renderer.transform == leftOar || renderer.transform == rightOar) continue;
            if (!any) bounds = renderer.bounds;
            else bounds.Encapsulate(renderer.bounds);
            any = true;
        }
        if (any)
        {
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = transform.InverseTransformPoint(bounds.center);
            box.size = bounds.size;
        }

        // Su seviyesi kökün 0.5 üstünde (kök hücrenin tabanında, su en alt kat hücresinin ortasında)
        waterFx.Setup(transform, effectShader, 0.5f);
        RegisterWaterClip(model);
    }

    // Su bu botun içinde çizilmesin: gövdenin profili mesh'ten bir kez ölçülür (su seviyesinin biraz altından kenara
    // kadar katmanlar), her kare botun konumuyla su shader'ına gider (WaterHullClip). Ayar gerekmez.
    // Gövde mesh'i okunabilir olmalı (bot.fbx → Read/Write).
    private void RegisterWaterClip(Transform model)
    {
        MeshFilter hull = null;
        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null || filter.transform == leftOar || filter.transform == rightOar) continue;
            if (hull == null || filter.sharedMesh.vertexCount > hull.sharedMesh.vertexCount) hull = filter;
        }
        if (hull == null) return;
        if (!hull.sharedMesh.isReadable)
        {
            Debug.LogWarning("Boat: gövde mesh'i okunamıyor (bot.fbx → Model → Read/Write); su botun içinden görünebilir.", this);
            return;
        }

        // Katmanlar modelin uzayında: su seviyesi (-waterline) biraz altından (dalga çukuru, bot kalkınca) yukarı
        float scale = Mathf.Max(modelScale, 0.0001f);
        float water = -waterline / scale;
        float bottom = water - 0.12f / scale;
        float step = 0.32f / scale / (WaterHullClip.Levels - 1);
        Matrix4x4 toModel = model.worldToLocalMatrix * hull.transform.localToWorldMatrix;
        float[] profile = WaterHullClip.MeasureProfile(hull.sharedMesh, toModel, bottom, step);
        hullClip = WaterHullClip.Register(model, profile, bottom, step);
    }

    private void LateUpdate() => WaterHullClip.Upload(); // bot sallandıktan sonra (Update'te), çizimden önce

    private void OnDestroy() => WaterHullClip.Unregister(hullClip);

    private static Transform Find(Transform parent, string objectName)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == objectName) return t;
        Debug.LogWarning($"Boat: modelde '{objectName}' bulunamadı", parent);
        return null;
    }

    private void Animate(float dt)
    {
        if (visual == null) return;

        Vector3 delta = transform.position - lastPosition;
        lastPosition = transform.position;
        float distance = new Vector2(delta.x, delta.z).magnitude;
        float yaw = transform.eulerAngles.y;
        float yawRate = Mathf.DeltaAngle(lastYaw, yaw) / Mathf.Max(dt, 0.0001f);
        lastYaw = yaw;

        rowWeight = Mathf.MoveTowards(rowWeight, distance > 0.0001f ? 1f : 0f, dt * 5f);
        float previousPhase = rowPhase;
        rowPhase += distance / Mathf.Max(strokeLength, 0.01f);

        // Kürek suya giriyor (tur başı) / çıkıyor (yarım tur): sıçrama
        if (rowWeight > 0.5f)
        {
            bool entered = Mathf.Floor(rowPhase) > Mathf.Floor(previousPhase);
            bool exited = Mathf.Floor(rowPhase - 0.5f) > Mathf.Floor(previousPhase - 0.5f);
            if (entered || exited)
            {
                if (leftOar != null) waterFx.OarSplash(leftOar.position, -1f, entered);
                if (rightOar != null) waterFx.OarSplash(rightOar.position, 1f, entered);
            }
        }
        waterFx.Tick(dt, rowWeight > 0.3f);
        lean = Mathf.Lerp(lean, Mathf.Clamp(-yawRate * turnLean, -14f, 14f), 1f - Mathf.Exp(-6f * dt));

        // Kürek çekişinin itişi: çekiş yarısında bot ileri atılır, burnu kalkar; dönüş yarısında geri süzülür
        float stroke = Mathf.Repeat(rowPhase, 1f);
        float drive = (stroke < 0.5f ? Mathf.Sin(stroke / 0.5f * Mathf.PI) : -0.35f * Mathf.Sin((stroke - 0.5f) / 0.5f * Mathf.PI))
                      * Mathf.SmoothStep(0f, 1f, rowWeight);

        // Suda inip kalkma ve yalpalama (farklı hızlarda: tekrar eden bir döngü gibi görünmesin)
        float t = Time.time * bobSpeed * Mathf.PI * 2f;
        visual.localPosition = visualRest + Vector3.up * (Mathf.Sin(t) * bobAmount) + Vector3.forward * (drive * strokeLunge);
        float roll = Mathf.Sin(t * 0.8f) * rockAngle + lean;
        float pitch = Mathf.Sin(t * 0.6f + 1f) * rockAngle * 0.6f - surgePitch * rowWeight - strokePitch * Mathf.Max(0f, drive); // eksi = burun yukarı
        visual.localRotation = Quaternion.Euler(pitch, 0f, roll);

        AnimateOar(leftOar, leftOarRest, -1f);
        AnimateOar(rightOar, rightOarRest, 1f);
    }

    // Kürek çekişi: ilk yarı kürek suda önden arkaya süpürür (çekiş), ikinci yarı sudan kalkıp öne döner.
    // Dönüşler modelin eksenlerinde (modelin uzayında yukarı ve ileri); sol kürek aynalanır (side = -1).
    private void AnimateOar(Transform oar, Quaternion rest, float side)
    {
        if (oar == null) return;
        float s = Mathf.Repeat(rowPhase, 1f);
        float sweep, lift;
        if (s < 0.5f)
        {
            sweep = Mathf.Lerp(-oarSweep, oarSweep, Mathf.SmoothStep(0f, 1f, s / 0.5f));
            lift = 0f;
        }
        else
        {
            float k = (s - 0.5f) / 0.5f;
            sweep = Mathf.Lerp(oarSweep, -oarSweep, Mathf.SmoothStep(0f, 1f, k));
            lift = Mathf.Sin(k * Mathf.PI) * oarLift;
        }
        float w = Mathf.SmoothStep(0f, 1f, rowWeight);
        // Sağ kürekte yukarı ekseni etrafında artı açı ucu geriye, ileri ekseni etrafında artı açı ucu yukarı götürür
        oar.localRotation = Quaternion.AngleAxis(side * sweep * w, Vector3.up)
                          * Quaternion.AngleAxis(side * lift * w, Vector3.forward)
                          * rest;
    }
}
