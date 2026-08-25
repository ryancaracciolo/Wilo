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

    /// <summary>"Today", "Tomorrow", or the weekday name, for schedule rows.</summary>
    public static string WhenLabel(GameCalendar calendar, TournamentOccurrence occurrence)
    {
        if (!occurrence.IsValid)
            return "";

        int days = occurrence.DayIndex - calendar.DayIndex;
        string day = days switch
        {
            0 => "Today",
            1 => "Tomorrow",
            _ => calendar.WeekdayFor(occurrence.DayIndex).ToString()
        };

        return $"{day} · {GameCalendar.FormatHour(occurrence.Definition.StartHour)}";
    }

    /// <summary>Days until the occurrence, for "in 3 days" style copy.</summary>
    public static int DaysAway(GameCalendar calendar, TournamentOccurrence occurrence)
    {
        return occurrence.IsValid ? Mathf.Max(0, occurrence.DayIndex - calendar.DayIndex) : -1;
    }
}
