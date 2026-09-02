using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Activates habitat cells around the player/boat and returns fish to a
/// pool once those cells leave a despawn buffer. Fast travel just swaps
/// the active set; skipped cells never spawn.
/// Spawn rolls come from (world seed, calendar day, cell). Leave and return
/// the same day and the cell is unchanged. A new day re-rolls who lives there;
/// habitat still says whether the spot is generally good.
/// Fish taken out of a cell stay gone for the rest of that day; overnight the
/// pressure clears and the new day's sample fills in.
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
    [SerializeField, Tooltip("Panic ceiling only. Habitat density decides how many spawn below this.")]
    int maxFish = 28;
    [SerializeField] int maxCellActivationsPerUpdate = 6;
    [SerializeField] float updateInterval = 0.15f;
    [SerializeField] int prewarmPerSpecies = 8;
    [SerializeField] float keepClearOfViewer = 3f;
    [SerializeField, Tooltip("1 = real inches. Higher reads from the boat. 2.3 is about 2× the first pass.")]
    float visualScaleMultiplier = 2.3f;
    [SerializeField, Range(0.25f, 1.5f), Tooltip("1 = fade with the lakebed. Lower hides fish sooner than the bed.")]
    float visibilityScale = 1f;
    [SerializeField, Tooltip("Planar fade so distant bass do not read as a grid.")]
    float viewDistance = 48f;
    [SerializeField, Tooltip("TEMP: unhide every live fish and draw its weight. Turn off / delete when done scouting.")]
    bool debugShowFish = true;

    readonly Dictionary<LakeCellId, List<FishAgent>> occupied = new Dictionary<LakeCellId, List<FishAgent>>();
    readonly HashSet<LakeCellId> empty = new HashSet<LakeCellId>();
    readonly Dictionary<LakeCellId, int> harvested = new Dictionary<LakeCellId, int>();
    readonly Dictionary<FishSpecies, Stack<FishAgent>> pools = new Dictionary<FishSpecies, Stack<FishAgent>>();
    readonly List<LakeCellId> scratchCells = new List<LakeCellId>();
    readonly List<(LakeCellId id, float dist)> candidates = new List<(LakeCellId, float)>();

    Transform poolRoot;
    Vector3 lastOrigin;
    float nextUpdateTime;
    int liveCount;
    int worldSeed;
    int daySalt;
    GUIStyle debugLabel;
    GUIStyle debugHud;

    void Awake()
    {
        if (lake == null)
            lake = GetComponent<LakeSimulation>();
        poolRoot = new GameObject("FishPool").transform;
        poolRoot.SetParent(transform, false);
        ApplyFrom(SaveService.Instance);
    }

    /// <summary>
    /// The seed is the lake's identity. Mixed with the calendar day it rebuilds
    /// today's sample, so quitting and coming back mid-day does not reshuffle
    /// the water you were just fishing.
    /// </summary>
    void ApplyFrom(SaveService save)
    {
        if (save == null)
        {
            // No service in this scene (a test bed, say). Fall back to a one-off lake.
            worldSeed = UnityEngine.Random.Range(1, int.MaxValue);
            return;
        }

        worldSeed = save.Lake.worldSeed;
        if (save.Lake.clock != null)
            daySalt = save.Lake.clock.dayIndex;

        harvested.Clear();
        List<HarvestedCell> cells = save.Lake.harvested;
        for (int i = 0; i < cells.Count; i++)
            harvested[new LakeCellId(cells[i].x, cells[i].z)] = cells[i].count;
    }

    public void CaptureTo(LakeSave save)
    {
        if (save == null)
            return;

        save.worldSeed = worldSeed;
        save.harvested.Clear();
        foreach (KeyValuePair<LakeCellId, int> pair in harvested)
        {
            if (pair.Value <= 0)
                continue;

            save.harvested.Add(new HarvestedCell
            {
                x = pair.Key.X,
                z = pair.Key.Z,
                count = pair.Value
            });
        }
    }

    void Start()
    {
        Prewarm();
        nextUpdateTime = 0f;
        if (lake != null && lake.Conditions != null)
        {
            daySalt = lake.Conditions.DayIndex;
            lake.Conditions.DayChanged += OnDayChanged;
        }
    }

    void OnDestroy()
    {
        if (lake != null && lake.Conditions != null)
            lake.Conditions.DayChanged -= OnDayChanged;
    }

    /// <summary>
    /// A new day is a new roll. Pressure clears, live fish go back to the pool,
    /// and nearby cells spawn from today's hash on the next refresh.
    /// </summary>
    void OnDayChanged(int dayIndex)
    {
        daySalt = dayIndex;
        harvested.Clear();
        DespawnAll();
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

    void OnGUI()
    {
        if (!debugShowFish)
            return;

        EnsureDebugStyles();
        float heaviest = 0f;
        float sum = 0f;
        int n = 0;
        Camera cam = Camera.main;
        foreach (List<FishAgent> fish in occupied.Values)
        {
            for (int i = 0; i < fish.Count; i++)
            {
                FishAgent agent = fish[i];
                if (agent == null || !agent.gameObject.activeInHierarchy)
                    continue;

                float pounds = agent.Size.Pounds;
                sum += pounds;
                n++;
                if (pounds > heaviest)
                    heaviest = pounds;

                if (cam == null)
                    continue;

                Vector3 world = agent.transform.position + Vector3.up * 0.55f;
                Vector3 screen = cam.WorldToScreenPoint(world);
                if (screen.z < 0.4f)
                    continue;

                string name = agent.Species != null ? agent.Species.DisplayName : "Fish";
                string text = $"{name}  {pounds:0.0} lb";
                var rect = new Rect(screen.x - 90f, Screen.height - screen.y - 18f, 180f, 22f);
                DrawDebugLabel(rect, text, DebugWeightColor(pounds));
            }
        }

        string hud = n > 0
            ? $"TEMP  {n} live   avg {sum / n:0.0} lb   biggest {heaviest:0.0} lb"
            : "TEMP  0 live";
        var hudRect = new Rect((Screen.width - 520f) * 0.5f, 10f, 520f, 28f);
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.Label(new Rect(hudRect.x + 1f, hudRect.y + 1f, hudRect.width, hudRect.height), hud, debugHud);
        GUI.color = Color.white;
        GUI.Label(hudRect, hud, debugHud);
    }

    void EnsureDebugStyles()
    {
        if (debugLabel != null)
            return;

        debugLabel = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13,
            fontStyle = FontStyle.Bold
        };
        debugHud = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 16,
            fontStyle = FontStyle.Bold
        };
    }

    void DrawDebugLabel(Rect rect, string text, Color color)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, debugLabel);
        GUI.color = color;
        GUI.Label(rect, text, debugLabel);
        GUI.color = Color.white;
    }

    static Color DebugWeightColor(float pounds)
    {
        if (pounds >= 6f)
            return new Color(1f, 0.55f, 0.2f);
        if (pounds >= 4f)
            return new Color(1f, 0.85f, 0.25f);
        if (pounds >= 2f)
            return Color.white;
        return new Color(0.75f, 0.82f, 0.9f);
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

                candidates.Add((id, dist));
            }
        }

        // Closest cells first. Density still decides how many fish a cell holds;
        // ranking the whole ring by peak was letting far rock piles empty the
        // nearby weed flats.
        candidates.Sort(CompareDistance);
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
        // Peak is the stump / rock in the cell. Mean is diluted by open water
        // in the same 24 m square, so lean on peak or a lone boulder never
        // holds more than one fish.
        float expected = (mean * 0.40f + peak * 0.60f) * (area / 1000f);
        Vector3 cellCenter = CellCenter(id, origin.y);
        float half = cellSize * 0.5f;
        if (peak >= 1.4f &&
            (lake.TryCoverInCell(cellCenter, half, CoverKind.Rock, out _) ||
             lake.TryCoverInCell(cellCenter, half, CoverKind.Wood, out _)))
        {
            // A 24 m cell's open water dilutes density so a lone boulder
            // coin-flips one fish. Cover should hold a small group.
            float coverHold = Mathf.Lerp(1.9f, 3.3f, Mathf.InverseLerp(1.4f, 6.5f, peak));
            expected = Mathf.Max(expected, coverHold);
        }
        int target = StochasticRound(expected, Hash01(Hash(id, 3)));

        // The roll is what the cell holds on an untouched day. Whatever came out
        // of it today comes off the top, so a spot you worked over stays thin.
        harvested.TryGetValue(id, out int taken);
        target -= taken;
        if (target <= 0)
        {
            empty.Add(id);
            return false;
        }

        int attempts = Mathf.Max(target * 8, 12);
        var spawned = new List<FishAgent>(target);
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
            if (!FishAgent.HasSwimRoom(lake.GroundDepthMeters(point)))
                continue;

            HabitatFeatures sizeFeatures = SizeFeaturesFromNearbyCover(point, features);
            FishSpecies chosen = lake.PickSpecies(sizeFeatures, Hash01(Hash(id, 101 + i)));
            if (chosen == null || chosen.Prefab == null)
                continue;

            FishSize size = FishSize.Roll(
                local,
                chosen,
                Hash01(Hash(id, 50 + i)),
                Hash01(Hash(id, 61 + i)),
                lake.Profile,
                sizeFeatures,
                Hash01(Hash(id, 83 + i)));
            float columnT = FishAgent.BottomWeightedColumn(Hash01(Hash(id, 71 + i)));
            Vector3 spawnAt = new Vector3(
                point.x,
                lake.SurfaceY - FishAgent.DepthBelowSurface(lake.GroundDepthMeters(point), columnT),
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

        // Wood, rock, and pads compete on habitat value so a stray log does not
        // beat the rock a smallmouth wants, and a lily bed still gets used.
        ConsiderCover(id, origin, salt, center, CoverKind.Wood, ref best, ref bestDensity);
        ConsiderCover(id, origin, salt, center, CoverKind.Rock, ref best, ref bestDensity);
        ConsiderCover(id, origin, salt, center, CoverKind.Vegetation, ref best, ref bestDensity);
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
        float coverRadius;
        if (!lake.TryCoverInCell(center, cellSize * 0.5f, kind, out coverAt, out coverRadius))
            return;

        for (int i = 0; i < 6; i++)
        {
            float a = Hash01(Hash(id, salt + (int)kind * 271 + i * 17)) * Mathf.PI * 2f;
            float r = CoverSitMeters(kind, coverRadius, id, salt, i);
            Vector3 p = ClampToCell(
                id,
                new Vector3(coverAt.x + Mathf.Cos(a) * r, origin.y, coverAt.z + Mathf.Sin(a) * r));
            if (DistanceXZ(p, origin) < keepClearOfViewer)
                continue;

            HabitatSample local = lake.SampleAt(p);
            if (!local.HasFish)
                continue;
            if (!FishAgent.HasSwimRoom(lake.GroundDepthMeters(p)))
                continue;

            if (local.FishPerThousandSqMeters > bestDensity)
            {
                bestDensity = local.FishPerThousandSqMeters;
                best = p;
            }
        }
    }

    /// <summary>
    /// Size and species should read the structure the fish belongs to, not the
    /// sit point 2 m off it. Depth stays where they spawned.
    /// </summary>
    HabitatFeatures SizeFeaturesFromNearbyCover(Vector3 spawn, in HabitatFeatures atSpawn)
    {
        float rock = atSpawn.Rock;
        float wood = atSpawn.Wood;
        Vector3 coverAt;
        if (lake.TryCoverInCell(spawn, 6f, CoverKind.Rock, out coverAt))
            rock = Mathf.Max(rock, lake.SampleFeatures(coverAt).Rock);
        if (lake.TryCoverInCell(spawn, 6f, CoverKind.Wood, out coverAt))
            wood = Mathf.Max(wood, lake.SampleFeatures(coverAt).Wood);
        if (Mathf.Approximately(rock, atSpawn.Rock) && Mathf.Approximately(wood, atSpawn.Wood))
            return atSpawn;
        return new HabitatFeatures(
            atSpawn.DepthFeet,
            atSpawn.Dropoff,
            rock,
            wood,
            atSpawn.Vegetation,
            atSpawn.Convexity);
    }

    float CoverSitMeters(CoverKind kind, float coverRadius, LakeCellId id, int salt, int sample)
    {
        float u = Hash01(Hash(id, salt + (int)kind * 733 + sample * 41));
        HabitatProfile p = lake.Profile;
        switch (kind)
        {
            case CoverKind.Wood:
            {
                float hug = p != null ? p.woodHugMeters : 2.2f;
                return 0.35f + u * Mathf.Clamp(hug * 0.4f, 0.4f, 1f);
            }
            case CoverKind.Rock:
            {
                // Stay on the rock. Sitting 2 m off a 2 m stone reads as
                // open water and the size roll collapses.
                float maxR = Mathf.Max(0.35f, coverRadius) * 0.8f;
                return maxR * u * u;
            }
            default:
                return 0.45f + u * 1.35f;
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
        if (lake.TryCoverInCell(center, half, CoverKind.Vegetation, out coverAt))
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

    static int CompareDistance((LakeCellId id, float dist) a, (LakeCellId id, float dist) b)
    {
        return a.dist.CompareTo(b.dist);
    }

    void PushVisibility()
    {
        if (lake == null)
            return;

        float water = lake.Conditions != null ? lake.Conditions.WaterVisibility : 13.72f;
        float vis = debugShowFish ? 0f : Mathf.Max(1f, water * visibilityScale);
        Shader.SetGlobalFloat("_WiloWaterY", lake.SurfaceY);
        Shader.SetGlobalFloat("_WiloFishVisibility", vis);
        Shader.SetGlobalFloat("_WiloFishViewDistance", debugShowFish ? 400f : viewDistance);
        Shader.SetGlobalFloat("_WiloFishFadePower", 1.1f);
    }

    int Hash(LakeCellId id, int salt)
    {
        unchecked
        {
            uint h = (uint)(id.X * 73856093)
                ^ (uint)(id.Z * 19349663)
                ^ (uint)(salt * 83492791)
                ^ (uint)(worldSeed * 1103515245)
                ^ (uint)(daySalt * 198491317);

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
