using System.Collections.Generic;
using UnityEngine;

/// <summary>One dated running of a tournament.</summary>
public readonly struct TournamentOccurrence
{
    public readonly TournamentDefinition Definition;
    public readonly int DayIndex;

    public TournamentOccurrence(TournamentDefinition definition, int dayIndex)
    {
        Definition = definition;
        DayIndex = dayIndex;
    }

    public bool IsValid => Definition != null;
    public string Id => Definition != null ? Definition.Id : "";
    public bool SameAs(TournamentOccurrence other) =>
        Definition == other.Definition && DayIndex == other.DayIndex;
}

/// <summary>
/// Turns tournament definitions plus the calendar into dated occurrences.
/// Plain C# so it stays testable and cheap to call.
/// </summary>
public static class TournamentSchedule
{
    /// <summary>The occurrence falling on a given day, if any.</summary>
    public static TournamentOccurrence On(
        IReadOnlyList<TournamentDefinition> definitions,
        GameCalendar calendar,
        int dayIndex)
    {
        if (definitions == null)
            return default;

        System.DayOfWeek weekday = calendar.WeekdayFor(dayIndex);
        for (int i = 0; i < definitions.Count; i++)
        {
            TournamentDefinition d = definitions[i];
            if (d != null && d.Weekday == weekday)
                return new TournamentOccurrence(d, dayIndex);
        }

        return default;
    }

    /// <summary>
    /// Fills <paramref name="into"/> with the next occurrences starting today,
    /// nearest first. Today counts while its window has not closed.
    /// </summary>
    public static void Upcoming(
        IReadOnlyList<TournamentDefinition> definitions,
        GameCalendar calendar,
        int count,
        List<TournamentOccurrence> into)
    {
        into.Clear();
        if (definitions == null || definitions.Count == 0 || count <= 0)
            return;

        // Two weeks is enough headroom for any weekly schedule.
        for (int offset = 0; offset < 14 && into.Count < count; offset++)
        {
            int day = calendar.DayIndex + offset;
            TournamentOccurrence occ = On(definitions, calendar, day);
            if (!occ.IsValid)
                continue;
            if (offset == 0 && calendar.Hour >= occ.Definition.EndHour)
                continue;

            into.Add(occ);
        }
    }

    /// <summary>"Sat · Aug 3 · 7:00 AM – 4:00 PM": the dated line for a schedule row.</summary>
    public static string WhenLabel(GameCalendar calendar, TournamentOccurrence occurrence)
    {
        if (!occurrence.IsValid)
            return "";

        return $"{calendar.DateLabelFor(occurrence.DayIndex)}  ·  {occurrence.Definition.WindowLabel}";
    }

    /// <summary>"Today", "Tomorrow", "In 5 days": how far off the occurrence is.</summary>
    public static string CountdownLabel(GameCalendar calendar, TournamentOccurrence occurrence)
    {
        int days = DaysAway(calendar, occurrence);
        return days switch
        {
            < 0 => "",
            0 => "Today",
            1 => "Tomorrow",
            _ => $"In {days} days"
        };
    }

    /// <summary>
    /// Heading the occurrence sits under. This is what keeps two runnings of the
    /// same weekly event apart on a schedule that lists several of them.
    /// </summary>
    public static string WeekLabel(GameCalendar calendar, TournamentOccurrence occurrence)
    {
        if (!occurrence.IsValid)
            return "";

        int weeks = (WeekStart(calendar, occurrence.DayIndex) - WeekStart(calendar, calendar.DayIndex)) / 7;
        return weeks switch
        {
            <= 0 => "This weekend",
            1 => "Next weekend",
            _ => $"In {weeks} weeks"
        };
    }

    /// <summary>Days until the occurrence, for "in 3 days" style copy.</summary>
    public static int DaysAway(GameCalendar calendar, TournamentOccurrence occurrence)
    {
        return occurrence.IsValid ? Mathf.Max(0, occurrence.DayIndex - calendar.DayIndex) : -1;
    }

    /// <summary>
    /// The Monday that opens the week holding a day. Cutting weeks on Monday puts
    /// a Saturday event and the Sunday after it in one group, which is how the
    /// player reads them, and keeps midweek pointed at the weekend ahead.
    /// </summary>
    static int WeekStart(GameCalendar calendar, int dayIndex)
    {
        return dayIndex - ((int)calendar.WeekdayFor(dayIndex) + 6) % 7;
    }
}
