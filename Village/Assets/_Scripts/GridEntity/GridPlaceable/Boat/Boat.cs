using UnityEngine;

// Su üstünde giden 2×1 bot (yürüyen placeable, kedi gibi): sadece suya konur (CanOccupy), su hücrelerinde
// GridWalker ile gider (Medium = Water); grid'deki yerini GridWalker her adımda günceller.
// Binme: mouse botun ya da tarlasının üstüne gelince (uzaklık sınırı yok) eldiven kıyıdan zıplayıp koltuğa (Seat) oturur. Binmişken WASD botu kameranın
// açısına göre hücre hücre sürer (W ileri, S geri, A/D sola/sağa; yol bulma yok), kamera botu takip eder.
// WASD ile gidilecek hücre kara (base) ise eldiven oraya iner, mouse imleci de oraya taşınır. Binerken oyuncu girişi
// kilitli (şimdilik sadece gezme).
// Görsel: model kodla kurulur; giderken kürekler yol başına çekilir, bot suda hafif inip kalkar, yalpalar, dönüşte yatar.
// Tarla: botun arkasında tek sayılı kare (3×3, 5×5...), çekme halatıyla botun izinden gelir (IWalkerExtraCells):
// gövdenin geçtiği hücrelerin izi tutulur, tarla merkezi o izin FarmLag adım gerisindedir. Tek sayılı kare dönünce aynı
// hücreleri kapladığı için köşeler sorun değil. Halat: iz köşede kıvrıldığı için tarla izde (yarıçap + 1) adım geride
// olsa köşede botun gövdesiyle çakışırdı; çakışmaması için en az 2 × (yarıçap + 1) geride (düzde arada halat boşluğu).
// Dünya grid'i bot + tarlayı tek placeable olarak görür. Geri gitme yok; sıkışınca limana dönülür (şimdilik H tuşu).
[RequireComponent(typeof(GridWalker))]
[DefaultExecutionOrder(10)] // GridWalker'dan (0) sonra: görsel ve koltuk bu karenin konumuyla, eldiven (LateUpdate) okumadan önce
public class Boat : GridPlaceable, ISaveState, IGlovePassThrough, IWalkerExtraCells
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
             "içine aynı yere konur, görünmez olur.")]
    [SerializeField] private GameObject maskModel;
    [Tooltip("Scene görünümünde maskeyi (sarı) göster; Play'e girmeden de, prefab modunda da")]
    [SerializeField] private bool showMaskGizmo = true;

    [Header("Tarla (botun arkasından gelir)")]
    [Tooltip("Tarla alanının kenarı: hep tek sayı (3, 5, 7...), bot önde ortada. 0 = tarla yok.")]
    [SerializeField] private int farmSize = 3;
    [Tooltip("Tarla güvertesinin üst yüzünün su seviyesinden yüksekliği ve güvertenin kalınlığı")]
    [SerializeField] private float deckHeight = 0.12f;
    [SerializeField] private float deckThickness = 0.3f;
    [SerializeField] private Color deckColor = new Color(0.72f, 0.52f, 0.36f);
    [Tooltip("Tarlanın botu takip yumuşaklığı (küçük = daha gecikmeli)")]
    [SerializeField] private float deckFollow = 6f;
    [Tooltip("Bot ile tarla arasındaki halat boşluğu (hücre, düz giderken). -1 = otomatik: köşede çakışmayan en kısa " +
             "(yarıçap + 1). Daha kısa olursa dönüşlerde tarla botun altına girer.")]
    [SerializeField] private int towCells = -1;
    [SerializeField] private Color ropeColor = new Color(0.55f, 0.42f, 0.3f);
    [SerializeField] private float ropeWidth = 0.04f;

    [Header("Binme / inme")]
    [Tooltip("İndikten sonra tekrar binmek için en az bekleme (saniye)")]
    [SerializeField] private float boardCooldown = 0.8f;

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
    private Transform maskTransform;
    private Vector3Int? restoredHeadOffset; // kayıttan: arka hücreden ön hücreye

    // Tarla treni: gövdenin geçtiği hücreler (son = şimdiki gövde); tarla merkezi bunun FarmLag adım gerisi
    private readonly System.Collections.Generic.List<Vector3Int> trail = new System.Collections.Generic.List<Vector3Int>();
    private Vector3Int farmCenter, farmForward = Vector3Int.forward;
    private Transform deck;
    private bool snapDeck = true;
    private Vector3Int homeHead, homeBody; // liman: ilk konduğu yer
    private bool hasHome;

    private int FarmRadius => Mathf.Max(0, farmSize) / 2;
    // Gövdeden tarla merkezine iz üzerinde adım: tarlanın ön kenarı (yarıçap + 1) + halat
    private int FarmLag => FarmRadius + 1 + (towCells < 0 ? FarmRadius + 1 : towCells);
    private LineRenderer rope;
    private bool HasFarm => farmSize > 0;

    public bool IsRidden => riding;

    private GloveCursor Glove => glove != null ? glove : glove = FindFirstObjectByType<GloveCursor>();

    private void Awake()
    {
        walker = GetComponent<GridWalker>();
        walker.WalkMedium = GridWalker.Medium.Water; // bot hep suda gider (hız/dönüş ayarları GridWalker'da)
        if (modelPrefab != null) BuildModel();
    }

    // --- Grid ve kayıt ---

    // Sadece su hücreleri
    public override bool CanOccupy(Vector3Int worldCell) => GridManager.Instance != null && GridManager.Instance.IsWater(worldCell);

    // Arka hücre (origin) + ön hücre + arkadaki tarla karesi. Yerleşikken yürüyüşten, yerleşmeden önce kayıttan ya da
    // rotasyondan (Deg0'da ön +Z; tarla düz arkada). Sıra önemli: [1] ön hücre (OnPlaced kafayı oradan alır).
    public override GridFootprint GetFootprint()
    {
        bool placed = walker != null && walker.IsPlaced;
        Vector3Int front = placed ? walker.HeadCell - walker.BodyCell
                         : restoredHeadOffset ?? GridMaskRotator.RotateOffset(Vector3Int.forward, Rotation);
        var cells = new System.Collections.Generic.List<Vector3Int> { Vector3Int.zero, front };
        if (HasFarm)
        {
            Vector3Int center = placed ? farmCenter - walker.BodyCell : -front * FarmLag;
            foreach (Vector3Int cell in FarmSquare(center))
                if (cell != Vector3Int.zero && cell != front) cells.Add(cell);
        }
        return new GridFootprint(cells.ToArray());
    }

    private System.Collections.Generic.IEnumerable<Vector3Int> FarmSquare(Vector3Int center)
    {
        int r = FarmRadius;
        for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
                yield return center + new Vector3Int(dx, 0, dz);
    }

    // --- Tarla treni (GridWalker her adımda sorar) ---

    // Bu adım atılırsa tarlanın kaplayacağı hücreler (dünya)
    public System.Collections.Generic.IEnumerable<Vector3Int> CellsFor(Vector3Int head, Vector3Int body)
    {
        if (!HasFarm) return System.Array.Empty<Vector3Int>();
        return FarmSquare(FarmCenterFor(head, body, out _));
    }

    // Adım atıldı: iz güncellenir, tarlanın yeni merkezi
    public void OnOccupied(Vector3Int head, Vector3Int body)
    {
        if (!Continues(body))
        {
            ResetTrail(head, body);
            snapDeck = true; // ilk yerleşme / limana dönüş: güverte kaymadan yerine
        }
        else if (trail[trail.Count - 1] != body) trail.Add(body);
        while (trail.Count > FarmLag + 2) trail.RemoveAt(0);
        farmCenter = FarmCenterFor(head, body, out farmForward);
    }

    // Gövde izin devamı mı (aynı hücre ya da komşusu); değilse (ilk yerleşme, ışınlanma) iz yeniden kurulur
    private bool Continues(Vector3Int body)
    {
        if (trail.Count == 0) return false;
        Vector3Int d = body - trail[trail.Count - 1];
        return Mathf.Abs(d.x) + Mathf.Abs(d.y) + Mathf.Abs(d.z) <= 1;
    }

    // İz yok: tarla düz arkada
    private void ResetTrail(Vector3Int head, Vector3Int body)
    {
        trail.Clear();
        Vector3Int back = body - head;
        for (int i = FarmLag; i >= 0; i--) trail.Add(body + back * i);
    }

    // Aday iz (mevcut iz + yeni gövde) üzerinde gövdeden FarmLag adım geri; forward: tarlanın gidiş yönü
    private Vector3Int FarmCenterFor(Vector3Int head, Vector3Int body, out Vector3Int forward)
    {
        if (!Continues(body))
        {
            Vector3Int back = body - head;
            forward = -back;
            return body + back * FarmLag;
        }
        bool appended = trail[trail.Count - 1] != body;
        int count = trail.Count + (appended ? 1 : 0);
        Vector3Int At(int i) => i < trail.Count ? trail[i] : body;

        int index = count - 1 - FarmLag;
        if (index < 0)
        {
            // İz henüz kısa: ilk hücreden düz geriye uzat
            Vector3Int direction = count > 1 ? At(1) - At(0) : head - body;
            forward = direction;
            return At(0) - direction * (-index);
        }
        forward = At(index + 1) - At(index);
        return At(index);
    }

    // Bu adımda kafa ya da gövde, tarlanın gideceği karenin içine düşer mi
    private bool HitsOwnFarm(Vector3Int nextHead)
    {
        if (!HasFarm || !walker.IsPlaced) return false;
        Vector3Int center = FarmCenterFor(nextHead, walker.HeadCell, out _);
        int r = FarmRadius;
        bool Inside(Vector3Int cell) => Mathf.Abs(cell.x - center.x) <= r && Mathf.Abs(cell.z - center.z) <= r;
        return Inside(nextHead) || Inside(walker.HeadCell);
    }

    // Limana dön (sıkışınca): bütün tarlasıyla ilk konduğu yere ışınlanır. Liman doluysa olmaz.
    public bool ReturnToHarbor()
    {
        if (!hasHome || !walker.IsPlaced) return false;
        walker.Stop();
        if (!walker.TryPlace(homeHead, homeBody))
        {
            Debug.Log("Boat: liman dolu, dönülemedi.", this);
            return false;
        }
        lastPosition = transform.position;
        lastYaw = transform.eulerAngles.y;
        return true;
    }

    public override void OnPlaced(Vector3Int origin)
    {
        base.OnPlaced(origin);
        Vector3Int head = origin + PlacedFootprint.FilledCells()[1];
        walker.Place(head, origin);
        if (!hasHome)
        {
            homeHead = head;
            homeBody = origin;
            hasHome = true;
        }
        lastPosition = transform.position;
        lastYaw = transform.eulerAngles.y;
    }

    [System.Serializable]
    private class SaveState
    {
        public Vector3Int front; // arka hücreden ön hücreye
        public bool hasHome;
        public Vector3Int homeHead, homeBody; // liman
        public int farmSize;                  // 0 = prefab'taki
    }

    // Tarla kayıttan hep botun düz arkasına kurulur (köşede kaydedildiyse de)
    public string CaptureState() => JsonUtility.ToJson(new SaveState
    {
        front = walker.IsPlaced ? walker.HeadCell - walker.BodyCell : Vector3Int.forward,
        hasHome = hasHome,
        homeHead = homeHead,
        homeBody = homeBody,
        farmSize = farmSize,
    });

    public void RestoreState(string state)
    {
        SaveState saved = JsonUtility.FromJson<SaveState>(state);
        if (saved == null) return;
        if (saved.front != Vector3Int.zero) restoredHeadOffset = saved.front;
        if (saved.farmSize > 0) farmSize = saved.farmSize;
        if (saved.hasHome)
        {
            hasHome = true;
            homeHead = saved.homeHead;
            homeBody = saved.homeBody;
        }
    }

    // --- Binme, sürme, inme ---

    private void Update()
    {
        if (!walker.IsPlaced)
        {
            // Grid'e konan bot OnPlaced'da yerini alır; sahneye elle konmuşsa (test) yakındaki suya yerleşir
            if (PlacedFootprint != null || !walker.TryPlaceNear(transform.position)) return;
        }

        if (riding)
        {
            if (Input.GetKeyDown(KeyCode.H)) ReturnToHarbor(); // geçici: ileride UI / iskele
            Steer();
        }
        else TryBoard();
        Animate(Time.deltaTime);
        UpdateDeck(Time.deltaTime);
    }

    private void TryBoard()
    {
        // Mouse botun (ya da tarlasının) üstüne gelince, bot ne kadar uzakta olursa olsun biner. Eldiven geçirgen
        // olduğu için o sırada kıyıda bekliyordu; oradan zıplar.
        GloveCursor g = Glove;
        if (g == null || g.IsRiding || g.IsCaptured || !ReferenceEquals(g.HoveredPassThrough, this)) return;
        if (Time.time - lastRideEnd < boardCooldown) return;

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
        if (next == walker.BodyCell) return; // geri gitme yok (arkada tarla var): U çizerek dönülür
        if (HitsOwnFarm(next)) return; // yılan gibi: kendi tarlasına çarpamaz (dar U dönüşü için tarla kadar yer gerekir)
        if (walker.IsWalkable(next))
        {
            walker.StepTo(next); // tarla hücreleri de boş değilse GridWalker adımı atmaz
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
        CreateDeck(model);
    }

    // Botun içini dolduran görünmez maske (Blender'da modellenen, maskModel): sudan hemen önce derinlik yazar, arkasındaki
    // (botun içindeki) su çizilmez. Botun modelinin içine aynı yere konur, bütün renderer'larına maske malzemesi verilir.
    // Modelin child'ı: bot sallandıkça maske de sallanır.
    private void CreateWaterMask(Transform model)
    {
        if (maskModel == null || maskShader == null)
        {
            Debug.LogWarning("Boat: Mask Model ya da Mask Shader atanmamış; su botun içinden görünebilir.", this);
            return;
        }

        var material = new Material(maskShader);
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
    }

#if UNITY_EDITOR
    // Maske Scene görünümünde (Play'e girmeden de, prefab modunda da): sarı yarı saydam + tel kafes
    private void OnDrawGizmos()
    {
        if (!showMaskGizmo) return;

        if (Application.isPlaying)
        {
            if (maskTransform == null) return;
            foreach (MeshFilter filter in maskTransform.GetComponentsInChildren<MeshFilter>())
                DrawMaskMesh(filter.sharedMesh, filter.transform.localToWorldMatrix);
            return;
        }
        if (maskModel == null) return;

        // Çalışırken kurulacak hiyerarşinin aynısı: kök → Visual (su çizgisi) → model (dönüş, ölçek) → maske
        Matrix4x4 model = transform.localToWorldMatrix
                          * Matrix4x4.Translate(Vector3.up * (0.5f + waterline))
                          * Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(modelRotation), Vector3.one * modelScale);
        Matrix4x4 toRoot = maskModel.transform.worldToLocalMatrix;
        foreach (MeshFilter filter in maskModel.GetComponentsInChildren<MeshFilter>(true))
            DrawMaskMesh(filter.sharedMesh, model * toRoot * filter.transform.localToWorldMatrix);
    }

    private static void DrawMaskMesh(Mesh mesh, Matrix4x4 matrix)
    {
        if (mesh == null) return;
        Gizmos.matrix = matrix;
        if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal)) // DrawMesh normal ister
        {
            Gizmos.color = new Color(1f, 0.9f, 0f, 0.25f);
            Gizmos.DrawMesh(mesh);
        }
        Gizmos.color = new Color(1f, 0.9f, 0f, 0.9f);
        Gizmos.DrawWireMesh(mesh);
        Gizmos.matrix = Matrix4x4.identity;
    }
#endif

    private static Transform Find(Transform parent, string objectName)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == objectName) return t;
        Debug.LogWarning($"Boat: modelde '{objectName}' bulunamadı", parent);
        return null;
    }

    // Tarla güvertesi (şimdilik düz bir sal; tarla parçaları sonra üstüne oturacak). Gövdenin malzemesinden kopya, dokusuz
    private void CreateDeck(Transform model)
    {
        if (!HasFarm) return;
        GameObject plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plank.name = "Farm Deck";
        deck = plank.transform;
        deck.SetParent(transform, false);
        deck.localScale = new Vector3(farmSize, deckThickness, farmSize);

        Renderer hull = null;
        foreach (Renderer candidate in model.GetComponentsInChildren<Renderer>())
            if (candidate.transform != leftOar && candidate.transform != rightOar) { hull = candidate; break; }
        var renderer = plank.GetComponent<Renderer>();
        if (hull != null)
        {
            var material = new Material(hull.sharedMaterial);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", null);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", deckColor);
            renderer.sharedMaterial = material;

            // Çekme halatı: botun kıçından güvertenin ön kenarına, ortası hafif sarkık
            var ropeObject = new GameObject("Tow Rope");
            ropeObject.transform.SetParent(transform, false);
            rope = ropeObject.AddComponent<LineRenderer>();
            var ropeMaterial = new Material(hull.sharedMaterial);
            if (ropeMaterial.HasProperty("_BaseMap")) ropeMaterial.SetTexture("_BaseMap", null);
            if (ropeMaterial.HasProperty("_BaseColor")) ropeMaterial.SetColor("_BaseColor", ropeColor);
            rope.sharedMaterial = ropeMaterial;
            rope.positionCount = 8;
            rope.widthMultiplier = ropeWidth;
            rope.useWorldSpace = true;
            rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        snapDeck = true;
    }

    // Güverte tarlanın merkezini yumuşakça (gecikmeli) takip eder, gidiş yönüne döner, suda hafif sallanır.
    // Dünya transform'u her kare yazılır (botun child'ı ama bot döndükçe dönmesin)
    private void UpdateDeck(float dt)
    {
        if (deck == null || !walker.IsPlaced) return;
        Vector3 target = GridWalker.FeetPosition(farmCenter) + Vector3.up * (0.5f + deckHeight - deckThickness * 0.5f);
        target.y += Mathf.Sin(Time.time * bobSpeed * Mathf.PI * 2f + 1.7f) * bobAmount;
        Vector3 forward = new Vector3(farmForward.x, 0f, farmForward.z);
        Quaternion rotation = forward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(forward, Vector3.up) : deck.rotation;
        if (snapDeck)
        {
            deck.SetPositionAndRotation(target, rotation);
            snapDeck = false;
            return;
        }
        float k = 1f - Mathf.Exp(-deckFollow * dt);
        deck.SetPositionAndRotation(Vector3.Lerp(deck.position, target, k), Quaternion.Slerp(deck.rotation, rotation, k));
        UpdateRope();
    }

    private void UpdateRope()
    {
        if (rope == null) return;
        float water = transform.position.y + 0.5f;
        Vector3 from = transform.position - transform.forward * 0.95f; // botun kıçı
        from.y = water + 0.15f;
        Vector3 to = deck.position + deck.forward * (farmSize * 0.5f);   // güvertenin ön kenarı
        to.y = water + deckHeight;
        float sag = Mathf.Min(0.12f, Vector3.Distance(from, to) * 0.05f);
        for (int i = 0; i < rope.positionCount; i++)
        {
            float s = i / (float)(rope.positionCount - 1);
            rope.SetPosition(i, Vector3.Lerp(from, to, s) + Vector3.down * (Mathf.Sin(s * Mathf.PI) * sag));
        }
    }

    private void OnValidate()
    {
        if (farmSize > 0 && farmSize % 2 == 0) farmSize++; // hep tek sayı: bot ortada
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
