using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The save format. Plain data only: no MonoBehaviours, no ScriptableObject
/// references, no computed state. Content is referenced by the stable ids in
/// ContentRegistry so assets can move or be renamed without breaking a save.
///
/// Anything derivable from (worldSeed, dayIndex) is deliberately absent —
/// tournament fields and weather are recomputed, not stored.
///
/// Two documents, because the lake is not a property of the player: a player
/// carries their wallet, tackle, and log onto whichever lake they are fishing.
/// </summary>
[Serializable]
public class LakeSave
{
    public const int CurrentVersion = 1;

    public int saveVersion = CurrentVersion;

    /// <summary>Identity of this lake, so a player's log can say where it happened.</summary>
    public string lakeId = "";

    /// <summary>Seeds every fish draw. The same seed must always rebuild the same lake.</summary>
    public int worldSeed;

    public ClockData clock = new ClockData();

    /// <summary>
    /// Fish taken out of each cell today, so quitting mid-day and coming back
    /// does not refill the water you already worked. Cleared on the day rollover.
    /// </summary>
    public List<HarvestedCell> harvested = new List<HarvestedCell>();
}

[Serializable]
public class PlayerSave
{
    public const int CurrentVersion = 1;

    public int saveVersion = CurrentVersion;

    /// <summary>Minted locally on first launch. Works offline and is what a friend would invite.</summary>
    public string playerId = "";

    /// <summary>Blank means the player has not signed a tournament sheet yet.</summary>
    public string displayName = "";

    /// <summary>Set when the first-run intro is finished. Older saves stay playable without it.</summary>
    public bool introComplete;

    /// <summary>Which lake the player chose at the cabin door. See LakeChoice.</summary>
    public string selectedLake = "";

    public AppearanceData appearance = new AppearanceData();

    public int money = 250;
    public int reputation;
    public string bestSpecies = "";
    public float bestBassPounds;

    public List<CatchRecord> catches = new List<CatchRecord>();
    public TackleData tackle = new TackleData();
    public TournamentData tournaments = new TournamentData();
}

/// <summary>Mirrors GameCalendar's three real fields. Everything else it exposes is derived.</summary>
[Serializable]
public class ClockData
{
    public int dayIndex;
    public double minutesInDay;

    /// <summary>DayOfWeek stored as int so renaming or reordering the enum cannot silently shift dates.</summary>
    public int epochWeekday;

    public bool IsUnset => dayIndex == 0 && minutesInDay <= 0.01;

    public static ClockData From(GameCalendar calendar)
    {
        return new ClockData
        {
            dayIndex = calendar.DayIndex,
            minutesInDay = calendar.MinutesInDay,
            epochWeekday = (int)calendar.EpochWeekday
        };
    }
}

[Serializable]
public class HarvestedCell
{
    public int x;
    public int z;
    public int count;
}

[Serializable]
public class TackleData
{
    public List<string> ownedLureIds = new List<string>();
    public string equippedLureId = "";
}

[Serializable]
public class TournamentRegistrationData
{
    public string definitionId = "";
    public int dayIndex;
}

[Serializable]
public class TournamentData
{
    public List<TournamentRegistrationData> registrations = new List<TournamentRegistrationData>();
    public List<TournamentResult> history = new List<TournamentResult>();

    /// <summary>
    /// A tournament in progress. Quitting mid-event and coming back should not
    /// hand the player a fresh bag, so the live state rides along in the save.
    /// Only meaningful while phase is not Idle.
    /// </summary>
    public int phase;

    public string activeDefinitionId = "";
    public int activeDayIndex;
    public int bagLimit = 5;
    public List<CatchRecord> bag = new List<CatchRecord>();
}

/// <summary>Colors the intro writes and PlayerAppearance applies. Alpha 0 means "unset".</summary>
[Serializable]
public class AppearanceData
{
    public Color skin;
    public Color hat;
    public Color vest;
    public Color pockets;

    public bool HasColors => skin.a > 0.01f || hat.a > 0.01f || vest.a > 0.01f || pockets.a > 0.01f;
}

/// <summary>One playthrough on the porch list. The documents live under this id.</summary>
[Serializable]
public class LakeSlot
{
    public string id = "";
    public string displayName = "";
    public string lakeKey = LakeChoice.Willow;
    public int dayIndex;
    public long lastPlayed;
    public AppearanceData appearance = new AppearanceData();
}

[Serializable]
public class LakeSlotCatalog
{
    public List<LakeSlot> slots = new List<LakeSlot>();
    public string lastSlotId = "";
}
