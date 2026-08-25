using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Activates habitat cells around the player/boat and returns fish to a
/// pool once those cells leave a despawn buffer. Fast travel just swaps
/// the active set; skipped cells never spawn.
/// Spawn rolls use a session seed so a new play draws a fresh sample
/// from habitat; the same cell keeps that sample if you leave and return.
/// </summary>
[RequireComponent(typeof(LakeSimulation))]
public class LocalFishPopulation : MonoBehaviour
{
    [SerializeField] LakeSimulation lake;
    [SerializeField] float cellSize = 24f;
    [SerializeField, Tooltip("About two full casts.")]
    float activeRadius = 72f;
    [SerializeField, Tooltip("Must be larger than active radius so edge cells do not flicker.")]
    float despawnRadius = 96f;
    [SerializeField] int maxFish = 22;
    [SerializeField] int maxFishPerCell = 3;
    [SerializeField] int maxCellActivationsPerUpdate = 6;
    [SerializeField] float updateInterval = 0.15f;
    [SerializeField] int prewarmPerSpecies = 8;
    [SerializeField] float keepClearOfViewer = 3f;
    [SerializeField, Tooltip("1 = real inches. Higher reads from the boat. 2.3 is about 2× the first pass.")]
    float visualScaleMultiplier = 2.3f;
    [SerializeField, Range(0.25f, 1.5f), Tooltip("1 = fade in gameplay meters (visual depth × scale). Lower hides fish sooner than the bed.")]
    float visibilityScale = 1f;
    [SerializeField, Tooltip("Planar fade so distant bass do not read as a grid.")]
    float viewDistance = 48f;

    readonly Dictionary<LakeCellId, List<FishAgent>> occupied = new Dictionary<LakeCellId, List<FishAgent>>();
    readonly HashSet<LakeCellId> empty = new HashSet<LakeCellId>();
    readonly Dictionary<LakeCellId, int> harvested = new Dictionary<LakeCellId, int>();
    readonly Dictionary<FishSpecies, Stack<FishAgent>> pools = new Dictionary<FishSpecies, Stack<FishAgent>>();
    readonly List<LakeCellId> scratchCells = new List<LakeCellId>();
    readonly List<(LakeCellId id, float dist, float density)> candidates = new List<(LakeCellId, float, float)>();

    Transform poolRoot;
    Vector3 lastOrigin;
    float nextUpdateTime;
    int liveCount;
    int sessionSeed;

    void Awake()
    {
        if (lake == null)
            lake = GetComponent<LakeSimulation>();
        poolRoot = new GameObject("FishPool").transform;
        poolRoot.SetParent(transform, false);
        sessionSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        if (sessionSeed == int.MinValue)
            sessionSeed = 1;
    }

    void Start()
    {
        Prewarm();
        nextUpdateTime = 0f;
    }

    void OnDisable()
    {
        DespawnAll();
    }

    void LateUpdate()
    {
        PushVisibility();
        if (lake == null || lake.Conditions == null)
            return;

        Vector3 origin = lake.Conditions.QueryOrigin;
        float moved = Vector3.Distance(
            new Vector3(origin.x, 0f, origin.z),
            new Vector3(lastOrigin.x, 0f, lastOrigin.z));
        if (Time.time < nextUpdateTime && moved < cellSize * 0.35f)
            return;

        nextUpdateTime = Time.time + updateInterval;
        Refresh(origin, moved);
        lastOrigin = origin;
    }

    void Refresh(Vector3 origin, float moved)
    {
        DespawnFarCells(origin);

        int x0 = Mathf.FloorToInt((origin.x - activeRadius) / cellSize);
        int x1 = Mathf.FloorToInt((origin.x + activeRadius) / cellSize);
        int z0 = Mathf.FloorToInt((origin.z - activeRadius) / cellSize);
        int z1 = Mathf.FloorToInt((origin.z + activeRadius) / cellSize);

        candidates.Clear();
        for (int x = x0; x <= x1; x++)
        {
            for (int z = z0; z <= z1; z++)
            {
                var id = new LakeCellId(x, z);
                if (occupied.ContainsKey(id) || empty.Contains(id))
                    continue;

                float dist = DistanceToCell(origin, id);
                if (dist > activeRadius)
                    continue;

                candidates.Add((id, dist, DensestInCell(id, origin.y)));
            }
        }

        candidates.Sort(CompareHabitat);
        int spawnBudget = maxCellActivationsPerUpdate;
        if (moved > cellSize)
            spawnBudget *= 3;
        int examineBudget = Mathf.Max(spawnBudget * 4, 16);

        for (int i = 0; i < candidates.Count; i++)
        {
            if (liveCount >= maxFish || examineBudget <= 0)
                break;
            examineBudget--;
            if (!TryActivate(candidates[i].id, origin))
                continue;
            spawnBudget--;
            if (spawnBudget <= 0)
                break;
        }
    }

    void DespawnFarCells(Vector3 origin)
    {
        scratchCells.Clear();
        foreach (var pair in occupied)
        {
            if (DistanceToCell(origin, pair.Key) > despawnRadius)
                scratchCells.Add(pair.Key);
        }

        for (int i = 0; i < scratchCells.Count; i++)
            ReleaseCell(scratchCells[i]);

        scratchCells.Clear();
        foreach (var id in empty)
        {
            if (DistanceToCell(origin, id) > despawnRadius)
                scratchCells.Add(id);
        }

        for (int i = 0; i < scratchCells.Count; i++)
            empty.Remove(scratchCells[i]);
    }

    bool TryActivate(LakeCellId id, Vector3 origin)
    {
        IReadOnlyList<FishSpecies> species = lake.Species;
        if (species == null || species.Count == 0)
        {
            empty.Add(id);
            return false;
        }

        float area = cellSize * cellSize;
        float mean;
        float peak;
        SampleCellDensity(id, origin.y, out mean, out peak);
        float expected = (mean * 0.55f + peak * 0.45f) * (area / 1000f);
        int target = Mathf.Clamp(StochasticRound(expected, Hash01(Hash(id, 3))), 0, maxFishPerCell);
        if (target <= 0)
        {
            empty.Add(id);
            return false;
        }

        int attempts = Mathf.Max(target * 8, 12);
        var spawned = new List<FishAgent>(maxFishPerCell);
        float minAccept = Mathf.Max(0.04f, peak * 0.38f);
        for (int i = 0; i < attempts && spawned.Count < target && liveCount + spawned.Count < maxFish; i++)
        {
            Vector3 point = BestPointInCell(id, origin, Hash(id, i * 13));
            if (DistanceXZ(point, origin) < keepClearOfViewer)
                continue;
            HabitatFeatures features = lake.SampleFeatures(point);
            HabitatSample local = lake.Habitat.Sample(
                lake.GeometricDepthMeters(point),
                features,
                species);
            if (!local.HasFish || local.FishPerThousandSqMeters < minAccept)
                continue;

            FishSpecies chosen = lake.PickSpecies(features, Hash01(Hash(id, 101 + i)));
            if (chosen == null || chosen.Prefab == null)
                continue;

            FishSize size = FishSize.Roll(
                local,
                chosen,
                Hash01(Hash(id, 50 + i)),
                Hash01(Hash(id, 61 + i)),
                lake.Profile,
                features);
            float columnT = FishAgent.BottomWeightedColumn(Hash01(Hash(id, 71 + i)));
            float depth = lake.GeometricDepthMeters(point);
            Vector3 spawnAt = new Vector3(
                point.x,
                lake.SurfaceY - FishAgent.DepthBelowSurface(depth, columnT),
                point.z);
            FishAgent agent = Take(chosen);
            if (agent == null)
                continue;

            float yaw = (Hash(id, 17 + i) & 1023) / 1023f * 360f;
            float speed = 0.4f + (Hash(id, 31 + i) & 255) / 255f * 0.45f;
            float visual = chosen.VisualScale(size, visualScaleMultiplier);
            float hug = lake.Profile != null ? lake.Profile.woodHugMeters : 2.2f;
            float wander;
            if (features.Wood > 0.35f)
                wander = Mathf.Clamp(hug, 1f, 4f);
            else if (features.Vegetation > 0.4f || features.Rock > 0.35f)
                wander = 4f;
            else
                wander = cellSize * 0.28f;
            agent.Activate(
                lake,
                chosen,
                size,
                spawnAt,
                wander,
                speed,
                yaw,
                columnT,
                visual);
            spawned.Add(agent);
        }

        if (spawned.Count == 0)
        {
            empty.Add(id);
            return false;
        }

        occupied[id] = spawned;
        liveCount += spawned.Count;
        return true;
    }

    public void Detach(FishAgent agent)
    {
        if (agent == null)
            return;

        LakeCellId? emptied = null;
        foreach (var pair in occupied)
        {
            if (!pair.Value.Remove(agent))
                continue;

            liveCount--;
            harvested.TryGetValue(pair.Key, out int taken);
            harvested[pair.Key] = taken + 1;
            if (pair.Value.Count == 0)
                emptied = pair.Key;
            break;
        }

        if (!emptied.HasValue)
            return;

        occupied.Remove(emptied.Value);
        empty.Add(emptied.Value);
    }

    public void Remove(FishAgent agent)
    {
        Detach(agent);
        Release(agent);
    }

    void ReleaseCell(LakeCellId id)
    {
        if (!occupied.TryGetValue(id, out List<FishAgent> fish))
            return;

        for (int i = 0; i < fish.Count; i++)
            Release(fish[i]);
        liveCount -= fish.Count;
        occupied.Remove(id);
    }

    void DespawnAll()
    {
        scratchCells.Clear();
        foreach (var key in occupied.Keys)
            scratchCells.Add(key);
        for (int i = 0; i < scratchCells.Count; i++)
            ReleaseCell(scratchCells[i]);
        empty.Clear();
        liveCount = 0;
    }

    void Prewarm()
    {
        IReadOnlyList<FishSpecies> species = lake.Species;
        if (species == null)
            return;

        for (int s = 0; s < species.Count; s++)
        {
            FishSpecies kind = species[s];
            if (kind == null || kind.Prefab == null)
                continue;
            for (int i = 0; i < prewarmPerSpecies; i++)
                Release(Create(kind));
        }
    }

    FishAgent Take(FishSpecies species)
    {
        FishAgent agent;
        if (pools.TryGetValue(species, out Stack<FishAgent> stack) && stack.Count > 0)
            agent = stack.Pop();
        else
            agent = Create(species);

        agent.gameObject.SetActive(true);
        return agent;
    }

    FishAgent Create(FishSpecies species)
    {
        GameObject go = Instantiate(species.Prefab, poolRoot);
        go.name = species.DisplayName;
        var agent = go.GetComponent<FishAgent>();
        if (agent == null)
            agent = go.AddComponent<FishAgent>();
        agent.Bind(species);
        go.SetActive(false);
        return agent;
    }

    void Release(FishAgent agent)
    {
        if (agent == null)
            return;

        FishSpecies species = agent.Species;
        agent.Sleep();
        agent.gameObject.SetActive(false);
        agent.transform.SetParent(poolRoot, false);

        if (species == null)
        {
            Destroy(agent.gameObject);
            return;
        }

        if (!pools.TryGetValue(species, out Stack<FishAgent> stack))
        {
            stack = new Stack<FishAgent>();
            pools[species] = stack;
        }

        stack.Push(agent);
    }

    float DistanceToCell(Vector3 origin, LakeCellId id)
    {
        float xMin = id.X * cellSize;
        float zMin = id.Z * cellSize;
        float x = Mathf.Clamp(origin.x, xMin, xMin + cellSize);
        float z = Mathf.Clamp(origin.z, zMin, zMin + cellSize);
        return DistanceXZ(origin, new Vector3(x, 0f, z));
    }

    Vector3 CellCenter(LakeCellId id, float y)
    {
        return new Vector3((id.X + 0.5f) * cellSize, y, (id.Z + 0.5f) * cellSize);
    }

    Vector3 PointInCell(LakeCellId id, float y, int hash)
    {
        float u = (hash & 0xffff) / 65535f;
        float v = ((hash >> 16) & 0xffff) / 65535f;
        return new Vector3(
            (id.X + Mathf.Lerp(0.08f, 0.92f, u)) * cellSize,
            y,
            (id.Z + Mathf.Lerp(0.08f, 0.92f, v)) * cellSize);
    }

    Vector3 BestPointInCell(LakeCellId id, Vector3 origin, int salt)
    {
        Vector3 center = CellCenter(id, origin.y);
        Vector3 best = PointInCell(id, origin.y, salt);
        float bestDensity = -1f;

        // Let wood and rock compete on habitat value. Preferring wood outright
        // parks smallmouth on a stray log instead of the rock they want.
        ConsiderCover(id, origin, salt, center, CoverKind.Wood, ref best, ref bestDensity);
        ConsiderCover(id, origin, salt, center, CoverKind.Rock, ref best, ref bestDensity);
        if (bestDensity > 0f)
            return best;

        for (int k = 0; k < 8; k++)
        {
            Vector3 point = PointInCell(id, origin.y, Hash(id, salt + k * 19));
            if (DistanceXZ(point, origin) < keepClearOfViewer)
                continue;

            HabitatSample local = lake.SampleAt(point);
            if (!local.HasFish || local.FishPerThousandSqMeters <= bestDensity)
                continue;

            bestDensity = local.FishPerThousandSqMeters;
            best = point;
        }

        return best;
    }

    void ConsiderCover(
        LakeCellId id,
        Vector3 origin,
        int salt,
        Vector3 center,
        CoverKind kind,
        ref Vector3 best,
        ref float bestDensity)
    {
        Vector3 coverAt;
        if (!lake.TryCoverInCell(center, cellSize * 0.5f, kind, out coverAt))
            return;

        float ang = Hash01(Hash(id, salt + (int)kind * 271)) * Mathf.PI * 2f;
        float r = 1f + Hash01(Hash(id, salt + (int)kind * 733)) * 3f;
        for (int i = 0; i < 6; i++)
        {
            float a = ang + i * 1.047f;
            Vector3 p = ClampToCell(
                id,
                new Vector3(coverAt.x + Mathf.Cos(a) * r, origin.y, coverAt.z + Mathf.Sin(a) * r));
            if (DistanceXZ(p, origin) < keepClearOfViewer)
                continue;

            HabitatSample local = lake.SampleAt(p);
            if (!local.HasFish)
                continue;

            if (local.FishPerThousandSqMeters > bestDensity)
            {
                bestDensity = local.FishPerThousandSqMeters;
                best = p;
            }

            return;
        }
    }

    Vector3 ClampToCell(LakeCellId id, Vector3 point)
    {
        float xMin = id.X * cellSize;
        float zMin = id.Z * cellSize;
        point.x = Mathf.Clamp(point.x, xMin + 0.5f, xMin + cellSize - 0.5f);
        point.z = Mathf.Clamp(point.z, zMin + 0.5f, zMin + cellSize - 0.5f);
        return point;
    }

    float DensestInCell(LakeCellId id, float y)
    {
        float mean;
        float peak;
        SampleCellDensity(id, y, out mean, out peak);
        return peak;
    }

    void SampleCellDensity(LakeCellId id, float y, out float mean, out float peak)
    {
        float sum = 0f;
        int n = 0;
        peak = 0f;
        AccumulateDensity(CellCenter(id, y), ref sum, ref n, ref peak);
        for (int p = 0; p < 4; p++)
            AccumulateDensity(PointInCell(id, y, Hash(id, 200 + p)), ref sum, ref n, ref peak);

        Vector3 center = CellCenter(id, y);
        float half = cellSize * 0.5f;
        Vector3 coverAt;
        if (lake.TryCoverInCell(center, half, CoverKind.Wood, out coverAt))
            AccumulateDensity(coverAt, ref sum, ref n, ref peak);
        if (lake.TryCoverInCell(center, half, CoverKind.Rock, out coverAt))
            AccumulateDensity(coverAt, ref sum, ref n, ref peak);

        mean = n > 0 ? sum / n : 0f;
    }

    void AccumulateDensity(Vector3 world, ref float sum, ref int n, ref float peak)
    {
        float density = lake.SampleAt(world).FishPerThousandSqMeters;
        sum += density;
        n++;
        if (density > peak)
            peak = density;
    }

    static int StochasticRound(float expected, float u)
    {
        if (expected <= 0.0001f)
            return 0;
        int whole = Mathf.FloorToInt(expected);
        if (u < expected - whole)
            whole++;
        return whole;
    }

    static float DistanceXZ(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static int CompareHabitat((LakeCellId id, float dist, float density) a, (LakeCellId id, float dist, float density) b)
    {
        int byDensity = b.density.CompareTo(a.density);
        return byDensity != 0 ? byDensity : a.dist.CompareTo(b.dist);
    }

    void PushVisibility()
    {
        if (lake == null)
            return;

        float water = lake.Conditions != null ? lake.Conditions.WaterVisibility : 10.4f;
        float gameplay = lake.Conditions != null ? lake.Conditions.GameplayDepthScale : 0.5f;
        float vis = water * visibilityScale / Mathf.Max(0.2f, gameplay);
        Shader.SetGlobalFloat("_WiloWaterY", lake.SurfaceY);
        Shader.SetGlobalFloat("_WiloFishVisibility", Mathf.Max(1f, vis));
        Shader.SetGlobalFloat("_WiloFishViewDistance", viewDistance);
        Shader.SetGlobalFloat("_WiloFishFadePower", 1.1f);
    }

    int Hash(LakeCellId id, int salt)
    {
        harvested.TryGetValue(id, out int taken);
        unchecked
        {
            uint h = (uint)(id.X * 73856093)
                ^ (uint)(id.Z * 19349663)
                ^ (uint)(salt * 83492791)
                ^ (uint)(taken * 374761393)
                ^ (uint)(sessionSeed * 1103515245);

            // Callers read low bits (PointInCell, yaw), and an XOR of products
            // leaves those marching in step across the grid. Avalanche first.
            h ^= h >> 16;
            h *= 0x7feb352du;
            h ^= h >> 15;
            h *= 0x846ca68bu;
            h ^= h >> 16;

            int result = (int)h;
            return result == int.MinValue ? 0 : result;
        }
    }

    static float Hash01(int hash)
    {
        return (hash & 0x7fffffff) / (float)int.MaxValue;
    }

    void OnValidate()
    {
        cellSize = Mathf.Max(8f, cellSize);
        activeRadius = Mathf.Max(cellSize, activeRadius);
        despawnRadius = Mathf.Max(activeRadius + cellSize, despawnRadius);
        maxFish = Mathf.Max(1, maxFish);
        maxFishPerCell = Mathf.Max(1, maxFishPerCell);
        maxCellActivationsPerUpdate = Mathf.Max(1, maxCellActivationsPerUpdate);
        visualScaleMultiplier = Mathf.Clamp(visualScaleMultiplier, 1f, 3.5f);
        visibilityScale = Mathf.Clamp(visibilityScale, 0.25f, 1.5f);
        viewDistance = Mathf.Max(8f, viewDistance);
    }

    readonly struct LakeCellId : IEquatable<LakeCellId>
    {
        public readonly int X;
        public readonly int Z;

        public LakeCellId(int x, int z)
        {
            X = x;
            Z = z;
        }

        public bool Equals(LakeCellId other)
        {
            return X == other.X && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is LakeCellId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (X * 397) ^ Z;
        }
    }
}
