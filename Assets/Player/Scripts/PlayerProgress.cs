using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cabin-life stats the HUD can show. Empty fields are placeholders until
/// fishing, tournaments, and a save system fill them in.
/// </summary>
public class PlayerProgress : MonoBehaviour
{
    [SerializeField] string displayName = "You";
    [SerializeField] int money = 250;
    [SerializeField] string bestSpecies = "Largemouth";
    [SerializeField] float bestBassPounds;

    readonly List<CatchRecord> catches = new List<CatchRecord>();

    public string DisplayName => displayName;
    public int Money => money;
    public string BestSpecies => bestSpecies;
    public float BestBassPounds => bestBassPounds;
    public bool HasPersonalBest => bestBassPounds > 0.01f;
    public IReadOnlyList<CatchRecord> Catches => catches;
    public event System.Action<CatchRecord> Caught;
    public event System.Action MarkedChanged;

    public void SetMoney(int value)
    {
        money = Mathf.Max(0, value);
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
