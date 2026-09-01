using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates the chibi fishing vest as a skinned mesh wrapped around the
/// character's torso and weighted to its spine bones, then attaches it to the
/// player prefab. Open from Wilo > Build Chibi Fishing Vest.
/// </summary>
public static class ChibiVestBuilder
{
    const string ChibiModelPath = "Assets/PolyOne/Chibi Character/Model/SM_Chibi_Character.fbx";
    const string PlayerPrefabPath = "Assets/Player/Prefabs/Player.prefab";
    const string MeshPath = "Assets/Player/Meshes/ChibiFishingVest.mesh";
    const string ShellMaterialPath = "Assets/Player/Materials/M_ChibiVest.mat";
    const string PocketMaterialPath = "Assets/Player/Materials/M_ChibiVestTrim.mat";
    const string BodyName = "SM_Chibi_Body";
    const string VestName = "FishingVest";
    const string SettingsPath = "Assets/Editor/ChibiVestSettings.asset";

    /// <summary>Hips, Spine, Spine1 and Spine2 — the bones the vest is weighted to.</summary>
    const int LastTorsoBone = 3;
    const int NeckBone = 4;
    const int RightClavicle = 6;
    const int LeftClavicle = 12;

    // Mirrors of the settings asset, cached so the generators below stay terse.
    // All are in the chibi mesh's local space, where the character stands 0.475 tall.
    static float HemY;
    static float CollarY;
    static float ArmholeDrop;
    static float SkinGap;
    static float Thickness;
    static float OpeningDeg;
    static bool AddShoulders;
    static float ShoulderFrontDeg;
    static float ShoulderOverlap;
    static float ShoulderPeakY;
    static float ShoulderWidth;
    static float ShoulderThickness;
    static int Columns;
    static int Rings;
    static Color ShellColor;
    static Color PocketColor;
    static ChibiVestSettings.Pocket[] Patches;

    static Vector3[] bodyVerts;
    static BoneWeight[] bodyWeights;
    static bool[] isTorso;
    static bool[] isSurface;
    static bool crossIsFront;

    static List<Vector3> verts;
    static List<Vector2> uvs;
    static List<int> shellTris;
    static List<int> pocketTris;

    [MenuItem("Wilo/Chibi Fishing Vest")]
    public static void SelectSettings()
    {
        var settings = LoadSettings();
        Selection.activeObject = settings;
        EditorGUIUtility.PingObject(settings);
    }

    /// <summary>Loads the tuning asset, creating it with defaults the first time.</summary>
    public static ChibiVestSettings LoadSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<ChibiVestSettings>(SettingsPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<ChibiVestSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
        }
        return settings;
    }

    public static string Build()
    {
        var settings = LoadSettings();
        HemY = settings.hemY;
        CollarY = settings.collarY;
        ArmholeDrop = settings.armholeDrop;
        SkinGap = settings.skinGap;
        Thickness = settings.thickness;
        OpeningDeg = settings.openingDeg;
        AddShoulders = settings.addShoulders;
        ShoulderFrontDeg = settings.shoulderFrontDeg;
        ShoulderOverlap = settings.shoulderOverlap;
        ShoulderPeakY = settings.shoulderPeakY;
        ShoulderWidth = settings.shoulderWidth;
        ShoulderThickness = settings.shoulderThickness;
        Columns = Mathf.Max(9, settings.columns);
        Rings = Mathf.Max(3, settings.rings);
        ShellColor = settings.shellColor;
        PocketColor = settings.pocketColor;
        Patches = settings.pockets ?? new ChibiVestSettings.Pocket[0];

        var bodyMesh = LoadBodyMesh();
        if (bodyMesh == null)
            return $"Could not find {BodyName} in {ChibiModelPath}.";

        bodyVerts = bodyMesh.vertices;
        bodyWeights = bodyMesh.boneWeights;
        isTorso = new bool[bodyVerts.Length];
        isSurface = new bool[bodyVerts.Length];
        for (int i = 0; i < bodyVerts.Length; i++)
        {
            int bone = bodyWeights[i].boneIndex0;
            bool dominant = bodyWeights[i].weight0 > 0.5f;
            isTorso[i] = dominant && bone >= 0 && bone <= LastTorsoBone;

            // The chibi's spine only reaches the belly; above it the skin belongs to
            // the neck and clavicles. Arm bones are left out on purpose — sampling
            // them pulls the straps through the upper arm.
            isSurface[i] = dominant && (bone <= NeckBone || bone == RightClavicle || bone == LeftClavicle);
        }

        crossIsFront = ProbeWindingConvention();

        verts = new List<Vector3>();
        uvs = new List<Vector2>();
        shellTris = new List<int>();
        pocketTris = new List<int>();

        BuildShell();
        if (AddShoulders)
        {
            BuildShoulder(1f);
            BuildShoulder(-1f);
        }
        for (int i = 0; i < Patches.Length; i++)
        {
            AddPatch(Patches[i]);
            ChibiVestSettings.Pocket mirrored = Patches[i];
            mirrored.centerDeg = -mirrored.centerDeg;
            AddPatch(mirrored);
        }

        var weights = new BoneWeight[verts.Count];
        for (int i = 0; i < verts.Count; i++)
            weights[i] = TorsoWeightNear(verts[i]);

        // Rebuild in place. Copying a differently sized mesh over the asset leaves the
        // serialized vertex buffer inconsistent and the renderer silently stops drawing.
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        bool isNewAsset = mesh == null;
        if (isNewAsset)
            mesh = new Mesh();
        else
            mesh.Clear(false);

        mesh.name = "ChibiFishingVest";
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(shellTris, 0);
        mesh.SetTriangles(pocketTris, 1);
        mesh.boneWeights = weights;
        mesh.bindposes = bodyMesh.bindposes;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (isNewAsset)
            AssetDatabase.CreateAsset(mesh, MeshPath);
        else
            EditorUtility.SetDirty(mesh);

        Mesh asset = mesh;

        var shellMaterial = EnsureMaterial(ShellMaterialPath, ShellColor);
        var pocketMaterial = EnsureMaterial(PocketMaterialPath, PocketColor);
        AssetDatabase.SaveAssets();

        string attached = Attach(asset, shellMaterial, pocketMaterial);
        return $"Vest rebuilt: {verts.Count} verts, {(shellTris.Count + pocketTris.Count) / 3} tris. {attached}";
    }

    static Mesh LoadBodyMesh()
    {
        Object[] all = AssetDatabase.LoadAllAssetsAtPath(ChibiModelPath);
        for (int i = 0; i < all.Length; i++)
        {
            var mesh = all[i] as Mesh;
            if (mesh != null && mesh.name == BodyName)
                return mesh;
        }
        return null;
    }

    static string Attach(Mesh mesh, Material shellMaterial, Material pocketMaterial)
    {
        var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        var body = FindDeep(root.transform, BodyName);
        if (body == null)
        {
            PrefabUtility.UnloadPrefabContents(root);
            return $"{BodyName} is not in the player prefab, so nothing was attached.";
        }

        Transform holder = body.parent;
        var bodyRenderer = body.GetComponent<SkinnedMeshRenderer>();
        Transform[] bones = bodyRenderer.bones;
        Transform rootBone = bodyRenderer.rootBone;
        Bounds localBounds = bodyRenderer.localBounds;

        var existing = FindDeep(root.transform, VestName);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(VestName);
            go.transform.SetParent(holder, false);
        }

        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var renderer = go.GetComponent<SkinnedMeshRenderer>();
        if (renderer == null)
            renderer = go.AddComponent<SkinnedMeshRenderer>();

        renderer.sharedMesh = mesh;
        renderer.bones = bones;
        renderer.rootBone = rootBone;
        renderer.sharedMaterials = new[] { shellMaterial, pocketMaterial };
        renderer.localBounds = localBounds;
        renderer.updateWhenOffscreen = false;

        string holderName = holder.name;
        PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        return $"Attached under {holderName}.";
    }

    static Material EnsureMaterial(string path, Color color)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.1f);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static void BuildShell()
    {
        var outer = new Vector3[Rings, Columns];
        var inner = new Vector3[Rings, Columns];

        for (int i = 0; i < Rings; i++)
        {
            float t = (float)i / (Rings - 1);
            for (int j = 0; j < Columns; j++)
            {
                float deg = ColumnDeg(j);
                float y = Mathf.Lerp(HemY, TopY(deg), t);
                float r = TorsoReach(y, deg);
                Vector3 d = Direction(deg);
                inner[i, j] = new Vector3(d.x * (r + SkinGap), y, d.z * (r + SkinGap));
                outer[i, j] = new Vector3(d.x * (r + SkinGap + Thickness), y, d.z * (r + SkinGap + Thickness));
            }
        }

        AddGrid(outer, true);
        AddGrid(inner, false);

        var collar = new Vector3[2, Columns];
        var hem = new Vector3[2, Columns];
        for (int j = 0; j < Columns; j++)
        {
            collar[0, j] = inner[Rings - 1, j];
            collar[1, j] = outer[Rings - 1, j];
            hem[0, j] = outer[0, j];
            hem[1, j] = inner[0, j];
        }
        AddBand(collar, Columns, Vector3.up, shellTris);
        AddBand(hem, Columns, Vector3.down, shellTris);

        var leftEdge = new Vector3[2, Rings];
        var rightEdge = new Vector3[2, Rings];
        for (int i = 0; i < Rings; i++)
        {
            leftEdge[0, i] = inner[i, 0];
            leftEdge[1, i] = outer[i, 0];
            rightEdge[0, i] = outer[i, Columns - 1];
            rightEdge[1, i] = inner[i, Columns - 1];
        }
        AddBand(leftEdge, Rings, OpeningNormal(ColumnDeg(0), -1f), shellTris);
        AddBand(rightEdge, Rings, OpeningNormal(ColumnDeg(Columns - 1), 1f), shellTris);
    }

    /// <summary>
    /// A strap that sits on the same surface as the vest, starting below the
    /// collar so it reads as part of the shell, then climbing over the neck
    /// side of the shoulder. Arm verts are not sampled, so the path cannot
    /// jump out into the upper arm.
    /// </summary>
    static void BuildShoulder(float side)
    {
        float frontDeg = ShoulderFrontDeg * side;
        float backDeg = (180f - ShoulderFrontDeg) * side;
        float attachY = TopY(frontDeg);
        float startY = Mathf.Lerp(HemY, attachY, ShoulderOverlap);

        const int along = 10;
        const int across = 4;
        var outer = new Vector3[along, across];
        var inner = new Vector3[along, across];

        for (int i = 0; i < along; i++)
        {
            float t = (float)i / (along - 1);
            float rise = Mathf.Sin(t * Mathf.PI);
            float deg = Mathf.Lerp(frontDeg, backDeg, t);
            float y = Mathf.Lerp(startY, ShoulderPeakY, rise);

            // Wider on the vest so the root covers a real patch of shell;
            // narrower over the top so it stays between neck and arm.
            float halfDeg = Mathf.Lerp(14f, 6f, rise) * (ShoulderWidth / 0.022f);

            // Thickness points out with the vest, then more upward at the peak
            // so the strap sits on top of the shoulder instead of into the arm.
            Vector3 outward = Vector3.Lerp(Direction(deg), Vector3.up, rise * 0.75f).normalized;

            for (int j = 0; j < across; j++)
            {
                float sampleDeg = deg + side * Mathf.Lerp(-halfDeg, halfDeg, (float)j / (across - 1));
                inner[i, j] = ShellPoint(y, sampleDeg, 0f) + Vector3.up * (0.004f * rise);
                outer[i, j] = inner[i, j] + outward * ShoulderThickness;
            }
        }

        AddGridN(outer, along, across, true, shellTris);
        AddGridN(inner, along, across, false, shellTris);

        var startCap = new Vector3[2, across];
        var endCap = new Vector3[2, across];
        for (int j = 0; j < across; j++)
        {
            startCap[0, j] = inner[0, j];
            startCap[1, j] = outer[0, j];
            endCap[0, j] = outer[along - 1, j];
            endCap[1, j] = inner[along - 1, j];
        }
        AddBand(startCap, across, Direction(frontDeg), shellTris);
        AddBand(endCap, across, Direction(backDeg), shellTris);

        var neckEdge = new Vector3[2, along];
        var armEdge = new Vector3[2, along];
        for (int i = 0; i < along; i++)
        {
            neckEdge[0, i] = inner[i, 0];
            neckEdge[1, i] = outer[i, 0];
            armEdge[0, i] = outer[i, across - 1];
            armEdge[1, i] = inner[i, across - 1];
        }
        AddBand(neckEdge, along, -Vector3.right * side, shellTris);
        AddBand(armEdge, along, Vector3.right * side, shellTris);
    }

    static Vector3 ShellPoint(float y, float deg, float extra)
    {
        float r = TorsoReach(y, deg) + SkinGap + extra;
        Vector3 d = Direction(deg);
        return new Vector3(d.x * r, y, d.z * r);
    }

    static void AddPatch(ChibiVestSettings.Pocket p)
    {
        const int Sides = 12;
        var rim = new Vector3[Sides];
        var face = new Vector3[Sides];
        Vector3 sum = Vector3.zero;

        for (int k = 0; k < Sides; k++)
        {
            float phi = k * Mathf.PI * 2f / Sides;
            float u = Squircle(Mathf.Cos(phi));
            float v = Squircle(Mathf.Sin(phi));

            float rimDeg = p.centerDeg + u * p.halfDeg;
            float rimY = p.centerY + v * p.halfY;
            Vector3 rimDir = Direction(rimDeg);
            float rimR = TorsoReach(rimY, rimDeg) + SkinGap + Thickness + 0.0015f;
            rim[k] = new Vector3(rimDir.x * rimR, rimY, rimDir.z * rimR);

            float faceDeg = p.centerDeg + u * p.halfDeg * p.inset;
            float faceY = p.centerY + v * p.halfY * p.inset;
            Vector3 faceDir = Direction(faceDeg);
            float faceR = TorsoReach(faceY, faceDeg) + SkinGap + Thickness + p.lift;
            face[k] = new Vector3(faceDir.x * faceR, faceY, faceDir.z * faceR);

            sum += rim[k] + face[k];
        }

        Vector3 core = sum / (Sides * 2);
        Vector3 outward = Direction(p.centerDeg);

        int start = verts.Count;
        for (int k = 0; k < Sides; k++)
            AddVertex(rim[k], new Vector2((float)k / Sides, 0f));
        for (int k = 0; k < Sides; k++)
            AddVertex(face[k], new Vector2((float)k / Sides, 1f));

        for (int k = 0; k < Sides; k++)
        {
            int n = (k + 1) % Sides;
            Vector3 mid = (rim[k] + rim[n] + face[k] + face[n]) * 0.25f;
            AddQuad(pocketTris, start + k, start + n, start + Sides + n, start + Sides + k, mid - core);
        }

        AddFan(face, outward);
        AddFan(rim, -outward);
    }

    static void AddFan(Vector3[] loop, Vector3 reference)
    {
        int n = loop.Length;
        Vector3 center = Vector3.zero;
        for (int k = 0; k < n; k++)
            center += loop[k];
        center /= n;

        int start = verts.Count;
        AddVertex(center, new Vector2(0.5f, 0.5f));
        for (int k = 0; k < n; k++)
        {
            float phi = k * Mathf.PI * 2f / n;
            AddVertex(loop[k], new Vector2(0.5f + 0.5f * Mathf.Cos(phi), 0.5f + 0.5f * Mathf.Sin(phi)));
        }

        for (int k = 0; k < n; k++)
            AddTriangle(pocketTris, start, start + 1 + k, start + 1 + (k + 1) % n, reference);
    }

    static void AddGrid(Vector3[,] g, bool facingOut)
    {
        AddGridN(g, Rings, Columns, facingOut, shellTris);
    }

    static void AddGridN(Vector3[,] g, int rows, int cols, bool facingOut, List<int> tris)
    {
        int start = verts.Count;
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                AddVertex(g[i, j], new Vector2((float)j / (cols - 1), (float)i / (rows - 1)));

        for (int i = 0; i < rows - 1; i++)
            for (int j = 0; j < cols - 1; j++)
            {
                Vector3 mid = (g[i, j] + g[i, j + 1] + g[i + 1, j + 1] + g[i + 1, j]) * 0.25f;
                Vector3 radial = new Vector3(mid.x, 0f, mid.z);
                if (radial.sqrMagnitude < 1e-8f)
                    radial = Vector3.up;
                radial.Normalize();
                AddQuad(tris,
                    start + i * cols + j,
                    start + i * cols + j + 1,
                    start + (i + 1) * cols + j + 1,
                    start + (i + 1) * cols + j,
                    facingOut ? radial : -radial);
            }
    }

    static void AddBand(Vector3[,] g, int span, Vector3 reference, List<int> tris)
    {
        int start = verts.Count;
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < span; j++)
                AddVertex(g[i, j], new Vector2((float)j / (span - 1), i));

        for (int j = 0; j < span - 1; j++)
            AddQuad(tris, start + j, start + j + 1, start + span + j + 1, start + span + j, reference);
    }

    static void AddVertex(Vector3 p, Vector2 uv)
    {
        verts.Add(p);
        uvs.Add(uv);
    }

    static void AddQuad(List<int> tris, int a, int b, int c, int d, Vector3 reference)
    {
        AddTriangle(tris, a, b, c, reference);
        AddTriangle(tris, a, c, d, reference);
    }

    /// <summary>Emits a triangle wound so its front face points along <paramref name="reference"/>.</summary>
    static void AddTriangle(List<int> tris, int a, int b, int c, Vector3 reference)
    {
        Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
        if (!crossIsFront)
            n = -n;

        tris.Add(a);
        if (Vector3.Dot(n, reference) >= 0f)
        {
            tris.Add(b);
            tris.Add(c);
        }
        else
        {
            tris.Add(c);
            tris.Add(b);
        }
    }

    static bool ProbeWindingConvention()
    {
        var probe = new Mesh();
        probe.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
        probe.triangles = new[] { 0, 1, 2 };
        probe.RecalculateNormals();
        bool result = Vector3.Dot(probe.normals[0], Vector3.Cross(Vector3.right, Vector3.up)) > 0f;
        Object.DestroyImmediate(probe);
        return result;
    }

    static BoneWeight TorsoWeightNear(Vector3 p)
    {
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < bodyVerts.Length; i++)
        {
            if (!isTorso[i])
                continue;
            float d = (bodyVerts[i] - p).sqrMagnitude;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        BoneWeight source = bodyWeights[best];
        var index = new[] { source.boneIndex0, source.boneIndex1, source.boneIndex2, source.boneIndex3 };
        var weight = new[] { source.weight0, source.weight1, source.weight2, source.weight3 };

        float total = 0f;
        for (int k = 0; k < 4; k++)
        {
            if (index[k] < 0 || index[k] > LastTorsoBone)
                weight[k] = 0f;
            total += weight[k];
        }

        var result = new BoneWeight();
        if (total <= 0.0001f)
        {
            result.boneIndex0 = LastTorsoBone;
            result.weight0 = 1f;
            return result;
        }

        result.boneIndex0 = index[0];
        result.boneIndex1 = index[1];
        result.boneIndex2 = index[2];
        result.boneIndex3 = index[3];
        result.weight0 = weight[0] / total;
        result.weight1 = weight[1] / total;
        result.weight2 = weight[2] / total;
        result.weight3 = weight[3] / total;
        return result;
    }

    /// <summary>
    /// How far the torso reaches along <paramref name="deg"/> at a given height.
    /// Taking the furthest projection wraps the torso convexly, so the shell can
    /// never sink into the body between samples.
    /// </summary>
    static float TorsoReach(float y, float deg)
    {
        return SurfaceReach(y, deg, isSurface);
    }

    static float SurfaceReach(float y, float deg, bool[] mask)
    {
        Vector3 d = Direction(deg);
        float reach = 0f;
        for (int i = 0; i < bodyVerts.Length; i++)
        {
            if (!mask[i])
                continue;
            Vector3 v = bodyVerts[i];
            if (Mathf.Abs(v.y - y) > 0.012f)
                continue;
            float projection = v.x * d.x + v.z * d.z;
            if (projection > reach)
                reach = projection;
        }
        return reach;
    }

    static float ColumnDeg(int j)
    {
        return OpeningDeg + (360f - 2f * OpeningDeg) * j / (Columns - 1);
    }

    /// <summary>Shoulder height at a given angle, dipping at the sides to open the armholes.</summary>
    static float TopY(float deg)
    {
        float side = Mathf.Abs(Mathf.Sin(deg * Mathf.Deg2Rad));
        return CollarY - ArmholeDrop * Mathf.Pow(side, 1.6f);
    }

    static Vector3 OpeningNormal(float deg, float sign)
    {
        float r = deg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(r), 0f, -Mathf.Sin(r)) * sign;
    }

    static float Squircle(float v)
    {
        return Mathf.Sign(v) * Mathf.Pow(Mathf.Abs(v), 0.55f);
    }

    static Vector3 Direction(float deg)
    {
        float r = deg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name)
            return t;
        foreach (Transform child in t)
        {
            var found = FindDeep(child, name);
            if (found != null)
                return found;
        }
        return null;
    }
}
