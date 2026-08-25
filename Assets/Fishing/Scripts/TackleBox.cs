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
            AddStarterLures();
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

    void AddStarterLures()
    {
        ContentRegistry registry = ContentRegistry.Instance;
        if (registry == null)
        {
            Debug.LogError("TackleBox: no ContentRegistry in Resources, so the box stays empty.", this);
            return;
        }

        IReadOnlyList<LureDefinition> starters = registry.StarterLures;
        for (int i = 0; i < starters.Count; i++)
        {
            if (starters[i] != null)
                lures.Add(starters[i]);
        }
    }
}
