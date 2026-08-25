using System;

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
    public bool Forfeited;

    public string WinnerName;
    public float WinnerPounds;

    public bool Won => Place == 1 && !Forfeited && Pounds > 0.01f;
    public int Net => Payout - EntryFee;

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
