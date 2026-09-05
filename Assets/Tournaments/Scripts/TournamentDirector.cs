using System;
using System.Collections.Generic;
using UnityEngine;

public enum TournamentPhase
{
    /// <summary>Nothing running. The player may or may not hold registrations.</summary>
    Idle,

    /// <summary>Inside the fishing window; catches count toward the bag.</summary>
    Running,

    /// <summary>Window closed, bag not yet weighed. The camp is the scales.</summary>
    AwaitingWeighIn
}

/// <summary>
/// Runtime hub for tournaments: registration and entry fees, the live bag during
/// a window, weigh-in at the camp, placing against the generated field, and the
/// payout. Scheduling and field generation live in the plain-C# helpers; this
/// class only owns the scene-bound state.
///
/// Blast-off and weigh-in happen at <see cref="TournamentSite"/>, not the cabin.
/// Windows sit inside one daylight day, so hours compare directly without
/// wrapping past midnight.
/// </summary>
public class TournamentDirector : MonoBehaviour
{
    [SerializeField] WorldConditions conditions;
    [SerializeField] DayCycle dayCycle;
    [SerializeField] PlayerProgress progress;
    [SerializeField] TournamentSite site;

    [Tooltip("Every event on the calendar. Several may share a weekday.")]
    [SerializeField] List<TournamentDefinition> definitions = new List<TournamentDefinition>();

    [SerializeField, Min(1)] int scheduleLength = 8;

    [Tooltip("Hour an entered tournament morning starts at camp. Ordinary days still use the cabin wake.")]
    [SerializeField, Range(0f, 24f)] float tournamentWakeHour = 6.5f;

    [Tooltip("Hours after blast-off the player can still check in if they wander off the grounds.")]
    [SerializeField, Min(0f)] float lateCheckInHours = 0.5f;

    readonly List<TournamentOccurrence> registrations = new List<TournamentOccurrence>();
    readonly List<TournamentOccurrence> upcoming = new List<TournamentOccurrence>();
    readonly List<TournamentStanding> standings = new List<TournamentStanding>();
    readonly List<TournamentResult> history = new List<TournamentResult>();
    readonly TournamentBag bag = new TournamentBag();

    TournamentOccurrence active;
    bool subscribed;
    bool hookedDayCycle;
    bool warned;
    int campNoticeDay = -1;

    /// <summary>Short banner lines, matching the day cycle's notices.</summary>
    public event Action<string> Notice;

    public event Action BagChanged;
    public event Action<CatchRecord> CullRequired;
    public event Action<TournamentResult> Finished;
    public event Action<TournamentPhase> PhaseChanged;

    public TournamentPhase Phase { get; private set; } = TournamentPhase.Idle;
    public bool IsFriendEvent { get; private set; }
    public TournamentOccurrence Active => active;
    public TournamentDefinition ActiveDefinition => active.Definition;
    public bool IsRunning => Phase == TournamentPhase.Running;
    public bool AwaitingWeighIn => Phase == TournamentPhase.AwaitingWeighIn;

    public int BagFish => bag.Fish;
    public float BagPounds => bag.Pounds;
    public int BagLimit => bag.Limit;
    public IReadOnlyList<CatchRecord> Bag => bag.Kept;
    public IReadOnlyList<TournamentResult> History => history;
    public IReadOnlyList<TournamentDefinition> Definitions => definitions;

    /// <summary>The catch waiting to be culled into the bag, or null.</summary>
    public CatchRecord PendingCull { get; private set; }

    /// <summary>Player chose to replace the fish at <paramref name="index"/>.</summary>
    public void AcceptCull(int index)
    {
        if (PendingCull == null)
            return;

        bag.Replace(index, PendingCull);
        PendingCull = null;
        BagChanged?.Invoke();
    }

    /// <summary>Player chose to release the new catch.</summary>
    public void ReleaseCull()
    {
        PendingCull = null;
    }

    /// <summary>Live entries, nearest day first is the caller's job.</summary>
    public void CopyRegistrations(List<TournamentOccurrence> into)
    {
        into.Clear();
        into.AddRange(registrations);
    }

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
                if (IsFriendEvent)
                    return $"{def.DisplayName}  ·  {count}  ·  {weight}  ·  weigh in when you're done";
                return $"{def.DisplayName}  ·  {count}  ·  {weight}  ·  lines out {GameCalendar.FormatHour(def.EndHour)}";
            }

            if (Phase == TournamentPhase.AwaitingWeighIn)
            {
                string close = GameCalendar.FormatHour(def.EndHour + def.ForfeitAfterHours);
                return $"{def.DisplayName}  ·  {BagPounds:0.00} lb  ·  camp weigh-in by {close}";
            }

            return "";
        }
    }

    void Awake()
    {
        ApplyFrom(SaveService.Instance);
    }

    void OnEnable()
    {
        Resolve();
        Subscribe();
    }

    /// <summary>
    /// Restores entries, results, and a tournament that was still running when
    /// the player quit, so coming back mid-event does not hand out a fresh bag.
    /// </summary>
    void ApplyFrom(SaveService save)
    {
        if (save == null || save.IsNewGame)
            return;

        TournamentData data = save.Player.tournaments;

        registrations.Clear();
        for (int i = 0; i < data.registrations.Count; i++)
        {
            TournamentRegistrationData entry = data.registrations[i];
            TournamentDefinition def = FindDefinition(entry.definitionId);
            if (def != null)
                registrations.Add(new TournamentOccurrence(def, entry.dayIndex));
        }

        history.Clear();
        for (int i = 0; i < data.history.Count; i++)
        {
            if (data.history[i] != null)
                history.Add(data.history[i]);
        }

        Phase = (TournamentPhase)data.phase;
        if (Phase == TournamentPhase.Idle)
            return;

        TournamentDefinition activeDef = FindDefinition(data.activeDefinitionId);
        if (activeDef == null)
        {
            Phase = TournamentPhase.Idle;
            return;
        }

        active = new TournamentOccurrence(activeDef, data.activeDayIndex);
        bag.Reset(data.bagLimit);
        for (int i = 0; i < data.bag.Count; i++)
            bag.Consider(data.bag[i]);
    }

    public void CaptureTo(PlayerSave save)
    {
        if (save == null)
            return;

        TournamentData data = save.tournaments;

        data.registrations.Clear();
        for (int i = 0; i < registrations.Count; i++)
        {
            TournamentDefinition def = registrations[i].Definition;
            if (def == null)
                continue;

            data.registrations.Add(new TournamentRegistrationData
            {
                definitionId = def.Id,
                dayIndex = registrations[i].DayIndex
            });
        }

        data.history.Clear();
        data.history.AddRange(history);

        if (IsFriendEvent)
        {
            data.phase = (int)TournamentPhase.Idle;
            data.activeDefinitionId = "";
            data.activeDayIndex = 0;
            data.bagLimit = bag.Limit;
            data.bag.Clear();
            return;
        }

        data.phase = (int)Phase;
        data.activeDefinitionId = active.Definition != null ? active.Definition.Id : "";
        data.activeDayIndex = active.DayIndex;
        data.bagLimit = bag.Limit;
        data.bag.Clear();
        data.bag.AddRange(bag.Kept);
    }

    public TournamentDefinition FindDefinition(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        for (int i = 0; i < definitions.Count; i++)
        {
            if (definitions[i] != null && definitions[i].Id == id)
                return definitions[i];
        }

        Debug.LogWarning($"Tournaments: saved event '{id}' is no longer on the schedule.", this);
        return null;
    }

    void OnDisable()
    {
        if (subscribed && progress != null)
            progress.Caught -= OnCaught;
        if (dayCycle != null)
        {
            dayCycle.BeforeTurnIn -= OnBeforeTurnIn;
            dayCycle.Morning -= OnMorning;
        }
        subscribed = false;
        hookedDayCycle = false;
    }

    /// <summary>
    /// Going home without the camp scales is a no-show. The bag is already dead
    /// if lines-out plus the forfeit window passed on the water.
    /// </summary>
    void OnBeforeTurnIn()
    {
        if (IsFriendEvent)
        {
            TournamentLobby.Instance?.ForfeitAndLeave();
            return;
        }

        if (Phase != TournamentPhase.Idle)
            Finish(true);
    }

    void OnMorning(DayReport _)
    {
        TryCampNotice();
    }

    /// <summary>
    /// Once per tournament morning, including a save loaded already on that day.
    /// </summary>
    void TryCampNotice()
    {
        if (conditions == null || Phase != TournamentPhase.Idle)
            return;

        int today = conditions.DayIndex;
        if (campNoticeDay == today)
            return;

        for (int i = 0; i < registrations.Count; i++)
        {
            TournamentOccurrence occ = registrations[i];
            TournamentDefinition def = occ.Definition;
            if (def == null || occ.DayIndex != today)
                continue;
            if (conditions.Hour >= def.StartHour)
                return;

            campNoticeDay = today;
            Notice?.Invoke($"{def.DisplayName} is this morning. Blast-off is at {GameCalendar.FormatHour(def.StartHour)}.");
            return;
        }
    }

    void Update()
    {
        Resolve();
        Subscribe();
        if (conditions == null)
            return;

        int today = conditions.DayIndex;
        float hour = conditions.Hour;

        if (IsFriendEvent)
            return;

        if (Phase != TournamentPhase.Idle)
        {
            TickLive(today, hour);
            return;
        }

        PruneRegistrations(today);
        TryStart(today, hour);
        TryCampNotice();
    }

    void TickLive(int today, float hour)
    {
        TournamentDefinition def = active.Definition;
        if (def == null)
        {
            SetPhase(TournamentPhase.Idle);
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
            {
                TryWarn(def, hour);
                return;
            }

            SetPhase(TournamentPhase.AwaitingWeighIn);
            BagChanged?.Invoke();
            int interval = Mathf.Max(1, Mathf.RoundToInt(def.LatePenaltyIntervalMinutes));
            Notice?.Invoke($"Lines out. Weigh in at the camp by {GameCalendar.FormatHour(def.EndHour + def.ForfeitAfterHours)} or you're out. −{def.LatePenaltyPounds:0.#} lb every {interval} min.");
            return;
        }

        if (hour - def.EndHour >= def.ForfeitAfterHours)
        {
            Finish(true);
            return;
        }

        bool turningIn = dayCycle != null && dayCycle.IsTurningIn;
        if (!turningIn && AtSite)
            Finish(false);
    }

    void TryWarn(TournamentDefinition def, float hour)
    {
        if (warned || def == null || def.WarningLeadHours <= 0.01f)
            return;
        if (hour < def.WarningHour)
            return;

        warned = true;
        int minutes = Mathf.Max(1, Mathf.RoundToInt(def.WarningLeadHours * 60f));
        Notice?.Invoke($"{minutes} minutes to lines out. Be at the camp by {GameCalendar.FormatHour(def.EndHour)}.");
    }

    void TryStart(int today, float hour)
    {
        for (int i = registrations.Count - 1; i >= 0; i--)
        {
            TournamentOccurrence occ = registrations[i];
            TournamentDefinition def = occ.Definition;
            if (def == null || occ.DayIndex != today)
                continue;
            if (hour < def.StartHour)
                continue;

            if (hour < def.EndHour && AtSite)
            {
                registrations.RemoveAt(i);
                active = occ;
                warned = false;
                SetPhase(TournamentPhase.Running);
                bag.Reset(def.BagLimit);
                BagChanged?.Invoke();
                var boats = FindFirstObjectByType<TournamentBoatDirector>();
                string pick = boats != null && boats.PlayerTakeoff > 0
                    ? $" You're boat {boats.PlayerTakeoff} of {boats.TakeoffCount}."
                    : "";
                Notice?.Invoke($"{def.DisplayName} is underway.{pick} {def.FormatLabel} until {GameCalendar.FormatHour(def.EndHour)}.");
                return;
            }

            if (hour < def.EndHour && hour < def.StartHour + lateCheckInHours)
                continue;

            registrations.RemoveAt(i);
            Notice?.Invoke($"You missed blast-off for {def.DisplayName}. Be at the camp by {GameCalendar.FormatHour(def.StartHour)} next time.");
        }
    }

    /// <summary>True when the player is in the camp pocket for blast-off or scales.</summary>
    public bool AtSite
    {
        get
        {
            if (site == null)
                site = FindFirstObjectByType<TournamentSite>();
            if (site == null || progress == null)
                return false;
            return site.Contains(ProbePosition());
        }
    }

    /// <summary>The hull if they are still in the boat, otherwise where they stand.</summary>
    Vector3 ProbePosition()
    {
        var boats = progress.GetComponent<PlayerBoatInteractor>();
        if (boats != null && boats.OccupiedBoat != null)
            return boats.OccupiedBoat.transform.position;
        return progress.transform.position;
    }

    /// <summary>
    /// Camp wake on a morning the player has entered. False when that day
    /// has no entry, so the cabin hour stays in effect.
    /// </summary>
    public bool TryGetWakeHour(int dayIndex, out float hour)
    {
        hour = 0f;
        if (!HasRegistrationOn(dayIndex))
            return false;

        hour = Mathf.Repeat(tournamentWakeHour, 24f);
        return true;
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

    /// <summary>True when the player could still enter: unlocked, not entered, not today, fee covered, and not already booked that morning.</summary>
    public bool CanRegister(TournamentOccurrence occurrence)
    {
        if (!occurrence.IsValid || IsRegistered(occurrence) || Phase != TournamentPhase.Idle)
            return false;
        if (EntryClosed(occurrence))
            return false;
        if (!MeetsReputation(occurrence.Definition))
            return false;
        if (HasRegistrationOn(occurrence.DayIndex))
            return false;
        return AffordableFee(occurrence.Definition);
    }

    /// <summary>Signups close the night before, so a camp morning only happens after an advance entry.</summary>
    public bool EntryClosed(TournamentOccurrence occurrence)
    {
        if (!occurrence.IsValid || conditions == null)
            return false;
        return occurrence.DayIndex <= conditions.DayIndex;
    }

    public bool MeetsReputation(TournamentDefinition definition)
    {
        if (definition == null)
            return false;
        if (definition.ReputationRequired <= 0)
            return true;
        return progress != null && progress.Reputation >= definition.ReputationRequired;
    }

    /// <summary>True when another event is already entered on this calendar day.</summary>
    public bool HasRegistrationOn(int dayIndex)
    {
        return TryGetEntryOn(dayIndex, out _);
    }

    /// <summary>The entry booked on this calendar day, if any.</summary>
    public bool TryGetEntryOn(int dayIndex, out TournamentOccurrence occurrence)
    {
        for (int i = 0; i < registrations.Count; i++)
        {
            if (registrations[i].DayIndex != dayIndex || !registrations[i].IsValid)
                continue;

            occurrence = registrations[i];
            return true;
        }

        occurrence = default;
        return false;
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

    /// <summary>True when the player can sleep straight through to this event.</summary>
    public bool CanSkipTo(TournamentOccurrence occurrence)
    {
        if (!occurrence.IsValid || Phase != TournamentPhase.Idle)
            return false;
        if (conditions == null || dayCycle == null || dayCycle.IsTurningIn)
            return false;
        if (!MeetsReputation(occurrence.Definition))
            return false;
        return occurrence.DayIndex > conditions.DayIndex;
    }

    /// <summary>Entries a skip past this day would drop, nearest first.</summary>
    public void RegistrationsBefore(int dayIndex, List<TournamentOccurrence> into)
    {
        into.Clear();
        for (int i = 0; i < registrations.Count; i++)
        {
            if (registrations[i].DayIndex < dayIndex)
                into.Add(registrations[i]);
        }

        into.Sort((a, b) => a.DayIndex.CompareTo(b.DayIndex));
    }

    /// <summary>
    /// Sleeps ahead to an event's morning. Anything entered before it is treated
    /// as a withdrawal and refunded, since those days are never fished.
    /// </summary>
    public bool SkipTo(TournamentOccurrence occurrence)
    {
        if (!CanSkipTo(occurrence))
            return false;

        int refunded = 0;
        for (int i = registrations.Count - 1; i >= 0; i--)
        {
            if (registrations[i].DayIndex >= occurrence.DayIndex)
                continue;

            TournamentDefinition dropped = registrations[i].Definition;
            if (dropped != null)
                refunded += dropped.EntryFee;
            registrations.RemoveAt(i);
        }

        AdjustMoney(refunded);
        dayCycle.SkipToDay(occurrence.DayIndex);
        Notice?.Invoke(refunded > 0
            ? $"Skipped ahead to {occurrence.Definition.DisplayName}. ${refunded} in entries refunded."
            : $"Skipped ahead to {occurrence.Definition.DisplayName}.");
        return true;
    }

    /// <summary>Whether this event's window has already closed today.</summary>
    public bool HasPassed(TournamentOccurrence occurrence)
    {
        if (!occurrence.IsValid || conditions == null)
            return false;
        if (occurrence.DayIndex > conditions.DayIndex)
            return false;
        return occurrence.DayIndex < conditions.DayIndex || conditions.Hour >= occurrence.Definition.StartHour;
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
            SetPhase(TournamentPhase.Idle);
            return;
        }

        TournamentField.Build(active, Bite(), standings);

        float raw = bag.Pounds;
        float penalty = 0f;
        if (!forfeited && conditions != null)
            penalty = def.LatePenaltyFor(conditions.Hour - def.EndHour);

        float scored = forfeited ? 0f : Mathf.Max(0f, raw - penalty);
        float playerLm = forfeited ? 0f : bag.BestLargemouth;
        float playerSm = forfeited ? 0f : bag.BestSmallmouth;
        standings.Add(new TournamentStanding
        {
            Name = progress != null ? progress.DisplayName : "You",
            Pounds = Mathf.Round(scored * 100f) * 0.01f,
            Fish = forfeited ? 0 : bag.Fish,
            IsPlayer = true,
            LunkerLargemouth = playerLm,
            LunkerSmallmouth = playerSm
        });
        standings.Sort(TournamentField.CompareHeaviest);
        TournamentField.AwardLunkers(standings);

        int place = standings.Count;
        bool wonLm = false;
        bool wonSm = false;
        for (int i = 0; i < standings.Count; i++)
        {
            if (!standings[i].IsPlayer)
                continue;
            place = i + 1;
            wonLm = standings[i].WonLunkerLargemouth;
            wonSm = standings[i].WonLunkerSmallmouth;
            break;
        }

        // A blank bag never pays a place, however the rest of the field did.
        int placePayout = forfeited || scored <= 0.01f ? 0 : def.PayoutFor(place);
        int lunkerPay = 0;
        int lunkerRep = 0;
        if (!forfeited)
        {
            if (wonLm)
            {
                lunkerPay += def.LunkerPayout(true);
                lunkerRep += def.LunkerReputation(true);
            }

            if (wonSm)
            {
                lunkerPay += def.LunkerPayout(false);
                lunkerRep += def.LunkerReputation(false);
            }
        }

        int payout = placePayout + lunkerPay;
        AdjustMoney(payout);

        int reputation = def.ReputationFor(place, forfeited) + lunkerRep;
        if (progress != null)
            progress.AddReputation(reputation);

        TournamentStanding winner = standings.Count > 0 ? standings[0] : default;
        var result = new TournamentResult
        {
            Id = def.Id,
            DisplayName = def.DisplayName,
            FormatLabel = def.FormatLabel,
            DayIndex = active.DayIndex,
            DateLabel = conditions != null
                ? conditions.Calendar.DateLabelFor(active.DayIndex)
                : "",
            Place = place,
            Entrants = standings.Count,
            Fish = forfeited ? 0 : bag.Fish,
            RawPounds = raw,
            Penalty = penalty,
            Pounds = scored,
            EntryFee = def.EntryFee,
            Payout = payout,
            PlacePayout = placePayout,
            Reputation = reputation,
            Forfeited = forfeited,
            WinnerName = winner.Name,
            WinnerPounds = winner.Pounds,
            LunkerLargemouth = playerLm,
            LunkerSmallmouth = playerSm,
            WonLunkerLargemouth = wonLm,
            WonLunkerSmallmouth = wonSm,
            LunkerPayout = lunkerPay,
            LunkerReputation = lunkerRep,
            Standings = new List<TournamentStanding>(standings)
        };

        history.Insert(0, result);
        active = default;
        warned = false;
        PendingCull = null;
        bag.Reset(def.BagLimit);
        SetPhase(TournamentPhase.Idle);
        BagChanged?.Invoke();
        Finished?.Invoke(result);
        SaveService.Instance?.Save();
    }

    /// <summary>Host a friend lobby for this field, or for one already on Entered.</summary>
    public bool CanInvite(TournamentDefinition definition)
    {
        if (HasUpcomingEntry(definition))
            return true;
        if (!CanJoinFriend(definition))
            return false;
        return !ScheduledToday(definition);
    }

    /// <summary>Board signup for this field on a later morning. The entry fee is already paid.</summary>
    public bool HasUpcomingEntry(TournamentDefinition definition)
    {
        if (definition == null || conditions == null || Phase != TournamentPhase.Idle)
            return false;

        for (int i = 0; i < registrations.Count; i++)
        {
            TournamentOccurrence row = registrations[i];
            if (row.Definition == null || row.Definition.Id != definition.Id)
                continue;
            if (row.DayIndex <= conditions.DayIndex)
                continue;
            return true;
        }

        return false;
    }

    /// <summary>Join a lobby the host already opened. Guest calendar does not block; the host already picked the field.</summary>
    public bool CanJoinFriend(TournamentDefinition definition)
    {
        if (definition == null || Phase != TournamentPhase.Idle || IsFriendEvent)
            return false;
        if (!MeetsReputation(definition))
            return false;
        return AffordableFee(definition);
    }

    /// <summary>This field is on today's calendar. No same-day Enter or Invite.</summary>
    public bool ScheduledToday(TournamentDefinition definition)
    {
        if (definition == null || conditions == null)
            return false;
        return definition.Weekday == conditions.Calendar.Weekday;
    }

    public bool TryPayEntry(TournamentDefinition definition)
    {
        if (definition == null)
            return false;
        if (HasUpcomingEntry(definition))
            return true;
        if (!AffordableFee(definition))
            return false;
        AdjustMoney(-definition.EntryFee);
        return true;
    }

    public void RefundEntry(TournamentDefinition definition)
    {
        if (definition == null || definition.EntryFee <= 0)
            return;
        AdjustMoney(definition.EntryFee);
    }

    /// <summary>Starts this event immediately with a friend. Same bag and purse as the board, no camp check-in.</summary>
    public bool StartFriendEvent(TournamentDefinition definition)
    {
        if (Phase != TournamentPhase.Idle)
            return false;

        TournamentDefinition def = definition != null ? definition : FriendDefinition();
        if (def == null || conditions == null)
            return false;

        IsFriendEvent = true;
        warned = false;
        PendingCull = null;
        active = new TournamentOccurrence(def, conditions.DayIndex);
        bag.Reset(def.BagLimit);
        SetPhase(TournamentPhase.Running);
        BagChanged?.Invoke();
        Notice?.Invoke($"{def.DisplayName} is underway. Weigh in from Tournaments when you are done.");
        return true;
    }

    public void CancelFriendEvent()
    {
        if (!IsFriendEvent)
            return;

        PendingCull = null;
        warned = false;
        active = default;
        bag.Reset(5);
        IsFriendEvent = false;
        SetPhase(TournamentPhase.Idle);
        BagChanged?.Invoke();
    }

    public void NoticeFriend(string message)
    {
        Announce(message);
    }

    public void Announce(string message)
    {
        if (!string.IsNullOrEmpty(message))
            Notice?.Invoke(message);
    }

    public TournamentResult BuildFriendResult(IReadOnlyList<FriendBag> bags, long clientId, string playerId)
    {
        TournamentDefinition def = active.Definition ?? FriendDefinition();
        if (def == null)
            return null;

        standings.Clear();
        TournamentField.Build(active.IsValid ? active : new TournamentOccurrence(def, conditions != null ? conditions.DayIndex : 0), Bite(), standings);

        for (int i = 0; i < bags.Count; i++)
        {
            FriendBag entry = bags[i];
            standings.Add(new TournamentStanding
            {
                Name = string.IsNullOrEmpty(entry.Name) ? "Angler" : entry.Name,
                Pounds = entry.Forfeited ? 0f : Mathf.Round(entry.Pounds * 100f) * 0.01f,
                Fish = entry.Forfeited ? 0 : entry.Fish,
                IsPlayer = true,
                PlayerId = entry.PlayerId ?? "",
                LunkerLargemouth = entry.Forfeited ? 0f : entry.LunkerLargemouth,
                LunkerSmallmouth = entry.Forfeited ? 0f : entry.LunkerSmallmouth
            });
        }

        standings.Sort(TournamentField.CompareHeaviest);
        TournamentField.AwardLunkers(standings);

        FriendBag mine = default;
        bool found = false;
        for (int i = 0; i < bags.Count; i++)
        {
            if (!SameAngler(bags[i], clientId, playerId))
                continue;
            mine = bags[i];
            found = true;
            break;
        }

        string mineName = found
            ? (string.IsNullOrEmpty(mine.Name) ? "Angler" : mine.Name)
            : "";
        float minePounds = found && !mine.Forfeited ? Mathf.Round(mine.Pounds * 100f) * 0.01f : 0f;
        bool marked = false;
        for (int i = 0; i < standings.Count; i++)
        {
            TournamentStanding row = standings[i];
            bool local = found && !marked && row.Name == mineName
                && Mathf.Abs(row.Pounds - minePounds) < 0.001f
                && (string.IsNullOrEmpty(mine.PlayerId) || row.PlayerId == mine.PlayerId);
            row.IsPlayer = local;
            if (local)
                marked = true;
            standings[i] = row;
        }

        int place = standings.Count;
        bool wonLm = false;
        bool wonSm = false;
        for (int i = 0; i < standings.Count; i++)
        {
            if (!standings[i].IsPlayer)
                continue;
            place = i + 1;
            wonLm = standings[i].WonLunkerLargemouth;
            wonSm = standings[i].WonLunkerSmallmouth;
            break;
        }

        bool forfeited = !found || mine.Forfeited;
        float raw = found ? mine.Pounds : 0f;
        float scored = forfeited ? 0f : raw;
        int placePayout = forfeited || scored <= 0.01f ? 0 : def.PayoutFor(place);
        int lunkerPay = 0;
        int lunkerRep = 0;
        if (!forfeited)
        {
            if (wonLm)
            {
                lunkerPay += def.LunkerPayout(true);
                lunkerRep += def.LunkerReputation(true);
            }

            if (wonSm)
            {
                lunkerPay += def.LunkerPayout(false);
                lunkerRep += def.LunkerReputation(false);
            }
        }

        int payout = placePayout + lunkerPay;
        int reputation = def.ReputationFor(place, forfeited) + lunkerRep;
        TournamentStanding winner = standings.Count > 0 ? standings[0] : default;

        return new TournamentResult
        {
            Id = def.Id,
            DisplayName = def.DisplayName,
            FormatLabel = def.FormatLabel,
            DayIndex = conditions != null ? conditions.DayIndex : 0,
            DateLabel = conditions != null ? conditions.Calendar.DateLabelFor(conditions.DayIndex) : "",
            Place = place,
            Entrants = standings.Count,
            Fish = forfeited ? 0 : mine.Fish,
            RawPounds = raw,
            Penalty = 0f,
            Pounds = scored,
            EntryFee = def.EntryFee,
            Payout = payout,
            PlacePayout = placePayout,
            Reputation = reputation,
            Forfeited = forfeited,
            WinnerName = winner.Name,
            WinnerPounds = winner.Pounds,
            LunkerLargemouth = forfeited ? 0f : mine.LunkerLargemouth,
            LunkerSmallmouth = forfeited ? 0f : mine.LunkerSmallmouth,
            WonLunkerLargemouth = wonLm,
            WonLunkerSmallmouth = wonSm,
            LunkerPayout = lunkerPay,
            LunkerReputation = lunkerRep,
            Standings = new List<TournamentStanding>(standings)
        };
    }

    public void ApplyFriendResult(TournamentResult result)
    {
        if (result == null)
            return;

        AdjustMoney(result.Payout);
        if (progress != null)
            progress.AddReputation(result.Reputation);

        history.Insert(0, result);
        PendingCull = null;
        warned = false;
        active = default;
        bag.Reset(result.Fish > 0 ? Mathf.Max(1, result.Fish) : 5);
        IsFriendEvent = false;
        SetPhase(TournamentPhase.Idle);
        BagChanged?.Invoke();
        Finished?.Invoke(result);
        SaveService.Instance?.Save();
    }

    public static bool SameAngler(FriendBag bag, long clientId, string playerId)
    {
        if (bag.ClientId == clientId)
            return true;
        return !string.IsNullOrEmpty(playerId) && bag.PlayerId == playerId;
    }

    TournamentDefinition FriendDefinition()
    {
        for (int i = 0; i < definitions.Count; i++)
        {
            if (definitions[i] != null)
                return definitions[i];
        }

        return null;
    }

    void SetPhase(TournamentPhase next)
    {
        if (Phase == next)
            return;

        Phase = next;
        PhaseChanged?.Invoke(Phase);
    }

    /// <summary>
    /// Small seasonal nudge on rival bags. Weather can replace this later.
    /// </summary>
    float Bite()
    {
        if (conditions == null)
            return 1f;

        return conditions.Phase switch
        {
            FishingPhase.Prespawn => 1.04f,
            FishingPhase.Spawn => 1.06f,
            FishingPhase.Postspawn => 0.96f,
            FishingPhase.Summer => 1f,
            FishingPhase.FallFeeding => 1.04f,
            _ => 0.92f
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

        var result = bag.Offer(record);
        if (result == TournamentBag.OfferResult.Kept)
        {
            BagChanged?.Invoke();
            return;
        }

        // Bag is full — hold the catch and let the UI ask the player.
        PendingCull = record;
        CullRequired?.Invoke(record);
    }

    void Resolve()
    {
        if (conditions == null)
            conditions = FindFirstObjectByType<WorldConditions>();
        if (dayCycle == null)
            dayCycle = FindFirstObjectByType<DayCycle>();
        if (site == null)
            site = FindFirstObjectByType<TournamentSite>();
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
            dayCycle.Morning += OnMorning;
            hookedDayCycle = true;
        }
    }
}
