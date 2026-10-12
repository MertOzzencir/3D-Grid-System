using UnityEngine;

// Su üstünde giden gemi (yürüyen placeable): sadece suya konur (CanOccupy), su hücrelerinde GridWalker ile gider
// (Medium = Water); grid'deki yerini GridWalker her adımda günceller. Hareket GridWalker'ın iki hücresiyle (kafa + gövde,
// yılan gibi): gövde hücresi geminin ortası, kafa önü. Gemi gövdenin etrafında shipWidth × shipLength dikdörtgen kaplar
// (IWalkerExtraCells; hepsi su ve boş olmalı, dönüşte de).
// Binme: mouse botun gövdesinin üstüne gelince (uzaklık sınırı yok; tarla değil, oradaki parçalar tutulur) eldiven
// kıyıdan zıplayıp koltuğa (Seat) oturur. Binmişken WASD botu kameranın
// açısına göre hücre hücre sürer (W ileri, S geri, A/D sola/sağa; yol bulma yok), kamera botu takip eder.
// WASD ile gidilecek hücre kara (base) ise eldiven oraya iner, mouse imleci de oraya taşınır. Binerken oyuncu girişi
// kilitli (şimdilik sadece gezme).
// Görsel: model kodla kurulur; giderken çarklar (adı wheelPrefix ile başlayan objeler) döner, gemi suda hafif inip
// kalkar, yalpalar, dönüşte yatar. Toprak deposu: soil kadar torba (soilBagModel) soilStackAnchor'ın üstüne piramit
// gibi dizilir (3×3, 2×2, 1).
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
    [Tooltip("Modelin kaydırılması (kökün uzayında). Kök kafa ile gövde hücresinin ortasında; gövde (geminin ortası) " +
             "0.5 geride. Model ortası origin'deyse (0, 0, -0.5).")]
    [SerializeField] private Vector3 modelOffset = new Vector3(0f, 0f, -0.5f);
    [Tooltip("Scene görünümünde geminin tel kafes önizlemesi (prefab modunda da): halat ve toprak noktalarını yerleştirmek için")]
    [SerializeField] private bool showModelGizmo = true;
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

    [Header("Gemi (grid'de kapladığı alan)")]
    [Tooltip("Gemi kaç hücre uzun (gidiş yönünde) ve kaç hücre geniş; tek sayı (gövde hücresi ortada)")]
    [SerializeField] private int shipLength = 5;
    [SerializeField] private int shipWidth = 3;

    [Header("Çarklar")]
    [Tooltip("Adı bununla başlayan model objeleri çark sayılır (göbek çivileri dahil), giderken döner")]
    [SerializeField] private string wheelPrefix = "WHEEL";
    [Tooltip("Çarkın döndüğü eksen (çark objesinin kendi uzayında) ve gidilen birim başına dönüş (derece). Ters dönüyorsa eksi yap.")]
    [SerializeField] private Vector3 wheelAxis = Vector3.right;
    [SerializeField] private float wheelDegreesPerUnit = 120f;

    [Header("Toprak deposu")]
    [Tooltip("Gemideki toprak miktarı (tarla parçası yapmak için). Şimdilik elle; ileride madencilik artıracak. Kaydedilir.")]
    [SerializeField, Min(0)] private int soil = 0;
    [Tooltip("Her toprak birimi için depoda görünen torba modeli")]
    [SerializeField] private GameObject soilBagModel;
    [Tooltip("Torbaların dizildiği yer: geminin child'ı boş bir obje, deponun üstünde. Alt kat (3×3) bunun ortasında, " +
             "bu objenin eksenlerinde dizilir; üst katlar (2×2, 1) torba boyu kadar yukarıda.")]
    [SerializeField] private Transform soilStackAnchor;
    [Tooltip("Torbalar arası mesafe ve kat yüksekliği; 0 = torba modelinin boyundan")]
    [SerializeField] private float soilBagSpacing = 0f;
    [SerializeField] private float soilLayerHeight = 0f;

    [Header("Tarla (botun arkasından gelir)")]
    [Tooltip("Tarla alanının kenarı: hep tek sayı (3, 5, 7...), bot önde ortada. 0 = tarla yok.")]
    [SerializeField] private int farmSize = 3;
    [Tooltip("Tarla güvertesinin üst yüzünün su seviyesinden yüksekliği ve güvertenin kalınlığı")]
    [SerializeField] private float deckHeight = 0.12f;
    [SerializeField] private float deckThickness = 0.3f;
    [SerializeField] private Color deckColor = new Color(0.72f, 0.52f, 0.36f);
    [Tooltip("Tarlanın botu takip yumuşaklığı (küçük = daha gecikmeli)")]
    [SerializeField] private float deckFollow = 6f;
    [Tooltip("Gemi ile tarla arasındaki halat boşluğu (hücre, düz giderken). -1 = otomatik: dönüşlerde gemiyle çakışmayan " +
             "(tarla yarıçapı + 1 + gemi yarı genişliği). Daha kısa olursa dönüşlerde tarla geminin altına girer (o adım atılmaz).")]
    [SerializeField] private int towCells = -1;
    [SerializeField] private Color ropeColor = new Color(0.55f, 0.42f, 0.3f);
    [Tooltip("Halatın gemideki ucu (kökün uzayında; kök su seviyesinin 0.5 altında, kafa ile gövde hücresinin ortasında). " +
             "Scene'de turuncu top.")]
    [SerializeField] private Vector3 ropeStart = new Vector3(0f, 0.65f, -3f);
    [Tooltip("Halatın tarladaki ucu: güvertenin ön kenarının ortasına göre kaydırma (güvertenin uzayında). Scene'de turuncu top.")]
    [SerializeField] private Vector3 ropeEndOffset = Vector3.zero;
    [Tooltip("Bot bir adımı atamazsa nedenini konsola yaz (sıkışmaları anlamak için)")]
    [SerializeField] private bool logBlockedSteps = true;
    [SerializeField] private float ropeWidth = 0.04f;

    [Header("Tarla büyümesi (düğme + kapak, FarmGrower)")]
    [Tooltip("Düğmeye basınca tarlanın kapalı kalma süresi (saniye); sonunda bütün fideler olgunlaşır")]
    [SerializeField] private float growDuration = 60f;
    [Tooltip("Kapağın güverteden yüksekliği: parçalar (1) + olgun ekinlerin üstünde kalmalı")]
    [SerializeField] private float coverHeight = 1.5f;
    [SerializeField] private Color coverColor = new Color(0.93f, 0.85f, 0.7f);
    [SerializeField] private Color growButtonColor = new Color(0.95f, 0.45f, 0.4f);
    [Tooltip("Kapak modeli (boşsa kutu). 1×1 alana göre modellenir, pivot tabanın ortasında; kod tarlanın boyuna X/Z'de ölçekler. " +
             "Yüksekliği Cover Height'a uysun.")]
    [SerializeField] private GameObject coverModel;

    [Header("Tarla efektleri (ekim, hasat, kapak)")]
    [SerializeField] private FarmFx farmFx = new FarmFx();

    [Header("Binme / inme")]
    [Tooltip("İndikten sonra tekrar binmek için en az bekleme (saniye)")]
    [SerializeField] private float boardCooldown = 0.8f;

    [Header("Suda sallanma")]
    [SerializeField] private float bobAmount = 0.025f;
    [Tooltip("Sallanma hızı (salınım/sn)")]
    [SerializeField] private float bobSpeed = 0.5f;
    [SerializeField] private float rockAngle = 2.5f;
    [Tooltip("Dönüş hızı (derece/sn) başına yana yatma (derece)")]
    [SerializeField] private float turnLean = 0.09f;
    [Tooltip("Giderken burnun kalkması (derece)")]
    [SerializeField] private float surgePitch = 2f;

    private GridWalker walker;
    private GloveCursor glove;
    private Transform visual, seat;
    private readonly System.Collections.Generic.List<(Transform t, Quaternion rest)> wheels =
        new System.Collections.Generic.List<(Transform, Quaternion)>();
    private float wheelAngle;
    private readonly System.Collections.Generic.List<Transform> soilBags = new System.Collections.Generic.List<Transform>();
    private bool restoredSoil;
    private Vector3 visualRest;
    private Vector3 lastPosition;
    private float lastYaw, moveWeight, lean;
    private bool riding;
    private float lastRideEnd = float.MinValue;
    private Transform maskTransform;
    private Vector3Int? restoredHeadOffset; // kayıttan: arka hücreden ön hücreye

    // Tarla treni: gövdenin geçtiği hücreler (son = şimdiki gövde); tarla merkezi bunun FarmLag adım gerisi
    private readonly System.Collections.Generic.List<Vector3Int> trail = new System.Collections.Generic.List<Vector3Int>();
    private Vector3Int farmCenter, farmForward = Vector3Int.forward;
    private Transform deck;
    private FarmGrid farmGrid;
    private bool snapDeck = true;
    private System.Collections.Generic.List<FarmPieceSave> restoredPieces;
    // Güvertenin yumuşatılmış dünya pozu: takip bunun üstünden yapılır. Transform'dan okunsaydı güverte botun child'ı
    // olduğu için bot dönünce o kare botla birlikte dönmüş halinden başlardı (anlık sağa dönüp düzelme).
    private Vector3 deckPosition;
    private Quaternion deckRotation = Quaternion.identity;
    private Vector3Int homeHead, homeBody; // liman: ilk konduğu yer
    private bool hasHome;

    private int FarmRadius => Mathf.Max(0, farmSize) / 2;
    private int ShipBack => Mathf.Max(1, shipLength) / 2;     // gövde hücresinden öne / arkaya
    private int ShipHalfWidth => Mathf.Max(1, shipWidth) / 2;
    // Gövdeden tarla merkezine iz üzerinde adım: geminin kıçı + tarlanın ön kenarı (yarıçap + 1) + halat
    private int FarmLag => ShipBack + FarmRadius + 1 + (towCells < 0 ? FarmRadius + 1 + ShipHalfWidth : towCells);

    // Toprak (tarla parçası yapımı; ileride madencilik artırır)
    public int Soil
    {
        get => soil;
        set => soil = Mathf.Max(0, value);
    }
    public void AddSoil(int amount) => Soil = soil + amount;
    public bool TrySpendSoil(int amount)
    {
        if (amount > soil) return false;
        Soil = soil - amount;
        return true;
    }
    private LineRenderer rope;
    private string lastBlockLog;
    private bool HasFarm => farmSize > 0;

    public bool IsRidden => riding;

    private GloveCursor Glove => glove != null ? glove : glove = FindFirstObjectByType<GloveCursor>();

    private void Awake()
    {
        walker = GetComponent<GridWalker>();
        walker.WalkMedium = GridWalker.Medium.Water; // bot hep suda gider (hız/dönüş ayarları GridWalker'da)
        if (modelPrefab != null) BuildModel();
        // İşaret geminin görseliyle sallansın (prefab'ta kökün child'ı)
        if (soilStackAnchor != null && visual != null) soilStackAnchor.SetParent(visual, true);
    }

    // --- Grid ve kayıt ---

    // Sadece su hücreleri
    public override bool CanOccupy(Vector3Int worldCell) => GridManager.Instance != null && GridManager.Instance.IsWater(worldCell);

    // Gövde hücresi (origin) + ön hücre + geminin dikdörtgeni + arkadaki tarla karesi. Yerleşikken yürüyüşten, yerleşmeden
    // önce kayıttan ya da rotasyondan (Deg0'da ön +Z; tarla düz arkada). Sıra önemli: [1] ön hücre (OnPlaced kafayı oradan alır).
    public override GridFootprint GetFootprint()
    {
        bool placed = walker != null && walker.IsPlaced;
        Vector3Int front = placed ? walker.HeadCell - walker.BodyCell
                         : restoredHeadOffset ?? GridMaskRotator.RotateOffset(Vector3Int.forward, Rotation);
        var cells = new System.Collections.Generic.List<Vector3Int> { Vector3Int.zero, front };
        foreach (Vector3Int cell in ShipCells(front, Vector3Int.zero))
            if (cell != Vector3Int.zero && cell != front) cells.Add(cell);
        if (HasFarm)
        {
            Vector3Int center = placed ? farmCenter - walker.BodyCell : -front * FarmLag;
            foreach (Vector3Int cell in FarmSquare(center))
                if (cell != Vector3Int.zero && cell != front) cells.Add(cell);
        }
        return new GridFootprint(cells.ToArray());
    }

    // Geminin kapladığı dikdörtgen: gövde hücresinin etrafında, gidiş yönünde ±ShipBack, yanlara ±ShipHalfWidth
    private System.Collections.Generic.IEnumerable<Vector3Int> ShipCells(Vector3Int head, Vector3Int body)
    {
        Vector3Int forward = head - body;
        var side = new Vector3Int(forward.z, 0, -forward.x);
        for (int l = -ShipBack; l <= ShipBack; l++)
            for (int w = -ShipHalfWidth; w <= ShipHalfWidth; w++)
                yield return body + forward * l + side * w;
    }

    private System.Collections.Generic.IEnumerable<Vector3Int> FarmSquare(Vector3Int center)
    {
        int r = FarmRadius;
        for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
                yield return center + new Vector3Int(dx, 0, dz);
    }

    // --- Tarla treni (GridWalker her adımda sorar) ---

    // Bu adım atılırsa geminin ve tarlanın kaplayacağı hücreler (dünya)
    public System.Collections.Generic.IEnumerable<Vector3Int> CellsFor(Vector3Int head, Vector3Int body)
    {
        foreach (Vector3Int cell in ShipCells(head, body)) yield return cell;
        if (!HasFarm) yield break;
        foreach (Vector3Int cell in FarmSquare(FarmCenterFor(head, body, out _))) yield return cell;
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

    // Gemi bu kafa / gövdeyle giderse, tarlanın gideceği karenin içine düşer mi
    private bool HitsOwnFarm(Vector3Int newHead, Vector3Int newBody)
    {
        if (!HasFarm || !walker.IsPlaced) return false;
        Vector3Int center = FarmCenterFor(newHead, newBody, out _);
        int r = FarmRadius;
        foreach (Vector3Int cell in ShipCells(newHead, newBody))
            if (Mathf.Abs(cell.x - center.x) <= r && Mathf.Abs(cell.z - center.z) <= r) return true;
        return false;
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
        public bool hasSoil;
        public int soil;
        public System.Collections.Generic.List<FarmPieceSave> pieces = new System.Collections.Generic.List<FarmPieceSave>();
    }

    [System.Serializable]
    private class FarmPieceSave
    {
        public string id;    // SO SaveId
        public int x, z;     // tarlanın yerel hücresi (pivot)
        public GridMaskRotator.Rotation rotation;
    }

    // Tarla kayıttan hep botun düz arkasına kurulur (köşede kaydedildiyse de)
    public string CaptureState()
    {
        var state = new SaveState
        {
            front = walker.IsPlaced ? walker.HeadCell - walker.BodyCell : Vector3Int.forward,
            hasHome = hasHome,
            homeHead = homeHead,
            homeBody = homeBody,
            farmSize = farmSize,
            hasSoil = true,
            soil = soil,
        };
        if (farmGrid != null)
            foreach (FarmPiece piece in farmGrid.Pieces)
                state.pieces.Add(new FarmPieceSave { id = piece.GetData().SaveId, x = piece.FarmCell.x, z = piece.FarmCell.y, rotation = piece.Rotation });
        return JsonUtility.ToJson(state);
    }

    public void RestoreState(string state)
    {
        SaveState saved = JsonUtility.FromJson<SaveState>(state);
        if (saved == null) return;
        if (saved.front != Vector3Int.zero) restoredHeadOffset = saved.front;
        if (saved.farmSize > 0) farmSize = saved.farmSize;
        if (saved.hasSoil) soil = Mathf.Max(0, saved.soil);
        if (saved.hasHome)
        {
            hasHome = true;
            homeHead = saved.homeHead;
            homeBody = saved.homeBody;
        }
        restoredPieces = saved.pieces;
        if (farmGrid != null) RestorePieces();
    }

    // Kayıttaki tarla parçaları: id'den prefab (SaveRegistry), tarladaki yerine
    private void RestorePieces()
    {
        if (restoredPieces == null || SaveManager.Registry == null) return;
        foreach (FarmPieceSave save in restoredPieces)
        {
            if (!(SaveManager.Registry.GetEntity(save.id)?.GetPrefabBase() is FarmPiece prefab))
            {
                Debug.LogWarning($"Boat: tarla parçası '{save.id}' bulunamadı (SaveRegistry), atlandı.", this);
                continue;
            }
            FarmPiece piece = Instantiate(prefab);
            piece.Rotation = save.rotation;
            if (!farmGrid.Attach(piece, new Vector2Int(save.x, save.z))) Destroy(piece.gameObject);
        }
        restoredPieces = null;
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
        UpdateSoilStack();
    }

    private void TryBoard()
    {
        // Mouse botun (ya da tarlasının) üstüne gelince, bot ne kadar uzakta olursa olsun biner. Eldiven geçirgen
        // olduğu için o sırada kıyıda bekliyordu; oradan zıplar.
        GloveCursor g = Glove;
        if (g == null || g.IsRiding || g.IsCaptured || !ReferenceEquals(g.HoveredPassThrough, this)) return;
        if (deck != null && g.HoveredPassThroughCollider != null && g.HoveredPassThroughCollider.transform.IsChildOf(deck)) return; // tarla
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

        Vector3Int direction = GridDirection(input);
        Vector3Int facing = walker.HeadCell - walker.BodyCell;
        facing.y = 0;
        if (direction == -facing) return; // geri gitme yok (arkada tarla var): U çizerek dönülür

        // Geminin o yöndeki kenarının hemen ötesi kara: eldiven o karonun üstüne iner (mouse da oraya taşınır), gemi kalır
        int reach = direction == facing ? ShipBack + 1 : ShipHalfWidth + 1;
        Vector3Int shore = walker.BodyCell + direction * reach;
        if (GridManager.Instance != null && GridManager.Instance.TryGetTopBase(shore.x, shore.z, out GridData land))
        {
            EndRide(land.WorldPosition + Vector3.up * 0.5f);
            return;
        }
        Vector3Int ahead = walker.HeadCell + direction;

        if (direction != facing)
        {
            // Sert dönüş: kıçının (gövde hücresi) etrafında yerinde 90°; gövde kıpırdamadığı için tarla da yerinde kalır
            if (walker.IsMoving) return; // o anki adım bitsin
            Vector3Int pivotHead = walker.BodyCell + direction;
            string pivotBlock = HitsOwnFarm(pivotHead, walker.BodyCell) ? "kendi tarlasına çarpar" : BlockReason(pivotHead, walker.BodyCell);
            if (pivotBlock == null && walker.PivotTo(pivotHead)) lastBlockLog = null;
            else LogBlocked("dönüş: " + (pivotBlock ?? "o an dönülemedi"));
            return;
        }

        string block = HitsOwnFarm(ahead, walker.HeadCell) ? "kendi tarlasına çarpar" : BlockReason(ahead, walker.HeadCell);
        if (block == null)
        {
            walker.StepTo(ahead);
            lastBlockLog = null;
        }
        else LogBlocked("ileri: " + block);
    }

    // Kafa / gövde bu hücrelere giderse neden gidemez (null = gidebilir): geminin ve tarlanın gideceği hücreler
    private string BlockReason(Vector3Int newHead, Vector3Int newBody)
    {
        GridManager grid = GridManager.Instance;
        if (grid == null) return null;

        string Cell(Vector3Int cell)
        {
            if (!grid.TryGetCell(cell, out GridData data)) return $"{cell} grid dışında";
            if (data.Base != null) return $"{cell} kara";
            if (!grid.IsWater(cell)) return $"{cell} su değil (en alt kat değil)";
            if (data.Placeable != null && data.Placeable != this) return $"{cell} dolu ({data.Placeable.name})";
            return null;
        }

        foreach (Vector3Int cell in ShipCells(newHead, newBody))
        {
            string ship = Cell(cell);
            if (ship != null) return "gemi: " + ship;
        }
        if (!HasFarm) return null;
        foreach (Vector3Int cell in FarmSquare(FarmCenterFor(newHead, newBody, out _)))
        {
            string farm = Cell(cell);
            if (farm != null) return "tarla: " + farm;
        }
        return null;
    }

    // Aynı sebep tekrar tekrar yazılmasın (tuş basılı tutulurken)
    private void LogBlocked(string reason)
    {
        if (!logBlockedSteps || reason == lastBlockLog) return;
        lastBlockLog = reason;
        Debug.Log($"Boat: adım atılamadı, {reason}", this);
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
        model.localPosition = modelOffset;
        model.localRotation = Quaternion.Euler(modelRotation);
        model.localScale = Vector3.one * modelScale;

        if (!string.IsNullOrEmpty(wheelPrefix))
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                if (t != model && t.name.StartsWith(wheelPrefix)) wheels.Add((t, t.localRotation));

        seat = new GameObject("Seat").transform;
        seat.SetParent(model, false);
        seat.localPosition = seatOffset;

        // Tıklama / build mode'da silme için gövdeyi saran kutu
        bool any = false;
        Bounds bounds = default;
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
        {
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
    // Çalışırken kurulacak hiyerarşinin aynısı: kök → Visual (su çizgisi) → model (kaydırma, dönüş, ölçek)
    private Matrix4x4 ModelMatrix() => transform.localToWorldMatrix
                                       * Matrix4x4.Translate(Vector3.up * (0.5f + waterline))
                                       * Matrix4x4.TRS(modelOffset, Quaternion.Euler(modelRotation), Vector3.one * modelScale);

    // Prefab modunda gemi görünmüyor (model çalışırken kurulur): tel kafes önizleme, halatın iki ucu (turuncu) ve toprak
    // torbalarının yerleri (kahverengi)
    private void OnDrawGizmosSelected()
    {
        if (showModelGizmo && !Application.isPlaying && modelPrefab != null)
        {
            Matrix4x4 model = ModelMatrix();
            Matrix4x4 toRoot = modelPrefab.transform.worldToLocalMatrix;
            Gizmos.color = new Color(0.8f, 0.85f, 0.9f, 0.35f);
            foreach (MeshFilter filter in modelPrefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Gizmos.matrix = model * toRoot * filter.transform.localToWorldMatrix;
                Gizmos.DrawWireMesh(filter.sharedMesh);
            }
            Gizmos.matrix = Matrix4x4.identity;
        }

        // Halat: gemideki uç ve (yaklaşık) tarladaki uç: tarla düz arkada, merkezi gövdeden FarmLag geride
        Gizmos.color = new Color(1f, 0.55f, 0.1f);
        Vector3 start = transform.TransformPoint(ropeStart);
        Gizmos.DrawSphere(start, 0.08f);
        if (HasFarm)
        {
            Vector3 farmFront = transform.TransformPoint(new Vector3(0f, 0.5f + deckHeight, -0.5f - FarmLag + farmSize * 0.5f));
            Vector3 end = farmFront + transform.rotation * ropeEndOffset;
            Gizmos.DrawSphere(end, 0.08f);
            Gizmos.DrawLine(start, end);
            Gizmos.DrawWireCube(transform.TransformPoint(new Vector3(0f, 0.5f + deckHeight, -0.5f - FarmLag)),
                                new Vector3(farmSize, 0.02f, farmSize));
        }

        if (soilStackAnchor != null)
        {
            Gizmos.color = new Color(0.55f, 0.35f, 0.2f, 0.8f);
            Vector3 size = SoilBagSize();
            foreach (Vector3 slot in SoilSlots(14))
            {
                Gizmos.matrix = soilStackAnchor.localToWorldMatrix;
                Gizmos.DrawWireCube(slot + Vector3.up * size.y * 0.5f, size);
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }

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
        Matrix4x4 model = ModelMatrix();
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
        // Kök: güvertenin üst yüzünün ortası (ölçeksiz; parçalar child olur). Görsel sal ve collider altında.
        deck = new GameObject("Farm").transform;
        deck.SetParent(transform, false);
        farmGrid = deck.gameObject.AddComponent<FarmGrid>();
        farmGrid.Setup(farmSize);
        var deckCollider = deck.gameObject.AddComponent<BoxCollider>(); // parça bırakılacak yer (tarla hedefi)
        deckCollider.size = new Vector3(farmSize, deckThickness, farmSize);
        deckCollider.center = new Vector3(0f, -deckThickness * 0.5f, 0f);

        GameObject plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plank.name = "Farm Deck";
        Destroy(plank.GetComponent<Collider>());
        plank.transform.SetParent(deck, false);
        plank.transform.localPosition = new Vector3(0f, -deckThickness * 0.5f, 0f);
        plank.transform.localScale = new Vector3(farmSize, deckThickness, farmSize);

        Renderer hull = null;
        foreach (Renderer candidate in model.GetComponentsInChildren<Renderer>())
            { hull = candidate; break; }
        var renderer = plank.GetComponent<Renderer>();
        if (hull != null)
        {
            var material = new Material(hull.sharedMaterial);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", null);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", deckColor);
            renderer.sharedMaterial = material;
            farmFx.Setup(deck, material, effectShader);
            farmGrid.Fx = farmFx;
            FarmGrower grower = deck.gameObject.AddComponent<FarmGrower>();
            grower.Fx = farmFx;
            grower.Setup(farmGrid, farmSize, growDuration, coverHeight, material, coverColor, growButtonColor, coverModel);

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
        Vector3 target = GridWalker.FeetPosition(farmCenter) + Vector3.up * (0.5f + deckHeight); // üst yüz
        target.y += Mathf.Sin(Time.time * bobSpeed * Mathf.PI * 2f + 1.7f) * bobAmount;
        Vector3 forward = new Vector3(farmForward.x, 0f, farmForward.z);
        Quaternion rotation = forward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(forward, Vector3.up) : deckRotation;
        if (snapDeck)
        {
            deckPosition = target;
            deckRotation = rotation;
            snapDeck = false;
        }
        else
        {
            float k = 1f - Mathf.Exp(-deckFollow * dt);
            deckPosition = Vector3.Lerp(deckPosition, target, k);
            deckRotation = Quaternion.Slerp(deckRotation, rotation, k);
        }
        deck.SetPositionAndRotation(deckPosition, deckRotation);
        UpdateRope();
    }

    private void UpdateRope()
    {
        if (rope == null) return;
        // Geminin kıçı (ayar): görsele bağlı, gemi sallandıkça uç da onunla gider. ropeStart kökün uzayında, görselin
        // dinlenme pozuna göre yazılmış; görselin uzayına çevrilir
        Vector3 from = visual != null ? visual.TransformPoint(ropeStart - visualRest) : transform.TransformPoint(ropeStart);
        Vector3 to = deck.TransformPoint(new Vector3(0f, 0f, farmSize * 0.5f) + ropeEndOffset); // güvertenin ön kenarı
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
        shipLength = Mathf.Max(3, shipLength | 1);           // tek sayı, en az 3 (kafa hücresi gemide)
        shipWidth = Mathf.Max(1, shipWidth | 1);
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

        moveWeight = Mathf.MoveTowards(moveWeight, distance > 0.0001f ? 1f : 0f, dt * 5f);
        waterFx.Tick(dt, moveWeight > 0.3f);
        lean = Mathf.Lerp(lean, Mathf.Clamp(-yawRate * turnLean, -14f, 14f), 1f - Mathf.Exp(-6f * dt));

        // Çarklar: gidilen yol kadar döner (yerinde dönüşte de)
        wheelAngle += distance * wheelDegreesPerUnit;
        foreach ((Transform wheel, Quaternion rest) in wheels)
            if (wheel != null) wheel.localRotation = rest * Quaternion.AngleAxis(wheelAngle, wheelAxis);

        // Suda inip kalkma ve yalpalama (farklı hızlarda: tekrar eden bir döngü gibi görünmesin)
        float t = Time.time * bobSpeed * Mathf.PI * 2f;
        visual.localPosition = visualRest + Vector3.up * (Mathf.Sin(t) * bobAmount);
        float roll = Mathf.Sin(t * 0.8f) * rockAngle + lean;
        float pitch = Mathf.Sin(t * 0.6f + 1f) * rockAngle * 0.6f - surgePitch * moveWeight; // eksi = burun yukarı
        visual.localRotation = Quaternion.Euler(pitch, 0f, roll);
    }

    // --- Toprak deposu ---

    // Torbanın boyu (dizilim aralığı) ve kat yüksekliği: ayardan ya da modelin sınırlarından
    private Vector3 SoilBagSize()
    {
        Vector3 size = new Vector3(0.3f, 0.3f, 0.3f);
        if (soilBagModel != null)
        {
            var filter = soilBagModel.GetComponentInChildren<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
                size = Vector3.Scale(filter.sharedMesh.bounds.size, filter.transform.lossyScale);
        }
        float spacing = soilBagSpacing > 0f ? soilBagSpacing : Mathf.Max(size.x, size.z);
        float height = soilLayerHeight > 0f ? soilLayerHeight : size.y;
        return new Vector3(spacing, height, spacing);
    }

    // Torbaların yerleri (işaretin uzayında, torbanın tabanı): alt kat 3×3, sonra 2×2 (yarım aralık kaydırılmış), en üstte 1
    private System.Collections.Generic.IEnumerable<Vector3> SoilSlots(int count)
    {
        Vector3 size = SoilBagSize();
        int placed = 0;
        for (int layer = 0, n = 3; n >= 1 && placed < count; layer++, n--)
            for (int z = 0; z < n && placed < count; z++)
                for (int x = 0; x < n && placed < count; x++, placed++)
                    yield return new Vector3((x - (n - 1) * 0.5f) * size.x, layer * size.y, (z - (n - 1) * 0.5f) * size.z);
    }

    private const int MaxSoilBags = 14; // 9 + 4 + 1

    // Toprak değiştikçe torbalar eklenir (küçükten büyüyerek) ya da kaldırılır
    private void UpdateSoilStack()
    {
        if (soilBagModel == null || soilStackAnchor == null) return;
        int target = Mathf.Min(soil, MaxSoilBags);
        if (target == soilBags.Count) return;

        while (soilBags.Count > target)
        {
            Transform last = soilBags[soilBags.Count - 1];
            soilBags.RemoveAt(soilBags.Count - 1);
            if (last != null) Destroy(last.gameObject);
        }
        if (soilBags.Count == target) return;

        // Torbanın tabanı yuvanın tabanına otursun (model origin'i ortada olabilir)
        float bottom = 0f;
        var filter = soilBagModel.GetComponentInChildren<MeshFilter>();
        if (filter != null && filter.sharedMesh != null) bottom = filter.sharedMesh.bounds.min.y * filter.transform.lossyScale.y;

        int index = 0;
        foreach (Vector3 slot in SoilSlots(target))
        {
            if (index++ < soilBags.Count) continue;
            Transform bag = Instantiate(soilBagModel, soilStackAnchor).transform;
            bag.localPosition = slot - Vector3.up * bottom;
            bag.localRotation = Quaternion.Euler(0f, Random.Range(-12f, 12f), 0f); // hepsi aynı açıda durmasın
            foreach (Collider collider in bag.GetComponentsInChildren<Collider>()) Destroy(collider);
            soilBags.Add(bag);
            StartCoroutine(PopIn(bag, (soilBags.Count - 1) * 0.04f));
        }
    }

    private static System.Collections.IEnumerator PopIn(Transform target, float delay)
    {
        Vector3 rest = target.localScale;
        target.localScale = Vector3.zero;
        for (float wait = 0f; wait < delay; wait += Time.deltaTime) yield return null;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
        {
            if (target == null) yield break;
            float overshoot = 1f + Mathf.Sin(t * Mathf.PI) * 0.2f * (1f - t);
            target.localScale = rest * (Mathf.SmoothStep(0f, 1f, t) * overshoot);
            yield return null;
        }
        if (target != null) target.localScale = rest;
    }
}
