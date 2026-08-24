using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's tiny lure collection and what's tied on right now.
/// Swap this later for save data without touching the HUD.
/// </summary>
public class TackleBox : MonoBehaviour
{
    [SerializeField] List<LureDefinition> lures = new List<LureDefinition>();
    [SerializeField] LureDefinition equipped;

    public IReadOnlyList<LureDefinition> Lures => lures;
    public LureDefinition Equipped => equipped;

    public event Action Changed;

    void Awake()
    {
        if (lures.Count == 0)
            lures.AddRange(CreateStarterLures());
        if (equipped == null && lures.Count > 0)
            equipped = lures[0];
    }

    public void Equip(LureDefinition lure)
    {
        if (lure == null || lure == equipped)
            return;
        if (!lures.Contains(lure))
            lures.Add(lure);
        equipped = lure;
        Changed?.Invoke();
    }

    public static List<LureDefinition> CreateStarterLures()
    {
        return new List<LureDefinition>
        {
            Make("Worm", "Soft and simple. Slow along the bottom.", new Color(0.45f, 0.28f, 0.42f), LureKind.Worm, 0.16f),
            Make("Spinnerbait", "Flash and vibration. Counts down through the column.", new Color(0.85f, 0.78f, 0.28f), LureKind.Spinnerbait, 0.55f),
            Make("Jig", "Heavy. Drops fast and stays on the bottom.", new Color(0.72f, 0.55f, 0.18f), LureKind.Jig, 1.85f)
        };
    }

    static LureDefinition Make(string name, string hint, Color color, LureKind kind, float sinkSpeed)
    {
        var lure = ScriptableObject.CreateInstance<LureDefinition>();
        lure.name = name;
        lure.DisplayName = name;
        lure.Hint = hint;
        lure.Color = color;
        lure.Kind = kind;
        lure.SinkSpeed = sinkSpeed;
        return lure;
    }
}
