using UnityEngine;

public class Tree : ResourceEntity, IToolTarget
{
    [SerializeField] private ArrowBase[] Arrows;
    [SerializeField] private GameObject mainVisual;
    [SerializeField] private GameObject cutedVisual;

    public static Vector3 ArrowLocalPosition;
    public static Vector3 ArrowLocalRotation;


    private Vector3 lastRaycastPoint;
    private ArrowBase selectedArrow;

    public Vector3 GetToolTargetPosition(IInteractable interactable, out bool accept)
    {
        accept = false;
        if (!(interactable is Axe)) return default;
        accept = true;
        SetArrows(true);
        SetArrowLocalPositions();
        Debug.Log("Interacted");
        if (!TryGetArrowPlane(out Plane plane))
            return transform.position;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!plane.Raycast(ray, out float enter))
            return transform.position;
        Vector3 point = ray.GetPoint(enter);

        float maxRadius = 2f;
        Vector3 center = transform.position + new Vector3(PlacedFootprint.Size.x / 2f, 0, PlacedFootprint.Size.z / 2f);
        if (Vector3.Distance(point, center) > maxRadius)
            return transform.position;

        lastRaycastPoint = point;
        ArrowBase newSelected = FindClosestArrow(lastRaycastPoint);

        if (newSelected != selectedArrow)
        {
            if (selectedArrow != null) selectedArrow.Selected(ArrowLocalPosition, false);
            selectedArrow = newSelected;
            if (selectedArrow != null) selectedArrow.Selected(ArrowLocalPosition, true);
        }

        return selectedArrow != null ? selectedArrow.GetTransform().position : transform.position;
    }
    public Quaternion GetToolTargetRotation()
    {
        return selectedArrow != null ? selectedArrow.GetTransform().rotation : transform.rotation;
    }

    public bool OnToolUsed(IInteractable tool)
    {
        if (!(tool is Axe)) return false;
        if (selectedArrow == null || selectedArrow.isUsed) return false;

        ArrowBase usedArrow = selectedArrow;
        selectedArrow = null;

        usedArrow.transform.parent = null;
        SpawnLogic(usedArrow, out bool success);
        return success;
    }

    public void OnToolTargetExit()
    {
        SetArrows(false);
        if (selectedArrow != null)
        {
            selectedArrow.Selected(ArrowLocalPosition, false);
            selectedArrow = null;
        }
    }

    private bool TryGetArrowPlane(out Plane plane)
    {
        foreach (var a in Arrows)
        {
            if (a == null || a.isUsed) continue;
            plane = new Plane(-a.transform.forward, a.transform.position);
            return true;
        }
        plane = default;
        return false;
    }

    private ArrowBase FindClosestArrow(Vector3 point)
    {
        ArrowBase closest = null;
        float closestDist = float.MaxValue;

        foreach (var a in Arrows)
        {
            if (a == null || a.isUsed) continue;

            float dist = Mathf.Abs(point.y - a.transform.position.y);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = a;
            }
        }

        return closest;
    }
    public void SpawnLogic(ArrowBase arrow, out bool success)
    {
        success = false;
        switch (arrow.GetLength())
        {
            case Lenghts.BR_1:
                success = true;
                arrow.OnUsed(OnSpawnLogicFinished);
                break;

            case Lenghts.BR_2:
                arrow.OnUsed(OnSpawnLogicFinished);
                gameObject.SetActive(false);
                success = true;
                break;
        }
    }

    public void OnSpawnLogicFinished(ArrowBase arrow)
    {
        switch (arrow.GetLength())
        {
            case Lenghts.BR_1:
                Vector3 spawnPosition = GridManager.Instance.GetEmptyGridFromEntityPosition(this, out bool positionSuccess);
                if (positionSuccess)
                {
                    ArrowChild tempArrowChild = arrow as ArrowChild;
                    Transform spawnedPosition = tempArrowChild.VisualPart();
                    SourceBase wood = Instantiate(SourceBaseStaticManager.GetItemByIndex(0).Prefab, spawnedPosition.transform.position, Quaternion.identity);
                    wood.OnSpawned();
                    GridManager.Instance.PlaceablePlaceOn(wood, spawnPosition);
                    wood.SpawnAnimation(spawnPosition, spawnedPosition.position);
                }
                break;
            case Lenghts.BR_2:
                bool anyUsed = false;
                foreach (var a in Arrows)
                {
                    if (a == null || a == arrow) continue;
                    if (a.isUsed)
                        anyUsed = a.isUsed;
                }
                if (anyUsed)
                {
                    Vector3 spawnPosition2 = GridManager.Instance.GetEmptyGridFromEntityPosition(this, out bool positionSuccess2);
                    if (positionSuccess2)
                    {
                        ArrowRoot tempArrowChild = arrow as ArrowRoot;
                        Transform spawnedPosition = tempArrowChild.VisualPart();
                        SourceBase wood = Instantiate(SourceBaseStaticManager.GetItemByIndex(0).Prefab, spawnedPosition.transform.position, Quaternion.identity);
                        wood.OnSpawned();
                        GridManager.Instance.PlaceablePlaceOn(wood, spawnPosition2);
                        wood.SpawnAnimation(spawnPosition2, spawnedPosition.position);
                    }
                }
                else
                {
                    Vector3 spawnPosition3 = GridManager.Instance.GetEmptyGridFromEntityPosition(this, out bool positionSuccess2);
                    if (positionSuccess2)
                    {
                        ArrowRoot tempArrowChild = arrow as ArrowRoot;
                        Transform spawnedPosition = tempArrowChild.VisualPart();
                        SourceBase wood = Instantiate(SourceBaseStaticManager.GetItemByIndex(1).Prefab, spawnedPosition.transform.position, Quaternion.identity);
                        wood.OnSpawned();
                        GridManager.Instance.PlaceablePlaceOn(wood, spawnPosition3);
                        wood.SpawnAnimation(spawnPosition3, spawnedPosition.position);
                    }
                }
                break;
        }
        arrow.gameObject.SetActive(false);
    }

    private void SetArrows(bool active)
    {
        foreach (var a in Arrows)
        {
            if (a == null || a.isUsed) continue;
            a.gameObject.SetActive(active);
        }
    }

    private void SetArrowLocalPositions()
    {
        foreach (var a in Arrows)
        {
            if (a == null || a.isUsed) continue;
            if (a == selectedArrow) continue;
            a.transform.localPosition = ArrowLocalPosition + Vector3.up * a.transform.localPosition.y;
        }
    }
    private void SetArrowLocalRotations()
    {
        foreach (var a in Arrows)
        {
            if (a == null) continue;
            a.transform.localEulerAngles = ArrowLocalRotation;
        }
    }

    public static void ArrowStaticTransforms(float xRotationFloat)
    {
        Vector3 localPosition;
        Vector3 localRotation;

        switch (xRotationFloat)
        {
            case 0f: localPosition = new Vector3(0, 0, 1); localRotation = Vector3.zero; break;
            case -90f: localPosition = new Vector3(1, 0, 0); localRotation = new Vector3(0, -90, 0); break;
            case -180f: localPosition = new Vector3(2, 0, 1); localRotation = new Vector3(0, -180, 0); break;
            case 90f: localPosition = new Vector3(1, 0, 2); localRotation = new Vector3(0, 90, 0); break;
            default: localPosition = new Vector3(0, 0, 1); localRotation = Vector3.zero; break;
        }

        ArrowLocalPosition = localPosition;
        ArrowLocalRotation = localRotation;
    }

    public override void OnPlaced(Vector3Int origin)
    {
        base.OnPlaced(origin);
        ArrowLocalPosition = new Vector3(0, 0, 1);
        ArrowLocalRotation = Vector3.zero;
        SetArrowLocalPositions();
        SetArrowLocalRotations();
    }

    private void CameraRotated(CameraFacing facing)
    {
        ArrowStaticTransforms((float)facing);
        SetArrowLocalPositions();
        SetArrowLocalRotations();
    }

    void OnEnable()
    {
        CameraController.OnCameraRotation += CameraRotated;
    }

    void OnDisable()
    {
        CameraController.OnCameraRotation -= CameraRotated;

        if (GridManager.Instance != null)
            GridManager.Instance.PlaceableRemoveOn(this);
    }

    void OnDrawGizmos()
    {
        Gizmos.DrawSphere(lastRaycastPoint, 0.2f);
    }
}