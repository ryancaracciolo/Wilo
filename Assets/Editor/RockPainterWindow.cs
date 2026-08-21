using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Scene-view brush for scattering underwater rocks onto the lakebed.
/// Click the water (or the bottom); rocks snap to terrain and stay submerged.
/// Open from Wilo > Rock Painter.
/// </summary>
public class RockPainterWindow : EditorWindow
{
    const string PrefsPrefix = "Wilo.RockPainter.";
    const string ParentName = "Rocks";
    const string RocksFolder = "Assets/HS_LowPoly/ForestEssentials/Prefabs/Rocks/";

    static readonly string[] DefaultPrefabNames =
    {
        "P_HS_LP_Rock_Small_01",
        "P_HS_LP_Rock_Small_02",
        "P_HS_LP_Rock_Small_03",
        "P_HS_LP_Rock_Small_04",
        "P_HS_LP_Rock_Medium_01",
        "P_HS_LP_Rock_Medium_02",
        "P_HS_LP_Rock_Medium_03",
        "P_HS_LP_Rock_Medium_04",
        "P_HS_LP_Rock_Large_01",
        "P_HS_LP_Rock_Large_02",
        "P_HS_LP_Rock_Large_03",
        "P_HS_LP_Rock_Flat_01",
        "P_HS_LP_Rock_Flat_02"
    };

    enum ToolMode
    {
        Scatter,
        PlaceOne,
        Erase
    }

    enum RockSize
    {
        Small,
        Medium,
        Large,
        Flat,
        Other
    }

    readonly List<GameObject> prefabs = new List<GameObject>();
    readonly List<Vector3> existingPositions = new List<Vector3>(256);
    readonly RaycastHit[] hitBuffer = new RaycastHit[24];

    Transform parent;
    ToolMode mode = ToolMode.Scatter;
    bool paintingEnabled;
    bool prefabFoldout = true;

    float brushRadius = 16f;
    float spacing = 4.5f;
    [Range(0.1f, 1f)] float strength = 0.7f;
    float scaleMin = 1.8f;
    float scaleMax = 4.2f;
    [Range(0f, 1f)] float sizeMix = 0.4f;
    bool randomYaw = true;
    [Range(0f, 1f)] float slopeAlign = 0.35f;
    float embed = 0.12f;
    bool requireUnderwater = true;
    float maxSlope = 50f;

    Vector3 lastStampPosition;
    bool hasLastStamp;
    int undoGroup = -1;
    int lastPaintedCount;
    float cachedWaterY;
    bool hasCachedWaterY;

    [MenuItem("Wilo/Rock Painter")]
    public static void Open()
    {
        var window = GetWindow<RockPainterWindow>("Rock Painter");
        window.minSize = new Vector2(340, 500);
        window.Show();
    }

    void OnEnable()
    {
        LoadPrefs();
        if (prefabs.Count == 0)
            LoadDefaultPrefabs();

        SceneView.duringSceneGui += OnSceneGUI;
        wantsMouseMove = true;
        hasCachedWaterY = false;
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
                ? "Painting is on. Click and drag the water in the Scene view.\nRocks snap to the lakebed.  [ ] resize · Shift+scroll radius · Esc to stop"
                : "Turn painting on, then paint rock fields on the lake. Rocks sit on the bottom, not the water surface.",
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
        parent = (Transform)EditorGUILayout.ObjectField("Parent", parent, typeof(Transform), true);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Find / Create Parent"))
                parent = GetOrCreateParent(true);
            if (GUILayout.Button("Gather Existing Rocks"))
                GatherExistingRocks();
        }

        EditorGUILayout.Space(6);
        prefabFoldout = EditorGUILayout.Foldout(prefabFoldout, "Rock Prefabs (" + CountValidPrefabs() + ")", true);
        if (prefabFoldout)
        {
            EditorGUI.indentLevel++;
            for (int i = 0; i < prefabs.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    prefabs[i] = (GameObject)EditorGUILayout.ObjectField(prefabs[i], typeof(GameObject), false);
                    if (GUILayout.Button("×", GUILayout.Width(22)))
                    {
                        prefabs.RemoveAt(i);
                        i--;
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Slot"))
                    prefabs.Add(null);
                if (GUILayout.Button("Load Defaults"))
                    LoadDefaultPrefabs();
            }

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(6);
        brushRadius = EditorGUILayout.Slider("Brush Radius", brushRadius, 1f, 60f);
        using (new EditorGUI.DisabledScope(mode == ToolMode.PlaceOne))
        {
            spacing = EditorGUILayout.Slider("Min Spacing", spacing, 0.8f, 20f);
            strength = EditorGUILayout.Slider("Fill Strength", strength, 0.1f, 1f);
        }

        scaleMin = EditorGUILayout.Slider("Scale Min", scaleMin, 0.4f, 40f);
        scaleMax = EditorGUILayout.Slider("Scale Max", scaleMax, 0.4f, 40f);
        if (scaleMax < scaleMin)
            scaleMax = scaleMin;

        sizeMix = EditorGUILayout.Slider("Size Mix (small → large)", sizeMix, 0f, 1f);
        randomYaw = EditorGUILayout.Toggle("Random Yaw", randomYaw);
        slopeAlign = EditorGUILayout.Slider("Align To Slope", slopeAlign, 0f, 1f);
        embed = EditorGUILayout.Slider("Embed", embed, 0f, 1f);

        EditorGUILayout.Space(6);
        requireUnderwater = EditorGUILayout.Toggle("Only Underwater", requireUnderwater);
        maxSlope = EditorGUILayout.Slider("Max Slope", maxSlope, 5f, 89f);

        EditorGUILayout.Space(8);
        int count = CountRocks();
        EditorGUILayout.LabelField("Rocks In Parent", count.ToString());
        if (lastPaintedCount > 0)
            EditorGUILayout.LabelField("Last Stroke", lastPaintedCount.ToString());

        using (new EditorGUI.DisabledScope(parent == null || count == 0))
        {
            if (GUILayout.Button("Clear All Rocks In Parent"))
                ClearRocks();
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
                brushRadius = Mathf.Max(1f, brushRadius * 0.8f);
                Repaint();
                e.Use();
            }
            else if (e.keyCode == KeyCode.RightBracket)
            {
                brushRadius = Mathf.Min(60f, brushRadius * 1.25f);
                Repaint();
                e.Use();
            }
        }

        if (e.type == EventType.ScrollWheel && e.shift)
        {
            brushRadius = Mathf.Clamp(brushRadius * (e.delta.y > 0f ? 0.9f : 1.1f), 1f, 60f);
            Repaint();
            e.Use();
        }

        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlId);

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (!TryGetLakebedFromRay(ray, out RaycastHit bed, out float waterY, out bool hitWater))
        {
            if (e.type == EventType.MouseMove)
                sceneView.Repaint();
            return;
        }

        float depth = waterY - bed.point.y;
        float slope = Vector3.Angle(Vector3.up, bed.normal);
        bool canPaint = IsValidPlacement(depth, slope);

        Color brushColor = mode == ToolMode.Erase
            ? new Color(1f, 0.35f, 0.25f, 1f)
            : canPaint
                ? new Color(0.55f, 0.62f, 0.72f, 1f)
                : new Color(1f, 0.7f, 0.2f, 1f);

        Handles.zTest = CompareFunction.LessEqual;
        Handles.color = new Color(brushColor.r, brushColor.g, brushColor.b, 0.18f);
        Handles.DrawSolidDisc(bed.point, bed.normal, brushRadius);
        Handles.color = brushColor;
        Handles.DrawWireDisc(bed.point, bed.normal, brushRadius);
        Handles.DrawLine(bed.point, bed.point + bed.normal * 0.6f);

        if (hitWater)
        {
            Vector3 waterPoint = new Vector3(bed.point.x, waterY, bed.point.z);
            Handles.color = new Color(0.25f, 0.55f, 0.85f, 0.35f);
            Handles.DrawWireDisc(waterPoint, Vector3.up, brushRadius);
        }

        Handles.BeginGUI();
        var labelRect = new Rect(e.mousePosition.x + 16f, e.mousePosition.y - 40f, 280f, 40f);
        GUI.Label(labelRect, PlacementLabel(canPaint, depth, slope), EditorStyles.whiteLargeLabel);
        Handles.EndGUI();

        bool erasing = mode == ToolMode.Erase || e.control || e.command;
        bool mousePaint = e.button == 0 && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag);

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            GUIUtility.hotControl = controlId;
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(erasing ? "Erase Rocks" : "Paint Rocks");
            lastPaintedCount = 0;
            hasLastStamp = false;
            CacheExistingPositions();
            e.Use();
        }

        if (mousePaint && GUIUtility.hotControl == controlId)
        {
            if (erasing)
            {
                lastPaintedCount += EraseAt(bed.point);
            }
            else if (canPaint)
            {
                bool farEnough = !hasLastStamp ||
                                 Vector3.Distance(lastStampPosition, bed.point) >= brushRadius * 0.35f;
                if (e.type == EventType.MouseDown || farEnough)
                {
                    int placed = mode == ToolMode.PlaceOne
                        ? PlaceOne(bed)
                        : ScatterAt(bed, waterY);
                    lastPaintedCount += placed;
                    lastStampPosition = bed.point;
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

    int PlaceOne(RaycastHit bed)
    {
        GameObject prefab = PickPrefab(out RockSize size);
        if (prefab == null)
            return 0;

        if (IsTooClose(bed.point, SpacingFor(size)))
            return 0;

        CreateRock(prefab, bed);
        return 1;
    }

    int ScatterAt(RaycastHit bed, float waterY)
    {
        if (CountValidPrefabs() == 0)
            return 0;

        Vector3 right = Vector3.Cross(bed.normal, Vector3.forward);
        if (right.sqrMagnitude < 0.001f)
            right = Vector3.Cross(bed.normal, Vector3.right);
        right.Normalize();
        Vector3 forward = Vector3.Cross(right, bed.normal);

        float area = Mathf.PI * brushRadius * brushRadius;
        int target = Mathf.Max(1, Mathf.RoundToInt(area / (spacing * spacing) * strength));
        int attempts = target * 8;
        int placed = 0;

        for (int i = 0; i < attempts && placed < target; i++)
        {
            Vector2 offset = Random.insideUnitCircle * brushRadius;
            Vector3 xz = bed.point + right * offset.x + forward * offset.y;
            if (!TrySampleLakebed(xz, waterY, out RaycastHit snap, out _))
                continue;

            float depth = waterY - snap.point.y;
            float slope = Vector3.Angle(Vector3.up, snap.normal);
            if (!IsValidPlacement(depth, slope))
                continue;

            GameObject prefab = PickPrefab(out RockSize size);
            if (prefab == null)
                continue;
            if (IsTooClose(snap.point, SpacingFor(size)))
                continue;

            CreateRock(prefab, snap);
            placed++;
        }

        return placed;
    }

    void CreateRock(GameObject prefab, RaycastHit bed)
    {
        Transform container = GetOrCreateParent(false);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, container);
        Undo.RegisterCreatedObjectUndo(instance, "Paint Rocks");

        float yaw = randomYaw ? Random.Range(0f, 360f) : 0f;
        Vector3 up = Vector3.Slerp(Vector3.up, bed.normal, slopeAlign).normalized;
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, yaw, 0f);
        float scale = Random.Range(scaleMin, scaleMax);
        Vector3 position = bed.point - up * embed * scale;

        instance.transform.SetPositionAndRotation(position, rotation);
        instance.transform.localScale = Vector3.one * scale;

        existingPositions.Add(position);
    }

    int EraseAt(Vector3 point)
    {
        Transform container = parent != null ? parent : GameObject.Find(ParentName)?.transform;
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

    bool IsTooClose(Vector3 position, float minSpacing)
    {
        float minSq = minSpacing * minSpacing;
        for (int i = 0; i < existingPositions.Count; i++)
        {
            Vector3 delta = existingPositions[i] - position;
            delta.y = 0f;
            if (delta.sqrMagnitude < minSq)
                return true;
        }

        return false;
    }

    float SpacingFor(RockSize size)
    {
        switch (size)
        {
            case RockSize.Small: return spacing * 0.55f;
            case RockSize.Large: return spacing * 1.7f;
            case RockSize.Flat: return spacing * 1.15f;
            default: return spacing;
        }
    }

    void CacheExistingPositions()
    {
        existingPositions.Clear();
        Transform container = parent != null ? parent : GameObject.Find(ParentName)?.transform;
        if (container == null)
            return;

        for (int i = 0; i < container.childCount; i++)
            existingPositions.Add(container.GetChild(i).position);
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

        Vector3 origin = new Vector3(xz.x, waterY + 40f, xz.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, hitBuffer, 250f);
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

    bool IsValidPlacement(float depth, float slope)
    {
        if (slope > maxSlope)
            return false;
        if (requireUnderwater && depth <= 0f)
            return false;
        return true;
    }

    string PlacementLabel(bool canPaint, float depth, float slope)
    {
        if (mode == ToolMode.Erase)
            return "Erase";
        if (canPaint)
            return mode == ToolMode.PlaceOne ? "Place one" : "Scatter";
        if (requireUnderwater && depth <= 0f)
            return "On land — paint the lake";
        if (slope > maxSlope)
            return $"Too steep ({slope:0}°)";
        return "Can't place here";
    }

    GameObject PickPrefab(out RockSize size)
    {
        size = RockSize.Medium;
        if (CountValidPrefabs() == 0)
            return null;

        float smallW = Mathf.Lerp(0.58f, 0.14f, sizeMix);
        float mediumW = 0.28f;
        float largeW = Mathf.Lerp(0.07f, 0.42f, sizeMix);
        float flatW = Mathf.Lerp(0.07f, 0.16f, sizeMix);
        float total = smallW + mediumW + largeW + flatW;
        float roll = Random.value * total;

        RockSize wanted;
        if (roll < smallW)
            wanted = RockSize.Small;
        else if (roll < smallW + mediumW)
            wanted = RockSize.Medium;
        else if (roll < smallW + mediumW + largeW)
            wanted = RockSize.Large;
        else
            wanted = RockSize.Flat;

        GameObject match = PickPrefabOfSize(wanted);
        if (match != null)
        {
            size = wanted;
            return match;
        }

        GameObject any = PickAnyPrefab();
        size = ClassifySize(any);
        return any;
    }

    GameObject PickPrefabOfSize(RockSize wanted)
    {
        int count = 0;
        for (int i = 0; i < prefabs.Count; i++)
        {
            if (prefabs[i] != null && ClassifySize(prefabs[i]) == wanted)
                count++;
        }

        if (count == 0)
            return null;

        int pick = Random.Range(0, count);
        for (int i = 0; i < prefabs.Count; i++)
        {
            if (prefabs[i] == null || ClassifySize(prefabs[i]) != wanted)
                continue;
            if (pick == 0)
                return prefabs[i];
            pick--;
        }

        return null;
    }

    GameObject PickAnyPrefab()
    {
        int count = CountValidPrefabs();
        if (count == 0)
            return null;

        int pick = Random.Range(0, count);
        for (int i = 0; i < prefabs.Count; i++)
        {
            if (prefabs[i] == null)
                continue;
            if (pick == 0)
                return prefabs[i];
            pick--;
        }

        return null;
    }

    static RockSize ClassifySize(GameObject prefab)
    {
        if (prefab == null)
            return RockSize.Other;

        string n = prefab.name;
        if (n.Contains("Small"))
            return RockSize.Small;
        if (n.Contains("Large") || n.Contains("Cliff"))
            return RockSize.Large;
        if (n.Contains("Flat"))
            return RockSize.Flat;
        if (n.Contains("Medium"))
            return RockSize.Medium;
        return RockSize.Other;
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
        Undo.RegisterCreatedObjectUndo(go, "Create Rocks");
        parent = go.transform;
        if (select)
            Selection.activeGameObject = go;
        return parent;
    }

    void GatherExistingRocks()
    {
        if (CountValidPrefabs() == 0)
        {
            EditorUtility.DisplayDialog("Rock Painter", "Assign at least one rock prefab first.", "OK");
            return;
        }

        var allowedPaths = new HashSet<string>();
        for (int i = 0; i < prefabs.Count; i++)
        {
            if (prefabs[i] == null)
                continue;
            string path = AssetDatabase.GetAssetPath(prefabs[i]);
            if (!string.IsNullOrEmpty(path))
                allowedPaths.Add(path);
        }

        Transform container = GetOrCreateParent(true);
        int moved = 0;
        var all = FindObjectsByType<Transform>();
        Undo.SetCurrentGroupName("Gather Rocks");
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

            Undo.SetTransformParent(t, container, "Gather Rocks");
            moved++;
        }

        Undo.CollapseUndoOperations(group);
        CacheExistingPositions();
        Debug.Log($"Rock Painter: gathered {moved} existing rock(s) under '{container.name}'.");
        Repaint();
    }

    int CountRocks()
    {
        Transform container = parent != null ? parent : GameObject.Find(ParentName)?.transform;
        return container != null ? container.childCount : 0;
    }

    static bool IsMossPrefab(GameObject prefab)
    {
        return prefab != null && prefab.name.IndexOf("Moss", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    int CountValidPrefabs()
    {
        int n = 0;
        for (int i = 0; i < prefabs.Count; i++)
        {
            if (prefabs[i] != null)
                n++;
        }

        return n;
    }

    void ClearRocks()
    {
        Transform container = parent != null ? parent : GameObject.Find(ParentName)?.transform;
        if (container == null || container.childCount == 0)
            return;

        if (!EditorUtility.DisplayDialog(
                "Clear Rocks",
                $"Delete all {container.childCount} objects under '{container.name}'?",
                "Delete",
                "Cancel"))
            return;

        Undo.SetCurrentGroupName("Clear Rocks");
        int group = Undo.GetCurrentGroup();
        for (int i = container.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(container.GetChild(i).gameObject);
        Undo.CollapseUndoOperations(group);
        existingPositions.Clear();
        lastPaintedCount = 0;
        Repaint();
    }

    void LoadDefaultPrefabs()
    {
        prefabs.Clear();
        for (int i = 0; i < DefaultPrefabNames.Length; i++)
        {
            string path = RocksFolder + DefaultPrefabNames[i] + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && !IsMossPrefab(prefab))
                prefabs.Add(prefab);
        }
    }

    void LoadPrefs()
    {
        brushRadius = EditorPrefs.GetFloat(PrefsPrefix + "Radius", 16f);
        spacing = EditorPrefs.GetFloat(PrefsPrefix + "Spacing", 4.5f);
        strength = EditorPrefs.GetFloat(PrefsPrefix + "Strength", 0.7f);
        scaleMin = EditorPrefs.GetFloat(PrefsPrefix + "ScaleMin", 1.8f);
        scaleMax = EditorPrefs.GetFloat(PrefsPrefix + "ScaleMax", 4.2f);
        sizeMix = EditorPrefs.GetFloat(PrefsPrefix + "SizeMix", 0.4f);
        randomYaw = EditorPrefs.GetBool(PrefsPrefix + "RandomYaw", true);
        slopeAlign = EditorPrefs.GetFloat(PrefsPrefix + "SlopeAlign", 0.35f);
        embed = EditorPrefs.GetFloat(PrefsPrefix + "Embed", 0.12f);
        requireUnderwater = EditorPrefs.GetBool(PrefsPrefix + "RequireUnderwater", true);
        maxSlope = EditorPrefs.GetFloat(PrefsPrefix + "MaxSlope", 50f);
        mode = (ToolMode)EditorPrefs.GetInt(PrefsPrefix + "Mode", 0);

        prefabs.Clear();
        string saved = EditorPrefs.GetString(PrefsPrefix + "Prefabs", string.Empty);
        if (!string.IsNullOrEmpty(saved))
        {
            string[] paths = saved.Split('|');
            for (int i = 0; i < paths.Length; i++)
            {
                if (string.IsNullOrEmpty(paths[i]))
                    continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                if (prefab != null && !IsMossPrefab(prefab))
                    prefabs.Add(prefab);
            }
        }
    }

    void SavePrefs()
    {
        EditorPrefs.SetFloat(PrefsPrefix + "Radius", brushRadius);
        EditorPrefs.SetFloat(PrefsPrefix + "Spacing", spacing);
        EditorPrefs.SetFloat(PrefsPrefix + "Strength", strength);
        EditorPrefs.SetFloat(PrefsPrefix + "ScaleMin", scaleMin);
        EditorPrefs.SetFloat(PrefsPrefix + "ScaleMax", scaleMax);
        EditorPrefs.SetFloat(PrefsPrefix + "SizeMix", sizeMix);
        EditorPrefs.SetBool(PrefsPrefix + "RandomYaw", randomYaw);
        EditorPrefs.SetFloat(PrefsPrefix + "SlopeAlign", slopeAlign);
        EditorPrefs.SetFloat(PrefsPrefix + "Embed", embed);
        EditorPrefs.SetBool(PrefsPrefix + "RequireUnderwater", requireUnderwater);
        EditorPrefs.SetFloat(PrefsPrefix + "MaxSlope", maxSlope);
        EditorPrefs.SetInt(PrefsPrefix + "Mode", (int)mode);

        var paths = new List<string>();
        for (int i = 0; i < prefabs.Count; i++)
        {
            if (prefabs[i] == null)
                continue;
            string path = AssetDatabase.GetAssetPath(prefabs[i]);
            if (!string.IsNullOrEmpty(path))
                paths.Add(path);
        }

        EditorPrefs.SetString(PrefsPrefix + "Prefabs", string.Join("|", paths));
    }
}
