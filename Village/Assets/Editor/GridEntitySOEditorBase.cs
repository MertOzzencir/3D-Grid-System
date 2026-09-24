using UnityEditor;
using UnityEngine;

// DİKKAT: Bunda [CustomEditor] YOK — bu yüzden hiçbir SO'ya otomatik atanmıyor
public abstract class GridEntitySOEditorBase : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty sizeProp = serializedObject.FindProperty("Size");
        EditorGUILayout.PropertyField(sizeProp);

        int width = Mathf.Max(1, (int)sizeProp.vector2Value.x);
        int height = Mathf.Max(1, (int)sizeProp.vector2Value.y);

        SerializedProperty maskProp = serializedObject.FindProperty("mask");
        int requiredLength = width * height;
        if (maskProp.arraySize != requiredLength)
            maskProp.arraySize = requiredLength;

        EditorGUILayout.Space();
        for (int y = height - 1; y >= 0; y--)
        {
            EditorGUILayout.BeginHorizontal();
            for (int x = 0; x < width; x++)
            {
                SerializedProperty cell = maskProp.GetArrayElementAtIndex(y * width + x);
                cell.boolValue = GUILayout.Toggle(cell.boolValue, "", GUILayout.Width(30), GUILayout.Height(30));
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.Space();

        SerializedProperty prop = serializedObject.GetIterator();
        bool enterChildren = true;
        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (prop.name == "Size" || prop.name == "mask" || prop.name == "m_Script")
                continue;
            EditorGUILayout.PropertyField(prop, true);
        }

        serializedObject.ApplyModifiedProperties();
    }
}

// Her concrete SO tipi için TEK SATIR
[CustomEditor(typeof(GridBaseSO))]
public class GridBaseSOEditor : GridEntitySOEditorBase { }
[CustomEditor(typeof(GridPlaceableSO))]
public class GridPlaceableSOEditor : GridEntitySOEditorBase { }