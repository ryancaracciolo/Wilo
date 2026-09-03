using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Copies only meshes from a live player or boat so a remote stand-in
/// cannot steal cameras, input, or save hooks.
/// </summary>
public static class VisualCopy
{
    public static GameObject Clone(GameObject source, string name)
    {
        var root = new GameObject(name);
        if (source == null)
            return root;

        CopyRenderers(source, root);
        foreach (Transform child in source.transform)
            CloneChild(child, root.transform);
        return root;
    }

    static void CloneChild(Transform from, Transform parent)
    {
        if (from.GetComponent<Camera>() != null || from.GetComponent<AudioListener>() != null)
            return;

        var dest = new GameObject(from.name);
        dest.transform.SetParent(parent, false);
        dest.transform.localPosition = from.localPosition;
        dest.transform.localRotation = from.localRotation;
        dest.transform.localScale = from.localScale;
        CopyRenderers(from.gameObject, dest);
        foreach (Transform child in from)
            CloneChild(child, dest.transform);
    }

    static void CopyRenderers(GameObject from, GameObject to)
    {
        var mf = from.GetComponent<MeshFilter>();
        var mr = from.GetComponent<MeshRenderer>();
        if (mf != null && mr != null && mf.sharedMesh != null)
        {
            to.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            CopyMeshRenderer(mr, to.AddComponent<MeshRenderer>());
        }

        var smr = from.GetComponent<SkinnedMeshRenderer>();
        if (smr == null || smr.sharedMesh == null)
            return;

        var copy = to.AddComponent<SkinnedMeshRenderer>();
        copy.sharedMesh = smr.sharedMesh;
        copy.sharedMaterials = smr.sharedMaterials;
        copy.shadowCastingMode = smr.shadowCastingMode;
        copy.receiveShadows = smr.receiveShadows;
        copy.lightProbeUsage = LightProbeUsage.Off;
    }

    static void CopyMeshRenderer(MeshRenderer from, MeshRenderer to)
    {
        to.sharedMaterials = from.sharedMaterials;
        to.shadowCastingMode = from.shadowCastingMode;
        to.receiveShadows = from.receiveShadows;
        to.lightProbeUsage = LightProbeUsage.Off;
    }
}
