using UnityEngine;

/// <summary>
/// Rolled size for one physical fish. Pounds are the sim truth;
/// length follows a simple bass cube-law using the species typical.
/// </summary>
public readonly struct FishSize
{
    public readonly float Pounds;
    public readonly float LengthInches;

    public FishSize(float pounds, float lengthInches)
    {
        Pounds = Mathf.Max(0.25f, pounds);
        LengthInches = Mathf.Max(4f, lengthInches);
    }

    public static FishSize FromPounds(float pounds, FishSpecies species)
    {
        float k = LengthCubePerPound(species);
        float length = Mathf.Pow(Mathf.Max(0.25f, pounds) * k, 1f / 3f);
        return new FishSize(pounds, length);
    }

    public static FishSize Roll(
        HabitatSample sample,
        FishSpecies species,
        float u,
        float v,
        HabitatProfile profile = null,
        HabitatFeatures features = default,
        float w = 0.37f)
    {
        float pounds;
        if (profile != null && species != null)
            pounds = profile.RollPounds(species, features, u, v, w);
        else
        {
            float n = Mathf.Clamp01((u + v) * 0.5f);
            pounds = sample.MeanPounds + (n - 0.5f) * 2f * sample.PoundsSpread;
            float min = Mathf.Max(0.5f, species.TypicalPounds * 0.22f);
            float max = Mathf.Max(species.TypicalPounds * 2.8f, min + 0.5f);
            pounds = Mathf.Clamp(pounds, min, max);
        }

        return FromPounds(pounds, species);
    }

    public static float LengthCubePerPound(FishSpecies species)
    {
        float length = Mathf.Max(1f, species.TypicalLengthInches);
        float pounds = Mathf.Max(0.1f, species.TypicalPounds);
        return (length * length * length) / pounds;
    }
}
