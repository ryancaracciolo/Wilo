using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Place a single fallen tree or log on the shoreline, leaning into the lake.
/// Open from Wilo > Fallen Tree Painter.
/// </summary>
public class FallenTreePainterWindow : EditorWindow
{
    const string PrefsPrefix = "Wilo.FallenTreePainter.";
    const string ParentName = "FallenTrees";

    struct Choice
    {
        public string Label;
        public string Path;
        public Choice(string label, string path)
        {
            Label = label;
            Path = path;
        }
    }

    static readonly Choice[] TreeChoices =
    {
        new Choice("Broken", "Assets/HS_LowPoly/ForestEssentials/Prefabs/Trees_Character/P_HS_LP_Tree_Broken_01.prefab"),
        new Choice("Burnt", "Assets/HS_LowPoly/ForestEssentials/Prefabs/Trees_Character/P_HS_LP_Tree_Burnt_01.prefab"),
        new Choice("Dead Oak", "Assets/HS_LowPoly/ForestEssentials/Prefabs/Trees_Character/P_HS_LP_Tree_DeadOak_01.prefab"),
        new Choice("Dead Pine", "Assets/HS_LowPoly/ForestEssentials/Prefabs/Trees_Character/P_HS_LP_Tree_DeadPine_01.prefab"),
        new Choice("Fallen", "Assets/HS_LowPoly/ForestEssentials/Prefabs/Trees_Character/P_HS_LP_Tree_Fallen_01.prefab"),
        new Choice("Leaning", "Assets/HS_LowPoly/ForestEssentials/Prefabs/Trees_Character/P_HS_LP_Tree_Leaning_01.prefab")
    };

    static readonly Choice[] LogChoices =
    {
        new Choice("Log 1", "Assets/HS_LowPoly/ForestEssentials/Prefabs/Logs/P_HS_LP_Log_01.prefab"),
        new Choice("Log 2", "Assets/HS_LowPoly/ForestEssentials/Prefabs/Logs/P_HS_LP_Log_02.prefab"),
        new Choice("Half Log", "Assets/HS_LowPoly/ForestEssentials/Prefabs/Logs/P_HS_LP_Log_Half_01.prefab"),
        new Choice("Moss Log 1", "Assets/HS_LowPoly/ForestEssentials/Prefabs/Logs/P_HS_LP_Log_Moss_01.prefab"),
        new Choice("Moss Log 2", "Assets/HS_LowPoly/ForestEssentials/Prefabs/Logs/P_HS_LP_Log_Moss_02.prefab"),
        new Choice("Branch 1", "Assets/HS_LowPoly/ForestEssentials/Prefabs/Logs/P_HS_LP_Branch_01.prefab"),
        new Choice("Branch 2", "Assets/HS_LowPoly/ForestEssentials/Prefabs/Logs/P_HS_LP_Branch_02.prefab"),
        new Choice("Branch 3", "Assets/HS_LowPoly/ForestEssentials/Prefabs/Logs/P_HS_LP_Branch_03.prefab")
    };

    enum ToolMode
    {
        Place,
        Erase
    }

    readonly RaycastHit[] hitBuffer = new RaycastHit[24];
    readonly List<GameObject> loadedTrees = new List<GameObject>();
    readonly List<GameObject> loadedLogs = new List<GameObject>();

    Transform parent;
    ToolMode mode = ToolMode.Place;
    bool paintingEnabled;
    int treeIndex;
    int logIndex = -1;
    bool selectingLog;

    float scale = 6f;
    float leanAngle = 22f;
    float yawJitter = 8f;
    float embed = 0.12f;
    float eraseRadius = 10f;
    bool onlyOnShoreline = true;

    GameObject preview;
    GameObject previewPrefab;
    int undoGroup = -1;
    int lastPaintedCount;
    float cachedWaterY;
    bool hasCachedWaterY;

    [MenuItem("Wilo/Fallen Tree Painter")]
    public static void Open()
    {
        var window = GetWindow<FallenTreePainterWindow>("Fallen Tree Painter");
        window.minSize = new Vector2(360, 560);
        window.Show();
    }

    void OnEnable()
    {
        LoadChoices();
        LoadPrefs();
        SceneView.duringSceneGui += OnSceneGUI;
        wantsMouseMove = true;
        hasCachedWaterY = false;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        paintingEnabled = false;
        DestroyPreview();
        SavePrefs();
    }

    void LoadChoices()
    {
        loadedTrees.Clear();
        loadedLogs.Clear();
        for (int i = 0; i < TreeChoices.Length; i++)
            loadedTrees.Add(AssetDatabase.LoadAssetAtPath<GameObject>(TreeChoices[i].Path));
        for (int i = 0; i < LogChoices.Length; i++)
            loadedLogs.Add(AssetDatabase.LoadAssetAtPath<GameObject>(LogChoices[i].Path));
    }

    GameObject SelectedPrefab
    {
        get
        {
            if (selectingLog)
            {
                if (logIndex >= 0 && logIndex < loadedLogs.Count)
                    return loadedLogs[logIndex];
                return null;
            }

            if (treeIndex >= 0 && treeIndex < loadedTrees.Count)
                return loadedTrees[treeIndex];
            return null;
        }
    }

    void OnGUI()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            paintingEnabled
                ? "Painting is on. Click the shoreline in the Scene view.\nThe piece leans into the water.  Esc to stop"
                : "Pick a tree or log, then click the bank. One piece per click, oriented into the lake.",
            paintingEnabled ? MessageType.Info : MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = paintingEnabled ? new Color(0.45f, 0.85f, 0.5f) : prev;
            if (GUILayout.Button(paintingEnabled ? "Stop Painting" : "Start Painting", GUILayout.Height(32)))
            {
                paintingEnabled = !paintingEnabled;
                if (!paintingEnabled)
                    DestroyPreview();
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = prev;
        }

        EditorGUILayout.Space(6);
        mode = (ToolMode)GUILayout.Toolbar((int)mode, new[] { "Place One", "Erase" });

        EditorGUILayout.Space(8);
        parent = (Transform)EditorGUILayout.ObjectField("Parent", parent, typeof(Transform), true);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Find / Create Parent"))
                parent = GetOrCreateParent(true);
            if (GUILayout.Button("Gather Existing"))
                GatherExisting();
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Trees", EditorStyles.boldLabel);
        DrawChoiceGrid(TreeChoices, loadedTrees, ref treeIndex, false);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Logs & Branches", EditorStyles.boldLabel);
        DrawChoiceGrid(LogChoices, loadedLogs, ref logIndex, true);

        EditorGUILayout.Space(8);
        using (new EditorGUI.DisabledScope(mode == ToolMode.Erase))
        {
            scale = EditorGUILayout.Slider("Scale", scale, 0.5f, 40f);
            leanAngle = EditorGUILayout.Slider("Lean Into Water", leanAngle, 0f, 80f);
            yawJitter = EditorGUILayout.Slider("Yaw Jitter", yawJitter, 0f, 45f);
            embed = EditorGUILayout.Slider("Embed", embed, 0f, 1.5f);
            onlyOnShoreline = EditorGUILayout.Toggle("Only On Shoreline", onlyOnShoreline);
        }

        using (new EditorGUI.DisabledScope(mode != ToolMode.Erase))
            eraseRadius = EditorGUILayout.Slider("Erase Radius", eraseRadius, 1f, 40f);

        EditorGUILayout.Space(8);
        int count = CountPlaced();
        EditorGUILayout.LabelField("Pieces In Parent", count.ToString());
        if (lastPaintedCount > 0)
            EditorGUILayout.LabelField("Last Action", lastPaintedCount.ToString());

        using (new EditorGUI.DisabledScope(parent == null || count == 0))
        {
            if (GUILayout.Button("Clear All In Parent"))
                ClearAll();
        }
    }

    void DrawChoiceGrid(Choice[] choices, List<GameObject> loaded, ref int selected, bool logs)
    {
        int columns = 3;
        int rows = Mathf.CeilToInt(choices.Length / (float)columns);
        int index = 0;
        for (int r = 0; r < rows; r++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int c = 0; c < columns; c++)
                {
                    if (index >= choices.Length)
                    {
                        GUILayout.FlexibleSpace();
                        continue;
                    }

                    bool isSelected = (!logs && !selectingLog && selected == index) ||
                                      (logs && selectingLog && selected == index);
                    Color prev = GUI.backgroundColor;
                    GUI.backgroundColor = isSelected ? new Color(0.45f, 0.85f, 0.5f) : prev;
                    EditorGUI.BeginDisabledGroup(loaded[index] == null);
                    if (GUILayout.Button(choices[index].Label, GUILayout.Height(24)))
                    {
                        selected = index;
                        selectingLog = logs;
                        if (logs)
                            treeIndex = -1;
                        else
                            logIndex = -1;
                        DestroyPreview();
                        SceneView.RepaintAll();
                    }

                    EditorGUI.EndDisabledGroup();
                    GUI.backgroundColor = prev;
                    index++;
                }
            }
        }
    }

    void OnSceneGUI(SceneView sceneView)
    {
        if (!paintingEnabled)
        {
            DestroyPreview();
            return;
        }

        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            paintingEnabled = false;
            DestroyPreview();
            Repaint();
            sceneView.Repaint();
            e.Use();
            return;
        }

        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlId);

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (!TryGetLakebedFromRay(ray, out RaycastHit bed, out float waterY, out _))
        {
            DestroyPreview();
            if (e.type == EventType.MouseMove)
                sceneView.Repaint();
            return;
        }

        Vector3 intoWater = EstimateIntoWater(bed.point, waterY);
        Vector3 placePoint = FindBankPoint(bed.point, waterY, intoWater);
        if (!TrySampleLakebed(placePoint, waterY, out RaycastHit placeHit, out _))
            placeHit = bed;

        float depth = waterY - placeHit.point.y;
        bool nearShore = !onlyOnShoreline || IsNearShoreline(placeHit.point, waterY);
        bool canPlace = mode == ToolMode.Erase || (SelectedPrefab != null && nearShore);

        Color brushColor = mode == ToolMode.Erase
            ? new Color(1f, 0.35f, 0.25f, 1f)
            : canPlace
                ? new Color(0.7f, 0.52f, 0.32f, 1f)
                : new Color(1f, 0.7f, 0.2f, 1f);

        float discRadius = mode == ToolMode.Erase ? eraseRadius : 2.5f;
        Handles.zTest = CompareFunction.LessEqual;
        Handles.color = new Color(brushColor.r, brushColor.g, brushColor.b, 0.18f);
        Handles.DrawSolidDisc(placeHit.point, Vector3.up, discRadius);
        Handles.color = brushColor;
        Handles.DrawWireDisc(placeHit.point, Vector3.up, discRadius);
        Handles.ArrowHandleCap(0, placeHit.point + Vector3.up * 0.4f, Quaternion.LookRotation(intoWater), 4f, EventType.Repaint);

        if (mode == ToolMode.Place && canPlace && SelectedPrefab != null)
        {
            Quaternion rotation = RotationFor(SelectedPrefab, intoWater, false);
            Vector3 position = placeHit.point - Vector3.up * embed;
            UpdatePreview(SelectedPrefab, position, rotation, Vector3.one * scale);
        }
        else
        {
            DestroyPreview();
        }

        Handles.BeginGUI();
        var labelRect = new Rect(e.mousePosition.x + 16f, e.mousePosition.y - 36f, 280f, 36f);
        GUI.Label(labelRect, PlacementLabel(canPlace, nearShore, depth), EditorStyles.whiteLargeLabel);
        Handles.EndGUI();

        bool erasing = mode == ToolMode.Erase || e.control || e.command;

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            GUIUtility.hotControl = controlId;
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(erasing ? "Erase Fallen Trees" : "Place Fallen Tree");
            lastPaintedCount = 0;

            if (erasing)
            {
                lastPaintedCount = EraseAt(placeHit.point);
            }
            else if (canPlace)
            {
                lastPaintedCount = PlaceOne(placeHit, intoWater);
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

    int PlaceOne(RaycastHit bed, Vector3 intoWater)
    {
        GameObject prefab = SelectedPrefab;
        if (prefab == null)
            return 0;

        Transform container = GetOrCreateParent(false);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, container);
        Undo.RegisterCreatedObjectUndo(instance, "Place Fallen Tree");

        Quaternion rotation = RotationFor(prefab, intoWater, true);
        Vector3 position = bed.point - Vector3.up * embed;
        instance.transform.SetPositionAndRotation(position, rotation);
        instance.transform.localScale = Vector3.one * scale;
        return 1;
    }

    Quaternion RotationFor(GameObject prefab, Vector3 intoWater, bool applyJitter)
    {
        Vector3 into = intoWater.sqrMagnitude > 0.001f ? intoWater.normalized : Vector3.forward;
        float jitter = applyJitter && yawJitter > 0f ? Random.Range(-yawJitter, yawJitter) : 0f;
        Vector3 leanAxis = Vector3.Cross(Vector3.up, into);
        if (leanAxis.sqrMagnitude < 0.001f)
            leanAxis = Vector3.right;
        leanAxis.Normalize();

        if (IsHorizontalPrefab(prefab))
        {
            Vector3 longAxis = PrefabLongAxis(prefab);
            Quaternion align = Quaternion.FromToRotation(longAxis, into);
            Quaternion dip = Quaternion.AngleAxis(leanAngle, leanAxis);
            return dip * align * Quaternion.Euler(0f, jitter, 0f);
        }

        Quaternion spin = Quaternion.Euler(0f, jitter, 0f);
        Quaternion lean = Quaternion.AngleAxis(leanAngle, leanAxis);
        return lean * spin;
    }

    static bool IsHorizontalPrefab(GameObject prefab)
    {
        Bounds bounds = PrefabBounds(prefab);
        float xz = Mathf.Max(bounds.size.x, bounds.size.z);
        return bounds.size.y < xz * 0.55f;
    }

    static Vector3 PrefabLongAxis(GameObject prefab)
    {
        Bounds bounds = PrefabBounds(prefab);
        return bounds.size.x >= bounds.size.z ? Vector3.right : Vector3.forward;
    }

    static Bounds PrefabBounds(GameObject prefab)
    {
        var renderers = prefab.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(Vector3.zero, Vector3.one);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    Vector3 EstimateIntoWater(Vector3 point, float waterY)
    {
        Vector3 best = Vector3.zero;
        float bestDepth = float.MinValue;
        for (int i = 0; i < 12; i++)
        {
            float ang = i * 30f;
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            Vector3 sample = point + dir * 6f;
            float groundY = SampleGroundY(sample, waterY);
            float depth = waterY - groundY;
            if (depth > bestDepth)
            {
                bestDepth = depth;
                best = dir;
            }
        }

        if (best.sqrMagnitude < 0.001f)
            best = Vector3.forward;
        best.y = 0f;
        return best.normalized;
    }

    Vector3 FindBankPoint(Vector3 point, float waterY, Vector3 intoWater)
    {
        float depth = waterY - SampleGroundY(point, waterY);
        if (depth <= 0.2f)
            return point;

        Vector3 towardLand = -intoWater;
        towardLand.y = 0f;
        if (towardLand.sqrMagnitude < 0.001f)
            return point;
        towardLand.Normalize();

        Vector3 best = point;
        for (int i = 1; i <= 20; i++)
        {
            Vector3 sample = point + towardLand * (i * 0.6f);
            float d = waterY - SampleGroundY(sample, waterY);
            best = sample;
            if (d <= 0.15f)
                return sample;
        }

        return best;
    }

    bool IsNearShoreline(Vector3 point, float waterY)
    {
        bool hasLand = false;
        bool hasWater = false;
        float[] distances = { 3f, 8f };
        for (int d = 0; d < distances.Length; d++)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                float depth = waterY - SampleGroundY(point + dir * distances[d], waterY);
                if (depth <= 0f)
                    hasLand = true;
                else
                    hasWater = true;
                if (hasLand && hasWater)
                    return true;
            }
        }

        float here = waterY - SampleGroundY(point, waterY);
        return Mathf.Abs(here) < 1.25f;
    }

    float SampleGroundY(Vector3 xz, float waterY)
    {
        if (TrySampleLakebed(xz, waterY, out RaycastHit hit, out _))
            return hit.point.y;

        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
            return terrain.SampleHeight(xz) + terrain.transform.position.y;
        return xz.y;
    }

    void UpdatePreview(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 localScale)
    {
        if (preview != null && previewPrefab != prefab)
            DestroyPreview();

        if (preview == null)
        {
            preview = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            preview.name = "FallenTreePreview";
            preview.hideFlags = HideFlags.HideAndDontSave;
            previewPrefab = prefab;
            var colliders = preview.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
        }

        preview.transform.SetPositionAndRotation(position, rotation);
        preview.transform.localScale = localScale;
    }

    void DestroyPreview()
    {
        if (preview != null)
        {
            DestroyImmediate(preview);
            preview = null;
            previewPrefab = null;
        }
    }

    int EraseAt(Vector3 point)
    {
        Transform container = parent != null ? parent : GameObject.Find(ParentName)?.transform;
        if (container == null)
            return 0;

        float radiusSq = eraseRadius * eraseRadius;
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

        return removed;
    }

    string PlacementLabel(bool canPlace, bool nearShore, float depth)
    {
        if (mode == ToolMode.Erase)
            return "Erase";
        if (SelectedPrefab == null)
            return "Pick a tree or log";
        if (!nearShore)
            return "Not shoreline — click the bank";
        if (canPlace)
            return "Place one  ·  into water";
        return depth <= 0f ? "On land" : "Can't place here";
    }

    bool TryGetLakebedFromRay(Ray ray, out RaycastHit bed, out float waterY, out bool hitWater)
    {
        bed = default;
        waterY = GetWaterY();
        hitWater = false;

        int count = Physics.RaycastNonAlloc(ray, hitBuffer, 20000f);
        if (count == 0)
            return false;

        int waterIndex = -1;
        int terrainIndex = -1;
        float waterDist = float.MaxValue;
        float terrainDist = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hitBuffer[i];
            if (IsWaterHit(hit))
            {
                if (hit.distance < waterDist)
                {
                    waterDist = hit.distance;
                    waterIndex = i;
                }
            }
            else if (IsTerrainHit(hit) && hit.distance < terrainDist)
            {
                terrainDist = hit.distance;
                terrainIndex = i;
            }
        }

        if (waterIndex >= 0)
        {
            hitWater = true;
            waterY = hitBuffer[waterIndex].point.y;
            cachedWaterY = waterY;
            hasCachedWaterY = true;
        }

        if (terrainIndex >= 0)
        {
            bed = hitBuffer[terrainIndex];
            return true;
        }

        if (waterIndex >= 0)
            return TrySampleLakebed(hitBuffer[waterIndex].point, waterY, out bed, out _);

        return false;
    }

    bool TrySampleLakebed(Vector3 xz, float waterY, out RaycastHit bed, out bool hitWater)
    {
        bed = default;
        hitWater = false;

        Vector3 origin = new Vector3(xz.x, waterY + 80f, xz.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, hitBuffer, 400f);
        if (count == 0)
            return false;

        int terrainIndex = -1;
        float terrainDist = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hitBuffer[i];
            if (IsWaterHit(hit))
                hitWater = true;
            else if (IsTerrainHit(hit) && hit.distance < terrainDist)
            {
                terrainDist = hit.distance;
                terrainIndex = i;
            }
        }

        if (terrainIndex < 0)
            return false;

        bed = hitBuffer[terrainIndex];
        if (!hitWater && bed.point.y < waterY - 0.02f)
            hitWater = true;
        return true;
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

    static bool IsTerrainHit(RaycastHit hit)
    {
        return hit.collider is TerrainCollider;
    }

    float GetWaterY()
    {
        if (hasCachedWaterY)
            return cachedWaterY;

        GameObject water = GameObject.Find("Water");
        if (water != null)
        {
            Transform surface = water.transform.Find("Surface");
            Renderer renderer = surface != null
                ? surface.GetComponent<Renderer>()
                : water.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                cachedWaterY = renderer.bounds.center.y;
                hasCachedWaterY = true;
                return cachedWaterY;
            }
        }

        return 201.7f;
    }

    Transform GetOrCreateParent(bool select)
    {
        if (parent != null)
            return parent;

        GameObject existing = GameObject.Find(ParentName);
        if (existing != null)
        {
            parent = existing.transform;
            return parent;
        }

        var go = new GameObject(ParentName);
        Undo.RegisterCreatedObjectUndo(go, "Create FallenTrees");
        parent = go.transform;
        if (select)
            Selection.activeGameObject = go;
        return parent;
    }

    void GatherExisting()
    {
        var allowedPaths = new HashSet<string>();
        for (int i = 0; i < TreeChoices.Length; i++)
            allowedPaths.Add(TreeChoices[i].Path);
        for (int i = 0; i < LogChoices.Length; i++)
            allowedPaths.Add(LogChoices[i].Path);

        Transform container = GetOrCreateParent(true);
        int moved = 0;
        var all = FindObjectsByType<Transform>();
        Undo.SetCurrentGroupName("Gather Fallen Trees");
        int group = Undo.GetCurrentGroup();

        foreach (Transform t in all)
        {
            if (t == null || t.parent == container)
                continue;
            if (t.parent != null)
                continue;

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            if (source == null)
                continue;
            string path = AssetDatabase.GetAssetPath(source);
            if (!allowedPaths.Contains(path))
                continue;

            Undo.SetTransformParent(t, container, "Gather Fallen Trees");
            moved++;
        }

        Undo.CollapseUndoOperations(group);
        Debug.Log($"Fallen Tree Painter: gathered {moved} existing piece(s) under '{container.name}'.");
        Repaint();
    }

    int CountPlaced()
    {
        Transform container = parent != null ? parent : GameObject.Find(ParentName)?.transform;
        return container != null ? container.childCount : 0;
    }

    void ClearAll()
    {
        Transform container = parent != null ? parent : GameObject.Find(ParentName)?.transform;
        if (container == null || container.childCount == 0)
            return;

        if (!EditorUtility.DisplayDialog(
                "Clear Fallen Trees",
                $"Delete all {container.childCount} objects under '{container.name}'?",
                "Delete",
                "Cancel"))
            return;

        Undo.SetCurrentGroupName("Clear Fallen Trees");
        int group = Undo.GetCurrentGroup();
        for (int i = container.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(container.GetChild(i).gameObject);
        Undo.CollapseUndoOperations(group);
        lastPaintedCount = 0;
        Repaint();
    }

    void LoadPrefs()
    {
        scale = EditorPrefs.GetFloat(PrefsPrefix + "Scale", 6f);
        leanAngle = EditorPrefs.GetFloat(PrefsPrefix + "Lean", 22f);
        yawJitter = EditorPrefs.GetFloat(PrefsPrefix + "YawJitter", 8f);
        embed = EditorPrefs.GetFloat(PrefsPrefix + "Embed", 0.12f);
        eraseRadius = EditorPrefs.GetFloat(PrefsPrefix + "EraseRadius", 10f);
        onlyOnShoreline = EditorPrefs.GetBool(PrefsPrefix + "OnlyShore", true);
        mode = (ToolMode)EditorPrefs.GetInt(PrefsPrefix + "Mode", 0);
        selectingLog = EditorPrefs.GetBool(PrefsPrefix + "SelectingLog", false);
        treeIndex = EditorPrefs.GetInt(PrefsPrefix + "TreeIndex", 4);
        logIndex = EditorPrefs.GetInt(PrefsPrefix + "LogIndex", -1);
        if (treeIndex < 0 && logIndex < 0)
            treeIndex = 4;
    }

    void SavePrefs()
    {
        EditorPrefs.SetFloat(PrefsPrefix + "Scale", scale);
        EditorPrefs.SetFloat(PrefsPrefix + "Lean", leanAngle);
        EditorPrefs.SetFloat(PrefsPrefix + "YawJitter", yawJitter);
        EditorPrefs.SetFloat(PrefsPrefix + "Embed", embed);
        EditorPrefs.SetFloat(PrefsPrefix + "EraseRadius", eraseRadius);
        EditorPrefs.SetBool(PrefsPrefix + "OnlyShore", onlyOnShoreline);
        EditorPrefs.SetInt(PrefsPrefix + "Mode", (int)mode);
        EditorPrefs.SetBool(PrefsPrefix + "SelectingLog", selectingLog);
        EditorPrefs.SetInt(PrefsPrefix + "TreeIndex", treeIndex);
        EditorPrefs.SetInt(PrefsPrefix + "LogIndex", logIndex);
    }
}
