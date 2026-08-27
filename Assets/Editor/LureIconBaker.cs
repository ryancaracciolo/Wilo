using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Renders each lure's low-poly mesh to a tackle-box sprite and assigns it
/// on the lure asset. Open from Wilo > Bake Lure Icons.
/// </summary>
public static class LureIconBaker
{
    const string Folder = "Assets/Fishing/Art/Lures";
    const int Size = 256;

    [MenuItem("Wilo/Bake Lure Icons")]
    public static void BakeFromMenu()
    {
        string status = BakeAll();
        EditorUtility.DisplayDialog("Lure Icons", status, "OK");
    }

    public static string BakeAll()
    {
        if (!Directory.Exists(Folder))
            Directory.CreateDirectory(Folder);

        string[] gids = AssetDatabase.FindAssets("t:LureDefinition");
        if (gids.Length == 0)
            return "No LureDefinition assets found.";

        int baked = 0;
        for (int i = 0; i < gids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(gids[i]);
            var lure = AssetDatabase.LoadAssetAtPath<LureDefinition>(assetPath);
            if (lure == null)
                continue;

            BakeOne(lure, $"{Folder}/{lure.name}.png");
            baked++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return $"Baked {baked} lure icon(s) into {Folder}.";
    }

    static void BakeOne(LureDefinition lure, string pngPath)
    {
        var preview = new PreviewRenderUtility();
        Texture2D tex = null;
        try
        {
            preview.camera.orthographic = true;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(0.957f, 0.886f, 0.776f, 1f);
            preview.camera.nearClipPlane = 0.01f;
            preview.camera.farClipPlane = 12f;
            preview.camera.allowHDR = false;
            preview.camera.allowMSAA = false;
            preview.ambientColor = new Color(0.62f, 0.64f, 0.66f);

            preview.lights[0].intensity = 1.15f;
            preview.lights[0].color = new Color(1f, 0.97f, 0.92f);
            preview.lights[0].transform.rotation = Quaternion.Euler(28f, -35f, 0f);
            if (preview.lights.Length > 1)
            {
                preview.lights[1].intensity = 0.55f;
                preview.lights[1].color = new Color(0.75f, 0.82f, 0.9f);
                preview.lights[1].transform.rotation = Quaternion.Euler(50f, 140f, 0f);
            }

            var hold = new GameObject("LurePreview");
            var visual = hold.AddComponent<LurePlaceholder>();
            visual.Apply(lure);
            preview.AddSingleGO(hold);

            Bounds bounds = Encapsulate(hold);
            Vector3 view = ViewDir(lure.Kind);
            preview.camera.transform.position = bounds.center - view * 3f;
            preview.camera.transform.rotation = Quaternion.LookRotation(view, Vector3.up);
            preview.camera.orthographicSize = Mathf.Max(0.08f, bounds.extents.magnitude * 1.08f);

            preview.BeginStaticPreview(new Rect(0f, 0f, Size, Size));
            preview.camera.Render();
            tex = preview.EndStaticPreview();
            File.WriteAllBytes(pngPath, tex.EncodeToPNG());
        }
        finally
        {
            if (tex != null)
                Object.DestroyImmediate(tex);
            preview.Cleanup();
        }

        AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spritePixelsPerUnit = 100f;
            importer.maxTextureSize = Size;
            importer.SaveAndReimport();
        }

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
        Undo.RecordObject(lure, "Assign lure icon");
        lure.Icon = sprite;
        EditorUtility.SetDirty(lure);
    }

    static Vector3 ViewDir(LureKind kind)
    {
        switch (kind)
        {
            case LureKind.Worm:
                return Quaternion.Euler(8f, 72f, 0f) * Vector3.forward;
            case LureKind.Spinnerbait:
                return Quaternion.Euler(0f, 72f, 0f) * Vector3.forward;
            case LureKind.Jig:
                return Quaternion.Euler(14f, 40f, 0f) * Vector3.forward;
            case LureKind.Crankbait:
                return Quaternion.Euler(12f, 55f, 0f) * Vector3.forward;
            case LureKind.Topwater:
                return Quaternion.Euler(4f, 68f, 0f) * Vector3.forward;
            default:
                return Quaternion.Euler(6f, 48f, 0f) * Vector3.forward;
        }
    }

    static Bounds Encapsulate(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(root.transform.position, Vector3.one * 0.2f);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
