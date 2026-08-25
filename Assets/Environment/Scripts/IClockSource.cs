/// <summary>
/// Where the calendar comes from. WorldConditions reads time through this
/// rather than owning it, so a shared session can later drive the clock from
/// one authority without every caller of conditions.Hour having to change.
/// </summary>
public interface IClockSource
{
    GameCalendar Calendar { get; }

    /// <summary>
    /// Freezes time while a HUD panel is open. A session-driven source is
    /// expected to ignore this: one player reading the map must not stop the
    /// afternoon for everybody else.
    /// </summary>
    bool Hold { get; set; }

    /// <summary>Advances the clock. Returns whole days rolled over.</summary>
    int Tick(float deltaSeconds, float realMinutesPerDay);

    /// <summary>Replaces the calendar outright: loading a save, or sleeping to morning.</summary>
    void Set(GameCalendar calendar);
}

/// <summary>Single-player clock. Ticks locally and honours the HUD's pause.</summary>
public class LocalClockSource : IClockSource
{
    GameCalendar calendar;

    public LocalClockSource(GameCalendar start)
    {
        calendar = start;
    }

    public GameCalendar Calendar => calendar;

    public bool Hold { get; set; }

    public int Tick(float deltaSeconds, float realMinutesPerDay)
    {
        return Hold ? 0 : calendar.Tick(deltaSeconds, realMinutesPerDay);
    }

    public void Set(GameCalendar value)
    {
        calendar = value;
    }
}
