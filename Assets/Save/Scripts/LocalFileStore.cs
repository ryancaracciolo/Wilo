using System;
using System.IO;
using UnityEngine;

/// <summary>
/// JSON files under Application.persistentDataPath. Writes go to a temp file
/// first and only then replace the real one, so a crash or a pulled power cord
/// mid-write cannot destroy an existing save.
/// </summary>
public class LocalFileStore : ISaveStore
{
    readonly string root;

    public LocalFileStore(string folderName = "wilo")
    {
        root = Path.Combine(Application.persistentDataPath, folderName);
    }

    /// <summary>Handy in a log line when someone needs to find their save.</summary>
    public string Root => root;

    public bool TryLoad(string key, out string json) => TryRead(PathFor(key), out json);

    public bool TryLoadBackup(string key, out string json) => TryRead(BackupFor(key), out json);

    public void Save(string key, string json)
    {
        string path = PathFor(key);
        string temp = path + ".tmp";

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(temp, json);

            if (File.Exists(path))
                Swap(temp, path, BackupFor(key));
            else
                File.Move(temp, path);
        }
        catch (Exception e)
        {
            Debug.LogError($"Save: could not write '{key}'. {e.Message}");
            TryDelete(temp);
        }
    }

    public void Delete(string key)
    {
        TryDelete(PathFor(key));
        TryDelete(BackupFor(key));
        TryDelete(PathFor(key) + ".tmp");
    }

    string PathFor(string key) => Path.Combine(root, key + ".json");

    string BackupFor(string key) => PathFor(key) + ".bak";

    /// <summary>Promotes temp over live, demoting live to backup.</summary>
    static void Swap(string temp, string live, string backup)
    {
        try
        {
            File.Replace(temp, live, backup);
        }
        catch (Exception)
        {
            // File.Replace is unsupported on some Android and network volumes.
            // The manual path is a hair less atomic but keeps the backup honest.
            TryDelete(backup);
            File.Move(live, backup);
            File.Move(temp, live);
        }
    }

    static bool TryRead(string path, out string json)
    {
        json = null;
        try
        {
            if (!File.Exists(path))
                return false;

            json = File.ReadAllText(path);
            return !string.IsNullOrWhiteSpace(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"Save: could not read '{path}'. {e.Message}");
            return false;
        }
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
            // Nothing useful to do; a stale temp or backup is harmless.
        }
    }
}
