using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Scrolling depth-finder: newest sample on the right, sand bed filled in.
/// Rocks sit on that bed as rounded stone mounds, not jagged mesh spikes.
/// </summary>
public class SonarElement : VisualElement
{
    const int Capacity = 96;
    readonly float[] beds = new float[Capacity];
    readonly float[] rocks = new float[Capacity];
    int count;
    int next;
    float displayMax = 20f;

    public SonarElement()
    {
        AddToClassList("hud-sonar");
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    public void Push(float bedFeet, float rockRiseFeet)
    {
        float bed = Mathf.Max(0f, bedFeet);
        float rock = Mathf.Max(0f, rockRiseFeet);
        if (count > 0)
        {
            int prev = (next - 1 + Capacity) % Capacity;
            bed = Mathf.Lerp(beds[prev], bed, 0.55f);
            rock = rock > rocks[prev]
                ? Mathf.Lerp(rocks[prev], rock, 0.6f)
                : Mathf.Lerp(rocks[prev], rock, 0.32f);
        }

        beds[next] = bed;
        rocks[next] = rock;
        next = (next + 1) % Capacity;
        if (count < Capacity)
            count++;

        float localMax = 8f;
        for (int i = 0; i < count; i++)
            localMax = Mathf.Max(localMax, BedAt(i));
        displayMax = Mathf.Lerp(displayMax, Mathf.Ceil(localMax / 5f) * 5f + 5f, 0.12f);
        MarkDirtyRepaint();
    }

    float BedAt(int chronologicalIndex)
    {
        int first = count < Capacity ? 0 : next;
        return beds[(first + chronologicalIndex) % Capacity];
    }

    float RockAt(int chronologicalIndex)
    {
        int first = count < Capacity ? 0 : next;
        return rocks[(first + chronologicalIndex) % Capacity];
    }

    float RiseAt(int chronologicalIndex)
    {
        float self = RockAt(chronologicalIndex);
        float left = chronologicalIndex > 0 ? RockAt(chronologicalIndex - 1) : self;
        float right = chronologicalIndex < count - 1 ? RockAt(chronologicalIndex + 1) : self;
        float smooth = 0.25f * left + 0.5f * self + 0.25f * right;
        float neighbor = Mathf.Max(left, right);
        if (smooth < 0.08f && neighbor > 0.25f)
            smooth = neighbor * 0.42f;

        float lump = 1f
            + 0.1f * Mathf.Sin(chronologicalIndex * 1.9f + 0.35f)
            + 0.06f * Mathf.Sin(chronologicalIndex * 3.3f + 1.1f);
        return Mathf.Max(0f, smooth * lump);
    }

    void Draw(MeshGenerationContext ctx)
    {
        Rect rect = contentRect;
        if (rect.width < 4f || rect.height < 4f)
            return;

        var p = ctx.painter2D;
        p.fillColor = HudTheme.SonarWater;
        p.BeginPath();
        p.MoveTo(rect.min);
        p.LineTo(new Vector2(rect.xMax, rect.yMin));
        p.LineTo(rect.max);
        p.LineTo(new Vector2(rect.xMin, rect.yMax));
        p.ClosePath();
        p.Fill();

        if (count < 2)
            return;

        float padL = 6f;
        float padR = 28f;
        float padT = 8f;
        float padB = 8f;
        float x0 = rect.xMin + padL;
        float x1 = rect.xMax - padR;
        float y0 = rect.yMin + padT;
        float y1 = rect.yMax - padB;
        float span = Mathf.Max(1f, displayMax);

        p.fillColor = HudTheme.SonarSand;
        p.BeginPath();
        p.MoveTo(new Vector2(x0, y1));
        for (int i = 0; i < count; i++)
            p.LineTo(SamplePoint(i, BedAt(i), x0, x1, y0, y1, span));

        float lastX = Mathf.Lerp(x0, x1, (count - 1) / (Capacity - 1f));
        p.LineTo(new Vector2(lastX, y1));
        p.ClosePath();
        p.Fill();

        bool anyRock = false;
        for (int i = 0; i < count; i++)
        {
            if (RiseAt(i) > 0.08f)
            {
                anyRock = true;
                break;
            }
        }

        if (anyRock)
        {
            p.fillColor = HudTheme.SonarRock;
            p.BeginPath();
            p.MoveTo(SamplePoint(0, BedAt(0), x0, x1, y0, y1, span));
            for (int i = 0; i < count; i++)
            {
                float rise = RiseAt(i);
                p.LineTo(SamplePoint(i, Mathf.Max(0f, BedAt(i) - rise), x0, x1, y0, y1, span));
            }

            for (int i = count - 1; i >= 0; i--)
                p.LineTo(SamplePoint(i, BedAt(i), x0, x1, y0, y1, span));
            p.ClosePath();
            p.Fill();
        }

        p.strokeColor = Color.white;
        p.lineWidth = 1.6f;
        p.lineCap = LineCap.Round;
        p.lineJoin = LineJoin.Round;
        p.BeginPath();
        for (int i = 0; i < count; i++)
        {
            float rise = RiseAt(i);
            Vector2 pt = SamplePoint(i, Mathf.Max(0f, BedAt(i) - rise), x0, x1, y0, y1, span);
            if (i == 0)
                p.MoveTo(pt);
            else
                p.LineTo(pt);
        }

        p.Stroke();

        p.strokeColor = new Color(1f, 1f, 1f, 0.25f);
        p.lineWidth = 1f;
        int ticks = 3;
        for (int t = 1; t <= ticks; t++)
        {
            float y = Mathf.Lerp(y0, y1, t / (float)ticks);
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y));
            p.LineTo(new Vector2(x1, y));
            p.Stroke();
        }
    }

    Vector2 SamplePoint(int i, float feet, float x0, float x1, float y0, float y1, float span)
    {
        float x = Mathf.Lerp(x0, x1, i / (Capacity - 1f));
        float y = Mathf.Lerp(y0, y1, Mathf.Clamp01(feet / span));
        return new Vector2(x, y);
    }
}
