using System.IO;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>Makes the networked angler prefab if it is missing after a fresh clone.</summary>
[InitializeOnLoad]
static class AnglerPresencePrefab
{
    const string Path = "Assets/Multiplayer/Resources/AnglerPresence.prefab";

    static AnglerPresencePrefab()
    {
        EditorApplication.delayCall += Ensure;
    }

    static void Ensure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Path) != null)
            return;

        Directory.CreateDirectory("Assets/Multiplayer/Resources");
        var go = new GameObject("AnglerPresence");
        go.AddComponent<NetworkObject>();
        go.AddComponent<AnglerPresence>();
        PrefabUtility.SaveAsPrefabAsset(go, Path);
        Object.DestroyImmediate(go);
        AssetDatabase.Refresh();
    }
}
