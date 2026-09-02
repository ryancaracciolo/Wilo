using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple transform-based rowboat: throttle and steer on the water plane.
/// Gameplay lives on this object; put the art mesh under a Visual child so
/// the dummy boat can be swapped without rewriting boarding or driving.
/// </summary>
public class BoatMotor : MonoBehaviour
{
    [Header("Handling")]
    [SerializeField] float maxSpeed = 18f;
    [SerializeField] float acceleration = 9f;
    [SerializeField] float deceleration = 6f;
    [SerializeField] float turnSpeed = 85f;
    [SerializeField] float hullRadius = 1.35f;
    [Tooltip("The hull may run this far above the waterline onto sand. Steeper banks still stop it.")]
    [SerializeField] float beachClimb = 0.4f;

    [Header("Lake")]
    [SerializeField] Transform waterSurface;
    [SerializeField] Transform seat;
    [SerializeField] Transform helm;
    [SerializeField] Transform tiller;
    [Tooltip("Visual mesh lives under this transform so the dummy boat can be swapped later.")]
    [SerializeField] Transform visualRoot;
    [SerializeField] float bobAmplitude = 0.05f;
    [SerializeField] float bobSpeed = 1.15f;
    [Tooltip("How high the hull sits above the water plane. Keep small so the boat sits in the water.")]
    [SerializeField] float hullClearance = 0.05f;
    [SerializeField] float wakeSpacing = 0.7f;

    float waterHeight;
    bool hasWaterHeight;
    float currentSpeed;
    InputAction moveAction;
    bool occupied;
    bool controlsLocked;
    bool aiControlled;
    Vector2 aiInput;
    float wakeTraveled;

    /// <summary>False on decorative field boats so the player cannot board them.</summary>
    public bool Boardable { get; private set; } = true;

    public bool IsOccupied => occupied;
    public bool IsAiControlled => aiControlled;
    public Transform Seat => seat;
    public Transform Helm => helm;
    public Transform Tiller => tiller;
    public bool HasDriveInput
    {
        get
        {
            if (!occupied || controlsLocked || HudInput.PopupOpen)
                return false;
            return ReadMove().sqrMagnitude > 0.04f;
        }
    }
    public float Speed => currentSpeed;
    /// <summary>Yaw of the bow. Drive and the mesh face opposite the transform.</summary>
    public float BowYaw => transform.eulerAngles.y + 180f;
    public float WaterHeight => hasWaterHeight ? waterHeight : transform.position.y;
    public bool ControlsLocked
    {
        get => controlsLocked;
        set
        {
            controlsLocked = value;
            if (controlsLocked)
                currentSpeed = 0f;
        }
    }

    public void SetSeat(Transform value)
    {
        seat = value;
    }

    public void SetHelm(Transform value)
    {
        helm = value;
    }

    public void SetTiller(Transform value)
    {
        tiller = value;
    }

    public void SetVisualRoot(Transform value)
    {
        visualRoot = value;
    }

    public void SetOccupied(bool value)
    {
        occupied = value;
        if (!occupied && !aiControlled)
            currentSpeed = 0f;
    }

    public void SetBoardable(bool value)
    {
        Boardable = value;
    }

    /// <summary>NPC throttle/steer. Cleared with <see cref="ClearAiDrive"/>.</summary>
    public void SetAiDrive(Vector2 throttleSteer)
    {
        aiControlled = true;
        aiInput = Vector2.ClampMagnitude(throttleSteer, 1f);
    }

    public void ClearAiDrive()
    {
        aiControlled = false;
        aiInput = Vector2.zero;
        currentSpeed = 0f;
    }

    void OnEnable()
    {
        CacheWaterHeight();
        var actions = InputSystem.actions;
        moveAction = actions != null ? actions.FindAction("Player/Move") : null;
        moveAction?.Enable();
    }

    void Update()
    {
        SnapToWater();
        if (controlsLocked)
            return;

        Vector2 input;
        if (aiControlled)
        {
            input = aiInput;
        }
        else
        {
            if (!occupied || HudInput.PopupOpen)
                return;
            input = ReadMove();
        }

        float throttle = Mathf.Clamp(input.y, -0.55f, 1f);
        float steer = input.x;

        float targetSpeed = throttle * maxSpeed;
        float accel = Mathf.Abs(targetSpeed) > Mathf.Abs(currentSpeed) ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, accel * Time.deltaTime);

        float speedFactor = 0.4f + 0.6f * Mathf.Clamp01(Mathf.Abs(currentSpeed) / maxSpeed);
        transform.Rotate(0f, steer * turnSpeed * speedFactor * Time.deltaTime, 0f);

        Vector3 motion = -transform.forward * (currentSpeed * Time.deltaTime);
        Vector3 next = transform.position + motion;
        if (!IsBlocked(next))
            transform.position = next;

        SnapToWater();
        EmitWake(Mathf.Abs(currentSpeed) * Time.deltaTime);
    }

    Vector2 ReadMove()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && !HudInput.Typing)
        {
            Vector2 wasd = Vector2.zero;
            if (keyboard.wKey.isPressed)
                wasd.y += 1f;
            if (keyboard.sKey.isPressed)
                wasd.y -= 1f;
            if (keyboard.aKey.isPressed)
                wasd.x -= 1f;
            if (keyboard.dKey.isPressed)
                wasd.x += 1f;
            if (wasd.sqrMagnitude > 0.0001f)
                return Vector2.ClampMagnitude(wasd, 1f);
            return Vector2.zero;
        }

        return moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
    }

    void SnapToWater()
    {
        if (!hasWaterHeight)
            return;

        Vector3 pos = transform.position;
        float bob = Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
        float waterY = waterHeight + hullClearance + bob;
        float groundY = SampleGround(pos);
        // Sit on wet sand when beached; stay on the water plane otherwise.
        pos.y = groundY > waterHeight - 0.05f
            ? Mathf.Max(waterY, groundY + hullClearance)
            : waterY;
        transform.position = pos;
    }

    void EmitWake(float distance)
    {
        if (distance < 0.0001f)
            return;

        float spacing = aiControlled ? wakeSpacing * 2.4f : wakeSpacing;
        wakeTraveled += distance;
        if (wakeTraveled < spacing)
            return;

        wakeTraveled = 0f;
        Vector3 stern = transform.position + transform.forward * 1.7f;
        if (aiControlled)
        {
            WaterRipples.Emit(stern, WaterRippleKind.Boat, 0.55f);
            return;
        }

        float speedBlend = Mathf.Clamp01(Mathf.Abs(currentSpeed) / maxSpeed);
        float width = Mathf.Lerp(0.35f, 0.7f, speedBlend);
        Vector3 side = transform.right * width;
        WaterRipples.Emit(stern + side, WaterRippleKind.Boat, 1f, true);
        WaterRipples.Emit(stern - side, WaterRippleKind.Boat, 1f, true);
        WaterRipples.Emit(stern + transform.forward * 0.55f, WaterRippleKind.Boat, 1f, true);
    }

    bool IsBlocked(Vector3 nextPosition)
    {
        if (hasWaterHeight && SampleGround(nextPosition) > waterHeight + beachClimb)
            return true;

        Vector3 origin = transform.position + Vector3.up * 0.4f;
        Vector3 destination = nextPosition + Vector3.up * 0.4f;
        Vector3 delta = destination - origin;
        float distance = delta.magnitude;
        if (distance < 0.0001f)
            return false;

        int mask = ~0;
        int structure = WorldConditions.StructureLayer;
        if (structure >= 0)
            mask &= ~(1 << structure);
        if (!Physics.SphereCast(origin, hullRadius, delta / distance, out RaycastHit hit, distance, mask, QueryTriggerInteraction.Ignore))
            return false;

        if (hit.transform.root == transform)
            return false;
        if (aiControlled && hit.collider.GetComponentInParent<BoatMotor>() != null)
            return false;
        if (hit.collider.GetComponent<CharacterController>() != null)
            return false;
        if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Water"))
            return false;
        // Underwater slope and wet sand are the beach, not a wall.
        if (IsBeachHit(hit))
            return false;

        Vector3 now = transform.position;
        now.y = 0f;
        Vector3 nextFlat = nextPosition;
        nextFlat.y = 0f;
        Vector3 hitFlat = hit.point;
        hitFlat.y = 0f;
        return Vector3.Distance(nextFlat, hitFlat) < Vector3.Distance(now, hitFlat);
    }

    bool IsBeachHit(RaycastHit hit)
    {
        if (!hasWaterHeight)
            return false;

        bool terrainHit = hit.collider is TerrainCollider
            || (Terrain.activeTerrain != null && hit.collider.gameObject == Terrain.activeTerrain.gameObject);
        if (!terrainHit)
            return false;

        return SampleGround(hit.point) <= waterHeight + beachClimb;
    }

    static float SampleGround(Vector3 worldPosition)
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null || terrain.terrainData == null)
            return float.NegativeInfinity;
        return terrain.SampleHeight(worldPosition) + terrain.transform.position.y;
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
}
