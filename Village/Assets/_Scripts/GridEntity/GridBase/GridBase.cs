public class GridBase : GridEntity
{
    public GridBaseSO GetBaseData() => GetData() as GridBaseSO;
    public GridBase GetBasePrefab() => GetBaseData()?.Prefab;
}