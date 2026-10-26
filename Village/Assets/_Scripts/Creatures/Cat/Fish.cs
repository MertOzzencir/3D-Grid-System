using UnityEngine;

// Balık: kedinin yemeği (balık tutma gelene kadar geçici olarak build menüsünden konur): sağ tıkla alınır, kedinin üstüne getirilip sol
// tıklanınca kedi yer (Cat besleme barı bir dilim dolar; bkz. Cat.TryFeed). Geçici görünüm: mavi gövde + kuyruk.
// Tohum gibi yığınlanır: tutarken aynı türden balıklar toplanır, kedi her tıklamada yığından bir tane yer.
public class Fish : Carryable
{
    [Tooltip("Geçici görünümün rengi (model yokken)")]
    [SerializeField] private Color placeholderColor = new Color(0.45f, 0.7f, 0.85f);

    protected override bool Stackable => true;

    protected override void BuildPlaceholder(Transform parent)
    {
        Primitive(PrimitiveType.Sphere, parent, new Vector3(0f, -0.35f, 0f), new Vector3(0.5f, 0.22f, 0.25f), placeholderColor);
        Color tail = placeholderColor * 0.8f;
        tail.a = 1f;
        Primitive(PrimitiveType.Cube, parent, new Vector3(-0.3f, -0.35f, 0f), new Vector3(0.15f, 0.16f, 0.04f), tail,
                  Quaternion.Euler(0f, 0f, 45f));
    }

    // Kedi yığından bir tane yedi (son taneyse balık yok olur)
    public void Eat() => UseOne();
}
