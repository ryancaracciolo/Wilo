using UnityEngine;

/// <summary>
/// The public camp: blast-off and weigh-in. Cabin dock stays home.
/// Counts the beach (sand and shallows) and the dock, including a boat
/// pulled onto the sand or tied alongside.
/// </summary>
public class TournamentSite : MonoBehaviour
{
    [SerializeField] Transform dock;

    [Tooltip("Local-space box covering the beach. Dock proximity is separate.")]
    [SerializeField] Vector3 areaCenter = new Vector3(8f, 0f, 28f);
    [SerializeField] Vector3 areaSize = new Vector3(88f, 10f, 72f);

    [Tooltip("How far off the dock a tied-up boat still counts.")]
    [SerializeField, Min(1f)] float dockPadding = 12f;

    [Tooltip("Water this deep still counts as beached shallows.")]
    [SerializeField, Min(0.05f)] float shallowDepth = 0.55f;

    [Tooltip("How high above water the sand/bank behind the beach may be.")]
    [SerializeField, Min(0.5f)] float beachHeight = 4.5f;

    [Tooltip("The camp clearing around this marker, even if the ground is a low bank.")]
    [SerializeField, Min(1f)] float clearingRadius = 12f;

    float cachedWaterY;
    bool hasWaterY;

    public bool Contains(Vector3 worldPosition)
    {
        if (NearDock(worldPosition) || InClearing(worldPosition))
            return true;

        if (!InBeachArea(worldPosition))
            return false;

        return IsBeachSurface(worldPosition);
    }

    bool InClearing(Vector3 worldPosition)
    {
        Vector3 a = transform.position;
        a.y = 0f;
        worldPosition.y = 0f;
        return Vector3.Distance(a, worldPosition) <= clearingRadius;
    }

    public bool NearDock(Vector3 worldPosition)
    {
        Transform root = DockRoot;
        if (root == null)
            return false;

        Bounds bounds = DockBounds(root);
        Vector3 probe = worldPosition;
        probe.y = bounds.center.y;
        Vector3 closest = bounds.ClosestPoint(probe);
        closest.y = 0f;
        worldPosition.y = 0f;
        return Vector3.Distance(closest, worldPosition) <= dockPadding;
    }

    Transform DockRoot
    {
        get
        {
            if (dock != null)
                return dock;
            Transform child = transform.Find("CampDock");
            if (child != null)
                dock = child;
            return dock;
        }
    }

    bool InBeachArea(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition) - areaCenter;
        Vector3 half = areaSize * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z;
    }

    bool IsBeachSurface(Vector3 worldPosition)
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null)
            return false;

        float waterY = WaterHeight();
        float groundY = terrain.SampleHeight(worldPosition) + terrain.transform.position.y;
        float rel = groundY - waterY;
        return rel >= -shallowDepth && rel <= beachHeight;
    }

    float WaterHeight()
    {
        if (hasWaterY)
            return cachedWaterY;

        var surface = GameObject.Find("Surface");
        if (surface == null)
            return 0f;

        var renderer = surface.GetComponent<Renderer>();
        cachedWaterY = renderer != null ? renderer.bounds.max.y : surface.transform.position.y;
        hasWaterY = true;
        return cachedWaterY;
    }

    static Bounds DockBounds(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(root.position, new Vector3(8f, 4f, 8f));

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.82f, 0.25f, 0.35f);
        Vector3 center = transform.TransformPoint(areaCenter);
        Vector3 size = Vector3.Scale(areaSize, transform.lossyScale);
        size.y = Mathf.Max(2f, areaSize.y);
        Gizmos.DrawWireCube(center, size);

        Transform root = DockRoot;
        if (root == null)
            return;

        Bounds bounds = DockBounds(root);
        Gizmos.color = new Color(0.4f, 0.75f, 1f, 0.9f);
        Gizmos.DrawWireCube(bounds.center, bounds.size + new Vector3(dockPadding * 2f, 0f, dockPadding * 2f));
    }
}
