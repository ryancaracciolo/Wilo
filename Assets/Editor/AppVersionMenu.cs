using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Weekly ship helper: bump Player Settings, then write the hosted manifest
/// testers fetch on the porch. Push version.json with the build announcement.
/// </summary>
static class AppVersionMenu
{
    const string ManifestPath = "version.json";

    [MenuItem("Wilo/Version/Bump Patch")]
    static void BumpPatch() => Bump(2);

    [MenuItem("Wilo/Version/Bump Minor")]
    static void BumpMinor() => Bump(1);

    [MenuItem("Wilo/Version/Require This Build")]
    static void RequireCurrent()
    {
        string current = PlayerSettings.bundleVersion;
        AppVersion.Manifest manifest = ReadOrCreate(current);
        manifest.latest = current;
        manifest.minimum = current;
        Write(manifest);
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog(
            "Willow Lake",
            "Older builds will be blocked until they update to " + current + ".",
            "OK");
    }

    [MenuItem("Wilo/Version/Write Manifest")]
    static void WriteManifest()
    {
        string current = PlayerSettings.bundleVersion;
        AppVersion.Manifest manifest = ReadOrCreate(current);
        manifest.latest = current;
        Write(manifest);
        AssetDatabase.Refresh();
        Debug.Log("Wrote " + ManifestPath + " for " + current + ".");
    }

    static void Bump(int part)
    {
        string current = PlayerSettings.bundleVersion;
        AppVersion.TryBump(current, part, out string next);
        if (!EditorUtility.DisplayDialog(
                "Willow Lake",
                "Bump the app version from " + current + " to " + next + "?",
                "Bump",
                "Cancel"))
            return;

        PlayerSettings.bundleVersion = next;
        AppVersion.Manifest manifest = ReadOrCreate(next);
        manifest.latest = next;
        Write(manifest);
        AssetDatabase.Refresh();
        Debug.Log("App version is now " + next + ". Push version.json when you ship.");
    }

    static AppVersion.Manifest ReadOrCreate(string latest)
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ManifestPath));
        if (File.Exists(path))
        {
            AppVersion.Manifest existing = AppVersion.Parse(File.ReadAllText(path));
            if (existing != null)
                return existing;
        }

        return new AppVersion.Manifest
        {
            latest = latest,
            minimum = "0.1.0",
            notes = "",
            url = ""
        };
    }

    static void Write(AppVersion.Manifest manifest)
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ManifestPath));
        string json =
            "{\n" +
            "  \"latest\": \"" + Escape(manifest.latest) + "\",\n" +
            "  \"minimum\": \"" + Escape(manifest.minimum) + "\",\n" +
            "  \"notes\": \"" + Escape(manifest.notes) + "\",\n" +
            "  \"url\": \"" + Escape(manifest.url) + "\"\n" +
            "}\n";
        File.WriteAllText(path, json);
    }

    static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
