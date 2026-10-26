using UnityEngine;

// Balık: kedinin yemeği (balık tutma gelene kadar geçici olarak build menüsünden konur): sağ tıkla alınır, kedinin üstüne getirilip sol
// tıklanınca kedi yer (Cat besleme barı bir dilim dolar; bkz. Cat.TryFeed). Geçici görünüm: mavi gövde + kuyruk.
// Tohum gibi yığınlanır: tutarken aynı türden balıklar toplanır, kedi her tıklamada yığından bir tane yer.
// Gözler kodla: oynak göz (GooglyEyes), modelde "Head" kemiği varsa ona, yoksa modelin köküne bağlanır. Burun +Z.
public class Fish : Carryable
{
    [Tooltip("Geçici görünümün rengi (model yokken)")]
    [SerializeField] private Color placeholderColor = new Color(0.45f, 0.7f, 0.85f);
    [Tooltip("Oynak gözler: yerleri, boyları ve fiziği (Play'de ayarlanırken model gelince yeri değişir)")]
    [SerializeField] private GooglyEyes eyes = new GooglyEyes();

    protected override void Awake()
    {
        base.Awake();
        eyes.Build(FindChild(Visual, "Head") ?? Visual, PlaceholderShader);
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        eyes.Tick(Time.deltaTime);
    }

    private static Transform FindChild(Transform parent, string childName)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            if (child.name == childName) return child;
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

    // Kedi yığından bir tane yedi (son taneyse balık yok olur)
    public void Eat() => UseOne();
}
