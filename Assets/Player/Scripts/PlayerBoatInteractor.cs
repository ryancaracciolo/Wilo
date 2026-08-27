using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Boards and leaves the nearby rowboat. Uses F / Interact so it does not
/// fight Q/E camera rotation.
/// </summary>
[RequireComponent(typeof(PlayerMotor))]
public class PlayerBoatInteractor : MonoBehaviour
{
    const string CueId = "boat";

    [SerializeField] float boardRange = 5.2f;
    [SerializeField] float dockRange = 8f;
    [SerializeField] float interactCooldown = 0.35f;

    PlayerMotor motor;
    CharacterController controller;
    BoatMotor occupiedBoat;
    PlayerFishing fishing;
    InputAction interactAction;
    float cooldownUntil;
    Renderer waterRenderer;
    BoatMotor[] boats;
    BoatDock[] landings;
    float boatsUntil;
    float landingsUntil;

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

    void OnDisable()
    {
        HudCues.Clear(CueId);
    }

    void Update()
    {
        RefreshPrompt();

        if (HudInput.PopupOpen)
            return;

        if (!WasInteractPressed())
            return;

        TryInteract();
    }

    bool WasInteractPressed()
    {
        if (interactAction != null && interactAction.WasPressedThisFrame())
            return true;

        if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            return true;

        return false;
    }

    void TryInteract()
    {
        if (fishing != null && fishing.IsFishing)
            return;
        if (Time.time < cooldownUntil)
            return;

        cooldownUntil = Time.time + interactCooldown;
        if (occupiedBoat != null)
            TryDisembark();
        else
            TryBoard();
    }

    void RefreshPrompt()
    {
        if (HudInput.PopupOpen || (fishing != null && fishing.IsFishing))
        {
            HudCues.Clear(CueId);
            return;
        }

        if (occupiedBoat != null)
        {
            if (TryGetDisembarkPose(out _, out _))
            {
                HudCues.ShowAction(CueId, "F", "Get off", TryInteract);
            }
            else
                HudCues.Clear(CueId);
            return;
        }

        BoatMotor boat = FindClosestBoat(boardRange);
        if (boat != null && boat.Seat != null)
            HudCues.ShowAction(CueId, "F", "Board", TryInteract);
        else
            HudCues.Clear(CueId);
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
        HudCues.Clear(CueId);
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

        BoatDock landing = occupiedBoat != null
            ? FindClosestLanding(occupiedBoat.transform.position, dockRange)
            : null;
        if (landing != null)
        {
            var renderer = landing.GetComponent<Renderer>();
            position = landing.transform.position;
            position.y = renderer != null ? renderer.bounds.max.y + 0.02f : landing.transform.position.y + 0.2f;
            rotation = Quaternion.Euler(0f, landing.Facing.eulerAngles.y, 0f);
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

        ResolveScene();
        float waterY = waterRenderer != null
            ? waterRenderer.bounds.max.y
            : occupiedBoat.transform.position.y;

        Vector3 hull = occupiedBoat.transform.position;
        Vector3[] sides =
        {
            occupiedBoat.transform.right,
            -occupiedBoat.transform.right,
            occupiedBoat.transform.forward,
            -occupiedBoat.transform.forward,
            (occupiedBoat.transform.right + occupiedBoat.transform.forward).normalized,
            (-occupiedBoat.transform.right + occupiedBoat.transform.forward).normalized,
            (occupiedBoat.transform.right - occupiedBoat.transform.forward).normalized,
            (-occupiedBoat.transform.right - occupiedBoat.transform.forward).normalized
        };

        float[] reaches = { 2.2f, 3.4f, 4.6f };
        Vector3 best = Vector3.zero;
        float bestHeight = float.NegativeInfinity;
        bool found = false;

        for (int r = 0; r < reaches.Length; r++)
        {
            for (int i = 0; i < sides.Length; i++)
            {
                Vector3 probe = hull + sides[i] * reaches[r];
                float groundY = terrain.SampleHeight(probe) + terrain.transform.position.y;
                if (groundY < waterY - 0.3f || groundY < bestHeight)
                    continue;

                best = probe;
                best.y = groundY + 0.02f;
                bestHeight = groundY;
                found = true;
            }

            if (found)
                break;
        }

        if (found)
        {
            position = best;
            return true;
        }

        // Already sitting on the sand: step off the higher side.
        float hullGround = terrain.SampleHeight(hull) + terrain.transform.position.y;
        if (hullGround < waterY - 0.3f)
            return false;

        Vector3 inland = occupiedBoat.transform.right;
        float rightY = terrain.SampleHeight(hull + inland * 2.4f) + terrain.transform.position.y;
        float leftY = terrain.SampleHeight(hull - inland * 2.4f) + terrain.transform.position.y;
        Vector3 side = rightY >= leftY ? inland : -inland;
        position = hull + side * 2.4f;
        position.y = Mathf.Max(rightY, leftY) + 0.02f;
        return true;
    }

    void ResolveScene()
    {
        if (waterRenderer != null)
            return;

        var water = GameObject.Find("Surface");
        if (water != null)
            waterRenderer = water.GetComponent<Renderer>();
    }

    BoatDock FindClosestLanding(Vector3 from, float range)
    {
        if (landings == null || Time.time >= landingsUntil)
        {
            landings = FindObjectsByType<BoatDock>();
            landingsUntil = Time.time + 0.4f;
        }

        BoatDock closest = null;
        float best = range;
        for (int i = 0; i < landings.Length; i++)
        {
            BoatDock landing = landings[i];
            if (landing == null)
                continue;

            float d = Vector3.Distance(from, landing.transform.position);
            if (d <= best)
            {
                best = d;
                closest = landing;
            }
        }

        return closest;
    }

    BoatMotor FindClosestBoat(float range)
    {
        if (boats == null || Time.time >= boatsUntil)
        {
            boats = FindObjectsByType<BoatMotor>();
            boatsUntil = Time.time + 0.4f;
        }

        BoatMotor closest = null;
        float best = range;
        for (int i = 0; i < boats.Length; i++)
        {
            BoatMotor boat = boats[i];
            if (boat == null)
                continue;

            float d = Vector3.Distance(transform.position, boat.transform.position);
            if (d <= best)
            {
                best = d;
                closest = boat;
            }
        }

        return closest;
    }
}
