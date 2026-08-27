using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gameplay systems push short-lived alerts and sticky "press F" prompts here.
/// <see cref="HudCueOverlay"/> draws them. Call <see cref="ShowAction"/> while
/// an interaction is available and <see cref="Clear"/> when it goes away;
/// <see cref="Pulse"/> is for a one-shot bang such as a strike.
/// </summary>
public static class HudCues
{
    static readonly List<HudCue> cues = new List<HudCue>(8);
    static readonly List<HudCue> tickScratch = new List<HudCue>(8);

    public static IReadOnlyList<HudCue> Active => cues;

    /// <summary>
    /// Sticky key prompt, drawn above the player. Safe to call every frame with
    /// the same id; nothing allocates unless the prompt actually changes.
    /// </summary>
    public static void ShowAction(string id, string key, string label, Action onActivate = null)
    {
        Set(id, HudCueKind.Action, key, label, null, Vector3.zero, float.PositiveInfinity, onActivate);
    }

    /// <summary>Brief excitement flash. Re-pulsing the same id restarts the animation.</summary>
    public static void Pulse(string id, string text = "!", Transform worldAnchor = null, float seconds = 1.25f)
    {
        Vector3 offset = worldAnchor != null ? Vector3.up * 2.25f : Vector3.zero;
        Pulse(id, text, worldAnchor, offset, seconds);
    }

    public static void Pulse(string id, string text, Transform worldAnchor, Vector3 worldOffset, float seconds = 1.25f)
    {
        Set(id, HudCueKind.Alert, "", text, worldAnchor, worldOffset, Time.unscaledTime + Mathf.Max(0.15f, seconds), null, restart: true);
    }

    public static void Clear(string id)
    {
        if (string.IsNullOrEmpty(id))
            return;

        for (int i = cues.Count - 1; i >= 0; i--)
        {
            if (cues[i].Id == id)
                cues.RemoveAt(i);
        }
    }

    public static void TryActivate(string id)
    {
        HudCue cue = Find(id);
        cue?.Activate?.Invoke();
    }

    public static void Tick()
    {
        float now = Time.unscaledTime;
        tickScratch.Clear();
        for (int i = 0; i < cues.Count; i++)
        {
            HudCue cue = cues[i];
            if (cue.ExpireAt < now)
                tickScratch.Add(cue);
        }

        for (int i = 0; i < tickScratch.Count; i++)
            cues.Remove(tickScratch[i]);
        tickScratch.Clear();
    }

    public static void Reset()
    {
        cues.Clear();
        tickScratch.Clear();
    }

    static void Set(
        string id,
        HudCueKind kind,
        string key,
        string label,
        Transform worldAnchor,
        Vector3 worldOffset,
        float expireAt,
        Action onActivate,
        bool restart = false)
    {
        if (string.IsNullOrEmpty(id))
            return;

        key ??= "";
        label ??= "";

        HudCue cue = Find(id);
        if (cue == null)
        {
            cue = new HudCue { Id = id };
            cues.Add(cue);
            restart = true;
        }
        else if (!restart
                 && cue.Kind == kind
                 && cue.Key == key
                 && cue.Label == label
                 && cue.Anchor == worldAnchor
                 && cue.WorldOffset == worldOffset
                 && cue.Activate == onActivate
                 && float.IsPositiveInfinity(cue.ExpireAt) == float.IsPositiveInfinity(expireAt))
        {
            return;
        }

        cue.Kind = kind;
        cue.Key = key;
        cue.Label = label;
        cue.Anchor = worldAnchor;
        cue.WorldOffset = worldOffset;
        cue.ExpireAt = expireAt;
        cue.Activate = onActivate;
        if (restart)
            cue.StartedAt = Time.unscaledTime;
    }

    static HudCue Find(string id)
    {
        for (int i = 0; i < cues.Count; i++)
        {
            if (cues[i].Id == id)
                return cues[i];
        }

        return null;
    }
}

public enum HudCueKind
{
    Action,
    Alert
}

public sealed class HudCue
{
    public string Id { get; internal set; }
    public HudCueKind Kind { get; internal set; }
    public string Key { get; internal set; }
    public string Label { get; internal set; }
    public Transform Anchor { get; internal set; }
    public Vector3 WorldOffset { get; internal set; }
    public float ExpireAt { get; internal set; }
    public float StartedAt { get; internal set; }
    public Action Activate { get; internal set; }

    public bool IsSticky => float.IsPositiveInfinity(ExpireAt);
}
