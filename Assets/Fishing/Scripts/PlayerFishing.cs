using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Click-and-drag cast: left click or C to start, then steer with the mouse
/// or arrows. Hold to reel; double-click to bring it all the way in.
/// </summary>
public class PlayerFishing : MonoBehaviour
{
    enum Phase
    {
        Idle,
        Aiming,
        Flying,
        InWater,
        Retrieving
    }

    [Header("Cast")]
    [SerializeField] float minCastDistance = 4f;
    [SerializeField] float maxCastDistance = 36f;
    [SerializeField] float startCastDistance = 18f;
    [SerializeField] float yawDegreesPerPixel = 0.22f;
    [SerializeField] float distancePerPixel = 0.12f;
    [SerializeField] float keyboardAimPixelsPerSecond = 520f;
    [SerializeField] float lureSteerSpeed = 3.4f;
    [SerializeField] float maxYawOffset = 80f;
    [SerializeField] float flyDuration = 0.7f;
    [SerializeField] float retrieveSpeed = 9f;
    [SerializeField] float retrieveLiftDistance = 1.65f;
    [SerializeField] float doubleClickWindow = 0.32f;
    [SerializeField] float arcHeight = 3.15f;
    [SerializeField] Vector3 castOriginOffset = new Vector3(0f, 1.15f, 0.2f);

    [Header("Bobber")]
    [SerializeField] float bobberRadius = 0.12f;
    [SerializeField] Color bobberColor = new Color(0.92f, 0.28f, 0.18f);
    [SerializeField] Color lineColor = new Color(0.93f, 0.9f, 0.82f, 0.9f);

    [Header("Reticle")]
    [SerializeField] Color liveColor = new Color(0.96f, 0.97f, 1f, 1f);
    [SerializeField] Color invalidColor = new Color(1f, 0.58f, 0.58f, 1f);

    public bool IsFishing => phase != Phase.Idle;

    PlayerMotor motor;
    PlayerBoatInteractor boatInteractor;
    InputAction attackAction;
    Camera cam;
    Transform waterSurface;
    float waterHeight;
    bool hasWaterHeight;

    Phase phase;
    GameObject bobber;
    LineRenderer line;
    CastMarker liveMarker;
    Vector3 flyStart;
    Vector3 flyEnd;
    float flyTime;
    int aimStartFrame;
    Vector2 aimMouseOrigin;
    Vector3 aimStartDir;
    Vector3 pendingLanding;
    bool pendingValid;
    float reelRippleTraveled;
    Vector2 keyboardAimOffset;
    bool aimHeldByKeyboard;
    bool retrieveAllTheWay;
    float lastCastPressTime = -10f;

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        boatInteractor = GetComponent<PlayerBoatInteractor>();
    }

    void OnEnable()
    {
        var actions = InputSystem.actions;
        attackAction = actions != null ? actions.FindAction("Player/Attack") : null;
        attackAction?.Enable();
        CacheWaterHeight();
    }

    void Update()
    {
        if (cam == null)
            cam = Camera.main;

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
        }

        UpdateLine();
    }

    bool WasCastPressed()
    {
        if (attackAction != null && attackAction.WasPressedThisFrame())
            return true;
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            return true;
        return Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame;
    }

    bool WasCastReleased()
    {
        if (aimHeldByKeyboard)
            return Keyboard.current != null && Keyboard.current.cKey.wasReleasedThisFrame;

        if (attackAction != null && attackAction.WasReleasedThisFrame())
            return true;
        return Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
    }

    bool IsCastHeld()
    {
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            return true;
        if (Keyboard.current != null && Keyboard.current.cKey.isPressed)
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
        aimMouseOrigin = CurrentMousePosition();
        keyboardAimOffset = Vector2.zero;
        aimHeldByKeyboard = Keyboard.current != null &&
            Keyboard.current.cKey.isPressed &&
            (Mouse.current == null || !Mouse.current.leftButton.isPressed);
        aimStartDir = Flatten(cam.transform.forward);
        pendingValid = false;
        phase = Phase.Aiming;
        SetFishingLocked(true);
        EnsureMarkers();
        TickAim();
    }

    void TickAim()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CancelAim();
            return;
        }

        keyboardAimOffset += ArrowAim() * keyboardAimPixelsPerSecond * Time.deltaTime;
        Vector2 drag = CurrentMousePosition() - aimMouseOrigin + keyboardAimOffset;
        float yaw = Mathf.Clamp(-drag.x * yawDegreesPerPixel, -maxYawOffset, maxYawOffset);
        float distance = Mathf.Clamp(
            startCastDistance - drag.y * distancePerPixel,
            minCastDistance,
            maxCastDistance);

        Vector3 origin = transform.position;
        origin.y = waterHeight;
        Vector3 aimDir = Quaternion.AngleAxis(yaw, Vector3.up) * aimStartDir;
        Vector3 livePos = origin + aimDir * distance;
        livePos.y = waterHeight + 0.03f;
        bool liveOnWater = IsWaterAt(livePos);

        pendingLanding = livePos;
        pendingLanding.y = waterHeight + bobberRadius;
        pendingValid = liveOnWater;

        SetLiveMarker(true, livePos);

        if (!WasCastReleased() || Time.frameCount <= aimStartFrame)
            return;

        if (pendingValid)
            ReleaseCast();
        else
            CancelAim();
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
        EnsureBobber();
        flyStart = transform.TransformPoint(castOriginOffset);
        flyEnd = pendingLanding;
        flyTime = 0f;
        bobber.SetActive(true);
        bobber.transform.position = flyStart;
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
        flyTime += Time.deltaTime;
        float t = Mathf.Clamp01(flyTime / flyDuration);
        float eased = t * t * (3f - 2f * t);
        Vector3 pos = Vector3.Lerp(flyStart, flyEnd, eased);
        float distance = Vector3.Distance(flyStart, flyEnd);
        float arc = ArcHeight(distance);
        pos.y += Mathf.Sin(eased * Mathf.PI) * arc;
        bobber.transform.position = pos;

        if (t >= 1f)
        {
            bobber.transform.position = flyEnd;
            WaterRipples.Emit(flyEnd, WaterRippleKind.Cast);
            retrieveAllTheWay = false;
            reelRippleTraveled = 0f;
            phase = Phase.InWater;
        }
    }

    void TickInWater()
    {
        SteerLureOnWater();

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
        phase = Phase.Retrieving;
    }

    void TickRetrieve()
    {
        if (ConsumeDoubleClick())
            retrieveAllTheWay = true;

        if (!retrieveAllTheWay && !IsCastHeld())
        {
            Vector3 rest = bobber.transform.position;
            rest.y = waterHeight + bobberRadius;
            bobber.transform.position = rest;
            phase = Phase.InWater;
            return;
        }

        Vector3 rod = transform.TransformPoint(castOriginOffset);
        Vector3 previous = bobber.transform.position;

        Vector3 planar = previous;
        planar.y = waterHeight + bobberRadius;
        Vector3 planarTarget = rod;
        planarTarget.y = waterHeight + bobberRadius;
        planar = Vector3.MoveTowards(planar, planarTarget, retrieveSpeed * Time.deltaTime);
        SteerLurePlanar(ref planar);

        float remaining = Vector3.Distance(
            new Vector3(planar.x, 0f, planar.z),
            new Vector3(rod.x, 0f, rod.z));
        float liftT = 1f - Mathf.Clamp01(remaining / retrieveLiftDistance);
        liftT = liftT * liftT * liftT;

        Vector3 next = planar;
        next.y = Mathf.Lerp(waterHeight + bobberRadius, rod.y, liftT);
        bobber.transform.position = next;

        if (liftT < 0.2f)
            EmitReelRipples(previous, next, Flatten(planarTarget - previous));

        if (Vector3.Distance(next, rod) <= 0.28f)
            EndFishing();
    }

    void SteerLureOnWater()
    {
        Vector3 planar = bobber.transform.position;
        planar.y = waterHeight + bobberRadius;
        SteerLurePlanar(ref planar);
        bobber.transform.position = planar;
    }

    void SteerLurePlanar(ref Vector3 planar)
    {
        Vector2 arrows = ArrowAim();
        if (arrows.sqrMagnitude < 0.0001f)
            return;

        Vector3 right = Flatten(cam != null ? cam.transform.right : transform.right);
        Vector3 forward = Flatten(cam != null ? cam.transform.forward : transform.forward);
        Vector3 next = planar + (right * arrows.x + forward * arrows.y) * lureSteerSpeed * Time.deltaTime;
        next.y = waterHeight + bobberRadius;

        Vector3 origin = transform.position;
        origin.y = waterHeight;
        Vector3 offset = next - origin;
        offset.y = 0f;
        float range = offset.magnitude;
        if (range > maxCastDistance)
            next = origin + offset / range * maxCastDistance;

        if (!IsWaterAt(next))
            return;

        planar = next;
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

    void EndFishing()
    {
        retrieveAllTheWay = false;
        phase = Phase.Idle;
        HideMarkers();
        if (bobber != null)
            bobber.SetActive(false);
        SetFishingLocked(false);
    }

    void SetFishingLocked(bool locked)
    {
        if (boatInteractor != null && boatInteractor.IsOnBoat)
        {
            BoatMotor boat = GetComponentInParent<BoatMotor>();
            if (boat != null)
                boat.ControlsLocked = locked;
            return;
        }

        if (motor != null)
            motor.enabled = !locked;
    }

    void UpdateLine()
    {
        if (line == null)
            return;

        Vector3 rod = transform.TransformPoint(castOriginOffset);
        if (phase == Phase.Aiming)
        {
            SetLineArc(rod, pendingLanding);
            return;
        }

        if (bobber == null || !bobber.activeSelf || phase == Phase.Idle)
        {
            line.enabled = false;
            return;
        }

        if (phase == Phase.Flying)
        {
            SetLineArc(rod, bobber.transform.position);
            return;
        }

        SetLineTo(rod, bobber.transform.position);
    }

    void SetLineArc(Vector3 start, Vector3 end, float heightScale = 1f)
    {
        const int points = 16;
        EnsureLinePoints(points);

        float distance = Vector3.Distance(start, end);
        float arc = ArcHeight(distance) * heightScale;
        for (int i = 0; i < points; i++)
        {
            float t = i / (points - 1f);
            Vector3 point = Vector3.Lerp(start, end, t);
            point.y += Mathf.Sin(t * Mathf.PI) * arc;
            line.SetPosition(i, point);
        }
    }

    void SetLineTo(Vector3 start, Vector3 end)
    {
        const int points = 12;
        EnsureLinePoints(points);

        float sag = Mathf.Min(0.35f, Vector3.Distance(start, end) * 0.04f);
        for (int i = 0; i < points; i++)
        {
            float t = i / (points - 1f);
            Vector3 point = Vector3.Lerp(start, end, t);
            point.y -= Mathf.Sin(t * Mathf.PI) * sag;
            if (point.y < waterHeight + 0.02f)
                point.y = waterHeight + 0.02f;
            line.SetPosition(i, point);
        }
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

    void EnsureBobber()
    {
        if (bobber != null)
            return;

        bobber = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bobber.name = "Bobber";
        bobber.transform.localScale = Vector3.one * (bobberRadius * 2f);
        Object.Destroy(bobber.GetComponent<Collider>());
        var renderer = bobber.GetComponent<MeshRenderer>();
        var mat = new Material(FindLitShader());
        mat.SetColor("_BaseColor", bobberColor);
        renderer.sharedMaterial = mat;

        var lineGo = new GameObject("FishingLine");
        lineGo.transform.SetParent(transform, false);
        line = lineGo.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.widthMultiplier = 0.025f;
        line.useWorldSpace = true;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.material = new Material(FindUnlitShader());
        ApplyColor(line.material, lineColor);
        line.startColor = lineColor;
        line.endColor = lineColor;
    }

    void EnsureMarkers()
    {
        EnsureBobber();
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

    static Shader FindLitShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        return shader != null ? shader : Shader.Find("Sprites/Default");
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
            if (root.activeSelf != visible)
                root.SetActive(visible);
        }

        public void SetPosition(Vector3 world)
        {
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
