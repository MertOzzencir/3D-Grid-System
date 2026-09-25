using UnityEngine;

public abstract class GridPlacementControllerBase<TEntity> : GridPlacementControllerBase where TEntity : GridEntity
{
    private Plane plane;
    [SerializeField] private string menuName;

    private int draggingNumber = -1;
    private Vector3 dragStartPos;
    private bool isDragging;

    public override string MenuName
    {
        get => menuName;
        set => menuName = value;
    }

    protected virtual void Awake()
    {
        plane = new Plane(Vector3.up, transform.position);
    }

    protected abstract TEntity GetPrefab(int index);
    protected abstract bool CanPlace(TEntity entity, Vector3 position);
    protected abstract bool PlaceEntity(TEntity entity, Vector3 position);
    protected abstract bool RemoveEntity(TEntity entity);

    private void Place(int obj, bool isPressed)
    {
        if (isPressed)
        {
            TEntity prefab = GetPrefab(obj - 1);
            if (prefab == null) return;
            if (!TryGetTargetPosition(out Vector3 startPos)) return;

            draggingNumber = obj;
            dragStartPos = startPos;
            isDragging = true;
        }
        else
        {
            if (!isDragging || draggingNumber != obj) return;
            isDragging = false;

            if (!TryGetTargetPosition(out Vector3 endPos)) return;

            SpawnLine(obj, dragStartPos, endPos);
            draggingNumber = -1;
        }
    }

    private bool TryGetTargetPosition(out Vector3 position)
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            position = hit.point + Vector3.up / 2;
            return true;
        }
        else if (plane.Raycast(ray, out float enter))
        {
            position = ray.GetPoint(enter);
            return true;
        }

        position = Vector3.zero;
        return false;
    }

    private void SpawnLine(int obj, Vector3 start, Vector3 end)
    {
        TEntity prefab = GetPrefab(obj - 1);
        if (prefab == null) return;

        float step = Mathf.Max(1, prefab.GetFootprint().Size.x); // her entity arası boşluk, footprint genişliğine göre

        Vector3 delta = end - start;

        // Hattı dominant eksene kilitle (X ya da Z) — çapraz değil, düzgün bir sıra
        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.z))
            delta.z = 0f;
        else
            delta.x = 0f;

        float distance = delta.magnitude;
        Vector3 direction = distance > 0.001f ? delta.normalized : Vector3.zero;
        int count = Mathf.Max(1, Mathf.RoundToInt(distance / step));

        for (int i = 0; i <= count; i++)
        {
            Vector3 pos = start + direction * (step * i);

            if (!CanPlace(prefab, pos)) continue;

            TEntity instance = Instantiate(prefab);
            PlaceEntity(instance, pos);
        }
    }

    private void DeletePlaced()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider.TryGetComponent(out TEntity entity))
            RemoveEntity(entity);
    }

    protected virtual void OnEnable()
    {
        InputManager.OnNumbers += Place;
        InputManager.OnE += DeletePlaced;
    }

    protected virtual void OnDisable()
    {
        InputManager.OnNumbers -= Place;
        InputManager.OnE -= DeletePlaced;
    }
}
public abstract class GridPlacementControllerBase : MonoBehaviour
{
    public abstract string MenuName { get; set; }
    public GridMenuBaseSO MenusSO;
}