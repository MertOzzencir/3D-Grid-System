using System.IO;
using UnityEditor;
using UnityEngine;

// Tools → Blueprint Creator. Play mode'da grid'deki parçalardan blueprint üretir; işi BlueprintBuilder yapar.
public class BlueprintCreatorWindow : EditorWindow
{
    private string blueprintName = "New Blueprint";
    private string folder = "Assets/Blueprints";

    [MenuItem("Tools/Blueprint Creator")]
    private static void Open() => GetWindow<BlueprintCreatorWindow>("Blueprint Creator");

    // Play mode'a girip çıkınca buton durumu güncellensin
    private void OnInspectorUpdate() => Repaint();

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Play mode'da parçaları yerleştir, sonra Blueprint Oluştur'a bas.\n" +
            "Grid'deki tüm parçalar (0,0,0) noktasına göre kaydedilir.",
            MessageType.Info);

        blueprintName = EditorGUILayout.TextField("İsim", blueprintName);
        folder = EditorGUILayout.TextField("Klasör", folder);

        string error = Validate();
        if (error != null)
            EditorGUILayout.HelpBox(error, MessageType.Warning);

        using (new EditorGUI.DisabledScope(error != null))
        {
            if (GUILayout.Button("Blueprint Oluştur", GUILayout.Height(30)))
                Create();
        }
    }

    // Sorun yoksa null
    private string Validate()
    {
        if (!Application.isPlaying) return "Play mode'da olmalısın.";
        if (GridManager.Instance == null) return "Sahnede GridManager yok.";
        if (string.IsNullOrWhiteSpace(blueprintName)) return "İsim boş olamaz.";
        if (blueprintName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "İsimde geçersiz karakter var.";
        if (!folder.StartsWith("Assets")) return "Klasör Assets altında olmalı.";
        return null;
    }

    private void Create()
    {
        string cleanName = blueprintName.Trim();
        string cleanFolder = folder.TrimEnd('/');
        string prefabPath = $"{cleanFolder}/{cleanName}.prefab";

        if (File.Exists(prefabPath) &&
            !EditorUtility.DisplayDialog("Blueprint zaten var", $"{prefabPath} üzerine yazılsın mı?", "Üzerine yaz", "İptal"))
            return;

        bool success = BlueprintBuilder.Build(cleanName, cleanFolder, out string message);
        if (success)
        {
            Debug.Log(message);
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
        }
        else
        {
            Debug.LogWarning(message);
        }
        ShowNotification(new GUIContent(message));
    }
}
