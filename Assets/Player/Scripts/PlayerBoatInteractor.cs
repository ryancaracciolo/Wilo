using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Boards and leaves the nearby rowboat. Uses F / Interact so it does not
/// fight Q/E camera rotation.
/// </summary>
[RequireComponent(typeof(PlayerMotor))]
public class PlayerBoatInteractor : MonoBehaviour
{
    [SerializeField] float boardRange = 5.2f;
    [SerializeField] float dockRange = 8f;
    [SerializeField] float interactCooldown = 0.35f;

    PlayerMotor motor;
    CharacterController controller;
    BoatMotor occupiedBoat;
    PlayerFishing fishing;
    InputAction interactAction;
    float cooldownUntil;

    public bool IsOnBoat => occupiedBoat != null;
    public BoatMotor OccupiedBoat => occupiedBoat;

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        controller = GetComponent<CharacterController>();
        fishing = GetComponent<PlayerFishing>();
    }

    void OnEnable()
    {
        var actions = InputSystem.actions;
        interactAction = actions != null ? actions.FindAction("Player/Interact") : null;
        interactAction?.Enable();
    }

    void Update()
    {
        if (HudInput.PopupOpen)
            return;

        if (!WasInteractPressed() || Time.time < cooldownUntil)
            return;

        if (fishing != null && fishing.IsFishing)
            return;

        cooldownUntil = Time.time + interactCooldown;

        if (occupiedBoat != null)
            TryDisembark();
        else
            TryBoard();
    }

    bool WasInteractPressed()
    {
        if (interactAction != null && interactAction.WasPressedThisFrame())
            return true;

        if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            return true;

        return false;
    }

    /// <summary>Steps off wherever the player is. Used when the day ends away from the dock.</summary>
    public void ForceDisembark()
    {
        if (occupiedBoat == null)
            return;

        occupiedBoat.SetOccupied(false);
        transform.SetParent(null, true);
        occupiedBoat = null;

        if (controller != null)
            controller.enabled = true;
        motor.enabled = true;
    }

    void TryBoard()
    {
        BoatMotor boat = FindClosestBoat(boardRange);
        if (boat == null || boat.Seat == null)
            return;

        occupiedBoat = boat;
        motor.enabled = false;
        if (controller != null)
            controller.enabled = false;

        transform.SetParent(boat.Seat, true);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        boat.SetOccupied(true);
    }

    void TryDisembark()
    {
        if (!TryGetDisembarkPose(out Vector3 position, out Quaternion rotation))
            return;

        occupiedBoat.SetOccupied(false);
        transform.SetParent(null, true);
        transform.SetPositionAndRotation(position, rotation);
        occupiedBoat = null;

        if (controller != null)
            controller.enabled = true;
        motor.enabled = true;
    }

    bool TryGetDisembarkPose(out Vector3 position, out Quaternion rotation)
    {
        position = transform.position;
        rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        var dock = GameObject.Find("EndPlatform");
        if (dock != null && Vector3.Distance(occupiedBoat.transform.position, dock.transform.position) <= dockRange)
        {
            var renderer = dock.GetComponent<Renderer>();
            position = dock.transform.position;
            position.y = renderer != null ? renderer.bounds.max.y + 0.02f : dock.transform.position.y + 0.2f;
            var dockRoot = GameObject.Find("DockPlaceholder");
            if (dockRoot != null)
                rotation = Quaternion.Euler(0f, dockRoot.transform.eulerAngles.y, 0f);
            return true;
        }

        if (TryFindShorePoint(out position))
        {
            Vector3 away = position - occupiedBoat.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.001f)
                rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            return true;
        }

        return false;
    }

    bool TryFindShorePoint(out Vector3 position)
    {
        position = Vector3.zero;
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null || occupiedBoat == null)
            return false;

        var water = GameObject.Find("Surface");
        float waterY = water != null && water.GetComponent<Renderer>() != null
            ? water.GetComponent<Renderer>().bounds.max.y
            : occupiedBoat.transform.position.y;

        Vector3[] sides =
        {
            occupiedBoat.transform.right,
            -occupiedBoat.transform.right,
            occupiedBoat.transform.forward,
            -occupiedBoat.transform.forward
        };

        for (int i = 0; i < sides.Length; i++)
        {
            Vector3 probe = occupiedBoat.transform.position + sides[i] * 2.4f;
            float groundY = terrain.SampleHeight(probe) + terrain.transform.position.y;
            if (groundY >= waterY - 0.3f)
            {
                position = probe;
                position.y = groundY + 0.02f;
                return true;
            }
        }

        return false;
    }

    BoatMotor FindClosestBoat(float range)
    {
        BoatMotor[] boats = FindObjectsByType<BoatMotor>(FindObjectsSortMode.None);
        BoatMotor closest = null;
        float best = range;
        for (int i = 0; i < boats.Length; i++)
        {
            float d = Vector3.Distance(transform.position, boats[i].transform.position);
            if (d <= best)
            {
                best = d;
                closest = boats[i];
            }
        }

        return closest;
    }
}
