using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Caps play-mode cost so the lake does not spin the GPU unbounded.
/// Grass and lily pads stay visible; they just stop casting shadows and
/// share instanced draws. Tree fade is pulled in from the whole-map range.
/// </summary>
public static class PlayPerformance
{
    const int TargetFrameRate = 60;
    const float TreeDistance = 520f;
    const float TreeBillboardStart = 120f;

    static readonly string[] SoftFoliageRoots = { "Grass", "LilyPads" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Apply()
    {
        Application.targetFrameRate = TargetFrameRate;
        QualitySettings.vSyncCount = 0;

        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            terrain.treeDistance = Mathf.Min(terrain.treeDistance, TreeDistance);
            terrain.treeBillboardDistance = Mathf.Min(terrain.treeBillboardDistance, TreeBillboardStart);
        }

        for (int i = 0; i < SoftFoliageRoots.Length; i++)
            SoftenFoliage(GameObject.Find(SoftFoliageRoots[i]));
    }

    static void SoftenFoliage(GameObject root)
    {
        if (root == null)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            Material[] materials = renderer.sharedMaterials;
            for (int m = 0; m < materials.Length; m++)
            {
                if (materials[m] != null)
                    materials[m].enableInstancing = true;
            }
        }
    }
}
