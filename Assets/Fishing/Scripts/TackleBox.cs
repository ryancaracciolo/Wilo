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
        ApplyFrom(SaveService.Instance);
        if (lures.Count == 0)
            AddStarterLures();
        if (equipped == null && lures.Count > 0)
            equipped = lures[0];
    }

    void ApplyFrom(SaveService save)
    {
        if (save == null || save.IsNewGame)
            return;

        ContentRegistry registry = ContentRegistry.Instance;
        if (registry == null)
            return;

        TackleData data = save.Player.tackle;
        lures.Clear();
        for (int i = 0; i < data.ownedLureIds.Count; i++)
        {
            string id = data.ownedLureIds[i];
            LureDefinition lure = registry.Lure(id);
            if (lure == null)
            {
                Debug.LogWarning($"TackleBox: saved lure '{id}' is not in the ContentRegistry and was dropped.", this);
                continue;
            }

            if (!lures.Contains(lure))
                lures.Add(lure);
        }

        equipped = registry.Lure(data.equippedLureId);
    }

    public void CaptureTo(PlayerSave save)
    {
        if (save == null)
            return;

        TackleData data = save.tackle;
        data.ownedLureIds.Clear();
        for (int i = 0; i < lures.Count; i++)
        {
            if (lures[i] != null && !string.IsNullOrEmpty(lures[i].Id))
                data.ownedLureIds.Add(lures[i].Id);
        }

        data.equippedLureId = equipped != null ? equipped.Id : "";
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
