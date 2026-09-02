using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Spawns the decorative tournament field when the player is actually in an
/// event, then tears it down at weigh-in or forfeit. Boats are the player's
/// hull with recolored copies of the player on them.
/// </summary>
public class TournamentBoatDirector : MonoBehaviour
{
    static readonly Color[] Skins =
    {
        new Color(0.96f, 0.80f, 0.66f),
        new Color(0.93f, 0.72f, 0.58f),
        new Color(0.83f, 0.61f, 0.45f),
        new Color(0.72f, 0.49f, 0.34f),
        new Color(0.55f, 0.35f, 0.23f),
        new Color(0.38f, 0.24f, 0.16f)
    };

    static readonly Color[] Hats =
    {
        Color.white,
        new Color(0.12f, 0.16f, 0.22f),
        new Color(0.78f, 0.18f, 0.16f),
        new Color(0.16f, 0.32f, 0.56f),
        new Color(0.92f, 0.55f, 0.12f),
        new Color(0.22f, 0.42f, 0.24f),
        new Color(0.45f, 0.28f, 0.18f),
        new Color(0.85f, 0.82f, 0.55f)
    };

    static readonly Color[] Vests =
    {
        new Color(0.322f, 0.373f, 0.235f),
        new Color(0.82f, 0.32f, 0.12f),
        new Color(0.14f, 0.28f, 0.48f),
        new Color(0.55f, 0.18f, 0.16f),
        new Color(0.18f, 0.18f, 0.20f),
        new Color(0.62f, 0.52f, 0.28f),
        new Color(0.28f, 0.42f, 0.38f)
    };

    static readonly Color[] Pockets =
    {
        new Color(0.639f, 0.545f, 0.373f),
        new Color(0.22f, 0.22f, 0.22f),
        Color.white,
        new Color(0.45f, 0.28f, 0.14f),
        new Color(0.78f, 0.72f, 0.52f)
    };

    [SerializeField] TournamentDirector director;
    [SerializeField] WorldConditions conditions;
    [SerializeField] TournamentSite site;
    [SerializeField] LakeSimulation lake;

    [Tooltip("Player hull. Empty uses the scene PlayerBoat.")]
    [SerializeField] GameObject boatPrefab;

    [Tooltip("Stripped angler. Empty copies and strips the player.")]
    [SerializeField] GameObject anglerPrefab;

    [Tooltip("0 uses the event field size.")]
    [SerializeField, Min(0)] int boatCount;

    [SerializeField, Min(1)] int maxBoats = 12;
    [SerializeField] float minCruiseDepth = 1.15f;
    [SerializeField] float minCampDepth = 0.7f;
    [SerializeField] float boatSpacing = 12f;
    [SerializeField] float campSpacing = 7f;

    readonly List<TournamentBoatAgent> field = new List<TournamentBoatAgent>();
    readonly List<string> names = new List<string>();
    readonly List<GameObject> spawned = new List<GameObject>();

    Vector3 waterSide = Vector3.forward;
    bool hooked;
    bool laidOut;

    public float Hour => conditions != null ? conditions.Hour : 0f;

    void OnEnable()
    {
        Resolve();
        Hook();
        Sync();
    }

    void OnDisable()
    {
        if (director != null && hooked)
            director.PhaseChanged -= OnPhaseChanged;
        hooked = false;
        Despawn();
    }

    void Update()
    {
        Resolve();
        Hook();
        if (director != null && director.AwaitingWeighIn)
            RecallAll();
    }

    void OnPhaseChanged(TournamentPhase _)
    {
        Sync();
    }

    void Sync()
    {
        if (director == null || director.Phase == TournamentPhase.Idle)
        {
            Despawn();
            return;
        }

        SpawnField();
        if (director.AwaitingWeighIn)
            RecallAll();
    }

    void SpawnField()
    {
        if (field.Count > 0)
            return;
        if (director == null || !director.Active.IsValid)
            return;

        Resolve();
        CacheWaterSide();

        TournamentOccurrence occurrence = director.Active;
        TournamentDefinition def = occurrence.Definition;
        TournamentField.CopyNames(occurrence, names);

        int count = boatCount > 0 ? boatCount : def.FieldSize;
        count = Mathf.Clamp(count, 1, Mathf.Min(maxBoats, names.Count));

        var rng = new System.Random(Seed(occurrence.Id, occurrence.DayIndex) ^ 7919);
        float hour = Hour;
        GameObject boatTemplate = ResolveBoatTemplate();
        if (boatTemplate == null)
            return;

        for (int i = 0; i < count; i++)
        {
            if (!TrySpawnSlot(i, count, rng, out Vector3 spawnAt, out float yaw))
                continue;

            GameObject hullGo = Instantiate(boatTemplate, spawnAt, Quaternion.Euler(0f, yaw, 0f));
            hullGo.name = $"TournamentBoat_{i + 1}";
            spawned.Add(hullGo);

            BoatMotor hull = hullGo.GetComponent<BoatMotor>();
            if (hull == null)
            {
                Destroy(hullGo);
                continue;
            }

            hull.SetBoardable(false);
            hull.ClearAiDrive();

            string anglerName = i < names.Count ? names[i] : $"Angler {i + 1}";
            GameObject rider = SpawnAngler(hullGo.transform, anglerName, occurrence.DayIndex * 31 + i);
            if (rider == null)
            {
                Destroy(hullGo);
                continue;
            }

            var agent = hullGo.AddComponent<TournamentBoatAgent>();
            agent.Bind(this, hull, rider.transform, anglerName, occurrence.DayIndex * 17 + i * 13, def.StartHour, def.EndHour, hour);
            field.Add(agent);
        }
    }

    void Despawn()
    {
        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i] != null)
                Destroy(spawned[i]);
        }

        spawned.Clear();
        field.Clear();
        laidOut = false;
    }

    void RecallAll()
    {
        for (int i = 0; i < field.Count; i++)
        {
            if (field[i] != null)
                field[i].Recall();
        }
    }

    public bool TryLakeSpot(TournamentBoatAgent asker, bool placeHere, out Vector3 spot)
    {
        spot = asker != null ? asker.transform.position : Vector3.zero;
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null || terrain.terrainData == null)
            return false;

        Vector3 origin = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        var rng = new System.Random(Random.Range(int.MinValue, int.MaxValue));
        Vector3 camp = site != null ? site.DockPosition : origin + size * 0.5f;
        Vector3 from = asker != null ? asker.transform.position : camp;
        Vector3 player = PlayerHull();

        for (int i = 0; i < 28; i++)
        {
            float x = origin.x + 40f + (float)rng.NextDouble() * Mathf.Max(80f, size.x - 80f);
            float z = origin.z + 40f + (float)rng.NextDouble() * Mathf.Max(80f, size.z - 80f);
            Vector3 candidate = new Vector3(x, 0f, z);
            if (!IsNavigable(candidate, minCruiseDepth))
                continue;
            if (DistanceXZ(candidate, camp) < 70f)
                continue;
            if (DistanceXZ(candidate, player) < 22f)
                continue;
            if (!placeHere && DistanceXZ(candidate, from) < 40f)
                continue;
            if (TooCloseToField(candidate, boatSpacing, asker))
                continue;

            spot = SnapHeight(candidate);
            return true;
        }

        return false;
    }

    public bool TryCampSpot(TournamentBoatAgent asker, out Vector3 spot)
    {
        spot = site != null ? site.DockPosition : Vector3.zero;
        if (site == null)
            return false;

        CacheWaterSide();
        Vector3 dock = site.DockPosition;
        Vector3 along = Vector3.Cross(Vector3.up, waterSide).normalized;
        int index = asker != null ? field.IndexOf(asker) : 0;
        if (index < 0)
            index = field.Count;

        for (int attempt = 0; attempt < 10; attempt++)
        {
            float row = 16f + (index % 4) * 5.5f + attempt * 2.4f;
            float side = ((index / 4) - 1.2f) * campSpacing + (attempt % 3 - 1) * 2.2f;
            Vector3 candidate = dock + waterSide * row + along * side;
            if (!IsNavigable(candidate, minCampDepth))
                continue;
            if (TooCloseToField(candidate, campSpacing * 0.75f, asker))
                continue;

            spot = SnapHeight(candidate);
            return true;
        }

        spot = SnapHeight(dock + waterSide * 18f);
        return IsNavigable(spot, minCampDepth * 0.6f);
    }

    public bool IsNavigable(Vector3 world, float minDepth = -1f)
    {
        if (minDepth < 0f)
            minDepth = minCruiseDepth;

        float depth = conditions != null
            ? conditions.GeometricDepthMeters(world)
            : lake != null ? lake.GeometricDepthMeters(world) : 0f;
        return depth >= minDepth;
    }

    GameObject ResolveBoatTemplate()
    {
        if (boatPrefab != null)
            return boatPrefab;

        BoatMotor[] boats = FindObjectsByType<BoatMotor>();
        for (int i = 0; i < boats.Length; i++)
        {
            if (boats[i] != null && boats[i].Boardable)
                return boats[i].gameObject;
        }

        return null;
    }

    GameObject SpawnAngler(Transform hull, string anglerName, int seed)
    {
        GameObject template = anglerPrefab != null
            ? anglerPrefab
            : GameObject.FindGameObjectWithTag("Player");
        if (template == null)
            return null;

        GameObject rider = Instantiate(template, hull);
        rider.name = anglerName;
        rider.tag = "Untagged";
        StripGameplay(rider);
        PoseOnHull(rider, hull);

        var look = rider.GetComponent<PlayerAppearance>();
        if (look != null)
            look.Apply(LookFor(seed));

        return rider;
    }

    static void StripGameplay(GameObject rider)
    {
        rider.tag = "Untagged";

        var inputs = rider.GetComponentsInChildren<PlayerInput>(true);
        for (int i = 0; i < inputs.Length; i++)
            DestroyComponent(inputs[i]);

        DestroyComponent(rider.GetComponent<PlayerFishing>());
        DestroyComponent(rider.GetComponent<PlayerBoatInteractor>());
        DestroyComponent(rider.GetComponent<PlayerProgress>());
        DestroyComponent(rider.GetComponent<TackleBox>());
        DestroyComponent(rider.GetComponent<PlayerOrbitCamera>());

        var motor = rider.GetComponent<PlayerMotor>();
        if (motor != null)
            motor.enabled = false;

        var controller = rider.GetComponent<CharacterController>();
        if (controller != null)
            controller.enabled = false;

        var cameras = rider.GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
            cameras[i].enabled = false;
    }

    static void DestroyComponent(Component component)
    {
        if (component == null)
            return;
        if (component is Behaviour behaviour)
            behaviour.enabled = false;
        Destroy(component);
    }

    static void PoseOnHull(GameObject rider, Transform hull)
    {
        var boat = hull.GetComponent<BoatMotor>();
        Transform stance = boat != null && boat.Seat != null ? boat.Seat : hull;
        rider.transform.SetParent(hull, false);
        rider.transform.localPosition = stance.localPosition;
        rider.transform.localRotation = stance.localRotation;
    }

    static AppearanceData LookFor(int seed)
    {
        var rng = new System.Random(seed);
        return new AppearanceData
        {
            skin = Skins[rng.Next(Skins.Length)],
            hat = Hats[rng.Next(Hats.Length)],
            vest = Vests[rng.Next(Vests.Length)],
            pockets = Pockets[rng.Next(Pockets.Length)]
        };
    }

    bool TrySpawnSlot(int index, int count, System.Random rng, out Vector3 spawnAt, out float yaw)
    {
        spawnAt = Vector3.zero;
        yaw = 0f;
        if (site == null)
            return false;

        CacheWaterSide();
        Vector3 dock = site.DockPosition;
        Vector3 along = Vector3.Cross(Vector3.up, waterSide).normalized;
        Vector3 player = PlayerHull();
        float row = 14f + (index % 3) * 5f;
        float spread = count <= 1 ? 0f : (index - (count - 1) * 0.5f) * 6.2f;
        Vector3 candidate = dock + waterSide * row + along * spread;

        for (int k = 0; k < 10; k++)
        {
            if (k > 0)
                candidate = dock + waterSide * (12f + k * 3f) + along * ((float)rng.NextDouble() - 0.5f) * 22f;
            if (!IsNavigable(candidate, minCampDepth))
                continue;
            if (DistanceXZ(candidate, player) < 7f)
                continue;

            spawnAt = SnapHeight(candidate);
            yaw = Quaternion.LookRotation(-waterSide, Vector3.up).eulerAngles.y;
            return true;
        }

        return false;
    }

    void CacheWaterSide()
    {
        if (laidOut || site == null)
            return;

        Vector3 dock = site.DockPosition;
        Vector3 best = Vector3.forward;
        float bestDepth = -1f;
        for (int i = 0; i < 12; i++)
        {
            float a = i * 30f * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float depth = conditions != null
                ? conditions.GeometricDepthMeters(dock + dir * 18f)
                : 0f;
            if (depth <= bestDepth)
                continue;
            bestDepth = depth;
            best = dir;
        }

        waterSide = best.sqrMagnitude > 0.01f ? best.normalized : Vector3.forward;
        laidOut = true;
    }

    Vector3 SnapHeight(Vector3 world)
    {
        if (conditions != null)
            world.y = conditions.WaterHeight;
        return world;
    }

    bool TooCloseToField(Vector3 point, float spacing, TournamentBoatAgent except)
    {
        for (int i = 0; i < field.Count; i++)
        {
            TournamentBoatAgent other = field[i];
            if (other == null || other == except)
                continue;
            if (DistanceXZ(point, other.transform.position) < spacing)
                return true;
        }

        return false;
    }

    static Vector3 PlayerHull()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return Vector3.zero;

        var boats = player.GetComponent<PlayerBoatInteractor>();
        if (boats != null && boats.OccupiedBoat != null)
            return boats.OccupiedBoat.transform.position;
        return player.transform.position;
    }

    static float DistanceXZ(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    static int Seed(string id, int dayIndex)
    {
        int hash = 17;
        if (!string.IsNullOrEmpty(id))
        {
            for (int i = 0; i < id.Length; i++)
                hash = hash * 31 + id[i];
        }

        return hash * 31 + dayIndex;
    }

    void Resolve()
    {
        if (director == null)
            director = GetComponent<TournamentDirector>() ?? FindFirstObjectByType<TournamentDirector>();
        if (conditions == null)
            conditions = GetComponent<WorldConditions>() ?? FindFirstObjectByType<WorldConditions>();
        if (site == null)
            site = FindFirstObjectByType<TournamentSite>();
        if (lake == null)
            lake = FindFirstObjectByType<LakeSimulation>();
    }

    void Hook()
    {
        if (hooked || director == null)
            return;

        director.PhaseChanged += OnPhaseChanged;
        hooked = true;
    }
}
