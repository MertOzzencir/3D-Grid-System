using UnityEngine;

public class GridPlaceablePlacementController : GridPlacementControllerBase<GridPlaceable>
{


    protected override GridPlaceable GetPrefab(int index)
        => index >= 0 && index < MenusSO.Entities.Count ? MenusSO.Entities[index].GetPrefabBase() as GridPlaceable : null;

    protected override bool CanPlace(GridPlaceable entity, Vector3 position) => GridManager.Instance.CanPlaceablePlaceOn(entity, position);
    protected override bool PlaceEntity(GridPlaceable entity, Vector3 position) => GridManager.Instance.PlaceablePlaceOn(entity, position);
    protected override bool RemoveEntity(GridPlaceable entity) => GridManager.Instance.PlaceableRemoveOn(entity);
}