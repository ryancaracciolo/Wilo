using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Compares the Player Settings version to a tiny hosted JSON file so weekly
/// tester builds can warn or block. Fetch failures stay open so a downed host
/// does not lock people out of the last build they already have.
/// </summary>
public static class AppVersion
{
    public const string DefaultManifestUrl =
        "https://raw.githubusercontent.com/ryancaracciolo/Wilo/main/version.json";

    public static string Local => Application.version;

    public enum Gate
    {
        Current,
        Optional,
        Required
    }

    [Serializable]
    public class Manifest
    {
        public string latest;
        public string minimum;
        public string notes;
        public string url;
    }

    public static IEnumerator Check(string url, Action<Manifest> done)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            done?.Invoke(null);
            yield break;
        }

        string stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string full = url.IndexOf('?') >= 0 ? url + "&t=" + stamp : url + "?t=" + stamp;

        using (UnityWebRequest req = UnityWebRequest.Get(full))
        {
            req.timeout = 6;
            req.SetRequestHeader("Cache-Control", "no-cache");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                done?.Invoke(null);
                yield break;
            }

            done?.Invoke(Parse(req.downloadHandler.text));
        }
    }

    public static Manifest Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            Manifest manifest = JsonUtility.FromJson<Manifest>(json);
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.latest))
                return null;
            return manifest;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static Gate Evaluate(Manifest remote, string local)
    {
        if (remote == null)
            return Gate.Current;

        string have = string.IsNullOrWhiteSpace(local) ? "0.0.0" : local.Trim();
        if (!string.IsNullOrWhiteSpace(remote.minimum) && Compare(have, remote.minimum) < 0)
            return Gate.Required;
        if (Compare(have, remote.latest) < 0)
            return Gate.Optional;
        return Gate.Current;
    }

    public static int Compare(string a, string b)
    {
        Split(a, out int a0, out int a1, out int a2);
        Split(b, out int b0, out int b1, out int b2);
        if (a0 != b0)
            return a0.CompareTo(b0);
        if (a1 != b1)
            return a1.CompareTo(b1);
        return a2.CompareTo(b2);
    }

    public static bool TryBump(string current, int part, out string next)
    {
        Split(current, out int major, out int minor, out int patch);
        if (part == 0)
        {
            major++;
            minor = 0;
            patch = 0;
        }
        else if (part == 1)
        {
            minor++;
            patch = 0;
        }
        else
        {
            patch++;
        }

        next = major + "." + minor + "." + patch;
        return true;
    }

    static void Split(string value, out int major, out int minor, out int patch)
    {
        major = 0;
        minor = 0;
        patch = 0;
        if (string.IsNullOrWhiteSpace(value))
            return;

        string[] parts = value.Trim().Split('.');
        if (parts.Length > 0)
            int.TryParse(parts[0], out major);
        if (parts.Length > 1)
            int.TryParse(parts[1], out minor);
        if (parts.Length > 2)
            int.TryParse(parts[2], out patch);
    }
}
