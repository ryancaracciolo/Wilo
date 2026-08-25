using System;
using UnityEngine;

/// <summary>
/// The lure currently in the water. Fish read this; PlayerFishing writes it.
/// Also tracks how the lure is moving, since a bait's pull depends on whether
/// it is working or lying dead on the bottom.
/// </summary>
public class LurePresence : MonoBehaviour
{
    const float DefaultDraw = 8f;
    const float StillThreshold = 0.35f;

    /// <summary>Speed at which a bait is doing everything it knows how to do.</summary>
    const float FullyWorkingSpeed = 3f;

    /// <summary>
    /// How long a bait takes to settle into its at-rest behaviour. Short on
    /// purpose: a blade is dead the moment it stops, and fish decide in a few
    /// seconds, so a slow fade would never get a chance to matter.
    /// </summary>
    const float SettleSeconds = 1.5f;

    FishAgent claimed;

    public bool IsActive { get; private set; }
    public Vector3 Position { get; private set; }
    public LureDefinition Lure { get; private set; }

    /// <summary>Metres per second the lure is travelling through the water.</summary>
    public float Speed { get; private set; }

    /// <summary>Seconds the lure has been sitting motionless.</summary>
    public float StillTime { get; private set; }

    public event Action<FishAgent> Struck;

    /// <summary>
    /// How far off a fish can pick this lure out. Flash and vibration only
    /// exist while the lure is moving, which is what stops a loud bait from
    /// being strictly better than a quiet one.
    /// </summary>
    public float NoticeRadius
    {
        get
        {
            if (Lure == null)
                return DefaultDraw;

            float motion = Mathf.Clamp01(Speed / FullyWorkingSpeed);
            return Lure.DrawDistance * Mathf.Lerp(Lure.WorksAtRest, 1f, motion);
        }
    }

    /// <summary>
    /// Scales how readily a fish in the zone commits. Since every lure reels at
    /// the same speed, this is where presentation lives: a soft bait keeps
    /// working when you stop, a blade goes dead and has to be kept moving.
    /// </summary>
    public float Liveliness
    {
        get
        {
            float atRest = Lure != null ? Lure.WorksAtRest : 0.3f;
            return Mathf.Lerp(1f, atRest, Mathf.Clamp01(StillTime / SettleSeconds));
        }
    }

    public void Set(Vector3 world, LureDefinition lure)
    {
        float dt = Time.deltaTime;
        if (IsActive && dt > 0.0001f)
        {
            float rate = Vector3.Distance(world, Position) / dt;
            Speed = Mathf.Lerp(Speed, rate, 1f - Mathf.Exp(-8f * dt));
            StillTime = Speed < StillThreshold ? StillTime + dt : 0f;
        }
        else
        {
            Speed = 0f;
            StillTime = 0f;
        }

        IsActive = true;
        Position = world;
        Lure = lure;
    }

    public void Clear()
    {
        IsActive = false;
        claimed = null;
        Speed = 0f;
        StillTime = 0f;
    }

    public bool OfferStrike(FishAgent fish)
    {
        if (!IsActive || fish == null || claimed != null)
            return false;

        claimed = fish;
        Struck?.Invoke(fish);
        return true;
    }

    /// <summary>Hand the lure back when a strike was offered but nothing hooked up.</summary>
    public void ReleaseClaim(FishAgent fish)
    {
        if (claimed == fish)
            claimed = null;
    }
}
