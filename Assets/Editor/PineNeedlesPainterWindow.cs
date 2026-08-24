using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds a pine-needle terrain layer and paints it on land from water height.
/// Lakebed stays dirt. Open from Wilo > Pine Needles.
/// </summary>
public class PineNeedlesPainterWindow : EditorWindow
{
    const string PrefsPrefix = "Wilo.PineNeedles.";
    const string AlbedoPath = "Assets/Environment/Textures/PineNeedles.png";
    const string NormalPath = "Assets/Environment/Textures/PineNeedlesNormal.png";
    const string LayerPath = "Assets/Environment/Terrain/Layers/PineNeedles.terrainlayer";
    const string DirtLayerPath = "Assets/Environment/Terrain/Layers/Dirt.terrainlayer";
    const int TextureSize = 512;

    TerrainLayer needleLayer;
    float shoreOffset = 0.2f;
    float blendWidth = 2f;
    float shorelineNoise = 1.2f;
    Vector2 tileSize = new Vector2(8f, 8f);
    string status = "Needles on land, dirt under the lake. Paint uses the water surface height.";

    [MenuItem("Wilo/Pine Needles")]
    public static void Open()
    {
        var window = GetWindow<PineNeedlesPainterWindow>("Pine Needles");
        window.minSize = new Vector2(340, 280);
        window.Show();
    }

    [MenuItem("Wilo/Paint Pine Needles On Land")]
    public static void PaintFromMenu()
    {
        TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerPath);
        string status = PaintLandCover(0.2f, 2f, 1.2f, new Vector2(8f, 8f), ref layer);
        EditorUtility.DisplayDialog("Pine Needles", status, "OK");
    }

    void OnEnable()
    {
        LoadPrefs();
        needleLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerPath);
    }

    void OnDisable()
    {
        SavePrefs();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(status, MessageType.Info);

        needleLayer = (TerrainLayer)EditorGUILayout.ObjectField(
            "Needle Layer", needleLayer, typeof(TerrainLayer), false);

        shoreOffset = EditorGUILayout.Slider("Above Water (m)", shoreOffset, 0f, 2f);
        blendWidth = EditorGUILayout.Slider("Shore Blend (m)", blendWidth, 0.2f, 8f);
        shorelineNoise = EditorGUILayout.Slider("Shoreline Wobble", shorelineNoise, 0f, 4f);
        tileSize = EditorGUILayout.Vector2Field("Tile Size", tileSize);

        EditorGUILayout.Space(8);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Create / Refresh Texture", GUILayout.Height(28)))
            {
                needleLayer = EnsureNeedleLayer(true);
                status = "Needle texture ready.";
            }

            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.45f, 0.85f, 0.5f);
            if (GUILayout.Button("Paint Land Cover", GUILayout.Height(28)))
            {
                status = PaintLandCover(shoreOffset, blendWidth, shorelineNoise, tileSize, ref needleLayer);
                Repaint();
            }

            GUI.backgroundColor = prev;
        }
    }

    public static string PaintLandCover(
        float shoreOffset,
        float blendWidth,
        float shorelineNoise,
        Vector2 tileSize,
        ref TerrainLayer needleLayer,
        bool refreshTexture = false)
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null)
            return "No active terrain in the scene.";

        if (!TryGetWaterY(out float waterY))
            return "Could not find Water / Surface. Place the water mesh first.";

        needleLayer = EnsureNeedleLayer(refreshTexture);
        if (needleLayer == null)
            return "Failed to create the pine needle terrain layer.";

        needleLayer.tileSize = tileSize;
        EditorUtility.SetDirty(needleLayer);

        TerrainData data = terrain.terrainData;
        TerrainLayer dirtLayer = LoadDirtLayer(data);
        if (dirtLayer == null)
            return "Dirt terrain layer is missing.";

        int dirtIndex;
        int needleIndex;
        TerrainLayer[] layers = EnsureTerrainLayers(data, dirtLayer, needleLayer, out dirtIndex, out needleIndex);

        Undo.RegisterCompleteObjectUndo(data, "Paint Pine Needles");
        data.terrainLayers = layers;

        int res = data.alphamapResolution;
        int layerCount = data.alphamapLayers;
        if (layerCount < 2)
            return "Terrain alphamap did not expand to two layers.";

        float[,,] map = new float[res, res, layerCount];
        Vector3 origin = terrain.GetPosition();
        int landPixels = 0;
        int waterPixels = 0;

        for (int y = 0; y < res; y++)
        {
            float v = res == 1 ? 0f : (float)y / (res - 1);
            for (int x = 0; x < res; x++)
            {
                float u = res == 1 ? 0f : (float)x / (res - 1);
                float worldX = origin.x + u * data.size.x;
                float worldZ = origin.z + v * data.size.z;
                float worldY = origin.y + data.GetInterpolatedHeight(u, v);

                float wobble = 0f;
                if (shorelineNoise > 0f)
                {
                    float n = Mathf.PerlinNoise(worldX * 0.07f, worldZ * 0.07f);
                    float n2 = Mathf.PerlinNoise(worldX * 0.021f + 40f, worldZ * 0.021f);
                    wobble = (n - 0.5f) * shorelineNoise + (n2 - 0.5f) * shorelineNoise * 0.45f;
                }

                float startY = waterY + shoreOffset + wobble;
                float needles = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(startY, startY + blendWidth, worldY));

                for (int layer = 0; layer < layerCount; layer++)
                    map[y, x, layer] = 0f;

                map[y, x, dirtIndex] = 1f - needles;
                map[y, x, needleIndex] = needles;

                if (needles >= 0.5f)
                    landPixels++;
                else
                    waterPixels++;
            }
        }

        data.SetAlphamaps(0, 0, map);
        terrain.Flush();
        EditorUtility.SetDirty(data);
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);

        return $"Painted needles on land. Water Y {waterY:F2}. Land {landPixels}  ·  lakebed {waterPixels}.";
    }

    static TerrainLayer[] EnsureTerrainLayers(
        TerrainData data,
        TerrainLayer dirtLayer,
        TerrainLayer needleLayer,
        out int dirtIndex,
        out int needleIndex)
    {
        var layers = new List<TerrainLayer>(data.terrainLayers);
        if (layers.Count == 0)
            layers.Add(dirtLayer);
        else if (layers[0] == null)
            layers[0] = dirtLayer;

        dirtIndex = 0;
        needleIndex = -1;
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i] == needleLayer)
            {
                needleIndex = i;
                break;
            }
        }

        if (needleIndex < 0)
        {
            layers.Add(needleLayer);
            needleIndex = layers.Count - 1;
        }

        return layers.ToArray();
    }

    static TerrainLayer LoadDirtLayer(TerrainData data)
    {
        if (data.terrainLayers.Length > 0 && data.terrainLayers[0] != null)
            return data.terrainLayers[0];

        return AssetDatabase.LoadAssetAtPath<TerrainLayer>(DirtLayerPath);
    }

    static TerrainLayer EnsureNeedleLayer(bool forceRefreshTexture)
    {
        bool missingAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath) == null;
        bool missingNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath) == null;
        if (forceRefreshTexture || missingAlbedo || missingNormal)
            WriteNeedleTextures();

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
        if (layer.tileSize.x < 0.1f)
            layer.tileSize = new Vector2(8f, 8f);
        layer.metallic = 0f;
        layer.smoothness = 0f;
        layer.normalScale = 0.9f;
        EditorUtility.SetDirty(layer);
        AssetDatabase.SaveAssets();
        return layer;
    }

    static void WriteNeedleTextures()
    {
        GenerateNeedleMaps(TextureSize, out Color[] albedoPixels, out Color[] normalPixels);

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

    static void GenerateNeedleMaps(int size, out Color[] albedo, out Color[] normal)
    {
        albedo = new Color[size * size];
        var height = new float[size * size];

        Color duffDark = new Color(0.28f, 0.18f, 0.09f);
        Color duffLight = new Color(0.40f, 0.26f, 0.12f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.02f, y * 0.02f);
                float n2 = Mathf.PerlinNoise(x * 0.07f + 12f, y * 0.07f + 7f);
                albedo[y * size + x] = Color.Lerp(duffDark, duffLight, n * 0.7f + n2 * 0.3f);
                height[y * size + x] = 0.28f + n * 0.08f;
            }
        }

        var rng = new System.Random(17);
        Color[] needleColors =
        {
            new Color(0.62f, 0.38f, 0.14f),
            new Color(0.48f, 0.32f, 0.12f),
            new Color(0.70f, 0.48f, 0.22f),
            new Color(0.30f, 0.20f, 0.08f),
            new Color(0.55f, 0.42f, 0.18f),
            new Color(0.42f, 0.28f, 0.10f)
        };

        int clusters = size * 10;
        for (int i = 0; i < clusters; i++)
        {
            float x = (float)rng.NextDouble() * size;
            float y = (float)rng.NextDouble() * size;
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            int bunch = 3 + rng.Next(5);
            for (int b = 0; b < bunch; b++)
            {
                float a = angle + ((float)rng.NextDouble() - 0.5f) * 0.45f;
                float length = 7f + (float)rng.NextDouble() * 11f;
                float radius = 0.7f + (float)rng.NextDouble() * 0.7f;
                Color color = needleColors[rng.Next(needleColors.Length)];
                float shade = 0.85f + (float)rng.NextDouble() * 0.28f;
                color *= shade;
                color.a = 1f;
                float ox = x + ((float)rng.NextDouble() - 0.5f) * 2.4f;
                float oy = y + ((float)rng.NextDouble() - 0.5f) * 2.4f;
                StampNeedle(albedo, height, size, ox, oy, a, length, radius, color);
            }
        }

        normal = new Color[size * size];
        const float bump = 4.5f;
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

    static void StampNeedle(
        Color[] pixels,
        float[] height,
        int size,
        float x,
        float y,
        float angle,
        float length,
        float radius,
        Color color)
    {
        float dx = Mathf.Cos(angle);
        float dy = Mathf.Sin(angle);
        int steps = Mathf.Max(2, Mathf.CeilToInt(length + radius * 2f));
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float px = x + dx * (t - 0.5f) * length;
            float py = y + dy * (t - 0.5f) * length;
            StampSoftCircle(pixels, height, size, px, py, radius, color);
        }
    }

    static void StampSoftCircle(
        Color[] pixels,
        float[] height,
        int size,
        float px,
        float py,
        float radius,
        Color color)
    {
        int r = Mathf.CeilToInt(radius + 1f);
        for (int oy = -r; oy <= r; oy++)
        {
            for (int ox = -r; ox <= r; ox++)
            {
                float fx = ox - (px - Mathf.Floor(px));
                float fy = oy - (py - Mathf.Floor(py));
                float d = Mathf.Sqrt(fx * fx + fy * fy);
                if (d > radius)
                    continue;

                float a = 1f - d / radius;
                a *= a;
                int sx = Wrap((int)Mathf.Floor(px) + ox, size);
                int sy = Wrap((int)Mathf.Floor(py) + oy, size);
                int i = sy * size + sx;
                pixels[i] = Color.Lerp(pixels[i], color, a * 0.92f);
                height[i] += a * 0.16f;
            }
        }
    }

    static int Wrap(int v, int size)
    {
        v %= size;
        if (v < 0)
            v += size;
        return v;
    }

    static bool TryGetWaterY(out float waterY)
    {
        GameObject water = GameObject.Find("Water");
        if (water != null)
        {
            Transform surface = water.transform.Find("Surface");
            Renderer renderer = surface != null
                ? surface.GetComponent<Renderer>()
                : water.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                waterY = renderer.bounds.max.y;
                return true;
            }
        }

        waterY = 201.7f;
        return GameObject.Find("Water") == null && Terrain.activeTerrain != null;
    }

    void LoadPrefs()
    {
        shoreOffset = EditorPrefs.GetFloat(PrefsPrefix + "ShoreOffset", 0.2f);
        blendWidth = EditorPrefs.GetFloat(PrefsPrefix + "BlendWidth", 2f);
        shorelineNoise = EditorPrefs.GetFloat(PrefsPrefix + "ShorelineNoise", 1.2f);
        tileSize.x = EditorPrefs.GetFloat(PrefsPrefix + "TileX", 8f);
        tileSize.y = EditorPrefs.GetFloat(PrefsPrefix + "TileY", 8f);
    }

    void SavePrefs()
    {
        EditorPrefs.SetFloat(PrefsPrefix + "ShoreOffset", shoreOffset);
        EditorPrefs.SetFloat(PrefsPrefix + "BlendWidth", blendWidth);
        EditorPrefs.SetFloat(PrefsPrefix + "ShorelineNoise", shorelineNoise);
        EditorPrefs.SetFloat(PrefsPrefix + "TileX", tileSize.x);
        EditorPrefs.SetFloat(PrefsPrefix + "TileY", tileSize.y);
    }
}
