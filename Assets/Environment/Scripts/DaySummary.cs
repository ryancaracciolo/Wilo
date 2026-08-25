using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the player did today, assembled when the day ends. Plain C# built from
/// the catch log and tournament results rather than a running tally, so it stays
/// correct after a load and cannot drift out of sync with the records it reads.
/// </summary>
public class DaySummary
{
    public int DayIndex;
    public bool Forced;
    public string DateLabel = "";
    public string SeasonLabel = "";
    public string WeatherLabel = "";

    public int Fish;
    public float TotalPounds;
    public float BestPounds;
    public string BestSpecies = "";
    public bool PersonalBest;

    public string TopLure = "";
    public int TopLureFish;

    public readonly List<TournamentResult> Tournaments = new List<TournamentResult>();

    /// <summary>Tournament winnings minus entry fees. Negative on a bad Sunday.</summary>
    public int Earned;

    public bool Blanked => Fish == 0;
    public float AveragePounds => Fish > 0 ? TotalPounds / Fish : 0f;

    public void Collect(IReadOnlyList<CatchRecord> catches, IReadOnlyList<TournamentResult> results)
    {
        Fish = 0;
        TotalPounds = 0f;
        BestPounds = 0f;
        BestSpecies = "";
        PersonalBest = false;
        TopLure = "";
        TopLureFish = 0;
        Tournaments.Clear();
        Earned = 0;

        if (catches != null)
        {
            var byLure = new Dictionary<string, int>();
            for (int i = 0; i < catches.Count; i++)
            {
                CatchRecord c = catches[i];
                if (c == null || c.DayIndex != DayIndex)
                    continue;

                Fish++;
                TotalPounds += c.Pounds;
                if (c.Pounds > BestPounds)
                {
                    BestPounds = c.Pounds;
                    BestSpecies = c.SpeciesName;
                }

                if (c.PersonalBest)
                    PersonalBest = true;

                if (string.IsNullOrEmpty(c.LureName))
                    continue;
                byLure.TryGetValue(c.LureName, out int n);
                byLure[c.LureName] = n + 1;
            }

            foreach (KeyValuePair<string, int> pair in byLure)
            {
                // Ties break on name so the same day always reads the same way.
                if (pair.Value > TopLureFish ||
                    (pair.Value == TopLureFish && string.CompareOrdinal(pair.Key, TopLure) < 0))
                {
                    TopLure = pair.Key;
                    TopLureFish = pair.Value;
                }
            }
        }

        if (results == null)
            return;

        for (int i = 0; i < results.Count; i++)
        {
            TournamentResult r = results[i];
            if (r == null || r.DayIndex != DayIndex)
                continue;
            Tournaments.Add(r);
            Earned += r.Net;
        }
    }

    /// <summary>Headline for the top of the page.</summary>
    public string Headline
    {
        get
        {
            for (int i = 0; i < Tournaments.Count; i++)
            {
                if (Tournaments[i].Won)
                    return "You won the " + Tournaments[i].DisplayName + "!";
            }

            if (PersonalBest)
                return "A new personal best.";
            if (Blanked)
                return "No fish today.";
            if (Fish == 1)
                return "One fish in the boat.";
            return Fish + " fish in the boat.";
        }
    }

    public string CatchLine => Blanked
        ? "Nothing wanted to play."
        : $"{Fish} fish  ·  {TotalPounds:0.00} lb total  ·  {AveragePounds:0.00} lb average";

    public string BestLine => Blanked
        ? ""
        : $"{BestSpecies}  ·  {BestPounds:0.00} lb" + (PersonalBest ? "  ·  personal best" : "");

    public string LureLine => TopLureFish > 0 ? $"{TopLure}  ·  {TopLureFish} fish" : "";

    public string EarnedLine
    {
        get
        {
            if (Earned == 0)
                return "";
            return Earned > 0 ? $"+${Earned}" : $"−${-Earned}";
        }
    }
}
