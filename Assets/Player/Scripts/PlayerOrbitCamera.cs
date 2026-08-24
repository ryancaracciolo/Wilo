using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Third-person orbit camera in the Animal Crossing register: high over the
/// player, 360° yaw, tightly limited pitch and zoom, and kept out of terrain
/// and water.
/// </summary>
public class PlayerOrbitCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] Transform target;
    [SerializeField] Vector3 pivotOffset = new Vector3(0f, 0.95f, 0f);
    [Tooltip("Where the player should sit in the viewport. X 0.5 is centered; Y slightly under 0.5 keeps a standing figure in the middle of the frame.")]
    [SerializeField] Vector2 followViewport = new Vector2(0.5f, 0.45f);

    [Header("Orbit")]
    [SerializeField] float defaultDistance = 6.2f;
    [SerializeField] float minDistance = 4.5f;
    [SerializeField] float maxDistance = 15.5f;
    [SerializeField] float defaultPitch = 48f;
    [SerializeField] float minPitch = 8f;
    [SerializeField] float maxPitch = 68f;
    [SerializeField] float mouseSensitivity = 0.32f;
    [SerializeField] float gamepadSensitivity = 165f;
    [SerializeField] float keyboardYawSpeed = 150f;
    [SerializeField] float zoomStep = 0.85f;
    [SerializeField] bool invertY;

    [Header("Collision")]
    [SerializeField] float cameraRadius = 0.28f;
    [SerializeField] float minCollisionDistance = 1.6f;
    [SerializeField] float waterClearance = 0.85f;
    [SerializeField] float terrainClearance = 0.9f;
    [SerializeField] LayerMask collisionMask = ~0;
    [SerializeField] Transform waterSurface;

    [Header("Follow")]
    [SerializeField] float followSmoothTime = 0.08f;

    [Header("Catch framing")]
    [SerializeField] float catchDistance = 3.3f;
    [SerializeField] float catchPitch = 26f;
    [SerializeField] Vector2 catchViewport = new Vector2(0.56f, 0.46f);
    [SerializeField] float catchBlendDuration = 0.62f;
    [SerializeField] float catchRestoreDuration = 0.52f;

    InputAction lookAction;
    InputAction zoomAction;

    float yaw;
    float pitch;
    float distance;
    Vector2 viewport;
    float waterHeight;
    bool hasWaterHeight;
    Vector3 followVelocity;
    bool initialized;
    LayerMask runtimeCollisionMask;
    Camera cam;
    PlayerFishing fishing;

    Transform catchFocus;
    bool catchFraming;
    bool blending;
    bool hasSavedOrbit;
    float blendElapsed;
    float blendDuration;
    float blendFromYaw;
    float blendFromPitch;
    float blendFromDistance;
    Vector2 blendFromViewport;
    float blendToYaw;
    float blendToPitch;
    float blendToDistance;
    Vector2 blendToViewport;
    float savedYaw;
    float savedPitch;
    float savedDistance;
    Vector2 savedViewport;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        if (target != null)
            SnapToOrbit();
        fishing = target != null ? target.GetComponent<PlayerFishing>() : null;
    }

    void OnEnable()
    {
        var actions = InputSystem.actions;
        if (actions != null)
        {
            lookAction = actions.FindAction("Player/Look");
            lookAction?.Enable();

            zoomAction = actions.FindAction("UI/ScrollWheel");
            zoomAction?.Enable();
        }

        CacheWaterHeight();
        StripWaterFromCollisionMask();
        if (cam == null)
            cam = GetComponent<Camera>();
    }

    void Start()
    {
        if (target == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                target = player.transform;
        }

        SnapToOrbit();
    }

    void LateUpdate()
    {
        if (target == null)
            return;

        if (fishing == null)
            fishing = target.GetComponent<PlayerFishing>();

        if (!initialized)
            SnapToOrbit();

        TickOrbitBlend();
        if (!catchFraming && !blending)
        {
            ApplyLookInput();
            ApplyZoomInput();
        }

        Vector3 pivot = FramingPivot();
        Vector3 desired = DesiredOrbitPosition(pivot);
        desired = ResolveCollisions(pivot, desired);
        desired = ClampAboveSurfaces(desired);

        float smooth = catchFraming || blending ? Mathf.Max(0.16f, followSmoothTime) : followSmoothTime;
        transform.position = Vector3.SmoothDamp(transform.position, desired, ref followVelocity, smooth);
        AimFocusOnScreen(pivot, viewport);
    }

    public void BeginCatchFraming(Transform fish)
    {
        if (!initialized)
            SnapToOrbit();

        catchFocus = fish;
        if (!catchFraming)
        {
            savedYaw = yaw;
            savedPitch = pitch;
            savedDistance = distance;
            savedViewport = viewport;
            hasSavedOrbit = true;
        }

        catchFraming = true;
        float sizeBoost = 0f;
        var agent = fish != null ? fish.GetComponent<FishAgent>() : null;
        if (agent != null)
            sizeBoost = Mathf.Lerp(0f, 0.55f, Mathf.InverseLerp(0.6f, 8f, agent.Size.Pounds));

        BeginBlend(
            yaw, pitch, distance, viewport,
            yaw, catchPitch, catchDistance + sizeBoost, catchViewport,
            catchBlendDuration);
    }

    public void EndCatchFraming()
    {
        catchFocus = null;
        catchFraming = false;
        if (!hasSavedOrbit)
            return;

        hasSavedOrbit = false;
        BeginBlend(
            yaw, pitch, distance, viewport,
            savedYaw, savedPitch, savedDistance, savedViewport,
            catchRestoreDuration);
    }

    void BeginBlend(
        float fromYaw, float fromPitch, float fromDistance, Vector2 fromViewport,
        float toYaw, float toPitch, float toDistance, Vector2 toViewport,
        float duration)
    {
        blendFromYaw = fromYaw;
        blendFromPitch = fromPitch;
        blendFromDistance = fromDistance;
        blendFromViewport = fromViewport;
        blendToYaw = toYaw;
        blendToPitch = toPitch;
        blendToDistance = toDistance;
        blendToViewport = toViewport;
        blendDuration = Mathf.Max(0.05f, duration);
        blendElapsed = 0f;
        blending = true;
    }

    void TickOrbitBlend()
    {
        if (!blending)
            return;

        blendElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(blendElapsed / blendDuration);
        t = t * t * (3f - 2f * t);
        yaw = Mathf.LerpAngle(blendFromYaw, blendToYaw, t);
        pitch = Mathf.Lerp(blendFromPitch, blendToPitch, t);
        distance = Mathf.Lerp(blendFromDistance, blendToDistance, t);
        viewport = Vector2.Lerp(blendFromViewport, blendToViewport, t);
        if (blendElapsed < blendDuration)
            return;

        yaw = blendToYaw;
        pitch = blendToPitch;
        distance = blendToDistance;
        viewport = blendToViewport;
        blending = false;
    }

    Vector3 FramingPivot()
    {
        Vector3 playerPivot = target.position + pivotOffset;
        if (!catchFraming || catchFocus == null)
            return playerPivot;
        return Vector3.Lerp(playerPivot, catchFocus.position, 0.46f);
    }

    void ApplyLookInput()
    {
        Vector2 look = lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;
        bool mouseLookHeld = Mouse.current != null &&
            (Mouse.current.rightButton.isPressed || Mouse.current.middleButton.isPressed);
        Vector2 stick = Gamepad.current != null ? Gamepad.current.rightStick.ReadValue() : Vector2.zero;

        if (stick.sqrMagnitude > 0.0001f)
        {
            yaw += stick.x * gamepadSensitivity * Time.deltaTime;
            float pitchDelta = stick.y * gamepadSensitivity * Time.deltaTime;
            pitch += invertY ? pitchDelta : -pitchDelta;
        }
        else if (mouseLookHeld)
        {
            yaw += look.x * mouseSensitivity;
            float pitchDelta = look.y * mouseSensitivity;
            pitch += invertY ? pitchDelta : -pitchDelta;
        }

        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (fishing != null && fishing.CapturesArrowKeys)
            {
                if (keyboard.qKey.isPressed)
                    yaw -= keyboardYawSpeed * Time.deltaTime;
                if (keyboard.eKey.isPressed)
                    yaw += keyboardYawSpeed * Time.deltaTime;
            }
            else
            {
                if (keyboard.qKey.isPressed || keyboard.leftArrowKey.isPressed)
                    yaw -= keyboardYawSpeed * Time.deltaTime;
                if (keyboard.eKey.isPressed || keyboard.rightArrowKey.isPressed)
                    yaw += keyboardYawSpeed * Time.deltaTime;
                if (keyboard.upArrowKey.isPressed)
                    pitch -= keyboardYawSpeed * 0.45f * Time.deltaTime;
                if (keyboard.downArrowKey.isPressed)
                    pitch += keyboardYawSpeed * 0.45f * Time.deltaTime;
            }
        }

        yaw = Mathf.Repeat(yaw, 360f);
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    void ApplyZoomInput()
    {
        float scroll = 0f;
        if (!HudInput.BlocksWorldClick)
        {
            if (zoomAction != null)
                scroll = zoomAction.ReadValue<Vector2>().y;
            else if (Mouse.current != null)
                scroll = Mouse.current.scroll.ReadValue().y;
        }

        if (Mathf.Abs(scroll) > 0.01f)
            distance -= Mathf.Sign(scroll) * zoomStep;

        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.minusKey.wasPressedThisFrame)
                distance += zoomStep;
            if (keyboard.equalsKey.wasPressedThisFrame)
                distance -= zoomStep;
        }

        distance = Mathf.Clamp(distance, minDistance, maxDistance);
    }

    Vector3 DesiredOrbitPosition(Vector3 pivot)
    {
        Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
        return pivot + orbit * (Vector3.back * distance);
    }

    Vector3 ResolveCollisions(Vector3 pivot, Vector3 desired)
    {
        Vector3 toCamera = desired - pivot;
        float desiredDistance = toCamera.magnitude;
        if (desiredDistance < 0.001f)
            return desired;

        Vector3 direction = toCamera / desiredDistance;
        float castDistance = Mathf.Max(0f, desiredDistance - cameraRadius);

        if (Physics.SphereCast(pivot, cameraRadius, direction, out RaycastHit hit, castDistance, runtimeCollisionMask, QueryTriggerInteraction.Ignore))
        {
            float blocked = Mathf.Max(minCollisionDistance, hit.distance - cameraRadius);
            return pivot + direction * blocked;
        }

        return desired;
    }

    Vector3 ClampAboveSurfaces(Vector3 position)
    {
        if (hasWaterHeight && position.y < waterHeight + waterClearance)
            position.y = waterHeight + waterClearance;

        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null && terrain.terrainData != null)
        {
            Vector3 terrainPos = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            bool overTerrain =
                position.x >= terrainPos.x && position.x <= terrainPos.x + size.x &&
                position.z >= terrainPos.z && position.z <= terrainPos.z + size.z;

            if (overTerrain)
            {
                float groundY = terrain.SampleHeight(position) + terrainPos.y;
                float minY = groundY + terrainClearance;
                if (position.y < minY)
                    position.y = minY;
            }
        }

        return position;
    }

    void SnapToOrbit()
    {
        if (target == null)
            return;

        yaw = target.eulerAngles.y;
        pitch = defaultPitch;
        distance = defaultDistance;
        viewport = followViewport;
        followVelocity = Vector3.zero;

        Vector3 pivot = target.position + pivotOffset;
        Vector3 desired = ClampAboveSurfaces(ResolveCollisions(pivot, DesiredOrbitPosition(pivot)));
        transform.position = desired;
        AimFocusOnScreen(pivot, viewport);
        initialized = true;
    }

    void AimFocusOnScreen(Vector3 focus, Vector2 viewportPoint)
    {
        Vector3 toFocus = focus - transform.position;
        if (toFocus.sqrMagnitude < 0.0001f)
            return;

        transform.rotation = Quaternion.LookRotation(toFocus, Vector3.up);

        if (cam == null)
            cam = GetComponent<Camera>();
        if (cam == null)
            return;

        Ray viewportRay = cam.ViewportPointToRay(new Vector3(viewportPoint.x, viewportPoint.y, 0f));
        transform.rotation = Quaternion.FromToRotation(viewportRay.direction, toFocus.normalized) * transform.rotation;

        Vector3 euler = transform.eulerAngles;
        transform.rotation = Quaternion.Euler(euler.x, euler.y, 0f);
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

    void StripWaterFromCollisionMask()
    {
        runtimeCollisionMask = collisionMask;

        int waterLayer = LayerMask.NameToLayer("Water");
        int playerLayer = LayerMask.NameToLayer("Player");
        int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
        int uiLayer = LayerMask.NameToLayer("UI");

        if (waterLayer >= 0)
            runtimeCollisionMask &= ~(1 << waterLayer);
        if (playerLayer >= 0)
            runtimeCollisionMask &= ~(1 << playerLayer);
        if (ignoreRaycast >= 0)
            runtimeCollisionMask &= ~(1 << ignoreRaycast);
        if (uiLayer >= 0)
            runtimeCollisionMask &= ~(1 << uiLayer);
    }
}
