using UnityEditor;
using UnityEngine;

// Botun su maskesini elle şekillendirme: Scene'de iki halka nokta (sarı üst = kenarın tepesi, turuncu alt = su
// seviyesinin altı). Noktaya tıkla → taşıma okları çıkar. Ayna açıksa sağ-sol karşılığı da taşınır.
// Prefab modunda ayarla (Play'de yapılan değişiklik Play bitince kaybolur).
[CustomEditor(typeof(Boat))]
public class BoatEditor : Editor
{
    private int selectedRing = -1, selectedIndex = -1;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var boat = (Boat)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Su maskesi noktaları", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Scene'de noktaya tıkla, çıkan oklarla taşı. Sarı: üst halka (kenarın tepesi), " +
                                "turuncu: alt halka (su seviyesinin altı). Prefab modunda ayarla.", MessageType.None);
        if (GUILayout.Button("Noktaları gövdeden ölç"))
        {
            Undo.RecordObject(boat, "Maske noktalarını ölç");
            boat.EditorMeasureRings();
            EditorUtility.SetDirty(boat);
            SceneView.RepaintAll();
        }
        if (GUILayout.Button("Noktaları sil (otomatik maske)"))
        {
            Undo.RecordObject(boat, "Maske noktalarını sil");
            boat.EditorClearRings();
            EditorUtility.SetDirty(boat);
            SceneView.RepaintAll();
        }
    }

    private void OnSceneGUI()
    {
        var boat = (Boat)target;
        Vector3[] top = boat.EditorTopRing, bottom = boat.EditorBottomRing;
        if (top == null || top.Length < 3 || bottom == null || bottom.Length != top.Length) return;

        Matrix4x4 model = boat.EditorModelMatrix();
        HandleRing(boat, top, 0, model, Color.yellow);
        HandleRing(boat, bottom, 1, model, new Color(1f, 0.5f, 0f));
    }

    private void HandleRing(Boat boat, Vector3[] ring, int ringId, Matrix4x4 model, Color color)
    {
        // Halkanın kendisi (noktaları birleştiren çizgi)
        Handles.color = color;
        for (int i = 0; i < ring.Length; i++)
            Handles.DrawLine(model.MultiplyPoint3x4(ring[i]), model.MultiplyPoint3x4(ring[(i + 1) % ring.Length]));

        for (int i = 0; i < ring.Length; i++)
        {
            Vector3 world = model.MultiplyPoint3x4(ring[i]);
            float size = HandleUtility.GetHandleSize(world) * 0.07f;
            bool selected = selectedRing == ringId && selectedIndex == i;
            Handles.color = selected ? Color.white : color;
            if (Handles.Button(world, Quaternion.identity, size, size * 1.3f, Handles.SphereHandleCap))
            {
                selectedRing = ringId;
                selectedIndex = i;
            }
            if (!selected) continue;

            Quaternion rotation = Tools.pivotRotation == PivotRotation.Local ? model.rotation : Quaternion.identity;
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(world, rotation);
            if (!EditorGUI.EndChangeCheck()) continue;

            Undo.RecordObject(boat, "Maske noktası");
            Vector3 local = model.inverse.MultiplyPoint3x4(moved);
            ring[i] = local;
            if (boat.EditorMirror)
            {
                int mirror = Boat.MirrorIndex(i, ring.Length);
                if (mirror == i) ring[i].x = 0f;
                else ring[mirror] = new Vector3(-local.x, local.y, local.z);
            }
            boat.EditorInvalidatePreview();
            EditorUtility.SetDirty(boat);
        }
    }
}
