using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared editor helpers so painted lake props (rocks, stumps, grass, fallen trees)
/// get a <see cref="TerrainAnchor"/> and can be migrated or resnapped from any painter.
/// </summary>
public static class TerrainAnchorEditorUtil
{
    public static void DrawFollowSection(Transform container, string nounPlural)
    {
        bool follow = TerrainAnchorSync.FollowTerrain;
        bool nextFollow = EditorGUILayout.Toggle("Follow Terrain Edits", follow);
        if (nextFollow != follow)
            TerrainAnchorSync.FollowTerrain = nextFollow;

        EditorGUILayout.HelpBox(
            $"Anchored {nounPlural} stay planted when you sculpt the lakebed. New paints get an anchor automatically. Existing {nounPlural} need the button below once.",
            MessageType.None);

        int count = CountPainted(container);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(count == 0))
            {
                if (GUILayout.Button("Anchor Existing"))
                {
                    int added = AnchorExisting(container);
                    Debug.Log($"Anchored {added} existing {nounPlural} to the terrain.");
                }

                if (GUILayout.Button("Plant On Terrain"))
                {
                    int snapped = ResnapAll(container);
                    Debug.Log($"Planted {snapped} {nounPlural} on the terrain.");
                    SceneView.RepaintAll();
                }
            }
        }
    }

    public static TerrainAnchor AddConfigured(GameObject instance, float embed, float yaw, float slopeAlign)
    {
        TerrainAnchor anchor = GetOrAdd(instance);
        anchor.Configure(embed, yaw, slopeAlign);
        return anchor;
    }

    public static TerrainAnchor AddKeepingRotation(GameObject instance)
    {
        TerrainAnchor anchor = GetOrAdd(instance);
        Terrain terrain = TerrainAnchor.FindTerrain(instance.transform.position);
        if (terrain != null)
            anchor.CaptureKeepingRotation(terrain);
        return anchor;
    }

    public static List<Transform> CollectPainted(Transform container)
    {
        var list = new List<Transform>(256);
        if (container == null)
            return list;

        Transform[] transforms = container.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform t = transforms[i];
            if (t == null || t == container)
                continue;
            if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))
                list.Add(t);
        }

        return list;
    }

    public static int CountPainted(Transform container)
    {
        return CollectPainted(container).Count;
    }

    public static void CachePositions(Transform container, List<Vector3> into)
    {
        into.Clear();
        List<Transform> painted = CollectPainted(container);
        for (int i = 0; i < painted.Count; i++)
            into.Add(painted[i].position);
    }

    public static int EraseInRadius(Transform container, Vector3 point, float radius)
    {
        if (container == null)
            return 0;

        List<Transform> painted = CollectPainted(container);
        float radiusSq = radius * radius;
        int removed = 0;
        for (int i = painted.Count - 1; i >= 0; i--)
        {
            Transform child = painted[i];
            if (child == null)
                continue;
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

    public static readonly string[] PaintedParentNames =
    {
        "Rocks",
        "Stumps",
        "FallenTrees",
        "Grass",
        "WeedBeds"
    };

    public static int EnsureMissingAnchors()
    {
        int added = 0;
        for (int n = 0; n < PaintedParentNames.Length; n++)
        {
            Transform root = GameObject.Find(PaintedParentNames[n])?.transform;
            if (root != null)
                added += EnsureMissingUnder(root);
        }

        return added;
    }

    static int EnsureMissingUnder(Transform container)
    {
        List<Transform> painted = CollectPainted(container);
        Terrain fallback = Terrain.activeTerrain;
        int added = 0;
        for (int i = 0; i < painted.Count; i++)
        {
            GameObject go = painted[i].gameObject;
            if (go.GetComponent<TerrainAnchor>() != null)
                continue;

            Terrain terrain = TerrainAnchor.FindTerrain(go.transform.position);
            if (terrain == null)
                terrain = fallback;
            if (terrain == null)
                continue;

            var anchor = go.AddComponent<TerrainAnchor>();
            anchor.CaptureKeepingRotation(terrain);
            added++;
        }

        TerrainAnchor[] stray = container.GetComponentsInChildren<TerrainAnchor>(true);
        for (int i = 0; i < stray.Length; i++)
        {
            TerrainAnchor anchor = stray[i];
            if (anchor == null)
                continue;
            if (PrefabUtility.IsAnyPrefabInstanceRoot(anchor.gameObject))
                continue;
            Object.DestroyImmediate(anchor);
        }

        return added;
    }

    public static int AnchorExisting(Transform container)
    {
        if (container == null)
            return 0;

        List<Transform> painted = CollectPainted(container);
        if (painted.Count == 0)
            return 0;

        Undo.SetCurrentGroupName("Anchor To Terrain");
        int group = Undo.GetCurrentGroup();
        int added = 0;
        for (int i = 0; i < painted.Count; i++)
        {
            GameObject go = painted[i].gameObject;
            if (go.GetComponent<TerrainAnchor>() != null)
                continue;

            Terrain terrain = TerrainAnchor.FindTerrain(go.transform.position);
            if (terrain == null)
                continue;

            var anchor = Undo.AddComponent<TerrainAnchor>(go);
            anchor.CaptureKeepingRotation(terrain);
            added++;
        }

        StripAnchorsFromGroups(container);
        Undo.CollapseUndoOperations(group);
        return added;
    }

    public static int ResnapAll(Transform container)
    {
        if (container == null)
            return 0;

        List<Transform> painted = CollectPainted(container);
        if (painted.Count == 0)
            return 0;

        Undo.SetCurrentGroupName("Resnap To Terrain");
        int group = Undo.GetCurrentGroup();
        int snapped = 0;
        for (int i = 0; i < painted.Count; i++)
        {
            Transform child = painted[i];
            Terrain terrain = TerrainAnchor.FindTerrain(child.position);
            if (terrain == null)
                continue;

            var anchor = child.GetComponent<TerrainAnchor>();
            if (anchor == null)
            {
                anchor = Undo.AddComponent<TerrainAnchor>(child.gameObject);
                anchor.CaptureKeepingRotation(terrain);
            }

            Undo.RecordObject(child, "Resnap To Terrain");
            bool useMeshBottom = container.name != "FallenTrees";
            anchor.PlantOnSurface(terrain, useMeshBottom);
            snapped++;
        }

        Undo.CollapseUndoOperations(group);
        return snapped;
    }

    public static int PlantAllOnSurface()
    {
        int planted = 0;
        for (int n = 0; n < PaintedParentNames.Length; n++)
        {
            Transform root = GameObject.Find(PaintedParentNames[n])?.transform;
            if (root != null)
                planted += PlantUnder(root, root.name != "FallenTrees");
        }

        return planted;
    }

    static int PlantUnder(Transform container, bool useMeshBottom)
    {
        List<Transform> painted = CollectPainted(container);
        Terrain fallback = Terrain.activeTerrain;
        int planted = 0;
        for (int i = 0; i < painted.Count; i++)
        {
            Transform child = painted[i];
            Terrain terrain = TerrainAnchor.FindTerrain(child.position);
            if (terrain == null)
                terrain = fallback;
            if (terrain == null)
                continue;

            var anchor = child.GetComponent<TerrainAnchor>();
            if (anchor == null)
            {
                anchor = child.gameObject.AddComponent<TerrainAnchor>();
                anchor.CaptureKeepingRotation(terrain);
            }

            anchor.PlantOnSurface(terrain, useMeshBottom);
            planted++;
        }

        return planted;
    }

    static TerrainAnchor GetOrAdd(GameObject instance)
    {
        var anchor = instance.GetComponent<TerrainAnchor>();
        if (anchor == null)
            anchor = Undo.AddComponent<TerrainAnchor>(instance);
        return anchor;
    }

    static void StripAnchorsFromGroups(Transform container)
    {
        TerrainAnchor[] anchors = container.GetComponentsInChildren<TerrainAnchor>(true);
        for (int i = 0; i < anchors.Length; i++)
        {
            TerrainAnchor anchor = anchors[i];
            if (anchor == null)
                continue;
            if (PrefabUtility.IsAnyPrefabInstanceRoot(anchor.gameObject))
                continue;
            Undo.DestroyObjectImmediate(anchor);
        }
    }
}
