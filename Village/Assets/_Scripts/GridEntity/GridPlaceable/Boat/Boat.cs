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
    [Tooltip("Görünmez maske shader'ı (Village/Depth Mask). Referansla tutulur ki build'e girsin.")]
    [SerializeField] private Shader maskShader;
    [Tooltip("Blender'da modellenen maske (FBX): botun içini kenarın tepesine kadar dolduran kapalı bir mesh. Botla aynı " +
             "origin'de (0,0,0), transform'lar uygulanmış, botla aynı export ayarlarıyla ayrı bir FBX. Botun modelinin " +
             "içine aynı yere konur, görünmez olur. Boşsa maske gövdeden otomatik ölçülür.")]
    [SerializeField] private GameObject maskModel;
    [Tooltip("Maske iç duvardan bu kadar içeride kalır (modelin birimi). Normalde dokunma.")]
    [SerializeField] private float maskInset = 0.01f;
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

    // Botun içini dolduran görünmez maske: sudan hemen önce derinlik yazar, arkasındaki (botun içindeki) su çizilmez.
    // Varsa Blender'da modellenen maske (maskModel) kullanılır: botun modelinin içine aynı yere konur, bütün
    // renderer'larına maske malzemesi verilir. Yoksa şekli gövde mesh'inden örneklenir (TryBuildMask; bot.fbx Read/Write).
    // Modelin child'ı: bot sallandıkça maske de sallanır.
    private void CreateWaterMask(Transform model)
    {
        if (maskShader == null)
        {
            Debug.LogWarning("Boat: Mask Shader (Village/Depth Mask) atanmamış; su botun içinden görünebilir.", this);
            return;
        }

        var material = new Material(maskShader);
        if (maskModel != null)
        {
            maskTransform = Instantiate(maskModel, model).transform;
            maskTransform.name = "Water Mask";
            maskTransform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            maskTransform.localScale = Vector3.one;
            foreach (Collider collider in maskTransform.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            foreach (Renderer maskRenderer in maskTransform.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new Material[maskRenderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                maskRenderer.sharedMaterials = materials;
                maskRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                maskRenderer.receiveShadows = false;
            }
            return;
        }

        if (!TryBuildMask(model, leftOar, rightOar, MaskBottom, maskInset, out maskMesh, out _, out _))
        {
            Debug.LogWarning("Boat: su maskesi kurulamadı (gövde mesh'i okunabilir mi? bot.fbx → Read/Write).", this);
            return;
        }

        var mask = new GameObject("Water Mask");
        maskTransform = mask.transform;
        maskTransform.SetParent(model, false);
        mask.AddComponent<MeshFilter>().sharedMesh = maskMesh;
        var renderer = mask.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private const int MaskAutoSectors = 64;

    // Maskenin en altı (modelin uzayında): su seviyesinin epey altı (bot yalpalayınca uçlarda su aşağı iner); altı umursanmaz
    private float MaskBottom => (-waterline - 0.3f) / Mathf.Max(modelScale, 0.0001f);

    // Gövdenin içini kalıp gibi dolduran maske (modelin uzayında). Gövde = küreklerin dışındaki en büyük mesh.
    //   Etrafı 64 açı dilimine, yüksekliği 16 katmana bölünür. Her dilim/katmanda merkeze en yakın DUVAR vertex'i
    //   (normali yataya yakın; döşeme ve kenarın tepesi gibi yukarı bakanlar hariç) = iç duvar (döşemenin altında dış duvar).
    //   Her dilimde kenarın tepe yüksekliği ayrıca alınır: üst halka kenarı takip eder (uçlarda kalkık, ortada alçak).
    //   Halkalar duvardan inset kadar içeride; üstü kapak (merkezden yelpaze), altı kapalı. Boş hücreler komşuların
    //   küçüğüyle dolar (hep içeride kalınır).
    private static bool TryBuildMask(Transform model, Transform skipA, Transform skipB, float bottom, float inset,
                                     out Mesh mesh, out MeshFilter hull, out Matrix4x4 hullToModel)
    {
        const int sectors = MaskAutoSectors, levels = 16;
        mesh = null;
        hull = null;
        hullToModel = Matrix4x4.identity;
        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.transform == skipA || filter.transform == skipB) continue;
            if (hull == null || filter.sharedMesh.vertexCount > hull.sharedMesh.vertexCount) hull = filter;
        }
        if (hull == null || !hull.sharedMesh.isReadable) return false;

        hullToModel = model.worldToLocalMatrix * hull.transform.localToWorldMatrix;
        Vector3[] source = hull.sharedMesh.vertices;
        Vector3[] sourceNormals = hull.sharedMesh.normals;
        bool hasNormals = sourceNormals != null && sourceNormals.Length == source.Length;

        // Vertex'ler modelin uzayında; merkez = sınırların ortası
        var points = new Vector3[source.Length];
        var isWall = new bool[source.Length];
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        for (int i = 0; i < source.Length; i++)
        {
            points[i] = hullToModel.MultiplyPoint3x4(source[i]);
            isWall[i] = !hasNormals || Mathf.Abs(hullToModel.MultiplyVector(sourceNormals[i]).normalized.y) < 0.7f;
            min = Vector3.Min(min, points[i]);
            max = Vector3.Max(max, points[i]);
        }
        Vector2 center = new Vector2((min.x + max.x) * 0.5f, (min.z + max.z) * 0.5f);
        bottom = Mathf.Clamp(bottom, min.y, max.y - 0.01f);

        int SectorOf(Vector3 p) =>
            Mathf.FloorToInt(Mathf.Repeat(Mathf.Atan2(p.z - center.y, p.x - center.x) / (Mathf.PI * 2f), 1f) * sectors) % sectors;
        float RadiusOf(Vector3 p) => new Vector2(p.x - center.x, p.z - center.y).magnitude;

        // Her dilimde kenarın tepesi
        var rimTop = new float[sectors];
        for (int s = 0; s < sectors; s++) rimTop[s] = float.NegativeInfinity;
        foreach (Vector3 p in points)
        {
            int s = SectorOf(p);
            rimTop[s] = Mathf.Max(rimTop[s], p.y);
        }
        FillGaps(rimTop, float.NegativeInfinity, sectors, Mathf.Min, max.y);

        // Yükseklik katmanları (en üst = en yüksek kenar) ve her katmanda iç duvar uzaklığı; ayrıca her dilimin kenar
        // tepesindeki iç kenar
        float step = (max.y - bottom) / (levels - 1);
        float band = step * 0.75f;
        var radius = new float[levels, sectors];
        var topRadius = new float[sectors];
        for (int l = 0; l < levels; l++)
            for (int s = 0; s < sectors; s++) radius[l, s] = float.PositiveInfinity;
        for (int s = 0; s < sectors; s++) topRadius[s] = float.PositiveInfinity;

        for (int i = 0; i < points.Length; i++)
        {
            if (!isWall[i]) continue;
            Vector3 p = points[i];
            int s = SectorOf(p);
            float r = RadiusOf(p);
            if (Mathf.Abs(p.y - rimTop[s]) <= band) topRadius[s] = Mathf.Min(topRadius[s], r);
            int first = Mathf.Max(0, Mathf.CeilToInt((max.y - p.y - band) / step));
            int last = Mathf.Min(levels - 1, Mathf.FloorToInt((max.y - p.y + band) / step));
            for (int l = first; l <= last; l++) radius[l, s] = Mathf.Min(radius[l, s], r);
        }
        FillGaps(topRadius, float.PositiveInfinity, sectors, Mathf.Min, 0f);
        for (int l = 0; l < levels; l++)
        {
            var row = new float[sectors];
            for (int s = 0; s < sectors; s++) row[s] = radius[l, s];
            FillGaps(row, float.PositiveInfinity, sectors, Mathf.Min, 0f);
            for (int s = 0; s < sectors; s++) radius[l, s] = row[s];
        }

        // Halkalar: 0 = kenarın tepesi, sonra katmanlar (dilimin kenarından yüksekte kalanlar kenara indirilir)
        var vertices = new System.Collections.Generic.List<Vector3>();
        float averageTop = 0f;
        for (int s = 0; s < sectors; s++) averageTop += rimTop[s];
        vertices.Add(new Vector3(center.x, averageTop / sectors, center.y)); // 0: kapağın ortası

        int ringCount = levels + 1;
        for (int ring = 0; ring < ringCount; ring++)
        {
            for (int s = 0; s < sectors; s++)
            {
                float height, r;
                if (ring == 0)
                {
                    height = rimTop[s];
                    r = topRadius[s];
                }
                else
                {
                    float levelHeight = max.y - (ring - 1) * step;
                    height = Mathf.Min(levelHeight, rimTop[s]);
                    r = levelHeight >= rimTop[s] ? topRadius[s] : radius[ring - 1, s];
                }
                r = Mathf.Max(0f, r - inset);
                float angle = (s + 0.5f) / sectors * Mathf.PI * 2f;
                vertices.Add(new Vector3(center.x + Mathf.Cos(angle) * r, height, center.y + Mathf.Sin(angle) * r));
            }
        }
        int bottomCenter = vertices.Count;
        vertices.Add(new Vector3(center.x, bottom, center.y));

        var triangles = new System.Collections.Generic.List<int>();
        for (int s = 0; s < sectors; s++)
        {
            int next = (s + 1) % sectors;
            triangles.Add(0); triangles.Add(1 + next); triangles.Add(1 + s); // kapak
            for (int ring = 0; ring < ringCount - 1; ring++)
            {
                int a = 1 + ring * sectors + s, b = 1 + ring * sectors + next;
                int c = a + sectors, d = b + sectors;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(b); triangles.Add(d); triangles.Add(c);
            }
            int lastRing = 1 + (ringCount - 1) * sectors;
            triangles.Add(bottomCenter); triangles.Add(lastRing + s); triangles.Add(lastRing + next); // dip
        }

        mesh = new Mesh { name = "Boat Water Mask" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals(); // Gizmos.DrawMesh normal ister (çizimde kullanılmaz)
        mesh.RecalculateBounds();
        return true;
    }

    // Ölçülemeyen dilimler (empty değerinde) en yakın ölçülmüş komşuların pick'iyle (Min: içeride kal) dolar; hiç yoksa fallback
    private static void FillGaps(float[] values, float empty, int count, System.Func<float, float, float> pick, float fallback)
    {
        var source = (float[])values.Clone();
        for (int i = 0; i < count; i++)
        {
            if (source[i] != empty) continue;
            float left = empty, right = empty;
            for (int k = 1; k < count && (left == empty || right == empty); k++)
            {
                if (left == empty) left = source[(i - k + count) % count];
                if (right == empty) right = source[(i + k) % count];
            }
            values[i] = left == empty && right == empty ? fallback
                      : left == empty ? right : right == empty ? left : pick(left, right);
        }
    }

#if UNITY_EDITOR
    private Mesh editorMaskMesh;
    private MeshFilter editorHull;
    private Matrix4x4 editorHullToModel;

    private void OnValidate() => editorMaskMesh = null; // ayar değişince gizmo yeniden kurulur

    // Modelin uzayından dünyaya: Play'de kurulan model, editörde çalışırken kurulacak hiyerarşinin aynısı
    // (kök → Visual (su çizgisi) → model (dönüş, ölçek))
    private Matrix4x4 ModelMatrix()
    {
        if (Application.isPlaying && visual != null && visual.childCount > 0) return visual.GetChild(0).localToWorldMatrix;
        return transform.localToWorldMatrix
               * Matrix4x4.Translate(Vector3.up * (0.5f + waterline))
               * Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(modelRotation), Vector3.one * modelScale);
    }

    // Maske Scene görünümünde (Play'e girmeden de, prefab modunda da): sarı yarı saydam + tel kafes; gövde mavi tel kafes
    private void OnDrawGizmos()
    {
        if (!showMaskGizmo) return;
        Matrix4x4 model = ModelMatrix();

        if (Application.isPlaying)
        {
            if (maskTransform == null) return;
            foreach (MeshFilter filter in maskTransform.GetComponentsInChildren<MeshFilter>())
                DrawMaskMesh(filter.sharedMesh, filter.transform.localToWorldMatrix);
            return;
        }
        if (modelPrefab == null) return;

        // Gövde (mavi) ve otomatik maske bir kez ölçülür
        if (editorMaskMesh == null)
        {
            Transform asset = modelPrefab.transform;
            TryBuildMask(asset, FindQuiet(asset, leftOarName), FindQuiet(asset, rightOarName), MaskBottom, maskInset,
                         out editorMaskMesh, out editorHull, out editorHullToModel);
            if (editorMaskMesh != null) editorMaskMesh.hideFlags = HideFlags.HideAndDontSave; // sahneye kaydedilmesin
        }
        if (editorHull != null && editorHull.sharedMesh != null)
        {
            Gizmos.matrix = model * editorHullToModel;
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.35f);
            Gizmos.DrawWireMesh(editorHull.sharedMesh);
        }

        if (maskModel != null)
        {
            // Blender'daki maske: asset'teki yeriyle (kökü botun modeliyle aynı yerde)
            Matrix4x4 toRoot = maskModel.transform.worldToLocalMatrix;
            foreach (MeshFilter filter in maskModel.GetComponentsInChildren<MeshFilter>(true))
                DrawMaskMesh(filter.sharedMesh, model * toRoot * filter.transform.localToWorldMatrix);
        }
        else DrawMaskMesh(editorMaskMesh, model);
        Gizmos.matrix = Matrix4x4.identity;
    }

    private static void DrawMaskMesh(Mesh mesh, Matrix4x4 matrix)
    {
        if (mesh == null) return;
        Gizmos.matrix = matrix;
        if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal)) // okunamayan mesh'te de çalışır
        {
            Gizmos.color = new Color(1f, 0.9f, 0f, 0.25f);
            Gizmos.DrawMesh(mesh);
        }
        Gizmos.color = new Color(1f, 0.9f, 0f, 0.9f);
        Gizmos.DrawWireMesh(mesh);
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
