using UnityEngine;

public class GridBasePlacementController : GridPlacementControllerBase<GridBase>
{

    protected override GridBase GetPrefab(int index)
        => index >= 0 && index < MenusSO.Entities.Count ? MenusSO.Entities[index].GetPrefabBase() as GridBase : null;

    protected override bool CanPlace(GridBase entity, Vector3 position) => GridManager.Instance.CanPlaceBase(entity, position);
    protected override bool PlaceEntity(GridBase entity, Vector3 position) => GridManager.Instance.PlaceBase(entity, position);
    protected override bool RemoveEntity(GridBase entity) => GridManager.Instance.RemoveBase(entity);
}