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

    [Header("Su maskesi (su botun içinden görünmesin)")]
    [Tooltip("Görünmez yarım küre shader'ı (Village/Depth Mask). Referansla tutulur ki build'e girsin.")]
    [SerializeField] private Shader maskShader;
    [Tooltip("Yarım kürenin üst kapağının genişliği, gövdenin dış genişliğine oranla. İç kenardan geniş, dış kenardan dar " +
             "olmalı (kenarın tepesine denk gelsin): küçükse köşelerde su görünür, büyükse botun yanındaki su kaybolur.")]
    [SerializeField, Range(0.6f, 1f)] private float maskFit = 0.9f;
    [Tooltip("Yarım kürenin üst kapaktan aşağı derinliği, gövdenin yüksekliğine oranla (altı umursanmaz)")]
    [SerializeField, Range(0.3f, 1.5f)] private float maskDepth = 0.9f;
    [Tooltip("Scene görünümünde maskeyi (sarı) ve gövdeyi (mavi tel kafes) göster; Play'e girmeden de, prefab modunda da")]
    [SerializeField] private bool showMaskGizmo = true;

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
    private Mesh maskMesh; // gizmo için
    private Transform maskTransform;
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
        CreateWaterMask(model);
    }

    // Botun içini dolduran görünmez yarım küre (üstü düz kapak, altı kase): sudan hemen önce derinlik yazar, arkasındaki
    // (botun içindeki) su çizilmez. Ölçüleri gövdenin sınırlarından: kapak gövdenin en üstünde (kenarın tepesi),
    // genişliği dış ölçünün maskFit katı (kenarın tepesine denk gelir: köşeler dahil bütün ağız kapanır, kenarın altına
    // taşmaz). Kasenin gövdenin altından taşan kısmı sorun değil (orada su kasenin önünde). Modelin child'ı: bot
    // sallandıkça maske de sallanır.
    private void CreateWaterMask(Transform model)
    {
        if (maskShader == null)
        {
            Debug.LogWarning("Boat: Mask Shader (Village/Depth Mask) atanmamış; su botun içinden görünebilir.", this);
            return;
        }

        if (!TryBuildMask(model, leftOar, rightOar, maskFit, maskDepth, out Vector3 top, out maskMesh, out _, out _)) return;

        var mask = new GameObject("Water Mask");
        maskTransform = mask.transform;
        maskTransform.SetParent(model, false);
        maskTransform.localPosition = top;
        mask.AddComponent<MeshFilter>().sharedMesh = maskMesh;
        var renderer = mask.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = new Material(maskShader);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // Maskenin ölçüsü (modelin uzayında): gövde = küreklerin dışındaki en büyük mesh; kapak gövdenin en üstünde,
    // genişliği dış ölçünün fit katı, derinliği gövde yüksekliğinin depthRatio katı. Hem çalışırken (kurulan model)
    // hem editörde (modelin asset'i, gizmo için) kullanılır.
    private static bool TryBuildMask(Transform model, Transform skipA, Transform skipB, float fit, float depthRatio,
                                     out Vector3 top, out Mesh mesh, out MeshFilter hull, out Matrix4x4 hullToModel)
    {
        top = default;
        mesh = null;
        hull = null;
        hullToModel = Matrix4x4.identity;
        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.transform == skipA || filter.transform == skipB) continue;
            if (hull == null || filter.sharedMesh.vertexCount > hull.sharedMesh.vertexCount) hull = filter;
        }
        if (hull == null) return false;

        Bounds local = hull.sharedMesh.bounds;
        hullToModel = model.worldToLocalMatrix * hull.transform.localToWorldMatrix;
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 p = hullToModel.MultiplyPoint3x4(new Vector3(
                (corner & 1) == 0 ? local.min.x : local.max.x,
                (corner & 2) == 0 ? local.min.y : local.max.y,
                (corner & 4) == 0 ? local.min.z : local.max.z));
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        Vector3 center = (min + max) * 0.5f;
        top = new Vector3(center.x, max.y, center.z);
        mesh = HalfEllipsoid((max.x - min.x) * 0.5f * fit, (max.z - min.z) * 0.5f * fit, (max.y - min.y) * depthRatio);
        return true;
    }

    // Kapalı yarım elipsoit: y = 0'da düz kapak, aşağı doğru kase (en derin nokta -depth). Yüzlerin yönü önemsiz (Cull Off).
    private static Mesh HalfEllipsoid(float radiusX, float radiusZ, float depth)
    {
        const int segments = 40, rings = 10;
        var vertices = new System.Collections.Generic.List<Vector3> { Vector3.zero }; // 0: kapağın ortası
        var triangles = new System.Collections.Generic.List<int>();

        // Halkalar: 0 = kenar (y = 0), rings = en alt
        for (int ring = 0; ring <= rings; ring++)
        {
            float angle = ring / (float)rings * Mathf.PI * 0.5f;
            float scale = Mathf.Cos(angle), y = -Mathf.Sin(angle) * depth;
            for (int i = 0; i < segments; i++)
            {
                float around = i / (float)segments * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(around) * radiusX * scale, y, Mathf.Sin(around) * radiusZ * scale));
            }
        }

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            // Kapak
            triangles.Add(0); triangles.Add(1 + next); triangles.Add(1 + i);
            // Kase
            for (int ring = 0; ring < rings; ring++)
            {
                int a = 1 + ring * segments + i, b = 1 + ring * segments + next;
                int c = a + segments, d = b + segments;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(b); triangles.Add(d); triangles.Add(c);
            }
        }

        var mesh = new Mesh { name = "Boat Water Mask" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals(); // Gizmos.DrawMesh normal ister (çizimde kullanılmaz)
        mesh.RecalculateBounds();
        return mesh;
    }

#if UNITY_EDITOR
    private Mesh editorMaskMesh;
    private Vector3 editorMaskTop;
    private MeshFilter editorHull;
    private Matrix4x4 editorHullToModel;

    private void OnValidate() => editorMaskMesh = null; // ayar değişince gizmo yeniden ölçülür

    // Maske Scene görünümünde: sarı yarı saydam + tel kafes; gövde mavi tel kafes (maskenin gövdeye nasıl oturduğu).
    // Play'de kurulan gerçek maske; editörde (prefab modunda da) modelin asset'inden aynı hesapla.
    private void OnDrawGizmos()
    {
        if (!showMaskGizmo) return;

        Mesh mask;
        Matrix4x4 maskMatrix, hullMatrix = Matrix4x4.identity;
        MeshFilter hull = null;
        if (Application.isPlaying)
        {
            if (maskMesh == null || maskTransform == null) return;
            mask = maskMesh;
            maskMatrix = maskTransform.localToWorldMatrix;
        }
        else
        {
            if (modelPrefab == null) return;
            if (editorMaskMesh == null)
            {
                Transform asset = modelPrefab.transform;
                TryBuildMask(asset, FindQuiet(asset, leftOarName), FindQuiet(asset, rightOarName), maskFit, maskDepth,
                             out editorMaskTop, out editorMaskMesh, out editorHull, out editorHullToModel);
                if (editorMaskMesh == null) return;
                editorMaskMesh.hideFlags = HideFlags.HideAndDontSave; // sahneye kaydedilmesin
            }
            // Çalışırken kurulan hiyerarşinin aynısı: kök → Visual (su çizgisi) → model (dönüş, ölçek)
            Matrix4x4 model = transform.localToWorldMatrix
                              * Matrix4x4.Translate(Vector3.up * (0.5f + waterline))
                              * Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(modelRotation), Vector3.one * modelScale);
            mask = editorMaskMesh;
            maskMatrix = model * Matrix4x4.Translate(editorMaskTop);
            hull = editorHull;
            hullMatrix = model * editorHullToModel;
        }

        Gizmos.matrix = maskMatrix;
        Gizmos.color = new Color(1f, 0.9f, 0f, 0.25f);
        Gizmos.DrawMesh(mask);
        Gizmos.color = new Color(1f, 0.9f, 0f, 0.9f);
        Gizmos.DrawWireMesh(mask);

        if (hull != null && hull.sharedMesh != null)
        {
            Gizmos.matrix = hullMatrix;
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.35f);
            Gizmos.DrawWireMesh(hull.sharedMesh);
        }
        Gizmos.matrix = Matrix4x4.identity;
    }

    private static Transform FindQuiet(Transform parent, string objectName)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == objectName) return t;
        return null;
    }
#endif

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
