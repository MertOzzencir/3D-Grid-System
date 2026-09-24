using UnityEngine;
public abstract class GridEntitySO<TPrefab> : GridEntitySOBase where TPrefab : GridEntity
{
    public TPrefab Prefab;
    public override GridEntity GetPrefabBase() => Prefab;
}
public abstract class GridEntitySOBase : ScriptableObject
{
    public string Name;
    public Sprite Icon;
    public Vector2 Size;
    [SerializeField] public bool[] mask = new bool[1] { true };
    public abstract GridEntity GetPrefabBase();
}

