using UnityEngine;

// Kedinin gözleri (Village/Cat Eyes shader'ı): kırpma, ruh haline göre ifade, eldivene (ya da etrafa) bakma.
// Değerler her göze MaterialPropertyBlock ile gönderilir; geçişler yumuşatılır.
//   Happy → ^ ^   Sleep → ‿ (kapalı)   Angry → çatık   Alert (şaplak) → şaşkın, kocaman
public class CatEyes : MonoBehaviour
{
    private static readonly int OpenId = Shader.PropertyToID("_Open");
    private static readonly int HappyId = Shader.PropertyToID("_Happy");
    private static readonly int AngryId = Shader.PropertyToID("_Angry");
    private static readonly int SurpriseId = Shader.PropertyToID("_Surprise");
    private static readonly int LookId = Shader.PropertyToID("_Look");
    private static readonly int SideId = Shader.PropertyToID("_Side");

    [Tooltip("Village/Cat Eyes shader'lı materyal. Boşsa shader'dan yeni materyal oluşturulur.")]
    [SerializeField] private Material eyeMaterial;
    [Tooltip("Sağ gözün UV'si sola göre aynalıysa açık: parlamalar iki gözde aynı yöne baksın")]
    [SerializeField] private bool rightEyeMirrored = true;
    [Tooltip("Gözler eldivenin ters yönüne kayıyorsa çevir")]
    [SerializeField] private bool invertLookX, invertLookY;

    [Header("Kırpma")]
    [SerializeField] private Vector2 blinkInterval = new Vector2(2f, 6f);
    [SerializeField] private float blinkDuration = 0.14f;

    [Header("Bakış")]
    [SerializeField] private float lookRadius = 4f;
    [Tooltip("Hedef yokken arada bir rastgele bakınma (saniye aralığı)")]
    [SerializeField] private Vector2 glanceInterval = new Vector2(1f, 3f);
    [SerializeField] private float expressionSpeed = 8f;

    private Transform catRoot, head;
    private Renderer left, right;
    private MaterialPropertyBlock block;
    private float open = 1f, happy, angry, surprise;
    private Vector2 look, glance;
    private float nextBlink, blinkTime = float.MaxValue, nextGlance;

    public CatMood Mood { get; set; } = CatMood.Idle;
    public Transform LookTarget { get; set; }

    public void Setup(Transform catTransform, Transform headBone, Renderer leftEye, Renderer rightEye)
    {
        catRoot = catTransform;
        head = headBone != null ? headBone : catTransform;
        left = leftEye;
        right = rightEye;
        block = new MaterialPropertyBlock();

        Material material = eyeMaterial != null ? eyeMaterial : new Material(Shader.Find("Village/Cat Eyes"));
        if (left != null) left.sharedMaterial = material;
        if (right != null) right.sharedMaterial = material;
        ScheduleBlink();
    }

    private void ScheduleBlink() => nextBlink = Time.time + Random.Range(blinkInterval.x, blinkInterval.y);

    private void Update()
    {
        if (block == null) return;
        float dt = Time.deltaTime;
        float k = 1f - Mathf.Exp(-expressionSpeed * dt);

        // İfade hedefleri
        float targetOpen = Mood == CatMood.Sleep ? 0f : 1f;
        happy = Mathf.Lerp(happy, Mood == CatMood.Happy ? 1f : 0f, k);
        angry = Mathf.Lerp(angry, Mood == CatMood.Angry ? 1f : 0f, k);
        // Pusudayken de gözler iri açılır (avlanan kedi)
        surprise = Mathf.Lerp(surprise, Mood == CatMood.Alert || Mood == CatMood.Hunt ? 1f : 0f, k);

        // Kırpma: kısa bir kapanıp açılma (uyurken ve mutluyken yok)
        if (Mood != CatMood.Sleep && Mood != CatMood.Happy && Time.time >= nextBlink)
        {
            blinkTime = 0f;
            ScheduleBlink();
        }
        float blink = 0f;
        if (blinkTime < blinkDuration)
        {
            blinkTime += dt;
            blink = Mathf.Sin(Mathf.Clamp01(blinkTime / blinkDuration) * Mathf.PI);
        }
        open = Mathf.Lerp(open, targetOpen, k);
        float finalOpen = open * (1f - blink);

        // Bakış: eldiven yakınsa ona, yoksa arada bir rastgele
        Vector2 targetLook;
        if (LookTarget != null && Vector3.Distance(LookTarget.position, head.position) < lookRadius)
        {
            Vector3 direction = (LookTarget.position - head.position).normalized;
            targetLook = new Vector2(Vector3.Dot(direction, catRoot.right), Vector3.Dot(direction, catRoot.up)) * 1.6f;
        }
        else
        {
            if (Time.time >= nextGlance)
            {
                glance = Random.insideUnitCircle * 0.7f;
                nextGlance = Time.time + Random.Range(glanceInterval.x, glanceInterval.y);
            }
            targetLook = glance;
        }
        if (invertLookX) targetLook.x = -targetLook.x;
        if (invertLookY) targetLook.y = -targetLook.y;
        look = Vector2.Lerp(look, Vector2.ClampMagnitude(targetLook, 1f), k);

        Apply(left, finalOpen, 1f, look);
        // Aynalı UV'de bakışın x'i de aynalanır ki iki göz aynı yöne baksın
        Apply(right, finalOpen, rightEyeMirrored ? -1f : 1f, rightEyeMirrored ? new Vector2(-look.x, look.y) : look);
    }

    private void Apply(Renderer eye, float openAmount, float side, Vector2 eyeLook)
    {
        if (eye == null) return;
        eye.GetPropertyBlock(block);
        block.SetFloat(OpenId, openAmount);
        block.SetFloat(HappyId, happy);
        block.SetFloat(AngryId, angry);
        block.SetFloat(SurpriseId, surprise);
        block.SetVector(LookId, eyeLook);
        block.SetFloat(SideId, side);
        eye.SetPropertyBlock(block);
    }
}
