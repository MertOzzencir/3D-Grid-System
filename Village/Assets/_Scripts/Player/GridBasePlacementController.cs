using UnityEngine;

public class GridBasePlacementController : GridPlacementControllerBase<GridBase>
{

    protected override GridBase GetPrefab(int index)
        => index >= 0 && index < MenusSO.Entities.Count ? MenusSO.Entities[index].GetPrefabBase() as GridBase : null;

    protected override bool CanPlace(GridBase entity, Vector3 position) => GridManager.Instance.CanPlaceBase(entity, position);
    protected override bool PlaceEntity(GridBase entity, Vector3 position) => GridManager.Instance.PlaceBase(entity, position);
    protected override bool RemoveEntity(GridBase entity) => GridManager.Instance.RemoveBase(entity);

    // Base'ler seçili kata (GridManager.LayerLevel, ▲ ▼ butonları) konur: mouse ışını o kattaki yatay düzleme atılır.
    // Altta grid olmasa da (boşluğa) konabilir; mevcut karolar ışını engellemez.
    protected override bool TryGetTargetPosition(out Vector3 position)
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        var plane = new Plane(Vector3.up, new Vector3(0f, GridManager.Instance.LayerLevel, 0f));
        if (plane.Raycast(ray, out float enter))
        {
            position = ray.GetPoint(enter);
            return true;
        }

        position = default;
        return false;
    }
}
