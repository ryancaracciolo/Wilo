using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Click and hold to drop the cast mark under the cursor. C aims along the
/// camera; steer with the arrows. Hold to reel; double-click to bring it all
/// the way in.
/// </summary>
[DefaultExecutionOrder(-20)]
public class PlayerFishing : MonoBehaviour
{
    public enum Phase
    {
        Idle,
        Aiming,
        Flying,
        InWater,
        Retrieving,
        Fighting,
        ShowingCatch
    }

    [Header("Cast")]
    [SerializeField] float minCastDistance = 4f;
    [SerializeField] float maxCastDistance = 54f;
    [SerializeField] float startCastDistance = 27f;
    [SerializeField] float yawDegreesPerPixel = 0.22f;
    [SerializeField] float distancePerPixel = 0.12f;
    [SerializeField] float keyboardAimPixelsPerSecond = 520f;
    [SerializeField] float maxYawOffset = 80f;
    [SerializeField] float flyDuration = 0.7f;
    [SerializeField] float retrieveSpeed = 7.5f;
    [SerializeField] float retrieveLiftDistance = 1.65f;
    [SerializeField] float doubleClickWindow = 0.32f;
    [SerializeField] float fullRetrieveSpeedMultiplier = 4f;
    [SerializeField] float maxLineSanityDistance = 90f;
    [SerializeField] float arcHeight = 3.15f;
    [SerializeField] Vector3 castOriginOffset = new Vector3(0f, 0.9f, 0.22f);

    /// <summary>
    /// How quickly a lure settles onto its ride depth once reeling starts. Fast
    /// enough that a tap of the reel reads as a pop rather than a long climb.
    /// </summary>
    const float RideTrackSpeed = 6f;
    const float FlingDuration = 0.42f;
    const float FlingRelease = 0.58f;

    /// <summary>Metres of bottom the lure feels out ahead of itself.</summary>
    const float BottomLookahead = 0.35f;

    /// <summary>How fast the lure rides up onto something it is about to reach.</summary>
    const float BottomClimbSpeed = 6f;

    /// <summary>How fat the lure is when it ticks a rock or stump.</summary>
    const float LureRadius = 0.1f;
    static readonly Collider[] CoverOverlap = new Collider[8];
    static readonly RaycastHit[] CoverHits = new RaycastHit[8];

    [Header("Lure")]
    [SerializeField] float lureClearance = 0.1f;
    [SerializeField] Color lineColor = new Color(0.93f, 0.9f, 0.82f, 0.9f);

    [Header("Rod")]
    [SerializeField] GameObject polePrefab;
    [SerializeField] float poleScale = 0.48f;
    [SerializeField] float poleThickness = 3.6f;
    [SerializeField] Vector3 poleHoldOffset = new Vector3(0.18f, 0.4f, 0.08f);
    [SerializeField] Vector3 poleCastHoldOffset = new Vector3(0.24f, 0.42f, -0.05f);
    [SerializeField] float polePitch = 14f;
    [SerializeField] float poleCastPitch = 6f;

    /// <summary>
    /// Where the blank points when the cast is loaded, as weights on the aim
    /// frame: x leans outboard, y lifts, z rocks back. Keep this mostly
    /// horizontal — the brim is wider than the arms can reach, so a high
    /// sweep goes through the hat.
    /// </summary>
    [SerializeField] Vector3 poleCastLean = new Vector3(0.95f, 0.12f, 0.5f);

    [Header("Catch")]
    [SerializeField] Vector3 catchHoldPos = new Vector3(-1f, 1.5f, 0f);

    [SerializeField, Range(0f, 0.8f)]
    float catchCameraOffset = 0.55f;

    /// <summary>Lower lip grip for two-hand holds so a long bass clears the hat.</summary>
    [SerializeField] Vector3 catchHoldTwoHandLocal = new Vector3(-0.18f, 0.54f, 0.28f);

    /// <summary>
    /// Head-to-tail in player space for two-hand holds. Keep Z near 0 so the
    /// tail sits as far out from the chest as the head.
    /// </summary>
    [SerializeField] Vector3 catchHangLocal = new Vector3(0.72f, -0.52f, 0f);

    [SerializeField, Range(0.3f, 0.7f)]
    float catchLipDist = 0.422f;
    [SerializeField, Range(0f, 0.08f)]
    float catchLipDrop = 0.0179f;
    [SerializeField, Range(0f, 0.2f)]
    float catchGripAlong = 0.127f;
    [SerializeField, Range(0f, 0.35f)]
    float catchGripOut = 0.238f;
    [SerializeField, Range(0.2f, 0.9f)]
    float catchSupportAlong = 0.52f;
    [SerializeField, Range(0f, 0.22f)]
    float catchSupportOut = 0.11f;
    [SerializeField, Range(0f, 0.14f)]
    float catchSupportDown = 0.06f;

    [Header("Reticle")]
    [SerializeField] Color liveColor = new Color(0.96f, 0.97f, 1f, 1f);
    [SerializeField] Color invalidColor = new Color(1f, 0.58f, 0.58f, 1f);

    public bool IsFishing => phase != Phase.Idle;
    public bool ShowingCatch => phase == Phase.ShowingCatch;
    public FishAgent ShownCatch => phase == Phase.ShowingCatch ? hooked : null;
    public bool CapturesArrowKeys => phase == Phase.Aiming;
    public FishFight Fight { get; private set; }
    public event System.Action Escaped;

    public void CancelCastClick()
    {
        ignoreCastFrame = Time.frameCount;
        if (phase == Phase.Aiming)
            CancelAim();
        else if (phase == Phase.Flying && flyTime < 0.2f)
            EndFishing();
    }

    PlayerMotor motor;
    PlayerBoatInteractor boatInteractor;
    PlayerProgress progress;
    LakeSimulation lake;
    LocalFishPopulation fishPopulation;
    TackleBox tackle;
    InputAction attackAction;
    Camera cam;
    PlayerOrbitCamera orbit;
    Transform waterSurface;
    float waterHeight;
    bool hasWaterHeight;

    Phase phase;
    GameObject lureObject;
    LurePlaceholder lureVisual;
    LureDefinition shownLure;
    LineRenderer line;
    Vector3[] lineBuffer;
    FishingPole pole;
    CastMarker liveMarker;
    Vector3 flyStart;
    Vector3 flyEnd;
    float flyTime;
    int aimStartFrame;
    Vector3 aimStartDir;
    float aimStartDistance;
    Vector3 pendingLanding;
    bool pendingValid;
    float reelRippleTraveled;
    float holdDepthY;
    float bottomFloorY;
    Vector2 keyboardAimOffset;
    bool aimHeldByKeyboard;
    bool retrieveAllTheWay;
    float lastCastPressTime = -10f;
    FishAgent hooked;
    FishFight fight;
    int ignoreCastFrame = -1;
    Quaternion catchFacingSaved;
    bool catchFacingStored;
    float flingTime;
    bool lureOnTip;
    float lastFishY;
    float fightSway;
    float fightSwayVel;
    float catchHoldWeight;

    /// <summary>True while a cast lure is riding in the water column.</summary>
    public bool LureInWater { get; private set; }

    /// <summary>How deep the lure is running, in gameplay feet.</summary>
    public float LureDepthFeet { get; private set; }

    /// <summary>Bed depth under the lure, in gameplay feet.</summary>
    public float LureBedFeet { get; private set; }

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        boatInteractor = GetComponent<PlayerBoatInteractor>();
        progress = GetComponent<PlayerProgress>();
        tackle = GetComponent<TackleBox>();
    }

    void Start()
    {
        HidePole();
    }

    void OnEnable()
    {
        var actions = InputSystem.actions;
        attackAction = actions != null ? actions.FindAction("Player/Attack") : null;
        attackAction?.Enable();
        CacheWaterHeight();
        lake = FindFirstObjectByType<LakeSimulation>();
        if (lake != null)
        {
            fishPopulation = lake.GetComponent<LocalFishPopulation>();
            LurePresence lure = lake.Lure ?? lake.GetComponent<LurePresence>() ?? lake.gameObject.AddComponent<LurePresence>();
            lure.Struck += OnFishStruck;
        }

        if (tackle != null)
            tackle.Changed += OnTackleChanged;
    }

    void Update()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam != null)
                orbit = cam.GetComponent<PlayerOrbitCamera>();
        }

        switch (phase)
        {
            case Phase.Idle:
                HideMarkers();
                if (WasCastPressed())
                    BeginAim();
                break;
            case Phase.Aiming:
                TickAim();
                break;
            case Phase.Flying:
                TickFly();
                break;
            case Phase.InWater:
                TickInWater();
                break;
            case Phase.Retrieving:
                TickRetrieve();
                break;
            case Phase.Fighting:
                TickFight();
                break;
            case Phase.ShowingCatch:
                TickShowingCatch();
                break;
        }

        UpdateLure();
    }

    void LateUpdate()
    {
        if (HudInput.AteWorldClick)
            CancelCastClick();

        TickPole();
        ReleaseLureFromTip();
        UpdateLine();
    }

    bool WasCastPressed()
    {
        // The key never routes through the HUD, so it stays live while the pointer
        // is merely busy up there — but not while a panel or text field owns input.
        if (!HudInput.BlocksWorldKeys && Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
            return true;

        if (Time.frameCount <= ignoreCastFrame)
            return false;

        if (HudInput.BlocksWorldClick)
            return false;

        if (attackAction != null && attackAction.WasPressedThisFrame())
            return true;
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    }

    bool WasCastReleased()
    {
        if (aimHeldByKeyboard)
            return Keyboard.current != null && Keyboard.current.cKey.wasReleasedThisFrame;

        if (attackAction != null && attackAction.WasReleasedThisFrame())
            return true;
        return Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
    }

    bool IsFightHeld()
    {
        if (IsCastHeld())
            return true;
        return !HudInput.Typing && Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
    }

    bool IsCastHeld()
    {
        // Held keys only stand down for text entry: a panel opened mid-cast or
        // mid-fight should not drop the line the player is already working.
        if (!HudInput.Typing && Keyboard.current != null && Keyboard.current.cKey.isPressed)
            return true;
        if (HudInput.AteWorldClick)
            return false;
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            return true;
        return attackAction != null && attackAction.IsPressed();
    }

    bool ConsumeDoubleClick()
    {
        if (!WasCastPressed())
            return false;

        bool doubled = Time.unscaledTime - lastCastPressTime <= doubleClickWindow;
        lastCastPressTime = Time.unscaledTime;
        return doubled;
    }

    Vector2 ArrowAim()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return Vector2.zero;

        Vector2 arrows = Vector2.zero;
        if (keyboard.leftArrowKey.isPressed)
            arrows.x -= 1f;
        if (keyboard.rightArrowKey.isPressed)
            arrows.x += 1f;
        if (keyboard.upArrowKey.isPressed)
            arrows.y += 1f;
        if (keyboard.downArrowKey.isPressed)
            arrows.y -= 1f;
        return arrows;
    }

    void BeginAim()
    {
        if (!hasWaterHeight)
            CacheWaterHeight();
        if (!hasWaterHeight || cam == null)
            return;

        aimStartFrame = Time.frameCount;
        keyboardAimOffset = Vector2.zero;
        aimHeldByKeyboard = Keyboard.current != null &&
            Keyboard.current.cKey.isPressed &&
            (Mouse.current == null || !Mouse.current.leftButton.isPressed);
        aimStartDir = Flatten(cam.transform.forward);
        aimStartDistance = startCastDistance;
        if (!aimHeldByKeyboard)
            SeedAimFromPointer();
        pendingValid = false;
        phase = Phase.Aiming;
        SetFishingLocked(true);
        EnsureMarkers();
        TickAim();
    }

    void SeedAimFromPointer()
    {
        Vector3 origin = transform.position;
        origin.y = waterHeight;
        if (!TryLandingFromPointer(origin, out Vector3 landing))
        {
            aimStartDistance = maxCastDistance;
            return;
        }

        Vector3 delta = landing - origin;
        delta.y = 0f;
        float mag = delta.magnitude;
        aimStartDir = mag > 0.15f ? delta / mag : aimStartDir;
        aimStartDistance = mag > 0.15f ? mag : maxCastDistance;
    }

    void TickAim()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CancelAim();
            return;
        }

        Vector3 origin = transform.position;
        origin.y = waterHeight;
        if (aimHeldByKeyboard)
            TickKeyboardAim(origin);
        else
            TickPointerAim(origin);

        Vector3 livePos = pendingLanding;
        livePos.y = waterHeight + 0.03f;
        pendingValid = IsWaterAt(livePos);

        SetLiveMarker(true, livePos);

        if (HudInput.AteWorldClick)
        {
            CancelAim();
            return;
        }

        if (!WasCastReleased() || Time.frameCount <= aimStartFrame)
            return;

        if (pendingValid)
            ReleaseCast();
        else
            CancelAim();
    }

    void TickPointerAim(Vector3 origin)
    {
        if (TryLandingFromPointer(origin, out Vector3 landing))
        {
            pendingLanding = landing;
            Vector3 delta = landing - origin;
            delta.y = 0f;
            float mag = delta.magnitude;
            if (mag > 0.15f)
            {
                aimStartDir = delta / mag;
                aimStartDistance = mag;
            }

            return;
        }

        Vector3 dir = Flatten(aimStartDir);
        float distance = Mathf.Clamp(aimStartDistance, minCastDistance, maxCastDistance);
        pendingLanding = origin + dir * distance;
        pendingLanding.y = waterHeight;
    }

    bool TryLandingFromPointer(Vector3 origin, out Vector3 landing)
    {
        landing = default;
        if (cam == null)
            return false;

        Ray ray = cam.ScreenPointToRay(CurrentMousePosition());
        var water = new Plane(Vector3.up, new Vector3(0f, waterHeight, 0f));
        if (!water.Raycast(ray, out float enter) || enter < 0.2f)
            return false;

        Vector3 hit = ray.GetPoint(enter);
        Vector3 delta = hit - origin;
        delta.y = 0f;
        float mag = delta.magnitude;
        if (mag < 0.15f)
            return false;

        float distance = Mathf.Clamp(mag, minCastDistance, maxCastDistance);
        landing = origin + (delta / mag) * distance;
        landing.y = waterHeight;
        return true;
    }

    void TickKeyboardAim(Vector3 origin)
    {
        keyboardAimOffset += ArrowAim() * keyboardAimPixelsPerSecond * Time.deltaTime;
        float yaw = Mathf.Clamp(keyboardAimOffset.x * yawDegreesPerPixel, -maxYawOffset, maxYawOffset);
        float distance = Mathf.Clamp(
            aimStartDistance + keyboardAimOffset.y * distancePerPixel,
            minCastDistance,
            maxCastDistance);

        Vector3 aimDir = Quaternion.AngleAxis(yaw, Vector3.up) * aimStartDir;
        pendingLanding = origin + aimDir * distance;
        pendingLanding.y = waterHeight;
    }

    Vector2 CurrentMousePosition()
    {
        if (Mouse.current != null)
            return Mouse.current.position.ReadValue();
        if (cam != null)
            return new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f);
        return Vector2.zero;
    }

    void ReleaseCast()
    {
        HideMarkers();
        EnsureLure();
        ApplyEquippedLure();
        flingTime = 0f;
        lureOnTip = true;
        flyTime = 0f;
        lureObject.SetActive(true);
        lureObject.transform.rotation = Quaternion.LookRotation(Flatten(pendingLanding - transform.position), Vector3.up);
        phase = Phase.Flying;
    }

    void CancelAim()
    {
        HideMarkers();
        phase = Phase.Idle;
        SetFishingLocked(false);
    }

    bool IsWaterAt(Vector3 point)
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null)
            return true;

        float groundY = terrain.SampleHeight(point) + terrain.transform.position.y;
        return groundY <= waterHeight - 0.05f;
    }

    void TickFly()
    {
        if (lureOnTip)
        {
            if (lureObject != null)
                lureObject.transform.position = RodTip();
            return;
        }

        flyTime += Time.deltaTime;
        float t = Mathf.Clamp01(flyTime / flyDuration);
        float eased = t * t * (3f - 2f * t);
        Vector3 pos = Vector3.Lerp(flyStart, flyEnd, eased);
        float distance = Vector3.Distance(flyStart, flyEnd);
        float arc = ArcHeight(distance);
        pos.y += Mathf.Sin(eased * Mathf.PI) * arc;
        lureObject.transform.position = pos;

        if (t >= 1f)
        {
            lureObject.transform.position = flyEnd;
            WaterRipples.Emit(flyEnd, WaterRippleKind.Cast);
            retrieveAllTheWay = false;
            reelRippleTraveled = 0f;
            phase = Phase.InWater;
            SetBoatLocked(false);
        }
    }

    void TickInWater()
    {
        if (RecoverFromBrokenLine())
            return;

        ApplySink();

        if (ConsumeDoubleClick())
        {
            retrieveAllTheWay = true;
            BeginRetrieve();
            return;
        }

        if (IsCastHeld())
            BeginRetrieve();
    }

    void BeginRetrieve()
    {
        // A hold-depth bait comes back at the depth it counted down to, so the
        // bottom can lift it over a rock and it still settles back afterwards.
        if (lureObject != null)
        {
            holdDepthY = lureObject.transform.position.y;
            bottomFloorY = BedY(lureObject.transform.position);
        }

        phase = Phase.Retrieving;
    }

    void TickRetrieve()
    {
        if (RecoverFromBrokenLine())
            return;

        if (ConsumeDoubleClick())
            retrieveAllTheWay = true;

        if (!retrieveAllTheWay && !IsCastHeld())
        {
            phase = Phase.InWater;
            return;
        }

        Vector3 rod = RodTip();
        Vector3 previous = lureObject.transform.position;

        Vector3 planar = new Vector3(previous.x, 0f, previous.z);
        Vector3 planarTarget = new Vector3(rod.x, 0f, rod.z);
        Vector3 travel = planarTarget - planar;
        planar = Vector3.MoveTowards(planar, planarTarget, CurrentRetrieveSpeed() * Time.deltaTime);
        planar = SlideOffCover(previous, planar);
        Vector3 slid = planar - new Vector3(previous.x, 0f, previous.z);
        if (slid.sqrMagnitude > 0.0001f)
            travel = slid;

        float remaining = Vector3.Distance(planar, planarTarget);
        float liftT = 1f - Mathf.Clamp01(remaining / retrieveLiftDistance);
        liftT = liftT * liftT * liftT;

        float floorY = BottomFloor(planar, travel);
        float y = RideY(previous.y, planar);
        if (liftT >= 0.05f)
            y = Mathf.Lerp(y, rod.y, liftT);

        if (liftT < 0.05f)
            y = Mathf.Clamp(y, floorY, waterHeight - 0.02f);
        else
            y = Mathf.Max(y, floorY);

        Vector3 next = new Vector3(planar.x, y, planar.z);
        lureObject.transform.position = next;
        OrientLure(planarTarget - planar);

        if (liftT < 0.2f && next.y > waterHeight - 0.4f)
            EmitReelRipples(previous, next, Flatten(planarTarget - planar));

        if (Vector3.Distance(next, rod) <= 0.28f)
            EndFishing();
    }

    void EmitReelRipples(Vector3 from, Vector3 to, Vector3 along)
    {
        Vector3 delta = to - from;
        delta.y = 0f;
        reelRippleTraveled += delta.magnitude;
        if (reelRippleTraveled < 0.48f)
            return;

        reelRippleTraveled = 0f;
        Vector3 side = Vector3.Cross(Vector3.up, along.sqrMagnitude > 0.0001f ? along.normalized : transform.forward);
        Vector3 splash = to;
        splash.y = waterHeight;
        WaterRipples.Emit(splash + side * 0.2f, WaterRippleKind.Reel);
        WaterRipples.Emit(splash - side * 0.2f, WaterRippleKind.Reel);
    }

    void OnFishStruck(FishAgent fish)
    {
        if (hooked != null || fish == null || lureObject == null)
            return;

        hooked = fish;
        fishPopulation?.Detach(fish);
        fish.Hook(lureObject.transform, transform);
        HudCues.Pulse(
            "strike",
            "!",
            transform,
            Vector3.up * (boatInteractor != null && boatInteractor.IsOnBoat ? 1.4f : 2.2f),
            0.7f);
        WaterRipples.Emit(lureObject.transform.position, WaterRippleKind.Cast);
        if (lake != null && lake.Lure != null)
            lake.Lure.Clear();
        fight = new FishFight();
        fight.Begin(fish.Size.Pounds);
        lastFishY = fight.FishY;
        fightSway = 0f;
        fightSwayVel = 0f;
        Fight = fight;
        retrieveAllTheWay = false;
        phase = Phase.Fighting;
        SetBoatLocked(true);
    }

    void TickFight()
    {
        if (RecoverFromBrokenLine())
            return;

        if (fight == null || hooked == null)
        {
            EndFishing();
            return;
        }

        FishFight.Result result = fight.Tick(IsFightHeld(), Time.deltaTime);
        if (result == FishFight.Result.Won)
            FinishFight(true);
        else if (result == FishFight.Result.Lost)
            FinishFight(false);
    }

    void TickShowingCatch()
    {
        FaceCatchCamera(10f);
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            DismissCatch();
    }

    /// <summary>Drops the line and returns to idle, for when the day ends mid-cast.</summary>
    public void AbortFishing()
    {
        if (phase != Phase.Idle)
            EndFishing();
    }

    public void DismissCatch()
    {
        if (phase != Phase.ShowingCatch)
            return;

        ignoreCastFrame = Time.frameCount;
        if (hooked != null)
        {
            fishPopulation?.Remove(hooked);
            hooked = null;
        }

        EndFishing();
    }

    void FinishFight(bool won)
    {
        fight = null;
        Fight = null;

        if (won && hooked != null)
        {
            LandCatch(hooked);
            if (lureObject != null)
                lureObject.SetActive(false);
            phase = Phase.ShowingCatch;
            BeginCatchCamera(hooked);
            return;
        }

        if (hooked != null)
        {
            fishPopulation?.Remove(hooked);
            hooked = null;
            Escaped?.Invoke();
        }

        EndFishing();
    }

    void UpdateLure()
    {
        if (lake == null || lake.Lure == null)
            return;

        bool inPlay = hooked == null &&
            (phase == Phase.InWater || phase == Phase.Retrieving) &&
            lureObject != null && lureObject.activeSelf;
        if (inPlay)
        {
            Vector3 at = lureObject.transform.position;
            lake.Lure.Set(at, Equipped());
            TrackLureDepth(at);
            return;
        }

        LureInWater = false;
        if (hooked == null)
            lake.Lure.Clear();
    }

    void TrackLureDepth(Vector3 at)
    {
        float scale = lake.Conditions != null ? lake.Conditions.GameplayDepthScale : 0.5f;
        LureInWater = at.y <= waterHeight + 0.05f;
        LureDepthFeet = Mathf.Max(0f, (waterHeight - at.y) * scale * 3.28084f);
        LureBedFeet = lake.DepthMeters(at) * 3.28084f;
    }

    void LandCatch(FishAgent fish)
    {
        if (fish == null)
            return;

        Vector3 at = fish.transform.position;
        WorldConditions world = lake != null ? lake.Conditions : null;
        LureDefinition lure = tackle != null ? tackle.Equipped : null;
        HabitatFeatures spot = lake != null ? lake.SampleFeatures(at) : default;

        var record = new CatchRecord
        {
            SpeciesName = fish.Species != null ? fish.Species.DisplayName : "Bass",
            Pounds = fish.Size.Pounds,
            LengthInches = fish.Size.LengthInches,
            LureName = lure != null ? lure.DisplayName : "Lure",
            LureColor = lure != null ? lure.Color : new Color(0.55f, 0.38f, 0.22f),
            WorldPosition = at,
            DepthFeet = spot.DepthFeet,
            DayIndex = world != null ? world.DayIndex : 0,
            Hour = world != null ? world.Hour : 0f,
            TimeLabel = world != null ? world.TimeLabel : "",
            WeatherLabel = world != null ? world.WeatherLabel : "",
            WaterTempF = world != null ? world.WaterTempF : 0f,
            SeasonLabel = world != null ? world.SeasonLabel : ""
        };

        if (progress == null)
            progress = GetComponent<PlayerProgress>();
        SaveCatchFacing();
        FaceCatchCamera(0f);
        progress?.RecordCatch(record);
        fish.PresentCatch(transform, CatchHoldPoint());
    }

    void SaveCatchFacing()
    {
        if (catchFacingStored)
            return;
        catchFacingSaved = transform.localRotation;
        catchFacingStored = true;
    }

    void RestoreCatchFacing()
    {
        if (!catchFacingStored)
            return;
        transform.localRotation = catchFacingSaved;
        catchFacingStored = false;
    }

    void FaceCatchCamera(float rate)
    {
        if (cam == null)
            cam = Camera.main;
        if (cam == null)
            return;

        Vector3 toCam = cam.transform.position - transform.position;
        toCam.y = 0f;
        if (toCam.sqrMagnitude < 0.0001f)
        {
            toCam = -cam.transform.forward;
            toCam.y = 0f;
        }

        if (toCam.sqrMagnitude < 0.0001f)
            return;

        Quaternion look = Quaternion.LookRotation(toCam.normalized, Vector3.up);
        if (rate <= 0.01f)
            transform.rotation = look;
        else
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                look,
                1f - Mathf.Exp(-rate * Time.deltaTime));
    }

    void BeginCatchCamera(FishAgent fish)
    {
        if (orbit == null && cam != null)
            orbit = cam.GetComponent<PlayerOrbitCamera>();
        orbit?.BeginCatchFraming(fish != null ? fish.transform : null);
    }

    void EndCatchCamera()
    {
        if (orbit == null && cam != null)
            orbit = cam.GetComponent<PlayerOrbitCamera>();
        orbit?.EndCatchFraming();
    }

    void EndFishing()
    {
        if (phase == Phase.ShowingCatch)
        {
            EndCatchCamera();
            RestoreCatchFacing();
        }

        if (hooked != null)
        {
            fishPopulation?.Remove(hooked);
            hooked = null;
        }

        fight = null;
        Fight = null;

        if (lake != null && lake.Lure != null)
            lake.Lure.Clear();

        retrieveAllTheWay = false;
        phase = Phase.Idle;
        HideMarkers();
        lureOnTip = false;
        HidePole();
        flingTime = 0f;
        fightSway = 0f;
        fightSwayVel = 0f;
        catchHoldWeight = 0f;
        if (lureObject != null)
            lureObject.SetActive(false);
        SetFishingLocked(false);
    }

    /// <summary>
    /// Locks foot movement for the whole cast. Boat throttle only locks while
    /// aiming or fighting, so the hull can still be moved with a lure in the water.
    /// </summary>
    void SetFishingLocked(bool locked)
    {
        SetBoatLocked(locked);
        if (boatInteractor != null && boatInteractor.IsOnBoat)
            return;

        if (motor != null)
            motor.enabled = !locked;
    }

    void SetBoatLocked(bool locked)
    {
        if (boatInteractor == null || !boatInteractor.IsOnBoat)
            return;

        BoatMotor boat = GetComponentInParent<BoatMotor>();
        if (boat != null)
            boat.ControlsLocked = locked;
    }

    void TickPole()
    {
        bool showing = phase == Phase.ShowingCatch;
        bool held = phase != Phase.Idle && !showing;
        catchHoldWeight = Mathf.MoveTowards(catchHoldWeight, showing ? 1f : 0f, Time.deltaTime * 8f);

        if (!held)
            HidePole();
        else
        {
            EnsurePole();
            FaceFishingTarget(FacePoint());
            if (phase == Phase.Flying)
                flingTime += Time.deltaTime;

            if (pole != null)
            {
                AdvanceFightSway(out float sway, out float load);
                float stage = Mathf.Clamp01(flingTime / FlingDuration);
                pole.Tick(true, BuildPoleMotion(phase, PoleAimPoint(), stage, sway, load, IsFightHeld()));
            }
        }

        if (showing || catchHoldWeight > 0.01f)
        {
            if (pole == null)
                EnsurePole();
            PoseCatchHold();
        }
    }

    Vector3 CatchHoldPoint()
    {
        return transform.TransformPoint(catchHoldPos) + transform.forward * catchCameraOffset;
    }

    Vector3 CatchHangDirection()
    {
        if (hooked == null || !hooked.WantsTwoHandHold)
            return Vector3.down;

        Vector3 local = catchHangLocal.sqrMagnitude > 0.01f
            ? catchHangLocal
            : new Vector3(0.72f, -0.52f, 0f);
        Vector3 world = transform.TransformDirection(local);
        return world.sqrMagnitude > 0.0001f ? world.normalized : Vector3.down;
    }

    void PoseCatchHold()
    {
        Vector3 hang = CatchHangDirection();
        Vector3 hold = CatchHoldPoint();
        Vector3 lip = hold;
        if (pole != null)
            lip = pole.PoseCatchHold(hold, hang, catchHoldWeight, catchGripAlong, catchGripOut);
        if (hooked == null)
            return;

        hooked.ApplyCatchFit(catchLipDist, catchLipDrop, catchSupportAlong, catchSupportOut, catchSupportDown);
        hooked.HangFromLip(lip, transform, hang);
        if (pole != null && hooked.WantsTwoHandHold)
            pole.PoseCatchSupport(hooked.CatchSupportPoint, hang, catchHoldWeight);
    }

    /// <summary>
    /// Rod pose for a phase, derived only from its arguments so the editor
    /// preview poses the rod exactly the way gameplay does. <paramref name="castStage"/>
    /// runs 0 at the top of the backcast to 1 once the rod has come through.
    /// </summary>
    public FishingPole.Motion BuildPoleMotion(
        Phase forPhase, Vector3 aimPoint, float castStage, float sway, float load, bool fightHeld)
    {
        float fling = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(castStage));

        var motion = new FishingPole.Motion
        {
            AimPoint = aimPoint,
            HoldLocal = poleHoldOffset,
            Scale = poleScale,
            Thickness = poleThickness,
            Pitch = polePitch,
            CastLean = poleCastLean,
            LeftHand = 1f
        };

        switch (forPhase)
        {
            case Phase.Aiming:
                motion.HoldLocal = poleCastHoldOffset;
                motion.Pitch = poleCastPitch;
                motion.BackCast = 1f;
                motion.LeftHand = 0.35f;
                motion.Bend = 0.1f;
                break;
            case Phase.Flying:
                motion.HoldLocal = Vector3.Lerp(poleCastHoldOffset, poleHoldOffset, fling);
                motion.Pitch = Mathf.Lerp(poleCastPitch, polePitch, fling);
                motion.BackCast = 1f - fling;
                motion.LeftHand = Mathf.Lerp(0.35f, 1f, fling);
                motion.Bend = 0.08f + 4f * fling * (1f - fling) * 0.4f;
                break;
            case Phase.Retrieving:
            {
                float dist = Vector3.Distance(aimPoint, transform.position);
                motion.HoldLocal = poleHoldOffset + new Vector3(0.01f, 0.02f, 0.03f);
                motion.Pitch = Mathf.Lerp(24f, polePitch, Mathf.InverseLerp(1.4f, 14f, dist));
                motion.Bend = 0.14f;
                break;
            }
            case Phase.Fighting:
            {
                float dist = Vector3.Distance(aimPoint, transform.position);
                motion.HoldLocal = poleHoldOffset + new Vector3(sway * 0.025f, fightHeld ? 0.06f : 0.02f, 0.04f);
                motion.Pitch = Mathf.Lerp(26f, polePitch + 4f, Mathf.InverseLerp(1.4f, 14f, dist));
                motion.Pitch += fightHeld ? 8f : -4f;
                motion.Yaw = sway * 10f;
                motion.Bend = Mathf.Clamp01(load * (fightHeld ? 1.05f : 0.78f));
                break;
            }
            case Phase.InWater:
                motion.Bend = 0.06f;
                break;
        }

        return motion;
    }

    /// <summary>Tracks how hard the fish is pulling and which way the rod leans.</summary>
    void AdvanceFightSway(out float sway, out float load)
    {
        if (fight == null)
        {
            sway = 0f;
            load = 0.35f;
            return;
        }

        float dart = Mathf.Abs(fight.FishY - lastFishY) / Mathf.Max(Time.deltaTime, 0.0001f);
        lastFishY = fight.FishY;
        load = 0.38f + fight.Size * 0.42f;
        if (dart > 0.7f)
            load = Mathf.Min(1f, load + 0.28f);

        float targetSway = Mathf.Sin(Time.time * 1.15f) * 0.4f
            + Mathf.Sin(Time.time * 2.35f) * 0.22f
            + (fight.FishY - 0.5f) * 0.55f;
        if (dart > 0.7f)
            targetSway += Mathf.Sign(Mathf.Sin(Time.time * 9f + fight.Size)) * 0.45f;

        fightSway = Mathf.SmoothDamp(fightSway, Mathf.Clamp(targetSway, -1f, 1f), ref fightSwayVel, 0.16f);
        sway = fightSway;
    }

    void ReleaseLureFromTip()
    {
        if (!lureOnTip || phase != Phase.Flying || lureObject == null)
            return;

        Vector3 tip = RodTip();
        lureObject.transform.position = tip;
        if (flingTime < FlingDuration * FlingRelease)
            return;

        lureOnTip = false;
        flyStart = tip;
        flyEnd = pendingLanding;
        flyTime = 0f;
        lureObject.transform.rotation = Quaternion.LookRotation(Flatten(flyEnd - flyStart), Vector3.up);
    }

    Vector3 PoleAimPoint()
    {
        if (phase == Phase.Aiming || phase == Phase.Flying)
            return pendingLanding;
        if (phase == Phase.Fighting && hooked != null)
            return hooked.LinePoint;
        if (lureObject != null && lureObject.activeSelf && !lureOnTip)
            return lureObject.transform.position;
        return transform.position + transform.forward * 8f;
    }

    /// <summary>
    /// Body facing during the swing stays on the landing. Aiming the torso at a
    /// lure still clipped to the tip is what sent the angler around in a circle.
    /// </summary>
    Vector3 FacePoint()
    {
        if (phase == Phase.Aiming || phase == Phase.Flying)
            return pendingLanding;
        return PoleAimPoint();
    }

    Vector3 RodTip()
    {
        if (pole != null && pole.gameObject.activeInHierarchy)
            return pole.TipPosition;
        return transform.TransformPoint(castOriginOffset);
    }

    void FaceFishingTarget(Vector3 world)
    {
        Vector3 to = world - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.25f)
            return;

        Quaternion look = Quaternion.LookRotation(to.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            look,
            1f - Mathf.Exp(-4.5f * Time.deltaTime));
    }

    void UpdateLine()
    {
        if (line == null)
            return;

        if (phase == Phase.Idle || phase == Phase.ShowingCatch)
        {
            line.enabled = false;
            return;
        }

        int rodCount = 0;
        if (pole != null && pole.gameObject.activeInHierarchy)
        {
            EnsureLineBuffer();
            rodCount = pole.CopyPath(lineBuffer);
        }

        Vector3 tip = rodCount > 0 ? lineBuffer[rodCount - 1] : RodTip();
        Vector3 end;
        bool arc;
        if (phase == Phase.Aiming)
        {
            end = pendingLanding;
            arc = true;
        }
        else if (lureObject == null || !lureObject.activeSelf)
        {
            line.enabled = false;
            return;
        }
        else if (phase == Phase.Fighting && hooked != null)
        {
            end = hooked.LinePoint;
            arc = false;
        }
        else if (phase == Phase.Flying)
        {
            end = lureObject.transform.position;
            arc = true;
        }
        else
        {
            end = lureObject.transform.position;
            arc = false;
        }

        SetLineFromRod(rodCount, tip, end, arc);
    }

    void SetLineFromRod(int rodCount, Vector3 tip, Vector3 end, bool arc)
    {
        const int extra = 12;
        int prefix = rodCount > 0 ? rodCount : 1;
        int total = prefix + extra;
        EnsureLinePoints(total);

        Vector3 from;
        int start;
        if (rodCount > 0)
        {
            for (int i = 0; i < rodCount; i++)
                line.SetPosition(i, lineBuffer[i]);
            from = lineBuffer[rodCount - 1];
            start = rodCount;
        }
        else
        {
            from = tip;
            line.SetPosition(0, from);
            start = 1;
        }

        float distance = Vector3.Distance(from, end);
        float bow = arc
            ? ArcHeight(distance)
            : Mathf.Min(0.35f, distance * 0.04f);
        bool pinToWater = !arc && phase != Phase.Fighting && end.y >= waterHeight - 0.04f;
        int remaining = total - start;
        for (int i = 0; i < remaining; i++)
        {
            float t = remaining <= 1 ? 1f : (i + 1) / (float)remaining;
            Vector3 point = Vector3.Lerp(from, end, t);
            float wave = Mathf.Sin(t * Mathf.PI) * bow;
            point.y += arc ? wave : -wave;
            if (pinToWater && point.y < waterHeight + 0.02f)
                point.y = waterHeight + 0.02f;
            line.SetPosition(start + i, point);
        }
    }

    void EnsureLineBuffer()
    {
        int needed = pole != null ? Mathf.Max(8, pole.PathCount) : 8;
        if (lineBuffer == null || lineBuffer.Length < needed)
            lineBuffer = new Vector3[needed];
    }

    void EnsureLinePoints(int points)
    {
        line.enabled = true;
        if (line.positionCount != points)
            line.positionCount = points;
    }

    float ArcHeight(float distance)
    {
        return Mathf.Lerp(1.45f, arcHeight, Mathf.InverseLerp(minCastDistance, maxCastDistance, distance));
    }

    void HidePole()
    {
        if (pole != null)
            pole.PutAway();

        foreach (var stray in GetComponentsInChildren<FishingPole>(true))
        {
            if (stray != null && stray != pole)
                stray.gameObject.SetActive(false);
        }
    }

    void EnsurePole()
    {
        if (pole != null)
            return;

#if UNITY_EDITOR
        if (polePrefab == null)
            polePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(FishingPole.PrefabPath);
#endif
        if (polePrefab == null)
            return;

        pole = FishingPole.Spawn(polePrefab, transform, GetComponentInChildren<Animator>());
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor preview hook. The rod caches bone and marker references that a
    /// domain reload wipes, so tuning always starts from a fresh spawn.
    /// </summary>
    public FishingPole EditorRebuildPole()
    {
        foreach (var stale in GetComponentsInChildren<FishingPole>(true))
            DestroyImmediate(stale.gameObject);

        pole = null;
        EnsurePole();
        return pole;
    }
#endif

    void EnsureLure()
    {
        if (lureObject != null)
            return;

        lureObject = new GameObject("Lure");
        lureObject.SetActive(false);
        lureVisual = lureObject.AddComponent<LurePlaceholder>();

        var lineGo = new GameObject("FishingLine");
        lineGo.transform.SetParent(transform, false);
        line = lineGo.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.widthMultiplier = 0.018f;
        line.useWorldSpace = true;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.material = new Material(FindUnlitShader());
        ApplyColor(line.material, lineColor);
        line.startColor = lineColor;
        line.endColor = lineColor;
    }

    void OnTackleChanged()
    {
        if (phase == Phase.Idle || phase == Phase.Aiming || phase == Phase.ShowingCatch)
            return;
        ApplyEquippedLure();
    }

    void ApplyEquippedLure()
    {
        EnsureLure();
        LureDefinition lure = tackle != null ? tackle.Equipped : null;
        if (lure == shownLure && lureObject.transform.childCount > 0)
            return;
        shownLure = lure;
        lureVisual.Apply(lure);
    }

    void ApplySink()
    {
        if (lureObject == null)
            return;

        Vector3 pos = lureObject.transform.position;
        float restY = RestY(pos);
        float nextY = Mathf.Clamp(pos.y - CurrentSinkSpeed() * Time.deltaTime, restY, waterHeight - 0.02f);
        pos.y = nextY;
        lureObject.transform.position = pos;
    }

    void OrientLure(Vector3 planarDelta)
    {
        if (lureObject == null || planarDelta.sqrMagnitude < 0.0001f)
            return;

        Quaternion look = Quaternion.LookRotation(planarDelta.normalized, Vector3.up);
        lureObject.transform.rotation = Quaternion.Slerp(
            lureObject.transform.rotation,
            look,
            1f - Mathf.Exp(-8f * Time.deltaTime));
    }

    LureDefinition Equipped() => tackle != null ? tackle.Equipped : null;

    /// <summary>
    /// Where the lure wants to sit in the column while it is being reeled.
    /// Holding depth means a countdown decides the running depth; the other
    /// rides pin themselves to the bed, a set band, or the surface.
    /// </summary>
    float RideY(float currentY, Vector3 planar)
    {
        LureDefinition lure = Equipped();
        if (lure == null)
            return currentY;

        float step = RideTrackSpeed * Time.deltaTime;
        switch (lure.Ride)
        {
            case LureRide.Bottom:
                // Reeling lifts a bottom bait and letting go drops it, so tapping
                // the reel hops it along instead of dragging it the whole way.
                return Mathf.MoveTowards(currentY, RestY(planar) + RideOffsetMeters(lure.HopFeet), step);
            case LureRide.Surface:
                return Mathf.MoveTowards(currentY, waterHeight - 0.02f, step);
            case LureRide.FixedBand:
                return Mathf.MoveTowards(currentY, waterHeight - RideOffsetMeters(lure.RideDepthFeet), step);
            default:
                return Mathf.MoveTowards(currentY, holdDepthY, step);
        }
    }

    /// <summary>
    /// Tick off the side of rock and timber. Tops and gentle slopes stay a
    /// floor so the lure can ride over them; a wall slides the retrieve around
    /// the real mesh instead of a bounds-sized halo.
    /// </summary>
    Vector3 SlideOffCover(Vector3 from, Vector3 planar)
    {
        int mask = WorldConditions.StructureMask;
        if (mask == 0)
            return planar;

        Vector3 at = from;
        int overlapped = Physics.OverlapSphereNonAlloc(at, LureRadius, CoverOverlap, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlapped; i++)
        {
            Collider col = CoverOverlap[i];
            if (col == null)
                continue;

            Vector3 closest = col.ClosestPoint(at);
            Vector3 push = at - closest;
            if (push.sqrMagnitude < 0.000001f)
                push = Vector3.ProjectOnPlane(at - col.bounds.center, Vector3.up);
            if (push.sqrMagnitude < 0.000001f)
                continue;

            push.y = 0f;
            if (push.sqrMagnitude < 0.000001f)
                continue;

            at += push.normalized * (LureRadius - push.magnitude + 0.01f);
        }

        Vector3 desired = new Vector3(planar.x, at.y, planar.z);
        Vector3 delta = desired - at;
        delta.y = 0f;
        float dist = delta.magnitude;
        if (dist > 0.0001f)
        {
            int hits = Physics.SphereCastNonAlloc(
                at,
                LureRadius,
                delta / dist,
                CoverHits,
                dist,
                mask,
                QueryTriggerInteraction.Ignore);
            RaycastHit best = default;
            bool found = false;
            for (int i = 0; i < hits; i++)
            {
                RaycastHit hit = CoverHits[i];
                if (hit.collider == null)
                    continue;
                if (hit.normal.y > 0.65f)
                    continue;
                if (!found || hit.distance < best.distance)
                {
                    best = hit;
                    found = true;
                }
            }

            if (found)
            {
                Vector3 n = best.normal;
                n.y = 0f;
                if (n.sqrMagnitude > 0.0001f)
                {
                    n.Normalize();
                    Vector3 leftover = delta.normalized * Mathf.Max(0f, dist - best.distance);
                    Vector3 slide = Vector3.ProjectOnPlane(leftover, n);
                    at = best.point + n * LureRadius + slide;
                }
            }
            else
            {
                at = desired;
            }
        }

        return new Vector3(at.x, 0f, at.z);
    }

    /// <summary>
    /// The shallowest the lure may run right now. Rock and timber ahead start
    /// lifting it before it arrives, so an obstruction reads as the lure riding
    /// up and over instead of clipping through and popping out the far side.
    /// </summary>
    float BottomFloor(Vector3 planar, Vector3 travel)
    {
        float here = BedY(planar);
        if (travel.sqrMagnitude > 0.0001f)
        {
            float ahead = BedY(planar + travel.normalized * BottomLookahead);
            bottomFloorY = Mathf.MoveTowards(bottomFloorY, ahead, BottomClimbSpeed * Time.deltaTime);
        }

        return Mathf.Max(here, bottomFloorY);
    }

    float CurrentRetrieveSpeed()
    {
        LureDefinition lure = Equipped();
        float speed = retrieveSpeed * (lure != null ? lure.RetrieveScale : 1f);
        return retrieveAllTheWay ? speed * fullRetrieveSpeedMultiplier : speed;
    }

    bool RecoverFromBrokenLine()
    {
        if (phase == Phase.Idle || phase == Phase.Aiming || phase == Phase.ShowingCatch)
            return false;

        Vector3 end;
        if (phase == Phase.Fighting && hooked != null)
            end = hooked.LinePoint;
        else if (lureObject != null && lureObject.activeSelf)
            end = lureObject.transform.position;
        else
            return false;

        if (EndpointIsBroken(end))
        {
            Debug.LogWarning($"PlayerFishing recovered broken line endpoint in phase {phase} at {end}.");
            EndFishing();
            return true;
        }

        Vector3 rod = RodTip();
        float distance = Vector3.Distance(rod, end);
        if (distance <= maxLineSanityDistance)
            return false;

        Debug.LogWarning($"PlayerFishing recovered runaway line distance of {distance:F1}m in phase {phase}.");
        EndFishing();
        return true;
    }

    static bool EndpointIsBroken(Vector3 value)
    {
        return !IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z);
    }

    static bool IsFinite(float value)
    {
        return !(float.IsNaN(value) || float.IsInfinity(value));
    }

    /// <summary>Where the lure settles when nobody is reeling. Bottom rides keep their clearance.</summary>
    float RestY(Vector3 world)
    {
        float bedY = BedY(world);
        LureDefinition lure = Equipped();
        if (lure != null && lure.Ride == LureRide.Bottom)
            bedY += RideOffsetMeters(lure.RideDepthFeet);
        return bedY;
    }

    /// <summary>Ride offsets are authored in gameplay feet; the world runs on true metres.</summary>
    float RideOffsetMeters(float feet)
    {
        if (feet <= 0f)
            return 0f;

        float scale = lake != null && lake.Conditions != null ? lake.Conditions.GameplayDepthScale : 0.5f;
        return feet / 3.28084f / Mathf.Max(0.05f, scale);
    }

    float CurrentSinkSpeed()
    {
        LureDefinition lure = Equipped();
        float rate = lure != null ? lure.SinkSpeed : 0.35f;

        // The bed sits at true geometric depth while the player reads gameplay
        // feet, so fall fast enough to match the depth shown on the sonar.
        float scale = lake != null && lake.Conditions != null
            ? lake.Conditions.GameplayDepthScale
            : 0.5f;
        return rate / Mathf.Clamp(scale, 0.15f, 1f);
    }

    float BedY(Vector3 world)
    {
        float depth = lake != null ? lake.LureBottomMeters(world) : 2f;
        float bedY = waterHeight - depth;
        if (depth < 0.05f)
        {
            // Nothing wet above the bottom here, which also covers rock standing
            // clear of the water: fall back to the bare terrain under it.
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null)
                bedY = terrain.SampleHeight(world) + terrain.transform.position.y;
        }

        // Clearance must never lift the lure out of the water where the bottom
        // comes right up to the surface.
        return Mathf.Min(bedY + lureClearance, waterHeight - 0.05f);
    }

    void EnsureMarkers()
    {
        EnsureLure();
        if (liveMarker == null)
            liveMarker = CastMarker.Create("CastLive", 0.42f, liveColor);
    }

    void SetLiveMarker(bool visible, Vector3 livePos)
    {
        EnsureMarkers();
        liveMarker.SetVisible(visible);
        if (!visible)
            return;

        float pulse = 1f + 0.025f * Mathf.Sin(Time.time * 6f);
        liveMarker.SetPosition(livePos);
        liveMarker.SetScale(pulse);
        liveMarker.SetColor(pendingValid ? liveColor : invalidColor);
    }

    void HideMarkers()
    {
        liveMarker?.SetVisible(false);
    }

    static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        return value.sqrMagnitude > 0.001f ? value.normalized : Vector3.forward;
    }

    static Shader FindUnlitShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        return shader != null ? shader : Shader.Find("Sprites/Default");
    }

    static void ApplyColor(Material material, Color color)
    {
        if (material == null)
            return;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    void CacheWaterHeight()
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
        hasWaterHeight = true;
    }

    void OnDisable()
    {
        if (tackle != null)
            tackle.Changed -= OnTackleChanged;
        if (lake != null && lake.Lure != null)
            lake.Lure.Struck -= OnFishStruck;
        EndFishing();
    }

    sealed class CastMarker
    {
        readonly GameObject root;
        readonly LineRenderer ring;
        readonly Material centerMaterial;

        CastMarker(GameObject root, LineRenderer ring, Material centerMaterial)
        {
            this.root = root;
            this.ring = ring;
            this.centerMaterial = centerMaterial;
        }

        public static CastMarker Create(string name, float radius, Color color)
        {
            var root = new GameObject(name);

            var ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(root.transform, false);
            var ring = ringGo.AddComponent<LineRenderer>();
            ring.loop = true;
            ring.useWorldSpace = false;
            ring.alignment = LineAlignment.TransformZ;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.widthMultiplier = 0.04f;
            ring.positionCount = 28;
            ring.material = new Material(FindUnlitShader());
            ring.material.renderQueue = 3100;
            ApplyColor(ring.material, color);
            ring.startColor = color;
            ring.endColor = color;
            for (int i = 0; i < 28; i++)
            {
                float angle = i / 28f * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0.012f, Mathf.Sin(angle) * radius));
            }

            var center = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            center.name = "Center";
            center.transform.SetParent(root.transform, false);
            center.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            center.transform.localScale = new Vector3(0.11f, 0.004f, 0.11f);
            Object.Destroy(center.GetComponent<Collider>());
            ConfigureSurfaceRenderer(center.GetComponent<MeshRenderer>());

            Material centerMat = MakeMarkerMaterial(color, 1.2f);
            center.GetComponent<MeshRenderer>().sharedMaterial = centerMat;

            root.SetActive(false);
            return new CastMarker(root, ring, centerMat);
        }

        static void ConfigureSurfaceRenderer(MeshRenderer renderer)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        }

        static Material MakeMarkerMaterial(Color color, float emission)
        {
            var mat = new Material(FindUnlitShader());
            Color opaque = color;
            opaque.a = 1f;
            ApplyColor(mat, opaque);
            mat.renderQueue = 3100;
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", opaque * emission);
            }

            return mat;
        }

        public void SetVisible(bool visible)
        {
            // Scene teardown can destroy the marker before OnDisable runs.
            if (root != null && root.activeSelf != visible)
                root.SetActive(visible);
        }

        public void SetPosition(Vector3 world)
        {
            if (root != null)
                root.transform.position = world;
        }

        public void SetScale(float scale)
        {
            root.transform.localScale = Vector3.one * scale;
        }

        public void SetColor(Color color)
        {
            Color opaque = color;
            opaque.a = 1f;
            ApplyColor(centerMaterial, opaque);
            if (centerMaterial.HasProperty("_EmissionColor"))
                centerMaterial.SetColor("_EmissionColor", opaque * 1.2f);
            if (ring != null)
            {
                ApplyColor(ring.material, opaque);
                ring.startColor = opaque;
                ring.endColor = opaque;
            }
        }
    }
}
