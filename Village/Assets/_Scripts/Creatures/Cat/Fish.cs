using UnityEngine;

// Balık: kedinin yemeği (balık tutma gelene kadar geçici olarak build menüsünden konur): sağ tıkla alınır, kedinin üstüne getirilip sol
// tıklanınca kedi yer (Cat besleme barı bir dilim dolar; bkz. Cat.TryFeed). Geçici görünüm: mavi gövde + kuyruk.
// Tohum gibi yığınlanır: tutarken aynı türden balıklar toplanır, kedi her tıklamada yığından bir tane yer.
// Gözler kodla: oynak göz (GooglyEyes), modelde "head" kemiği varsa ona, yoksa modelin köküne bağlanır. Burun +Z.
// Çırpınma kodla (FishFlop): elde tutarken kuyruk çırpar, yerdeyken arada bir çırpınıp zıplar.
public class Fish : Carryable
{
    [Tooltip("Geçici görünümün rengi (model yokken)")]
    [SerializeField] private Color placeholderColor = new Color(0.45f, 0.7f, 0.85f);
    [Tooltip("Oynak gözler: yerleri, boyları ve fiziği (Play'de ayarlanırken model gelince yeri değişir)")]
    [SerializeField] private GooglyEyes eyes = new GooglyEyes();
    [Tooltip("Çırpınma: kemik isimleri, elde ve yerde hareket")]
    [SerializeField] private FishFlop flop = new FishFlop();

    protected override void Awake()
    {
        base.Awake();
        eyes.Build(FindChild(Visual, "head") ?? Visual, PlaceholderShader);
        flop.Build(Visual);
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        bool held = InteractableController.Instance != null && ReferenceEquals(InteractableController.Instance.Held, this);
        flop.Tick(Time.deltaTime, held, !held && PlacedFootprint != null);
        eyes.Tick(Time.deltaTime); // çırpınmadan sonra: gözler bu karenin hareketini hissetsin
    }

    private static Transform FindChild(Transform parent, string childName)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            if (string.Equals(child.name, childName, System.StringComparison.OrdinalIgnoreCase)) return child;
        return null;
    }

    protected override bool Stackable => true;

    protected override void BuildPlaceholder(Transform parent)
    {
        // Burun +Z (gerçek modelle aynı yön), gözler GooglyEyes'ta
        Primitive(PrimitiveType.Sphere, parent, new Vector3(0f, -0.35f, 0f), new Vector3(0.25f, 0.22f, 0.5f), placeholderColor);
        Color tail = placeholderColor * 0.8f;
        tail.a = 1f;
        Primitive(PrimitiveType.Cube, parent, new Vector3(0f, -0.35f, -0.3f), new Vector3(0.04f, 0.16f, 0.15f), tail,
                  Quaternion.Euler(45f, 0f, 0f));
    }

    // Modelin (ya da geçici görünüşün) kökü: kedi yerken ağzında bunun kopyası durur
    public Transform VisualRoot => Visual;

    // Kedi yığından bir tane yedi (son taneyse balık yok olur)
    public void Eat() => UseOne();
}
