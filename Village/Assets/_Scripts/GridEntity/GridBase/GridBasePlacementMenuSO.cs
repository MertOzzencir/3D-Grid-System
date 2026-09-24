using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Placement", menuName = "Create Base Placeable Asset Menu/New Menu")]
public class GridBasePlacementMenuSO : GridMenuBaseSO
{
}

public class GridMenuBaseSO : ScriptableObject
{
    public List<GridEntitySOBase> Entities;
}