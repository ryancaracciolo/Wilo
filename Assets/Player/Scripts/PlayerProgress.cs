using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cabin-life stats the HUD can show. Empty fields are placeholders until
/// fishing, tournaments, and a save system fill them in.
/// </summary>
public class PlayerProgress : MonoBehaviour
{
    /// <summary>Blank until the player signs a tournament sheet. Never blank once set.</summary>
    [SerializeField] string displayName = "";
    [SerializeField] int money = 250;
    [SerializeField] string bestSpecies = "Largemouth";
    [SerializeField] float bestBassPounds;

    public const int MaxNameLength = 18;

    readonly List<CatchRecord> catches = new List<CatchRecord>();

    public string DisplayName => HasName ? displayName : "You";
    public bool HasName => !string.IsNullOrWhiteSpace(displayName);
    public int Money => money;
    public string BestSpecies => bestSpecies;
    public float BestBassPounds => bestBassPounds;
    public bool HasPersonalBest => bestBassPounds > 0.01f;
    public IReadOnlyList<CatchRecord> Catches => catches;
    public event System.Action<CatchRecord> Caught;
    public event System.Action MarkedChanged;

    void Awake()
    {
        ApplyFrom(SaveService.Instance);
    }

    void ApplyFrom(SaveService save)
    {
        if (save == null || save.IsNewGame)
            return;

        PlayerSave data = save.Player;
        displayName = data.displayName;
        money = data.money;
        bestSpecies = data.bestSpecies;
        bestBassPounds = data.bestBassPounds;

        catches.Clear();
        for (int i = 0; i < data.catches.Count; i++)
        {
            if (data.catches[i] != null)
                catches.Add(data.catches[i]);
        }
    }

    public void CaptureTo(PlayerSave save)
    {
        if (save == null)
            return;

        save.displayName = displayName;
        save.money = money;
        save.bestSpecies = bestSpecies;
        save.bestBassPounds = bestBassPounds;
        save.catches.Clear();
        save.catches.AddRange(catches);
    }

    public void SetMoney(int value)
    {
        money = Mathf.Max(0, value);
    }

    /// <summary>
    /// Returns false when the name was only whitespace, so a prompt can stay open
    /// rather than quietly accepting a blank entry onto a tournament board.
    /// </summary>
    public bool SetDisplayName(string value)
    {
        string clean = string.IsNullOrEmpty(value) ? "" : value.Trim();
        if (clean.Length > MaxNameLength)
            clean = clean.Substring(0, MaxNameLength).TrimEnd();
        if (clean.Length == 0)
            return false;

        displayName = clean;
        return true;
    }

    public CatchRecord RecordCatch(CatchRecord record)
    {
        if (record == null)
            return null;

        if (record.Pounds > bestBassPounds)
        {
            bestSpecies = record.SpeciesName;
            bestBassPounds = record.Pounds;
            record.PersonalBest = true;
        }

        catches.Add(record);
        Caught?.Invoke(record);
        return record;
    }

    public void MarkCatch(CatchRecord record)
    {
        if (record == null || record.Marked)
            return;
        record.Marked = true;
        MarkedChanged?.Invoke();
    }

    public void CopyMarked(List<CatchRecord> dest)
    {
        dest.Clear();
        for (int i = 0; i < catches.Count; i++)
        {
            if (catches[i] != null && catches[i].Marked)
                dest.Add(catches[i]);
        }
    }
}
