using System;
using UnityEngine;

public enum TournamentFormat
{
    BestFiveBass,
    BiggestBass
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

    [Header("Window")]
    [Range(0f, 24f)] public float StartHour = 7f;
    [Range(0f, 24f)] public float EndHour = 16f;

    [Header("Rules")]
    public TournamentFormat Format = TournamentFormat.BestFiveBass;
    [Min(0)] public int EntryFee;

    [Header("Late weigh-in")]
    [Tooltip("Pounds docked from the bag for every half hour past the end of the window.")]
    [Min(0f)] public float LatePenaltyPerHalfHour = 0.25f;
    [Tooltip("Weigh in this many hours late and the bag no longer pays.")]
    [Min(0.5f)] public float ForfeitAfterHours = 4f;

    [Header("Field")]
    [Tooltip("Number of rival anglers you place against.")]
    [Min(1)] public int FieldSize = 11;
    [Tooltip("Scales how heavy rival bags run. Raise it to make an event harder to win.")]
    [Range(0.4f, 2f)] public float FieldStrength = 1f;

    [Header("Purse")]
    [Tooltip("Payout by finishing place, first place first. Places past the end pay nothing.")]
    public int[] Payouts = { 120, 70, 40 };

    public int BagLimit => Format == TournamentFormat.BiggestBass ? 1 : 5;

    public string FormatLabel => Format == TournamentFormat.BiggestBass
        ? "Biggest bass"
        : "Best 5 bass";

    public string EntryLabel => EntryFee > 0 ? $"${EntryFee} entry" : "Free entry";

    public string WindowLabel =>
        $"{GameCalendar.FormatHour(StartHour)} – {GameCalendar.FormatHour(EndHour)}";

    public int PayoutFor(int place)
    {
        if (Payouts == null || place < 1 || place > Payouts.Length)
            return 0;
        return Mathf.Max(0, Payouts[place - 1]);
    }

    void OnValidate()
    {
        if (EndHour <= StartHour)
            EndHour = Mathf.Min(24f, StartHour + 1f);
    }
}
