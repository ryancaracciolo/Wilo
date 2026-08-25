using System;
using System.Collections;
using UnityEngine;

/// <summary>What the player is told when a new morning starts.</summary>
public readonly struct DayReport
{
    public readonly bool Forced;
    public readonly string DateLabel;
    public readonly string SeasonLabel;
    public readonly string TimeLabel;

    public DayReport(bool forced, string dateLabel, string seasonLabel, string timeLabel)
    {
        Forced = forced;
        DateLabel = dateLabel;
        SeasonLabel = seasonLabel;
        TimeLabel = timeLabel;
    }
}

/// <summary>
/// Ends the fishing day. Warns before curfew, lets the player turn in at the
/// dock, and ferries them home if they are still out when it gets dark.
/// Curfew follows dusk, so winter days end earlier than summer ones.
/// </summary>
public class DayCycle : MonoBehaviour
{
    /// <summary>
    /// The schedule is measured from 3 AM rather than dawn: dawn moves with the
    /// season, so anchoring to it would make a pre-dawn winter morning compare
    /// as "after curfew". Nothing in the cycle happens between midnight and 3 AM.
    /// </summary>
    const float DayAnchorHour = 3f;

    [SerializeField] WorldConditions conditions;

    [Header("Schedule")]
    [Tooltip("Curfew lands this many hours after dusk, so it moves with the season.")]
    [SerializeField] float curfewAfterDuskHours = 1f;
    [Tooltip("How long before curfew the player is warned.")]
    [SerializeField] float warningLeadHours = 1.5f;
    [Tooltip("Wake this long after dawn, so mornings track the season. Must not be negative or the new day reads as overdue.")]
    [SerializeField, Min(0f)] float wakeAfterDawnHours = 0f;

    [Header("Dock")]
    [Tooltip("Scene object the player must stand near to turn in. Falls back to the home anchor.")]
    [SerializeField] string dockObjectName = "EndPlatform";
    [SerializeField] float dockRadius = 11f;
    [Tooltip("Where the player wakes up. Defaults to wherever they started.")]
    [SerializeField] Transform homeAnchor;

    [Header("Transition")]
    [SerializeField] float fadeDuration = 0.8f;
    [SerializeField] float heldBlackSeconds = 0.35f;

    Transform player;
    Transform dock;
    PlayerFishing fishing;
    PlayerBoatInteractor boatInteractor;
    BoatMotor boat;
    Vector3 homePosition;
    Quaternion homeRotation;
    Vector3 mooringPosition;
    Quaternion mooringRotation;
    bool hasHome;
    bool warned;

    /// <summary>Transient banner text, such as the curfew warning.</summary>
    public event Action<string> Notice;

    /// <summary>
    /// Raised once the day is ending but before the clock moves, so systems that
    /// care about today can settle up. Tournaments weigh in here.
    /// </summary>
    public event Action BeforeTurnIn;

    /// <summary>Target alpha and duration for the HUD blackout.</summary>
    public event Action<float, float> FadeRequested;

    public event Action<DayReport> Morning;

    public bool IsTurningIn { get; private set; }
    public float CurfewHour => conditions != null ? conditions.DuskHour + curfewAfterDuskHours : 21f;
    public float WarningHour => CurfewHour - Mathf.Max(0.25f, warningLeadHours);
    public float WakeHour => conditions != null
        ? Mathf.Repeat(conditions.DawnHour + wakeAfterDawnHours, 24f)
        : 6.5f;

    public bool PastWarning => SinceAnchor(CurrentHour) >= SinceAnchor(WarningHour);
    public bool NearDock => IsNearDock();

    /// <summary>True when the player is at the dock late enough that turning in makes sense.</summary>
    public bool CanTurnIn => !IsTurningIn && PastWarning && NearDock;

    public string CurfewLabel => GameCalendar.FormatHour(CurfewHour);

    void Awake()
    {
        if (conditions == null)
            conditions = FindFirstObjectByType<WorldConditions>();
    }

    void Start()
    {
        ResolvePlayer();
        CaptureHome();
    }

    void Update()
    {
        if (IsTurningIn || conditions == null)
            return;

        ResolvePlayer();
        if (!hasHome)
            CaptureHome();

        float since = SinceAnchor(CurrentHour);
        if (since < SinceAnchor(WarningHour))
        {
            warned = false;
            return;
        }

        if (since >= SinceAnchor(CurfewHour))
        {
            TurnIn(true);
            return;
        }

        if (warned)
            return;

        warned = true;
        Notice?.Invoke($"Getting late. Be back at the dock by {CurfewLabel}.");
    }

    /// <summary>Ends the day. Forced runs happen at curfew, wherever the player is.</summary>
    public void TurnIn(bool forced)
    {
        if (IsTurningIn || !isActiveAndEnabled)
            return;
        StartCoroutine(TurnInRoutine(forced));
    }

    IEnumerator TurnInRoutine(bool forced)
    {
        IsTurningIn = true;
        FadeRequested?.Invoke(1f, fadeDuration);
        yield return new WaitForSecondsRealtime(fadeDuration);

        BeforeTurnIn?.Invoke();
        ReturnHome();
        conditions?.AdvanceToHour(WakeHour);
        yield return new WaitForSecondsRealtime(heldBlackSeconds);

        var report = new DayReport(
            forced,
            conditions != null ? conditions.DateLabel : "",
            conditions != null ? conditions.SeasonLabel : "",
            conditions != null ? conditions.TimeLabel : "");

        FadeRequested?.Invoke(0f, fadeDuration);
        Morning?.Invoke(report);
        IsTurningIn = false;
        warned = false;
    }

    void ReturnHome()
    {
        fishing?.AbortFishing();
        boatInteractor?.ForceDisembark();

        if (boat != null)
            boat.transform.SetPositionAndRotation(mooringPosition, mooringRotation);

        if (player == null || !hasHome)
            return;

        Vector3 position = homeAnchor != null ? homeAnchor.position : homePosition;
        Quaternion rotation = homeAnchor != null ? homeAnchor.rotation : homeRotation;

        var controller = player.GetComponent<CharacterController>();
        if (controller != null)
            controller.enabled = false;
        player.SetPositionAndRotation(position, rotation);
        if (controller != null)
            controller.enabled = true;
    }

    void ResolvePlayer()
    {
        if (player == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go == null)
                return;
            player = go.transform;
            fishing = go.GetComponent<PlayerFishing>();
            boatInteractor = go.GetComponent<PlayerBoatInteractor>();
        }

        if (boat == null)
            boat = FindFirstObjectByType<BoatMotor>();
        if (dock == null && !string.IsNullOrEmpty(dockObjectName))
        {
            var go = GameObject.Find(dockObjectName);
            if (go != null)
                dock = go.transform;
        }
    }

    void CaptureHome()
    {
        if (player == null)
            return;

        homePosition = player.position;
        homeRotation = player.rotation;
        hasHome = true;

        if (boat != null)
        {
            mooringPosition = boat.transform.position;
            mooringRotation = boat.transform.rotation;
        }
    }

    bool IsNearDock()
    {
        if (player == null)
            return false;

        Vector3 target = dock != null
            ? dock.position
            : homeAnchor != null ? homeAnchor.position : homePosition;
        if (dock == null && !hasHome && homeAnchor == null)
            return false;

        Vector3 a = player.position;
        a.y = 0f;
        target.y = 0f;
        return Vector3.Distance(a, target) <= dockRadius;
    }

    float CurrentHour => conditions != null ? conditions.Hour : GameCalendar.SolarNoonHour;

    /// <summary>Hours since the 3 AM anchor, so evening and after-midnight stay ordered.</summary>
    static float SinceAnchor(float hour)
    {
        return Mathf.Repeat(hour - DayAnchorHour, 24f);
    }
}
