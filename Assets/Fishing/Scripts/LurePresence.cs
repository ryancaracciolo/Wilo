using System;
using UnityEngine;

/// <summary>
/// The lure currently in the water. Fish read this; PlayerFishing writes it.
/// </summary>
public class LurePresence : MonoBehaviour
{
    FishAgent claimed;

    public bool IsActive { get; private set; }
    public Vector3 Position { get; private set; }

    public event Action<FishAgent> Struck;

    public void Set(Vector3 world)
    {
        IsActive = true;
        Position = world;
    }

    public void Clear()
    {
        IsActive = false;
        claimed = null;
    }

    public bool OfferStrike(FishAgent fish)
    {
        if (!IsActive || fish == null || claimed != null)
            return false;

        claimed = fish;
        Struck?.Invoke(fish);
        return true;
    }
}
