using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene owner for the lake-wide fishing model. LocalFishPopulation
/// asks this for habitat; this class does not spawn fish itself.
/// </summary>
public class LakeSimulation : MonoBehaviour
{
    [SerializeField] WorldConditions conditions;
    [SerializeField] FishSpecies[] species = Array.Empty<FishSpecies>();
    [SerializeField] HabitatProfile profile;

    [Header("Fallback if no profile")]
    [SerializeField] float fishPerThousandSqMeters = 4f;
    [SerializeField, Range(0f, 1f)] float activity = 0.5f;
    [SerializeField] float meanPounds = 2.5f;
    [SerializeField] float poundsSpread = 1.5f;
    [SerializeField] float landDepthMeters = 0.05f;

    LakeHabitat habitat;
    LurePresence lure;
    readonly LakeCoverIndex cover = new LakeCoverIndex();

    public LakeHabitat Habitat
    {
        get
        {
            if (habitat == null)
                Rebuild();
            return habitat;
        }
    }

    public IReadOnlyList<FishSpecies> Species => species;
    public WorldConditions Conditions => conditions;
    public LurePresence Lure => lure;
    public HabitatProfile Profile => profile;

    public float SurfaceY => conditions != null ? conditions.WaterHeight : 0f;

    /// <summary>Bare lake-bed depth. Habitat and depth readouts stay on this:
    /// a hole with a boulder in it is still a hole.</summary>
    public float GeometricDepthMeters(Vector3 world)
    {
        return conditions != null ? conditions.BedDepthMeters(world) : 0f;
    }

    /// <summary>
    /// Depth to the shallowest ground a fish or lure can rest on. Rock and
    /// timber count, so neither swims through a boulder.
    /// </summary>
    public float GroundDepthMeters(Vector3 world)
    {
        return conditions != null ? conditions.GeometricDepthMeters(world) : 0f;
    }

    public float LureBottomMeters(Vector3 world)
    {
        return GroundDepthMeters(world);
    }

    public float DepthMeters(Vector3 world)
    {
        if (conditions == null)
            return 0f;
        return conditions.ToGameplayDepth(conditions.BedDepthMeters(world));
    }

    void Awake()
    {
        if (conditions == null)
            conditions = FindFirstObjectByType<WorldConditions>();
        lure = GetComponent<LurePresence>() ?? gameObject.AddComponent<LurePresence>();
        Rebuild();
    }

    void Start()
    {
        if (conditions != null)
        {
            float _ = conditions.WaterHeight;
            conditions.GeometricDepthMeters(transform.position);
        }

        BuildCover();
    }

    void OnValidate()
    {
        Rebuild();
    }

    public LakeConditions SnapshotConditions()
    {
        if (conditions == null)
            return default;

        return new LakeConditions(
            conditions.Hour,
            conditions.Season,
            conditions.Phase,
            conditions.WaterTempF,
            conditions.AirTempF,
            conditions.WindFromDegrees,
            conditions.WindMph,
            conditions.Weather,
            conditions.DawnHour,
            conditions.DuskHour);
    }

    /// <summary>Lake-wide willingness to bite. Cheap; does not query cover.</summary>
    public float CurrentActivity
    {
        get
        {
            if (profile == null)
                return Mathf.Clamp01(activity);
            if (conditions == null)
                return profile.activity;
            return profile.EvaluateActivity(SnapshotConditions());
        }
    }

    public HabitatSample SampleAt(Vector3 world)
    {
        HabitatFeatures features = SampleFeatures(world);
        float depth = GeometricDepthMeters(world);
        if (conditions == null)
            return Habitat.Sample(depth, features, species);
        return Habitat.Sample(depth, features, species, SnapshotConditions());
    }

    public HabitatFeatures SampleFeatures(Vector3 world)
    {
        float geometric = GeometricDepthMeters(world);
        float feet = ToFeet(geometric);
        float dropoff = MeasureDropoff(world, geometric, feet);
        float rock = 0f;
        float wood = 0f;
        float veg = 0f;
        float extraRock = profile != null ? profile.rockReachMeters : 2.2f;
        float extraWood = profile != null ? Mathf.Clamp(profile.woodHugMeters * 0.7f, 0.8f, 2.5f) : 1.2f;
        float extraVeg = profile != null ? profile.coverRadiusMeters : 9f;
        cover.Evaluate(world.x, world.z, extraRock, extraWood, extraVeg, out rock, out wood, out veg);
        float vegQuality = profile != null ? profile.SaturateVegetation(veg) : Mathf.Clamp01(veg);
        float convexity = MeasureConvexity(world, geometric, feet);
        return new HabitatFeatures(feet, dropoff, rock, wood, vegQuality, convexity);
    }

    /// <summary>Nearest cover standing inside the square around <paramref name="center"/>.</summary>
    public bool TryCoverInCell(Vector3 center, float halfSize, CoverKind kind, out Vector3 at)
    {
        return TryCoverInCell(center, halfSize, kind, out at, out _);
    }

    public bool TryCoverInCell(
        Vector3 center,
        float halfSize,
        CoverKind kind,
        out Vector3 at,
        out float radius)
    {
        at = center;
        radius = 0f;
        float px;
        float pz;
        if (!cover.TryClosestInRect(
                center.x,
                center.z,
                center.x - halfSize,
                center.z - halfSize,
                center.x + halfSize,
                center.z + halfSize,
                kind,
                out px,
                out pz,
                out radius))
            return false;

        at = new Vector3(px, center.y, pz);
        return true;
    }

    public FishSpecies PickSpecies(Vector3 world, float u01)
    {
        return PickSpecies(SampleFeatures(world), u01);
    }

    public FishSpecies PickSpecies(in HabitatFeatures features, float u01)
    {
        return Habitat.Pick(species, features, u01);
    }

    void Rebuild()
    {
        habitat = new LakeHabitat(
            profile,
            new HabitatSample(fishPerThousandSqMeters, activity, meanPounds, poundsSpread),
            landDepthMeters);
    }

    float MeasureDropoff(Vector3 world, float centerGeometric, float centerFeet)
    {
        float span = profile != null ? profile.dropoffSampleMeters : 8f;
        float strong = profile != null ? profile.dropoffStrongFeet : 5f;
        if (span < 0.5f || strong < 0.2f)
            return 0f;
        if (centerGeometric <= landDepthMeters)
            return 0f;

        float maxDelta = 0f;
        maxDelta = Mathf.Max(maxDelta, WetDepthDelta(world + Vector3.right * span, centerFeet));
        maxDelta = Mathf.Max(maxDelta, WetDepthDelta(world - Vector3.right * span, centerFeet));
        maxDelta = Mathf.Max(maxDelta, WetDepthDelta(world + Vector3.forward * span, centerFeet));
        maxDelta = Mathf.Max(maxDelta, WetDepthDelta(world - Vector3.forward * span, centerFeet));
        return Mathf.Clamp01(maxDelta / strong);
    }

    /// <summary>
    /// Compares this spot to a ring around it. Water that is deeper on most
    /// sides means a point or shoal; shallower on most sides means a hole. A
    /// straight wall cancels out, which is the distinction Dropoff cannot make.
    /// </summary>
    float MeasureConvexity(Vector3 world, float centerGeometric, float centerFeet)
    {
        float span = profile != null ? profile.pointSampleMeters : 18f;
        float strong = profile != null ? profile.pointStrongFeet : 10f;
        if (span < 1f || strong < 0.5f)
            return 0f;
        if (centerGeometric <= landDepthMeters)
            return 0f;

        float sum = 0f;
        int n = 0;
        AccumulateRing(world + Vector3.right * span, ref sum, ref n);
        AccumulateRing(world - Vector3.right * span, ref sum, ref n);
        AccumulateRing(world + Vector3.forward * span, ref sum, ref n);
        AccumulateRing(world - Vector3.forward * span, ref sum, ref n);
        if (n < 2)
            return 0f;

        return Mathf.Clamp((sum / n - centerFeet) / strong, -1f, 1f);
    }

    void AccumulateRing(Vector3 world, ref float sum, ref int n)
    {
        float geometric = GeometricDepthMeters(world);
        if (geometric <= landDepthMeters)
            return;

        sum += ToFeet(geometric);
        n++;
    }

    float WetDepthDelta(Vector3 world, float centerFeet)
    {
        float geometric = GeometricDepthMeters(world);
        if (geometric <= landDepthMeters)
            return 0f;

        float feet = ToFeet(geometric);
        if (feet < 3f)
            return 0f;
        return Mathf.Abs(feet - centerFeet);
    }

    float ToFeet(float geometricMeters)
    {
        float gameplay = conditions != null
            ? conditions.ToGameplayDepth(geometricMeters)
            : geometricMeters;
        return gameplay * 3.28084f;
    }

    void BuildCover()
    {
        cover.Clear();
        var scene = gameObject.scene;
        if (!scene.IsValid())
            return;

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            CollectCover(roots[i].transform, 0);

        cover.Bake(profile != null ? profile.coverRadiusMeters : 9f);
    }

    void CollectCover(Transform t, int depth)
    {
        if (t == null || !t.gameObject.activeInHierarchy)
            return;
        if (t.name == "Grass" || t.name == "Terrain")
            return;

        CoverKind kind;
        if (TryCoverKind(t.name, out kind))
        {
            AddCoverChildren(t, kind);
            return;
        }

        if (IsWoodName(t.name))
        {
            AddCoverPoint(t, CoverKind.Wood);
            return;
        }

        if (depth >= 4)
            return;

        for (int i = 0; i < t.childCount; i++)
            CollectCover(t.GetChild(i), depth + 1);
    }

    static bool TryCoverKind(string name, out CoverKind kind)
    {
        if (name == "Rocks" || name.StartsWith("Rocks_"))
        {
            kind = CoverKind.Rock;
            return true;
        }

        if (name == "FallenTrees" || name == "Stumps" || name == "Logs" ||
            name.StartsWith("FallenTrees_") || name.StartsWith("Stumps_") ||
            name.StartsWith("Logs_"))
        {
            kind = CoverKind.Wood;
            return true;
        }

        if (name == "WeedBeds" || name == "LilyPads" ||
            name.StartsWith("LilyPads_") || name.StartsWith("WeedBeds_"))
        {
            kind = CoverKind.Vegetation;
            return true;
        }

        kind = CoverKind.Rock;
        return false;
    }

    static bool IsWoodName(string name)
    {
        return name.StartsWith("P_HS_LP_Log") ||
            name.StartsWith("P_HS_LP_Stump") ||
            name.StartsWith("P_HS_LP_Branch") ||
            name.StartsWith("P_HS_LP_Tree_Fallen") ||
            name.StartsWith("P_HS_LP_Tree_Dead") ||
            name.StartsWith("P_HS_LP_Tree_Burnt") ||
            name.IndexOf("Fallen", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    void AddCoverChildren(Transform root, CoverKind kind)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null || !child.gameObject.activeInHierarchy)
                continue;

            CoverKind nested;
            if (TryCoverKind(child.name, out nested))
                AddCoverChildren(child, nested);
            else
                AddCoverPoint(child, kind);
        }
    }

    void AddCoverPoint(Transform child, CoverKind kind)
    {
        Vector3 p = child.position;
        float radius = 1.4f;
        var renderer = child.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            Bounds bounds = renderer.bounds;
            p = bounds.center;
            Vector3 ext = bounds.extents;
            if (kind == CoverKind.Wood)
            {
                AddWoodPoints(bounds);
                return;
            }

            if (kind == CoverKind.Vegetation)
            {
                AddVegetationPoints(bounds);
                return;
            }

            radius = Mathf.Clamp(Mathf.Max(ext.x, ext.z), 0.8f, 12f);
        }

        cover.Add(p.x, p.z, radius, kind);
    }

    void AddVegetationPoints(Bounds bounds)
    {
        Vector3 c = bounds.center;
        float hx = Mathf.Max(0.4f, bounds.extents.x);
        float hz = Mathf.Max(0.4f, bounds.extents.z);
        float radius = Mathf.Clamp(Mathf.Min(hx, hz), 0.6f, 3.2f);
        int nx = Mathf.Clamp(Mathf.RoundToInt(hx / 3.5f) + 1, 1, 4);
        int nz = Mathf.Clamp(Mathf.RoundToInt(hz / 3.5f) + 1, 1, 4);
        for (int x = 0; x < nx; x++)
        {
            float tx = nx == 1 ? 0f : (x / (float)(nx - 1) - 0.5f) * 1.6f;
            for (int z = 0; z < nz; z++)
            {
                float tz = nz == 1 ? 0f : (z / (float)(nz - 1) - 0.5f) * 1.6f;
                cover.Add(c.x + tx * hx, c.z + tz * hz, radius, CoverKind.Vegetation);
            }
        }
    }

    void AddWoodPoints(Bounds bounds)
    {
        Vector3 c = bounds.center;
        float hx = Mathf.Max(0.4f, bounds.extents.x);
        float hz = Mathf.Max(0.4f, bounds.extents.z);
        float hug = profile != null ? profile.woodHugMeters : 2.2f;
        float radius = Mathf.Clamp(Mathf.Min(hx, hz) * 0.2f + hug * 0.55f, 1f, 4f);
        bool longX = hx >= hz;
        float half = longX ? hx : hz;
        int n = Mathf.Clamp(Mathf.RoundToInt(half / 1.8f) + 1, 1, 8);
        for (int i = 0; i < n; i++)
        {
            float t = n == 1 ? 0f : (i / (float)(n - 1) - 0.5f) * 1.85f;
            float x = c.x + (longX ? t * hx : 0f);
            float z = c.z + (longX ? 0f : t * hz);
            cover.Add(x, z, radius, CoverKind.Wood);
        }
    }
}
