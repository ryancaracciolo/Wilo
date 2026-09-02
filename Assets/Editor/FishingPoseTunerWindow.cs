using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Poses the held rod outside play mode so the cast and fight can be tuned
/// while watching the scene view. Values are edited straight on PlayerFishing,
/// so whatever looks right here is what gameplay uses.
/// Open from Wilo > Fishing Pose Tuner.
/// </summary>
public class FishingPoseTunerWindow : EditorWindow
{
    const string PrefsPrefix = "Wilo.FishingPoseTuner.";
    const int SweepSamples = 14;

    static readonly HumanBodyBones[] ArmBones =
    {
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.RightLowerArm,
        HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.LeftHand
    };

    static readonly string[] RodFields =
    {
        "poleScale",
        "poleThickness",
        "poleHoldOffset",
        "poleCastHoldOffset",
        "polePitch",
        "poleCastPitch",
        "poleCastLean"
    };

    readonly List<Transform> posedBones = new List<Transform>();
    readonly List<Quaternion> restRotations = new List<Quaternion>();
    readonly List<Vector3> blank = new List<Vector3>();
    readonly List<Vector3> sweepTrail = new List<Vector3>();

    PlayerFishing player;
    SerializedObject playerSo;
    FishingPole pole;
    Animator body;
    Vector2 scroll;

    bool previewing;
    PlayerFishing.Phase phase = PlayerFishing.Phase.Flying;
    float castStage;
    bool loopCast;
    float loopSeconds = 1.4f;
    float aimDistance = 14f;
    float fightSway;
    float fightLoad = 0.6f;
    bool fightHeld = true;

    bool showClearance = true;
    Vector3 headCentreLocal = new Vector3(0f, 0.98f, 0f);
    float headRadius = 0.36f;

    float worstClearance;
    float worstStage;
    double lastTick;

    [MenuItem("Wilo/Fishing Pose Tuner")]
    static void Open()
    {
        GetWindow<FishingPoseTunerWindow>("Rod Pose").minSize = new Vector2(330f, 460f);
    }

    void OnEnable()
    {
        castStage = EditorPrefs.GetFloat(PrefsPrefix + "stage", 0f);
        aimDistance = EditorPrefs.GetFloat(PrefsPrefix + "aim", 14f);
        headRadius = EditorPrefs.GetFloat(PrefsPrefix + "headRadius", 0.36f);
        headCentreLocal.y = EditorPrefs.GetFloat(PrefsPrefix + "headY", 0.98f);
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.update += Drive;
        AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    void OnDisable()
    {
        StopPreview();
        EditorPrefs.SetFloat(PrefsPrefix + "stage", castStage);
        EditorPrefs.SetFloat(PrefsPrefix + "aim", aimDistance);
        EditorPrefs.SetFloat(PrefsPrefix + "headRadius", headRadius);
        EditorPrefs.SetFloat(PrefsPrefix + "headY", headCentreLocal.y);
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.update -= Drive;
        AssemblyReloadEvents.beforeAssemblyReload -= StopPreview;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }

    void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode)
            StopPreview();
    }

    void OnGUI()
    {
        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Tuning runs in edit mode. Gameplay drives the rod itself while playing.",
                MessageType.Info);
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawTargetSection();
        if (player != null)
        {
            DrawPoseSection();
            DrawTuningSection();
            DrawClearanceSection();
            DrawCameraSection();
        }

        EditorGUILayout.EndScrollView();
    }

    void DrawTargetSection()
    {
        EditorGUI.BeginChangeCheck();
        var picked = (PlayerFishing)EditorGUILayout.ObjectField(
            "Angler", player, typeof(PlayerFishing), true);
        if (EditorGUI.EndChangeCheck())
            SetTarget(picked);

        if (player == null)
        {
            EditorGUILayout.HelpBox("No PlayerFishing assigned.", MessageType.Warning);
            if (GUILayout.Button("Find In Scene"))
                SetTarget(FindAnyObjectByType<PlayerFishing>());
            return;
        }

        using (new EditorGUI.DisabledScope(false))
        {
            if (GUILayout.Button(previewing ? "Stop Preview" : "Start Preview", GUILayout.Height(26f)))
            {
                if (previewing)
                    StopPreview();
                else
                    StartPreview();
            }
        }

        if (previewing)
        {
            EditorGUILayout.HelpBox(
                "Arm bones are being posed. Stopping the preview puts them back.",
                MessageType.None);
        }
    }

    void DrawPoseSection()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Pose", EditorStyles.boldLabel);
        phase = (PlayerFishing.Phase)EditorGUILayout.EnumPopup("Phase", phase);
        aimDistance = EditorGUILayout.Slider("Aim Distance", aimDistance, 2f, 40f);

        if (phase == PlayerFishing.Phase.Flying)
        {
            castStage = EditorGUILayout.Slider("Cast Sweep", castStage, 0f, 1f);
            loopCast = EditorGUILayout.Toggle("Loop Sweep", loopCast);
            if (loopCast)
                loopSeconds = EditorGUILayout.Slider("Loop Seconds", loopSeconds, 0.3f, 4f);
        }

        if (phase == PlayerFishing.Phase.Fighting)
        {
            fightSway = EditorGUILayout.Slider("Sway", fightSway, -1f, 1f);
            fightLoad = EditorGUILayout.Slider("Load", fightLoad, 0f, 1f);
            fightHeld = EditorGUILayout.Toggle("Reeling", fightHeld);
        }
    }

    void DrawTuningSection()
    {
        if (playerSo == null)
            return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Rod Settings", EditorStyles.boldLabel);
        playerSo.Update();
        foreach (string field in RodFields)
        {
            SerializedProperty prop = playerSo.FindProperty(field);
            if (prop != null)
                EditorGUILayout.PropertyField(prop);
        }

        playerSo.ApplyModifiedProperties();

        if (PrefabUtility.IsPartOfPrefabInstance(player)
            && GUILayout.Button("Apply Rod Settings To Prefab"))
        {
            PrefabUtility.ApplyObjectOverride(
                player,
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(player),
                InteractionMode.UserAction);
        }
    }

    void DrawClearanceSection()
    {
        EditorGUILayout.Space();
        showClearance = EditorGUILayout.ToggleLeft("Head Clearance Check", showClearance,
            EditorStyles.boldLabel);
        if (!showClearance)
            return;

        headCentreLocal.y = EditorGUILayout.Slider("Head Height", headCentreLocal.y, 0.4f, 1.6f);
        headRadius = EditorGUILayout.Slider("Head Radius", headRadius, 0.1f, 0.7f);

        if (!previewing)
        {
            EditorGUILayout.HelpBox("Start the preview to measure.", MessageType.None);
            return;
        }

        string reading = string.Format(
            "Worst clearance over the sweep: {0:F3} m (at sweep {1:F2})", worstClearance, worstStage);
        EditorGUILayout.HelpBox(
            reading,
            worstClearance < 0f ? MessageType.Error : MessageType.Info);
    }

    void DrawCameraSection()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Framing", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Casting Side"))
                Frame(player.transform.right * 1.7f - player.transform.forward * 1.0f + Vector3.up * 0.25f);
            if (GUILayout.Button("Front"))
                Frame(player.transform.forward * 2f + Vector3.up * 0.3f);
            if (GUILayout.Button("Behind"))
                Frame(-player.transform.forward * 2f + Vector3.up * 0.4f);
            if (GUILayout.Button("Above"))
                Frame(Vector3.up * 2f - player.transform.forward * 0.4f);
        }
    }

    void Frame(Vector3 offset)
    {
        SceneView sv = SceneView.lastActiveSceneView;
        if (sv == null)
            return;

        sv.orthographic = false;
        sv.LookAt(
            player.transform.position + Vector3.up * 0.9f,
            Quaternion.LookRotation(-offset.normalized),
            1.1f,
            false,
            false);
    }

    void SetTarget(PlayerFishing target)
    {
        if (previewing)
            StopPreview();

        player = target;
        playerSo = player != null ? new SerializedObject(player) : null;
    }

    void StartPreview()
    {
        if (player == null || Application.isPlaying)
            return;

        body = player.GetComponentInChildren<Animator>();
        pole = player.EditorRebuildPole();
        if (pole == null)
        {
            Debug.LogWarning("Fishing Pose Tuner: could not spawn the rod prefab.");
            return;
        }

        posedBones.Clear();
        restRotations.Clear();
        if (body != null && body.isHuman)
        {
            foreach (HumanBodyBones id in ArmBones)
            {
                Transform bone = body.GetBoneTransform(id);
                if (bone == null)
                    continue;
                posedBones.Add(bone);
                restRotations.Add(bone.localRotation);
            }
        }

        previewing = true;
        lastTick = EditorApplication.timeSinceStartup;
    }

    void StopPreview()
    {
        if (!previewing)
            return;

        previewing = false;
        for (int i = 0; i < posedBones.Count; i++)
        {
            if (posedBones[i] != null)
                posedBones[i].localRotation = restRotations[i];
        }

        posedBones.Clear();
        restRotations.Clear();
        if (pole != null)
            DestroyImmediate(pole.gameObject);

        pole = null;
        SceneView.RepaintAll();
    }

    void Drive()
    {
        if (!previewing)
            return;

        if (player == null || pole == null || Application.isPlaying)
        {
            StopPreview();
            return;
        }

        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - lastTick);
        lastTick = now;

        if (loopCast && phase == PlayerFishing.Phase.Flying)
        {
            castStage = Mathf.Repeat(castStage + dt / Mathf.Max(0.1f, loopSeconds), 1f);
            Repaint();
        }

        pole.Tick(true, MotionAt(castStage));
        if (showClearance)
            MeasureSweep();

        SceneView.RepaintAll();
    }

    FishingPole.Motion MotionAt(float stage)
    {
        Vector3 aim = player.transform.position + player.transform.forward * aimDistance;
        return player.BuildPoleMotion(phase, aim, stage, fightSway, fightLoad, fightHeld);
    }

    /// <summary>
    /// Walks the whole cast and records the tightest pass the blank makes at the
    /// head, then leaves the rod back on the stage the user is looking at.
    /// </summary>
    void MeasureSweep()
    {
        Vector3 centre = player.transform.TransformPoint(headCentreLocal);
        worstClearance = float.MaxValue;
        worstStage = castStage;
        sweepTrail.Clear();

        bool sweeps = phase == PlayerFishing.Phase.Flying;
        int samples = sweeps ? SweepSamples : 1;
        for (int i = 0; i < samples; i++)
        {
            float stage = samples == 1 ? castStage : i / (samples - 1f);
            pole.Tick(true, MotionAt(stage), false);
            sweepTrail.Add(pole.TipPosition);
            pole.CopyBlank(blank);
            float clearance = ClearanceOf(blank, centre) - headRadius;
            if (clearance < worstClearance)
            {
                worstClearance = clearance;
                worstStage = stage;
            }
        }

        pole.Tick(true, MotionAt(castStage), false);
        pole.CopyBlank(blank);
    }

    static float ClearanceOf(List<Vector3> polyline, Vector3 point)
    {
        float best = float.MaxValue;
        for (int i = 1; i < polyline.Count; i++)
            best = Mathf.Min(best, DistanceToSegment(point, polyline[i - 1], polyline[i]));
        return best;
    }

    static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lengthSq = ab.sqrMagnitude;
        if (lengthSq < 1e-6f)
            return Vector3.Distance(point, a);

        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSq);
        return Vector3.Distance(point, a + ab * t);
    }

    void OnSceneGUI(SceneView view)
    {
        if (!previewing || player == null || !showClearance)
            return;

        Vector3 centre = player.transform.TransformPoint(headCentreLocal);
        Handles.color = worstClearance < 0f
            ? new Color(1f, 0.35f, 0.3f, 0.9f)
            : new Color(0.4f, 0.9f, 0.5f, 0.6f);
        Handles.DrawWireDisc(centre, Vector3.up, headRadius);
        Handles.DrawWireDisc(centre, player.transform.right, headRadius);
        Handles.DrawWireDisc(centre, player.transform.forward, headRadius);

        if (sweepTrail.Count > 1)
        {
            Handles.color = new Color(1f, 0.85f, 0.35f, 0.9f);
            Handles.DrawAAPolyLine(3f, sweepTrail.ToArray());
        }

        if (blank.Count > 1)
        {
            Handles.color = new Color(0.4f, 0.75f, 1f, 0.9f);
            Handles.DrawAAPolyLine(4f, blank.ToArray());
        }
    }
}
