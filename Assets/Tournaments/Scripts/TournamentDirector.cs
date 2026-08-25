using System;
using System.Collections.Generic;
using UnityEngine;

public enum TournamentPhase
{
    /// <summary>Nothing running. The player may or may not hold registrations.</summary>
    Idle,

    /// <summary>Inside the fishing window; catches count toward the bag.</summary>
    Running,

    /// <summary>Window closed, bag not yet weighed. The dock is the scales.</summary>
    AwaitingWeighIn
}

/// <summary>
/// Runtime hub for tournaments: registration and entry fees, the live bag during
/// a window, weigh-in at the dock, placing against the generated field, and the
/// payout. Scheduling and field generation live in the plain-C# helpers; this
/// class only owns the scene-bound state.
///
/// Windows are assumed to sit inside one daylight day, so hours compare directly
/// without wrapping past midnight.
/// </summary>
public class TournamentDirector : MonoBehaviour
{
    [SerializeField] WorldConditions conditions;
    [SerializeField] DayCycle dayCycle;
    [SerializeField] PlayerProgress progress;

    [Tooltip("Every event on the calendar. One per weekday slot for now.")]
    [SerializeField] List<TournamentDefinition> definitions = new List<TournamentDefinition>();

    [SerializeField, Min(1)] int scheduleLength = 4;

    readonly List<TournamentOccurrence> registrations = new List<TournamentOccurrence>();
    readonly List<TournamentOccurrence> upcoming = new List<TournamentOccurrence>();
    readonly List<TournamentStanding> standings = new List<TournamentStanding>();
    readonly List<TournamentResult> history = new List<TournamentResult>();
    readonly TournamentBag bag = new TournamentBag();

    TournamentOccurrence active;
    bool subscribed;
    bool hookedDayCycle;

    /// <summary>Short banner lines, matching the day cycle's notices.</summary>
    public event Action<string> Notice;

    public event Action BagChanged;
    public event Action<TournamentResult> Finished;

    public TournamentPhase Phase { get; private set; } = TournamentPhase.Idle;
    public TournamentOccurrence Active => active;
    public TournamentDefinition ActiveDefinition => active.Definition;
    public bool IsRunning => Phase == TournamentPhase.Running;
    public bool AwaitingWeighIn => Phase == TournamentPhase.AwaitingWeighIn;

    public int BagFish => bag.Fish;
    public float BagPounds => bag.Pounds;
    public int BagLimit => bag.Limit;
    public IReadOnlyList<TournamentResult> History => history;
    public IReadOnlyList<TournamentDefinition> Definitions => definitions;

    /// <summary>Next few dated events, nearest first.</summary>
    public IReadOnlyList<TournamentOccurrence> Upcoming
    {
        get
        {
            if (conditions != null)
                TournamentSchedule.Upcoming(definitions, conditions.Calendar, scheduleLength, upcoming);
            return upcoming;
        }
    }

    /// <summary>One line for the HUD chip, or empty when nothing is live.</summary>
    public string StatusLine
    {
        get
        {
            TournamentDefinition def = active.Definition;
            if (def == null)
                return "";

            if (Phase == TournamentPhase.Running)
            {
                string weight = $"{BagPounds:0.00} lb";
                string count = def.BagLimit > 1 ? $"{BagFish}/{def.BagLimit}" : $"{BagFish}";
                return $"{def.DisplayName}  ·  {count}  ·  {weight}  ·  lines out {GameCalendar.FormatHour(def.EndHour)}";
            }

            if (Phase == TournamentPhase.AwaitingWeighIn)
                return $"{def.DisplayName}  ·  {BagPounds:0.00} lb  ·  weigh in at the dock";

            return "";
        }
    }

    void OnEnable()
    {
        Resolve();
        Subscribe();
    }

    void OnDisable()
    {
        if (subscribed && progress != null)
            progress.Caught -= OnCaught;
        if (dayCycle != null)
            dayCycle.BeforeTurnIn -= OnBeforeTurnIn;
        subscribed = false;
        hookedDayCycle = false;
    }

    /// <summary>
    /// Heading home settles the day's bag while the clock still reads today, so a
    /// short winter curfew cannot strand a bag that never reached the scales.
    /// </summary>
    void OnBeforeTurnIn()
    {
        if (Phase != TournamentPhase.Idle)
            Finish(false);
    }

    void Update()
    {
        Resolve();
        Subscribe();
        if (conditions == null)
            return;

        int today = conditions.DayIndex;
        float hour = conditions.Hour;

        if (Phase != TournamentPhase.Idle)
        {
            TickLive(today, hour);
            return;
        }

        PruneRegistrations(today);
        TryStart(today, hour);
    }

    void TickLive(int today, float hour)
    {
        TournamentDefinition def = active.Definition;
        if (def == null)
        {
            Phase = TournamentPhase.Idle;
            return;
        }

        // A rolled-over day means the bag never reached the scales.
        if (active.DayIndex != today)
        {
            Finish(true);
            return;
        }

        if (Phase == TournamentPhase.Running)
        {
            if (hour < def.EndHour)
                return;

            Phase = TournamentPhase.AwaitingWeighIn;
            BagChanged?.Invoke();
            Notice?.Invoke($"Lines out. Bring your bag to the dock by {GameCalendar.FormatHour(def.EndHour + def.ForfeitAfterHours)}.");
            return;
        }

        if (hour - def.EndHour >= def.ForfeitAfterHours)
        {
            Finish(true);
            return;
        }

        bool turningIn = dayCycle != null && dayCycle.IsTurningIn;
        if (!turningIn && dayCycle != null && dayCycle.NearDock)
            Finish(false);
    }

    void TryStart(int today, float hour)
    {
        for (int i = 0; i < registrations.Count; i++)
        {
            TournamentOccurrence occ = registrations[i];
            TournamentDefinition def = occ.Definition;
            if (def == null || occ.DayIndex != today)
                continue;
            if (hour < def.StartHour || hour >= def.EndHour)
                continue;

            registrations.RemoveAt(i);
            active = occ;
            Phase = TournamentPhase.Running;
            bag.Reset(def.BagLimit);
            BagChanged?.Invoke();
            Notice?.Invoke($"{def.DisplayName} is underway. {def.FormatLabel} until {GameCalendar.FormatHour(def.EndHour)}.");
            return;
        }
    }

    public bool IsRegistered(TournamentOccurrence occurrence)
    {
        for (int i = 0; i < registrations.Count; i++)
        {
            if (registrations[i].SameAs(occurrence))
                return true;
        }

        return false;
    }

    /// <summary>True when the player could still enter: not entered, not started, fee covered.</summary>
    public bool CanRegister(TournamentOccurrence occurrence)
    {
        if (!occurrence.IsValid || IsRegistered(occurrence) || Phase != TournamentPhase.Idle)
            return false;
        if (HasPassed(occurrence))
            return false;
        return AffordableFee(occurrence.Definition);
    }

    public bool Register(TournamentOccurrence occurrence)
    {
        if (!CanRegister(occurrence))
            return false;

        TournamentDefinition def = occurrence.Definition;
        AdjustMoney(-def.EntryFee);
        registrations.Add(occurrence);
        Notice?.Invoke(def.EntryFee > 0
            ? $"Entered {def.DisplayName}. ${def.EntryFee} entry paid."
            : $"Entered {def.DisplayName}.");
        return true;
    }

    /// <summary>Backing out before the window opens refunds the entry.</summary>
    public bool Withdraw(TournamentOccurrence occurrence)
    {
        for (int i = 0; i < registrations.Count; i++)
        {
            if (!registrations[i].SameAs(occurrence))
                continue;

            TournamentDefinition def = registrations[i].Definition;
            registrations.RemoveAt(i);
            if (def != null && def.EntryFee > 0)
            {
                AdjustMoney(def.EntryFee);
                Notice?.Invoke($"Withdrew from {def.DisplayName}. ${def.EntryFee} refunded.");
            }
            else if (def != null)
            {
                Notice?.Invoke($"Withdrew from {def.DisplayName}.");
            }

            return true;
        }

        return false;
    }

    /// <summary>Whether this event's window has already closed today.</summary>
    public bool HasPassed(TournamentOccurrence occurrence)
    {
        if (!occurrence.IsValid || conditions == null)
            return false;
        if (occurrence.DayIndex > conditions.DayIndex)
            return false;
        return occurrence.DayIndex < conditions.DayIndex || conditions.Hour >= occurrence.Definition.EndHour;
    }

    public bool AffordableFee(TournamentDefinition definition)
    {
        if (definition == null)
            return false;
        if (definition.EntryFee <= 0)
            return true;
        return progress != null && progress.Money >= definition.EntryFee;
    }

    void Finish(bool forfeited)
    {
        TournamentDefinition def = active.Definition;
        if (def == null)
        {
            Phase = TournamentPhase.Idle;
            return;
        }

        TournamentField.Build(active, Bite(), standings);

        float raw = bag.Pounds;
        float penalty = 0f;
        if (!forfeited)
        {
            float late = Mathf.Max(0f, conditions.Hour - def.EndHour);
            penalty = Mathf.Floor(late / 0.5f) * def.LatePenaltyPerHalfHour;
        }

        float scored = forfeited ? 0f : Mathf.Max(0f, raw - penalty);
        standings.Add(new TournamentStanding
        {
            Name = progress != null ? progress.DisplayName : "You",
            Pounds = Mathf.Round(scored * 100f) * 0.01f,
            Fish = forfeited ? 0 : bag.Fish,
            IsPlayer = true
        });
        standings.Sort(TournamentField.CompareHeaviest);

        int place = standings.Count;
        for (int i = 0; i < standings.Count; i++)
        {
            if (standings[i].IsPlayer)
            {
                place = i + 1;
                break;
            }
        }

        // A blank bag never pays, however the rest of the field did.
        int payout = forfeited || scored <= 0.01f ? 0 : def.PayoutFor(place);
        AdjustMoney(payout);

        TournamentStanding winner = standings.Count > 0 ? standings[0] : default;
        var result = new TournamentResult
        {
            Id = def.Id,
            DisplayName = def.DisplayName,
            FormatLabel = def.FormatLabel,
            DayIndex = active.DayIndex,
            DateLabel = conditions.DateLabel,
            Place = place,
            Entrants = standings.Count,
            Fish = forfeited ? 0 : bag.Fish,
            RawPounds = raw,
            Penalty = penalty,
            Pounds = scored,
            EntryFee = def.EntryFee,
            Payout = payout,
            Forfeited = forfeited,
            WinnerName = winner.Name,
            WinnerPounds = winner.Pounds
        };

        history.Insert(0, result);
        Phase = TournamentPhase.Idle;
        active = default;
        bag.Reset(def.BagLimit);
        BagChanged?.Invoke();
        Finished?.Invoke(result);
    }

    /// <summary>
    /// How well the field should do today. Derived from the seasonal phase for
    /// now; the weather pass can replace this with a real bite estimate.
    /// </summary>
    float Bite()
    {
        if (conditions == null)
            return 1f;

        return conditions.Phase switch
        {
            FishingPhase.Prespawn => 1.1f,
            FishingPhase.Spawn => 1.15f,
            FishingPhase.Postspawn => 0.9f,
            FishingPhase.Summer => 1f,
            FishingPhase.FallFeeding => 1.1f,
            _ => 0.7f
        };
    }

    void PruneRegistrations(int today)
    {
        for (int i = registrations.Count - 1; i >= 0; i--)
        {
            TournamentOccurrence occ = registrations[i];
            if (occ.Definition != null && occ.DayIndex >= today)
                continue;

            registrations.RemoveAt(i);
            if (occ.Definition != null)
                Notice?.Invoke($"You missed {occ.Definition.DisplayName}.");
        }
    }

    /// <summary>Positive adds to the wallet, negative takes from it.</summary>
    void AdjustMoney(int delta)
    {
        if (progress == null || delta == 0)
            return;
        progress.SetMoney(progress.Money + delta);
    }

    void OnCaught(CatchRecord record)
    {
        if (Phase != TournamentPhase.Running)
            return;
        if (bag.Consider(record))
            BagChanged?.Invoke();
    }

    void Resolve()
    {
        if (conditions == null)
            conditions = FindFirstObjectByType<WorldConditions>();
        if (dayCycle == null)
            dayCycle = FindFirstObjectByType<DayCycle>();
        if (progress != null)
            return;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            progress = player.GetComponent<PlayerProgress>();
    }

    void Subscribe()
    {
        if (!subscribed && progress != null)
        {
            progress.Caught += OnCaught;
            subscribed = true;
        }

        if (!hookedDayCycle && dayCycle != null)
        {
            dayCycle.BeforeTurnIn += OnBeforeTurnIn;
            hookedDayCycle = true;
        }
    }
}
