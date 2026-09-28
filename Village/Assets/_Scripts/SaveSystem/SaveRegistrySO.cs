using System.Collections.Generic;
using UnityEngine;

// Kayıt dosyasına yazılabilen her şeyin listesi: yüklerken "bu ID hangi SO / prefab?" sorusunu cevaplar.
// Yeni bir entity ya da kaynak SO'su eklenince: asset'e sağ tık (⋮) → "Find All Assets".
[CreateAssetMenu(fileName = "SaveRegistry", menuName = "Save/Save Registry")]
public class SaveRegistrySO : ScriptableObject
{
    [SerializeField] private List<GridEntitySOBase> entities = new List<GridEntitySOBase>();
    [SerializeField] private List<SourcesSO> sources = new List<SourcesSO>();

    public GridEntitySOBase GetEntity(string id) => entities.Find(e => e != null && e.SaveId == id);
    public SourcesSO GetSource(string id) => sources.Find(s => s != null && s.SaveId == id);
    public bool Contains(GridEntitySOBase data) => data != null && entities.Contains(data);

#if UNITY_EDITOR
    [ContextMenu("Find All Assets")]
    private void FindAllAssets()
    {
        entities = FindAll<GridEntitySOBase>();
        sources = FindAll<SourcesSO>();
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"SaveRegistry: {entities.Count} entity, {sources.Count} kaynak", this);
    }

    private static List<T> FindAll<T>() where T : ScriptableObject
    {
        var list = new List<T>();
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:" + typeof(T).Name))
            list.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<T>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)));
        return list;
    }
#endif
}
