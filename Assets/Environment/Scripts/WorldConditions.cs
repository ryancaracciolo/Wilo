using System;
using System.Collections.Generic;
using UnityEngine;

public enum WeatherKind
{
    Sunny,
    PartlyCloudy,
    Rain
}

/// <summary>
/// Scene facade for weather, calendar, and lake queries.
/// The HUD and fishing read this; DayNightVisuals drives look from the clock.
/// </summary>
[DefaultExecutionOrder(-50)]
public class WorldConditions : MonoBehaviour
{
    [Header("Weather")]
    [SerializeField] WeatherKind weather = WeatherKind.Sunny;
    [SerializeField] float airTempF = 71f;
    [SerializeField] float waterTempF = 64f;
    [SerializeField] float windFromDegrees = 45f;
    [SerializeField] float windMph = 4f;

    [Header("Clock")]
    [Tooltip("Real minutes that pass for one in-game day. 40 is half the previous pace.")]
    [SerializeField, Min(1f)] float realMinutesPerDay = 40f;
    [SerializeField, Range(0f, 24f)] float startHour = GameCalendar.NewGameHour;
    [SerializeField, Min(1)] int startYear = 1;
    [SerializeField, Range(0, GameCalendar.DaysPerYear - 1)] int startDayOfYear = GameCalendar.NewGameDayOfYear;
    [SerializeField] DayOfWeek startWeekday = GameCalendar.NewGameWeekday;

    [Header("Lake")]
    [SerializeField, Range(0.2f, 1f), Tooltip("Multiplies geometric depth for sonar and fishing. 0.4 makes a 20 ft hole read as 8 ft. Terrain stays put.")]
    float gameplayDepthScale = 0.4f;

    public const string StructureLayerName = "Structure";

    static readonly string[] StructureRoots = { "Rocks", "Stumps", "FallenTrees", "Logs" };
    static readonly string[] CoverRoots =
    {
        "Rocks", "Stumps", "FallenTrees", "Logs", "LilyPads", "WeedBeds"
    };
    static readonly RaycastHit[] DepthHits = new RaycastHit[24];
    static readonly List<Renderer> StructureScratch = new List<Renderer>();
    const string LureColliderName = "LureCollider";

    bool structureCached;

    PlayerBoatInteractor boat;
    Transform player;
    Transform waterSurface;
    float waterHeight;
    bool hasWaterHeight;
    float waterVisibility = 13.72f;
    IClockSource clock;
    bool live;
    readonly List<MapSpot> mapSpots = new List<MapSpot>();

    public WeatherKind Weather => weather;
    public float AirTempF => airTempF;
    public float WaterTempF => waterTempF;
    public float WindFromDegrees => windFromDegrees;
    public float WindMph => windMph;
    public float Hour => Clock.Hour;
    public int Year => Clock.Year;
    public int DayOfYear => Clock.DayOfYear;
    public int DayIndex => Clock.DayIndex;
    public DayOfWeek Weekday => Clock.Weekday;
    public Season Season => Clock.Season;
    public FishingPhase Phase => Clock.Phase;
    public string DateLabel => Clock.DateLabel;
    public string SeasonLabel => Clock.SeasonLabel;
    public float RealMinutesPerDay => Mathf.Max(1f, realMinutesPerDay);
    public float DawnHour => Clock.DawnHour;
    public float DuskHour => Clock.DuskHour;
    public float DaylightHours => Clock.DaylightHours;
    public float SeasonBlend => Clock.SeasonBlend;
    public bool IsNight => Clock.IsNight;

    /// <summary>Set every frame by the HUD so open panels do not burn fishing time.</summary>
    public bool HoldClock
    {
        get => clock != null && clock.Hold;
        set
        {
            if (clock != null)
                clock.Hold = value;
        }
    }

    /// <summary>The calendar itself, for systems that need to look at other days.</summary>
    public GameCalendar Calendar => Clock;

    /// <summary>Swaps where time comes from. A shared session installs its own source here.</summary>
    public void SetClockSource(IClockSource source)
    {
        if (source == null)
            return;
        clock = source;
    }

    GameCalendar Clock => live && clock != null ? clock.Calendar : PreviewClock();
    public float GameplayDepthScale => gameplayDepthScale > 0.05f ? gameplayDepthScale : 0.5f;
    public static int StructureLayer => LayerMask.NameToLayer(StructureLayerName);
    public static int StructureMask => LayerMask.GetMask(StructureLayerName);
    public float DepthFeet { get; private set; }

    /// <summary>Lake-bed depth with rock and timber ignored. Sonar sand uses this.</summary>
    public float BedFeet { get; private set; }

    /// <summary>How far a rock stands above the bed, in gameplay feet. 0 if none.</summary>
    public float RockRiseFeet { get; private set; }

    public float BoatSpeedMph { get; private set; }
    public bool OnBoat { get; private set; }
    public bool OverWater { get; private set; }
    public Transform PlayerTransform => player;
    public BoatMotor OccupiedBoat => boat != null ? boat.OccupiedBoat : null;
    public IReadOnlyList<MapSpot> MapSpots => mapSpots;
    public event Action MapSpotsChanged;

    public float WaterHeight
    {
        get
        {
            if (!hasWaterHeight)
                CacheWater();
            return waterHeight;
        }
    }

    public float WaterVisibility
    {
        get
        {
            if (!hasWaterHeight)
                CacheWater();
            return waterVisibility;
        }
    }

    public Vector3 QueryOrigin
    {
        get
        {
            if (OnBoat && OccupiedBoat != null)
                return OccupiedBoat.transform.position;
            if (player != null)
                return player.position;
            return transform.position;
        }
    }

    public string TimeLabel => Clock.TimeLabel;

    public string WeatherLabel => weather switch
    {
        WeatherKind.PartlyCloudy => "Partly cloudy",
        WeatherKind.Rain => "Rain",
        _ => "Sunny"
    };

    public string WindLabel
    {
        get
        {
            string[] dirs = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int i = Mathf.RoundToInt(windFromDegrees / 45f) & 7;
            return $"{dirs[i]} {windMph:0} mph";
        }
    }

    public event Action<int> DayChanged;

    /// <summary>Sleeps to the next occurrence of an hour. Returns days skipped.</summary>
    public int AdvanceToHour(float hour)
    {
        if (!Application.isPlaying)
        {
            SetTime(hour);
            return 0;
        }

        GameCalendar next = clock.Calendar;
        int days = next.AdvanceToHour(hour);
        clock.Set(next);
        if (days > 0)
            DayChanged?.Invoke(next.DayIndex);
        return days;
    }

    public void SetTime(float hour)
    {
        if (!Application.isPlaying)
        {
            startHour = Mathf.Repeat(hour, 24f);
            return;
        }

        GameCalendar next = clock.Calendar;
        next.SetHour(hour);
        clock.Set(next);
    }

    public void AdvanceDays(int days, float wakeHour = GameCalendar.NewGameHour)
    {
        if (!Application.isPlaying)
        {
            var next = PreviewClock();
            next.AdvanceDays(days, wakeHour);
            startYear = next.Year;
            startDayOfYear = next.DayOfYear;
            startHour = next.Hour;
            startWeekday = next.Weekday;
            return;
        }

        GameCalendar moved = clock.Calendar;
        int from = moved.DayIndex;
        moved.AdvanceDays(days, wakeHour);
        clock.Set(moved);
        if (moved.DayIndex != from)
            DayChanged?.Invoke(moved.DayIndex);
    }

    [ContextMenu("Time/Dawn")]
    void DebugDawn() => JumpTo(DawnHour + 0.25f);

    [ContextMenu("Time/Noon")]
    void DebugNoon() => JumpTo(GameCalendar.SolarNoonHour);

    [ContextMenu("Time/Dusk")]
    void DebugDusk() => JumpTo(DuskHour - 0.5f);

    [ContextMenu("Time/Night")]
    void DebugNight() => JumpTo(Mathf.Repeat(DuskHour + 2f, 24f));

    [ContextMenu("Time/Skip A Day")]
    void DebugSkipDay() => Jump(() => AdvanceDays(1, GameCalendar.NewGameHour));

    [ContextMenu("Time/Skip A Season")]
    void DebugSkipSeason() => Jump(() => AdvanceDays(GameCalendar.DaysPerSeason));

    void JumpTo(float hour) => Jump(() => SetTime(hour));

    void Jump(Action change)
    {
        change();
#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    void Awake()
    {
        clock = new LocalClockSource(PreviewClock());
        live = true;
        ApplyFrom(SaveService.Instance);
        Shader.SetGlobalFloat("_WiloGameplayDepthScale", GameplayDepthScale);
    }

    void ApplyFrom(SaveService save)
    {
        if (save == null || save.IsNewGame)
            return;

        LoadMapSpots(save.Lake);

        ClockData stored = save.Lake.clock;
        if (stored == null || IsUnplayedClock(save, stored))
            return;

        clock.Set(new GameCalendar
        {
            DayIndex = stored.dayIndex,
            MinutesInDay = stored.minutesInDay,
            EpochWeekday = (DayOfWeek)stored.epochWeekday
        });
    }

    public void CaptureTo(LakeSave save)
    {
        if (save == null)
            return;

        if (clock != null)
            save.clock = ClockData.From(clock.Calendar);

        if (save.mapSpots == null)
            save.mapSpots = new List<MapSpot>();
        save.mapSpots.Clear();
        save.mapSpots.AddRange(mapSpots);
    }

    void LoadMapSpots(LakeSave lake)
    {
        mapSpots.Clear();
        if (lake?.mapSpots == null)
            return;

        for (int i = 0; i < lake.mapSpots.Count; i++)
        {
            MapSpot spot = lake.mapSpots[i];
            if (spot == null || string.IsNullOrWhiteSpace(spot.name))
                continue;
            if (string.IsNullOrEmpty(spot.id))
                spot.id = Guid.NewGuid().ToString("N");
            mapSpots.Add(spot);
        }
    }

    public MapSpot AddMapSpot(string name, Vector3 world)
    {
        string clean = MapSpot.CleanName(name);
        if (clean.Length == 0 || mapSpots.Count >= MapSpot.MaxCount)
            return null;

        var spot = new MapSpot
        {
            id = Guid.NewGuid().ToString("N"),
            name = clean,
            worldPosition = world
        };
        mapSpots.Add(spot);
        MapSpotsChanged?.Invoke();
        return spot;
    }

    public bool RemoveMapSpot(MapSpot spot)
    {
        if (spot == null || !mapSpots.Remove(spot))
            return false;
        MapSpotsChanged?.Invoke();
        return true;
    }

    public void CopyMapSpots(List<MapSpot> dest)
    {
        dest.Clear();
        dest.AddRange(mapSpots);
    }

    /// <summary>
    /// The porch writes a lake file before anyone has stood on the dock. That
    /// used to persist midnight on March 1, which is past curfew and also the
    /// winter-spring seam — dark water, no sunrise. Keep the authored morning.
    /// </summary>
    static bool IsUnplayedClock(SaveService save, ClockData stored)
    {
        if (stored.IsUnset)
            return true;

        bool noHarvest = save.Lake.harvested == null || save.Lake.harvested.Count == 0;
        return stored.dayIndex == 0 && noHarvest;
    }

    void Start()
    {
        CacheWater();
        FindPlayer();
        CacheStructure();
    }

    void Update()
    {
        // Holding is the source's business now, so a session clock can refuse to.
        int wrapped = clock.Tick(Time.deltaTime, RealMinutesPerDay);
        if (wrapped > 0)
            DayChanged?.Invoke(clock.Calendar.DayIndex);

        if (player == null)
            FindPlayer();

        OnBoat = boat != null && boat.IsOnBoat;
        Vector3 sampleAt = OnBoat && OccupiedBoat != null
            ? OccupiedBoat.transform.position
            : player != null ? player.position : transform.position;
        Vector3 beamRight = OnBoat && OccupiedBoat != null
            ? OccupiedBoat.transform.right
            : Vector3.right;

        float beam = OnBoat ? 1.1f : 0f;
        float geometric = GeometricDepthMeters(sampleAt, beamRight, beam);
        OverWater = geometric > 0.05f;
        DepthFeet = Mathf.Max(0f, ToGameplayDepth(geometric) * 3.28084f);
        float bedMeters = SampleDepth(sampleAt, beamRight, beam, false);
        BedFeet = Mathf.Max(0f, ToGameplayDepth(bedMeters) * 3.28084f);
        float rockRiseMeters = SampleRockRiseMeters(sampleAt, beamRight, beam, bedMeters);
        RockRiseFeet = Mathf.Max(0f, ToGameplayDepth(rockRiseMeters) * 3.28084f);
        BoatSpeedMph = OnBoat && OccupiedBoat != null
            ? Mathf.Abs(OccupiedBoat.Speed) * 2.23694f
            : 0f;
    }

    public float GeometricDepthMeters(Vector3 world)
    {
        return GeometricDepthMeters(world, Vector3.right, 0f);
    }

    public float GeometricDepthMeters(Vector3 world, Vector3 right, float beam)
    {
        return SampleDepth(world, right, beam, true);
    }

    public float BedDepthMeters(Vector3 world)
    {
        return SampleColumn(world, false);
    }

    public float SampleDepthMeters(Vector3 world)
    {
        return ToGameplayDepth(GeometricDepthMeters(world));
    }

    public float SampleDepthMeters(Vector3 world, Vector3 right, float beam)
    {
        return ToGameplayDepth(GeometricDepthMeters(world, right, beam));
    }

    float SampleDepth(Vector3 world, Vector3 right, float beam, bool includeStructure)
    {
        float depth = SampleColumn(world, includeStructure);
        if (beam <= 0.01f)
            return depth;

        depth = Mathf.Min(depth, SampleColumn(world + right * beam, includeStructure));
        depth = Mathf.Min(depth, SampleColumn(world - right * beam, includeStructure));
        return depth;
    }

    public float ToGameplayDepth(float geometricMeters)
    {
        if (geometricMeters <= 0.05f)
            return geometricMeters;
        return geometricMeters * GameplayDepthScale;
    }

    /// <summary>
    /// Rock height above the bed. Uses collider bounds so the sonar mound
    /// follows the boulder, not every jagged mesh face.
    /// </summary>
    float SampleRockRiseMeters(Vector3 world, Vector3 right, float beam, float bedMeters)
    {
        float rise = RockRiseAt(world, bedMeters);
        if (beam <= 0.01f)
            return rise;

        rise = Mathf.Max(rise, RockRiseAt(world + right * beam, bedMeters));
        rise = Mathf.Max(rise, RockRiseAt(world - right * beam, bedMeters));
        return rise;
    }

    float RockRiseAt(Vector3 world, float bedMeters)
    {
        if (!hasWaterHeight)
            return 0f;
        if (!structureCached)
            CacheStructure();

        int mask = StructureMask;
        if (mask == 0)
            mask = ~LayerMask.GetMask("Player", "Water", "Ignore Raycast", "UI", "TransparentFX");

        Vector3 origin = world;
        origin.y = waterHeight + 2.5f;
        int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, DepthHits, 80f, mask, QueryTriggerInteraction.Ignore);
        float best = 0f;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = DepthHits[i];
            if (hit.collider == null || !IsRockTransform(hit.transform))
                continue;

            Bounds b = hit.collider.bounds;
            float topDepth = waterHeight - b.max.y;
            if (topDepth >= bedMeters - 0.02f)
                continue;

            float nx = Mathf.Abs(world.x - b.center.x) / Mathf.Max(0.15f, b.extents.x);
            float nz = Mathf.Abs(world.z - b.center.z) / Mathf.Max(0.15f, b.extents.z);
            float fade = 1f - Mathf.SmoothStep(0.4f, 1.05f, Mathf.Max(nx, nz));
            if (fade <= 0.01f)
                continue;

            best = Mathf.Max(best, (bedMeters - topDepth) * fade);
        }

        return best;
    }

    float SampleColumn(Vector3 world, bool includeStructure = true)
    {
        if (!hasWaterHeight)
            CacheWater();
        if (!hasWaterHeight)
            return 0f;

        float bedY = float.NegativeInfinity;

        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null && terrain.terrainData != null)
            bedY = terrain.SampleHeight(world) + terrain.transform.position.y;

        if (includeStructure && !structureCached)
            CacheStructure();

        int mask = ~LayerMask.GetMask("Player", "Water", "Ignore Raycast", "UI", "TransparentFX");
        Vector3 origin = world;
        origin.y = waterHeight + 2.5f;
        int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, DepthHits, 80f, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = DepthHits[i];
            if (ShouldIgnoreDepthHit(hit.transform, includeStructure))
                continue;
            if (hit.point.y > bedY)
                bedY = hit.point.y;
        }

        if (float.IsNegativeInfinity(bedY))
            return 0f;

        return waterHeight - bedY;
    }

    bool ShouldIgnoreDepthHit(Transform hit, bool includeStructure)
    {
        if (hit == null)
            return true;
        if (player != null && (hit == player || hit.IsChildOf(player)))
            return true;

        BoatMotor occupied = OccupiedBoat;
        if (occupied != null && (hit == occupied.transform || hit.IsChildOf(occupied.transform)))
            return true;

        if (!includeStructure && IsCoverTransform(hit))
            return true;

        return hit.GetComponent<CharacterController>() != null;
    }

    static bool IsCoverTransform(Transform hit)
    {
        return HitsNamedRoot(hit, CoverRoots);
    }

    static bool IsRockTransform(Transform hit)
    {
        Transform t = hit;
        while (t != null)
        {
            string name = t.name;
            if (name == "Rocks" || name.StartsWith("Rocks_"))
                return true;
            t = t.parent;
        }

        return false;
    }

    static bool HitsNamedRoot(Transform hit, string[] roots)
    {
        Transform t = hit;
        while (t != null)
        {
            string name = t.name;
            for (int i = 0; i < roots.Length; i++)
            {
                string root = roots[i];
                if (name == root || name.StartsWith(root + "_"))
                    return true;
            }

            t = t.parent;
        }

        return false;
    }

    /// <summary>
    /// Rock and timber have no authored colliders. Each mesh gets a hidden
    /// MeshCollider at play time so the lure can sit on the real surface
    /// instead of a bounds-sized dome.
    /// </summary>
    void CacheStructure()
    {
        if (structureCached || !Application.isPlaying)
            return;

        int layer = StructureLayer;
        if (layer < 0)
            return;

        Physics.IgnoreLayerCollision(layer, 0, true);
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
            Physics.IgnoreLayerCollision(layer, playerLayer, true);

        for (int i = 0; i < StructureRoots.Length; i++)
        {
            var root = GameObject.Find(StructureRoots[i]);
            if (root == null)
                continue;

            StructureScratch.Clear();
            root.GetComponentsInChildren(true, StructureScratch);
            for (int r = 0; r < StructureScratch.Count; r++)
                EnsureLureCollider(StructureScratch[r], layer);
        }

        Physics.SyncTransforms();
        structureCached = true;
    }

    static void EnsureLureCollider(Renderer renderer, int layer)
    {
        if (renderer == null || !renderer.enabled)
            return;

        var filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
            return;

        Transform existing = renderer.transform.Find(LureColliderName);
        if (existing != null)
            return;

        var go = new GameObject(LureColliderName);
        go.layer = layer;
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(renderer.transform, false);

        var collider = go.AddComponent<MeshCollider>();
        collider.sharedMesh = filter.sharedMesh;
        collider.convex = false;
    }

    GameCalendar PreviewClock()
    {
        return GameCalendar.FromStart(startYear, startDayOfYear, startWeekday, startHour);
    }

    void FindPlayer()
    {
        var playerGo = GameObject.FindGameObjectWithTag("Player");
        if (playerGo == null)
            return;
        player = playerGo.transform;
        boat = playerGo.GetComponent<PlayerBoatInteractor>();
    }

    void CacheWater()
    {
        if (waterSurface == null)
        {
            var surface = GameObject.Find("Surface");
            if (surface != null)
                waterSurface = surface.transform;
        }

        Shader.SetGlobalFloat("_WiloGameplayDepthScale", GameplayDepthScale);
        if (waterSurface == null)
            return;

        var renderer = waterSurface.GetComponent<Renderer>();
        waterHeight = renderer != null ? renderer.bounds.max.y : waterSurface.position.y;
        // Water shader authors hide-at in gameplay feet. Fishing and fish fade
        // want the same distance as metres of actual water column.
        waterVisibility = 18f / (3.28084f * GameplayDepthScale);
        Material water = renderer != null ? renderer.sharedMaterial : null;
        if (water != null && water.HasProperty("_Visibility"))
        {
            float hideFeet = Mathf.Max(1f, water.GetFloat("_Visibility"));
            waterVisibility = hideFeet / (3.28084f * GameplayDepthScale);
        }
        hasWaterHeight = true;
    }
}
