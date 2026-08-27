using System;
using System.Collections.Generic;

/// <summary>
/// A finished tournament from the player's side. Kept so the profile panel and
/// the save file can show a record without replaying the field.
/// Serialized straight into the save, so the fields below are a save format.
/// The computed properties are not stored.
/// </summary>
[Serializable]
public class TournamentResult
{
    public string Id;
    public string DisplayName;
    public string FormatLabel;
    public int DayIndex;
    public string DateLabel;

    public int Place;
    public int Entrants;
    public int Fish;

    /// <summary>Bag weight before any late penalty.</summary>
    public float RawPounds;
    public float Penalty;

    /// <summary>Weight that was actually scored.</summary>
    public float Pounds;

    public int EntryFee;
    public int Payout;
    public int PlacePayout;
    public int Reputation;
    public bool Forfeited;

    public string WinnerName;
    public float WinnerPounds;

    public float LunkerLargemouth;
    public float LunkerSmallmouth;
    public bool WonLunkerLargemouth;
    public bool WonLunkerSmallmouth;
    public int LunkerPayout;
    public int LunkerReputation;

    /// <summary>Full field at weigh-in, heaviest first. Empty on older saves.</summary>
    public List<TournamentStanding> Standings = new List<TournamentStanding>();

    public bool Won => Place == 1 && !Forfeited && Pounds > 0.01f;
    public bool Placed => !Forfeited && Pounds > 0.01f && Place >= 1 && Place <= 3;
    public bool Paid => Payout > 0;
    public bool WonBothLunkers => WonLunkerLargemouth && WonLunkerSmallmouth;
    public int Net => Payout - EntryFee;
    public bool HasStandings => Standings != null && Standings.Count > 0;

    public string PrizeHeadline
    {
        get
        {
            if (Place == 1 && !Forfeited && Pounds > 0.01f)
                return "You won!";
            if (Place == 2)
                return "2nd place!";
            if (Place == 3)
                return "3rd place!";
            if (WonBothLunkers)
                return "Both lunkers!";
            if (WonLunkerLargemouth)
                return "Largemouth lunker!";
            if (WonLunkerSmallmouth)
                return "Smallmouth lunker!";
            return PlaceLabel;
        }
    }

    public string PlaceLabel
    {
        get
        {
            if (Forfeited)
                return "Forfeited";
            if (Pounds <= 0.01f)
                return "No weight";
            return $"{Ordinal(Place)} of {Entrants}";
        }
    }

    public static string Ordinal(int place)
    {
        if (place <= 0)
            return "—";
        int lastTwo = place % 100;
        if (lastTwo >= 11 && lastTwo <= 13)
            return place + "th";
        return (place % 10) switch
        {
            1 => place + "st",
            2 => place + "nd",
            3 => place + "rd",
            _ => place + "th"
        };
    }
}
