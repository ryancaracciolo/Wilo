using System;
using UnityEngine;

/// <summary>
/// Occupancy model: depth is a size-dependent envelope, wood/rock/weeds
/// concentrate fish, and a break boosts cover rather than filling empty
/// slopes. Bigger bass prefer wood and rock; they can still sit in weeds.
/// </summary>
[CreateAssetMenu(menuName = "Wilo/Habitat Profile", fileName = "Habitat")]
public class HabitatProfile : ScriptableObject
{
    static readonly float[] SizeKnots = { 0.10f, 0.22f, 0.32f, 0.42f, 0.58f, 0.76f, 0.93f };

    [Header("Density")]
    [Tooltip("Scales occupancy into fish per 1,000 m². Background water stays thin because occupancy is near zero there.")]
    public float baseFishPerThousandSqMeters = 2.4f;
    public float maxFishPerThousandSqMeters = 10f;
    [Range(0f, 1f)] public float activity = 0.5f;
    [Tooltip("Occupancy in barren water at the smallest size. Larger fish do not use this.")]
    [Range(0f, 0.35f)] public float openWaterScatter = 0.07f;

    [Header("How much each feature concentrates fish")]
    [Range(0f, 2.5f)] public float depthWeight = 1f;
    [Tooltip("Boost on real cover at a break. Empty slopes stay thin.")]
    [Range(0f, 2.5f)] public float dropoffWeight = 0.55f;
    [Range(0f, 2.5f)] public float rockWeight = 1.7f;
    [Range(0f, 4f)] public float woodWeight = 2.4f;
    [Range(0f, 2.5f)] public float vegetationWeight = 1.15f;
    [Tooltip("Lower keeps wood/rock from looking identical to a weed flat. 0.38 leaves headroom for size taste.")]
    [Range(0.15f, 0.8f)] public float structureSoftness = 0.38f;
    [Tooltip("Weak extra occupancy on a clean break. Bigger fish prefer it; it is not a school.")]
    [Range(0f, 0.3f)] public float dropoffSolo = 0.08f;

    [Header("Size taste — 0 is small, 1 is trophy")]
    [Range(0.2f, 2f)] public float smallVegMul = 1.35f;
    [Range(0.2f, 2f)] public float largeVegMul = 0.38f;
    [Range(0.2f, 2f)] public float smallWoodMul = 0.75f;
    [Range(0.2f, 2f)] public float largeWoodMul = 1.35f;
    [Range(0.2f, 2f)] public float smallRockMul = 0.7f;
    [Range(0.2f, 2f)] public float largeRockMul = 1.55f;
    [Range(0.2f, 2f)] public float smallDropoffMul = 0.35f;
    [Range(0.2f, 2f)] public float largeDropoffMul = 1.2f;
    [Tooltip("Higher = fatter pile of average fish, thinner trophy tail. ~3.6 is ~3 lb LM / ~2 lb SM.")]
    [Range(1.5f, 5f)] public float sizePriorPower = 3.6f;

    [Header("Rock depth band (gameplay feet)")]
    [Tooltip("Rock is strongest here. 20 with width 6 is roughly 15–25 ft.")]
    public float rockPeakDepthFeet = 20f;
    public float rockPeakWidthFeet = 7f;
    [Tooltip("Trophy-class fish shift their rock peak toward this depth.")]
    public float rockTrophyDepthFeet = 42f;
    [Range(0f, 0.5f)] public float rockOffBand = 0.18f;

    [Header("Drop-off")]
    public float dropoffSampleMeters = 8f;
    public float dropoffStrongFeet = 7f;

    [Header("Cover reach")]
    [Tooltip("Extra reach around rocks. Total sit distance should land about 1–4 m.")]
    public float rockReachMeters = 2.2f;
    [Tooltip("How tightly bass sit on a stump or log (about 1–4 m).")]
    public float woodHugMeters = 2.2f;
    [Tooltip("Radius used to count lily / weed plants. Isolated pads still count; beds stack.")]
    public float coverRadiusMeters = 9f;
    [Tooltip("How fast extra pads raise vegetation quality. One pad is a small bump.")]
    [Range(0.05f, 0.8f)] public float vegetationGather = 0.24f;

    public SpeciesHabitat[] species = Array.Empty<SpeciesHabitat>();

    /// <summary>Species occupancy mixed across size. Used for density and species pick.</summary>
    public float Score(FishSpecies kind, in HabitatFeatures features)
    {
        SpeciesHabitat taste = Find(kind);
        if (taste == null || kind == null)
            return 0f;

        float sum = 0f;
        for (int i = 0; i < SizeKnots.Length; i++)
        {
            float t = SizeKnots[i];
            sum += SizePrior(t) * OccupancyAt(taste, features, t);
        }

        return sum;
    }

    public SpeciesHabitat Find(FishSpecies kind)
    {
        if (kind == null || species == null)
            return null;

        for (int i = 0; i < species.Length; i++)
        {
            if (species[i] != null && species[i].species == kind)
                return species[i];
        }

        return null;
    }

    public float SaturateVegetation(float rawCount)
    {
        float k = Mathf.Max(0.05f, vegetationGather);
        return 1f - Mathf.Exp(-k * Mathf.Max(0f, rawCount));
    }

    public float RollPounds(FishSpecies kind, in HabitatFeatures features, float u, float v)
    {
        SpeciesHabitat taste = Find(kind);
        float min = taste != null ? taste.minPounds : 0.5f;
        float trophy = taste != null ? taste.trophyPounds : 12f;
        if (taste == null)
            return Mathf.Lerp(min, trophy, Mathf.Clamp01(u) * 0.35f);

        float total = 0f;
        for (int i = 0; i < SizeKnots.Length; i++)
        {
            float t = SizeKnots[i];
            total += SizePrior(t) * OccupancyAt(taste, features, t);
        }

        float sizeT;
        if (total <= 0.0001f)
            sizeT = 0.12f;
        else
        {
            float pick = Mathf.Clamp01(u) * total;
            sizeT = SizeKnots[SizeKnots.Length - 1];
            for (int i = 0; i < SizeKnots.Length; i++)
            {
                float t = SizeKnots[i];
                pick -= SizePrior(t) * OccupancyAt(taste, features, t);
                if (pick > 0f)
                    continue;
                sizeT = t;
                break;
            }
        }

        sizeT = Mathf.Clamp01(sizeT + (Mathf.Clamp01(v) - 0.5f) * 0.08f);
        float pounds = min + (trophy - min) * sizeT;
        pounds *= 1f + (Mathf.Clamp01(v) - 0.5f) * 0.04f;
        return Mathf.Clamp(pounds, min, trophy);
    }

    void OnValidate()
    {
        baseFishPerThousandSqMeters = Mathf.Max(0.05f, baseFishPerThousandSqMeters);
        maxFishPerThousandSqMeters = Mathf.Max(baseFishPerThousandSqMeters, maxFishPerThousandSqMeters);
        rockPeakWidthFeet = Mathf.Max(1f, rockPeakWidthFeet);
        rockTrophyDepthFeet = Mathf.Max(rockPeakDepthFeet, rockTrophyDepthFeet);
        coverRadiusMeters = Mathf.Clamp(coverRadiusMeters, 4f, 16f);
        woodHugMeters = Mathf.Clamp(woodHugMeters, 1f, 4f);
        rockReachMeters = Mathf.Clamp(rockReachMeters, 0.8f, 6f);
        if (species == null)
            return;
        for (int i = 0; i < species.Length; i++)
        {
            SpeciesHabitat taste = species[i];
            if (taste == null)
                continue;
            taste.minPounds = Mathf.Max(0.35f, taste.minPounds);
            taste.trophyPounds = Mathf.Max(taste.minPounds + 0.5f, taste.trophyPounds);
            taste.minDepthFeet = Mathf.Max(0.5f, taste.minDepthFeet);
            taste.maxDepthFeet = Mathf.Max(taste.minDepthFeet + 1f, taste.maxDepthFeet);
            if (taste.smallIdealDepthFeet < 0.05f)
                taste.smallIdealDepthFeet = Mathf.Clamp(taste.idealDepthFeet * 0.5f, taste.minDepthFeet, taste.maxDepthFeet);
            if (taste.largeIdealDepthFeet < 0.05f)
                taste.largeIdealDepthFeet = Mathf.Clamp(taste.idealDepthFeet * 1.15f, taste.minDepthFeet, taste.maxDepthFeet);
            taste.smallIdealDepthFeet = Mathf.Clamp(taste.smallIdealDepthFeet, taste.minDepthFeet, taste.maxDepthFeet);
            taste.largeIdealDepthFeet = Mathf.Clamp(taste.largeIdealDepthFeet, taste.minDepthFeet, taste.maxDepthFeet);
            if (taste.depthSizePower < 0.2f)
                taste.depthSizePower = 1f;
            taste.depthSizePower = Mathf.Clamp(taste.depthSizePower, 0.6f, 3f);
        }
    }

    float OccupancyAt(SpeciesHabitat taste, in HabitatFeatures features, float sizeT)
    {
        float feet = features.DepthFeet;
        if (feet < taste.minDepthFeet || feet > taste.maxDepthFeet)
            return 0f;

        sizeT = Mathf.Clamp01(sizeT);
        float envelope = depthWeight * DepthEnvelope(taste, feet, sizeT);
        if (envelope <= 0.0001f)
            return 0f;

        float wood = woodWeight * taste.wood * features.Wood * Mathf.Lerp(smallWoodMul, largeWoodMul, sizeT);
        float veg = vegetationWeight * taste.vegetation * features.Vegetation * Mathf.Lerp(smallVegMul, largeVegMul, sizeT);
        float rock = rockWeight * taste.rock * features.Rock * RockDepthGate(feet, sizeT) * Mathf.Lerp(smallRockMul, largeRockMul, sizeT);
        float cover = wood + veg + rock;
        float sat = 1f - Mathf.Exp(-cover * Mathf.Max(0.15f, structureSoftness));
        float breakTaste = taste.dropoff * features.Dropoff * Mathf.Lerp(smallDropoffMul, largeDropoffMul, sizeT);
        float breakMul = 1f + dropoffWeight * breakTaste;
        float breakWhisper = dropoffSolo * breakTaste;
        float scatter = openWaterScatter * Mathf.Pow(1f - sizeT, 2.2f);
        return envelope * (sat * breakMul + breakWhisper + scatter);
    }

    static float DepthEnvelope(SpeciesHabitat taste, float feet, float sizeT)
    {
        float deepT = Mathf.Pow(sizeT, Mathf.Max(0.6f, taste.depthSizePower));
        float ideal = Mathf.Lerp(taste.smallIdealDepthFeet, taste.largeIdealDepthFeet, deepT);
        float comfort = Mathf.Max(0.5f, taste.depthComfortFeet) * Mathf.Lerp(1.2f, 0.55f, sizeT);
        float t = Mathf.Abs(feet - ideal) / comfort;
        float fit = 1f / (1f + t * t);
        float fadeIn = Mathf.Clamp01((feet - taste.minDepthFeet) / 1.5f);
        float fadeSpan = Mathf.Lerp(3f, 12f, sizeT);
        float fadeOut = Mathf.Clamp01((taste.maxDepthFeet - feet) / fadeSpan);
        return fit * fadeIn * fadeOut;
    }

    float RockDepthGate(float feet, float sizeT)
    {
        float deepT = sizeT * sizeT;
        float peak = Mathf.Lerp(rockPeakDepthFeet, rockTrophyDepthFeet, deepT);
        float width = Mathf.Lerp(Mathf.Max(1f, rockPeakWidthFeet), 12f, deepT);
        float d = Mathf.Abs(feet - peak) / width;
        float gate = 1f / (1f + d * d);
        return Mathf.Lerp(rockOffBand, 1f, gate);
    }

    float SizePrior(float sizeT)
    {
        sizeT = Mathf.Clamp01(sizeT);
        return Mathf.Pow(1f - sizeT, Mathf.Max(1.2f, sizePriorPower)) + 0.01f;
    }
}

[Serializable]
public class SpeciesHabitat
{
    public FishSpecies species;
    [Tooltip("Typical peak in gameplay feet. Used as a fallback; occupancy uses small/large ideals.")]
    public float idealDepthFeet = 10f;
    [Tooltip("Peak depth for the smallest fish of this species.")]
    public float smallIdealDepthFeet = 5f;
    [Tooltip("Peak depth for trophy-class fish of this species.")]
    public float largeIdealDepthFeet = 12f;
    [Tooltip("1 = size shifts depth linearly. Higher (SM ~1.8) keeps average fish shallow and sends only trophies deep.")]
    public float depthSizePower = 1f;
    [Tooltip("Distance from the size-specific ideal, in gameplay feet, where depth fit is about half.")]
    public float depthComfortFeet = 8f;
    public float minDepthFeet = 2f;
    public float maxDepthFeet = 35f;
    [Range(0f, 2f)] public float dropoff = 1f;
    [Range(0f, 2f)] public float rock = 1f;
    [Range(0f, 2.5f)] public float wood = 1f;
    [Range(0f, 2f)] public float vegetation = 1f;
    public float minPounds = 0.5f;
    public float trophyPounds = 12f;
}
