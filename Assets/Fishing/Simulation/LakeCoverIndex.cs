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

    float maxRadius;
    float vegetationReach = 10f;

    public int Count => points.Count;

    public void Clear()
    {
        points.Clear();
        grid.Clear();
        maxRadius = 0f;
    }

    public void Add(float x, float z, float radius, CoverKind kind)
    {
        radius = Mathf.Max(0.6f, radius);
        maxRadius = Mathf.Max(maxRadius, radius);
        points.Add(new CoverPoint(x, z, radius, kind));
    }

    /// <summary>
    /// Weeds are queried with extra reach, so they must be registered in every
    /// cell that reach can touch or beds silently drop out at grid seams.
    /// </summary>
    public void Bake(float vegetationQueryReach)
    {
        vegetationReach = Mathf.Max(0f, vegetationQueryReach);
        grid.Clear();
        for (int i = 0; i < points.Count; i++)
        {
            CoverPoint p = points[i];
            float bakeRadius = p.Radius;
            if (p.Kind == CoverKind.Vegetation)
                bakeRadius += vegetationReach;
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
        int span = Mathf.Max(1, Mathf.CeilToInt((extraMax + maxRadius) / Cell));
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
                            rock += w * RockBulk(p.Radius);
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

        // Boulders read above 1 so a pile or a big rock holds more fish than a cobble.
        rock = Mathf.Min(rock, 1.45f);
        wood = Mathf.Clamp01(wood);
        veg = Mathf.Min(veg, 16f);
    }

    /// <summary>
    /// Sitting on a cobble (~0.8 m) is about 0.52; a 10 m boulder is about 1.28.
    /// Radius only used to change how far the falloff reaches without this.
    /// </summary>
    public static float RockBulk(float radius)
    {
        return Mathf.Lerp(0.52f, 1.28f, Mathf.InverseLerp(0.8f, 10f, radius));
    }

    /// <summary>
    /// Nearest cover of a kind whose position falls inside the rect. Lets a
    /// spawn cell own exactly the cover standing in it, so neighbouring cells
    /// cannot both stack fish on the same stump.
    /// </summary>
    public bool TryClosestInRect(
        float x,
        float z,
        float xMin,
        float zMin,
        float xMax,
        float zMax,
        CoverKind kind,
        out float px,
        out float pz)
    {
        px = x;
        pz = z;
        float best = float.MaxValue;
        bool found = false;
        seen.Clear();

        int ix0 = Mathf.FloorToInt(xMin / Cell);
        int ix1 = Mathf.FloorToInt(xMax / Cell);
        int iz0 = Mathf.FloorToInt(zMin / Cell);
        int iz1 = Mathf.FloorToInt(zMax / Cell);
        for (int ix = ix0; ix <= ix1; ix++)
        {
            for (int iz = iz0; iz <= iz1; iz++)
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
                    if (p.X < xMin || p.X > xMax || p.Z < zMin || p.Z > zMax)
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
