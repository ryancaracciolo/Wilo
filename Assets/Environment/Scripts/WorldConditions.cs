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
    [SerializeField, Range(0f, 24f)] float startHour = 7.5f;
    [SerializeField, Min(1)] int startYear = 1;
    [SerializeField, Range(0, GameCalendar.DaysPerYear - 1)] int startDayOfYear = 30;
    [SerializeField] DayOfWeek startWeekday = DayOfWeek.Saturday;

    [Header("Lake")]
    [SerializeField, Range(0.2f, 1f), Tooltip("Multiplies geometric depth for sonar and fishing. 0.4 makes a 20 ft hole read as 8 ft. Terrain stays put.")]
    float gameplayDepthScale = 0.4f;

    static readonly string[] StructureRoots = { "Rocks", "Stumps", "FallenTrees" };
    static readonly string[] CoverRoots =
    {
        "Rocks", "Stumps", "FallenTrees", "Logs", "LilyPads", "WeedBeds"
    };
    static readonly RaycastHit[] DepthHits = new RaycastHit[24];
    static readonly List<Renderer> StructureScratch = new List<Renderer>();

    /// <summary>
    /// A piece of rock or timber standing off the bed. None of this scenery has
    /// colliders, so each one is baked once as the dome inscribed in its bounds.
    /// The boulders here are scaled up hard enough that a box would read as a
    /// wide flat shelf; a dome keeps them shaped like the thing being sampled.
    /// </summary>
    struct StructureDome
    {
        public float CenterX;
        public float CenterZ;
        public float HalfX;
        public float HalfZ;
        public float TopY;
        public float HalfY;

        public bool TryHeight(float x, float z, out float y)
        {
            float dx = (x - CenterX) / HalfX;
            float dz = (z - CenterZ) / HalfZ;
            float radius = dx * dx + dz * dz;
            if (radius >= 1f)
            {
                y = 0f;
                return false;
            }

            y = TopY - HalfY * (1f - Mathf.Sqrt(1f - radius));
            return true;
        }
    }

    PlayerBoatInteractor boat;
    Transform player;
    Transform waterSurface;
    readonly List<StructureDome> structure = new List<StructureDome>();
    float waterHeight;
    bool hasWaterHeight;
    float waterVisibility = 10.4f;
    IClockSource clock;
    bool live;

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
    public float DepthFeet { get; private set; }
    public float BoatSpeedMph { get; private set; }
    public bool OnBoat { get; private set; }
    public bool OverWater { get; private set; }
    public Transform PlayerTransform => player;
    public BoatMotor OccupiedBoat => boat != null ? boat.OccupiedBoat : null;

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

    public void AdvanceDays(int days, float wakeHour = 6.5f)
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
    void DebugSkipDay() => Jump(() => AdvanceDays(1, 7.5f));

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
    }

    void ApplyFrom(SaveService save)
    {
        if (save == null || save.IsNewGame)
            return;

        ClockData stored = save.Lake.clock;
        clock.Set(new GameCalendar
        {
            DayIndex = stored.dayIndex,
            MinutesInDay = stored.minutesInDay,
            EpochWeekday = (DayOfWeek)stored.epochWeekday
        });
    }

    public void CaptureTo(LakeSave save)
    {
        if (save == null || clock == null)
            return;

        GameCalendar now = clock.Calendar;
        save.clock.dayIndex = now.DayIndex;
        save.clock.minutesInDay = now.MinutesInDay;
        save.clock.epochWeekday = (int)now.EpochWeekday;
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

        float geometric = GeometricDepthMeters(sampleAt, beamRight, OnBoat ? 1.1f : 0f);
        OverWater = geometric > 0.05f;
        DepthFeet = Mathf.Max(0f, ToGameplayDepth(geometric) * 3.28084f);
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

        if (includeStructure)
        {
            if (structure.Count == 0)
                CacheStructure();

            for (int i = 0; i < structure.Count; i++)
            {
                float top;
                if (structure[i].TryHeight(world.x, world.z, out top) && top > bedY)
                    bedY = top;
            }
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
        Transform t = hit;
        while (t != null)
        {
            string name = t.name;
            for (int i = 0; i < CoverRoots.Length; i++)
            {
                string root = CoverRoots[i];
                if (name == root || name.StartsWith(root + "_"))
                    return true;
            }

            t = t.parent;
        }

        return false;
    }

    void CacheStructure()
    {
        structure.Clear();
        for (int i = 0; i < StructureRoots.Length; i++)
        {
            var root = GameObject.Find(StructureRoots[i]);
            if (root == null)
                continue;

            StructureScratch.Clear();
            root.GetComponentsInChildren(true, StructureScratch);
            for (int r = 0; r < StructureScratch.Count; r++)
            {
                Renderer renderer = StructureScratch[r];
                if (renderer == null || !renderer.enabled)
                    continue;

                Bounds bounds = renderer.bounds;
                structure.Add(new StructureDome
                {
                    CenterX = bounds.center.x,
                    CenterZ = bounds.center.z,
                    HalfX = Mathf.Max(0.05f, bounds.extents.x),
                    HalfZ = Mathf.Max(0.05f, bounds.extents.z),
                    TopY = bounds.max.y,
                    HalfY = bounds.extents.y
                });
            }
        }
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

        if (waterSurface == null)
            return;

        var renderer = waterSurface.GetComponent<Renderer>();
        waterHeight = renderer != null ? renderer.bounds.max.y : waterSurface.position.y;
        waterVisibility = 10.4f;
        Material water = renderer != null ? renderer.sharedMaterial : null;
        if (water != null && water.HasProperty("_Visibility"))
            waterVisibility = Mathf.Max(0.5f, water.GetFloat("_Visibility"));
        hasWaterHeight = true;
    }
}
