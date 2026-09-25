using UnityEditor;
using UnityEngine;

public abstract class GridEntitySOEditorBase : Editor
{
    public override void OnInspectorGUI()
    {
        var so = (GridEntitySOBase)target;
        so.EnsureLayout();
        serializedObject.Update();

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("size"));
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            so.EnsureLayout();
            serializedObject.Update();
        }

        Vector3Int size = so.Size;
        SerializedProperty layersProp = serializedObject.FindProperty("layers");

        for (int y = size.y - 1; y >= 0; y--) // üst katman en üstte görünsün
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(y == 0 ? "Katman 0 (zemin)" : $"Katman {y}", EditorStyles.boldLabel);

            SerializedProperty cells = layersProp.GetArrayElementAtIndex(y).FindPropertyRelative("cells");
            for (int z = size.z - 1; z >= 0; z--)
            {
                EditorGUILayout.BeginHorizontal();
                for (int x = 0; x < size.x; x++)
                {
                    SerializedProperty cell = cells.GetArrayElementAtIndex(z * size.x + x);
                    cell.boolValue = GUILayout.Toggle(cell.boolValue, "", GUILayout.Width(30), GUILayout.Height(30));
                }
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.Space();

        SerializedProperty prop = serializedObject.GetIterator();
        bool enterChildren = true;
        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (prop.name == "size" || prop.name == "layers" || prop.name == "m_Script")
                continue;
            EditorGUILayout.PropertyField(prop, true);
        }

        serializedObject.ApplyModifiedProperties();
    }
}

[CustomEditor(typeof(GridBaseSO))]
public class GridBaseSOEditor : GridEntitySOEditorBase { }
[CustomEditor(typeof(GridPlaceableSO))]
public class GridPlaceableSOEditor : GridEntitySOEditorBase { }

public static class GridEntityMigration
{
    [MenuItem("Tools/Grid/Eski 2B Size'ları 3B'ye Çevir")]
    private static void MigrateAll()
    {
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:GridEntitySOBase"))
        {
            var so = AssetDatabase.LoadAssetAtPath<GridEntitySOBase>(AssetDatabase.GUIDToAssetPath(guid));
            so.EnsureLayout();
            EditorUtility.SetDirty(so);
            count++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"{count} grid entity asset'i güncellendi.");
    }
}
