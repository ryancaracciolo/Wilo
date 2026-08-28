using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ChibiVestSettings))]
public class ChibiVestSettingsEditor : Editor
{
    const string AutoRebuildKey = "Wilo.ChibiVest.AutoRebuild";

    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        bool changed = EditorGUI.EndChangeCheck();

        EditorGUILayout.Space();

        bool autoRebuild = EditorPrefs.GetBool(AutoRebuildKey, true);
        bool wanted = EditorGUILayout.ToggleLeft("Rebuild as I edit", autoRebuild);
        if (wanted != autoRebuild)
            EditorPrefs.SetBool(AutoRebuildKey, wanted);

        if (GUILayout.Button("Rebuild Vest", GUILayout.Height(26f)) || (changed && wanted))
            Debug.Log(ChibiVestBuilder.Build());

        EditorGUILayout.HelpBox(
            "Distances are in the chibi mesh's local space: the character stands 0.475 tall, " +
            "hips at 0.077 and shoulders at 0.184. Rebuilding rewrites ChibiFishingVest.mesh " +
            "and the vest on the Player prefab.",
            MessageType.None);
    }
}
