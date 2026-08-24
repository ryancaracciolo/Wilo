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
    [SerializeField] float maxSpeed = 12f;
    [SerializeField] float acceleration = 9f;
    [SerializeField] float deceleration = 6f;
    [SerializeField] float turnSpeed = 85f;
    [SerializeField] float hullRadius = 1.35f;

    [Header("Lake")]
    [SerializeField] Transform waterSurface;
    [SerializeField] Transform seat;
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
    float wakeTraveled;

    public bool IsOccupied => occupied;
    public Transform Seat => seat;
    public float Speed => currentSpeed;
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

    public void SetVisualRoot(Transform value)
    {
        visualRoot = value;
    }

    public void SetOccupied(bool value)
    {
        occupied = value;
        if (!occupied)
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
        if (!occupied || controlsLocked || HudInput.PopupOpen)
            return;

        Vector2 input = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
        float throttle = Mathf.Clamp(input.y, -0.55f, 1f);
        float steer = input.x;

        float targetSpeed = throttle * maxSpeed;
        float accel = Mathf.Abs(targetSpeed) > Mathf.Abs(currentSpeed) ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, accel * Time.deltaTime);

        float speedFactor = 0.4f + 0.6f * Mathf.Clamp01(Mathf.Abs(currentSpeed) / maxSpeed);
        transform.Rotate(0f, steer * turnSpeed * speedFactor * Time.deltaTime, 0f);

        Vector3 motion = transform.forward * (currentSpeed * Time.deltaTime);
        Vector3 next = transform.position + motion;
        if (!IsBlocked(next))
            transform.position = next;

        SnapToWater();
        EmitWake(Mathf.Abs(currentSpeed) * Time.deltaTime);
    }

    void SnapToWater()
    {
        if (!hasWaterHeight)
            return;

        Vector3 pos = transform.position;
        pos.y = waterHeight + hullClearance + Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
        transform.position = pos;
    }

    void EmitWake(float distance)
    {
        if (distance < 0.0001f)
            return;

        wakeTraveled += distance;
        if (wakeTraveled < wakeSpacing)
            return;

        wakeTraveled = 0f;
        float speedBlend = Mathf.Clamp01(Mathf.Abs(currentSpeed) / maxSpeed);
        float width = Mathf.Lerp(0.35f, 0.7f, speedBlend);
        Vector3 stern = transform.position - transform.forward * 1.7f;
        Vector3 side = transform.right * width;
        WaterRipples.Emit(stern + side, WaterRippleKind.Boat);
        WaterRipples.Emit(stern - side, WaterRippleKind.Boat);
        WaterRipples.Emit(stern - transform.forward * 0.55f, WaterRippleKind.Boat);
    }

    bool IsBlocked(Vector3 nextPosition)
    {
        if (hasWaterHeight)
        {
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null && terrain.terrainData != null)
            {
                float groundY = terrain.SampleHeight(nextPosition) + terrain.transform.position.y;
                if (groundY > waterHeight - 0.08f)
                    return true;
            }
        }

        Vector3 origin = transform.position + Vector3.up * 0.4f;
        Vector3 destination = nextPosition + Vector3.up * 0.4f;
        Vector3 delta = destination - origin;
        float distance = delta.magnitude;
        if (distance < 0.0001f)
            return false;

        if (!Physics.SphereCast(origin, hullRadius, delta / distance, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
            return false;

        if (hit.transform.root == transform)
            return false;
        if (hit.collider.GetComponent<CharacterController>() != null)
            return false;
        if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Water"))
            return false;

        Vector3 now = transform.position;
        now.y = 0f;
        Vector3 nextFlat = nextPosition;
        nextFlat.y = 0f;
        Vector3 hitFlat = hit.point;
        hitFlat.y = 0f;
        return Vector3.Distance(nextFlat, hitFlat) < Vector3.Distance(now, hitFlat);
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
