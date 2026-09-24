using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Save", menuName = "New Save/Create Save")]
public class GridSaveSO : ScriptableObject
{
    public List<SaveData> SavedDatas = new List<SaveData>();
}
[Serializable]
public class SaveData
{
    public GridData SavedData;
    public GridEntity Prefab;
    public SaveData(GridData data, GridEntity prefab)
    {
        SavedData = data;
        Prefab = prefab;
    }
}