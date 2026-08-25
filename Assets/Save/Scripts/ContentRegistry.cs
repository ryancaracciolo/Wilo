using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns the stable ids held in save data back into the assets that ship with
/// the build. Save files must never reference a ScriptableObject directly, so
/// everything persisted by id is resolved here.
/// Lives in Resources so it loads without any scene wiring.
/// </summary>
[CreateAssetMenu(menuName = "Wilo/Content Registry", fileName = "ContentRegistry")]
public class ContentRegistry : ScriptableObject
{
    const string ResourcePath = "ContentRegistry";

    [Tooltip("Every lure a save can restore. A lure missing here cannot be loaded.")]
    [SerializeField] List<LureDefinition> lures = new List<LureDefinition>();
    [SerializeField] List<FishSpecies> species = new List<FishSpecies>();
    [SerializeField] List<TournamentDefinition> tournaments = new List<TournamentDefinition>();

    [Header("New game")]
    [Tooltip("What a brand new player finds in the tackle box.")]
    [SerializeField] List<LureDefinition> starterLures = new List<LureDefinition>();

    static ContentRegistry instance;

    Dictionary<string, LureDefinition> lureById;
    Dictionary<string, FishSpecies> speciesById;
    Dictionary<string, TournamentDefinition> tournamentById;

    public static ContentRegistry Instance
    {
        get
        {
            if (instance == null)
                instance = Resources.Load<ContentRegistry>(ResourcePath);
            return instance;
        }
    }

    public IReadOnlyList<LureDefinition> StarterLures => starterLures;

    public LureDefinition Lure(string id)
    {
        Index(lures, ref lureById, l => l.Id);
        return Find(lureById, id);
    }

    public FishSpecies Species(string id)
    {
        Index(species, ref speciesById, s => s.Id);
        return Find(speciesById, id);
    }

    public TournamentDefinition Tournament(string id)
    {
        Index(tournaments, ref tournamentById, t => t.Id);
        return Find(tournamentById, id);
    }

    static T Find<T>(Dictionary<string, T> map, string id) where T : Object
    {
        if (string.IsNullOrEmpty(id) || map == null)
            return null;
        return map.TryGetValue(id, out T found) ? found : null;
    }

    static void Index<T>(List<T> source, ref Dictionary<string, T> map, System.Func<T, string> idOf)
        where T : Object
    {
        if (map != null)
            return;

        map = new Dictionary<string, T>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            T entry = source[i];
            if (entry == null)
                continue;

            string id = idOf(entry);
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning($"ContentRegistry: {entry.name} has no Id and cannot be saved.", entry);
                continue;
            }

            if (map.ContainsKey(id))
            {
                Debug.LogError($"ContentRegistry: duplicate id '{id}' on {entry.name}. Saves will resolve it to the first entry.", entry);
                continue;
            }

            map[id] = entry;
        }
    }

    void OnDisable()
    {
        // Domain reload and asset reimport both land here; drop the caches so a
        // renamed id in the inspector does not survive as a stale lookup.
        lureById = null;
        speciesById = null;
        tournamentById = null;
    }
}
