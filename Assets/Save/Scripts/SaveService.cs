using System;
using UnityEngine;

/// <summary>
/// Owns the save documents and hands them to the systems that need them.
///
/// Bootstraps before the first scene loads so every system can read its slice
/// during its own Awake. Systems pull what they need; SaveService collects it
/// back in a fixed, visible order when it is time to write. That is deliberate
/// in place of an ISaveable framework: five systems is few enough that explicit
/// calls stay easier to follow than reflection.
/// </summary>
[DefaultExecutionOrder(-200)]
public class SaveService : MonoBehaviour
{
    public const string LakeKey = "lake";
    public const string PlayerKey = "player";

    static SaveService instance;

    ISaveStore store;
    WorldConditions conditions;
    LocalFishPopulation fish;
    PlayerProgress progress;
    TackleBox tackle;
    TournamentDirector director;

    public static SaveService Instance => instance;

    public LakeSave Lake { get; private set; }
    public PlayerSave Player { get; private set; }

    /// <summary>
    /// True until the first write. Systems use it to keep their authored
    /// defaults instead of restoring a save that does not exist yet.
    /// </summary>
    public bool IsNewGame { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null)
            return;

        var go = new GameObject("SaveService");
        DontDestroyOnLoad(go);
        go.AddComponent<SaveService>();
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        store = new LocalFileStore();
        Load();
    }

    void Load()
    {
        Lake = Read<LakeSave>(LakeKey);
        Player = Read<PlayerSave>(PlayerKey);
        IsNewGame = Lake == null || Player == null;

        Lake ??= NewLake();
        Player ??= NewPlayer();
    }

    public void Save()
    {
        Capture();
        store.Save(LakeKey, JsonUtility.ToJson(Lake, true));
        store.Save(PlayerKey, JsonUtility.ToJson(Player, true));
        IsNewGame = false;
    }

    void OnApplicationQuit()
    {
        Save();
    }

    void OnApplicationPause(bool paused)
    {
        // Mobile can be killed while backgrounded without ever reaching quit.
        if (paused)
            Save();
    }

    /// <summary>Throws away both documents and starts over on the next play.</summary>
    public void Wipe()
    {
        store.Delete(LakeKey);
        store.Delete(PlayerKey);
        Lake = NewLake();
        Player = NewPlayer();
        IsNewGame = true;
    }

    /// <summary>Collects live state back into the documents, in a fixed order.</summary>
    void Capture()
    {
        Resolve();

        if (conditions != null)
            conditions.CaptureTo(Lake);
        if (fish != null)
            fish.CaptureTo(Lake);

        if (progress != null)
            progress.CaptureTo(Player);
        if (tackle != null)
            tackle.CaptureTo(Player);
        if (director != null)
            director.CaptureTo(Player);
    }

    void Resolve()
    {
        if (conditions == null)
            conditions = FindFirstObjectByType<WorldConditions>();
        if (fish == null)
            fish = FindFirstObjectByType<LocalFishPopulation>();
        if (director == null)
            director = FindFirstObjectByType<TournamentDirector>();

        if (progress != null && tackle != null)
            return;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return;

        if (progress == null)
            progress = player.GetComponent<PlayerProgress>();
        if (tackle == null)
            tackle = player.GetComponent<TackleBox>();
    }

    T Read<T>(string key) where T : class
    {
        if (store.TryLoad(key, out string json) && TryParse(json, out T loaded))
            return loaded;

        if (store.TryLoadBackup(key, out string backup) && TryParse(backup, out T recovered))
        {
            Debug.LogWarning($"Save: '{key}' would not parse, fell back to the previous copy.");
            return recovered;
        }

        return null;
    }

    static bool TryParse<T>(string json, out T value) where T : class
    {
        value = null;
        try
        {
            value = JsonUtility.FromJson<T>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Save: could not parse save data. {e.Message}");
            return false;
        }

        return value != null;
    }

    static LakeSave NewLake()
    {
        return new LakeSave
        {
            lakeId = Guid.NewGuid().ToString("N"),
            worldSeed = NewSeed()
        };
    }

    static PlayerSave NewPlayer()
    {
        return new PlayerSave
        {
            playerId = Guid.NewGuid().ToString("N")
        };
    }

    static int NewSeed()
    {
        int seed = Guid.NewGuid().GetHashCode();
        return seed == 0 ? 1 : seed;
    }

    string Location => store is LocalFileStore local ? local.Root : "(not a file store)";

    [ContextMenu("Save/Save now")]
    void DebugSave()
    {
        Save();
        Debug.Log($"Save: wrote lake and player to {Location}");
    }

    [ContextMenu("Save/Wipe save")]
    void DebugWipe()
    {
        Wipe();
        Debug.Log($"Save: cleared {Location}. Restart play mode for a fresh lake.");
    }

    [ContextMenu("Save/Log location")]
    void DebugLocation()
    {
        Debug.Log($"Save: lake '{Lake.lakeId}' seed {Lake.worldSeed}, player '{Player.playerId}', at {Location}");
    }
}
