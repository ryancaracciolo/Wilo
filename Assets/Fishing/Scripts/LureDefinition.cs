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
    public LureKind Kind = LureKind.Worm;

    [Header("Falling")]
    [Tooltip("Metres per second the lure sinks while the player is not reeling.")]
    [Min(0f)]
    public float SinkSpeed = 0.35f;

    [Header("Retrieve")]
    public LureRide Ride = LureRide.HoldDepth;

    [Tooltip("Bottom: gameplay feet held above the bed. Fixed band: gameplay feet below the surface. Unused for the other rides.")]
    [Min(0f)]
    public float RideDepthFeet = 0f;

    [Tooltip("Bottom rides only: gameplay feet the lure lifts while you are reeling. Tap the reel to hop it along; let go and it drops back.")]
    [Min(0f)]
    public float HopFeet = 0f;

    [Header("Draw")]
    [Tooltip("Metres a working lure can be noticed from. This is the width of the corridor a cast sweeps.")]
    [Min(1f)]
    public float DrawDistance = 8f;

    [Tooltip("How much of the bait still works when it stops moving. Flash and vibration need motion; a soft bait does not. Drives both what a fish notices and whether it commits.")]
    [Range(0f, 1f)]
    public float WorksAtRest = 0.6f;

    void OnValidate()
    {
        DrawDistance = Mathf.Clamp(DrawDistance, 1f, 30f);
        RideDepthFeet = Mathf.Clamp(RideDepthFeet, 0f, 60f);
        HopFeet = Mathf.Clamp(HopFeet, 0f, 12f);
    }
}
