using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Creates a stylized sand terrain layer and adds it to the active terrain
/// so it can be painted in the Terrain Paint Texture tool.
/// Open from Wilo > Add Sand Terrain Layer.
/// </summary>
public static class SandTerrainLayerSetup
{
    const string AlbedoPath = "Assets/Environment/Textures/Sand.png";
    const string NormalPath = "Assets/Environment/Textures/SandNormal.png";
    const string LayerPath = "Assets/Environment/Terrain/Layers/Sand.terrainlayer";
    const int TextureSize = 512;

    [MenuItem("Wilo/Add Sand Terrain Layer")]
    public static void AddFromMenu()
    {
        string status = CreateAndAddToTerrain(false);
        EditorUtility.DisplayDialog("Sand Terrain Layer", status, "OK");
    }

    public static string CreateAndAddToTerrain(bool forceRefreshTexture)
    {
        TerrainLayer sandLayer = EnsureSandLayer(forceRefreshTexture);
        if (sandLayer == null)
            return "Failed to create the sand terrain layer.";

        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null)
            return "Sand layer is ready, but there is no active terrain in the scene.";

        TerrainData data = terrain.terrainData;
        var layers = new List<TerrainLayer>(data.terrainLayers);
        int existing = layers.IndexOf(sandLayer);
        if (existing >= 0)
            return $"Sand is already paint layer {existing} on {terrain.name}.";

        int res = data.alphamapResolution;
        int oldCount = data.alphamapLayers;
        float[,,] oldMap = oldCount > 0 ? data.GetAlphamaps(0, 0, res, res) : null;

        Undo.RegisterCompleteObjectUndo(data, "Add Sand Terrain Layer");
        layers.Add(sandLayer);
        data.terrainLayers = layers.ToArray();

        int newCount = data.alphamapLayers;
        if (oldMap != null && newCount > oldCount)
        {
            float[,,] map = new float[res, res, newCount];
            int copyCount = Mathf.Min(oldCount, oldMap.GetLength(2));
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    for (int layer = 0; layer < copyCount; layer++)
                        map[y, x, layer] = oldMap[y, x, layer];
                }
            }

            data.SetAlphamaps(0, 0, map);
        }

        terrain.Flush();
        EditorUtility.SetDirty(data);
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        AssetDatabase.SaveAssets();

        int sandIndex = layers.Count - 1;
        return $"Added Sand as paint layer {sandIndex} on {terrain.name}. Use Terrain > Paint Texture to brush it.";
    }

    static TerrainLayer EnsureSandLayer(bool forceRefreshTexture)
    {
        bool missingAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath) == null;
        bool missingNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath) == null;
        if (forceRefreshTexture || missingAlbedo || missingNormal)
            WriteSandTextures();

        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);

        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerPath);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, LayerPath);
        }

        layer.diffuseTexture = albedo;
        layer.normalMapTexture = normal;
        layer.tileSize = new Vector2(20f, 20f);
        layer.metallic = 0f;
        layer.smoothness = 0.08f;
        layer.normalScale = 0.75f;
        EditorUtility.SetDirty(layer);
        AssetDatabase.SaveAssets();
        return layer;
    }

    static void WriteSandTextures()
    {
        GenerateSandMaps(TextureSize, out Color[] albedoPixels, out Color[] normalPixels);
        WritePng(AlbedoPath, albedoPixels, TextureSize, false);
        WritePng(NormalPath, normalPixels, TextureSize, true);
        AssetDatabase.Refresh();
    }

    static void WritePng(string path, Color[] pixels, int size, bool normalMap)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.SetPixels(pixels);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null)
            return;

        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Bilinear;
        importer.anisoLevel = 4;
        importer.mipmapEnabled = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        if (normalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
            importer.convertToNormalmap = false;
        }
        else
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
        }

        importer.SaveAndReimport();
    }

    static void GenerateSandMaps(int size, out Color[] albedo, out Color[] normal)
    {
        albedo = new Color[size * size];
        var height = new float[size * size];

        Color sandDark = new Color(0.78f, 0.64f, 0.42f);
        Color sandMid = new Color(0.90f, 0.78f, 0.55f);
        Color sandLight = new Color(0.96f, 0.88f, 0.68f);
        Color grain = new Color(0.70f, 0.56f, 0.36f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float n1 = TileNoise(x, y, size, 0.018f);
                float n2 = TileNoise(x + 19f, y + 7f, size, 0.055f);
                float n3 = TileNoise(x + 41f, y + 23f, size, 0.13f);
                float ripple = 0.5f + 0.5f * Mathf.Sin((x / (float)size) * Mathf.PI * 8f + n1 * 1.2f);

                float t = n1 * 0.42f + n2 * 0.28f + ripple * 0.18f + n3 * 0.12f;
                Color color = Color.Lerp(sandDark, sandLight, t);
                color = Color.Lerp(color, sandMid, 0.22f);

                float speck = Hash((uint)(x * 73856093 ^ y * 19349663));
                if (speck > 0.93f)
                    color = Color.Lerp(color, grain, 0.28f);
                else if (speck < 0.06f)
                    color = Color.Lerp(color, sandLight, 0.22f);

                int i = y * size + x;
                albedo[i] = color;
                height[i] = 0.5f
                    + (n1 - 0.5f) * 0.10f
                    + (ripple - 0.5f) * 0.16f
                    + (n3 - 0.5f) * 0.05f
                    + (speck - 0.5f) * 0.03f;
            }
        }

        normal = new Color[size * size];
        const float bump = 3.2f;
        for (int y = 0; y < size; y++)
        {
            int y0 = (y + size - 1) % size;
            int y1 = (y + 1) % size;
            for (int x = 0; x < size; x++)
            {
                int x0 = (x + size - 1) % size;
                int x1 = (x + 1) % size;
                float dx = height[y * size + x1] - height[y * size + x0];
                float dy = height[y1 * size + x] - height[y0 * size + x];
                Vector3 n = new Vector3(-dx * bump, -dy * bump, 1f).normalized;
                normal[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
        }
    }

    static float TileNoise(float x, float y, int size, float freq)
    {
        float period = size * freq;
        float x0 = x * freq;
        float y0 = y * freq;
        float x1 = x0 - period;
        float y1 = y0 - period;
        float sx = x / size;
        float sy = y / size;
        float n00 = Mathf.PerlinNoise(x0, y0);
        float n10 = Mathf.PerlinNoise(x1, y0);
        float n01 = Mathf.PerlinNoise(x0, y1);
        float n11 = Mathf.PerlinNoise(x1, y1);
        float nx0 = Mathf.Lerp(n00, n10, sx);
        float nx1 = Mathf.Lerp(n01, n11, sx);
        return Mathf.Lerp(nx0, nx1, sy);
    }

    static float Hash(uint n)
    {
        n ^= n >> 16;
        n *= 0x7feb352du;
        n ^= n >> 15;
        n *= 0x846ca68bu;
        n ^= n >> 16;
        return (n & 0x00FFFFFFu) / 16777215f;
    }
}
