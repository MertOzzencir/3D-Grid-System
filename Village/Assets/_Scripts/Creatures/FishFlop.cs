using System;
using UnityEngine;

// Balığın prosedürel çırpınması (klip yok; Fish'te alan, ayarları Fish'in Inspector'ında "Flop"). Modelin kemikleri
// isimle bulunur (head, uppertail, tail; büyük / küçük harf fark etmez). Model gelmeden (geçici görünüm) bir şey yapmaz.
// Kıvrılma kemiğin kendi eksenine değil balığın "yukarı"sı etrafında: Blender → Unity eksen dönüşümü ne olursa olsun
// kuyruk sağa sola sallanır. Kuyruk ucu (tail) modelde kökün çocuğuysa oyunda kuyruk köküne (uppertail) bağlanır: kök
// kıvrılınca uç da onunla gelsin, kopuk durmasın.
//   Elde tutarken: kuyruk sürekli hızlı çırpar (heldFrequency); uç kökten biraz geride (tailLag) ve daha geniş savrulur,
//   kafa ters yöne hafifçe sallanır.
//   Yerdeyken: arada bir (flopInterval) kısa bir çırpınma (flopDuration) ve küçük zıplama (hopHeight), sonra durur.
[Serializable]
public class FishFlop
{
    [SerializeField] private bool enabled = true;
    [Tooltip("Kemik isimleri (büyük / küçük harf fark etmez)")]
    [SerializeField] private string headBone = "head";
    [SerializeField] private string upperTailBone = "uppertail";
    [SerializeField] private string tailBone = "tail";

    [Header("Elde")]
    [Tooltip("Saniyedeki çırpma sayısı")]
    [SerializeField] private float heldFrequency = 6f;
    [Tooltip("Kuyruk kökünün, ucunun ve kafanın en fazla dönüşü (derece)")]
    [SerializeField] private float upperTailAngle = 22f;
    [SerializeField] private float tailAngle = 35f;
    [SerializeField] private float headAngle = 8f;
    [Tooltip("Kuyruk ucunun kökten gecikmesi (tur): dalga gibi kıvrılsın")]
    [SerializeField, Range(0f, 0.5f)] private float tailLag = 0.18f;

    [Header("Yerde")]
    [Tooltip("İki çırpınma arası (saniye, aralıktan rastgele)")]
    [SerializeField] private Vector2 flopInterval = new Vector2(2f, 5f);
    [SerializeField] private float flopDuration = 0.6f;
    [SerializeField] private float flopFrequency = 9f;
    [Tooltip("Çırpınırken zıplama yüksekliği (birim)")]
    [SerializeField] private float hopHeight = 0.12f;

    private Transform root; // modelin kökü (yukarısı ve zıplama)
    private Transform head, upperTail, tail;
    private Quaternion headRest, upperTailRest, tailRest;
    private float appliedHop; // köke eklenmiş zıplama (sadece fark uygulanır: model ofseti Play'de ayarlanabilsin)
    private float time, flopStart = float.MinValue, nextFlop;

    public void Build(Transform model)
    {
        root = model;
        if (!enabled || model == null) return;
        head = Find(model, headBone);
        upperTail = Find(model, upperTailBone);
        tail = Find(model, tailBone);
        if (tail != null && upperTail != null && !tail.IsChildOf(upperTail)) tail.SetParent(upperTail, true);
        if (head != null) headRest = head.localRotation;
        if (upperTail != null) upperTailRest = upperTail.localRotation;
        if (tail != null) tailRest = tail.localRotation;
        nextFlop = UnityEngine.Random.Range(flopInterval.x, flopInterval.y);
    }

    private static Transform Find(Transform parent, string boneName)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            if (string.Equals(child.name, boneName, StringComparison.OrdinalIgnoreCase)) return child;
        return null;
    }

    // held: elde tutuluyor; onGround: grid'de duruyor (yerde)
    public void Tick(float dt, bool held, bool onGround)
    {
        if (!enabled || root == null || (upperTail == null && tail == null)) return;
        time += dt;

        float strength = 0f, frequency = heldFrequency, hop = 0f;
        if (held) strength = 1f;
        else if (onGround)
        {
            nextFlop -= dt;
            if (nextFlop <= 0f)
            {
                flopStart = time;
                nextFlop = UnityEngine.Random.Range(flopInterval.x, flopInterval.y) + flopDuration;
            }
            float t = (time - flopStart) / Mathf.Max(flopDuration, 0.01f);
            if (t >= 0f && t < 1f)
            {
                strength = Mathf.Sin(t * Mathf.PI); // yumuşak başlar, yumuşak biter
                frequency = flopFrequency;
                hop = hopHeight * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) * (1f - t);
            }
        }

        float phase = time * frequency * Mathf.PI * 2f;
        Swing(upperTail, upperTailRest, upperTailAngle * strength * Mathf.Sin(phase));
        Swing(tail, tailRest, tailAngle * strength * Mathf.Sin(phase - tailLag * Mathf.PI * 2f));
        Swing(head, headRest, -headAngle * strength * Mathf.Sin(phase));
        root.localPosition += Vector3.up * (hop - appliedHop);
        appliedHop = hop;
    }

    // Kemiği balığın yukarısı etrafında (ebeveyninin uzayında) dinlenme duruşundan döndürür
    private void Swing(Transform bone, Quaternion rest, float angle)
    {
        if (bone == null) return;
        Vector3 up = bone.parent != null ? bone.parent.InverseTransformDirection(root.up) : root.up;
        bone.localRotation = Quaternion.AngleAxis(angle, up) * rest;
    }
}
