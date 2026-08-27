/// <summary>
/// Snapshot of environment state for habitat queries.
/// Copied from the world so simulation stays independent of scene objects.
/// </summary>
public readonly struct LakeConditions
{
    public readonly float Hour;
    public readonly Season Season;
    public readonly FishingPhase Phase;
    public readonly float WaterTempF;
    public readonly float AirTempF;
    public readonly float WindFromDegrees;
    public readonly float WindMph;
    public readonly WeatherKind Weather;
    public readonly float DawnHour;
    public readonly float DuskHour;

    public LakeConditions(
        float hour,
        Season season,
        FishingPhase phase,
        float waterTempF,
        float airTempF,
        float windFromDegrees,
        float windMph,
        WeatherKind weather,
        float dawnHour = 6.2f,
        float duskHour = 20.3f)
    {
        Hour = hour;
        Season = season;
        Phase = phase;
        WaterTempF = waterTempF;
        AirTempF = airTempF;
        WindFromDegrees = windFromDegrees;
        WindMph = windMph;
        Weather = weather;
        DawnHour = dawnHour;
        DuskHour = duskHour;
    }
}
