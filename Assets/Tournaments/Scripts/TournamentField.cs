using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One line on the leaderboard. Saved with the result, so keep the fields flat.</summary>
[Serializable]
public struct TournamentStanding
{
    public string Name;
    public float Pounds;
    public int Fish;
    public bool IsPlayer;
    public float LunkerLargemouth;
    public float LunkerSmallmouth;
    public bool WonLunkerLargemouth;
    public bool WonLunkerSmallmouth;
}

/// <summary>
/// Builds the rival field for an occurrence. Bags are generated from a seed
/// derived from the event and the date, so the same tournament always produces
/// the same field and reloading cannot reroll a better result.
/// Later, decorative boats on the lake can be named from this same roster.
/// </summary>
public static class TournamentField
{
    static readonly string[] Names =
    {
        "Dale Hopper", "Marcy Kwan", "Bo Whitfield", "Junie Park", "Cal Brennan",
        "Rosa Villalobos", "Tuck Ansley", "Nell Okafor", "Pete Sandoval", "Ida Ferris",
        "Gus Lindqvist", "Mabel Cheng", "Roy Dubois", "Winnie Adair", "Sal Moretti",
        "Etta Boone", "Ray Kaminski", "Lou Tran", "Birdie Nakamura", "Hank Ellison"
    };

    /// <summary>
    /// Fills <paramref name="into"/> with rival results, heaviest first.
    /// <paramref name="bite"/> scales the whole field for how good the day is,
    /// so a tough winter Sunday produces lighter bags than a spring one.
    /// </summary>
    public static void Build(TournamentOccurrence occurrence, float bite, List<TournamentStanding> into)
    {
        into.Clear();
        if (!occurrence.IsValid)
            return;

        TournamentDefinition def = occurrence.Definition;
        var rng = new System.Random(Seed(def.Id, occurrence.DayIndex));
        int size = Mathf.Max(1, def.FieldSize);
        float strength = def.FieldStrength * Mathf.Clamp(bite, 0.35f, 1.6f);

        for (int i = 0; i < size; i++)
        {
            float skill = Skill(rng);
            into.Add(def.Format == TournamentFormat.BiggestBass
                ? BiggestBassEntry(rng, def, strength, skill, PickName(rng, i, size))
                : BestBagEntry(rng, def, strength, skill, PickName(rng, i, size)));
        }

        into.Sort(CompareHeaviest);
    }

    /// <summary>
    /// Rival names for this occurrence, same roster walk the standings use.
    /// Decorative boats can label themselves from this without rolling bags.
    /// </summary>
    public static void CopyNames(TournamentOccurrence occurrence, List<string> into)
    {
        into.Clear();
        if (!occurrence.IsValid)
            return;

        TournamentDefinition def = occurrence.Definition;
        var rng = new System.Random(Seed(def.Id, occurrence.DayIndex));
        int size = Mathf.Max(1, def.FieldSize);
        int offset = rng.Next(Names.Length);
        for (int i = 0; i < size; i++)
            into.Add(Names[(offset + i * 3) % Names.Length]);
    }

    /// <summary>Skewed toward the middle of the pack, with a thin tail of standout days.</summary>
    static float Skill(System.Random rng)
    {
        float a = (float)rng.NextDouble();
        float b = (float)rng.NextDouble();
        float bell = (a + b) * 0.5f;
        return Mathf.Clamp01(bell * 0.85f + Mathf.Pow(a, 6f) * 0.4f);
    }

    static TournamentStanding BestBagEntry(
        System.Random rng, TournamentDefinition def, float strength, float skill, string name)
    {
        // A weak bag is a couple of small keepers; a hot bag is a full limit of good fish.
        int fish = Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Lerp(1.4f, def.BagLimit + 0.4f, skill)),
            0, def.BagLimit);

        float perFish = Mathf.Lerp(1.5f, 3.9f, skill) * strength;
        float jitter = 0.88f + (float)rng.NextDouble() * 0.24f;
        float pounds = fish * perFish * jitter;

        if (fish == 0)
            pounds = 0f;

        SplitLunkers(rng, fish, pounds, out float lm, out float sm);

        return new TournamentStanding
        {
            Name = name,
            Pounds = Mathf.Round(pounds * 100f) * 0.01f,
            Fish = fish,
            LunkerLargemouth = lm,
            LunkerSmallmouth = sm
        };
    }

    static TournamentStanding BiggestBassEntry(
        System.Random rng, TournamentDefinition def, float strength, float skill, string name)
    {
        float jitter = 0.9f + (float)rng.NextDouble() * 0.2f;
        float pounds = Mathf.Lerp(1.6f, 6.4f, skill) * strength * jitter;
        bool blanked = skill < 0.08f;
        float scored = blanked ? 0f : Mathf.Round(pounds * 100f) * 0.01f;
        bool largemouth = rng.NextDouble() < 0.58;

        return new TournamentStanding
        {
            Name = name,
            Pounds = scored,
            Fish = blanked ? 0 : 1,
            LunkerLargemouth = !blanked && largemouth ? scored : 0f,
            LunkerSmallmouth = !blanked && !largemouth ? scored : 0f
        };
    }

    /// <summary>
    /// Splits a generated bag into individual fish so each angler can show a
    /// largemouth lunker and a smallmouth lunker that still add up.
    /// </summary>
    static void SplitLunkers(System.Random rng, int fish, float pounds, out float lm, out float sm)
    {
        lm = 0f;
        sm = 0f;
        if (fish <= 0 || pounds <= 0.01f)
            return;

        float[] weights = new float[fish];
        float sum = 0f;
        float average = pounds / fish;
        for (int i = 0; i < fish; i++)
        {
            weights[i] = average * (0.72f + (float)rng.NextDouble() * 0.56f);
            sum += weights[i];
        }

        if (sum > 0.001f)
        {
            float scale = pounds / sum;
            for (int i = 0; i < fish; i++)
                weights[i] *= scale;
        }

        for (int i = 0; i < fish; i++)
        {
            float w = Mathf.Round(weights[i] * 100f) * 0.01f;
            if (rng.NextDouble() < 0.58)
                lm = Mathf.Max(lm, w);
            else
                sm = Mathf.Max(sm, w);
        }
    }

    /// <summary>
    /// Marks the heaviest largemouth and smallmouth. The player wins a true tie.
    /// </summary>
    public static void AwardLunkers(List<TournamentStanding> standings)
    {
        if (standings == null || standings.Count == 0)
            return;

        int lm = -1;
        int sm = -1;
        float bestLm = 0.01f;
        float bestSm = 0.01f;
        for (int i = 0; i < standings.Count; i++)
        {
            TournamentStanding row = standings[i];
            if (BetterLunker(row.LunkerLargemouth, row.IsPlayer, bestLm, lm >= 0 && standings[lm].IsPlayer))
            {
                bestLm = row.LunkerLargemouth;
                lm = i;
            }

            if (BetterLunker(row.LunkerSmallmouth, row.IsPlayer, bestSm, sm >= 0 && standings[sm].IsPlayer))
            {
                bestSm = row.LunkerSmallmouth;
                sm = i;
            }
        }

        if (lm >= 0)
        {
            TournamentStanding row = standings[lm];
            row.WonLunkerLargemouth = true;
            standings[lm] = row;
        }

        if (sm >= 0)
        {
            TournamentStanding row = standings[sm];
            row.WonLunkerSmallmouth = true;
            standings[sm] = row;
        }
    }

    static bool BetterLunker(float pounds, bool isPlayer, float best, bool bestIsPlayer)
    {
        if (pounds <= 0.01f)
            return false;
        if (pounds > best)
            return true;
        return Mathf.Abs(pounds - best) < 0.001f && isPlayer && !bestIsPlayer;
    }

    static string PickName(System.Random rng, int index, int size)
    {
        // Walk the roster from a per-event offset so each field looks different
        // without ever repeating a name inside one event.
        if (size >= Names.Length)
            return Names[index % Names.Length];

        int offset = rng.Next(Names.Length);
        return Names[(offset + index * 3) % Names.Length];
    }

    public static int CompareHeaviest(TournamentStanding a, TournamentStanding b)
    {
        int byWeight = b.Pounds.CompareTo(a.Pounds);
        if (byWeight != 0)
            return byWeight;
        int byFish = b.Fish.CompareTo(a.Fish);
        return byFish != 0 ? byFish : string.CompareOrdinal(a.Name, b.Name);
    }

    static int Seed(string id, int dayIndex)
    {
        int hash = 17;
        if (!string.IsNullOrEmpty(id))
        {
            for (int i = 0; i < id.Length; i++)
                hash = hash * 31 + id[i];
        }

        return hash * 31 + dayIndex;
    }
}
