using System.Collections;
using UnityEngine;

// Pet kedi (2×1: kafa + gövde). Base karolarında gezer, oturur, uyur; eldivenle sevilir.
//   sol tık kafa → pat-pat (HeadPat)      sol tık kıç → şaplak, kıçını kaldırıp yeri tırmalar (ButtSlap)
//   sağ tık basılı → göbek modu: sırtüstü döner, eldivenle göbeği okşanır (BellyRub); fazla okşanırsa
//   arka ayaklarıyla tekmeler (BunnyKick) ve kaçar.
// Animasyonlar CreatureAnimator ile isimden oynatılır; model gelmeden yer tutucu (iki kutu) kendini kurar ve
// tepkileri basit kutu hareketleriyle gösterir.
[RequireComponent(typeof(GridWalker))]
[DefaultExecutionOrder(60)] // CatRig'den (50) sonra: sırtüstü yere oturtma kemiklerin son pozuyla yapılır
public class Cat : Creature
{
    [Header("Gezinme")]
    [SerializeField] private int wanderRadius = 5;
    [SerializeField] private Vector2 idleSeconds = new Vector2(2f, 5f);
    [SerializeField] private Vector2 sitSeconds = new Vector2(4f, 9f);
    [SerializeField] private Vector2 sleepSeconds = new Vector2(8f, 16f);

    [Header("Sevgi")]
    [Tooltip("0..1, sevildikçe artar (ileride davranışları etkileyecek)")]
    [SerializeField, Range(0f, 1f)] private float affection = 0.5f;
    [Tooltip("Göbek okşanırken birikir; bu sınırı geçerse tekmeler ve kaçar")]
    [SerializeField] private float overpetLimit = 6f;
    [Tooltip("Okşama miktarı (ekran yüksekliği cinsinden mouse yolu) başına aşırı sevilme artışı")]
    [SerializeField] private float overpetPerRub = 1f;
    [Tooltip("Aşırı sevilmenin saniyede sönme miktarı")]
    [SerializeField] private float overpetDecay = 0.4f;
    [Tooltip("Okşama başına sevgi artışı")]
    [SerializeField] private float affectionPerRub = 0.02f;

    [Header("Görüş")]
    [Tooltip("Kedinin önünde eldiveni gördüğü toplam açı (derece). Bunun dışında (arkada, yanda) kalan eldivene bakmaz.")]
    [SerializeField, Range(30f, 270f)] private float lookFieldOfView = 160f;

    [Header("Tepkiler")]
    [SerializeField] private float headPatSeconds = 1.2f;
    [SerializeField] private float buttSlapSeconds = 2.2f;
    [SerializeField] private float bunnyKickSeconds = 0.9f;
    [SerializeField] private int runAwayRadius = 8;

    [Header("Oyun: ele zıplama")]
    [Tooltip("Boş el (base üstünde) görüşe her girdiğinde: kafayla takip → çömelip kıç sallama → elin hücresine zıplama")]
    [SerializeField] private bool pounceAtHand = true;
    [Tooltip("Çömelmeden önce eli kafasıyla izleme süresi (saniye, aralıktan rastgele)")]
    [SerializeField] private Vector2 pounceWatchSeconds = new Vector2(0.6f, 1.2f);
    [Tooltip("Çömelip kıç sallama süresi (saniye)")]
    [SerializeField] private float pounceCrouchSeconds = 1.1f;
    [Tooltip("Süre dolunca el zıplanamaz bir yerdeyse (dolu hücre, çok uzak) bu kadar daha çömelik bekler, sonra vazgeçer")]
    [SerializeField] private float pounceWaitSeconds = 1.5f;
    [Tooltip("En uzak zıplama (kafa hücresinden yatay, hücre)")]
    [SerializeField] private float maxLeapDistance = 3.5f;
    [Tooltip("Yayın yüksekliği (birim); uzaklık arttıkça biraz artar")]
    [SerializeField] private float leapHeight = 0.45f;
    [Tooltip("Zıplama süresi = taban + hücre başına ek (saniye)")]
    [SerializeField] private float leapBaseSeconds = 0.3f;
    [SerializeField] private float leapSecondsPerCell = 0.08f;
    [Tooltip("İnişten sonra çömelip doğrulma süresi (saniye)")]
    [SerializeField] private float landSeconds = 0.4f;
    [Tooltip("Bir oyun bitince yenisi için en az bekleme (saniye)")]
    [SerializeField] private float pounceCooldown = 1f;

    [Header("Şaplak")]
    [Tooltip("Kıça şaplakta vurulan nokta jöle gibi titrer. Kedinin materyali 'Village/Creature Jiggle Lit' olmalı.")]
    [SerializeField] private CreatureJiggle slapJiggle = new CreatureJiggle();

    [Header("Eldiven hareketi")]
    [SerializeField] private float patLift = 0.15f;
    [SerializeField] private float patDuration = 0.28f;
    [SerializeField] private float slapLift = 0.25f;
    [SerializeField] private float slapDuration = 0.16f;

    [Header("Model")]
    [Tooltip("Kedi modeli (FBX). Atanırsa yer tutucu yerine kurulur: gözler kafaya, etkileşim bölgeleri kemiklere bağlanır.")]
    [SerializeField] private GameObject modelPrefab;
    [Tooltip("Modelin kedi köküne (kafa ile gövde hücresinin ortası, yerde) göre yeri. Origin kafa hücresindeyse +0.5 ileri.")]
    [SerializeField] private Vector3 modelOffset = new Vector3(0f, 0f, 0.5f);
    [SerializeField] private Vector3 modelRotation = Vector3.zero;
    [Tooltip("Açılışta modeli patilerin uçları (leg_*_end) yere değecek kadar indirir; Blender'daki origin yüksekliği önemsiz olur")]
    [SerializeField] private bool groundToPaws = true;
    [Tooltip("Mesh'ten kemik collider'ı uydururken yarıçapın alındığı dilim: 0.85 = vertex'lerin %85'i içeride. " +
             "Collider görselden büyükse düşür, parmaklar mesh'e gömülüyorsa artır.")]
    [SerializeField, Range(0.5f, 1f)] private float colliderFitPercentile = 0.85f;
    [Tooltip("Yedek kutu bölgelerin (mesh okunamazsa) genişliği ve yüksekliği")]
    [SerializeField] private Vector2 bodyZoneSize = new Vector2(0.75f, 0.65f);

    [Header("Yer tutucu (model yokken)")]
    [SerializeField] private bool buildPlaceholder = true;
    [SerializeField] private Color placeholderColor = new Color(0.95f, 0.65f, 0.35f);

    private GridWalker walker;
    private GloveCursor glove;
    private CatRig rig;
    private CatEyes eyes;
    private Renderer bodyRenderer; // gövde (en büyük skinned mesh): şaplak titreşimi buna
    private Transform visual;     // model ya da yer tutucunun kökü: göbek modunda döner
    private Transform headBox, bodyBox;
    private float overpet;
    private float bellyLift = 0.6f;
    private readonly System.Collections.Generic.List<Collider> bodyColliders = new System.Collections.Generic.List<Collider>(); // bacak ve kuyruk hariç // sırtüstü dönünce sırt yere otursun diye kaldırma (mesh'ten ölçülür)
    private Vector3 lastMouse;
    private bool handInSight;
    private float lastPounceTime = float.MinValue;

    public GridWalker Walker => walker;
    public float Affection => affection;

    protected override void Awake()
    {
        base.Awake();
        walker = GetComponent<GridWalker>();
        if (modelPrefab != null) BuildModel();
        else if (buildPlaceholder && GetComponentInChildren<CreatureZone>() == null) BuildPlaceholder();
    }

    // Prosedürel katman ve gözler için ruh hali (durumlar girerken söyler)
    private void SetMood(CatMood mood)
    {
        if (rig != null) rig.Mood = mood;
        if (eyes != null) eyes.Mood = mood;
    }

    protected override void Update()
    {
        // Base'ler kayıttan yüklenene kadar yerleşmeyi dene
        if (!walker.IsPlaced)
        {
            if (walker.TryPlaceNear(transform.position)) ChooseNext();
            return;
        }

        overpet = Mathf.Max(0f, overpet - overpetDecay * Time.deltaTime);
        base.Update();

        // Kafa ve gözler eldivene bakar: yakındaysa (CatRig / CatEyes kendi mesafesine bakar) ve görüş alanındaysa
        Transform lookTarget = Glove != null && CanSee(Glove.transform.position) ? Glove.transform : null;
        if (rig != null) rig.LookTarget = lookTarget;
        if (eyes != null) eyes.LookTarget = lookTarget;

        // El görüşe her yeni girdiğinde oyun başlar (görüşte kalan el tekrar tetiklemez)
        bool wasInSight = handInSight;
        handInSight = IsHandInSight();
        if (handInSight && !wasInSight && CanStartPounce()) ChangeState(new PounceState(this));
    }

    // Boş el base üstünde, görüş alanında ve bakma mesafesinde
    private bool IsHandInSight()
    {
        if (Glove == null || !Glove.OnBase || !CanSee(Glove.SurfacePoint)) return false;
        Vector3 eye = rig != null && rig.Head != null ? rig.Head.position : transform.position;
        float radius = rig != null ? rig.LookRadius : 3.5f;
        return Vector3.Distance(Glove.SurfacePoint, eye) < radius;
    }

    // Sadece gezinirken / beklerken / otururken; uyurken, sevilirken ya da tepki verirken değil
    private bool CanStartPounce()
    {
        if (!pounceAtHand || Time.time - lastPounceTime < pounceCooldown) return false;
        return CurrentState is WanderState || CurrentState is TimedState timed && timed.Name != "Sleep";
    }

    // Elin altındaki hücre zıplanabilir mi: boş (base ve placeable yok), altında base, en fazla bir kat fark,
    // menzil içinde ve kedinin kendi hücreleri değil. Gövde, iniş hücresinin kediye en yakın komşusuna konur
    // (kedi zıpladığı yöne bakarak iner).
    private bool TryGetLeapTarget(out Vector3Int head, out Vector3Int body)
    {
        head = body = default;
        if (!handInSight) return false;

        Vector3Int cell = Vector3Int.RoundToInt(Glove.SurfacePoint + Vector3.up * 0.5f);
        Vector3Int from = walker.HeadCell;
        if (cell == walker.HeadCell || cell == walker.BodyCell || Mathf.Abs(cell.y - from.y) > 1) return false;
        if (new Vector2(cell.x - from.x, cell.z - from.z).magnitude > maxLeapDistance) return false;
        if (!GridPathfinder.IsStandable(cell)) return false;

        float best = float.MaxValue;
        foreach (Vector3Int direction in new[] { Vector3Int.right, Vector3Int.left, Vector3Int.forward, Vector3Int.back })
        {
            Vector3Int candidate = cell + direction;
            if (!GridPathfinder.IsStandable(candidate)) continue;
            float distance = (GridWalker.FeetPosition(candidate) - transform.position).sqrMagnitude;
            if (distance >= best) continue;
            best = distance;
            body = candidate;
        }
        if (best == float.MaxValue) return false;
        head = cell;
        return true;
    }

    // Sırtüstüyken her kare: gövde collider'larının en alçak noktası tam yere gelsin (kıvrılma, nefes, okşanırken
    // kıpırdanma ne olursa olsun sırt yerde). Yükseklik yumuşakça takip edilir.
    private void LateUpdate()
    {
        if (!(CurrentState is BellyState) || visual == null || bodyColliders.Count == 0) return;

        float lowest = float.MaxValue;
        foreach (Collider body in bodyColliders)
            if (body != null) lowest = Mathf.Min(lowest, LowestPoint(body));
        if (lowest == float.MaxValue) return;

        float correction = transform.position.y - lowest; // + ise gömülmüş, - ise havada
        Vector3 position = visual.localPosition;
        position.y += correction * (1f - Mathf.Exp(-20f * Time.deltaTime));
        visual.localPosition = position;
    }

    private static float LowestPoint(Collider body)
    {
        Transform t = body.transform;
        float scale = t.lossyScale.x;
        switch (body)
        {
            case SphereCollider sphere:
                return t.TransformPoint(sphere.center).y - sphere.radius * scale;
            case CapsuleCollider capsule:
                Vector3 center = t.TransformPoint(capsule.center);
                Vector3 axis = t.TransformDirection(capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward);
                float half = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius) * scale;
                return Mathf.Min((center + axis * half).y, (center - axis * half).y) - capsule.radius * scale;
            default:
                return body.bounds.min.y;
        }
    }

    // Görüş alanı: kafadan hedefe yatay yön, kedinin baktığı yönden en fazla lookFieldOfView/2 sapabilir.
    // Arkada (ya da tam yanda) kalan eldiveni görmez, ona dönmeye çalışmaz.
    private bool CanSee(Vector3 point)
    {
        Vector3 eye = rig != null && rig.Head != null ? rig.Head.position : transform.position;
        Vector3 toTarget = Vector3.ProjectOnPlane(point - eye, transform.up);
        if (toTarget.sqrMagnitude < 0.0001f) return true; // tam tepesinde
        return Vector3.Angle(transform.forward, toTarget) <= lookFieldOfView * 0.5f;
    }

    // --- Davranış seçimi ---

    private void ChooseNext()
    {
        float roll = Random.value;
        if (roll < 0.45f) ChangeState(new WanderState(this, wanderRadius));
        else if (roll < 0.7f) ChangeState(new TimedState(this, "Idle", idleSeconds));
        else if (roll < 0.9f) ChangeState(new TimedState(this, "SitIdle", sitSeconds));
        else ChangeState(new TimedState(this, "Sleep", sleepSeconds));
    }

    private GloveCursor Glove => glove != null ? glove : glove = FindFirstObjectByType<GloveCursor>();

    // --- Etkileşim (CreatureZone'dan) ---

    public override void OnZoneClicked(CreatureZoneType zone, RaycastHit hit)
    {
        if (CurrentState is BellyState || CurrentState is BunnyKickState) return;
        if (CurrentState is PounceState pounce && pounce.Airborne) return; // havadayken yakalanmaz

        if (zone == CreatureZoneType.Head)
        {
            Glove?.PlayPress(patLift, patDuration);
            affection = Mathf.Clamp01(affection + 0.05f);
            ChangeState(new ReactionState(this, "HeadPat", headPatSeconds, CatMood.Happy, PlaceholderHeadPat()));
            // Klip yoksa prosedürel yedek: kafa ezilip yaylanır
            if (rig != null && Animator != null && !Animator.Has("HeadPat")) rig.PlayHeadPat();
        }
        else if (zone == CreatureZoneType.Rear)
        {
            Glove?.PlayPress(slapLift, slapDuration);
            ChangeState(new ReactionState(this, "ButtSlap", buttSlapSeconds, CatMood.Alert, PlaceholderButtSlap()));
            // Gözler pat-pat'teki gibi mutlu kısılır (kuyruk ve nefes Alert'te kalır)
            if (eyes != null) eyes.Mood = CatMood.Happy;
            // Vurulan nokta jöle gibi titrer (içeri göçüp sekerek söner); kıç kalkmaz, patiler oynamaz
            if (bodyRenderer != null) StartCoroutine(slapJiggle.Play(new[] { bodyRenderer }, hit.point, -hit.normal));
            // Klip yoksa prosedürel yedek: kafa tatlı tatlı sağa sola sallanır
            if (rig != null && Animator != null && !Animator.Has("ButtSlap")) rig.PlayHeadShake();
        }
    }

    public override bool OnZoneHoldBegin(CreatureZoneType zone)
    {
        if (CurrentState is BunnyKickState) return false;
        if (CurrentState is PounceState pounce && pounce.Airborne) return false;
        lastMouse = Input.mousePosition;
        ChangeState(new BellyState(this));
        return true;
    }

    // Göbek okşama: eldiven kedinin üstündeyken mouse'un katettiği yol okşama sayılır
    public override bool OnZoneHoldTick(CreatureZoneType zone)
    {
        if (!(CurrentState is BellyState)) return false;

        Vector3 mouse = Input.mousePosition;
        float rub = (mouse - lastMouse).magnitude / Mathf.Max(Screen.height, 1);
        lastMouse = mouse;
        if (!IsMouseOverMe()) rub = 0f;

        affection = Mathf.Clamp01(affection + rub * affectionPerRub);
        overpet += rub * overpetPerRub;
        if (overpet < overpetLimit) return true;

        // Fazla sevildi: tekmeler, sağ tık bırakılmış sayılır
        overpet = 0f;
        ChangeState(new BunnyKickState(this, bunnyKickSeconds));
        return false;
    }

    public override void OnZoneHoldEnd(CreatureZoneType zone)
    {
        if (CurrentState is BellyState) ChangeState(new ReactionState(this, "RollBack", 0.5f, CatMood.Idle, PlaceholderRoll(false)));
    }

    private bool IsMouseOverMe()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        return Physics.Raycast(ray, out RaycastHit hit) && hit.collider.transform.IsChildOf(transform);
    }

    // --- Durumlar ---

    // Rastgele yakın bir hücreye yürür, varınca sonraki davranışı seçer
    private class WanderState : CreatureState<Cat>
    {
        private readonly int radius;
        public WanderState(Cat owner, int radius) : base(owner) { this.radius = radius; }

        public override void Enter()
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector3Int head = owner.walker.HeadCell;
                var target = head + new Vector3Int(Random.Range(-radius, radius + 1), Random.Range(-1, 2), Random.Range(-radius, radius + 1));
                if (target == head || !GridPathfinder.IsStandable(target)) continue;
                if (owner.walker.MoveTo(target))
                {
                    // Yürüyüş prosedürel; klip varsa (yoksa yok sayılır) Idle'ın kafa bakınması yine kapatılır
                    owner.Animator?.Play("Walk");
                    owner.SetMood(CatMood.Walk);
                    return;
                }
            }
            owner.ChangeState(new TimedState(owner, "Idle", owner.idleSeconds));
        }

        public override void Tick()
        {
            if (!owner.walker.IsMoving) owner.ChooseNext();
        }
    }

    // Bir animasyonu belli süre oynatıp sonraki davranışı seçer (Idle, SitIdle, Sleep)
    private class TimedState : CreatureState<Cat>
    {
        private readonly string animation;
        private readonly float duration;
        public override string Name => animation;

        public TimedState(Cat owner, string animation, Vector2 seconds) : base(owner)
        {
            this.animation = animation;
            duration = Random.Range(seconds.x, seconds.y);
        }

        public override void Enter()
        {
            owner.walker.Stop();
            // Klibi olmayan durumlar Idle'a düşer (örn. SitIdle henüz yoksa)
            bool hasClip = owner.Animator == null || owner.Animator.Has(animation);
            owner.Animator?.Play(hasClip ? animation : "Idle");
            owner.SetMood(animation == "Sleep" ? CatMood.Sleep : CatMood.Idle);
        }

        public override void Tick()
        {
            if (owner.StateTime >= duration) owner.ChooseNext();
        }
    }

    // Tek seferlik tepki (pat, şaplak, geri dönme): animasyon + yer tutucu hareketi, bitince devam
    private class ReactionState : CreatureState<Cat>
    {
        private readonly string animation;
        private readonly float duration;
        private readonly CatMood mood;
        private readonly IEnumerator placeholder;
        private Coroutine routine;
        public override string Name => animation;

        public ReactionState(Cat owner, string animation, float duration, CatMood mood, IEnumerator placeholder) : base(owner)
        {
            this.animation = animation;
            this.duration = duration;
            this.mood = mood;
            this.placeholder = placeholder;
        }

        public override void Enter()
        {
            owner.walker.Stop();
            owner.Animator?.Play(animation, true);
            owner.SetMood(mood);
            if (placeholder != null && owner.visual != null) routine = owner.StartCoroutine(placeholder);
        }

        public override void Tick()
        {
            if (owner.StateTime >= duration) owner.ChooseNext();
        }

        public override void Exit()
        {
            if (routine != null) owner.StopCoroutine(routine);
            owner.ResetPlaceholderPose();
        }
    }

    // Ele zıplama oyunu: izle (kafa eli takip eder) → çömel, kıç salla → elin hücresine zıpla → in, doğrul.
    // Zıplama anında el zıplanamaz bir yerdeyse çömelik bekler (el boş bir hücreye gelirse zıplar), süre dolunca vazgeçer.
    // Klip yoksa her şey CatRig'de prosedürel; "Crouch" / "Pounce" klipleri gelirse oynar.
    private class PounceState : CreatureState<Cat>
    {
        private enum Phase { Watch, Crouch, Leap, Land }
        private Phase phase;
        private float phaseTime;
        private readonly float watchDuration;

        public bool Airborne => phase == Phase.Leap;
        public override string Name => $"Pounce.{phase}";

        public PounceState(Cat owner) : base(owner)
        {
            watchDuration = Random.Range(owner.pounceWatchSeconds.x, owner.pounceWatchSeconds.y);
        }

        public override void Enter()
        {
            owner.walker.Stop(); // o anki adım biter, sonra izlemeye başlar
            owner.Animator?.Play("Idle");
            owner.SetMood(CatMood.Hunt);
        }

        public override void Tick()
        {
            phaseTime += Time.deltaTime;
            switch (phase)
            {
                case Phase.Watch:
                    if (owner.walker.IsMoving) { phaseTime = 0f; return; }
                    if (!owner.handInSight) { owner.ChooseNext(); return; } // el gitti, ilgisi dağıldı
                    if (phaseTime >= watchDuration) Begin(Phase.Crouch);
                    break;

                case Phase.Crouch:
                    if (owner.rig != null)
                    {
                        owner.rig.Crouch = 1f;
                        owner.rig.Wiggle = phaseTime > owner.pounceCrouchSeconds * 0.35f ? 1f : 0f;
                    }
                    if (phaseTime < owner.pounceCrouchSeconds) return;
                    if (owner.TryGetLeapTarget(out Vector3Int head, out Vector3Int body)) Leap(head, body);
                    else if (phaseTime >= owner.pounceCrouchSeconds + owner.pounceWaitSeconds) owner.ChooseNext();
                    break;

                case Phase.Leap:
                    if (owner.rig != null) owner.rig.LeapProgress = owner.walker.JumpProgress;
                    if (owner.walker.IsJumping) return;
                    // İniş: kısa bir çömelme (yaylanma), sonra doğrulur
                    if (owner.rig != null)
                    {
                        owner.rig.LeapProgress = -1f;
                        owner.rig.Crouch = 0.7f;
                        owner.rig.Wiggle = 0f;
                    }
                    Begin(Phase.Land);
                    break;

                case Phase.Land:
                    if (owner.rig != null && phaseTime > 0.12f) owner.rig.Crouch = 0f;
                    if (phaseTime >= owner.landSeconds) owner.ChangeState(new TimedState(owner, "Idle", owner.idleSeconds));
                    break;
            }
        }

        private void Begin(Phase next)
        {
            phase = next;
            phaseTime = 0f;
            if (next == Phase.Crouch) owner.Animator?.Play("Crouch");
        }

        private void Leap(Vector3Int head, Vector3Int body)
        {
            Vector3Int from = owner.walker.HeadCell;
            float distance = new Vector2(head.x - from.x, head.z - from.z).magnitude;
            owner.walker.JumpTo(head, body, owner.leapBaseSeconds + distance * owner.leapSecondsPerCell,
                                owner.leapHeight + distance * 0.06f);
            owner.Animator?.Play("Pounce", true);
            if (owner.rig != null)
            {
                owner.rig.Wiggle = 0f;
                owner.rig.LeapProgress = 0f;
            }
            Begin(Phase.Leap);
        }

        public override void Exit()
        {
            owner.lastPounceTime = Time.time;
            if (owner.rig == null) return;
            owner.rig.Crouch = 0f;
            owner.rig.Wiggle = 0f;
            owner.rig.LeapProgress = -1f;
        }
    }

    // Göbek modu: sırtüstü, sağ tık basılı oldukça okşanır
    private class BellyState : CreatureState<Cat>
    {
        private Coroutine routine;
        public BellyState(Cat owner) : base(owner) { }

        public override void Enter()
        {
            owner.walker.Stop();
            owner.Animator?.Play("RollToBelly");
            owner.SetMood(CatMood.Happy);
            if (owner.rig != null) owner.rig.BellyUp = true; // gövde karna doğru kıvrılır, kafa yerden kalkar
            // Klip yoksa: model (ya da yer tutucu) bütün olarak sırtüstü döner
            if (owner.visual != null && (owner.Animator == null || !owner.Animator.Has("RollToBelly")))
                routine = owner.StartCoroutine(owner.PlaceholderRoll(true));
        }

        public override void Tick()
        {
            // Dönme bitince göbek açık bekleme; okşanıyorsa okşanma animasyonu
            if (owner.StateTime > 0.5f) owner.Animator?.Play(owner.IsMouseOverMe() ? "BellyRub" : "BellyIdle");
        }

        public override void Exit()
        {
            if (owner.rig != null) owner.rig.BellyUp = false;
            if (routine != null) owner.StopCoroutine(routine);
        }
    }

    // Fazla sevildi: arka ayaklarla tekme, sonra uzağa kaçar
    private class BunnyKickState : CreatureState<Cat>
    {
        private readonly float duration;
        private Coroutine routine;
        public BunnyKickState(Cat owner, float duration) : base(owner) { this.duration = duration; }

        public override void Enter()
        {
            owner.Animator?.Play("BunnyKick", true);
            owner.SetMood(CatMood.Angry);
            if (owner.visual != null) routine = owner.StartCoroutine(owner.PlaceholderBunnyKick());
        }

        public override void Tick()
        {
            if (owner.StateTime < duration) return;
            owner.ResetPlaceholderPose();
            owner.ChangeState(new WanderState(owner, owner.runAwayRadius)); // kaçış
        }

        public override void Exit()
        {
            if (routine != null) owner.StopCoroutine(routine);
            owner.ResetPlaceholderPose();
        }
    }

    // --- Gerçek model ---

    // Modeli yerleştirir; gözleri kafa kemiğine takar (FBX'te kökte geliyorlar), etkileşim bölgelerini kemiklere bağlar
    // (sırtüstü dönünce bölgeler de döner), animasyon köprüsünü, prosedürel katmanı ve gözleri kurar.
    private void BuildModel()
    {
        visual = new GameObject("Visual").transform;
        visual.SetParent(transform, false);

        Transform model = Instantiate(modelPrefab, visual).transform;
        model.localPosition = modelOffset;
        model.localRotation = Quaternion.Euler(modelRotation);
        if (groundToPaws) GroundToPaws(model);

        Transform head = FindBone(model, "Head");
        Renderer leftEye = AttachToBone(FindBone(model, "Left_Eyes"), head);
        Renderer rightEye = AttachToBone(FindBone(model, "Right_Eyes"), head);

        // Collider'lar mesh'ten ölçülür (kemik başına kapsül/küre); olmazsa kaba yedek kutular
        if (!BuildFittedColliders(model))
        {
            AddHeadZone(head, FindBone(model, "Head_end"));
            AddBodyZone(FindBone(model, "Root_BottomBody"), FindBone(model, "MiddleBody"), CreatureZoneType.Rear);
            AddBodyZone(FindBone(model, "MiddleBody"), FindBone(model, "UpperBody"), CreatureZoneType.Body);
        }

        CreatureAnimator creatureAnimator = GetComponent<CreatureAnimator>();
        if (creatureAnimator == null) creatureAnimator = gameObject.AddComponent<CreatureAnimator>();
        creatureAnimator.Setup(model.GetComponentInChildren<UnityEngine.Animator>());
        SetAnimator(creatureAnimator);

        rig = GetComponent<CatRig>();
        if (rig == null) rig = gameObject.AddComponent<CatRig>();
        rig.Setup(transform, model);

        eyes = GetComponent<CatEyes>();
        if (eyes == null) eyes = gameObject.AddComponent<CatEyes>();
        eyes.Setup(transform, head, leftEye, rightEye);
    }

    // En alttaki pati ucunu (rest pozunda) kedi kökünün yüksekliğine (yer) indirir
    private void GroundToPaws(Transform model)
    {
        float lowest = float.MaxValue;
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("leg_") && t.name.EndsWith("_end")) lowest = Mathf.Min(lowest, t.position.y);
        if (lowest == float.MaxValue) return;

        model.position += Vector3.up * (transform.position.y - lowest);
    }

    private static Transform FindBone(Transform model, string boneName)
    {
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            if (t.name == boneName) return t;
        Debug.LogWarning($"Cat: modelde '{boneName}' bulunamadı", model);
        return null;
    }

    private static Renderer AttachToBone(Transform part, Transform bone)
    {
        if (part == null) return null;
        if (bone != null) part.SetParent(bone, true);
        return part.GetComponent<Renderer>();
    }

    // Mesh'ten collider: rest pozu bir kez fırınlanır, her vertex en çok bağlı olduğu kemiğe atanır.
    // Her kemiğin vertex bulutuna kapsül (neredeyse yuvarlaksa küre) uydurulur: eksen = bulutun uzanma yönü (PCA),
    // yarıçap = eksene uzaklıkların collider Fit Percentile dilimi (taşkın birkaç vertex şişirmesin).
    // Collider'lar kemiklerin child'ı: animasyonu, yürüyüşü, sırtüstü dönmeyi her karede kendiliğinden takip eder.
    private bool BuildFittedColliders(Transform model)
    {
        SkinnedMeshRenderer skin = null;
        foreach (SkinnedMeshRenderer candidate in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            if (skin == null || candidate.sharedMesh.vertexCount > skin.sharedMesh.vertexCount) skin = candidate;
        bodyRenderer = skin;
        if (skin == null || skin.sharedMesh == null || skin.bones.Length == 0) return false;

        Mesh mesh = skin.sharedMesh;
        if (!mesh.isReadable)
        {
            Debug.LogWarning("Cat: kedi mesh'i okunamıyor; FBX import ayarlarında Model → Read/Write'ı aç. Kaba kutu collider'lar kullanılıyor.", this);
            return false;
        }
        var baked = new Mesh();
        skin.BakeMesh(baked, true);
        Vector3[] vertices = baked.vertices;
        BoneWeight[] weights = mesh.boneWeights;
        Destroy(baked);
        if (weights.Length != vertices.Length) return false;

        // Kemik → dünyadaki vertex'leri. Breath (karın) MiddleBody'ye sayılır: karın ayrı collider olmasın.
        Transform[] bones = skin.bones;
        Transform middle = System.Array.Find(bones, b => b != null && b.name == "MiddleBody");
        var groups = new System.Collections.Generic.Dictionary<Transform, System.Collections.Generic.List<Vector3>>();
        Transform skinTransform = skin.transform;
        for (int i = 0; i < vertices.Length; i++)
        {
            Transform bone = bones[weights[i].boneIndex0];
            if (bone == null) continue;
            if (bone.name == "Breath" && middle != null) bone = middle;
            if (!groups.TryGetValue(bone, out var list)) groups[bone] = list = new System.Collections.Generic.List<Vector3>();
            list.Add(skinTransform.position + skinTransform.rotation * vertices[i]);
        }

        // Sırtın yerden yüksekliği: ters dönünce sırt tam yere otursun
        float backTop = 0f;
        foreach (var pair in groups)
        {
            string boneName = pair.Key.name;
            if (boneName != "Root_BottomBody" && boneName != "MiddleBody" && boneName != "UpperBody") continue;
            foreach (Vector3 p in pair.Value) backTop = Mathf.Max(backTop, p.y - transform.position.y);
        }
        if (backTop > 0.05f) bellyLift = backTop;

        int built = 0;
        foreach (var pair in groups)
            if (pair.Value.Count >= 12 && FitCollider(pair.Key, pair.Value)) built++;
        return built > 0;
    }

    private bool FitCollider(Transform bone, System.Collections.Generic.List<Vector3> points)
    {
        // Ağırlık merkezi ve uzanma yönü (kovaryansın en büyük öz vektörü, kuvvet yinelemesiyle)
        Vector3 center = Vector3.zero;
        foreach (Vector3 p in points) center += p;
        center /= points.Count;

        float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (Vector3 p in points)
        {
            Vector3 d = p - center;
            xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
            yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
        }
        Vector3 axis = new Vector3(1f, 0.7f, 0.3f).normalized;
        for (int iteration = 0; iteration < 24; iteration++)
        {
            axis = new Vector3(xx * axis.x + xy * axis.y + xz * axis.z,
                               xy * axis.x + yy * axis.y + yz * axis.z,
                               xz * axis.x + yz * axis.y + zz * axis.z);
            if (axis.sqrMagnitude < 1e-12f) { axis = Vector3.up; break; }
            axis.Normalize();
        }

        // Eksen boyunca uzanma ve eksenden uzaklıklar
        float min = float.MaxValue, max = float.MinValue;
        var radial = new float[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 d = points[i] - center;
            float along = Vector3.Dot(d, axis);
            min = Mathf.Min(min, along);
            max = Mathf.Max(max, along);
            radial[i] = (d - axis * along).magnitude;
        }
        System.Array.Sort(radial);
        float radius = Mathf.Max(0.02f, radial[Mathf.Clamp(Mathf.RoundToInt(radial.Length * colliderFitPercentile) - 1, 0, radial.Length - 1)]);
        float length = max - min;
        Vector3 middlePoint = center + axis * ((min + max) * 0.5f);

        var zone = new GameObject($"{bone.name} Collider");
        zone.transform.SetParent(bone, false);
        zone.transform.SetPositionAndRotation(middlePoint, Quaternion.LookRotation(axis, Mathf.Abs(Vector3.Dot(axis, transform.up)) > 0.95f ? transform.forward : transform.up));
        float scale = Mathf.Max(bone.lossyScale.x, 0.0001f);

        // Neredeyse yuvarlak (kafa gibi): küre; uzunsa kapsül
        if (length < radius * 2.6f)
        {
            var sphere = zone.AddComponent<SphereCollider>();
            sphere.radius = Mathf.Max(radius, length * 0.5f) / scale;
        }
        else
        {
            var capsule = zone.AddComponent<CapsuleCollider>();
            capsule.direction = 2; // yerel Z = eksen
            capsule.radius = radius / scale;
            capsule.height = Mathf.Max(length, radius * 2f) / scale;
        }

        zone.AddComponent<CreatureZone>().Setup(ZoneForBone(bone.name), this);
        if (!bone.name.StartsWith("leg") && !bone.name.StartsWith("tail")) bodyColliders.Add(zone.GetComponent<Collider>());
        return true;
    }

    // Kafa → kafa bölgesi; kalça ve kuyruk → kıç bölgesi (şaplak); gerisi gövde
    private static CreatureZoneType ZoneForBone(string boneName)
    {
        if (boneName == "Head") return CreatureZoneType.Head;
        if (boneName == "Root_BottomBody" || boneName.StartsWith("tail")) return CreatureZoneType.Rear;
        return CreatureZoneType.Body;
    }

    // Kafa: kafa kemiğinden ucuna uzanan küre
    private void AddHeadZone(Transform head, Transform headEnd)
    {
        if (head == null || headEnd == null) return;
        var zone = new GameObject("Head Zone");
        zone.transform.SetParent(head, false);
        zone.transform.position = (head.position + headEnd.position) * 0.5f;
        var sphere = zone.AddComponent<SphereCollider>();
        sphere.radius = Vector3.Distance(head.position, headEnd.position) * 0.55f / Mathf.Max(head.lossyScale.x, 0.0001f);
        zone.AddComponent<CreatureZone>().Setup(CreatureZoneType.Head, this);
    }

    // Gövde parçası: iki kemik arasında, gövdeyle aynı yönde kutu
    private void AddBodyZone(Transform from, Transform to, CreatureZoneType type)
    {
        if (from == null || to == null) return;
        Vector3 axis = to.position - from.position;
        var zone = new GameObject($"{type} Zone");
        zone.transform.SetParent(from, false);
        zone.transform.SetPositionAndRotation((from.position + to.position) * 0.5f, Quaternion.LookRotation(axis, transform.up));
        var box = zone.AddComponent<BoxCollider>();
        float scale = Mathf.Max(from.lossyScale.x, 0.0001f);
        box.size = new Vector3(bodyZoneSize.x, bodyZoneSize.y, axis.magnitude + bodyZoneSize.x * 0.3f) / scale;
        zone.AddComponent<CreatureZone>().Setup(type, this);
    }

#if UNITY_EDITOR
    // Modelin FBX'indeki bütün animasyon kliplerini animasyon köprüsünün listesine yazar.
    // Döngüler: Idle, Walk, SitIdle, Sleep, BellyIdle, BellyRub; diğerleri tek sefer.
    [ContextMenu("Kedi: klipleri modelden doldur")]
    private void FillClipsFromModel()
    {
        if (modelPrefab == null)
        {
            Debug.LogWarning("Önce Model Prefab'ı ata", this);
            return;
        }

        string path = UnityEditor.AssetDatabase.GetAssetPath(modelPrefab);
        var clips = new System.Collections.Generic.List<AnimationClip>();
        foreach (Object asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) clips.Add(clip);

        var loops = new System.Collections.Generic.HashSet<string> { "Idle", "Walk", "SitIdle", "Sleep", "BellyIdle", "BellyRub" };
        CreatureAnimator creatureAnimator = GetComponent<CreatureAnimator>();
        if (creatureAnimator == null) creatureAnimator = UnityEditor.Undo.AddComponent<CreatureAnimator>(gameObject);
        UnityEditor.Undo.RecordObject(creatureAnimator, "Kedi klipleri");
        creatureAnimator.EditorSetClips(clips.ToArray(), loops.Contains);
        UnityEditor.EditorUtility.SetDirty(creatureAnimator);
        Debug.Log($"Cat: {clips.Count} klip yazıldı: {string.Join(", ", clips.ConvertAll(c => c.name))}", this);
    }
#endif

    // --- Yer tutucu (model yokken): iki kutu + bölgeler ---

    private void BuildPlaceholder()
    {
        visual = new GameObject("Placeholder Visual").transform;
        visual.SetParent(transform, false);

        bodyBox = MakeBox("Body (Rear Zone)", new Vector3(0f, 0.32f, -0.3f), new Vector3(0.65f, 0.55f, 1.2f), CreatureZoneType.Rear);
        headBox = MakeBox("Head (Head Zone)", new Vector3(0f, 0.55f, 0.55f), new Vector3(0.5f, 0.45f, 0.45f), CreatureZoneType.Head);
    }

    private Transform MakeBox(string boxName, Vector3 position, Vector3 size, CreatureZoneType zone)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = boxName;
        box.transform.SetParent(visual, false);
        box.transform.localPosition = position;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().material.color = placeholderColor;
        box.AddComponent<CreatureZone>().Setup(zone, this);
        return box.transform;
    }

    private void ResetPlaceholderPose()
    {
        if (visual == null) return;
        visual.localRotation = Quaternion.identity;
        visual.localPosition = Vector3.zero;
        if (headBox != null) headBox.localScale = new Vector3(0.5f, 0.45f, 0.45f);
        if (bodyBox != null) bodyBox.localRotation = Quaternion.identity;
    }

    // Pat-pat: kafa ezilip yaylanır
    private IEnumerator PlaceholderHeadPat()
    {
        if (headBox == null) yield break; // modelde prosedürel katman yapıyor
        Vector3 rest = new Vector3(0.5f, 0.45f, 0.45f);
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            float squash = Mathf.Sin(t / 0.6f * Mathf.PI) * Mathf.Exp(-t * 3f) * 0.35f;
            headBox.localScale = new Vector3(rest.x * (1f + squash), rest.y * (1f - squash), rest.z * (1f + squash));
            yield return null;
        }
        headBox.localScale = rest;
    }

    // Şaplak: kıç havaya kalkar, gövde ileri geri "tırmalama" ritmiyle sallanır
    private IEnumerator PlaceholderButtSlap()
    {
        if (bodyBox == null) yield break;
        for (float t = 0f; t < buttSlapSeconds; t += Time.deltaTime)
        {
            float raise = Mathf.Min(1f, t / 0.25f) * Mathf.Min(1f, (buttSlapSeconds - t) / 0.3f);
            float knead = Mathf.Sin(t * 18f) * 3f * raise;
            bodyBox.localRotation = Quaternion.Euler(-20f * raise + knead, 0f, 0f);
            yield return null;
        }
        bodyBox.localRotation = Quaternion.identity;
    }

    // Sırtüstü dönme (ve geri)
    private IEnumerator PlaceholderRoll(bool toBelly)
    {
        Quaternion from = visual.localRotation;
        Quaternion to = toBelly ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity;
        // Collider'lar varsa sırtüstü yüksekliği her kare yere oturtma belirler (LateUpdate); bu rutin sadece döndürür
        bool moveHeight = !toBelly || bodyColliders.Count == 0;
        Vector3 lift = toBelly ? Vector3.up * bellyLift : Vector3.zero;
        Vector3 startPosition = visual.localPosition;
        for (float t = 0f; t < 0.45f; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / 0.45f);
            visual.localRotation = Quaternion.Slerp(from, to, k);
            if (moveHeight) visual.localPosition = Vector3.Lerp(startPosition, lift, k);
            yield return null;
        }
        visual.localRotation = to;
        if (moveHeight) visual.localPosition = lift;
    }

    // Tekme: sırtüstüyken hızlı titrer, sonra döner
    private IEnumerator PlaceholderBunnyKick()
    {
        // Yer tutucuda gövde kutusu, modelde bütün model titrer (klip gelene kadar)
        Vector3 rest = visual.localPosition;
        for (float t = 0f; t < bunnyKickSeconds; t += Time.deltaTime)
        {
            float shake = Mathf.Sin(t * 40f);
            if (bodyBox != null) bodyBox.localRotation = Quaternion.Euler(shake * 12f, 0f, 0f);
            else visual.localPosition = rest + Vector3.forward * (shake * 0.05f);
            yield return null;
        }
        visual.localPosition = rest;
    }
}
