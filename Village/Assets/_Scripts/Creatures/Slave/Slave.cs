using System.Collections.Generic;
using UnityEngine;

// Kafeste bakılan insan (şimdilik kafessiz): grid'de rastgele bir hücre seçer, oraya yürür, bir süre bekler, tekrar.
// Tek hücrelik canlı: ayak izi datadaki gibi (1×1, boyu kadar kat). Kedinin iki hücreli GridWalker'ı yerine kendi basit
// adımlarını atar: her adımın BAŞINDA yeni hücreye geçer (GridManager.TryMovePlaceable), konum iki hücre arasında kayar.
// Pivot kuralı diğer objelerle aynı: kök hücrenin ortasında, model 0.5 aşağıda (tabanda). Yürüme animasyonu SlaveRig'de.
public class Slave : Creature
{
    [Header("Dolaşma")]
    [Tooltip("Birim / saniye")]
    [SerializeField] private float walkSpeed = 0.7f;
    [Tooltip("Hedef, bulunduğu hücreye en fazla bu kadar hücre uzakta seçilir")]
    [SerializeField] private int wanderRadius = 4;
    [Tooltip("Hedefe varınca bekleme süresi (saniye, en az / en çok)")]
    [SerializeField] private Vector2 waitSeconds = new Vector2(1.5f, 4f);
    [Tooltip("Yürüme yönüne dönme hızı")]
    [SerializeField] private float turnSharpness = 8f;

    [SerializeField] private SlaveRig rig;

    private Vector3Int cell;
    private bool placed;

    public Vector3Int Cell => cell;
    // Animasyon için: şu anki ilerleme hızı (dururken 0)
    public float CurrentSpeed { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        if (rig == null) rig = GetComponent<SlaveRig>();
    }

    public override void OnPlaced(Vector3Int origin)
    {
        base.OnPlaced(origin);
        cell = origin;
        placed = true;
        ChangeState(new WaitState(this));
    }

    protected override void Update()
    {
        if (!placed) return; // sadece grid'e konunca (önizleme kopyası yürümesin)
        base.Update();
        if (rig != null) rig.Tick(CurrentSpeed);
    }

    // Durabilir: altı zemin, kendi boyu kadar yukarısı boş (kendisi hariç)
    private bool IsWalkable(Vector3Int target)
    {
        if (!GridPathfinder.IsStandable(target, this)) return false;
        int height = GetFootprint().Size.y;
        for (int y = 1; y < height; y++)
        {
            if (!GridManager.Instance.TryGetCell(target + Vector3Int.up * y, out GridData data)) return false;
            if (data.Base != null || (data.Placeable != null && data.Placeable != this) || data.HasFine) return false;
        }
        return true;
    }

    // Yakında rastgele bir hücreye yol; birkaç deneme, bulamazsa null
    private List<Vector3Int> FindWanderPath()
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            Vector3Int goal = cell + new Vector3Int(Random.Range(-wanderRadius, wanderRadius + 1), Random.Range(-1, 2),
                                                    Random.Range(-wanderRadius, wanderRadius + 1));
            if (goal == cell || !IsWalkable(goal)) continue;
            List<Vector3Int> path = GridPathfinder.FindPath(cell, goal, IsWalkable, 400, this);
            if (path != null && path.Count > 0) return path;
        }
        return null;
    }

    // --- Durumlar ---

    private class WaitState : CreatureState<Slave>
    {
        private float duration;
        public WaitState(Slave owner) : base(owner) { }

        public override void Enter()
        {
            owner.CurrentSpeed = 0f;
            duration = Random.Range(owner.waitSeconds.x, owner.waitSeconds.y);
        }

        public override void Tick()
        {
            if (owner.StateTime < duration) return;
            List<Vector3Int> path = owner.FindWanderPath();
            if (path != null) owner.ChangeState(new WalkState(owner, path));
            else duration = owner.StateTime + 1f; // yer yok: biraz sonra tekrar dene
        }
    }

    private class WalkState : CreatureState<Slave>
    {
        private readonly List<Vector3Int> path;
        private Vector3 from, to;
        private float progress = 1f, stepDuration = 1f;

        public WalkState(Slave owner, List<Vector3Int> path) : base(owner) { this.path = path; }

        public override void Tick()
        {
            Transform t = owner.transform;
            if (progress >= 1f && !BeginStep())
            {
                owner.ChangeState(new WaitState(owner));
                return;
            }

            progress = Mathf.Min(1f, progress + Time.deltaTime / stepDuration);
            t.position = Vector3.Lerp(from, to, progress);
            owner.CurrentSpeed = owner.walkSpeed;

            Vector3 facing = to - from;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(facing.normalized, Vector3.up);
                t.rotation = Quaternion.Slerp(t.rotation, target, 1f - Mathf.Exp(-owner.turnSharpness * Time.deltaTime));
            }
        }

        // Sıradaki hücreye geç: yol bittiyse ya da arada kapandıysa (yeni obje kondu) false
        private bool BeginStep()
        {
            if (path.Count == 0) return false;
            Vector3Int next = path[0];
            path.RemoveAt(0);
            if (!owner.IsWalkable(next) || !GridManager.Instance.TryMovePlaceable(owner, next, owner.GetFootprint())) return false;

            from = owner.transform.position;
            to = next;
            owner.cell = next;
            stepDuration = Mathf.Max(0.01f, Vector3.Distance(from, to) / Mathf.Max(owner.walkSpeed, 0.01f));
            progress = 0f;
            return true;
        }
    }
}
