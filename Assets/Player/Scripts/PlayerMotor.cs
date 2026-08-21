using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Camera-relative walk motor for the placeholder fisherman.
/// Movement is Animal Crossing-like: WASD / left stick steer on the ground plane
/// relative to the camera, and the body turns to face the walk direction.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMotor : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] float walkSpeed = 3.6f;
    [SerializeField] float sprintSpeed = 5.8f;
    [SerializeField] float rotationSpeed = 12f;
    [SerializeField] float gravity = -22f;

    [Header("Lake")]
    [Tooltip("How far below the water surface the player may stand (shore wading).")]
    [SerializeField] float maxWadeDepth = 0.35f;
    [SerializeField] Transform waterSurface;

    CharacterController controller;
    Transform cameraTransform;
    InputAction moveAction;
    InputAction sprintAction;

    float verticalVelocity;
    float waterHeight;
    bool hasWaterHeight;
    float wadeTraveled;

    public Vector3 Velocity { get; private set; }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        CacheWaterHeight();
    }

    void OnEnable()
    {
        var actions = InputSystem.actions;
        if (actions == null)
            return;

        moveAction = actions.FindAction("Player/Move");
        sprintAction = actions.FindAction("Player/Sprint");
        moveAction?.Enable();
        sprintAction?.Enable();
    }

    void Start()
    {
        if (Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    void Update()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        Vector2 moveInput = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
        bool sprinting = sprintAction != null && sprintAction.IsPressed();

        Vector3 planar = CameraRelativePlanar(moveInput);
        if (planar.sqrMagnitude > 1f)
            planar.Normalize();

        float speed = sprinting ? sprintSpeed : walkSpeed;
        Vector3 motion = planar * speed;

        if (motion.sqrMagnitude > 0.0001f && WouldEnterDeepWater(motion))
            motion = Vector3.zero;

        if (motion.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(motion, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;
        else
            verticalVelocity += gravity * Time.deltaTime;

        motion.y = verticalVelocity;
        CollisionFlags flags = controller.Move(motion * Time.deltaTime);
        if ((flags & CollisionFlags.Below) != 0 && verticalVelocity < 0f)
            verticalVelocity = -2f;

        Velocity = controller.velocity;
        EmitWadeRipples();
    }

    void EmitWadeRipples()
    {
        if (!hasWaterHeight || !controller.isGrounded)
            return;
        if (transform.position.y > waterHeight - 0.02f)
            return;

        Vector3 planar = Velocity;
        planar.y = 0f;
        if (planar.sqrMagnitude < 0.04f)
            return;

        wadeTraveled += planar.magnitude * Time.deltaTime;
        if (wadeTraveled < 0.55f)
            return;

        wadeTraveled = 0f;
        Vector3 splash = transform.position;
        splash.y = waterHeight;
        Vector3 side = Vector3.Cross(Vector3.up, planar.normalized);
        WaterRipples.Emit(splash + side * 0.18f, WaterRippleKind.Wade);
        WaterRipples.Emit(splash - side * 0.18f, WaterRippleKind.Wade);
    }

    Vector3 CameraRelativePlanar(Vector2 input)
    {
        if (input.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        Vector3 forward;
        Vector3 right;
        if (cameraTransform != null)
        {
            forward = cameraTransform.forward;
            right = cameraTransform.right;
        }
        else
        {
            forward = transform.forward;
            right = transform.right;
        }

        forward.y = 0f;
        right.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        else
            forward.Normalize();

        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.right;
        else
            right.Normalize();

        return forward * input.y + right * input.x;
    }

    bool WouldEnterDeepWater(Vector3 planarMotion)
    {
        if (!hasWaterHeight)
            return false;

        Vector3 probe = transform.position + planarMotion.normalized * (controller.radius + 0.15f);
        probe.y = transform.position.y + 2f;

        int mask = ~LayerMask.GetMask("Player", "Water", "Ignore Raycast", "UI", "TransparentFX");
        if (!Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 6f, mask, QueryTriggerInteraction.Ignore))
            return false;

        return hit.point.y < waterHeight - maxWadeDepth;
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
