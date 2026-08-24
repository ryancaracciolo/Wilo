using UnityEngine;

/// <summary>
/// What's true at one point in the lake. Density is presence;
/// activity is willingness to bite. Keep them separate even when both are uniform.
/// </summary>
public readonly struct HabitatSample
{
    public readonly float FishPerThousandSqMeters;
    public readonly float Activity;
    public readonly float MeanPounds;
    public readonly float PoundsSpread;

    public static HabitatSample Empty => default;

    public bool HasFish => FishPerThousandSqMeters > 0.0001f;

    public HabitatSample(
        float fishPerThousandSqMeters,
        float activity,
        float meanPounds,
        float poundsSpread)
    {
        FishPerThousandSqMeters = Mathf.Max(0f, fishPerThousandSqMeters);
        Activity = Mathf.Clamp01(activity);
        MeanPounds = Mathf.Max(0f, meanPounds);
        PoundsSpread = Mathf.Max(0f, poundsSpread);
    }
}
