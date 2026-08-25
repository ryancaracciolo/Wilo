/// <summary>
/// Where save documents physically live. Swapping this for a cloud-backed store
/// is the whole point: nothing above it knows about files.
///
/// Deliberately synchronous. There is no bootstrap scene to await in, and
/// SaveService has to hand systems their state during Awake/Start, so a load
/// that returned a Task would race the first frame. A cloud store fits by
/// fetching into a local cache at boot and then answering these calls from it.
/// </summary>
public interface ISaveStore
{
    bool TryLoad(string key, out string json);

    /// <summary>The previous good copy, for when the current one will not parse.</summary>
    bool TryLoadBackup(string key, out string json);

    void Save(string key, string json);

    void Delete(string key);
}
