using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Scene-view brush for scattering lily pad prefabs (or any other prefab) across the water.
/// Open from Wilo > Lily Pad Painter.
/// </summary>
public class LilyPadPainterWindow : EditorWindow
{
    const string DefaultPrefabPath =
        "Assets/HS_LowPoly/ForestEssentials/Prefabs/Water/P_HS_LP_Water_LilyPads_01.prefab";
    const string PrefsPrefix = "Wilo.LilyPadPainter.";

    enum ToolMode
    {
        Scatter,
        PlaceOne,
        Erase
    }

    GameObject prefab;
    Transform parent;
    ToolMode mode = ToolMode.Scatter;
    bool paintingEnabled;

    float brushRadius = 8f;
    float spacing = 1.45f;
    [Range(0.1f, 1f)] float strength = 1f;
    float scaleMin = 0.85f;
    float scaleMax = 1.15f;
    bool randomYaw = true;
    bool requireWaterHit = true;
    float yOffset;

    Vector3 lastStampPosition;
    bool hasLastStamp;
    int undoGroup = -1;
    int lastPaintedCount;

    readonly List<Vector3> existingPositions = new List<Vector3>(256);

    [MenuItem("Wilo/Lily Pad Painter")]
    public static void Open()
    {
        var window = GetWindow<LilyPadPainterWindow>("Lily Pad Painter");
        window.minSize = new Vector2(320, 420);
        window.Show();
    }

    void OnEnable()
    {
        LoadPrefs();
        if (prefab == null)
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabPath);

        SceneView.duringSceneGui += OnSceneGUI;
        wantsMouseMove = true;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        paintingEnabled = false;
        SavePrefs();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            paintingEnabled
                ? "Painting is on. Click and drag in the Scene view.\n[ ] resize brush · Shift+scroll radius · Esc to stop"
                : "Turn painting on, then click the water in the Scene view to scatter pads.",
            paintingEnabled ? MessageType.Info : MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = paintingEnabled ? new Color(0.45f, 0.85f, 0.5f) : prev;
            if (GUILayout.Button(paintingEnabled ? "Stop Painting" : "Start Painting", GUILayout.Height(32)))
            {
                paintingEnabled = !paintingEnabled;
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = prev;
        }

        EditorGUILayout.Space(6);
        mode = (ToolMode)GUILayout.Toolbar((int)mode, new[] { "Scatter", "Place One", "Erase" });

        EditorGUILayout.Space(8);
        prefab = (GameObject)EditorGUILayout.ObjectField("Prefab", prefab, typeof(GameObject), false);
        parent = (Transform)EditorGUILayout.ObjectField("Parent", parent, typeof(Transform), true);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Find / Create Parent"))
                parent = GetOrCreateParent(true);
            if (GUILayout.Button("Gather Existing Pads"))
                GatherExistingPads();
        }

        EditorGUILayout.Space(6);
        brushRadius = EditorGUILayout.Slider("Brush Radius", brushRadius, 0.5f, 40f);
        using (new EditorGUI.DisabledScope(mode == ToolMode.PlaceOne))
        {
            spacing = EditorGUILayout.Slider("Min Spacing", spacing, 0.4f, 6f);
            strength = EditorGUILayout.Slider("Fill Strength", strength, 0.1f, 1f);
        }

        scaleMin = EditorGUILayout.Slider("Scale Min", scaleMin, 0.3f, 2f);
        scaleMax = EditorGUILayout.Slider("Scale Max", scaleMax, 0.3f, 2f);
        if (scaleMax < scaleMin)
            scaleMax = scaleMin;

        randomYaw = EditorGUILayout.Toggle("Random Yaw", randomYaw);
        requireWaterHit = EditorGUILayout.Toggle("Only On Water", requireWaterHit);
        yOffset = EditorGUILayout.Slider("Y Offset", yOffset, -0.5f, 0.5f);

        EditorGUILayout.Space(8);
        int count = CountPads();
        EditorGUILayout.LabelField("Pads In Parent", count.ToString());
        if (lastPaintedCount > 0)
            EditorGUILayout.LabelField("Last Stroke", lastPaintedCount.ToString());

        using (new EditorGUI.DisabledScope(parent == null || count == 0))
        {
            if (GUILayout.Button("Clear All Pads In Parent"))
                ClearPads();
        }
    }

    void OnSceneGUI(SceneView sceneView)
    {
        if (!paintingEnabled)
            return;

        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            paintingEnabled = false;
            Repaint();
            sceneView.Repaint();
            e.Use();
            return;
        }

        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.LeftBracket)
            {
                brushRadius = Mathf.Max(0.5f, brushRadius * 0.8f);
                Repaint();
                e.Use();
            }
            else if (e.keyCode == KeyCode.RightBracket)
            {
                brushRadius = Mathf.Min(40f, brushRadius * 1.25f);
                Repaint();
                e.Use();
            }
        }

        if (e.type == EventType.ScrollWheel && e.shift)
        {
            brushRadius = Mathf.Clamp(brushRadius * (e.delta.y > 0f ? 0.9f : 1.1f), 0.5f, 40f);
            Repaint();
            e.Use();
        }

        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlId);

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 20000f))
        {
            if (e.type == EventType.MouseMove)
                sceneView.Repaint();
            return;
        }

        bool onWater = IsWaterHit(hit);
        bool canPaint = !requireWaterHit || onWater;
        Color brushColor = mode == ToolMode.Erase
            ? new Color(1f, 0.35f, 0.25f, 1f)
            : canPaint
                ? new Color(0.25f, 0.75f, 1f, 1f)
                : new Color(1f, 0.7f, 0.2f, 1f);

        Handles.zTest = CompareFunction.LessEqual;
        Handles.color = new Color(brushColor.r, brushColor.g, brushColor.b, 0.18f);
        Handles.DrawSolidDisc(hit.point, hit.normal, brushRadius);
        Handles.color = brushColor;
        Handles.DrawWireDisc(hit.point, hit.normal, brushRadius);
        Handles.DrawLine(hit.point, hit.point + hit.normal * 0.4f);

        Handles.BeginGUI();
        var labelRect = new Rect(e.mousePosition.x + 16f, e.mousePosition.y - 36f, 260f, 32f);
        string label = mode == ToolMode.Erase
            ? "Erase"
            : canPaint
                ? (mode == ToolMode.PlaceOne ? "Place one" : "Scatter")
                : "Not water — click the lake surface";
        GUI.Label(labelRect, label, EditorStyles.whiteLargeLabel);
        Handles.EndGUI();

        bool erasing = mode == ToolMode.Erase || e.control || e.command;
        bool mousePaint = e.button == 0 && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag);

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            GUIUtility.hotControl = controlId;
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(erasing ? "Erase Lily Pads" : "Paint Lily Pads");
            lastPaintedCount = 0;
            hasLastStamp = false;
            CacheExistingPositions();
            e.Use();
        }

        if (mousePaint && GUIUtility.hotControl == controlId)
        {
            if (erasing)
            {
                lastPaintedCount += EraseAt(hit.point);
            }
            else if (canPaint)
            {
                bool farEnough = !hasLastStamp ||
                                 Vector3.Distance(lastStampPosition, hit.point) >= brushRadius * 0.35f;
                if (e.type == EventType.MouseDown || farEnough)
                {
                    int placed = mode == ToolMode.PlaceOne
                        ? PlaceOne(hit)
                        : ScatterAt(hit);
                    lastPaintedCount += placed;
                    lastStampPosition = hit.point;
                    hasLastStamp = true;
                }
            }

            e.Use();
            Repaint();
        }

        if (e.type == EventType.MouseUp && e.button == 0 && GUIUtility.hotControl == controlId)
        {
            GUIUtility.hotControl = 0;
            if (undoGroup >= 0)
                Undo.CollapseUndoOperations(undoGroup);
            undoGroup = -1;
            e.Use();
            Repaint();
        }

        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            sceneView.Repaint();
    }

    int PlaceOne(RaycastHit hit)
    {
        if (prefab == null)
            return 0;

        Vector3 position = hit.point + hit.normal * yOffset;
        if (IsTooClose(position))
            return 0;

        CreatePad(position, hit.normal);
        return 1;
    }

    int ScatterAt(RaycastHit hit)
    {
        if (prefab == null)
            return 0;

        Vector3 right = Vector3.Cross(hit.normal, Vector3.forward);
        if (right.sqrMagnitude < 0.001f)
            right = Vector3.Cross(hit.normal, Vector3.right);
        right.Normalize();
        Vector3 forward = Vector3.Cross(right, hit.normal);

        float area = Mathf.PI * brushRadius * brushRadius;
        int target = Mathf.Max(1, Mathf.RoundToInt(area / (spacing * spacing) * strength));
        int attempts = target * 6;
        int placed = 0;

        for (int i = 0; i < attempts && placed < target; i++)
        {
            Vector2 offset = Random.insideUnitCircle * brushRadius;
            Vector3 candidate = hit.point + right * offset.x + forward * offset.y + hit.normal * 2f;
            if (!Physics.Raycast(candidate, -hit.normal, out RaycastHit snap, 8f))
                continue;
            if (requireWaterHit && !IsWaterHit(snap))
                continue;

            Vector3 position = snap.point + snap.normal * yOffset;
            if (IsTooClose(position))
                continue;

            CreatePad(position, snap.normal);
            placed++;
        }

        return placed;
    }

    void CreatePad(Vector3 position, Vector3 normal)
    {
        Transform container = GetOrCreateParent(false);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, container);
        Undo.RegisterCreatedObjectUndo(instance, "Paint Lily Pads");

        float yaw = randomYaw ? Random.Range(0f, 360f) : 0f;
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, yaw, 0f);
        float scale = Random.Range(scaleMin, scaleMax);
        instance.transform.SetPositionAndRotation(position, rotation);
        instance.transform.localScale = Vector3.one * scale;

        existingPositions.Add(position);
    }

    int EraseAt(Vector3 point)
    {
        Transform container = parent != null ? parent : GameObject.Find("LilyPads")?.transform;
        if (container == null)
            return 0;

        float radiusSq = brushRadius * brushRadius;
        int removed = 0;
        for (int i = container.childCount - 1; i >= 0; i--)
        {
            Transform child = container.GetChild(i);
            Vector3 delta = child.position - point;
            delta.y = 0f;
            if (delta.sqrMagnitude <= radiusSq)
            {
                Undo.DestroyObjectImmediate(child.gameObject);
                removed++;
            }
        }

        if (removed > 0)
            CacheExistingPositions();
        return removed;
    }

    bool IsTooClose(Vector3 position)
    {
        float minSq = spacing * spacing;
        for (int i = 0; i < existingPositions.Count; i++)
        {
            Vector3 delta = existingPositions[i] - position;
            delta.y = 0f;
            if (delta.sqrMagnitude < minSq)
                return true;
        }

        return false;
    }

    void CacheExistingPositions()
    {
        existingPositions.Clear();
        Transform container = parent != null ? parent : GameObject.Find("LilyPads")?.transform;
        if (container == null)
            return;

        for (int i = 0; i < container.childCount; i++)
            existingPositions.Add(container.GetChild(i).position);
    }

    static bool IsWaterHit(RaycastHit hit)
    {
        Transform t = hit.collider != null ? hit.collider.transform : null;
        while (t != null)
        {
            if (t.name == "Water" || t.name == "Surface")
                return true;
            t = t.parent;
        }

        return false;
    }

    Transform GetOrCreateParent(bool select)
    {
        if (parent != null)
            return parent;

        GameObject existing = GameObject.Find("LilyPads");
        if (existing != null)
        {
            parent = existing.transform;
            return parent;
        }

        var go = new GameObject("LilyPads");
        Undo.RegisterCreatedObjectUndo(go, "Create LilyPads");
        parent = go.transform;
        if (select)
            Selection.activeGameObject = go;
        return parent;
    }

    void GatherExistingPads()
    {
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("Lily Pad Painter", "Assign a prefab first.", "OK");
            return;
        }

        Transform container = GetOrCreateParent(true);
        string prefabPath = AssetDatabase.GetAssetPath(prefab);
        int moved = 0;

        var all = FindObjectsByType<Transform>(FindObjectsSortMode.None);
        Undo.SetCurrentGroupName("Gather Lily Pads");
        int group = Undo.GetCurrentGroup();

        foreach (Transform t in all)
        {
            if (t == null || t.parent == container)
                continue;
            if (t.parent != null && t.parent != t.root)
                continue;

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            if (source == null)
                continue;
            if (AssetDatabase.GetAssetPath(source) != prefabPath)
                continue;

            Undo.SetTransformParent(t, container, "Gather Lily Pads");
            moved++;
        }

        Undo.CollapseUndoOperations(group);
        CacheExistingPositions();
        Debug.Log($"Lily Pad Painter: gathered {moved} existing pad(s) under '{container.name}'.");
        Repaint();
    }

    int CountPads()
    {
        Transform container = parent != null ? parent : GameObject.Find("LilyPads")?.transform;
        return container != null ? container.childCount : 0;
    }

    void ClearPads()
    {
        Transform container = parent != null ? parent : GameObject.Find("LilyPads")?.transform;
        if (container == null || container.childCount == 0)
            return;

        if (!EditorUtility.DisplayDialog(
                "Clear Lily Pads",
                $"Delete all {container.childCount} objects under '{container.name}'?",
                "Delete",
                "Cancel"))
            return;

        Undo.SetCurrentGroupName("Clear Lily Pads");
        int group = Undo.GetCurrentGroup();
        for (int i = container.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(container.GetChild(i).gameObject);
        Undo.CollapseUndoOperations(group);
        existingPositions.Clear();
        lastPaintedCount = 0;
        Repaint();
    }

    void LoadPrefs()
    {
        brushRadius = EditorPrefs.GetFloat(PrefsPrefix + "Radius", 8f);
        spacing = EditorPrefs.GetFloat(PrefsPrefix + "Spacing", 1.45f);
        strength = EditorPrefs.GetFloat(PrefsPrefix + "Strength", 1f);
        scaleMin = EditorPrefs.GetFloat(PrefsPrefix + "ScaleMin", 0.85f);
        scaleMax = EditorPrefs.GetFloat(PrefsPrefix + "ScaleMax", 1.15f);
        randomYaw = EditorPrefs.GetBool(PrefsPrefix + "RandomYaw", true);
        requireWaterHit = EditorPrefs.GetBool(PrefsPrefix + "RequireWater", true);
        yOffset = EditorPrefs.GetFloat(PrefsPrefix + "YOffset", 0f);
        mode = (ToolMode)EditorPrefs.GetInt(PrefsPrefix + "Mode", 0);
    }

    void SavePrefs()
    {
        EditorPrefs.SetFloat(PrefsPrefix + "Radius", brushRadius);
        EditorPrefs.SetFloat(PrefsPrefix + "Spacing", spacing);
        EditorPrefs.SetFloat(PrefsPrefix + "Strength", strength);
        EditorPrefs.SetFloat(PrefsPrefix + "ScaleMin", scaleMin);
        EditorPrefs.SetFloat(PrefsPrefix + "ScaleMax", scaleMax);
        EditorPrefs.SetBool(PrefsPrefix + "RandomYaw", randomYaw);
        EditorPrefs.SetBool(PrefsPrefix + "RequireWater", requireWaterHit);
        EditorPrefs.SetFloat(PrefsPrefix + "YOffset", yOffset);
        EditorPrefs.SetInt(PrefsPrefix + "Mode", (int)mode);
    }
}
