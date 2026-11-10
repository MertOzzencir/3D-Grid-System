using System;
using UnityEngine;

// İnsanın prosedürel yürüme animasyonu: klip yok, kemikler koddan döner. Temel duruş modelin kendi pozudur (kambur
// yürüme pozu, Blender'da verildiği gibi): oyun başında her kemiğin yerel dönüşü saklanır, hareket onun üstüne eklenir.
// Kemikler isimle bulunur (büyük/küçük harf duyarsız): Root, Middle-Chest, Neck, Head, UpperArm/BottomArm.L/R,
// UpperLeg/BottomLeg/FootBottom.L/R. Bulunamayan kemik atlanır (oyun bozulmaz).
// Dönüş eksenleri kemiğin kendi eksenlerinden değil gövdeden alınır (sağ, ileri, yukarı): Blender'daki kemik eksenleri
// ne olursa olsun bacak öne / arkaya sallanır. Yürürken: bacaklar ve kollar zıt sallanır, diz öne giden bacakta bükülür,
// gövde iki yana yalpalar ve hafif zıplar, göğüs bacaklara ters döner. Dururken: nefes alıp verir.
public class SlaveRig : MonoBehaviour
{
    [Serializable]
    private class WalkSettings
    {
        [Tooltip("Bir adımın boyu (birim): yürüme döngüsünün hıza göre temposu")]
        public float strideLength = 0.4f;
        [Tooltip("Bacakların öne / arkaya salınımı (derece)")]
        public float legSwing = 26f;
        [Tooltip("Öne giden bacakta diz bükülmesi (derece)")]
        public float kneeBend = 35f;
        [Tooltip("Kolların salınımı (derece, bacaklara zıt)")]
        public float armSwing = 18f;
        [Tooltip("Ön kolun ek salınımı (derece)")]
        public float forearmSwing = 10f;
        [Tooltip("Gövdenin iki yana yalpalaması (derece)")]
        public float sway = 5f;
        [Tooltip("Göğsün bacaklara ters dönmesi (derece)")]
        public float chestTwist = 6f;
        [Tooltip("Adım başına zıplama (birim)")]
        public float bob = 0.04f;
        [Tooltip("Kafanın adımla sallanması (derece)")]
        public float headNod = 4f;
        [Tooltip("Yürümeye başlama / durma yumuşaklığı")]
        public float blendSharpness = 6f;
    }

    [Serializable]
    private class IdleSettings
    {
        [Tooltip("Nefes hızı (saniyedeki nefes)")]
        public float breathRate = 0.3f;
        [Tooltip("Nefeste göğsün öne / arkaya oynaması (derece)")]
        public float breathChest = 2.5f;
        [Tooltip("Nefeste kafanın oynaması (derece)")]
        public float breathHead = 2f;
    }

    [Serializable]
    private class HeldSettings
    {
        [Tooltip("Kolların gidilen yöne uzanması: hız başına ne kadar öne (1 birim/sn hızda yerçekimine oranı)")]
        public float armReach = 0.9f;
        [Tooltip("Kolların bu yöne dönme gücü (0 kendi pozu, 1 tam o yöne)")]
        [Range(0f, 1f)] public float armWeight = 0.85f;
        [Tooltip("Bacakların geride sürüklenmesi: hız başına ne kadar arkaya")]
        public float legTrail = 0.6f;
        [Tooltip("Bacakların bu yöne dönme gücü")]
        [Range(0f, 1f)] public float legWeight = 0.55f;
        [Tooltip("Hızın etkisinin üst sınırı (birim / saniye): savurunca kollar tamamen yatay olmasın")]
        public float maxSpeed = 3f;
        [Tooltip("Uzuvların hıza yetişme yayı: yüksek daha çabuk")]
        public float limbStiffness = 35f;
        [Tooltip("Yayın sönümü: düşük olursa uzuvlar durunca biraz ileri geri sallanır")]
        public float limbDamping = 7f;
        [Tooltip("Gövdenin harekete göre bükülmesi (derece, 1 birim/sn hızda)")]
        public float bodyBend = 8f;
        [Tooltip("Gövdenin dururken hafif sallanması (derece)")]
        public float bodySway = 4f;
        [Tooltip("Gövde sallanma hızı (saniyedeki salınım)")]
        public float bodySwayRate = 0.5f;
        [Tooltip("Taşınırken kafanın kalkması (derece; yere değil öne baksın)")]
        public float headLift = 20f;
    }

    [Serializable]
    private class CagedSettings
    {
        [Tooltip("Başlangıçta gövdenin öne eğilmesi (derece). Eller yere değene kadar kendisi ayarlanır.")]
        public float startBend = 70f;
        [Tooltip("Eğilmenin sınırları (derece)")]
        public Vector2 bendLimits = new Vector2(20f, 85f);
        [Tooltip("Dizin yerden yüksekliği (birim; dizin kalınlığının yarısı)")]
        public float kneeRadius = 0.07f;
        [Tooltip("Elin (bilek) yerden yüksekliği (birim)")]
        public float handRadius = 0.05f;
        [Tooltip("Kafanın kalkması (derece): yere değil öne baksın")]
        public float headLift = 35f;
        [Tooltip("Dört ayak pozuna geçiş yumuşaklığı")]
        public float blendSharpness = 25f;
    }

    [Serializable]
    private class SlapSettings
    {
        [Tooltip("Şaplakta gövdenin öne sarsılması (derece; 0 kapalı: şaplak hissi kıçtaki jöle dalgasından gelir, kullanıcı)")]
        public float kick = 0f;
        [Tooltip("Şaplakta kafanın sağa sola sallanması (derece, kedideki gibi)")]
        public float headShake = 8f;
        [Tooltip("Sarsıntının süresi (saniye)")]
        public float duration = 0.6f;
        [Tooltip("Süre boyunca kaç kez sallansın")]
        public float oscillations = 2.5f;
    }

    [Tooltip("Modelin kökü (Model child'ı); boşsa ilk SkinnedMeshRenderer'ın modelinden bulunur")]
    [SerializeField] private Transform model;
    [SerializeField] private WalkSettings walk = new WalkSettings();
    [SerializeField] private IdleSettings idle = new IdleSettings();
    [SerializeField] private HeldSettings heldSettings = new HeldSettings();
    [SerializeField] private CagedSettings cagedSettings = new CagedSettings();
    [SerializeField] private SlapSettings slapSettings = new SlapSettings();

    private class Bone
    {
        public Transform transform;
        public Quaternion baseRotation;
    }

    private Bone root, chest, neck, head, upperArmL, upperArmR, lowerArmL, lowerArmR, upperLegL, upperLegR, lowerLegL, lowerLegR;
    private Bone handL, handR, footL, footR;
    private float cagedBlend;  // 0 ayakta, 1 dört ayak (kafeste)
    private float crawlBend;   // gövdenin öne eğilmesi (derece), eller yere değene kadar ayarlanır
    private float crawlDrop;   // modelin aşağı inmesi (dizler yere değsin)
    private float slapTime = float.MaxValue;

    // Şaplak: gövde öne sarsılıp sönerek sallanır, kafa sağa sola
    public void PlaySlap() => slapTime = 0f;
    private Vector3 modelBasePosition;
    private float phase;     // yürüme döngüsü (radyan)
    private float walkBlend; // 0 duruyor, 1 yürüyor
    private float breathTime;
    private float heldBlend; // 0 yerde, 1 taşınıyor
    private Vector3 limbVelocity, limbVelocitySpeed; // uzuvların gördüğü taşıma hızı (yayla gecikmeli) ve yayın hızı
    private bool ready;

    private void Start()
    {
        if (model == null)
        {
            var skinned = GetComponentInChildren<SkinnedMeshRenderer>();
            if (skinned != null) model = skinned.transform.parent;
        }
        if (model == null) return;

        modelBasePosition = model.localPosition;
        root = Find("Root");
        chest = Find("Middle-Chest");
        neck = Find("Neck");
        head = Find("Head");
        upperArmL = Find("UpperArm.L");
        upperArmR = Find("UpperArm.R");
        lowerArmL = Find("BottomArm.L");
        lowerArmR = Find("BottomArm.R");
        upperLegL = Find("UpperLeg.L");
        upperLegR = Find("UpperLeg.R");
        lowerLegL = Find("BottomLeg.L");
        lowerLegR = Find("BottomLeg.R");
        handL = Find("Hand.L");
        handR = Find("Hand.R");
        footL = Find("FootBottom.L");
        footR = Find("FootBottom.R");
        crawlBend = cagedSettings.startBend;
        ready = true;
    }

    private Bone Find(string boneName)
    {
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            if (string.Equals(t.name, boneName, StringComparison.OrdinalIgnoreCase))
                return new Bone { transform = t, baseRotation = t.localRotation };
        return null;
    }

    // Slave her kare çağırır: speed o anki yürüme hızı (dururken 0), held: popodan tutulmuş taşınıyor,
    // heldVelocity: taşınırken elin (popo) dünya hızı, caged: kafeste dört ayak üstünde
    public void Tick(float speed, bool held, Vector3 heldVelocity, bool caged)
    {
        if (!ready) return;

        float dt = Time.deltaTime;
        cagedBlend = Mathf.Lerp(cagedBlend, caged ? 1f : 0f, 1f - Mathf.Exp(-cagedSettings.blendSharpness * dt));
        heldBlend = Mathf.Lerp(heldBlend, held ? 1f : 0f, 1f - Mathf.Exp(-walk.blendSharpness * dt));

        // Uzuvlar hıza yayla yetişir: el durunca biraz daha ileri gidip geri gelirler
        Vector3 target = held ? Vector3.ClampMagnitude(heldVelocity, heldSettings.maxSpeed) : Vector3.zero;
        limbVelocitySpeed += ((target - limbVelocity) * heldSettings.limbStiffness - limbVelocitySpeed * heldSettings.limbDamping) * dt;
        limbVelocity += limbVelocitySpeed * dt;

        walkBlend = Mathf.Lerp(walkBlend, speed > 0.01f ? 1f : 0f, 1f - Mathf.Exp(-walk.blendSharpness * Time.deltaTime));
        phase += speed / Mathf.Max(walk.strideLength, 0.01f) * Mathf.PI * Time.deltaTime;
        breathTime += Time.deltaTime;
    }

    // Kemikler Animator'dan sonra yazılsın diye LateUpdate'te
    private void LateUpdate()
    {
        if (!ready) return;

        Vector3 right = transform.right, forward = transform.forward, up = transform.up;
        float w = walkBlend;
        float s = Mathf.Sin(phase), c = Mathf.Cos(phase);
        float breath = Mathf.Sin(breathTime * idle.breathRate * Mathf.PI * 2f) * (1f - w);

        // Bacaklar: açı + → geriye (sağ ekseni etrafında). Sol bacak öne giderken (cos > 0) dizi bükülür
        float legL = -s * walk.legSwing * w;
        Rotate(upperLegL, right, legL);
        Rotate(upperLegR, right, -legL);
        Rotate(lowerLegL, right, Mathf.Max(0f, c) * walk.kneeBend * w);
        Rotate(lowerLegR, right, Mathf.Max(0f, -c) * walk.kneeBend * w);

        // Kollar bacaklara zıt
        Rotate(upperArmL, right, -legL / Mathf.Max(walk.legSwing, 0.01f) * walk.armSwing);
        Rotate(upperArmR, right, legL / Mathf.Max(walk.legSwing, 0.01f) * walk.armSwing);
        Rotate(lowerArmL, right, -Mathf.Max(0f, s) * walk.forearmSwing * w);
        Rotate(lowerArmR, right, -Mathf.Max(0f, -s) * walk.forearmSwing * w);

        // Gövde: yana yalpalama, göğüs ters dönüş, nefes
        Rotate(root, forward, s * walk.sway * w);
        Rotate(chest, up, -s * walk.chestTwist * w, right, breath * idle.breathChest);
        Rotate(neck, up, s * walk.chestTwist * 0.5f * w);
        Rotate(head, right, Mathf.Cos(phase * 2f) * walk.headNod * w + breath * idle.breathHead);

        // Taşınırken: kollar yerçekimiyle aşağı sarkar ve gidilen yöne uzanır (yakalamaya çalışır gibi), bacaklar geride
        // sürüklenir; kafa hafifçe kalkar
        if (heldBlend > 0.001f)
        {
            Vector3 armDirection = (Vector3.down + limbVelocity * heldSettings.armReach).normalized;
            Vector3 legDirection = (Vector3.down - limbVelocity * heldSettings.legTrail).normalized;
            float arm = heldSettings.armWeight * heldBlend, leg = heldSettings.legWeight * heldBlend;

            // Gövde: hareketin tersine hafif bükülür (yayla gecikmeli hızdan, uzuvlarla birlikte salınır) ve dururken
            // kendi kendine hafifçe kıvrılır. Önce gövde: kollar ve bacaklar onun yeni duruşuna göre nişan alır.
            Vector3 flat = new Vector3(limbVelocity.x, 0f, limbVelocity.z);
            float swayWave = Mathf.Sin(breathTime * heldSettings.bodySwayRate * Mathf.PI * 2f) * heldSettings.bodySway * heldBlend;
            Vector3 bendAxis = flat.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, flat.normalized) : right;
            float bend = -flat.magnitude * heldSettings.bodyBend * heldBlend;
            AddRotation(root, bendAxis, bend * 0.5f, up, swayWave * 0.5f);
            AddRotation(chest, bendAxis, bend, forward, swayWave);
            Aim(upperArmL, lowerArmL, armDirection, arm);
            Aim(lowerArmL, handL, armDirection, arm * 0.7f);
            Aim(upperArmR, lowerArmR, armDirection, arm);
            Aim(lowerArmR, handR, armDirection, arm * 0.7f);
            Aim(upperLegL, lowerLegL, legDirection, leg);
            Aim(lowerLegL, footL, legDirection, leg * 0.6f);
            Aim(upperLegR, lowerLegR, legDirection, leg);
            Aim(lowerLegR, footR, legDirection, leg * 0.6f);
            AddRotation(head, right, -heldSettings.headLift * heldBlend, up, 0f);
        }

        if (cagedBlend > 0.001f) PoseCrawl(right, forward, up);
        else crawlDrop = 0f;

        if (slapTime < slapSettings.duration)
        {
            slapTime += Time.deltaTime;
            float t = slapTime / Mathf.Max(slapSettings.duration, 0.01f);
            float wave = Mathf.Sin(t * slapSettings.oscillations * Mathf.PI * 2f) * (1f - t) * (1f - t);
            AddRotation(root, right, wave * slapSettings.kick, up, 0f);
            AddRotation(head, up, wave * slapSettings.headShake, right, 0f);
        }

        // Adım ortasında (bacaklar dikken) en yüksek
        model.localPosition = modelBasePosition + Vector3.up * (walk.bob * w * (0.5f + 0.5f * Mathf.Cos(phase * 2f)) + crawlDrop);
    }

    // Dört ayak (kafeste): gövde kalçadan öne eğilir, uyluklar ve kollar dik aşağı, baldırlar geriye yerde, kafa öne
    // bakar. Model dizler yere değecek kadar iner; eğilme her kare elleri yere yaklaştıracak yönde ayarlanır (modelin
    // ölçülerinden bağımsız: kol ve bacak boyu ne olursa olsun eller ve dizler yere oturur).
    private void PoseCrawl(Vector3 right, Vector3 forward, Vector3 up)
    {
        float k = cagedBlend;
        AddRotation(root, right, crawlBend * k, up, 0f);
        AddRotation(head, right, -cagedSettings.headLift * k, up, 0f);
        Aim(upperLegL, lowerLegL, Vector3.down, k);
        Aim(upperLegR, lowerLegR, Vector3.down, k);
        Aim(lowerLegL, footL, -forward, k);
        Aim(lowerLegR, footR, -forward, k);
        Aim(upperArmL, lowerArmL, Vector3.down, k);
        Aim(upperArmR, lowerArmR, Vector3.down, k);
        Aim(lowerArmL, handL, Vector3.down, k);
        Aim(lowerArmR, handR, Vector3.down, k);

        if (lowerLegL == null || lowerLegR == null) return;
        // Kemikler şu an geçen karenin inişiyle duruyor: inişsiz yükseklikler bulunur, yenisi hesaplanır
        float floor = transform.position.y - 0.5f;
        // En alçakta kalan dayanak (diz ya da el) yere oturur: hiçbir yer zeminin içine girmez
        float knee = Mathf.Min(lowerLegL.transform.position.y, lowerLegR.transform.position.y) - crawlDrop - cagedSettings.kneeRadius;
        float lowest = knee;
        if (handL != null && handR != null)
            lowest = Mathf.Min(lowest, Mathf.Min(handL.transform.position.y, handR.transform.position.y) - crawlDrop - cagedSettings.handRadius);
        crawlDrop = (floor - lowest) * k;

        if (handL == null || handR == null || k < 0.9f) return;
        // Eller ve dizler aynı inişle duruyor: aradaki fark eğilmeyi söyler
        float kneeNow = Mathf.Min(lowerLegL.transform.position.y, lowerLegR.transform.position.y);
        float handNow = Mathf.Min(handL.transform.position.y, handR.transform.position.y);
        float gap = (handNow - kneeNow) - (cagedSettings.handRadius - cagedSettings.kneeRadius);
        // Eller dizlerden yukarıda kaldıysa daha çok eğil, yere gömüldüyse doğrul
        crawlBend = Mathf.Clamp(crawlBend + gap * 150f * Time.deltaTime, cagedSettings.bendLimits.x, cagedSettings.bendLimits.y);
    }

    // Kemiği (başından çocuğuna giden yönü) dünyada direction'a çevirir; weight kadar (0 hiç, 1 tam)
    private static void Aim(Bone bone, Bone child, Vector3 direction, float weight)
    {
        if (bone == null || child == null || weight <= 0f) return;
        Vector3 current = child.transform.position - bone.transform.position;
        if (current.sqrMagnitude < 0.000001f) return;
        Quaternion turn = Quaternion.FromToRotation(current, direction);
        bone.transform.rotation = Quaternion.Slerp(Quaternion.identity, turn, weight) * bone.transform.rotation;
    }

    // O anki dönüşün üstüne ek (Rotate'ten sonra çağrılır), dünya eksenleri etrafında
    private static void AddRotation(Bone bone, Vector3 axis, float degrees, Vector3 axis2, float degrees2)
    {
        if (bone == null) return;
        Transform parent = bone.transform.parent;
        Quaternion offset = Quaternion.AngleAxis(degrees, parent != null ? parent.InverseTransformDirection(axis) : axis) *
                            Quaternion.AngleAxis(degrees2, parent != null ? parent.InverseTransformDirection(axis2) : axis2);
        bone.transform.localRotation = offset * bone.transform.localRotation;
    }

    // Temel duruşun üstüne, dünya eksenleri etrafında (kemiğin ebeveyninin uzayına çevrilip) döndürür
    private static void Rotate(Bone bone, Vector3 axis, float degrees, Vector3 axis2 = default, float degrees2 = 0f)
    {
        if (bone == null) return;
        Transform parent = bone.transform.parent;
        Quaternion offset = Quaternion.AngleAxis(degrees, parent != null ? parent.InverseTransformDirection(axis) : axis);
        if (degrees2 != 0f)
            offset = Quaternion.AngleAxis(degrees2, parent != null ? parent.InverseTransformDirection(axis2) : axis2) * offset;
        bone.transform.localRotation = offset * bone.baseRotation;
    }
}
