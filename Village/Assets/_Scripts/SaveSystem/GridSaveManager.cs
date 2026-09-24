using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class GridSaveManager : MonoBehaviour
{
    [SerializeField] private GridSaveSO data;
    private GridManager manager;
    void Awake()
    {
        manager = GetComponent<GridManager>();
        InputManager.OnF5 += SaveDataNow;
    }

    private void SaveDataNow()
    {
        DeleteData();
        foreach (var a in manager.GetGridData())
        {
            if (a.Value.Base != null && a.Value.IsOrigin)
            {
                data.SavedDatas.Add(new SaveData(a.Value, a.Value.Base.GetBasePrefab()));
            }
        }
    }
    public List<SaveData> SavedData()
    {
        return data.SavedDatas;
    }
    [ContextMenu("Delete Data")]
    public void DeleteData()
    {
        data.SavedDatas.Clear();
    }
}



