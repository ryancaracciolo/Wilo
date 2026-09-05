using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Owns the save documents and the porch catalog of lakes. Each playthrough
/// is a slot with its own lake and player files. The catalog is a small index
/// so the intro can list cabins without opening every document.
/// </summary>
[DefaultExecutionOrder(-200)]
public class SaveService : MonoBehaviour
{
    public const string LakeKey = "lake";
    public const string PlayerKey = "player";
    public const string CatalogKey = "slots";

    static SaveService instance;

    LocalFileStore catalogStore;
    ISaveStore store;
    WorldConditions conditions;
    LocalFishPopulation fish;
    PlayerProgress progress;
    TackleBox tackle;
    TournamentDirector director;
    LakeSlotCatalog catalog = new LakeSlotCatalog();
    string currentSlotId = "";

    public static SaveService Instance => instance;

    public LakeSave Lake { get; private set; }
    public PlayerSave Player { get; private set; }
    public IReadOnlyList<LakeSlot> Slots => catalog.slots;
    public string CurrentSlotId => currentSlotId;

    /// <summary>
    /// True until the first write of the open slot. Systems use it to keep
    /// their authored defaults instead of restoring a save that does not exist yet.
    /// </summary>
    public bool IsNewGame { get; private set; }

    /// <summary>Set once the player leaves the porch for a chosen slot.</summary>
    public bool SessionActive { get; private set; }

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
        catalogStore = new LocalFileStore();
        LoadCatalog();
        MigrateLegacy();
        Lake = NewLake();
        Player = NewPlayer();
        IsNewGame = true;
    }

    public void BeginNewSlot()
    {
        ForgetSceneRefs();
        currentSlotId = Guid.NewGuid().ToString("N");
        store = new LocalFileStore("wilo", currentSlotId);
        Lake = NewLake();
        Player = NewPlayer();
        IsNewGame = true;
        SessionActive = false;
    }

    public bool OpenSlot(string slotId)
    {
        if (string.IsNullOrEmpty(slotId))
            return false;

        ForgetSceneRefs();
        currentSlotId = slotId;
        store = new LocalFileStore("wilo", slotId);
        LakeSave lake = Read<LakeSave>(LakeKey);
        PlayerSave player = Read<PlayerSave>(PlayerKey);
        if (lake == null || player == null)
            return false;

        Lake = lake;
        Player = player;
        IsNewGame = false;
        SessionActive = true;
        catalog.lastSlotId = slotId;
        WriteCatalog();
        return true;
    }

    public void Save()
    {
        if (store == null)
            return;

        // The porch writes a name and look before anyone has stood on the dock.
        // Do not scrape the Intro scene (or a bounced lake) for clock / wallet.
        if (SessionActive)
        {
            Capture();
            IsNewGame = false;
        }

        store.Save(LakeKey, JsonUtility.ToJson(Lake, true));
        store.Save(PlayerKey, JsonUtility.ToJson(Player, true));
        RememberCurrentSlot();
        WriteCatalog();
    }

    public void ActivateSession()
    {
        SessionActive = true;
    }

    void OnApplicationQuit()
    {
        if (ShouldPersist())
            Save();
    }

    void OnApplicationPause(bool paused)
    {
        if (paused && ShouldPersist())
            Save();
    }

    bool ShouldPersist()
    {
        return SessionActive && store != null;
    }

    /// <summary>Removes one porch lake and its documents. Returns false if the id is unknown.</summary>
    public bool DeleteSlot(string slotId)
    {
        if (string.IsNullOrEmpty(slotId))
            return false;

        LakeSlot slot = FindSlot(slotId);
        if (slot == null)
            return false;

        WiloAccount.ForgetSlotLater(slotId);

        var slotStore = new LocalFileStore("wilo", slotId);
        slotStore.Delete(LakeKey);
        slotStore.Delete(PlayerKey);
        slotStore.DeleteFolder();

        catalog.slots.Remove(slot);
        if (catalog.lastSlotId == slotId)
            catalog.lastSlotId = NewestSlotId();

        if (currentSlotId == slotId)
        {
            ForgetSceneRefs();
            currentSlotId = "";
            store = null;
            SessionActive = false;
            Lake = NewLake();
            Player = NewPlayer();
            IsNewGame = true;
        }

        WriteCatalog();
        return true;
    }

    string NewestSlotId()
    {
        LakeSlot best = null;
        for (int i = 0; i < catalog.slots.Count; i++)
        {
            LakeSlot slot = catalog.slots[i];
            if (slot != null && (best == null || slot.lastPlayed > best.lastPlayed))
                best = slot;
        }

        return best != null ? best.id : "";
    }

    /// <summary>Throws away the catalog and every slot.</summary>
    public void Wipe()
    {
        for (int i = 0; i < catalog.slots.Count; i++)
        {
            var slotStore = new LocalFileStore("wilo", catalog.slots[i].id);
            slotStore.Delete(LakeKey);
            slotStore.Delete(PlayerKey);
        }

        catalogStore.Delete(CatalogKey);
        catalogStore.Delete(LakeKey);
        catalogStore.Delete(PlayerKey);
        catalog = new LakeSlotCatalog();
        currentSlotId = "";
        store = null;
        SessionActive = false;
        Lake = NewLake();
        Player = NewPlayer();
        IsNewGame = true;
    }

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

    void ForgetSceneRefs()
    {
        conditions = null;
        fish = null;
        progress = null;
        tackle = null;
        director = null;
    }

    void LoadCatalog()
    {
        LakeSlotCatalog loaded = ReadFrom(catalogStore, CatalogKey, out LakeSlotCatalog value) ? value : null;
        catalog = loaded ?? new LakeSlotCatalog();
        if (catalog.slots == null)
            catalog.slots = new List<LakeSlot>();
    }

    void WriteCatalog()
    {
        catalogStore.Save(CatalogKey, JsonUtility.ToJson(catalog, true));
    }

    void RememberCurrentSlot()
    {
        if (string.IsNullOrEmpty(currentSlotId) || Player == null)
            return;

        LakeSlot slot = FindSlot(currentSlotId);
        if (slot == null)
        {
            slot = new LakeSlot { id = currentSlotId };
            catalog.slots.Add(slot);
        }

        slot.displayName = Player.displayName;
        slot.lakeKey = string.IsNullOrEmpty(Player.selectedLake) ? LakeChoice.Willow : Player.selectedLake;
        slot.dayIndex = Lake != null && Lake.clock != null ? Lake.clock.dayIndex : 0;
        slot.lastPlayed = DateTime.UtcNow.Ticks;
        slot.appearance = Player.appearance ?? new AppearanceData();
        catalog.lastSlotId = currentSlotId;
    }

    LakeSlot FindSlot(string id)
    {
        for (int i = 0; i < catalog.slots.Count; i++)
        {
            if (catalog.slots[i] != null && catalog.slots[i].id == id)
                return catalog.slots[i];
        }

        return null;
    }

    void MigrateLegacy()
    {
        if (catalog.slots.Count > 0)
            return;

        string root = catalogStore.Root;
        string oldLake = Path.Combine(root, LakeKey + ".json");
        string oldPlayer = Path.Combine(root, PlayerKey + ".json");
        if (!File.Exists(oldLake) && !File.Exists(oldPlayer))
            return;

        string id = Guid.NewGuid().ToString("N");
        string dest = Path.Combine(root, "s", id);
        Directory.CreateDirectory(dest);
        MoveLegacy(oldLake, Path.Combine(dest, LakeKey + ".json"));
        MoveLegacy(oldPlayer, Path.Combine(dest, PlayerKey + ".json"));
        MoveLegacy(oldLake + ".bak", Path.Combine(dest, LakeKey + ".json.bak"));
        MoveLegacy(oldPlayer + ".bak", Path.Combine(dest, PlayerKey + ".json.bak"));

        var slotStore = new LocalFileStore("wilo", id);
        store = slotStore;
        LakeSave lake = Read<LakeSave>(LakeKey) ?? NewLake();
        PlayerSave player = Read<PlayerSave>(PlayerKey) ?? NewPlayer();
        Lake = lake;
        Player = player;
        currentSlotId = id;
        RememberCurrentSlot();
        WriteCatalog();
        store = null;
        currentSlotId = "";
        Lake = NewLake();
        Player = NewPlayer();
        IsNewGame = true;
    }

    static void MoveLegacy(string from, string to)
    {
        if (!File.Exists(from))
            return;
        if (File.Exists(to))
            File.Delete(to);
        File.Move(from, to);
    }

    T Read<T>(string key) where T : class
    {
        return ReadFrom(store, key, out T value) ? value : null;
    }

    static bool ReadFrom<T>(ISaveStore source, string key, out T value) where T : class
    {
        value = null;
        if (source == null)
            return false;

        if (source.TryLoad(key, out string json) && TryParse(json, out value))
            return true;

        if (source.TryLoadBackup(key, out string backup) && TryParse(backup, out value))
        {
            Debug.LogWarning($"Save: '{key}' would not parse, fell back to the previous copy.");
            return true;
        }

        return false;
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
            worldSeed = NewSeed(),
            clock = ClockData.From(GameCalendar.NewGame())
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

    string Location => store is LocalFileStore local ? local.Root : catalogStore != null ? catalogStore.Root : "(no store)";

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
        Debug.Log($"Save: {catalog.slots.Count} lakes, open '{currentSlotId}', at {Location}");
    }
}
