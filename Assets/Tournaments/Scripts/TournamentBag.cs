using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's live bag for one tournament: the heaviest few fish kept so far.
/// Both formats are "keep the heaviest N and add them up", so the bag limit is
/// the only thing that separates biggest-bass from a five-fish limit.
/// </summary>
public class TournamentBag
{
    readonly List<CatchRecord> kept = new List<CatchRecord>();
    int limit = 5;

    public int Limit => limit;
    public int Fish => kept.Count;
    public bool IsFull => kept.Count >= limit;
    public IReadOnlyList<CatchRecord> Kept => kept;

    public float Pounds
    {
        get
        {
            float total = 0f;
            for (int i = 0; i < kept.Count; i++)
                total += kept[i].Pounds;
            return total;
        }
    }

    /// <summary>Heaviest fish in the bag, for a biggest-bass readout.</summary>
    public float BestPounds => kept.Count > 0 ? kept[0].Pounds : 0f;

    public void Reset(int bagLimit)
    {
        limit = Mathf.Max(1, bagLimit);
        kept.Clear();
    }

    /// <summary>
    /// Offers a catch to the bag. Returns true when it counts, either by filling
    /// a slot or by displacing the lightest fish already kept.
    /// </summary>
    public bool Consider(CatchRecord record)
    {
        if (record == null || record.Pounds <= 0f)
            return false;

        if (kept.Count < limit)
        {
            Insert(record);
            return true;
        }

        CatchRecord lightest = kept[kept.Count - 1];
        if (record.Pounds <= lightest.Pounds)
            return false;

        kept.RemoveAt(kept.Count - 1);
        Insert(record);
        return true;
    }

    void Insert(CatchRecord record)
    {
        int at = kept.Count;
        for (int i = 0; i < kept.Count; i++)
        {
            if (record.Pounds > kept[i].Pounds)
            {
                at = i;
                break;
            }
        }

        kept.Insert(at, record);
    }
}
