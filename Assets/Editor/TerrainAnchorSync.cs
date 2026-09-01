using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// When the terrain heightmap is sculpted, resnap <see cref="TerrainAnchor"/>
/// props that sit in the dirty region. Snap positions are not recorded in Undo
/// (they would become a second undo step after the sculpt). Instead, undo/redo
/// of the heightmap triggers another snap so props follow Ctrl+Z as well.
///
/// If the scene is reverted and anchors vanish, missing ones are restored on
/// load (and before the first sculpt) so follow-terrain keeps working.
/// </summary>
[InitializeOnLoad]
static class TerrainAnchorSync
{
    const string FollowPref = "Wilo.RockPainter.FollowTerrain";

    static Terrain pendingTerrain;
    static RectInt pendingTexels;
    static bool queued;
    static bool undoQueued;
    static bool triedEnsure;

    static TerrainAnchorSync()
    {
        TerrainCallbacks.heightmapChanged += OnHeightmapChanged;
        Undo.undoRedoPerformed += OnUndoRedo;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.delayCall += EnsureIfNeeded;
    }

    public static bool FollowTerrain
    {
        get => EditorPrefs.GetBool(FollowPref, true);
        set => EditorPrefs.SetBool(FollowPref, value);
    }

    static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        triedEnsure = false;
        EditorApplication.delayCall += EnsureIfNeeded;
    }

    static void EnsureIfNeeded()
    {
        if (!FollowTerrain || triedEnsure)
            return;
        if (TerrainAnchor.FindAll().Length > 0)
        {
            triedEnsure = true;
            return;
        }

        int added = TerrainAnchorEditorUtil.EnsureMissingAnchors();
        triedEnsure = added > 0 || GameObject.Find("Rocks") != null || GameObject.Find("Grass") != null;
        if (added > 0)
            Debug.Log($"Terrain Anchor: restored {added} missing anchors so lake props follow sculpting.");
    }

    static void OnHeightmapChanged(Terrain terrain, RectInt texels, bool synched)
    {
        if (!FollowTerrain || terrain == null)
            return;

        EnsureIfNeeded();

        if (queued && pendingTerrain == terrain)
            pendingTexels = Encapsulate(pendingTexels, texels);
        else
        {
            pendingTerrain = terrain;
            pendingTexels = texels;
        }

        if (queued)
            return;

        queued = true;
        EditorApplication.delayCall += Flush;
    }

    static void OnUndoRedo()
    {
        if (!FollowTerrain || undoQueued)
            return;

        undoQueued = true;
        EditorApplication.delayCall += FlushAfterUndo;
    }

    static void Flush()
    {
        queued = false;
        Terrain terrain = pendingTerrain;
        RectInt texels = pendingTexels;
        pendingTerrain = null;
        if (terrain == null || terrain.terrainData == null)
            return;

        TerrainData data = terrain.terrainData;
        int res = data.heightmapResolution;
        texels.x = Mathf.Max(0, texels.x - 2);
        texels.y = Mathf.Max(0, texels.y - 2);
        texels.width = Mathf.Min(res - texels.x, texels.width + 4);
        texels.height = Mathf.Min(res - texels.y, texels.height + 4);

        Vector3 origin = terrain.transform.position;
        Vector3 size = data.size;
        float sx = size.x / (res - 1);
        float sz = size.z / (res - 1);
        float pad = Mathf.Max(8f, Mathf.Max(sx, sz) * 2f);

        float minX = origin.x + texels.x * sx - pad;
        float maxX = origin.x + (texels.x + texels.width) * sx + pad;
        float minZ = origin.z + texels.y * sz - pad;
        float maxZ = origin.z + (texels.y + texels.height) * sz + pad;

        TerrainAnchor.SnapInWorldBounds(terrain, minX, maxX, minZ, maxZ);
    }

    static void FlushAfterUndo()
    {
        undoQueued = false;
        TerrainAnchor.SnapAll();
    }

    static RectInt Encapsulate(RectInt a, RectInt b)
    {
        int xMin = Mathf.Min(a.xMin, b.xMin);
        int yMin = Mathf.Min(a.yMin, b.yMin);
        int xMax = Mathf.Max(a.xMax, b.xMax);
        int yMax = Mathf.Max(a.yMax, b.yMax);
        return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
    }
}
