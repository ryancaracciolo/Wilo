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
    public float baseFishPerThousandSqMeters = 1.92f;
    public float maxFishPerThousandSqMeters = 10f;
    [Range(0f, 1f)] public float activity = 0.5f;
    [Tooltip("Occupancy in barren water at the smallest size. Larger fish do not use this.")]
    [Range(0f, 0.35f)] public float openWaterScatter = 0.07f;

    [Header("How much each feature concentrates fish")]
    [Range(0f, 2.5f)] public float depthWeight = 1f;
    [Tooltip("Boost on real cover at a break. Empty slopes stay thin.")]
    [Range(0f, 2.5f)] public float dropoffWeight = 0.55f;
    [Range(0f, 2.5f)] public float rockWeight = 1.96f;
    [Range(0f, 4f)] public float woodWeight = 2.4f;
    [Range(0f, 2.5f)] public float vegetationWeight = 1.15f;
    [Tooltip("Lower keeps wood/rock from looking identical to a weed flat. 0.38 leaves headroom for size taste.")]
    [Range(0.15f, 0.8f)] public float structureSoftness = 0.38f;
    [Tooltip("Weak extra occupancy on a clean break. Bigger fish prefer it; it is not a school.")]
    [Range(0f, 0.3f)] public float dropoffSolo = 0.08f;

    [Header("Points and shoals")]
    [Tooltip("Boost on cover that sits on a point or shoal instead of a straight bank.")]
    [Range(0f, 2.5f)] public float pointWeight = 0.9f;
    [Tooltip("Occupancy a bare point or shoal holds with no rock, wood or weeds on it.")]
    [Range(0f, 0.4f)] public float pointSolo = 0.16f;
    [Tooltip("Ring radius used to tell a point from a flat. Roughly the size of shoal you want to matter.")]
    public float pointSampleMeters = 18f;
    [Tooltip("How much deeper the surrounding ring must be for a full point reading, in gameplay feet.")]
    public float pointStrongFeet = 10f;

    [Header("Lure depth — bass feed up")]
    [Tooltip("How far a bass will rise for a lure above it, in gameplay feet at reference clarity.")]
    public float lureRiseFeet = 9f;
    [Tooltip("How far a bass will drop for a lure passing under it. Keep this well below the rise.")]
    public float lureSinkFeet = 6f;
    [Tooltip("Gameplay feet a bass shifts without thinking. Keeps a bait crawling the bed in the window of a fish holding just off it.")]
    public float lureSlackFeet = 2f;
    [Tooltip("Water visibility that gives the full rise. Clear water reaches further, stained water less.")]
    public float clarityReferenceMeters = 10f;

    [Header("Size taste — 0 is small, 1 is trophy")]
    [Range(0.2f, 2f)] public float smallVegMul = 1.35f;
    [Range(0.2f, 2f)] public float largeVegMul = 0.38f;
    [Range(0.2f, 2f)] public float smallWoodMul = 0.75f;
    [Range(0.2f, 2f)] public float largeWoodMul = 1.35f;
    [Range(0.2f, 2f)] public float smallRockMul = 0.7f;
    [Range(0.2f, 2f)] public float largeRockMul = 1.65f;
    [Range(0.2f, 2f)] public float smallDropoffMul = 0.35f;
    [Range(0.2f, 2f)] public float largeDropoffMul = 1.2f;
    [Tooltip("Higher = more small fish in the count. Mean weight is retargeted by depth and cover after this draw.")]
    [Range(1.5f, 5f)]     public float sizePriorPower = 3.6f;

    [Header("Size vs depth — occupancy still picks the knot; this retargets the roll")]
    [Tooltip("Gameplay feet where size aim is the species shallow target.")]
    public float sizeShallowFeet = 5f;
    [Tooltip("Gameplay feet where size aim reaches the species deep target.")]
    public float sizeDeepFeet = 36f;
    [Tooltip("0 = ignore this aim (pure occupancy draw). 1 = size follows depth/cover only.")]
    [Range(0.35f, 0.95f)] public float sizeBlend = 0.9f;
    [Tooltip("Pull on draws above SizeAim. 0 lets the occupancy tail reach trophy pounds. Draws below aim still use sizeBlend.")]
    [Range(0f, 0.6f)] public float trophyTailBlend = 0f;
    [Tooltip("On great wood/rock, chance a fish is quality-class (~5–6 lb largemouth) instead of the typical aim.")]
    [Range(0f, 0.25f)] public float qualityChance = 0.13f;
    [Tooltip("Of those quality fish, how many keep stretching toward the trophy cap (12 lb LM / 8 lb SM).")]
    [Range(0f, 0.25f)] public float trophyChance = 0.08f;
    [Tooltip("Scales typical pounds toward min after SizeAim. Trophy tail still reaches the cap.")]
    [Range(0.6f, 1f)] public float typicalSizeScale = 0.85f;

    [Header("Rock depth band (gameplay feet)")]
    [Tooltip("Rock is strongest here. Width 16 keeps 10 ft and 30 ft both in play.")]
    public float rockPeakDepthFeet = 20f;
    public float rockPeakWidthFeet = 16f;
    [Tooltip("Trophy-class fish shift their rock peak toward this depth.")]
    public float rockTrophyDepthFeet = 42f;
    [Range(0f, 0.5f)] public float rockOffBand = 0.3f;

    [Header("Drop-off")]
    public float dropoffSampleMeters = 8f;
    public float dropoffStrongFeet = 7f;

    [Header("Cover reach")]
    [Tooltip("How far past a rock's edge bass may sit. On-top sits use 0 to the rock radius.")]
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

    public float OccupancyForSize(FishSpecies kind, in HabitatFeatures features, float sizeT)
    {
        SpeciesHabitat taste = Find(kind);
        if (taste == null || kind == null)
            return 0f;
        return OccupancyAt(taste, features, sizeT);
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

    /// <summary>
    /// How well a lure sits in a fish's window. Positive feet means the lure is
    /// above the fish, which bass strongly prefer; below is a much tighter window.
    /// </summary>
    public float LureDepthFit(float lureAboveFeet, float visibilityMeters)
    {
        float clarity = Mathf.Clamp(
            visibilityMeters / Mathf.Max(1f, clarityReferenceMeters),
            0.45f,
            1.8f);
        float reach = lureAboveFeet >= 0f
            ? Mathf.Max(0.5f, lureRiseFeet * clarity)
            : Mathf.Max(0.3f, lureSinkFeet * clarity);

        // A bass moves a couple of feet without thinking about it. Without this
        // a jig on the bed is punished for a fish that is holding right over it.
        float gap = Mathf.Abs(lureAboveFeet) - Mathf.Min(lureSlackFeet, reach * 0.5f);
        if (gap <= 0f)
            return 1f;

        float t = gap / reach;
        return 1f / (1f + t * t);
    }

    /// <summary>
    /// Lake-wide willingness to bite. <see cref="activity"/> is a typical day;
    /// phase, temperature, weather, and wind swing around it. Never zero.
    /// Time of day is applied later, per lure, on the take roll — it does not
    /// belong here because occupancy must stay independent of the clock.
    /// </summary>
    public float EvaluateActivity(in LakeConditions c)
    {
        float a = Mathf.Clamp01(activity);
        a *= PhaseMul(c.Phase);
        a *= TempMul(c.WaterTempF);
        a *= WeatherMul(c.Weather);
        a *= WindMul(c.WindMph);
        return Mathf.Clamp(a, 0.12f, 0.95f);
    }

    static float PhaseMul(FishingPhase phase)
    {
        switch (phase)
        {
            case FishingPhase.Prespawn: return 1.06f;
            case FishingPhase.Spawn: return 1.03f;
            case FishingPhase.Postspawn: return 0.94f;
            case FishingPhase.Summer: return 0.96f;
            case FishingPhase.FallFeeding: return 1.08f;
            case FishingPhase.Winter: return 0.88f;
            default: return 1f;
        }
    }

    static float TempMul(float waterTempF)
    {
        if (waterTempF < 45f)
            return 0.4f;
        if (waterTempF < 55f)
            return Mathf.Lerp(0.4f, 0.88f, (waterTempF - 45f) / 10f);
        if (waterTempF <= 76f)
            return 1f;
        if (waterTempF < 86f)
            return Mathf.Lerp(1f, 0.5f, (waterTempF - 76f) / 10f);
        return 0.48f;
    }

    static float WeatherMul(WeatherKind weather)
    {
        if (weather == WeatherKind.Rain)
            return 1.12f;
        if (weather == WeatherKind.Sunny)
            return 0.92f;
        return 1f;
    }

    static float WindMul(float windMph)
    {
        if (windMph < 2f)
            return 0.94f;
        if (windMph <= 12f)
            return 1.05f;
        return Mathf.Lerp(1.05f, 0.8f, Mathf.Clamp01((windMph - 12f) / 10f));
    }

    public float SaturateVegetation(float rawCount)
    {
        float k = Mathf.Max(0.05f, vegetationGather);
        return 1f - Mathf.Exp(-k * Mathf.Max(0f, rawCount));
    }

    public float RollPounds(FishSpecies kind, in HabitatFeatures features, float u, float v, float w = 0.37f)
    {
        SpeciesHabitat taste = Find(kind);
        float min = taste != null ? taste.minPounds : 0.5f;
        float trophy = taste != null ? taste.trophyPounds : 12f;
        if (taste == null)
            return Mathf.Lerp(min, trophy, Mathf.Clamp01(u) * 0.35f);

        Span<float> weights = stackalloc float[SizeKnots.Length];
        float total = 0f;
        for (int i = 0; i < SizeKnots.Length; i++)
        {
            float t = SizeKnots[i];
            weights[i] = SizePrior(t) * OccupancyAt(taste, features, t);
            total += weights[i];
        }

        float sizeT;
        if (total <= 0.0001f)
            sizeT = 0.12f;
        else
        {
            float pick = Mathf.Clamp01(u) * total;
            int index = SizeKnots.Length - 1;
            for (int i = 0; i < SizeKnots.Length; i++)
            {
                if (pick <= weights[i])
                {
                    index = i;
                    break;
                }

                pick -= weights[i];
            }

            // Spread the knot's mass across its band so weights land on a
            // continuous range instead of snapping to seven values.
            float within = weights[index] > 0.0001f ? Mathf.Clamp01(pick / weights[index]) : 0.5f;
            sizeT = Mathf.Lerp(BandLow(index), BandHigh(index), within);
        }

        // Occupancy decides who is around; depth and cover decide how big they
        // typically run. Draws below the aim stay glued so a spot keeps its
        // average. Draws above it keep a tail so 12 lb / 8 lb are possible.
        float aim = SizeAim(taste, features);
        sizeT = BlendTowardAim(Mathf.Clamp01(sizeT), aim);
        // Move the body of the curve down without touching the 12 / 8 cap.
        float typical = min + (trophy - min) * sizeT;
        typical = min + (typical - min) * typicalSizeScale;
        sizeT = trophy > min + 0.01f ? (typical - min) / (trophy - min) : sizeT;
        sizeT = ApplyQualityTail(sizeT, taste, features, w);
        sizeT = Mathf.Clamp01(sizeT);
        float pounds = min + (trophy - min) * sizeT;
        pounds *= 1f + (Mathf.Clamp01(v) - 0.5f) * 0.05f;
        return Mathf.Clamp(pounds, min, trophy);
    }

    /// <summary>
    /// Where size should sit given depth and cover. Open water climbs slowly.
    /// Wood pulls largemouth toward a flat ~3 lb. Rock, especially boulders,
    /// is what actually grows fish at 20–30 ft.
    /// </summary>
    float SizeAim(SpeciesHabitat taste, in HabitatFeatures features)
    {
        float deepT = Mathf.InverseLerp(
            sizeShallowFeet,
            Mathf.Max(sizeShallowFeet + 1f, sizeDeepFeet),
            features.DepthFeet);
        float aim = Mathf.Lerp(taste.shallowSizeT, taste.deepSizeT, deepT);
        // Scene rocks are usually 2–4 m (quality ~0.6–0.8), not 8 m canvas
        // boulders. Map that band onto most of the boost so a 30 ft rock
        // actually holds 4–5 lb smallmouth.
        float rockQ = Mathf.InverseLerp(0.40f, 0.90f, Mathf.Clamp(features.Rock, 0f, 1.5f));
        aim += taste.rockSizeBoost * rockQ * Mathf.Lerp(0.28f, 0.72f, deepT);
        // Stumps / fallen trees: pull toward a flat target so wood does not
        // climb with depth the way rock does.
        if (taste.woodSizeT > 0.05f)
            aim = Mathf.Lerp(aim, taste.woodSizeT, Mathf.Clamp01(features.Wood) * 0.92f);
        else
            aim += taste.woodSizeBoost * features.Wood;
        return Mathf.Clamp01(aim);
    }

    float BlendTowardAim(float drawn, float aim)
    {
        if (drawn <= aim)
            return Mathf.Lerp(drawn, aim, sizeBlend);

        float span = Mathf.Max(0.05f, 1f - aim);
        float over = (drawn - aim) / span;
        float pull = Mathf.Lerp(sizeBlend, trophyTailBlend, Mathf.Clamp01(over));
        return Mathf.Lerp(drawn, aim, pull);
    }

    /// <summary>
    /// Great wood (largemouth) or rock (smallmouth) can roll a quality-class
    /// fish — about 5–6 lb largemouth — and a thin stretch from there to the
    /// trophy cap. Weeds and open water do not get this.
    /// </summary>
    float ApplyQualityTail(float sizeT, SpeciesHabitat taste, in HabitatFeatures features, float w)
    {
        float great = CoverGreatness(taste, features);
        if (great <= 0.02f || qualityChance <= 0f)
            return sizeT;

        float gate = Frac(w * 7.13f + 0.17f);
        if (gate >= qualityChance * great)
            return sizeT;

        float classU = Frac(w * 13.91f + 0.41f);
        float classT = Mathf.Lerp(0.40f, 0.51f, Mathf.Pow(classU, 1.35f));
        sizeT = Mathf.Max(sizeT, classT);

        float stretchGate = Frac(w * 23.77f + 0.63f);
        if (stretchGate >= trophyChance)
            return sizeT;

        // Most stretched fish land mid-tail (8–10 lb LM / 5–7 lb SM).
        // A sliver sit on the authored cap (12 lb LM / 8 lb SM).
        float capU = Frac(w * 53.17f + 0.22f);
        if (capU < 0.18f)
            return 1f;

        float stretchU = Frac(w * 41.33f + 0.09f);
        return Mathf.Lerp(classT, 1f, Mathf.Pow(stretchU, 1.7f));
    }

    static float CoverGreatness(SpeciesHabitat taste, in HabitatFeatures features)
    {
        float woodQ = Mathf.Clamp01(features.Wood);
        float rockQ = Mathf.InverseLerp(0.42f, 0.92f, Mathf.Clamp(features.Rock, 0f, 1.5f));
        float woodLike = Mathf.InverseLerp(0.45f, 1.6f, taste.wood);
        float rockLike = Mathf.InverseLerp(0.55f, 1.9f, taste.rock);
        return Mathf.Clamp01(Mathf.Max(woodQ * woodLike, rockQ * rockLike));
    }

    static float Frac(float x)
    {
        return x - Mathf.Floor(x);
    }

    static float BandLow(int index)
    {
        return index <= 0 ? 0f : (SizeKnots[index - 1] + SizeKnots[index]) * 0.5f;
    }

    static float BandHigh(int index)
    {
        return index >= SizeKnots.Length - 1
            ? 1f
            : (SizeKnots[index] + SizeKnots[index + 1]) * 0.5f;
    }

    void OnValidate()
    {
        baseFishPerThousandSqMeters = Mathf.Max(0.05f, baseFishPerThousandSqMeters);
        maxFishPerThousandSqMeters = Mathf.Max(baseFishPerThousandSqMeters, maxFishPerThousandSqMeters);
        if (sizeShallowFeet < 0.5f)
            sizeShallowFeet = 5f;
        if (sizeDeepFeet < sizeShallowFeet + 1f)
            sizeDeepFeet = 36f;
        if (sizeBlend < 0.3f)
            sizeBlend = 0.9f;
        trophyTailBlend = Mathf.Clamp(trophyTailBlend, 0f, 0.6f);
        qualityChance = Mathf.Clamp(qualityChance, 0f, 0.25f);
        trophyChance = Mathf.Clamp(trophyChance, 0f, 0.25f);
        typicalSizeScale = Mathf.Clamp(typicalSizeScale, 0.6f, 1f);
        sizeShallowFeet = Mathf.Max(1f, sizeShallowFeet);
        sizeDeepFeet = Mathf.Max(sizeShallowFeet + 4f, sizeDeepFeet);
        rockPeakWidthFeet = Mathf.Max(1f, rockPeakWidthFeet);
        rockTrophyDepthFeet = Mathf.Max(rockPeakDepthFeet, rockTrophyDepthFeet);
        pointSampleMeters = Mathf.Clamp(pointSampleMeters, 4f, 60f);
        pointStrongFeet = Mathf.Max(1f, pointStrongFeet);
        lureRiseFeet = Mathf.Max(1f, lureRiseFeet);
        lureSinkFeet = Mathf.Clamp(lureSinkFeet, 0.5f, lureRiseFeet);
        lureSlackFeet = Mathf.Clamp(lureSlackFeet, 0f, lureSinkFeet);
        clarityReferenceMeters = Mathf.Max(1f, clarityReferenceMeters);
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
            if (taste.shallowSizeT < 0.02f && taste.deepSizeT < 0.02f)
            {
                bool smallmouthLike = taste.rock > 1.5f;
                taste.shallowSizeT = smallmouthLike ? 0.12f : 0.08f;
                taste.deepSizeT = smallmouthLike ? 0.3f : 0.2f;
                taste.woodSizeT = smallmouthLike ? 0f : 0.28f;
                taste.woodSizeBoost = 0f;
                taste.rockSizeBoost = smallmouthLike ? 0.4f : 0.36f;
                taste.rockDepthEven = smallmouthLike ? 0.75f : 0.35f;
            }

            taste.shallowSizeT = Mathf.Clamp01(taste.shallowSizeT);
            taste.deepSizeT = Mathf.Clamp(taste.deepSizeT, taste.shallowSizeT, 0.95f);
            taste.woodSizeT = Mathf.Clamp01(taste.woodSizeT);
            taste.woodSizeBoost = Mathf.Clamp(taste.woodSizeBoost, 0f, 0.4f);
            taste.rockSizeBoost = Mathf.Clamp(taste.rockSizeBoost, 0f, 0.55f);
            taste.rockDepthEven = Mathf.Clamp01(taste.rockDepthEven);
            taste.deepOccupancyBoost = Mathf.Clamp(taste.deepOccupancyBoost, 0f, 0.4f);
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

        // Bare terrain: bigger fish read a break or a point harder than small ones.
        float terrainTaste = Mathf.Lerp(smallDropoffMul, largeDropoffMul, sizeT);
        float breakTaste = taste.dropoff * features.Dropoff * terrainTaste;
        float pointTaste = taste.point * Mathf.Max(0f, features.Convexity) * terrainTaste;

        float terrainMul = 1f + dropoffWeight * breakTaste + pointWeight * pointTaste;
        float whisper = dropoffSolo * breakTaste + pointSolo * pointTaste;
        float scatter = openWaterScatter * Mathf.Pow(1f - sizeT, 2.2f);

        // Rock occupancy should not pile twice as hard at 20 ft as at 10 ft.
        // Small fish flatten onto rock; trophies still follow the depth envelope.
        float even = taste.rockDepthEven * Mathf.Clamp(features.Rock, 0f, 1.5f) * (1f - sizeT);
        float env = Mathf.Lerp(envelope, 1f, Mathf.Clamp01(even));
        // Shallow stays put. Deeper water can hold a bit more of this species
        // (largemouth trophies sitting 18–30 ft).
        float deepHold = 1f + taste.deepOccupancyBoost * Mathf.InverseLerp(12f, 26f, feet);
        return env * (sat * terrainMul + whisper + scatter) * deepHold;
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
    [Tooltip("How hard this species relates to points and shoals with no cover on them.")]
    [Range(0f, 2f)] public float point = 1f;
    [Range(0f, 2f)] public float rock = 1f;
    [Range(0f, 2.5f)] public float wood = 1f;
    [Range(0f, 2f)] public float vegetation = 1f;
    public float minPounds = 0.5f;
    public float trophyPounds = 12f;
    [Tooltip("Size knot aim in the shallows (0 = min lb, 1 = trophy).")]
    [Range(0f, 0.6f)] public float shallowSizeT = 0.16f;
    [Tooltip("Size knot aim in deep water before wood/rock bonuses.")]
    [Range(0.15f, 0.8f)] public float deepSizeT = 0.4f;
    [Tooltip("If > 0.05, wood pulls size toward this knot (largemouth stumps stay ~3 lb at any depth).")]
    [Range(0f, 0.6f)] public float woodSizeT = 0f;
    [Tooltip("Tiny additive wood bump when woodSizeT is unused (smallmouth).")]
    [Range(0f, 0.4f)] public float woodSizeBoost = 0f;
    [Tooltip("Extra size on rock that grows with depth. Deep boulders hold the big ones.")]
    [Range(0f, 0.55f)] public float rockSizeBoost = 0f;
    [Tooltip("How much small fish ignore the depth envelope when sitting on rock. Stops 20 ft rock from doubling 10 ft density.")]
    [Range(0f, 1f)] public float rockDepthEven = 0f;
    [Tooltip("Extra occupancy that fades in with depth. 0.1 = +10% by ~26 ft, none in the shallows.")]
    [Range(0f, 0.4f)] public float deepOccupancyBoost = 0f;
}
