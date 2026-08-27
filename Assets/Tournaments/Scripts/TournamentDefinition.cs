using System;
using UnityEngine;

public enum TournamentFormat
{
    BestFiveBass,
    BiggestBass
}

/// <summary>How far up the weekend ladder an event sits. Unlocks are reputation, not this enum.</summary>
public enum TournamentTier
{
    Local,
    Open,
    Regional,
    Invitational
}

/// <summary>
/// One recurring event on the Wilo Lake calendar. Weekly for now; unlock rules
/// and varying purses hang off this without touching the runtime.
/// </summary>
[CreateAssetMenu(menuName = "Wilo/Tournament", fileName = "Tournament")]
public class TournamentDefinition : ScriptableObject
{
    [Tooltip("Stable key. Save data and registration refer to this, so do not rename it casually.")]
    public string Id = "saturday-open";
    public string DisplayName = "Saturday Open";
    public DayOfWeek Weekday = DayOfWeek.Saturday;
    public TournamentTier Tier = TournamentTier.Local;

    [Header("Window")]
    [Range(0f, 24f)] public float StartHour = 7f;
    [Range(0f, 24f)] public float EndHour = 16f;
    [Tooltip("Hours before lines-out to warn the player. 0 skips the warning.")]
    [Min(0f)] public float WarningLeadHours = 0.5f;

    [Header("Rules")]
    public TournamentFormat Format = TournamentFormat.BestFiveBass;
    [Min(0)] public int EntryFee;

    [Header("Reputation")]
    [Tooltip("Angler Reputation needed to enter. 0 is open to everyone.")]
    [Min(0)] public int ReputationRequired;
    [Tooltip("Reputation by finishing place, first place first. Places past the end get only the finish award.")]
    public int[] ReputationAwards = { 18, 12, 8 };
    [Tooltip("Awarded for weighing in, even out of the awards. Forfeits get nothing.")]
    [Min(0)] public int ReputationFinish = 3;

    [Header("Late weigh-in")]
    [Tooltip("Pounds docked from the bag for each late interval.")]
    [Min(0f)] public float LatePenaltyPounds = 0.5f;
    [Tooltip("Minutes between late penalties after lines-out.")]
    [Min(1f)] public float LatePenaltyIntervalMinutes = 10f;
    [Tooltip("Weigh in this many hours late and the bag no longer pays.")]
    [Min(0.5f)] public float ForfeitAfterHours = 1f;

    [Header("Field")]
    [Tooltip("Number of rival anglers you place against.")]
    [Min(1)] public int FieldSize = 11;
    [Tooltip("Scales how heavy rival bags run. Raise it to make an event harder to win.")]
    [Range(0.4f, 2f)] public float FieldStrength = 1f;

    [Header("Purse")]
    [Tooltip("Payout by finishing place, first place first. Places past the end pay nothing.")]
    public int[] Payouts = { 120, 70, 40 };

    [Header("Lunkers")]
    [Tooltip("Side pot for the heaviest largemouth weighed in. Stacks with place money.")]
    [Min(0)] public int LunkerLargemouthPayout = 25;
    [Min(0)] public int LunkerLargemouthReputation = 6;
    [Tooltip("Side pot for the heaviest smallmouth weighed in. Stacks with place money.")]
    [Min(0)] public int LunkerSmallmouthPayout = 25;
    [Min(0)] public int LunkerSmallmouthReputation = 6;

    public int BagLimit => Format == TournamentFormat.BiggestBass ? 1 : 5;

    public string FormatLabel => Format == TournamentFormat.BiggestBass
        ? "Biggest bass"
        : "Best 5 bass";

    public string EntryLabel => EntryFee > 0 ? $"${EntryFee} entry" : "Free entry";

    public string TierLabel => Tier switch
    {
        TournamentTier.Open => "Open",
        TournamentTier.Regional => "Regional",
        TournamentTier.Invitational => "Invitational",
        _ => "Local"
    };

    public string ReputationLockLabel => ReputationRequired > 0
        ? $"Requires {ReputationRequired} Reputation"
        : "";

    public string PlacesPurseLabel =>
        $"1st ${PayoutFor(1)}  ·  2nd ${PayoutFor(2)}  ·  3rd ${PayoutFor(3)}";

    public string LunkerPurseLabel =>
        $"LM lunker ${LunkerLargemouthPayout}  ·  SM lunker ${LunkerSmallmouthPayout}";

    public string WindowLabel =>
        $"{GameCalendar.FormatHour(StartHour)} – {GameCalendar.FormatHour(EndHour)}";

    public float WarningHour => EndHour - Mathf.Max(0f, WarningLeadHours);

    /// <summary>Late pounds to dock for a weigh-in this many hours after lines-out.</summary>
    public float LatePenaltyFor(float hoursLate)
    {
        if (hoursLate <= 0f || LatePenaltyIntervalMinutes <= 0f || LatePenaltyPounds <= 0f)
            return 0f;

        float minutesLate = hoursLate * 60f;
        return Mathf.Floor(minutesLate / LatePenaltyIntervalMinutes) * LatePenaltyPounds;
    }

    public int PayoutFor(int place)
    {
        if (Payouts == null || place < 1 || place > 3 || place > Payouts.Length)
            return 0;
        return Mathf.Max(0, Payouts[place - 1]);
    }

    /// <summary>Reputation earned for this finish. Forfeits and no-shows are zero.</summary>
    public int ReputationFor(int place, bool forfeited)
    {
        if (forfeited)
            return 0;

        int awarded = Mathf.Max(0, ReputationFinish);
        if (ReputationAwards != null && place >= 1 && place <= 3 && place <= ReputationAwards.Length)
            awarded += Mathf.Max(0, ReputationAwards[place - 1]);
        return awarded;
    }

    public int LunkerPayout(bool largemouth) =>
        Mathf.Max(0, largemouth ? LunkerLargemouthPayout : LunkerSmallmouthPayout);

    public int LunkerReputation(bool largemouth) =>
        Mathf.Max(0, largemouth ? LunkerLargemouthReputation : LunkerSmallmouthReputation);

    void OnValidate()
    {
        if (EndHour <= StartHour)
            EndHour = Mathf.Min(24f, StartHour + 1f);
        if (ReputationRequired < 0)
            ReputationRequired = 0;
    }
}
