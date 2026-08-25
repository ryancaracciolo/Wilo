/// <summary>
/// Cheap snapshot of a lake point. Depth is gameplay feet (sonar).
/// Rock / wood are 0-1 proximity. Vegetation is 0-1 bed quality from
/// nearby pad count, not a single plant.
/// </summary>
public readonly struct HabitatFeatures
{
    public readonly float DepthFeet;
    public readonly float Dropoff;
    public readonly float Rock;
    public readonly float Wood;
    public readonly float Vegetation;

    /// <summary>
    /// -1 is a hole or basin, 0 is flat or a straight wall, +1 is a point or
    /// shoal standing up out of deeper water.
    /// </summary>
    public readonly float Convexity;

    public HabitatFeatures(
        float depthFeet,
        float dropoff,
        float rock,
        float wood,
        float vegetation,
        float convexity = 0f)
    {
        DepthFeet = depthFeet;
        Dropoff = dropoff;
        Rock = rock;
        Wood = wood;
        Vegetation = vegetation;
        Convexity = convexity;
    }
}
