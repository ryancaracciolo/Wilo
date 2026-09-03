using UnityEditor;
using UnityEngine;

/// <summary>
/// Live catch-hold knobs. Land a fish in Play Mode and drag the sliders;
/// the pose updates the same frame. Values write onto PlayerFishing.
/// Open from Wilo > Catch Pose Tuner.
/// </summary>
public class CatchPoseTunerWindow : EditorWindow
{
    static readonly string[] HoldFields =
    {
        "catchHoldPos",
        "catchCameraOffset",
        "catchLipDist",
        "catchLipDrop",
        "catchGripAlong",
        "catchGripOut"
    };

    PlayerFishing player;
    SerializedObject playerSo;
    Vector2 scroll;

    [MenuItem("Wilo/Catch Pose Tuner")]
    static void Open()
    {
        GetWindow<CatchPoseTunerWindow>("Catch Pose").minSize = new Vector2(340f, 420f);
    }

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.update += Tick;
        TryFindPlayer();
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.update -= Tick;
    }

    void Tick()
    {
        if (Application.isPlaying && player != null && player.ShowingCatch)
            Repaint();
    }

    void OnPlayModeChanged(PlayModeStateChange change)
    {
        TryFindPlayer();
        Repaint();
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawTarget();
        if (player != null)
            DrawTuning();
        EditorGUILayout.EndScrollView();
    }

    void DrawTarget()
    {
        EditorGUI.BeginChangeCheck();
        var picked = (PlayerFishing)EditorGUILayout.ObjectField(
            "Angler", player, typeof(PlayerFishing), true);
        if (EditorGUI.EndChangeCheck())
            SetTarget(picked);

        if (player == null)
        {
            EditorGUILayout.HelpBox("No PlayerFishing in the scene.", MessageType.Warning);
            if (GUILayout.Button("Find In Scene"))
                TryFindPlayer();
            return;
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode and land a fish. The sliders drive the live pose.",
                MessageType.Info);
            return;
        }

        if (player.ShowingCatch)
            EditorGUILayout.HelpBox("Showing a catch. Drag sliders and watch the Game view.", MessageType.Info);
        else
            EditorGUILayout.HelpBox("Land a bass to see the hold. Sliders still save on the player.", MessageType.None);
    }

    void DrawTuning()
    {
        if (playerSo == null)
            return;

        playerSo.Update();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Hold", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Used for every size. Hold XYZ is left / up / out from the chest. Lip Along is how far the mouth sits from the mesh centre.",
            MessageType.None);
        DrawFields(HoldFields);

        playerSo.ApplyModifiedProperties();

        EditorGUILayout.Space();
        if (PrefabUtility.IsPartOfPrefabInstance(player)
            && GUILayout.Button("Apply Catch Settings To Prefab"))
        {
            PrefabUtility.ApplyObjectOverride(
                player,
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(player),
                InteractionMode.UserAction);
        }

        if (GUILayout.Button("Frame Catch"))
            FrameCatch();
    }

    void DrawFields(string[] fields)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            SerializedProperty prop = playerSo.FindProperty(fields[i]);
            if (prop != null)
                EditorGUILayout.PropertyField(prop);
        }
    }

    void FrameCatch()
    {
        if (player == null)
            return;

        SceneView sv = SceneView.lastActiveSceneView;
        if (sv == null)
            return;

        Vector3 focus = player.transform.position + player.transform.up * 0.7f + player.transform.forward * 0.25f;
        Vector3 offset = player.transform.forward * 2.2f + player.transform.right * 0.4f + Vector3.up * 0.2f;
        sv.orthographic = false;
        sv.LookAt(focus, Quaternion.LookRotation(-offset.normalized), 1.4f, false, false);
    }

    void TryFindPlayer()
    {
        SetTarget(FindAnyObjectByType<PlayerFishing>());
    }

    void SetTarget(PlayerFishing target)
    {
        player = target;
        playerSo = player != null ? new SerializedObject(player) : null;
    }

    void OnSceneGUI(SceneView view)
    {
        if (player == null || !player.ShowingCatch)
            return;

        FishAgent fish = player.ShownCatch;
        Transform t = player.transform;
        Vector3 hold = t.TransformPoint(
            fish != null && fish.WantsTwoHandHold
                ? SerializedVector("catchHoldTwoHandLocal", new Vector3(-0.18f, 0.54f, 0.28f))
                : SerializedVector("catchHoldPos", new Vector3(-1f, 1.5f, 0f)));

        Handles.color = new Color(0.2f, 0.85f, 0.45f, 0.9f);
        Handles.SphereHandleCap(0, hold, Quaternion.identity, 0.04f, EventType.Repaint);
        Handles.Label(hold + Vector3.up * 0.06f, "Lip hold");

        if (fish == null)
            return;

        Handles.color = new Color(0.95f, 0.75f, 0.2f, 0.9f);
        Handles.DrawLine(hold, fish.transform.position);
        if (fish.WantsTwoHandHold)
        {
            Handles.color = new Color(0.35f, 0.6f, 1f, 0.9f);
            Handles.SphereHandleCap(0, fish.CatchSupportPoint, Quaternion.identity, 0.04f, EventType.Repaint);
            Handles.Label(fish.CatchSupportPoint + Vector3.up * 0.06f, "Belly support");
        }
    }

    Vector3 SerializedVector(string field, Vector3 fallback)
    {
        if (playerSo == null)
            return fallback;
        playerSo.Update();
        SerializedProperty prop = playerSo.FindProperty(field);
        return prop != null ? prop.vector3Value : fallback;
    }
}
