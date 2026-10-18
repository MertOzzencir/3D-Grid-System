using UnityEngine;

// Testere sehpası (Workstation, 2×2): üstüne konan parça (yarım odun, kalas) sehpaya bağlı testereyle kesilir. Testere
// parça varken onun üstünde durur; sapı sağ tıkla tutulup mouse sağa sola sürüklenince (ekrana göre) kesim çizgisi
// boyunca gidip gelir (SawHandle). Bir uçtan öbür uca her varış yarım çekiş sayılır; her çekişte testere biraz derine
// iner, parça sarsılır, talaş (isteğe bağlı efekt) dökülür. strokesToCut tam çekişte (ileri + geri) parça bölünür,
// çıktılar sehpanın soluna ve sağına zıplar. Parça yokken testere sehpanın arkasında, üstünde durur.
// Geçici görünüm (model yokken): masa tablası + 4 ayak; testere: ağız + sap.
public class SawBench : Workstation
{
    [Header("Testere")]
    [Tooltip("Kaç tam çekişte (ileri + geri) kesilir")]
    [SerializeField, Min(1)] private int strokesToCut = 3;
    [Tooltip("Testerenin kesim çizgisi boyunca ortadan her iki yana gidebildiği mesafe (birim)")]
    [SerializeField] private float sawTravel = 0.3f;
    [Tooltip("Mouse hızı: ekranın yüksekliği kadar sağa sola sürükleme testereyi bu kadar (birim) götürür")]
    [SerializeField] private float mouseSpeed = 2.5f;
    [Tooltip("Kesim bitince testerenin parçanın üstünden ne kadar (birim) aşağı inmiş olacağı")]
    [SerializeField] private float cutDepth = 0.6f;
    [Tooltip("Testerenin modeli (isteğe bağlı; pivot ağzın ortasında, ağız yerel X boyunca, dişler aşağıda). Boşsa geçici testere.")]
    [SerializeField] private GameObject sawModel;
    [Tooltip("Her çekişte oynar (isteğe bağlı, talaş)")]
    [SerializeField] private ParticleSystem sawdustEffect;
    [SerializeField] private HitShake hitShake = new HitShake();
    [Tooltip("Testerenin yerine yetişme hızı")]
    [SerializeField] private float sawFollowSpeed = 20f;

    private const float BladeHalfHeight = 0.07f;

    private Transform saw;
    private bool sawing;
    private float lastMouseX;
    private float offset;       // kesim çizgisi boyunca konum (-sawTravel..sawTravel)
    private int lastEnd;        // en son varılan uç (-1 sol, 1 sağ, 0 henüz yok)
    private int halfStrokes;

    protected override void Awake()
    {
        base.Awake();
        BuildSaw();
        saw.SetPositionAndRotation(SawGoal(), SawRotation());
    }

    protected override void BuildPlaceholder(Transform parent)
    {
        Vector3 c = LocalCenter;
        float top = topHeight;
        float legHeight = Mathf.Max(0.05f, top + 0.5f - 0.08f);
        Primitive(PrimitiveType.Cube, parent, c + Vector3.up * (top - 0.04f), new Vector3(1.8f, 0.08f, 1.1f), placeholderColor);
        Color legColor = placeholderColor * 0.8f;
        legColor.a = 1f;
        foreach (Vector2 leg in new[] { new Vector2(-0.8f, -0.45f), new Vector2(0.8f, -0.45f), new Vector2(-0.8f, 0.45f), new Vector2(0.8f, 0.45f) })
            Primitive(PrimitiveType.Cube, parent, c + new Vector3(leg.x, -0.5f + legHeight * 0.5f, leg.y), new Vector3(0.1f, legHeight, 0.1f), legColor);
    }

    // Testere sehpanın çocuğu; sapından tutulur (SawHandle)
    private void BuildSaw()
    {
        var sawObject = new GameObject("Saw");
        sawObject.transform.SetParent(transform, false);
        if (sawModel != null) Instantiate(sawModel, sawObject.transform, false);
        else
        {
            Primitive(PrimitiveType.Cube, sawObject.transform, Vector3.zero, new Vector3(0.7f, BladeHalfHeight * 2f, 0.02f), new Color(0.75f, 0.77f, 0.8f));
            Primitive(PrimitiveType.Cube, sawObject.transform, new Vector3(0.42f, 0.03f, 0f), new Vector3(0.16f, 0.14f, 0.06f), new Color(0.45f, 0.28f, 0.16f));
        }
        var box = sawObject.AddComponent<BoxCollider>();
        box.size = new Vector3(0.9f, 0.25f, 0.15f);
        box.center = new Vector3(0.05f, 0f, 0f);
        sawObject.AddComponent<SawHandle>().Init(this);
        saw = sawObject.transform;
    }

    protected override void OnHeld() => ResetCut();
    protected override void OnReleased() => ResetCut();

    private void ResetCut()
    {
        offset = 0f;
        lastEnd = 0;
        halfStrokes = 0;
    }

    private void LateUpdate()
    {
        if (saw == null) return;
        float t = 1f - Mathf.Exp(-sawFollowSpeed * Time.deltaTime);
        saw.position = Vector3.Lerp(saw.position, SawGoal(), t);
        saw.rotation = Quaternion.Slerp(saw.rotation, SawRotation(), t);
    }

    // Ağız kesim çizgisi (kameranın sağı) boyunca, dik düzlemde
    private static Quaternion SawRotation()
    {
        Vector3 right = CameraRight();
        return Quaternion.LookRotation(Vector3.Cross(right, Vector3.up), Vector3.up);
    }

    // Parça varken onun üstünde (kesildikçe iner), yokken sehpanın arkasında, tablanın üstünde
    private Vector3 SawGoal()
    {
        Vector3 right = CameraRight();
        if (CanWork)
        {
            // Kesildikçe iner; alçak parçada (kalas) boyuyla orantılı daha az
            float sink = cutDepth * Mathf.Min(1f, HeightOf(held)) * Mathf.Clamp01(halfStrokes / (strokesToCut * 2f));
            return PieceTop + Vector3.up * (BladeHalfHeight - sink) + right * offset;
        }
        Vector3 back = Vector3.Cross(right, Vector3.up); // kameradan uzağa
        return Center + Vector3.up * (topHeight + BladeHalfHeight) + back * 0.4f;
    }

    // --- SawHandle: sap sağ tıkla tutulunca ---

    public void BeginSaw()
    {
        sawing = true;
        lastMouseX = Input.mousePosition.x;
    }

    // Kesilecek parça yoksa false (tutma biter)
    public bool TickSaw()
    {
        if (!sawing || !CanWork) return false;
        float dx = Input.mousePosition.x - lastMouseX;
        lastMouseX = Input.mousePosition.x;
        offset = Mathf.Clamp(offset + dx / Mathf.Max(1f, Screen.height) * mouseSpeed, -sawTravel, sawTravel);

        // Bir uca varış: öbür uçtan geldiyse yarım çekiş
        int end = offset >= sawTravel * 0.9f ? 1 : offset <= -sawTravel * 0.9f ? -1 : 0;
        if (end != 0 && end != lastEnd)
        {
            if (lastEnd != 0) Stroke();
            lastEnd = end;
        }
        return CanWork;
    }

    public void EndSaw() => sawing = false;

    private void Stroke()
    {
        halfStrokes++;
        if (sawdustEffect != null)
        {
            sawdustEffect.transform.position = PieceTop;
            sawdustEffect.Play();
        }
        if (halfStrokes < strokesToCut * 2)
        {
            StartCoroutine(hitShake.Play(held.transform, held.transform.localRotation, CameraRight() * lastEnd));
            return;
        }
        if (visual != null) StartCoroutine(hitShake.Play(visual, visualRest, CameraRight()));
        SplitHeld();
    }
}
