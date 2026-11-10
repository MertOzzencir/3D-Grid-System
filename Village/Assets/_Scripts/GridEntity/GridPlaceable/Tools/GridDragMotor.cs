using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GridDragMotor
{
    private static readonly Vector3 HoverOffset = Vector3.up / 2;
    private static readonly IComparer<RaycastHit> ByDistance =
        Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

    private readonly GridPlaceable owner;
    private readonly IInteractable interactable;
    private readonly float followSpeed;
    private readonly bool keepCenterOnRotate;
    private readonly RaycastHit[] hitBuffer = new RaycastHit[20];

    private IToolTarget currentTarget;
    private Vector3 dragStartPosition;
    private GridMaskRotator.Rotation dragStartRotation;
    private Vector3 lastValidGridPosition;
    private Coroutine placeRoutine;

    private Transform Transform => owner.transform;

    // Şu an üstünde durulan (kabul eden) hedef; yoksa null
    public IToolTarget CurrentTarget => currentTarget;

    // keepCenterOnRotate: taşırken R ile dönünce obje köşesinden değil kendi ortası etrafında döner (GridEntity.RotationCenterShift)
    public GridDragMotor(GridPlaceable owner, float followSpeed, bool keepCenterOnRotate = false)
    {
        this.owner = owner;
        this.followSpeed = followSpeed;
        this.keepCenterOnRotate = keepCenterOnRotate;
        interactable = owner as IInteractable;
        if (interactable == null)
            Debug.LogError($"{owner.name} uses GridDragMotor but doesn't implement IInteractable", owner);
    }

    public void Begin()
    {
        StopPlaceRoutine();
        dragStartPosition = Transform.position;
        dragStartRotation = owner.Rotation;
        lastValidGridPosition = Transform.position;
        GridManager.Instance.PlaceableRemoveOn(owner, false);
    }

    public void Tick()
    {
        // Karakter varsa obje onun elinde durur; sadece hazırlıkta (sağ tık basılı, InteractableController.IsAiming)
        // menzildeki hedeflere (örn. matkabın kazdığı blok) gider
        PlayerCharacter player = PlayerCharacter.Instance;
        if (player != null && !InteractableController.IsAiming)
        {
            ClearTarget();
            FollowTowards(player.HoldPoint, owner.GridRotation);
            return;
        }

        // Yapışkan hedef: kilit bırakılmadıkça ray ile yeni hedef aranmaz (mouse collider'dan çıksa da)
        if (TryKeepStickyTarget()) return;

        if (player != null)
        {
            float reach = owner is IToolReach tool ? tool.Reach : player.Reach;
            if (TryFindTarget(out RaycastHit near, out IToolTarget nearTarget) && player.InReach(near.point, reach))
            {
                if (currentTarget != null && currentTarget != nearTarget) currentTarget.OnToolTargetExit();
                currentTarget = nearTarget;
                Vector3 targetPosition = nearTarget.GetToolTargetPosition(interactable, out bool accepted);
                if (accepted)
                {
                    FollowTowards(targetPosition, nearTarget.GetToolTargetRotation());
                    return;
                }
            }
            ClearTarget();
            FollowTowards(player.HoldPoint, owner.GridRotation);
            return;
        }

        if (!TryRaycast(out RaycastHit hit, out IToolTarget target))
        {
            ClearTarget();
            StopPlaceRoutine();
            Transform.position = lastValidGridPosition;
            return;
        }

        if (target != null)
        {
            if (currentTarget != null && currentTarget != target)
                currentTarget.OnToolTargetExit();
            currentTarget = target;

            Vector3 targetPos = target.GetToolTargetPosition(interactable, out bool accept);
            if (accept)
            {
                FollowTowards(targetPos, target.GetToolTargetRotation());
                return;
            }
        }

        ClearTarget();

        Vector3 gridPos = GridManager.Instance.GetGridWorldPosition(hit.point + HoverOffset + RotationShift, out bool onGrid);
        if (onGrid)
        {
            lastValidGridPosition = gridPos;
            FollowTowards(gridPos, owner.GridRotation);
        }
    }

    private Vector3 RotationShift => keepCenterOnRotate ? (Vector3)owner.RotationCenterShift : Vector3.zero;

    private bool TryKeepStickyTarget()
    {
        if (!(currentTarget is IStickyToolTarget sticky)) return false;

        // Hedef bu arada yok edilmiş olabilir (Unity null'u)
        if (currentTarget is Object targetObject && targetObject == null)
        {
            currentTarget = null;
            return false;
        }

        // Ani (savurma) mouse hareketi kilidi her hedefte bırakır; küçük kaymalar etkileşimi kesmez
        if (InteractableController.Instance != null && InteractableController.Instance.IsMouseFlicking) return false;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!sticky.IsStillTargeted(interactable, ray)) return false;

        Vector3 targetPos = currentTarget.GetToolTargetPosition(interactable, out bool accept);
        if (!accept) return false; // uygun nokta kalmadı: normal akışa dön

        FollowTowards(targetPos, currentTarget.GetToolTargetRotation());
        return true;
    }

    public bool TryUseOnTarget(out bool finished)
    {
        if (currentTarget == null)
        {
            finished = false;
            return false;
        }
        finished = currentTarget.OnToolUsed(interactable);
        return true;
    }

    public void Cancel()
    {
        if (currentTarget != null) currentTarget.OnToolTargetExit();
        
        currentTarget = null;

        Vector3 dropPos = lastValidGridPosition;
        PlayerCharacter player = PlayerCharacter.Instance;
        if (player != null)
        {
            // Karakterin önündeki hücre
            Vector3 front = GridManager.Instance.GetGridWorldPosition(player.FrontDropPoint + RotationShift, out bool onGrid);
            dropPos = onGrid ? front : dragStartPosition;
        }
        else if (TryRaycast(out RaycastHit hit, out _))
        {
            Vector3 gridPos = GridManager.Instance.GetGridWorldPosition(hit.point + HoverOffset + RotationShift, out bool onGrid);
            if (onGrid) dropPos = gridPos;
        }

        StopPlaceRoutine();
        placeRoutine = owner.StartCoroutine(SmoothPlaceAt(dropPos));
    }

    // Taşımayı grid'e bırakmadan bitir (parça başka bir yere, örn. botun tarlasına oturtuldu)
    public void Abort()
    {
        ClearTarget();
        StopPlaceRoutine();
    }

    private IEnumerator SmoothPlaceAt(Vector3 targetPos)
    {
        while (Vector3.Distance(Transform.position, targetPos) > 0.05f)
        {
            FollowTowards(targetPos, owner.GridRotation);
            yield return null;
        }

        placeRoutine = null;

        if (GridManager.Instance.PlaceablePlaceOn(owner, targetPos)) yield break;

        // Döndürülmüş hali sığmadıysa: aldığımız yere, aldığımız rotasyonla geri koy
        owner.Rotation = dragStartRotation;
        if (!GridManager.Instance.PlaceablePlaceOn(owner, dragStartPosition))
            Debug.LogWarning($"{owner.name} could not be placed back on the grid", owner);
    }

    private void FollowTowards(Vector3 position, Quaternion rotation)
    {
        float t = followSpeed * Time.deltaTime;
        Transform.position = Vector3.Lerp(Transform.position, position, t);
        Transform.rotation = Quaternion.Slerp(Transform.rotation, rotation, t);
    }

    private bool TryRaycast(out RaycastHit firstHit, out IToolTarget firstTarget)
    {
        firstHit = default;
        firstTarget = null;
        bool found = false;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        int count = Physics.RaycastNonAlloc(ray, hitBuffer);
        System.Array.Sort(hitBuffer, 0, count, ByDistance);

        for (int i = 0; i < count; i++)
        {
            if (hitBuffer[i].transform.IsChildOf(Transform)) continue;

            if (!found)
            {
                firstHit = hitBuffer[i];
                found = true;
            }

            if (hitBuffer[i].collider.TryGetComponent(out IToolTarget candidate))
            {
                firstTarget = candidate;
                break;
            }
        }

        return found;
    }

    // Mouse'un altındaki hedef; tam üstünde değilse (birkaç piksel kaçırınca) mouse ışını etrafında kalın bir küreyle
    // tekrar aranır (TargetTolerance), en yakın hedef alınır
    private const float TargetTolerance = 0.35f;

    private bool TryFindTarget(out RaycastHit hit, out IToolTarget target)
    {
        if (TryRaycast(out hit, out target) && target != null) return true;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        int count = Physics.SphereCastNonAlloc(ray, TargetTolerance, hitBuffer, 500f);
        System.Array.Sort(hitBuffer, 0, count, ByDistance);
        for (int i = 0; i < count; i++)
        {
            if (hitBuffer[i].transform.IsChildOf(Transform)) continue;
            if (!hitBuffer[i].collider.TryGetComponent(out IToolTarget candidate)) continue;
            hit = hitBuffer[i];
            if (hit.distance <= 0f) hit.point = hitBuffer[i].collider.ClosestPoint(ray.origin); // küre başta içindeyse
            target = candidate;
            return true;
        }
        return false;
    }

    private void ClearTarget()
    {
        if (currentTarget == null) return;
        currentTarget.OnToolTargetExit();
        currentTarget = null;
    }

    private void StopPlaceRoutine()
    {
        if (placeRoutine == null) return;
        owner.StopCoroutine(placeRoutine);
        placeRoutine = null;
    }
}