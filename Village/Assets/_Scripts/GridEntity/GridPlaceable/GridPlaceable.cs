using UnityEngine;

public class GridPlaceable : GridEntity
{
    // Bu hücreyi kaplayabilir mi (slot boşluğundan ayrı, türe özel kural). Varsayılan: suya konmaz.
    // Su üstünde duranlar (bot) override eder.
    public virtual bool CanOccupy(Vector3Int worldCell) => GridManager.Instance == null || !GridManager.Instance.IsWater(worldCell);

    public GridPlaceableSO GetBaseData() => GetData() as GridPlaceableSO;
    public GridPlaceable GetBasePrefab() => GetBaseData()?.Prefab;
}
