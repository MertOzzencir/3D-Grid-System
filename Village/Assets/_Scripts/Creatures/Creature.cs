using UnityEngine;

// Bütün canlıların (kedi, ileride kuş, balık) ortak çekirdeği: durum makinesi + modüllerin bağlantısı.
// Canlı "yürüyen placeable"dır: build menüsünden konur, kayıtla gelir, durduğu hücrelere başka obje konamaz.
// Pivot kuralı canlıya uymaz: transform canlının kendisinde (hareket modülü sürer); grid'deki yeri
// (OriginWorldPosition + PlacedFootprint) hareket modülü her adımda GridManager.TryMovePlaceable ile günceller.
// İhtiyaçlar (sevgi, açlık) burada değil: her canlı beslenmez, onlar türe özel bileşenlerde.
// Modüller: hareket (türe göre: GridWalker, ileride yüzme), animasyon köprüsü (CreatureAnimator),
// etkileşim bölgeleri (CreatureZone), prosedürel katman.
public abstract class Creature : GridPlaceable
{
    [SerializeField] private CreatureAnimator creatureAnimator;

    public CreatureState CurrentState { get; private set; }
    public float StateTime { get; private set; }
    public CreatureAnimator Animator => creatureAnimator;

    protected virtual void Awake()
    {
        if (creatureAnimator == null) creatureAnimator = GetComponentInChildren<CreatureAnimator>();
    }

    // Model kodla kurulan türler (Cat) animasyon köprüsünü sonradan bağlar
    protected void SetAnimator(CreatureAnimator animator) => creatureAnimator = animator;

    protected virtual void Update()
    {
        StateTime += Time.deltaTime;
        CurrentState?.Tick();
    }

    public void ChangeState(CreatureState next)
    {
        CurrentState?.Exit();
        CurrentState = next;
        StateTime = 0f;
        next?.Enter();
    }

    // Bölgelerden gelen etkileşimler (CreatureZone yönlendirir). Tür hangilerine tepki vereceğine kendisi karar verir.
    public virtual void OnZoneClicked(CreatureZoneType zone, RaycastHit hit) { }
    public virtual bool OnZoneHoldBegin(CreatureZoneType zone) => false; // sağ tık basıldı: kabul ederse true
    public virtual bool OnZoneHoldTick(CreatureZoneType zone) => false;  // basılı: devam ediyorsa true
    public virtual void OnZoneHoldEnd(CreatureZoneType zone) { }
    public virtual void OnZoneHover(CreatureZoneType zone, bool entered) { }
}

// Bir durum: girer, her kare çalışır, çıkar. Tür kendi durumlarını bundan türetir.
public abstract class CreatureState
{
    public virtual string Name => GetType().Name;
    public virtual void Enter() { }
    public virtual void Tick() { }
    public virtual void Exit() { }
}

// Sahibine tipli erişimi olan durum (örn. CreatureState<Cat>)
public abstract class CreatureState<T> : CreatureState where T : Creature
{
    protected readonly T owner;
    protected CreatureState(T owner) { this.owner = owner; }
}
