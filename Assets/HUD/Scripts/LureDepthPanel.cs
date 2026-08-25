using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shows how deep the lure is riding against the bed under it. Depth decides
/// whether a fish will come for the lure, so the player needs to see it.
/// </summary>
public class LureDepthPanel : VisualElement
{
    const float MarkerHeight = 14f;

    readonly VisualElement column;
    readonly VisualElement marker;
    readonly Label readout;
    readonly Label bed;

    public LureDepthPanel()
    {
        AddToClassList("hud-lure-depth");
        pickingMode = PickingMode.Ignore;
        style.display = DisplayStyle.None;

        var title = new Label("Lure");
        title.AddToClassList("hud-lure-depth-title");
        title.pickingMode = PickingMode.Ignore;
        Add(title);

        readout = new Label();
        readout.AddToClassList("hud-lure-depth-readout");
        readout.pickingMode = PickingMode.Ignore;
        Add(readout);

        column = new VisualElement();
        column.AddToClassList("hud-lure-depth-column");
        column.pickingMode = PickingMode.Ignore;
        column.generateVisualContent += PaintColumn;

        marker = new VisualElement();
        marker.AddToClassList("hud-lure-depth-marker");
        marker.pickingMode = PickingMode.Ignore;
        marker.generateVisualContent += PaintMarker;
        column.Add(marker);
        Add(column);

        bed = new Label();
        bed.AddToClassList("hud-lure-depth-bed");
        bed.pickingMode = PickingMode.Ignore;
        Add(bed);
    }

    public void Tick(PlayerFishing fishing)
    {
        if (fishing == null || !fishing.LureInWater)
        {
            style.display = DisplayStyle.None;
            return;
        }

        style.display = DisplayStyle.Flex;

        float bedFeet = Mathf.Max(1f, fishing.LureBedFeet);
        float depthFeet = Mathf.Clamp(fishing.LureDepthFeet, 0f, bedFeet);
        float height = column.resolvedStyle.height;
        if (height < 8f)
            height = 200f;

        float travel = Mathf.Max(0f, height - MarkerHeight);
        marker.style.top = Mathf.Clamp01(depthFeet / bedFeet) * travel;
        readout.text = $"{depthFeet:0.0} ft";
        bed.text = $"bed {bedFeet:0} ft";

        // Sitting on the bottom reads differently from swimming in the column.
        marker.EnableInClassList("hud-lure-depth-marker--down", depthFeet >= bedFeet - 0.6f);
    }

    static void PaintColumn(MeshGenerationContext ctx)
    {
        Rect r = ctx.visualElement.contentRect;
        if (r.height < 1f || r.width < 1f)
            return;

        var p = ctx.painter2D;
        const int bands = 12;
        float bandH = r.height / bands;
        for (int i = 0; i < bands; i++)
        {
            float t = bands == 1 ? 0f : i / (float)(bands - 1);
            p.fillColor = t < 0.5f
                ? Color.Lerp(HudTheme.WaterShallow, HudTheme.WaterMid, t * 2f)
                : Color.Lerp(HudTheme.WaterMid, HudTheme.WaterDeep, (t - 0.5f) * 2f);
            p.BeginPath();
            p.MoveTo(new Vector2(0f, i * bandH));
            p.LineTo(new Vector2(r.width, i * bandH));
            p.LineTo(new Vector2(r.width, (i + 1) * bandH + 0.5f));
            p.LineTo(new Vector2(0f, (i + 1) * bandH + 0.5f));
            p.ClosePath();
            p.Fill();
        }

        p.fillColor = HudTheme.SonarSand;
        p.BeginPath();
        p.MoveTo(new Vector2(0f, r.height - 4f));
        p.LineTo(new Vector2(r.width, r.height - 4f));
        p.LineTo(new Vector2(r.width, r.height));
        p.LineTo(new Vector2(0f, r.height));
        p.ClosePath();
        p.Fill();
    }

    static void PaintMarker(MeshGenerationContext ctx)
    {
        Rect r = ctx.visualElement.contentRect;
        if (r.height < 1f || r.width < 1f)
            return;

        var p = ctx.painter2D;
        float y = r.height * 0.5f;

        p.strokeColor = HudTheme.Panel;
        p.lineWidth = 2f;
        p.BeginPath();
        p.MoveTo(new Vector2(0f, y));
        p.LineTo(new Vector2(r.width, y));
        p.Stroke();

        p.fillColor = HudTheme.Gold;
        p.BeginPath();
        p.Arc(new Vector2(r.width * 0.5f, y), Mathf.Min(5f, r.height * 0.42f), 0f, 360f);
        p.Fill();
    }
}
