using UnityEngine;
using UnityEngine.UIElements;

public class FishFightPanel : VisualElement
{
    readonly VisualElement fill;
    readonly VisualElement track;
    readonly VisualElement bar;
    readonly VisualElement fish;

    public FishFightPanel()
    {
        AddToClassList("hud-fight");
        pickingMode = PickingMode.Ignore;
        style.display = DisplayStyle.None;

        var row = new VisualElement();
        row.AddToClassList("hud-fight-row");
        row.pickingMode = PickingMode.Ignore;

        var meter = new VisualElement();
        meter.AddToClassList("hud-fight-meter");
        meter.pickingMode = PickingMode.Ignore;
        fill = new VisualElement();
        fill.AddToClassList("hud-fight-fill");
        fill.pickingMode = PickingMode.Ignore;
        meter.Add(fill);

        track = new VisualElement();
        track.AddToClassList("hud-fight-track");
        track.pickingMode = PickingMode.Ignore;

        bar = new VisualElement();
        bar.AddToClassList("hud-fight-bar");
        bar.pickingMode = PickingMode.Ignore;
        fish = new VisualElement();
        fish.AddToClassList("hud-fight-fish");
        fish.pickingMode = PickingMode.Ignore;
        fish.generateVisualContent += PaintFish;
        track.Add(bar);
        track.Add(fish);

        row.Add(meter);
        row.Add(track);
        Add(row);

        var hint = new Label("Hold");
        hint.AddToClassList("hud-fight-hint");
        hint.pickingMode = PickingMode.Ignore;
        Add(hint);
    }

    public void Tick(FishFight fight)
    {
        if (fight == null || !fight.Playing)
        {
            style.display = DisplayStyle.None;
            return;
        }

        style.display = DisplayStyle.Flex;
        BringToFront();
        float trackH = track.resolvedStyle.height;
        if (trackH < 8f)
            trackH = 300f;

        Place(bar, fight.BarY, fight.BarHeight, trackH);
        Place(fish, fight.FishY, fight.FishHeight, trackH);
        fill.style.height = Length.Percent(fight.Progress * 100f);
        fill.EnableInClassList("hud-fight-fill--hot", fight.Progress > 0.72f);
    }

    static void Place(VisualElement element, float y01, float height01, float trackH)
    {
        float h = Mathf.Max(10f, height01 * trackH);
        float y = Mathf.Clamp(y01 * trackH - h * 0.5f, 0f, trackH - h);
        element.style.height = h;
        element.style.bottom = y;
    }

    static void PaintFish(MeshGenerationContext ctx)
    {
        var r = ctx.visualElement.contentRect;
        var p = ctx.painter2D;
        Vector2 c = r.center;
        float w = r.width;
        float h = r.height;

        p.fillColor = HudTheme.TealDeep;
        p.BeginPath();
        p.MoveTo(new Vector2(c.x - w * 0.38f, c.y));
        p.BezierCurveTo(
            new Vector2(c.x - w * 0.12f, c.y - h * 0.42f),
            new Vector2(c.x + w * 0.18f, c.y - h * 0.38f),
            new Vector2(c.x + w * 0.32f, c.y));
        p.BezierCurveTo(
            new Vector2(c.x + w * 0.18f, c.y + h * 0.38f),
            new Vector2(c.x - w * 0.12f, c.y + h * 0.42f),
            new Vector2(c.x - w * 0.38f, c.y));
        p.Fill();

        p.BeginPath();
        p.MoveTo(new Vector2(c.x + w * 0.28f, c.y));
        p.LineTo(new Vector2(c.x + w * 0.48f, c.y - h * 0.28f));
        p.LineTo(new Vector2(c.x + w * 0.48f, c.y + h * 0.28f));
        p.ClosePath();
        p.Fill();

        p.fillColor = HudTheme.Panel;
        p.BeginPath();
        p.Arc(new Vector2(c.x - w * 0.16f, c.y - h * 0.06f), h * 0.08f, 0f, 360f);
        p.Fill();
    }
}
