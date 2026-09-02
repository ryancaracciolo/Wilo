using UnityEngine;

/// <summary>
/// The public camp: blast-off and weigh-in. Cabin dock stays home.
/// Counts the authored grounds, the dock, and boats waiting in the cove —
/// not only a hull pulled onto the sand.
/// </summary>
public class TournamentSite : MonoBehaviour
{
    [SerializeField] Transform dock;

    [Tooltip("Local-space box covering the beach and the water in front of it.")]
    [SerializeField] Vector3 areaCenter = new Vector3(8f, 0f, 28f);
    [SerializeField] Vector3 areaSize = new Vector3(88f, 10f, 72f);

    [Tooltip("How far off the dock a tied-up boat still counts.")]
    [SerializeField, Min(1f)] float dockPadding = 12f;

    [Tooltip("Boats waiting in the water this close to the dock still count.")]
    [SerializeField, Min(1f)] float coveRadius = 45f;

    [Tooltip("The camp clearing around this marker, even if the ground is a low bank.")]
    [SerializeField, Min(1f)] float clearingRadius = 12f;

    public Transform Dock => DockRoot;

    public Vector3 DockPosition => DockRoot != null ? DockRoot.position : transform.position;

    public bool Contains(Vector3 worldPosition)
    {
        return NearDock(worldPosition)
            || InClearing(worldPosition)
            || InCove(worldPosition)
            || InBeachArea(worldPosition);
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

    bool InCove(Vector3 worldPosition)
    {
        Vector3 dock = DockPosition;
        dock.y = 0f;
        worldPosition.y = 0f;
        return Vector3.Distance(dock, worldPosition) <= coveRadius;
    }

    bool InBeachArea(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition) - areaCenter;
        Vector3 half = areaSize * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z;
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

        Gizmos.color = new Color(0.35f, 0.85f, 1f, 0.25f);
        Gizmos.DrawWireSphere(new Vector3(root.position.x, bounds.center.y, root.position.z), coveRadius);
    }
}
