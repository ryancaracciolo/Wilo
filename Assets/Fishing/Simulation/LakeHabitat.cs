using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lake-wide habitat sampler. Occupancy is depth envelope × cover lumps,
/// with a thin open-water scatter of small fish.
/// </summary>
public sealed class LakeHabitat
{
    readonly HabitatProfile profile;
    readonly HabitatSample uniform;
    readonly float landDepthMeters;

    public LakeHabitat(HabitatProfile profile, HabitatSample uniform, float landDepthMeters = 0.05f)
    {
        this.profile = profile;
        this.uniform = uniform;
        this.landDepthMeters = Mathf.Max(0f, landDepthMeters);
    }

    public HabitatSample Sample(
        float geometricDepth,
        in HabitatFeatures features,
        IReadOnlyList<FishSpecies> species)
    {
        if (geometricDepth <= landDepthMeters)
            return HabitatSample.Empty;
        if (profile == null)
            return uniform;

        float sum = 0f;
        if (species != null)
        {
            for (int i = 0; i < species.Count; i++)
                sum += profile.Score(species[i], features);
        }

        float density = Mathf.Clamp(
            profile.baseFishPerThousandSqMeters * sum,
            0f,
            profile.maxFishPerThousandSqMeters);
        if (density < 0.0001f)
            return HabitatSample.Empty;

        return new HabitatSample(
            density,
            profile.activity,
            0f,
            0f);
    }

    public FishSpecies Pick(IReadOnlyList<FishSpecies> species, in HabitatFeatures features, float u01)
    {
        if (species == null || species.Count == 0)
            return null;

        if (profile == null)
        {
            int index = Mathf.Clamp(Mathf.FloorToInt(u01 * species.Count), 0, species.Count - 1);
            return species[index];
        }

        float total = 0f;
        for (int i = 0; i < species.Count; i++)
            total += profile.Score(species[i], features);
        if (total <= 0.0001f)
            return null;

        float pick = Mathf.Clamp01(u01) * total;
        for (int i = 0; i < species.Count; i++)
        {
            pick -= profile.Score(species[i], features);
            if (pick <= 0f)
                return species[i];
        }

        return species[species.Count - 1];
    }
}
