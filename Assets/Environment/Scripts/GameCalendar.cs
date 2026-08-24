using System;
using UnityEngine;

public enum Season
{
    Spring,
    Summer,
    Fall,
    Winter
}

public enum FishingPhase
{
    Prespawn,
    Spawn,
    Postspawn,
    Summer,
    FallFeeding,
    Winter
}

/// <summary>
/// Compressed lake calendar: 28 days per season, 112 days per year, year
/// starts March 1 so seasons line up with bass phases. Weekdays stay real.
/// </summary>
public struct GameCalendar
{
    public const int DaysPerSeason = 28;
    public const int DaysPerYear = 112;
    public const float MinutesPerDay = 1440f;

    static readonly string[] WeekdayAbbr = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
    static readonly string[] MonthAbbr =
    {
        "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec", "Jan", "Feb"
    };
    static readonly int[] DaysInMonth = { 9, 10, 9, 9, 10, 9, 9, 10, 9, 9, 10, 9 };

    public int DayIndex;
    public float MinutesInDay;
    public DayOfWeek EpochWeekday;

    public float Hour => Mathf.Repeat(MinutesInDay / 60f, 24f);
    public int DayOfYear => Mod(DayIndex, DaysPerYear);
    public int Year => DayIndex / DaysPerYear + 1;
    public Season Season => (Season)(DayOfYear / DaysPerSeason);
    public DayOfWeek Weekday => (DayOfWeek)Mod((int)EpochWeekday + DayIndex, 7);

    public FishingPhase Phase
    {
        get
        {
            int d = DayOfYear;
            if (d >= 102 || d < 7)
                return FishingPhase.Prespawn;
            if (d < 18)
                return FishingPhase.Spawn;
            if (d < 28)
                return FishingPhase.Postspawn;
            if (d < 56)
                return FishingPhase.Summer;
            if (d < 84)
                return FishingPhase.FallFeeding;
            return FishingPhase.Winter;
        }
    }

    public string TimeLabel
    {
        get
        {
            int total = Mathf.FloorToInt(Mathf.Repeat(MinutesInDay, MinutesPerDay));
            int hour24 = total / 60;
            int minute = total % 60;
            int hour12 = hour24 % 12;
            if (hour12 == 0)
                hour12 = 12;
            string ampm = hour24 < 12 ? "AM" : "PM";
            return $"{hour12}:{minute:00} {ampm}";
        }
    }

    public string DateLabel
    {
        get
        {
            ResolveMonth(out string month, out int day);
            return $"{WeekdayAbbr[(int)Weekday]} · {month} {day}";
        }
    }

    public string SeasonLabel => Season.ToString();

    public static GameCalendar FromStart(int year, int dayOfYear, DayOfWeek weekday, float hour)
    {
        year = Mathf.Max(1, year);
        dayOfYear = Mathf.Clamp(dayOfYear, 0, DaysPerYear - 1);
        int dayIndex = (year - 1) * DaysPerYear + dayOfYear;
        int epoch = Mod((int)weekday - dayIndex, 7);
        return new GameCalendar
        {
            DayIndex = dayIndex,
            MinutesInDay = Mathf.Repeat(hour, 24f) * 60f,
            EpochWeekday = (DayOfWeek)epoch
        };
    }

    public int Tick(float deltaSeconds, float realMinutesPerDay)
    {
        float secondsPerDay = Mathf.Max(1f, realMinutesPerDay) * 60f;
        MinutesInDay += deltaSeconds * (MinutesPerDay / secondsPerDay);
        int advanced = 0;
        while (MinutesInDay >= MinutesPerDay)
        {
            MinutesInDay -= MinutesPerDay;
            DayIndex++;
            advanced++;
        }

        return advanced;
    }

    public void SetHour(float hour)
    {
        MinutesInDay = Mathf.Repeat(hour, 24f) * 60f;
    }

    public void AdvanceDays(int days, float wakeHour = -1f)
    {
        DayIndex += days;
        if (wakeHour >= 0f)
            SetHour(wakeHour);
    }

    void ResolveMonth(out string abbr, out int dayOfMonth)
    {
        int remaining = DayOfYear;
        for (int i = 0; i < DaysInMonth.Length; i++)
        {
            int span = DaysInMonth[i];
            if (remaining < span)
            {
                abbr = MonthAbbr[i];
                dayOfMonth = remaining + 1;
                return;
            }

            remaining -= span;
        }

        abbr = MonthAbbr[0];
        dayOfMonth = 1;
    }

    static int Mod(int value, int modulus)
    {
        int r = value % modulus;
        return r < 0 ? r + modulus : r;
    }
}
