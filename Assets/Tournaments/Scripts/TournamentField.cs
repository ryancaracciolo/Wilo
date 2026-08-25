using System.Collections.Generic;
using UnityEngine;

/// <summary>One line on the leaderboard.</summary>
public struct TournamentStanding
{
    public string Name;
    public float Pounds;
    public int Fish;
    public bool IsPlayer;
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

        return new TournamentStanding
        {
            Name = name,
            Pounds = Mathf.Round(pounds * 100f) * 0.01f,
            Fish = fish
        };
    }

    static TournamentStanding BiggestBassEntry(
        System.Random rng, TournamentDefinition def, float strength, float skill, string name)
    {
        float jitter = 0.9f + (float)rng.NextDouble() * 0.2f;
        float pounds = Mathf.Lerp(1.6f, 6.4f, skill) * strength * jitter;
        bool blanked = skill < 0.08f;

        return new TournamentStanding
        {
            Name = name,
            Pounds = blanked ? 0f : Mathf.Round(pounds * 100f) * 0.01f,
            Fish = blanked ? 0 : 1
        };
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
