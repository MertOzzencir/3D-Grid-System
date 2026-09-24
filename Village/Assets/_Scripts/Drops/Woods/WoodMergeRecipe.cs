using UnityEngine;


[CreateAssetMenu(fileName = "New Merge Recipe", menuName = "Create Recipe/Wood Merge Recipe")]
public class WoodMergeRecipe : ScriptableObject
{
    public Lenghts InputA;
    public Lenghts InputB;
    public MergeOrientation Orientation; 
    public Wood ResultPrefab;
}
public enum MergeOrientation
{
    Longwise,
    Sideways
}
