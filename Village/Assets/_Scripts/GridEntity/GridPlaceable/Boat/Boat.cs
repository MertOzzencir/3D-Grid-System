using UnityEngine;

// Su üstünde giden 2×1 bot (yürüyen placeable, kedi gibi): sadece suya konur (CanOccupy), su hücrelerinde
// GridWalker ile gider (Medium = Water); grid'deki yerini GridWalker her adımda günceller.
// Binme: eldiven kıyıdayken (mouse suda) botun yanındaysa biner, koltuğa (Seat) oturur. Bot mouse'un su üstündeki
// hücresine yol bulup gider. Mouse karada botun yanındaki bir yeri gösterince eldiven iner. Mouse uzak bir karayı
// gösterirse bot oraya en yakın su hücresine gider. Binerken oyuncu girişi kilitli (şimdilik sadece gezme).
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

    [Header("Binme / inme")]
    [Tooltip("Eldiven kıyıdayken (mouse suda) botun bir hücresine yataydan bu kadar yakınsa biner")]
    [SerializeField] private float boardDistance = 1.3f;
    [Tooltip("Binmişken mouse karada botun bir hücresine bu kadar yakın bir yeri gösterirse eldiven iner")]
    [SerializeField] private float disembarkDistance = 1.6f;
    [Tooltip("İndikten sonra tekrar binmek için en az bekleme (saniye)")]
    [SerializeField] private float boardCooldown = 0.8f;

    [Header("Kürek")]
    [Tooltip("Bir kürek çekişinde alınan yol (birim)")]
    [SerializeField] private float strokeLength = 0.7f;
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
    [SerializeField] private float turnLean = 0.04f;
    [Tooltip("Giderken burnun kalkması (derece)")]
    [SerializeField] private float surgePitch = 2f;

    private GridWalker walker;
    private GloveCursor glove;
    private Transform visual, seat, leftOar, rightOar;
    private Quaternion leftOarRest, rightOarRest;
    private Vector3 visualRest;
    private Vector3 lastPosition;
    private float lastYaw, rowWeight, rowPhase, lean;
    private bool riding;
    private float lastRideEnd = float.MinValue;
    private Vector3Int lastTarget;
    private Vector3Int? restoredHeadOffset; // kayıttan: arka hücreden ön hücreye

    public bool IsRidden => riding;

    private GloveCursor Glove => glove != null ? glove : glove = FindFirstObjectByType<GloveCursor>();

    private void Awake()
    {
        walker = GetComponent<GridWalker>();
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
        lastTarget = walker.HeadCell;
        InputManager.SetLocked(this, true);
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

        Vector3Int target;
        if (g.MouseOverLand)
        {
            // Botun yanındaki karayı gösteriyor: in
            if (DistanceToBoat(g.MouseLandPoint) <= disembarkDistance)
            {
                EndRide();
                return;
            }
            if (!TryNearestWater(g.MouseLandPoint, 4, out target)) return;
        }
        else
        {
            Vector3 point = g.MouseWaterPoint;
            target = new Vector3Int(Mathf.RoundToInt(point.x), walker.HeadCell.y, Mathf.RoundToInt(point.z));
        }

        // Hedef değişince yeniden yol bulunur (her kare değil)
        if (target == lastTarget) return;
        lastTarget = target;
        if (target == walker.HeadCell || target == walker.BodyCell) walker.Stop();
        else if (walker.IsWalkable(target)) walker.MoveTo(target);
    }

    private void EndRide()
    {
        if (!riding) return;
        riding = false;
        lastRideEnd = Time.time;
        walker.Stop();
        InputManager.SetLocked(this, false);
        if (Glove != null) Glove.EndRide();
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

    // Noktaya en yakın gidilebilir su hücresi (botun katında)
    private bool TryNearestWater(Vector3 point, int radius, out Vector3Int result)
    {
        result = default;
        float best = float.MaxValue;
        int cx = Mathf.RoundToInt(point.x), cz = Mathf.RoundToInt(point.z), y = walker.HeadCell.y;
        for (int dz = -radius; dz <= radius; dz++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            var cell = new Vector3Int(cx + dx, y, cz + dz);
            if (!walker.IsWalkable(cell)) continue;
            float distance = new Vector2(cell.x - point.x, cell.z - point.z).sqrMagnitude;
            if (distance >= best) continue;
            best = distance;
            result = cell;
        }
        return best < float.MaxValue;
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
    }

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

        rowWeight = Mathf.MoveTowards(rowWeight, distance > 0.0001f ? 1f : 0f, dt * 3f);
        rowPhase += distance / Mathf.Max(strokeLength, 0.01f);
        lean = Mathf.Lerp(lean, Mathf.Clamp(-yawRate * turnLean, -8f, 8f), 1f - Mathf.Exp(-4f * dt));

        // Suda inip kalkma ve yalpalama (farklı hızlarda: tekrar eden bir döngü gibi görünmesin)
        float t = Time.time * bobSpeed * Mathf.PI * 2f;
        visual.localPosition = visualRest + Vector3.up * (Mathf.Sin(t) * bobAmount);
        float roll = Mathf.Sin(t * 0.8f) * rockAngle + lean;
        float pitch = Mathf.Sin(t * 0.6f + 1f) * rockAngle * 0.6f - surgePitch * rowWeight; // eksi = burun yukarı
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
