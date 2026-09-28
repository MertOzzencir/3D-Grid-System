using UnityEngine;

[CreateAssetMenu(fileName = "New Source", menuName = "Create Source/New Source")]
public class SourcesSO : ScriptableObject
{
    public string SourceName;
    public Sprite Icon;
    public SourceBase Prefab;

    [Tooltip("Kayıt dosyasındaki kimliği. Boşsa asset adı kullanılır. Oyun yayınlandıktan sonra değiştirme.")]
    [SerializeField] private string saveId;
    public string SaveId => string.IsNullOrEmpty(saveId) ? name : saveId;
}
