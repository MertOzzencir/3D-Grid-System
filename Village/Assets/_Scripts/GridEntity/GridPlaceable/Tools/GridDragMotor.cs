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
    private readonly RaycastHit[] hitBuffer = new RaycastHit[20];

    private IToolTarget currentTarget;
    private Vector3 dragStartPosition;
    private Vector3 lastValidGridPosition;
    private Coroutine placeRoutine;

    private Transform Transform => owner.transform;

    public GridDragMotor(GridPlaceable owner, float followSpeed)
    {
        this.owner = owner;
        this.followSpeed = followSpeed;
        interactable = owner as IInteractable;
        if (interactable == null)
            Debug.LogError($"{owner.name} uses GridDragMotor but doesn't implement IInteractable", owner);
    }

    public void Begin()
    {
        StopPlaceRoutine();
        dragStartPosition = Transform.position;
        lastValidGridPosition = Transform.position;
        GridManager.Instance.PlaceableRemoveOn(owner, false);
    }

    public void Tick()
    {
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

        Vector3 gridPos = GridManager.Instance.GetGridWorldPosition(hit.point + HoverOffset, out bool onGrid);
        if (onGrid)
        {
            lastValidGridPosition = gridPos;
            FollowTowards(gridPos, Quaternion.identity);
        }
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
        if (TryRaycast(out RaycastHit hit, out _))
        {
            Vector3 gridPos = GridManager.Instance.GetGridWorldPosition(hit.point + HoverOffset, out bool onGrid);
            if (onGrid) dropPos = gridPos;
        }

        StopPlaceRoutine();
        placeRoutine = owner.StartCoroutine(SmoothPlaceAt(dropPos));
    }

    private IEnumerator SmoothPlaceAt(Vector3 targetPos)
    {
        while (Vector3.Distance(Transform.position, targetPos) > 0.05f)
        {
            FollowTowards(targetPos, Quaternion.identity);
            yield return null;
        }

        Transform.SetPositionAndRotation(targetPos, Quaternion.identity);
        placeRoutine = null;

        if (!GridManager.Instance.PlaceablePlaceOn(owner, targetPos) &&
            !GridManager.Instance.PlaceablePlaceOn(owner, dragStartPosition))
        {
            Debug.LogWarning($"{owner.name} could not be placed back on the grid", owner);
        }
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