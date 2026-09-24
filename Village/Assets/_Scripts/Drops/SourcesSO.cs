using UnityEngine;

[CreateAssetMenu(fileName = "New Source", menuName = "Create Source/New Source")]
public class SourcesSO : ScriptableObject
{
    public string SourceName;
    public Sprite Icon;
    public SourceBase Prefab;

}
