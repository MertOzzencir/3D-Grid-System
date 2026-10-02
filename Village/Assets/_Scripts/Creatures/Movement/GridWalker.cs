using System.Collections.Generic;
using UnityEngine;

// Grid'de yürüyen iki hücreli (kafa + gövde) canlının hareket modülü (örn. kedi, 2×1).
// Yol kafa hücresi için bulunur; gövde kafanın bir önceki hücresine geçer, yani kafanın izinden gelir.
// Geri dönmek gerekirse (ilk adım gövdenin hücresi) kafa ile gövde yer değiştirir: canlı yerinde döner.
// Obje kökü kafa ile gövdenin ortasında durur, yüzü kafaya bakar. Yükseklik: hücrenin tabanı (altındaki base'in üstü).
public class GridWalker : MonoBehaviour
{
    [Tooltip("Birim / saniye")]
    [SerializeField] private float speed = 1.2f;
    [Tooltip("Yerinde dönerken (kafa-gövde yer değiştirme) süre (saniye)")]
    [SerializeField] private float turnAroundDuration = 0.5f;
    [Tooltip("Yüzünün yürüme yönüne dönme hızı")]
    [SerializeField] private float turnSharpness = 12f;

    private readonly List<Vector3Int> path = new List<Vector3Int>();
    private Vector3 headFrom, headTo, bodyFrom, bodyTo;
    private float stepProgress = 1f;
    private float stepDuration = 1f;
    private bool turningAround;
    private Vector3 turnStartFacing;
    private bool jumping;
    private float jumpHeight;
    private Quaternion jumpStartRotation, jumpEndRotation;

    public bool IsPlaced { get; private set; }
    public Vector3Int HeadCell { get; private set; }
    public Vector3Int BodyCell { get; private set; }
    public bool IsMoving => stepProgress < 1f || path.Count > 0;
    public bool IsJumping => jumping && stepProgress < 1f;
    // Zıplamanın ilerlemesi (0 kalkış, 1 iniş); zıplamıyorsa 1
    public float JumpProgress => jumping ? stepProgress : 1f;
    public float Speed => speed;
    // Şu anki ilerleme hızı (animasyon hızını eşlemek için); dururken 0
    public float CurrentSpeed { get; private set; }

    // Yürünebilir: üstünde durulabilir ve gövdenin bulunduğu hücre değil (kafa gövdeye giremez)
    public bool IsWalkable(Vector3Int cell) => GridPathfinder.IsStandable(cell);

    // Hücrenin tabanı (ayakların bastığı yer)
    public static Vector3 FeetPosition(Vector3Int cell) => cell + Vector3.down * 0.5f;

    public void Place(Vector3Int head, Vector3Int body)
    {
        HeadCell = head;
        BodyCell = body;
        headFrom = headTo = FeetPosition(head);
        bodyFrom = bodyTo = FeetPosition(body);
        stepProgress = 1f;
        path.Clear();
        IsPlaced = true;
        ApplyTransform(true);
    }

    // Yakındaki iki yan yana yürünebilir hücreye yerleşir (yükleme / başlangıç). Bulamazsa false.
    public bool TryPlaceNear(Vector3 position, int radius = 8)
    {
        Vector3Int origin = Vector3Int.RoundToInt(position);
        float best = float.MaxValue;
        Vector3Int bestHead = default, bestBody = default;

        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
        for (int z = -radius; z <= radius; z++)
        {
            Vector3Int head = origin + new Vector3Int(x, y, z);
            if (!GridPathfinder.IsStandable(head)) continue;
            float distance = (head - origin).sqrMagnitude;
            if (distance >= best) continue;

            foreach (Vector3Int neighbour in GridPathfinder.Neighbours(head, GridPathfinder.IsStandable))
            {
                if (neighbour.y != head.y) continue;
                best = distance;
                bestHead = head;
                bestBody = neighbour;
                break;
            }
        }

        if (best == float.MaxValue) return false;
        Place(bestHead, bestBody);
        return true;
    }

    public bool MoveTo(Vector3Int goal)
    {
        if (!IsPlaced) return false;
        List<Vector3Int> found = GridPathfinder.FindPath(HeadCell, goal, IsWalkable);
        if (found == null) return false;

        path.Clear();
        path.AddRange(found);
        return true;
    }

    public void Stop() => path.Clear(); // o anki adım tamamlanır (zıplama da yarıda kesilmez)

    // Yay çizerek yeni iki hücreye zıplar (yol bulmadan, aradakilerin üstünden). Hücreler kalkışta ayrılır.
    // height: yayın en yüksek noktasının, kalkış ve iniş arasındaki düz çizginin üstündeki yüksekliği.
    public void JumpTo(Vector3Int head, Vector3Int body, float duration, float height)
    {
        if (!IsPlaced) return;
        path.Clear();
        turningAround = false;
        jumping = true;
        jumpHeight = height;
        jumpStartRotation = transform.rotation;
        Vector3 facing = FeetPosition(head) - FeetPosition(body);
        facing.y = 0f;
        jumpEndRotation = facing.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(facing.normalized, Vector3.up) : transform.rotation;

        HeadCell = head;
        BodyCell = body;
        StartStep(FeetPosition(head), FeetPosition(body), duration);
    }

    private void Update()
    {
        if (!IsPlaced) return;

        if (stepProgress < 1f)
        {
            stepProgress = Mathf.Min(1f, stepProgress + Time.deltaTime / stepDuration);
            CurrentSpeed = turningAround || jumping ? 0f : speed;
        }
        else if (path.Count > 0)
        {
            jumping = false;
            BeginNextStep();
        }
        else
        {
            jumping = false;
            CurrentSpeed = 0f;
        }

        ApplyTransform(false);
    }

    private void BeginNextStep()
    {
        Vector3Int next = path[0];

        // Arada yol kapandıysa (yeni obje kondu) dur; canlının beyni yeni hedef seçer
        if (!IsWalkable(next))
        {
            path.Clear();
            return;
        }

        if (next == BodyCell)
        {
            // Geri dönüş: kafa ve gövde yer değiştirir (yerinde 180° döner), adım tüketilmez
            turningAround = true;
            turnStartFacing = transform.forward;
            (HeadCell, BodyCell) = (BodyCell, HeadCell);
            StartStep(FeetPosition(HeadCell), FeetPosition(BodyCell), turnAroundDuration);
            path.RemoveAt(0);
            return;
        }

        turningAround = false;
        path.RemoveAt(0);
        Vector3Int previousHead = HeadCell;
        HeadCell = next;
        BodyCell = previousHead;
        float distance = Vector3.Distance(FeetPosition(previousHead), FeetPosition(next));
        StartStep(FeetPosition(HeadCell), FeetPosition(BodyCell), distance / Mathf.Max(speed, 0.01f));
    }

    private void StartStep(Vector3 newHead, Vector3 newBody, float duration)
    {
        headFrom = Vector3.Lerp(headFrom, headTo, stepProgress);
        bodyFrom = Vector3.Lerp(bodyFrom, bodyTo, stepProgress);
        headTo = newHead;
        bodyTo = newBody;
        stepDuration = Mathf.Max(duration, 0.01f);
        stepProgress = 0f;
    }

    private void ApplyTransform(bool snap)
    {
        Vector3 head = Vector3.Lerp(headFrom, headTo, stepProgress);
        Vector3 body = Vector3.Lerp(bodyFrom, bodyTo, stepProgress);
        transform.position = (head + body) * 0.5f;

        // Zıplama: yatayda düz, dikeyde yay; yön kalkıştaki yönden inişteki yöne yumuşakça döner
        if (jumping)
        {
            transform.position += Vector3.up * (4f * jumpHeight * stepProgress * (1f - stepProgress));
            transform.rotation = Quaternion.Slerp(jumpStartRotation, jumpEndRotation, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, stepProgress * 1.6f)));
            return;
        }

        // Yerinde dönüş: konum aynı kalır (orta nokta değişmez), yön dikey eksen etrafında 180° döner
        if (turningAround && stepProgress < 1f)
        {
            Vector3 turning = Quaternion.AngleAxis(180f * Mathf.SmoothStep(0f, 1f, stepProgress), Vector3.up) * turnStartFacing;
            transform.rotation = Quaternion.LookRotation(turning, Vector3.up);
            return;
        }

        Vector3 facing = head - body;
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.0001f) return;

        Quaternion target = Quaternion.LookRotation(facing.normalized, Vector3.up);
        transform.rotation = snap ? target : Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-turnSharpness * Time.deltaTime));
    }
}
