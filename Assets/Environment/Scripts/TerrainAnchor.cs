using UnityEngine;

/// <summary>
/// Keeps a painted prop planted on the terrain after the heightmap changes.
/// The painters add this when scattering props; an editor hook resnaps
/// anchors in the sculpted region so they follow the lakebed.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Wilo/Terrain Anchor")]
public class TerrainAnchor : MonoBehaviour
{
    [SerializeField] float embedDistance;
    [SerializeField] float yaw;
    [SerializeField] float slopeAlign;
    [SerializeField] bool keepRotation;

    public void Configure(float embed, float yawDegrees, float align)
    {
        embedDistance = embed;
        yaw = yawDegrees;
        slopeAlign = align;
        keepRotation = false;
    }

    public void CaptureKeepingRotation(Terrain terrain)
    {
        if (!TrySample(terrain, transform.position, out float surfaceY, out _))
            return;

        embedDistance = surfaceY - transform.position.y;
        yaw = transform.eulerAngles.y;
        slopeAlign = 0f;
        keepRotation = true;
    }

    /// <summary>
    /// Seat this prop on the current terrain. Mesh-bottom mode lifts buried
    /// objects so the visible mesh sits slightly in the surface. Pivot mode
    /// only adjusts Y (for leaning fallen trees).
    /// </summary>
    public void PlantOnSurface(Terrain terrain, bool useMeshBottom)
    {
        Vector3 world = transform.position;
        if (!TrySample(terrain, world, out float surfaceY, out _))
            return;

        float bottomY = world.y;
        float embed = 0.08f;
        if (useMeshBottom)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            bool hasBounds = false;
            Bounds bounds = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                    bounds.Encapsulate(renderer.bounds);
            }

            if (hasBounds)
            {
                bottomY = bounds.min.y;
                embed = Mathf.Clamp(bounds.size.y * 0.08f, 0.02f, 0.8f);
            }
        }

        float lift = surfaceY - bottomY - embed;
        if (Mathf.Abs(lift) >= 0.0001f)
        {
            world.y += lift;
            transform.position = world;
        }

        CaptureKeepingRotation(terrain);
    }

    /// <summary>
    /// Push this prop down if it is hovering. Never raises objects that are
    /// already in the ground. Uses mesh geometry rather than renderer AABBs,
    /// which can be oversized and leave the visible mesh floating.
    /// </summary>
    public float SinkIfFloating(Terrain terrain, bool useMeshBottom)
    {
        Vector3 world = transform.position;
        if (!TrySample(terrain, world, out float surfaceY, out _))
            return 0f;

        float bottomY = world.y;
        float height = Mathf.Max(0.25f, transform.lossyScale.y);
        if (useMeshBottom && TryGetGeometryVerticalRange(out float meshMin, out float meshMax))
        {
            bottomY = meshMin;
            height = Mathf.Max(0.25f, meshMax - meshMin);
        }

        float embed = Mathf.Clamp(height * 0.18f, 0.06f, 1.2f);
        float targetBottom = surfaceY - embed;
        float sink = bottomY - targetBottom;
        if (sink <= 0.01f)
            return 0f;

        world.y -= sink;
        transform.position = world;
        CaptureKeepingRotation(terrain);
        return sink;
    }

    bool TryGetGeometryVerticalRange(out float minY, out float maxY)
    {
        minY = float.MaxValue;
        maxY = float.MinValue;
        bool any = false;

        MeshFilter[] filters = GetComponentsInChildren<MeshFilter>();
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter == null || filter.sharedMesh == null)
                continue;

            Bounds local = filter.sharedMesh.bounds;
            Transform meshTransform = filter.transform;
            Vector3 c = local.center;
            Vector3 e = local.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 world = meshTransform.TransformPoint(c + new Vector3(e.x * x, e.y * y, e.z * z));
                if (world.y < minY) minY = world.y;
                if (world.y > maxY) maxY = world.y;
                any = true;
            }
        }

        return any;
    }

    public void Snap(Terrain terrain)
    {
        Vector3 world = transform.position;
        if (!TrySample(terrain, world, out float surfaceY, out Vector3 normal))
            return;

        if (keepRotation)
        {
            float y = surfaceY - embedDistance;
            Vector3 pos = transform.position;
            if (Mathf.Abs(pos.y - y) < 0.0001f)
                return;
            pos.y = y;
            transform.position = pos;
            return;
        }

        Vector3 point = new Vector3(world.x, surfaceY, world.z);
        Vector3 up = Vector3.Slerp(Vector3.up, normal, slopeAlign).normalized;
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.up;

        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, yaw, 0f);
        Vector3 position = point - up * embedDistance;
        if ((transform.position - position).sqrMagnitude < 1e-8f &&
            Quaternion.Angle(transform.rotation, rotation) < 0.05f)
            return;

        transform.SetPositionAndRotation(position, rotation);
    }

    public static TerrainAnchor[] FindAll()
    {
        return Object.FindObjectsByType<TerrainAnchor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
    }

    public static void SnapInWorldBounds(Terrain terrain, float minX, float maxX, float minZ, float maxZ)
    {
        if (terrain == null)
            return;

        TerrainAnchor[] anchors = FindAll();
        for (int i = 0; i < anchors.Length; i++)
        {
            TerrainAnchor anchor = anchors[i];
            if (anchor == null)
                continue;

            Vector3 p = anchor.transform.position;
            if (p.x < minX || p.x > maxX || p.z < minZ || p.z > maxZ)
                continue;

            anchor.Snap(terrain);
        }
    }

    public static void SnapAll()
    {
        TerrainAnchor[] anchors = FindAll();
        for (int i = 0; i < anchors.Length; i++)
        {
            TerrainAnchor anchor = anchors[i];
            if (anchor == null)
                continue;

            Terrain terrain = FindTerrain(anchor.transform.position);
            if (terrain != null)
                anchor.Snap(terrain);
        }
    }

    public static Terrain FindTerrain(Vector3 worldPosition)
    {
        Terrain[] terrains = Terrain.activeTerrains;
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain t = terrains[i];
            if (Contains(t, worldPosition))
                return t;
        }

        return Terrain.activeTerrain;
    }

    static bool TrySample(Terrain terrain, Vector3 world, out float surfaceY, out Vector3 normal)
    {
        surfaceY = world.y;
        normal = Vector3.up;
        if (terrain == null || terrain.terrainData == null)
            return false;

        Vector3 origin = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        if (size.x < 0.0001f || size.z < 0.0001f)
            return false;

        float nx = (world.x - origin.x) / size.x;
        float nz = (world.z - origin.z) / size.z;
        if (nx < 0f || nx > 1f || nz < 0f || nz > 1f)
            return false;

        surfaceY = origin.y + terrain.terrainData.GetInterpolatedHeight(nx, nz);
        normal = terrain.terrainData.GetInterpolatedNormal(nx, nz);
        return true;
    }

    static bool Contains(Terrain terrain, Vector3 world)
    {
        if (terrain == null || terrain.terrainData == null)
            return false;

        Vector3 local = world - terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        return local.x >= 0f && local.x <= size.x && local.z >= 0f && local.z <= size.z;
    }
}
