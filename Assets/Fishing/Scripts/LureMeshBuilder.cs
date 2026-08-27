using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Low-poly bait meshes with vertex colours. Built to read as the real lure
/// kinds at boat distance, not as a pile of primitives.
/// +Z is the nose / line-tie so retrieve orientation matches PlayerFishing.
/// </summary>
public static class LureMeshBuilder
{
    public const float BladeSpinSpeed = 720f;

    static readonly Color Metal = new Color(0.84f, 0.82f, 0.74f);
    static readonly Color Lead = new Color(0.30f, 0.29f, 0.28f);
    static readonly Color White = new Color(0.93f, 0.91f, 0.86f);
    static readonly Color Pupil = new Color(0.08f, 0.08f, 0.09f);
    static readonly Color Lip = new Color(0.78f, 0.86f, 0.90f);

    public struct Result
    {
        public Mesh Body;
        public Mesh Blade;
        public Vector3 BladeLocalPosition;
        public float BladeSpinDegreesPerSecond;
    }

    public static Result Build(LureKind kind, Color color)
    {
        var body = new Scratch();
        var blade = new Scratch();
        var result = new Result();

        switch (kind)
        {
            case LureKind.Spinnerbait:
                BuildSpinner(body, blade, color);
                result.BladeLocalPosition = new Vector3(0f, 0.09f, -0.10f);
                result.BladeSpinDegreesPerSecond = BladeSpinSpeed;
                break;
            case LureKind.Jig:
                BuildJig(body, color);
                break;
            case LureKind.Crankbait:
                BuildCrankbait(body, color);
                break;
            case LureKind.Topwater:
                BuildTopwater(body, color);
                break;
            case LureKind.Dropshot:
                BuildDropshot(body, color);
                break;
            default:
                BuildWorm(body, color);
                break;
        }

        result.Body = body.ToMesh("LureBody");
        if (blade.HasGeometry)
        {
            result.Blade = blade.ToMesh("LureBlade");
            result.Blade.RecalculateBounds();
        }

        result.Body.RecalculateBounds();
        return result;
    }

    static void BuildWorm(Scratch s, Color color)
    {
        Color dark = Color.Lerp(color, Color.black, 0.18f);
        const int rings = 12;
        const float half = 0.20f;
        const float radius = 0.024f;
        var path = new Vector3[rings];
        var radii = new float[rings];
        for (int i = 0; i < rings; i++)
        {
            float t = i / (rings - 1f);
            float z = Mathf.Lerp(half, -half, t);
            float u = z / half;
            path[i] = new Vector3(0f, -0.014f * u * u - 0.008f, z);
            radii[i] = radius;
        }

        s.AddTube(path, radii, 6, (i, _) => (i & 1) == 0 ? color : dark);
        AddJHook(s, Vector3.zero, Vector3.back, 0.072f, Metal);
    }

    static void BuildSpinner(Scratch body, Scratch blade, Color color)
    {
        Color headColor = Color.Lerp(new Color(0.12f, 0.12f, 0.13f), color, 0.12f);
        Color skirt = Color.Lerp(headColor, new Color(0.18f, 0.16f, 0.08f), 0.35f);
        Color gold = new Color(0.82f, 0.58f, 0.18f);
        Color bladeColor = new Color(0.16f, 0.16f, 0.17f);

        body.AddTorus(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), 0.012f, 0.0032f, 8, 4, Metal);

        Vector3 head = new Vector3(0f, -0.042f, -0.12f);
        AddWire(body, Vector3.zero, head + new Vector3(0f, 0.012f, 0.03f), 0.0032f, Metal);
        Vector3 elbow = new Vector3(0f, 0.09f, -0.02f);
        Vector3 bladeAt = new Vector3(0f, 0.09f, -0.10f);
        AddWire(body, Vector3.zero, elbow, 0.0032f, Metal);
        AddWire(body, elbow, bladeAt, 0.0032f, Metal);

        body.AddEllipsoid(head, new Vector3(0.022f, 0.02f, 0.038f), Quaternion.identity, 6, 5, headColor);
        AddEye(body, head + new Vector3(0.02f, 0.004f, 0.01f), 0.0075f, gold);
        AddEye(body, head + new Vector3(-0.02f, 0.004f, 0.01f), 0.0075f, gold);
        AddSkirt(body, head + new Vector3(0f, -0.004f, -0.03f), Vector3.back, 16, 0.15f, 0.0036f, skirt, Color.Lerp(skirt, gold, 0.4f));
        AddJHook(body, head + new Vector3(0f, 0.004f, -0.02f), Vector3.back, 0.07f, Metal);

        var leaf = new Vector3[]
        {
            new Vector3(0f, 0f, -0.004f),
            new Vector3(0f, 0.014f, -0.068f),
            new Vector3(0f, 0f, -0.145f),
            new Vector3(0f, -0.014f, -0.068f)
        };
        blade.AddPrism(leaf, Vector3.right * 0.0035f, bladeColor);
        blade.AddSphere(Vector3.zero, 0.008f, 5, 4, Metal);
    }

    static void BuildJig(Scratch s, Color color)
    {
        Color head = Color.Lerp(color, new Color(0.22f, 0.20f, 0.10f), 0.35f);
        Color skirt = Color.Lerp(color, new Color(0.28f, 0.22f, 0.08f), 0.2f);
        Color accent = new Color(0.18f, 0.28f, 0.48f);

        s.AddTorus(Vector3.zero, Quaternion.Euler(78f, 0f, 0f), 0.01f, 0.0028f, 7, 4, Metal);
        Vector3 headAt = new Vector3(0f, -0.014f, -0.022f);
        s.AddEllipsoid(headAt, new Vector3(0.018f, 0.016f, 0.022f), Quaternion.identity, 6, 5, head);

        Vector3 guardDir = new Vector3(0f, 0.5f, -0.85f).normalized;
        s.AddBox(headAt + guardDir * 0.045f, new Vector3(0.008f, 0.0022f, 0.08f), Quaternion.LookRotation(guardDir, Vector3.up), skirt);

        AddSkirt(s, headAt + new Vector3(0f, -0.002f, -0.016f), Vector3.back + Vector3.down * 0.4f, 26, 0.155f, 0.0018f, skirt, accent);
        AddJHook(s, headAt + new Vector3(0f, 0.002f, -0.008f), Vector3.back, 0.07f, Lead);
    }

    static void BuildCrankbait(Scratch s, Color color)
    {
        Color belly = Color.Lerp(color, White, 0.55f);
        Color back = Color.Lerp(color, new Color(0.12f, 0.16f, 0.14f), 0.42f);

        var profile = new Vector2[]
        {
            new Vector2(-0.15f, 0.012f),
            new Vector2(-0.11f, 0.032f),
            new Vector2(-0.04f, 0.055f),
            new Vector2(0.03f, 0.052f),
            new Vector2(0.10f, 0.036f),
            new Vector2(0.15f, 0.018f)
        };
        s.AddLathe(profile, 8, new Vector3(0.82f, 1.05f, 1f), p =>
        {
            float u = Mathf.InverseLerp(-0.10f, 0.02f, p.y);
            return Color.Lerp(belly, back, u);
        }, new Vector3(0f, -0.04f, -0.12f));

        s.AddBox(new Vector3(0f, -0.09f, 0.04f), new Vector3(0.10f, 0.012f, 0.11f), Quaternion.Euler(42f, 0f, 0f), Lip);

        AddEye(s, new Vector3(0.038f, -0.028f, -0.04f), 0.012f);
        AddEye(s, new Vector3(-0.038f, -0.028f, -0.04f), 0.012f);

        s.AddBox(new Vector3(0f, 0f, -0.14f), new Vector3(0.01f, 0.018f, 0.08f), Quaternion.identity, back);
        s.AddBox(new Vector3(0f, -0.03f, -0.275f), new Vector3(0.004f, 0.04f, 0.03f), Quaternion.identity, back);
        s.AddTorus(Vector3.zero, Quaternion.Euler(80f, 0f, 0f), 0.012f, 0.003f, 7, 4, Metal);
    }

    static void BuildTopwater(Scratch s, Color color)
    {
        Color black = new Color(0.08f, 0.08f, 0.09f);
        Color cupInner = new Color(0.94f, 0.93f, 0.90f);
        Color pink = new Color(0.88f, 0.28f, 0.36f);
        Color yellow = new Color(0.95f, 0.78f, 0.12f);
        Color redRing = new Color(0.72f, 0.16f, 0.18f);
        Color lime = new Color(0.62f, 0.90f, 0.16f);
        Color charcoal = new Color(0.14f, 0.14f, 0.15f);
        Color collar = new Color(0.28f, 0.28f, 0.30f);
        _ = color;

        var profile = new Vector2[]
        {
            new Vector2(-0.11f, 0.026f),
            new Vector2(-0.07f, 0.042f),
            new Vector2(-0.02f, 0.050f),
            new Vector2(0.03f, 0.048f),
            new Vector2(0.055f, 0.040f)
        };
        s.AddLathe(profile, 8, new Vector3(0.95f, 1.08f, 1f), p =>
        {
            if (p.y > 0.012f)
                return black;
            Color belly = White;
            float spot = Mathf.Max(
                Mathf.PerlinNoise(p.z * 7.5f + 1.2f, Mathf.Abs(p.x) * 9f + 2.4f),
                Mathf.PerlinNoise(p.z * 4.2f + 6.1f, p.y * 8f + 3.8f));
            if (spot > 0.48f)
                belly = black;
            return belly;
        }, new Vector3(0f, 0.008f, -0.055f));

        const float rimZ = 0.012f;
        const float rimR = 0.062f;
        s.AddCylinder(new Vector3(0f, 0.01f, rimZ), rimR, 0.018f, 8, Quaternion.Euler(90f, 0f, 0f), black);
        s.AddDisc(new Vector3(0f, 0.01f, rimZ + 0.01f), Vector3.forward, rimR * 0.96f, 8, cupInner, true);
        s.AddDisc(new Vector3(0f, 0.01f, rimZ - 0.008f), Vector3.forward, rimR * 0.72f, 8, Color.Lerp(cupInner, black, 0.12f), true);
        s.AddDisc(new Vector3(0f, 0.01f, rimZ - 0.022f), Vector3.forward, rimR * 0.48f, 8, Color.Lerp(cupInner, black, 0.28f), true);
        s.AddDisc(new Vector3(0f, 0.01f, rimZ - 0.034f), Vector3.forward, rimR * 0.26f, 7, Color.Lerp(cupInner, black, 0.45f), true);
        s.AddBox(new Vector3(0f, -0.042f, rimZ + 0.004f), new Vector3(0.05f, 0.012f, 0.016f), Quaternion.identity, pink);
        s.AddTorus(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), 0.011f, 0.003f, 7, 4, Metal);

        Vector3 eye = new Vector3(0.052f, 0.032f, -0.012f);
        s.AddSphere(eye, 0.016f, 5, 4, redRing);
        AddEye(s, eye + new Vector3(0.004f, 0.002f, 0.004f), 0.013f, yellow);
        s.AddSphere(new Vector3(-eye.x, eye.y, eye.z), 0.016f, 5, 4, redRing);
        AddEye(s, new Vector3(-eye.x - 0.004f, eye.y + 0.002f, eye.z + 0.004f), 0.013f, yellow);

        s.AddSphere(new Vector3(0.044f, -0.01f, -0.028f), 0.013f, 4, 3, black);
        s.AddSphere(new Vector3(-0.044f, -0.01f, -0.028f), 0.013f, 4, 3, black);
        s.AddSphere(new Vector3(0.04f, -0.016f, -0.058f), 0.011f, 4, 3, black);
        s.AddSphere(new Vector3(-0.04f, -0.016f, -0.058f), 0.011f, 4, 3, black);
        s.AddSphere(new Vector3(0.036f, -0.008f, -0.086f), 0.009f, 4, 3, black);
        s.AddSphere(new Vector3(-0.036f, -0.008f, -0.086f), 0.009f, 4, 3, black);

        Vector3 tail = new Vector3(0f, 0.008f, -0.115f);
        s.AddCylinder(tail, 0.022f, 0.016f, 6, Quaternion.Euler(90f, 0f, 0f), collar);
        AddSkirt(s, tail + Vector3.back * 0.008f, Vector3.back + Vector3.down * 0.08f, 12, 0.10f, 0.0034f, charcoal, charcoal);
        AddSkirt(s, tail + Vector3.back * 0.008f, Vector3.back + Vector3.up * 0.55f, 12, 0.11f, 0.0034f, lime, lime);

        s.AddTorus(new Vector3(0f, -0.038f, -0.04f), Quaternion.identity, 0.008f, 0.0024f, 6, 4, Metal);
        s.AddBox(new Vector3(0f, -0.058f, -0.04f), new Vector3(0.016f, 0.026f, 0.004f), Quaternion.identity, Metal);
        s.AddBox(new Vector3(0.012f, -0.068f, -0.04f), new Vector3(0.004f, 0.02f, 0.014f), Quaternion.Euler(0f, 40f, 0f), Metal);
        s.AddBox(new Vector3(-0.012f, -0.068f, -0.04f), new Vector3(0.004f, 0.02f, 0.014f), Quaternion.Euler(0f, -40f, 0f), Metal);
    }

    static void BuildDropshot(Scratch s, Color color)
    {
        Color bait = Color.Lerp(color, White, 0.2f);
        Color dark = Color.Lerp(color, Color.black, 0.18f);

        var profile = new Vector2[]
        {
            new Vector2(-0.12f, 0.012f),
            new Vector2(-0.06f, 0.032f),
            new Vector2(0.02f, 0.038f),
            new Vector2(0.08f, 0.028f),
            new Vector2(0.12f, 0.014f)
        };
        s.AddLathe(profile, 7, new Vector3(0.85f, 1.05f, 1f), p => p.y > 0.01f ? dark : bait, new Vector3(0f, 0.14f, 0f));
        AddEye(s, new Vector3(0.022f, 0.148f, 0.07f), 0.009f);
        AddEye(s, new Vector3(-0.022f, 0.148f, 0.07f), 0.009f);
        s.AddBox(new Vector3(0f, 0.14f, -0.125f), new Vector3(0.004f, 0.028f, 0.022f), Quaternion.identity, dark);

        s.AddCylinder(new Vector3(0f, 0.02f, 0.02f), 0.005f, 0.22f, 5, Quaternion.identity, Metal);

        s.AddEllipsoid(new Vector3(0f, -0.10f, 0.02f), new Vector3(0.032f, 0.055f, 0.032f), Quaternion.identity, 6, 5, Lead);
        s.AddSphere(new Vector3(0f, -0.05f, 0.02f), 0.014f, 5, 4, Lead);
    }

    static void AddSkirt(Scratch s, Vector3 origin, Vector3 trail, int strands, float length, float thickness, Color color, Color accent)
    {
        Vector3 back = trail.sqrMagnitude > 0.0001f ? trail.normalized : Vector3.back;
        Vector3 up = Mathf.Abs(Vector3.Dot(back, Vector3.up)) > 0.92f ? Vector3.right : Vector3.up;
        Vector3 side = Vector3.Cross(up, back).normalized;
        up = Vector3.Cross(back, side);
        for (int i = 0; i < strands; i++)
        {
            float yaw = i * (Mathf.PI * 2f / strands) + i * 0.11f;
            float spread = 0.18f + (i % 3) * 0.05f;
            Vector3 dir = (back + (Mathf.Cos(yaw) * side + Mathf.Sin(yaw) * up) * spread).normalized;
            float len = length * (0.78f + (i % 5) * 0.055f);
            Color c = (i % 4) == 0 ? accent : color;
            s.AddBox(
                origin + dir * (len * 0.5f),
                new Vector3(thickness * 0.4f, thickness, len),
                Quaternion.LookRotation(dir, Vector3.up),
                c);
        }
    }

    static void AddEye(Scratch s, Vector3 pos, float radius)
    {
        AddEye(s, pos, radius, White);
    }

    static void AddEye(Scratch s, Vector3 pos, float radius, Color iris)
    {
        s.AddSphere(pos, radius, 5, 4, iris);
        Vector3 outward = new Vector3(Mathf.Sign(pos.x == 0f ? 1f : pos.x) * 0.45f, 0.1f, 0.4f).normalized;
        s.AddSphere(pos + outward * radius * 0.45f, radius * 0.45f, 4, 3, Pupil);
    }

    static void AddWire(Scratch s, Vector3 a, Vector3 b, float radius, Color color)
    {
        Vector3 delta = b - a;
        if (delta.sqrMagnitude < 1e-8f)
            return;
        s.AddCylinder((a + b) * 0.5f, radius, delta.magnitude, 5, Quaternion.FromToRotation(Vector3.up, delta.normalized), color);
    }

    static void AddJHook(Scratch s, Vector3 eye, Vector3 back, float size, Color color)
    {
        Vector3 rear = back.normalized;
        s.AddTorus(eye, Quaternion.LookRotation(rear, Vector3.up) * Quaternion.Euler(90f, 0f, 0f), size * 0.14f, size * 0.045f, 7, 4, color);
        Vector3 shank = eye + rear * size * 0.45f;
        AddWire(s, eye, shank, size * 0.04f, color);
        Vector3 bend = shank + rear * size * 0.18f + Vector3.up * size * 0.12f;
        AddWire(s, shank, bend, size * 0.038f, color);
        Vector3 point = bend - rear * size * 0.22f + Vector3.up * size * 0.16f;
        AddWire(s, bend, point, size * 0.032f, color);
        s.AddSphere(point, size * 0.04f, 4, 3, color);
    }

    sealed class Scratch
    {
        readonly List<Vector3> verts = new List<Vector3>(256);
        readonly List<Color32> colors = new List<Color32>(256);
        readonly List<int> tris = new List<int>(512);

        public bool HasGeometry => verts.Count > 0;

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            Color32 c32 = color;
            int i = verts.Count;
            verts.Add(a);
            verts.Add(b);
            verts.Add(c);
            colors.Add(c32);
            colors.Add(c32);
            colors.Add(c32);
            tris.Add(i);
            tris.Add(i + 1);
            tris.Add(i + 2);
        }

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            AddTriangle(a, b, c, color);
            AddTriangle(a, c, d, color);
        }

        public void AddBox(Vector3 center, Vector3 size, Quaternion rot, Color color)
        {
            Vector3 e = size * 0.5f;
            Vector3[] p =
            {
                center + rot * new Vector3(-e.x, -e.y, -e.z),
                center + rot * new Vector3(e.x, -e.y, -e.z),
                center + rot * new Vector3(e.x, -e.y, e.z),
                center + rot * new Vector3(-e.x, -e.y, e.z),
                center + rot * new Vector3(-e.x, e.y, -e.z),
                center + rot * new Vector3(e.x, e.y, -e.z),
                center + rot * new Vector3(e.x, e.y, e.z),
                center + rot * new Vector3(-e.x, e.y, e.z)
            };
            AddQuad(p[0], p[1], p[2], p[3], color);
            AddQuad(p[7], p[6], p[5], p[4], color);
            AddQuad(p[4], p[5], p[1], p[0], color);
            AddQuad(p[6], p[7], p[3], p[2], color);
            AddQuad(p[5], p[6], p[2], p[1], color);
            AddQuad(p[7], p[4], p[0], p[3], color);
        }

        public void AddPrism(Vector3[] outline, Vector3 along, Color color)
        {
            int n = outline.Length;
            var top = new Vector3[n];
            var bot = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                top[i] = outline[i] + along;
                bot[i] = outline[i] - along;
            }

            for (int i = 1; i < n - 1; i++)
            {
                AddTriangle(top[0], top[i], top[i + 1], color);
                AddTriangle(bot[0], bot[i + 1], bot[i], color);
            }

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                AddQuad(bot[i], bot[j], top[j], top[i], color);
            }
        }

        public void AddSphere(Vector3 center, float radius, int slices, int stacks, Color color)
        {
            AddEllipsoid(center, Vector3.one * radius, Quaternion.identity, slices, stacks, color);
        }

        public void AddEllipsoid(Vector3 center, Vector3 radii, Quaternion rot, int slices, int stacks, Color color)
        {
            slices = Mathf.Max(3, slices);
            stacks = Mathf.Max(2, stacks);
            var rings = new Vector3[stacks + 1][];
            for (int y = 0; y <= stacks; y++)
            {
                float v = y / (float)stacks;
                float pitch = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, v);
                float cy = Mathf.Sin(pitch);
                float cr = Mathf.Cos(pitch);
                rings[y] = new Vector3[slices];
                for (int x = 0; x < slices; x++)
                {
                    float yaw = x * (Mathf.PI * 2f / slices);
                    var local = new Vector3(Mathf.Cos(yaw) * cr * radii.x, cy * radii.y, Mathf.Sin(yaw) * cr * radii.z);
                    rings[y][x] = center + rot * local;
                }
            }

            for (int y = 0; y < stacks; y++)
            {
                for (int x = 0; x < slices; x++)
                {
                    int n = (x + 1) % slices;
                    AddQuad(rings[y][x], rings[y][n], rings[y + 1][n], rings[y + 1][x], color);
                }
            }
        }

        public void AddCylinder(Vector3 center, float radius, float height, int sides, Quaternion rot, Color color)
        {
            sides = Mathf.Max(3, sides);
            float h = height * 0.5f;
            var top = new Vector3[sides];
            var bot = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i * (Mathf.PI * 2f / sides);
                var rim = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                top[i] = center + rot * (rim + Vector3.up * h);
                bot[i] = center + rot * (rim - Vector3.up * h);
            }

            Vector3 topC = center + rot * (Vector3.up * h);
            Vector3 botC = center + rot * (Vector3.down * h);
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                AddQuad(bot[i], bot[j], top[j], top[i], color);
                AddTriangle(topC, top[i], top[j], color);
                AddTriangle(botC, bot[j], bot[i], color);
            }
        }

        public void AddDisc(Vector3 center, Vector3 normal, float radius, int sides, Color color, bool doubleSided)
        {
            sides = Mathf.Max(3, sides);
            var rot = Quaternion.FromToRotation(Vector3.up, normal.normalized);
            var rim = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i * (Mathf.PI * 2f / sides);
                rim[i] = center + rot * new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            }

            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                AddTriangle(center, rim[i], rim[j], color);
                if (doubleSided)
                    AddTriangle(center, rim[j], rim[i], color);
            }
        }

        public void AddTorus(Vector3 center, Quaternion rot, float major, float minor, int segs, int tube, Color color)
        {
            segs = Mathf.Max(3, segs);
            tube = Mathf.Max(3, tube);
            var rings = new Vector3[segs][];
            for (int i = 0; i < segs; i++)
            {
                float a = i * (Mathf.PI * 2f / segs);
                var ringCenter = new Vector3(Mathf.Cos(a) * major, 0f, Mathf.Sin(a) * major);
                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                rings[i] = new Vector3[tube];
                for (int j = 0; j < tube; j++)
                {
                    float b = j * (Mathf.PI * 2f / tube);
                    var local = ringCenter + outward * Mathf.Cos(b) * minor + Vector3.up * Mathf.Sin(b) * minor;
                    rings[i][j] = center + rot * local;
                }
            }

            for (int i = 0; i < segs; i++)
            {
                int n = (i + 1) % segs;
                for (int j = 0; j < tube; j++)
                {
                    int m = (j + 1) % tube;
                    AddQuad(rings[i][j], rings[n][j], rings[n][m], rings[i][m], color);
                }
            }
        }

        public void AddLathe(Vector2[] profile, int sides, Vector3 squash, System.Func<Vector3, Color> colorOf, Vector3 offset = default)
        {
            sides = Mathf.Max(3, sides);
            int rings = profile.Length;
            var grid = new Vector3[rings][];
            for (int i = 0; i < rings; i++)
            {
                grid[i] = new Vector3[sides];
                float z = profile[i].x * squash.z;
                float r = profile[i].y;
                for (int s = 0; s < sides; s++)
                {
                    float a = s * (Mathf.PI * 2f / sides);
                    grid[i][s] = offset + new Vector3(
                        Mathf.Cos(a) * r * squash.x,
                        Mathf.Sin(a) * r * squash.y,
                        z);
                }
            }

            for (int i = 0; i < rings - 1; i++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int n = (s + 1) % sides;
                    Vector3 a = grid[i][s];
                    Vector3 b = grid[i][n];
                    Vector3 c = grid[i + 1][n];
                    Vector3 d = grid[i + 1][s];
                    Color col = colorOf((a + b + c + d) * 0.25f);
                    AddQuad(a, b, c, d, col);
                }
            }

            CapLathe(grid[0], offset + new Vector3(0f, 0f, profile[0].x * squash.z), false, colorOf);
            CapLathe(grid[rings - 1], offset + new Vector3(0f, 0f, profile[rings - 1].x * squash.z), true, colorOf);
        }

        void CapLathe(Vector3[] ring, Vector3 center, bool outward, System.Func<Vector3, Color> colorOf)
        {
            Color col = colorOf(center);
            for (int i = 0; i < ring.Length; i++)
            {
                int n = (i + 1) % ring.Length;
                if (outward)
                    AddTriangle(center, ring[i], ring[n], col);
                else
                    AddTriangle(center, ring[n], ring[i], col);
            }
        }

        public void AddTube(Vector3[] path, float[] radii, int sides, System.Func<int, Vector3, Color> colorOf)
        {
            sides = Mathf.Max(3, sides);
            int rings = path.Length;
            var grid = new Vector3[rings][];
            Vector3 prev = Vector3.up;
            for (int i = 0; i < rings; i++)
            {
                Vector3 tangent = i < rings - 1 ? (path[i + 1] - path[i]) : (path[i] - path[i - 1]);
                if (tangent.sqrMagnitude < 1e-8f)
                    tangent = Vector3.forward;
                tangent.Normalize();
                Vector3 binormal = Vector3.Cross(tangent, prev);
                if (binormal.sqrMagnitude < 1e-6f)
                    binormal = Vector3.Cross(tangent, Vector3.right);
                binormal.Normalize();
                Vector3 normal = Vector3.Cross(binormal, tangent).normalized;
                prev = normal;
                grid[i] = new Vector3[sides];
                for (int s = 0; s < sides; s++)
                {
                    float a = s * (Mathf.PI * 2f / sides);
                    grid[i][s] = path[i] + (normal * Mathf.Cos(a) + binormal * Mathf.Sin(a)) * radii[i];
                }
            }

            for (int i = 0; i < rings - 1; i++)
            {
                Color col = colorOf(i, path[i]);
                for (int s = 0; s < sides; s++)
                {
                    int n = (s + 1) % sides;
                    AddQuad(grid[i][s], grid[i][n], grid[i + 1][n], grid[i + 1][s], col);
                }
            }
        }
    }
}
