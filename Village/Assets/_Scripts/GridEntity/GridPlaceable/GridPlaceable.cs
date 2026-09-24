using UnityEngine;

public class GridPlaceable : GridEntity
{

    public GridPlaceableSO GetBaseData() => GetData() as GridPlaceableSO;
    public GridPlaceable GetBasePrefab() => GetBaseData()?.Prefab;
}
