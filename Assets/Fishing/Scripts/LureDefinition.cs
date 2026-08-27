using UnityEngine;

public enum LureKind
{
    Worm,
    Spinnerbait,
    Jig,
    Crankbait,
    Topwater,
    Dropshot
}

/// <summary>How a lure carries itself once the player starts reeling.</summary>
public enum LureRide
{
    /// <summary>Comes straight back at whatever depth it counted down to.</summary>
    HoldDepth,

    /// <summary>Tracks the bed, staying a set clearance above it.</summary>
    Bottom,

    /// <summary>Dives to its own running depth and holds it regardless of the bed.</summary>
    FixedBand,

    /// <summary>Never leaves the top.</summary>
    Surface
}

/// <summary>
/// A lure is described only by how it physically behaves. Which fish want it
/// falls out of where it ends up and how long it stays there, so there are no
/// species preferences here on purpose.
/// </summary>
[CreateAssetMenu(menuName = "Wilo/Lure", fileName = "Lure")]
public class LureDefinition : ScriptableObject
{
    [Tooltip("Stable key. Save data refers to this, so do not rename it casually.")]
    public string Id = "";

    public string DisplayName = "Lure";
    [TextArea]
    public string Hint = "";
    public Color Color = new Color(0.55f, 0.38f, 0.22f);
    [Tooltip("Tackle-box picture. Baked from the low-poly lure mesh.")]
    public Sprite Icon;
    public LureKind Kind = LureKind.Worm;

    [Header("Falling")]
    [Tooltip("Metres per second the lure sinks while the player is not reeling.")]
    [Min(0f)]
    public float SinkSpeed = 0.35f;

    [Header("Retrieve")]
    public LureRide Ride = LureRide.HoldDepth;

    [Tooltip("Scales the reel speed. Below 1 for a bait that wants working slowly, above 1 to cover water.")]
    [Range(0.4f, 1.4f)]
    public float RetrieveScale = 1f;

    [Tooltip("Bottom: gameplay feet held above the bed. Fixed band: gameplay feet below the surface. Unused for the other rides.")]
    [Min(0f)]
    public float RideDepthFeet = 0f;

    [Tooltip("Bottom rides only: gameplay feet the lure lifts while you are reeling. Tap the reel to hop it along; let go and it drops back.")]
    [Min(0f)]
    public float HopFeet = 0f;

    [Header("Draw")]
    [Tooltip("Metres where a working lure's pull hits zero. Worm/jig ~6, spinner/crank/topwater ~7.5. At rest this shrinks by WorksAtRest.")]
    [Min(1f)]
    public float DrawDistance = 8f;

    [Tooltip("How much of the bait still works when it stops. Shrinks draw reach and the close-range take roll. A blade goes dead; a worm does not.")]
    [Range(0f, 1f)]
    public float WorksAtRest = 0.6f;

    [Tooltip("0 = slight dawn/dusk/night bump on the take (~10% peak to trough). 1 = topwater: weak midday, strong dusk.")]
    [Range(0f, 1f)]
    public float LowLightBias = 0f;

    /// <summary>
    /// Multiplier on the close-range take. Does not move fish or change
    /// occupancy. Standard baits peak in the morning, late afternoon, and
    /// night; a low-light bait is weakest at noon and strongest at dusk.
    /// </summary>
    public float TimeOfDayTakeMul(float hour, float dawn, float dusk)
    {
        dawn = dawn > 0.1f ? dawn : 6.2f;
        dusk = dusk > dawn + 1f ? dusk : dawn + 13f;
        hour = Mathf.Repeat(hour, 24f);

        float dawnBump = Glow(hour, dawn, 1.8f);
        float duskBump = Glow(hour, dusk, 1.8f);
        float night = hour < dawn || hour > dusk ? 1f : 0f;
        float lowLight = Mathf.Max(dawnBump, duskBump, night);
        float standard = Mathf.Lerp(0.96f, 1.05f, lowLight);

        float topDawn = Glow(hour, dawn, 1.6f);
        float topDusk = Glow(hour, dusk, 2.2f);
        const float dayFloor = 0.62f;
        float top = dayFloor;
        top = Mathf.Max(top, Mathf.Lerp(dayFloor, 1.12f, topDawn));
        top = Mathf.Max(top, Mathf.Lerp(dayFloor, 1.22f, topDusk));
        if (night > 0.5f)
            top = Mathf.Max(top, 1.06f);

        return Mathf.Lerp(standard, top, LowLightBias);
    }

    /// <summary>1 at the centre hour, falling off over <paramref name="widthHours"/>.</summary>
    static float Glow(float hour, float centre, float widthHours)
    {
        float delta = Mathf.Abs(Mathf.Repeat(hour - centre + 12f, 24f) - 12f);
        float t = delta / Mathf.Max(0.2f, widthHours);
        return Mathf.Exp(-t * t);
    }

    void OnValidate()
    {
        DrawDistance = Mathf.Clamp(DrawDistance, 1f, 30f);
        RideDepthFeet = Mathf.Clamp(RideDepthFeet, 0f, 60f);
        HopFeet = Mathf.Clamp(HopFeet, 0f, 12f);
    }
}
