using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Scrolling depth-finder: newest sample on the right, bottom contour filled in.
/// </summary>
public class SonarElement : VisualElement
{
    const int Capacity = 96;
    readonly float[] depths = new float[Capacity];
    int count;
    int next;
    float displayMax = 20f;

    public SonarElement()
    {
        AddToClassList("hud-sonar");
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    public void Push(float depthFeet)
    {
        depths[next] = Mathf.Max(0f, depthFeet);
        next = (next + 1) % Capacity;
        if (count < Capacity)
            count++;

        float localMax = 8f;
        for (int i = 0; i < count; i++)
            localMax = Mathf.Max(localMax, At(i));
        displayMax = Mathf.Lerp(displayMax, Mathf.Ceil(localMax / 5f) * 5f + 5f, 0.12f);
        MarkDirtyRepaint();
    }

    float At(int chronologicalIndex)
    {
        int first = count < Capacity ? 0 : next;
        return depths[(first + chronologicalIndex) % Capacity];
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
        {
            float x = Mathf.Lerp(x0, x1, i / (Capacity - 1f));
            float y = Mathf.Lerp(y0, y1, Mathf.Clamp01(At(i) / span));
            p.LineTo(new Vector2(x, y));
        }

        float lastX = Mathf.Lerp(x0, x1, (count - 1) / (Capacity - 1f));
        p.LineTo(new Vector2(lastX, y1));
        p.ClosePath();
        p.Fill();

        p.strokeColor = Color.white;
        p.lineWidth = 1.6f;
        p.lineCap = LineCap.Round;
        p.lineJoin = LineJoin.Round;
        p.BeginPath();
        for (int i = 0; i < count; i++)
        {
            float x = Mathf.Lerp(x0, x1, i / (Capacity - 1f));
            float y = Mathf.Lerp(y0, y1, Mathf.Clamp01(At(i) / span));
            if (i == 0)
                p.MoveTo(new Vector2(x, y));
            else
                p.LineTo(new Vector2(x, y));
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
}
