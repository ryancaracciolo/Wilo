using System;
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

    public float BestLargemouth => BestMatching(IsLargemouth);
    public float BestSmallmouth => BestMatching(IsSmallmouth);

    public static bool IsLargemouth(CatchRecord record) =>
        MatchesSpecies(record, "Largemouth");

    public static bool IsSmallmouth(CatchRecord record) =>
        MatchesSpecies(record, "Smallmouth");

    public void Reset(int bagLimit)
    {
        limit = Mathf.Max(1, bagLimit);
        kept.Clear();
    }

    /// <summary>
    /// Offers a catch to the bag. Returns true when it counts, either by filling
    /// a slot or by displacing the lightest fish already kept.
    /// Used only for save restoration; live catches go through
    /// <see cref="Offer"/> so the player can choose which fish to cull.
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

    /// <summary>
    /// Result of offering a catch to the bag.
    /// </summary>
    public enum OfferResult
    {
        /// <summary>Bag had room; the fish was kept automatically.</summary>
        Kept,
        /// <summary>Bag is full; the player must choose to replace or release.</summary>
        BagFull
    }

    /// <summary>
    /// Offers a catch to the bag during live play. If the bag has room the fish
    /// is inserted immediately. If full, the catch is held as pending so the
    /// player can pick which fish to cull (or release the new one).
    /// </summary>
    public OfferResult Offer(CatchRecord record)
    {
        if (record == null || record.Pounds <= 0f)
            return OfferResult.Kept;

        if (kept.Count < limit)
        {
            Insert(record);
            return OfferResult.Kept;
        }

        return OfferResult.BagFull;
    }

    /// <summary>
    /// Replaces the fish at <paramref name="index"/> with
    /// <paramref name="record"/> and returns the culled fish.
    /// </summary>
    public CatchRecord Replace(int index, CatchRecord record)
    {
        if (record == null || index < 0 || index >= kept.Count)
            return null;

        CatchRecord culled = kept[index];
        kept.RemoveAt(index);
        Insert(record);
        return culled;
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

    float BestMatching(Func<CatchRecord, bool> match)
    {
        float best = 0f;
        for (int i = 0; i < kept.Count; i++)
        {
            CatchRecord record = kept[i];
            if (record == null || !match(record) || record.Pounds <= best)
                continue;
            best = record.Pounds;
        }

        return best;
    }

    static bool MatchesSpecies(CatchRecord record, string token)
    {
        if (record == null || string.IsNullOrEmpty(record.SpeciesName))
            return false;
        return record.SpeciesName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
