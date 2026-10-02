using UnityEngine;

public enum CatMood { Idle, Walk, Happy, Alert, Angry, Sleep, Hunt }

// Kedinin prosedürel katmanı: animasyondan SONRA (LateUpdate) onun pozunun üstüne ekler.
//   yürüyüş: dört vuruşlu (arka sol → ön sol → arka sağ → ön sağ), adımlar katedilen mesafeyle ilerler; gövde zıplar/sallanır
//   kuyruk: ruh haline göre salınım + dönüşlerde ataletle geride kalma
//   nefes: Breath kemiğinin ölçeği (ruh haline göre hız/derinlik)
//   kafa: yürürken ileri bakar; eldiven yakındaysa ona döner (sınırlı açıyla)
//   tepkiler (klip yoksa yedek): pat → kafa ezilip yaylanır, şaplak → kıç kalkar + ön patiler tırmalar
// Eksenler açılışta kedi köküne göre hesaplanır (sağ = sallanma ekseni, yukarı = yana dönme), bone roll önemsiz.
// Eklemeler birikmesin diye: dokunulan kemikler her kare animasyondan ÖNCE (Update) rest pozuna döndürülür;
// animasyon yazdığını yazar, bu katman LateUpdate'te üstüne ekler. Klibi olmayan durumlar da güvenli.
[DefaultExecutionOrder(50)]
public class CatRig : MonoBehaviour
{
    private class Leg
    {
        public Transform upper, bottom;
        public Vector3 upperAxis, bottomAxis; // kedinin sağ ekseni, kemiklerin yerel uzayında
        public float phaseOffset;
        public bool front;
        public float restHeight; // rest pozunda kalçanın (üst kemiğin) yerden yüksekliği
    }

    private struct TailSettings
    {
        public float amplitude, frequency, raise;
        public TailSettings(float amplitude, float frequency, float raise)
        {
            this.amplitude = amplitude; this.frequency = frequency; this.raise = raise;
        }
    }

    [Header("Yürüyüş")]
    [Tooltip("Bir tam döngüde (dört adım) alınan yol (birim). Patiler kayıyorsa ayarla.")]
    [SerializeField] private float strideLength = 0.6f;
    [Tooltip("Bacakların öne-arkaya sallanması (derece)")]
    [SerializeField] private float hipSwing = 28f;
    [Tooltip("Adım atarken alt bacağın bükülmesi (derece)")]
    [SerializeField] private float kneeLift = 50f;
    [SerializeField] private float bodyBob = 0.035f;
    [Tooltip("Gövdenin yürürken yana sallanması (derece)")]
    [SerializeField] private float bodySway = 3f;
    [SerializeField] private float walkBlendSpeed = 6f;

    [Header("Dönüş kavisi")]
    [Tooltip("Dönüş hızı (derece/sn) başına gövdenin dönüş yönüne bükülmesi (derece)")]
    [SerializeField] private float turnBend = 0.08f;
    [Tooltip("En fazla bükülme (derece)")]
    [SerializeField] private float maxTurnBend = 18f;
    [Tooltip("Bükülmenin UpperBody'ye düşen payı (kalanı MiddleBody'de); kavis tek eklemde kırılmasın")]
    [SerializeField, Range(0f, 1f)] private float upperBendShare = 0.4f;

    [Header("Kuyruk")]
    [SerializeField] private float tailTurnInertia = 0.12f;

    [Header("Nefes")]
    [SerializeField] private float breathAmount = 0.035f;
    [SerializeField] private float breathPeriod = 2.8f;

    [Header("Kafa")]
    [Tooltip("Eldiven bu mesafeden yakınsa kafa ona döner")]
    [SerializeField] private float lookRadius = 3.5f;
    [Tooltip("Kafanın sağa / sola en fazla dönüşü (derece, her yöne); toplam aralık bunun iki katı")]
    [SerializeField] private float maxLookYaw = 45f;
    [Tooltip("Kafanın yukarı / aşağı en fazla dönüşü (derece)")]
    [SerializeField] private float maxLookPitch = 15f;
    [SerializeField] private float lookBlendSpeed = 4f;

    [Header("Sırtüstü (göbek modu)")]
    [Tooltip("Gövdenin karna doğru kıvrılması (MiddleBody / UpperBody, derece). Ters dönünce kafa ve ön taraf yerden kalkar.")]
    [SerializeField] private float bellyCurlMiddle = 22f;
    [SerializeField] private float bellyCurlUpper = 18f;
    [Tooltip("Kafanın göğse doğru eğilmesi (derece)")]
    [SerializeField] private float bellyHeadNod = 30f;
    [SerializeField] private float bellyBlendSpeed = 3f;

    [Header("Pusu ve zıplama")]
    [Tooltip("Çömelince gövdenin inmesi (birim)")]
    [SerializeField] private float crouchDepth = 0.12f;
    [Tooltip("Çömelince ön tarafın alçalması (derece); kıç hafif kalkık kalır")]
    [SerializeField] private float crouchPitch = 6f;
    [SerializeField] private float crouchBlendSpeed = 5f;
    [Tooltip("Zıplamadan önce kıç sallama: açı (derece) ve hız (salınım/sn)")]
    [SerializeField] private float wiggleAngle = 9f;
    [SerializeField] private float wiggleSpeed = 4.5f;
    [Tooltip("Havada bacakların açılması: ön patiler ileri, arka patiler geri (derece)")]
    [SerializeField] private float leapReach = 45f;
    [Tooltip("Havada gövdenin eğimi: çıkarken burun yukarı, inerken aşağı (derece)")]
    [SerializeField] private float leapPitch = 18f;
    [Tooltip("Çömelmede patilerin yerde kalması için bacak bükülmesinin çarpanı (patiler gömülüyorsa artır, havada kalıyorsa azalt)")]
    [SerializeField] private float crouchLegBend = 1f;

    [Header("Eldiveni kemirme")]
    [Tooltip("Kafanın eldivene doğru eğilmesi (derece) ve kemirirken inip kalkması (derece, salınım/sn)")]
    [SerializeField] private float chewHeadDown = 18f;
    [SerializeField] private float chewNod = 9f;
    [SerializeField] private float chewSpeed = 3.2f;
    [Tooltip("Arada bir kafayı hızlı hızlı sağa sola sallar (oyuncağı 'öldüren' kedi): açı, hız, kaç saniyede bir, ne kadar sürer")]
    [SerializeField] private float chewShakeAngle = 16f;
    [SerializeField] private float chewShakeSpeed = 7f;
    [SerializeField] private float chewShakeEvery = 1.4f;
    [SerializeField] private float chewShakeSeconds = 0.45f;
    [Tooltip("Ön patilerin eldivene sarılması: ileri uzanma (derece)")]
    [SerializeField] private float chewPawReach = 30f;

    [Header("Yedek tepkiler (klip yoksa)")]
    [SerializeField] private float headPatSquash = 0.22f;
    [Tooltip("Şaplakta kafanın sağa sola sallanması (derece), yana yatması (derece), hızı (salınım/sn) ve süresi (sn)")]
    [SerializeField] private float headShakeYaw = 14f;
    [SerializeField] private float headShakeTilt = 9f;
    [SerializeField] private float headShakeSpeed = 2.6f;
    [SerializeField] private float headShakeSeconds = 1.1f;
    [SerializeField] private float buttRaiseAngle = 14f;
    [SerializeField] private float kneadSwing = 18f;

    private Transform catRoot, root, middle, upperBody, head, breath;
    private Vector3 middleYawAxis, upperYawAxis;
    private Vector3 middlePitchAxis, upperPitchAxis, headPitchAxis, headYawAxis, headRollAxis;
    private float bend, bendVelocity, bellyWeight;

    // Göbek modu: gövde karna doğru kıvrılır (sırtüstü yatan kedinin "C" duruşu)
    public bool BellyUp { get; set; }

    // Pusu: 0..1 çömelme hedefi (yumuşakça geçilir), 0..1 kıç sallama, havadaki ilerleme (0..1; havada değilse <0)
    public float Crouch { get; set; }
    public float Wiggle { get; set; }
    public float LeapProgress { get; set; } = -1f;

    // Eldiveni ağzında kemiriyor: kafa eğik inip kalkar, arada sallar; ön patiler sarılır
    public bool Chew { get; set; }
    private float chewWeight, chewTime;
    private float crouchWeight, wiggleWeight, wigglePhase;
    private Vector3 rootYawAxis;
    private Transform[] tail;
    private Vector3[] tailYaw, tailPitch;
    private Leg[] legs;
    private Vector3 middleSwayAxis, headFaceLocal, rootPitchAxis;
    private Quaternion headRestLocal;
    private Vector3 breathRestScale, headRestScale;
    private float bodyLength = 1f;
    private bool ready;

    // Dokunulan kemiklerin rest pozu: her kare animasyondan önce buna döndürülür
    private Transform[] touched;
    private Vector3[] restPositions, restScales;
    private Quaternion[] restRotations;

    private float walkWeight, walkPhase, lookWeight, tailYawOffset, tailYawVelocity, lastYaw;
    private Vector3 lastPosition;
    private float shakeTime = float.MaxValue;
    private float patTime = float.MaxValue, slapTime = float.MaxValue, slapDuration;
    private TailSettings tailNow = new TailSettings(8f, 0.8f, 0f);

    public CatMood Mood { get; set; } = CatMood.Idle;
    public Transform LookTarget { get; set; }
    private Vector3 lastLookPoint;
    public Transform Head => head;
    public float LookRadius => lookRadius;

    // Kedi modeli kurulunca çağrılır. Kemikler isimden bulunur (orange-cat rig'i).
    public void Setup(Transform catTransform, Transform model)
    {
        catRoot = catTransform;
        root = Find(model, "Root_BottomBody");
        middle = Find(model, "MiddleBody");
        Transform upper = Find(model, "UpperBody");
        head = Find(model, "Head");
        breath = Find(model, "Breath");
        tail = new[] { Find(model, "tail_1"), Find(model, "tail_2"), Find(model, "tail_3"), Find(model, "tail_4") };

        Vector3 right = catRoot.right, up = catRoot.up, forward = catRoot.forward;
        upperBody = upper;
        if (root != null)
        {
            rootPitchAxis = Local(root, right);
            rootYawAxis = Local(root, up);
        }
        if (middle != null)
        {
            middleSwayAxis = Local(middle, forward);
            middleYawAxis = Local(middle, up);
            middlePitchAxis = Local(middle, right);
        }
        if (upperBody != null)
        {
            upperYawAxis = Local(upperBody, up);
            upperPitchAxis = Local(upperBody, right);
        }
        if (head != null)
        {
            headPitchAxis = Local(head, right);
            headYawAxis = Local(head, up);
            headRollAxis = Local(head, forward);
        }
        if (root != null && upper != null) bodyLength = Vector3.Distance(root.position, upper.position);
        if (head != null)
        {
            headFaceLocal = Local(head, forward);
            headRestLocal = head.localRotation;
            headRestScale = head.localScale;
        }
        if (breath != null) breathRestScale = breath.localScale;

        tailYaw = new Vector3[tail.Length];
        tailPitch = new Vector3[tail.Length];
        for (int i = 0; i < tail.Length; i++)
        {
            if (tail[i] == null) continue;
            tailYaw[i] = Local(tail[i], up);
            tailPitch[i] = Local(tail[i], right);
        }

        legs = new[]
        {
            MakeLeg(model, "back", "L", 0f, false),
            MakeLeg(model, "front", "L", 0.25f, true),
            MakeLeg(model, "back", "R", 0.5f, false),
            MakeLeg(model, "front", "R", 0.75f, true),
        };

        lastPosition = catRoot.position;
        lastYaw = catRoot.eulerAngles.y;
        CaptureRestPose();
        ready = true;
    }

    private void CaptureRestPose()
    {
        var list = new System.Collections.Generic.List<Transform> { root, middle, upperBody, head, breath };
        list.AddRange(tail);
        foreach (Leg leg in legs)
            if (leg != null) { list.Add(leg.upper); list.Add(leg.bottom); }
        list.RemoveAll(t => t == null);

        touched = list.ToArray();
        restPositions = new Vector3[touched.Length];
        restRotations = new Quaternion[touched.Length];
        restScales = new Vector3[touched.Length];
        for (int i = 0; i < touched.Length; i++)
        {
            restPositions[i] = touched[i].localPosition;
            restRotations[i] = touched[i].localRotation;
            restScales[i] = touched[i].localScale;
        }
    }

    // Animasyondan önce: önceki karenin eklemeleri silinir (klibi olmayan kemiklerde birikmesin)
    private void Update()
    {
        if (!ready) return;
        for (int i = 0; i < touched.Length; i++)
            touched[i].SetLocalPositionAndRotation(restPositions[i], restRotations[i]);
        for (int i = 0; i < touched.Length; i++)
            touched[i].localScale = restScales[i];
    }

    private Leg MakeLeg(Transform model, string end, string side, float phase, bool front)
    {
        Transform upperBone = Find(model, $"leg_{end}_upper_{side}");
        Transform bottomBone = Find(model, $"leg_{end}_bottom_{side}");
        if (upperBone == null || bottomBone == null) return null;
        return new Leg
        {
            upper = upperBone, bottom = bottomBone,
            upperAxis = Local(upperBone, catRoot.right), bottomAxis = Local(bottomBone, catRoot.right),
            phaseOffset = phase, front = front,
            restHeight = Mathf.Max(0.01f, Vector3.Dot(upperBone.position - catRoot.position, catRoot.up)),
        };
    }

    private static Vector3 Local(Transform bone, Vector3 worldAxis) => Quaternion.Inverse(bone.rotation) * worldAxis;

    private static Transform Find(Transform parent, string boneName)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == boneName) return t;
        return null;
    }

    public void PlayHeadPat() => patTime = 0f;

    // Şaplakta kafa tatlı tatlı sağa sola sallanır (ApplyHead'in sonunda, bakışın üstüne eklenir)
    public void PlayHeadShake() => shakeTime = 0f;

    public void PlayButtSlap(float duration)
    {
        slapTime = 0f;
        slapDuration = duration;
    }

    private void LateUpdate()
    {
        if (!ready) return;
        float dt = Time.deltaTime;

        // Hareket: yatayda katedilen yol ve dönüş hızı
        Vector3 delta = catRoot.position - lastPosition;
        lastPosition = catRoot.position;
        float distance = new Vector2(delta.x, delta.z).magnitude;
        float yaw = catRoot.eulerAngles.y;
        float yawRate = Mathf.DeltaAngle(lastYaw, yaw) / Mathf.Max(dt, 0.0001f);
        lastYaw = yaw;

        bool walking = Mood == CatMood.Walk && distance > 0.0001f;
        walkWeight = Mathf.MoveTowards(walkWeight, walking ? 1f : 0f, dt * walkBlendSpeed);
        walkPhase += distance / Mathf.Max(strideLength, 0.01f);

        ApplyWalk();
        ApplyTurnBend(dt, yawRate);
        ApplyBellyCurl(dt);
        ApplyReactions(dt);
        ApplyPounce(dt);
        ApplyBreath();
        ApplyTail(dt, yawRate);
        ApplyHead(dt);
        ApplyChew(dt);
        ApplyHeadShake(dt);
    }

    // Artı açı (kedinin sağ ekseni etrafında) burnu aşağı indirir
    private void ApplyChew(float dt)
    {
        chewWeight = Mathf.MoveTowards(chewWeight, Chew ? 1f : 0f, dt * 4f);
        if (chewWeight <= 0f)
        {
            chewTime = 0f;
            return;
        }
        chewTime += dt;
        float w = Mathf.SmoothStep(0f, 1f, chewWeight);

        if (head != null)
        {
            float nod = chewHeadDown + chewNod * (0.5f - 0.5f * Mathf.Cos(chewTime * chewSpeed * Mathf.PI * 2f));
            float cycle = Mathf.Repeat(chewTime, Mathf.Max(chewShakeEvery, 0.01f));
            float burst = cycle < chewShakeSeconds ? Mathf.Sin(cycle / Mathf.Max(chewShakeSeconds, 0.01f) * Mathf.PI) : 0f;
            float shake = Mathf.Sin(chewTime * chewShakeSpeed * Mathf.PI * 2f) * chewShakeAngle * burst;
            head.localRotation *= Quaternion.AngleAxis(nod * w, headPitchAxis) * Quaternion.AngleAxis(shake * w, headYawAxis);
        }

        // Ön patiler ileri uzanıp sırayla hafifçe yoğurur (eldiveni tutuyor)
        foreach (Leg leg in legs)
        {
            if (leg == null || !leg.front) continue;
            float knead = Mathf.Sin(chewTime * 5f + leg.phaseOffset * Mathf.PI * 4f) * 6f;
            leg.upper.localRotation *= Quaternion.AngleAxis((-chewPawReach + knead) * w, leg.upperAxis);
            leg.bottom.localRotation *= Quaternion.AngleAxis(chewPawReach * 0.5f * w, leg.bottomAxis);
        }
    }

    // Sağa sola sallanma + yana yatma (yatma biraz gecikmeli: kafa "hıh" der gibi yuvarlanır); hızla girer, sönerek biter
    private void ApplyHeadShake(float dt)
    {
        if (head == null || shakeTime >= headShakeSeconds) return;
        shakeTime += dt;
        float t = Mathf.Min(shakeTime, headShakeSeconds);
        float envelope = Mathf.Min(1f, t / 0.12f) * Mathf.Pow(1f - t / headShakeSeconds, 1.5f);
        float phase = t * headShakeSpeed * Mathf.PI * 2f;
        head.localRotation *= Quaternion.AngleAxis(Mathf.Sin(phase) * headShakeYaw * envelope, headYawAxis)
                            * Quaternion.AngleAxis(Mathf.Sin(phase - 0.9f) * headShakeTilt * envelope, headRollAxis);
    }

    private void ApplyWalk()
    {
        if (walkWeight <= 0f) return;
        float w = Mathf.SmoothStep(0f, 1f, walkWeight);

        foreach (Leg leg in legs)
        {
            if (leg == null) continue;
            float cycle = Mathf.Repeat(walkPhase + leg.phaseOffset, 1f);
            float hip, knee;
            if (cycle < 0.5f)
            {
                // Yerde: bacak önden arkaya iter (artı = geri)
                hip = Mathf.Lerp(-hipSwing, hipSwing, cycle / 0.5f);
                knee = 0f;
            }
            else
            {
                // Havada: patiyi kaldırıp öne atar
                float s = (cycle - 0.5f) / 0.5f;
                hip = Mathf.Lerp(hipSwing, -hipSwing, Mathf.SmoothStep(0f, 1f, s));
                knee = kneeLift * Mathf.Sin(s * Mathf.PI);
            }
            leg.upper.localRotation *= Quaternion.AngleAxis(hip * w, leg.upperAxis);
            leg.bottom.localRotation *= Quaternion.AngleAxis(knee * w, leg.bottomAxis);
        }

        // Her adımda hafif zıplama, gövde yana sallanır
        float bob = Mathf.Abs(Mathf.Sin(walkPhase * Mathf.PI * 4f)) * bodyBob * w;
        if (root != null) root.position += catRoot.up * bob;
        if (middle != null)
            middle.localRotation *= Quaternion.AngleAxis(Mathf.Sin(walkPhase * Mathf.PI * 2f) * bodySway * w, middleSwayAxis);
    }

    // Dönerken gövdenin ön kısmı dönüş yönüne kavislenir (kıç geriden gelir); yayla gelip yayla düzelir.
    // Pozitif dönüş hızı = sağa dönüş; yukarı ekseni etrafında pozitif açı da sağa büker.
    private void ApplyTurnBend(float dt, float yawRate)
    {
        float target = Mathf.Clamp(yawRate * turnBend, -maxTurnBend, maxTurnBend);
        float acceleration = 90f * (target - bend) - 12f * bendVelocity;
        bendVelocity += acceleration * dt;
        bend = Mathf.Clamp(bend + bendVelocity * dt, -maxTurnBend * 1.3f, maxTurnBend * 1.3f);
        if (Mathf.Abs(bend) < 0.01f) return;

        if (middle != null) middle.localRotation *= Quaternion.AngleAxis(bend * (1f - upperBendShare), middleYawAxis);
        if (upperBody != null) upperBody.localRotation *= Quaternion.AngleAxis(bend * upperBendShare, upperYawAxis);
    }

    // Kedinin sağ ekseni etrafında artı açı ön tarafı aşağı, yani karna doğru çevirir: gövde "C" gibi kıvrılır.
    // Kedi ters dönmüşken bu, kafayı ve ön tarafı yerden kaldırır; sırt yerde kalır.
    private void ApplyBellyCurl(float dt)
    {
        bellyWeight = Mathf.MoveTowards(bellyWeight, BellyUp ? 1f : 0f, dt * bellyBlendSpeed);
        if (bellyWeight <= 0f) return;
        float w = Mathf.SmoothStep(0f, 1f, bellyWeight);

        if (middle != null) middle.localRotation *= Quaternion.AngleAxis(bellyCurlMiddle * w, middlePitchAxis);
        if (upperBody != null) upperBody.localRotation *= Quaternion.AngleAxis(bellyCurlUpper * w, upperPitchAxis);
        if (head != null) head.localRotation *= Quaternion.AngleAxis(bellyHeadNod * w, headPitchAxis);
    }

    private void ApplyReactions(float dt)
    {
        // Pat: kafa ezilip yaylanır (gözler kafanın child'ı, onlar da ezilir)
        if (head != null && patTime < 0.7f)
        {
            patTime += dt;
            float squash = Mathf.Sin(patTime / 0.7f * Mathf.PI) * Mathf.Exp(-patTime * 3f) * headPatSquash;
            head.localScale = Vector3.Scale(headRestScale, new Vector3(1f + squash, 1f - squash, 1f + squash));
        }

        // Şaplak: ön taraf alçalır (kıç kalkar), ön patiler sırayla yeri tırmalar
        if (slapTime < slapDuration && root != null)
        {
            slapTime += dt;
            float envelope = Mathf.Min(1f, slapTime / 0.25f) * Mathf.Min(1f, (slapDuration - slapTime) / 0.3f);
            float angle = buttRaiseAngle * envelope;
            root.localRotation *= Quaternion.AngleAxis(angle, rootPitchAxis);
            root.position += catRoot.up * (bodyLength * Mathf.Sin(angle * Mathf.Deg2Rad));

            foreach (Leg leg in legs)
            {
                if (leg == null || !leg.front) continue;
                float knead = Mathf.Sin(slapTime * 16f + leg.phaseOffset * Mathf.PI * 4f) * kneadSwing * envelope;
                leg.upper.localRotation *= Quaternion.AngleAxis(knead, leg.upperAxis);
            }
        }
    }

    // Pusu: gövde alçalır (ön taraf biraz daha), kıç sağa sola sallanır; bacaklar patiler yerde kalacak kadar bükülür.
    // Havada: çıkarken burun yukarı, inerken aşağı; ön patiler ileri uzanır, arka patiler geride açılır.
    private void ApplyPounce(float dt)
    {
        bool airborne = LeapProgress >= 0f;
        // Kalkışta çömelme hızla açılır (yaylanma), inişte tekrar çömelince yumuşar
        float crouchSpeed = airborne ? crouchBlendSpeed * 3f : crouchBlendSpeed;
        crouchWeight = Mathf.MoveTowards(crouchWeight, airborne ? 0f : Crouch, dt * crouchSpeed);
        wiggleWeight = Mathf.MoveTowards(wiggleWeight, airborne ? 0f : Wiggle, dt * 4f);
        wigglePhase += dt * wiggleSpeed;
        if (crouchWeight <= 0f && !airborne) return;
        if (root == null) return;

        if (crouchWeight > 0f)
        {
            float w = Mathf.SmoothStep(0f, 1f, crouchWeight);
            root.position -= catRoot.up * (crouchDepth * w);
            root.localRotation *= Quaternion.AngleAxis(crouchPitch * w, rootPitchAxis);

            // Kıç sallama: kalça bir yana, gövdenin önü ters yana döner, böylece kafa ve göğüs yerinde kalır
            float wiggle = Mathf.Sin(wigglePhase * Mathf.PI * 2f) * wiggleAngle * Mathf.SmoothStep(0f, 1f, wiggleWeight) * w;
            root.localRotation *= Quaternion.AngleAxis(wiggle, rootYawAxis);
            if (middle != null) middle.localRotation *= Quaternion.AngleAxis(-wiggle, middleYawAxis);

            // Bacaklar zikzak bükülür: kalçanın yeni yüksekliği / rest yüksekliği = cos(açı).
            // Ön bacakta dirsek geriye, arka bacakta diz öne (kedinin doğal çömelmesi).
            foreach (Leg leg in legs)
            {
                if (leg == null) continue;
                float height = Vector3.Dot(leg.upper.position - catRoot.position, catRoot.up);
                float ratio = Mathf.Clamp(height / leg.restHeight, 0.2f, 1f);
                float bend = Mathf.Acos(ratio) * Mathf.Rad2Deg * crouchLegBend;
                float sign = leg.front ? 1f : -1f;
                leg.upper.localRotation *= Quaternion.AngleAxis(bend * sign, leg.upperAxis);
                leg.bottom.localRotation *= Quaternion.AngleAxis(-2f * bend * sign, leg.bottomAxis);
            }
        }

        if (airborne)
        {
            float s = Mathf.Clamp01(LeapProgress);
            float stretch = Mathf.Sin(s * Mathf.PI);
            // Artı açı ön tarafı indirir: başta eksi (burun yukarı), sonda artı (burun aşağı)
            root.localRotation *= Quaternion.AngleAxis(-leapPitch * Mathf.Cos(s * Mathf.PI) * Mathf.Min(1f, stretch * 2f), rootPitchAxis);
            foreach (Leg leg in legs)
            {
                if (leg == null) continue;
                // Yürüyüşteki gibi artı = geri: ön bacak ileri (eksi), arka bacak geri (artı)
                leg.upper.localRotation *= Quaternion.AngleAxis((leg.front ? -leapReach : leapReach) * stretch, leg.upperAxis);
                leg.bottom.localRotation *= Quaternion.AngleAxis((leg.front ? -0.3f : 0.3f) * leapReach * stretch, leg.bottomAxis);
            }
        }
    }

    private void ApplyBreath()
    {
        if (breath == null) return;
        float period = breathPeriod, amount = breathAmount;
        switch (Mood)
        {
            case CatMood.Sleep: period *= 1.6f; amount *= 1.5f; break;
            case CatMood.Walk: period *= 0.45f; break;
            case CatMood.Happy: period *= 0.6f; amount *= 0.8f; break;
            case CatMood.Angry: period *= 0.3f; break;
        }
        float factor = 1f + Mathf.Sin(Time.time / period * Mathf.PI * 2f) * amount;
        // Mırlama: nefese çok hafif hızlı titreme eklenir
        if (Mood == CatMood.Happy) factor += Mathf.Sin(Time.time * 60f) * amount * 0.15f;
        breath.localScale = breathRestScale * factor;
    }

    private void ApplyTail(float dt, float yawRate)
    {
        TailSettings target = Mood switch
        {
            CatMood.Walk => new TailSettings(12f, 2.2f, 18f),
            CatMood.Happy => new TailSettings(4f, 9f, 28f),
            CatMood.Alert => new TailSettings(5f, 12f, 35f),
            CatMood.Angry => new TailSettings(30f, 5f, 5f),
            CatMood.Sleep => new TailSettings(2f, 0.3f, -5f),
            CatMood.Hunt => new TailSettings(6f, 4f, -6f), // alçakta, ucu seğiren kuyruk
            _ => new TailSettings(8f, 0.8f, 5f),
        };
        float blend = 1f - Mathf.Exp(-4f * dt);
        tailNow.amplitude = Mathf.Lerp(tailNow.amplitude, target.amplitude, blend);
        tailNow.frequency = Mathf.Lerp(tailNow.frequency, target.frequency, blend);
        tailNow.raise = Mathf.Lerp(tailNow.raise, target.raise, blend);

        // Dönüşte kuyruk ataletle ters yöne savrulur, yayla geri gelir
        float inertiaTarget = -yawRate * tailTurnInertia;
        float acceleration = 120f * (inertiaTarget - tailYawOffset) - 14f * tailYawVelocity;
        tailYawVelocity += acceleration * dt;
        tailYawOffset = Mathf.Clamp(tailYawOffset + tailYawVelocity * dt, -45f, 45f);

        for (int i = 0; i < tail.Length; i++)
        {
            if (tail[i] == null) continue;
            float along = tail.Length > 1 ? (float)i / (tail.Length - 1) : 1f; // uca doğru artan
            float sway = Mathf.Sin(Time.time * tailNow.frequency * Mathf.PI * 2f - i * 0.6f) * tailNow.amplitude * (0.4f + 0.6f * along);
            float yawOffset = tailYawOffset * (1f - along * 0.5f) / tail.Length;
            float pitch = i == 0 ? tailNow.raise : tailNow.raise * 0.15f;
            tail[i].localRotation *= Quaternion.AngleAxis(sway + yawOffset, tailYaw[i])
                                   * Quaternion.AngleAxis(pitch, tailPitch[i]);
        }
    }

    private void ApplyHead(float dt)
    {
        if (head == null) return;

        // Yürürken etrafa bakınmayı (Idle animasyonu) bırakıp ileri bakar
        if (walkWeight > 0f)
            head.localRotation = Quaternion.Slerp(head.localRotation, headRestLocal, walkWeight * 0.85f);

        // Eldiven yakınsa ona döner (gövdeye göre sınırlı açıyla)
        bool canLook = LookTarget != null && Mood != CatMood.Sleep &&
                       Vector3.Distance(LookTarget.position, head.position) < lookRadius;
        if (canLook) lastLookPoint = LookTarget.position; // görüşten çıkınca son noktadan yumuşakça döner
        lookWeight = Mathf.MoveTowards(lookWeight, canLook ? 1f : 0f, dt * lookBlendSpeed);
        if (lookWeight <= 0f) return;

        // Hedef yönü kedinin kendi uzayında yatay (sağ-sol) ve dikey (yukarı-aşağı) açıya ayır, ayrı ayrı sınırla
        Vector3 local = catRoot.InverseTransformDirection(lastLookPoint - head.position);
        float yawAngle = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -maxLookYaw, maxLookYaw);
        float pitchAngle = Mathf.Clamp(Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg,
                                       -maxLookPitch, maxLookPitch);
        Vector3 desired = catRoot.rotation * (Quaternion.Euler(-pitchAngle, yawAngle, 0f) * Vector3.forward);

        Vector3 face = head.rotation * headFaceLocal;
        Quaternion turn = Quaternion.FromToRotation(face, desired);
        head.rotation = Quaternion.Slerp(Quaternion.identity, turn, Mathf.SmoothStep(0f, 1f, lookWeight)) * head.rotation;
    }
}
