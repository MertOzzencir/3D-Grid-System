using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Village → Karakteri Sahneye Ekle: açık sahneye oyuncu karakterini (CharacterController + PlayerCharacter, kapsül
// görünüş) kameranın baktığı zemine koyar ve eldiveni (GloveCursor) kapatır (kullanıcı: eldiven yerine karakter).
// Tek adımda Ctrl+Z ile geri alınır.
public static class PlayerCharacterSetup
{
    [MenuItem("Village/Karakteri Sahneye Ekle")]
    public static void Setup()
    {
        if (Object.FindFirstObjectByType<PlayerCharacter>() != null)
        {
            EditorUtility.DisplayDialog("Karakter", "Sahnede zaten bir karakter var.", "Tamam");
            return;
        }
        Undo.SetCurrentGroupName("Karakter ekle");
        int group = Undo.GetCurrentGroup();

        var player = new GameObject("Player");
        Undo.RegisterCreatedObjectUndo(player, "Karakter ekle");
        player.transform.position = GroundPoint();

        var controller = player.AddComponent<CharacterController>();
        controller.height = 1.6f;
        controller.radius = 0.3f;
        controller.center = new Vector3(0f, 0.8f, 0f);
        controller.stepOffset = 0.3f;
        controller.skinWidth = 0.04f;

        PlayerCharacter character = player.AddComponent<PlayerCharacter>();
        var serialized = new SerializedObject(character);
        serialized.FindProperty("bodyShader").objectReferenceValue = Shader.Find("Village/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
        serialized.ApplyModifiedPropertiesWithoutUndo();

        int disabled = 0;
        foreach (GloveCursor glove in Object.FindObjectsByType<GloveCursor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            Undo.RecordObject(glove.gameObject, "Karakter ekle");
            glove.gameObject.SetActive(false);
            disabled++;
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(player.scene);
        Selection.activeGameObject = player;
        EditorUtility.DisplayDialog("Karakter",
            $"Karakter eklendi ({player.transform.position}). Eldiven kapatıldı ({disabled}).\n\nSahneyi kaydet (Ctrl+S). " +
            "Karakter zeminin üstünde olmalı; değilse Scene'de taşı.", "Tamam");
    }

    // Kameranın (Scene görünümü ya da ana kamera) ortasının baktığı zemin noktası
    private static Vector3 GroundPoint()
    {
        Camera camera = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : Camera.main;
        if (camera != null && Physics.Raycast(camera.transform.position, camera.transform.forward, out RaycastHit hit, 500f))
            return hit.point + Vector3.up * 0.05f;
        return new Vector3(0f, 1f, 0f);
    }
}
