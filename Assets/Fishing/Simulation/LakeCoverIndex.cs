using System.Collections.Generic;
using UnityEngine;

public enum CoverKind
{
    Rock,
    Wood,
    Vegetation
}

/// <summary>
/// Spatial hash of painted cover. Built once from scene roots; queries
/// stay cheap even with thousands of weeds.
/// </summary>
public sealed class LakeCoverIndex
{
    const float Cell = 16f;

    readonly List<CoverPoint> points = new List<CoverPoint>(256);
    readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>(256);
    readonly HashSet<int> seen = new HashSet<int>();

    public int Count => points.Count;

    public void Clear()
    {
        points.Clear();
        grid.Clear();
    }

    public void Add(float x, float z, float radius, CoverKind kind)
    {
        points.Add(new CoverPoint(x, z, Mathf.Max(0.6f, radius), kind));
    }

    public void Bake()
    {
        grid.Clear();
        for (int i = 0; i < points.Count; i++)
        {
            CoverPoint p = points[i];
            float bakeRadius = p.Radius;
            if (p.Kind == CoverKind.Vegetation)
                bakeRadius += 10f;
            int reach = Mathf.Max(1, Mathf.CeilToInt(bakeRadius / Cell));
            int cx = Mathf.FloorToInt(p.X / Cell);
            int cz = Mathf.FloorToInt(p.Z / Cell);
            for (int x = cx - reach; x <= cx + reach; x++)
            {
                for (int z = cz - reach; z <= cz + reach; z++)
                {
                    long key = Key(x, z);
                    if (!grid.TryGetValue(key, out List<int> list))
                    {
                        list = new List<int>(4);
                        grid[key] = list;
                    }

                    list.Add(i);
                }
            }
        }
    }

    public void Evaluate(
        float x,
        float z,
        float extraRock,
        float extraWood,
        float extraVeg,
        out float rock,
        out float wood,
        out float veg)
    {
        rock = 0f;
        wood = 0f;
        veg = 0f;
        seen.Clear();

        float extraMax = Mathf.Max(extraRock, Mathf.Max(extraWood, extraVeg));
        int span = Mathf.Max(1, Mathf.CeilToInt((extraMax + 10f) / Cell));
        int cx = Mathf.FloorToInt(x / Cell);
        int cz = Mathf.FloorToInt(z / Cell);
        for (int ix = cx - span; ix <= cx + span; ix++)
        {
            for (int iz = cz - span; iz <= cz + span; iz++)
            {
                if (!grid.TryGetValue(Key(ix, iz), out List<int> list))
                    continue;

                for (int i = 0; i < list.Count; i++)
                {
                    int id = list[i];
                    if (!seen.Add(id))
                        continue;

                    CoverPoint p = points[id];
                    float extra = extraVeg;
                    if (p.Kind == CoverKind.Wood)
                        extra = extraWood;
                    else if (p.Kind == CoverKind.Rock)
                        extra = extraRock;

                    float dx = x - p.X;
                    float dz = z - p.Z;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    float reach = p.Radius + extra;
                    if (dist >= reach)
                        continue;

                    float w = 1f - dist / reach;
                    if (p.Kind == CoverKind.Wood)
                        w = w * w * w * w;
                    else
                        w *= w;

                    switch (p.Kind)
                    {
                        case CoverKind.Rock:
                            rock += w;
                            break;
                        case CoverKind.Wood:
                            wood += w;
                            break;
                        default:
                            veg += w;
                            break;
                    }
                }
            }
        }

        rock = Mathf.Clamp01(rock);
        wood = Mathf.Clamp01(wood);
        veg = Mathf.Min(veg, 16f);
    }

    public bool TryClosest(float x, float z, CoverKind kind, float maxDist, out float px, out float pz)
    {
        px = x;
        pz = z;
        float best = maxDist * maxDist;
        bool found = false;
        seen.Clear();

        int span = Mathf.Max(1, Mathf.CeilToInt(maxDist / Cell));
        int cx = Mathf.FloorToInt(x / Cell);
        int cz = Mathf.FloorToInt(z / Cell);
        for (int ix = cx - span; ix <= cx + span; ix++)
        {
            for (int iz = cz - span; iz <= cz + span; iz++)
            {
                if (!grid.TryGetValue(Key(ix, iz), out List<int> list))
                    continue;

                for (int i = 0; i < list.Count; i++)
                {
                    int id = list[i];
                    if (!seen.Add(id))
                        continue;

                    CoverPoint p = points[id];
                    if (p.Kind != kind)
                        continue;

                    float dx = x - p.X;
                    float dz = z - p.Z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 >= best)
                        continue;

                    best = d2;
                    px = p.X;
                    pz = p.Z;
                    found = true;
                }
            }
        }

        return found;
    }

    static long Key(int x, int z)
    {
        return ((long)x << 32) ^ (uint)z;
    }

    readonly struct CoverPoint
    {
        public readonly float X;
        public readonly float Z;
        public readonly float Radius;
        public readonly CoverKind Kind;

        public CoverPoint(float x, float z, float radius, CoverKind kind)
        {
            X = x;
            Z = z;
            Radius = radius;
            Kind = kind;
        }
    }
}
