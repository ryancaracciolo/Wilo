using System;
using UnityEngine;
using UnityEngine.UIElements;

public static class HudUi
{
    public static Button IconButton(string tooltip, Action<MeshGenerationContext> paint, Action onClick)
    {
        var button = new Button();
        button.AddToClassList("hud-icon-button");
        button.tooltip = tooltip;
        button.focusable = false;
        button.clicked += onClick;

        var glyph = new VisualElement();
        glyph.AddToClassList("hud-icon-glyph");
        glyph.pickingMode = PickingMode.Ignore;
        glyph.generateVisualContent += paint;
        button.Add(glyph);
        return button;
    }

    public static Button TextButton(string label, Action onClick, bool primary = false)
    {
        var button = new Button { text = label };
        button.AddToClassList("hud-text-button");
        if (primary)
            button.AddToClassList("hud-text-button--primary");
        button.focusable = false;
        button.clicked += onClick;
        return button;
    }

    public static Label Title(string text)
    {
        var label = new Label(text);
        label.AddToClassList("hud-title");
        label.pickingMode = PickingMode.Ignore;
        return label;
    }

    public static Label Body(string text)
    {
        var label = new Label(text);
        label.AddToClassList("hud-body");
        label.pickingMode = PickingMode.Ignore;
        return label;
    }

    public static Label Muted(string text)
    {
        var label = new Label(text);
        label.AddToClassList("hud-muted");
        label.pickingMode = PickingMode.Ignore;
        return label;
    }

    /// <summary>Small status tag that rides at the end of a row, such as "Entered".</summary>
    public static Label Pill(string text, bool accent = false)
    {
        var label = new Label(text);
        label.AddToClassList("hud-pill");
        if (accent)
            label.AddToClassList("hud-pill--on");
        label.pickingMode = PickingMode.Ignore;
        return label;
    }

    /// <summary>
    /// The only place the player types. While it holds focus it raises
    /// HudInput.Typing so gameplay keys stand down, and Enter confirms.
    /// </summary>
    public static TextField NameField(string value, int maxLength, Action submit)
    {
        var field = new TextField { value = value, maxLength = maxLength, isDelayed = false };
        field.AddToClassList("hud-name-field");
        field.RegisterCallback<FocusInEvent>(_ => HudInput.Typing = true);
        field.RegisterCallback<FocusOutEvent>(_ => HudInput.Typing = false);
        field.RegisterCallback<DetachFromPanelEvent>(_ => HudInput.Typing = false);
        field.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                return;
            evt.StopPropagation();
            submit?.Invoke();
        });

        // The panel has to lay the field out before it can take focus.
        field.schedule.Execute(() => field.Focus());
        return field;
    }

    public static VisualElement Row()
    {
        var row = new VisualElement();
        row.AddToClassList("hud-row");
        return row;
    }

    public static VisualElement TabRow()
    {
        var row = Row();
        row.AddToClassList("hud-tab-row");
        return row;
    }

    public static Button Tab(string label, Action onClick, bool selected)
    {
        var tab = new Button { text = label };
        tab.AddToClassList("hud-tab");
        tab.EnableInClassList("hud-tab--on", selected);
        tab.focusable = false;
        tab.clicked += onClick;
        return tab;
    }

    public static VisualElement LockLine(string text)
    {
        var row = Row();
        row.AddToClassList("hud-lock-row");

        var glyph = new VisualElement();
        glyph.AddToClassList("hud-lock-glyph");
        glyph.pickingMode = PickingMode.Ignore;
        glyph.generateVisualContent += PaintLock;
        row.Add(glyph);

        Label label = Muted(text);
        label.AddToClassList("hud-lock-label");
        row.Add(label);
        return row;
    }

    public static VisualElement Glyph(string className, Action<MeshGenerationContext> paint)
    {
        var glyph = new VisualElement();
        glyph.AddToClassList(className);
        glyph.pickingMode = PickingMode.Ignore;
        glyph.generateVisualContent += paint;
        return glyph;
    }

    /// <summary>Compact header readout such as money or reputation.</summary>
    public static VisualElement StatChip(string tooltip, Action<MeshGenerationContext> glyph, out Label value)
    {
        var chip = new VisualElement();
        chip.AddToClassList("hud-stat-chip");
        chip.tooltip = tooltip;
        chip.pickingMode = PickingMode.Ignore;
        if (glyph != null)
            chip.Add(Glyph("hud-stat-glyph", glyph));
        value = new Label();
        value.AddToClassList("hud-stat-value");
        value.pickingMode = PickingMode.Ignore;
        chip.Add(value);
        return chip;
    }

    /// <summary>Labeled money/reputation tile used on the profile card.</summary>
    public static VisualElement StatTile(string value, string caption, Action<MeshGenerationContext> glyph = null)
    {
        var tile = new VisualElement();
        tile.AddToClassList("hud-profile-stat");

        var valueRow = Row();
        if (glyph != null)
            valueRow.Add(Glyph("hud-profile-stat-glyph", glyph));
        Label number = Title(value);
        number.AddToClassList("hud-profile-stat-value");
        valueRow.Add(number);
        tile.Add(valueRow);

        Label label = Muted(caption);
        label.AddToClassList("hud-profile-stat-label");
        tile.Add(label);
        return tile;
    }

    public static void PaintProfile(MeshGenerationContext ctx)
    {
        var r = ctx.visualElement.contentRect;
        var p = ctx.painter2D;
        Vector2 c = r.center;
        float s = Mathf.Min(r.width, r.height);

        p.fillColor = HudTheme.Ink;
        p.BeginPath();
        p.Arc(new Vector2(c.x, c.y - s * 0.16f), s * 0.16f, 0f, 360f);
        p.Fill();

        p.BeginPath();
        p.MoveTo(new Vector2(c.x - s * 0.28f, c.y + s * 0.38f));
        p.BezierCurveTo(
            new Vector2(c.x - s * 0.28f, c.y + s * 0.04f),
            new Vector2(c.x + s * 0.28f, c.y + s * 0.04f),
            new Vector2(c.x + s * 0.28f, c.y + s * 0.38f));
        p.ClosePath();
        p.Fill();
    }

    public static void PaintStar(MeshGenerationContext ctx)
    {
        var r = ctx.visualElement.contentRect;
        var p = ctx.painter2D;
        Vector2 c = r.center;
        float s = Mathf.Min(r.width, r.height);
        float outer = s * 0.42f;
        float inner = s * 0.18f;

        p.fillColor = HudTheme.Teal;
        p.BeginPath();
        for (int i = 0; i < 5; i++)
        {
            float outerA = -Mathf.PI * 0.5f + i * Mathf.PI * 0.4f;
            float innerA = outerA + Mathf.PI * 0.2f;
            Vector2 o = c + new Vector2(Mathf.Cos(outerA), Mathf.Sin(outerA)) * outer;
            Vector2 n = c + new Vector2(Mathf.Cos(innerA), Mathf.Sin(innerA)) * inner;
            if (i == 0)
                p.MoveTo(o);
            else
                p.LineTo(o);
            p.LineTo(n);
        }
        p.ClosePath();
        p.Fill();
    }

    public static void PaintTrophy(MeshGenerationContext ctx)
    {
        var r = ctx.visualElement.contentRect;
        var p = ctx.painter2D;
        Vector2 c = r.center;
        float s = Mathf.Min(r.width, r.height);

        p.fillColor = HudTheme.Gold;
        p.BeginPath();
        p.MoveTo(new Vector2(c.x - s * 0.18f, c.y - s * 0.22f));
        p.LineTo(new Vector2(c.x + s * 0.18f, c.y - s * 0.22f));
        p.LineTo(new Vector2(c.x + s * 0.12f, c.y + s * 0.08f));
        p.LineTo(new Vector2(c.x - s * 0.12f, c.y + s * 0.08f));
        p.ClosePath();
        p.Fill();

        p.BeginPath();
        p.MoveTo(new Vector2(c.x - s * 0.08f, c.y + s * 0.08f));
        p.LineTo(new Vector2(c.x + s * 0.08f, c.y + s * 0.08f));
        p.LineTo(new Vector2(c.x + s * 0.06f, c.y + s * 0.18f));
        p.LineTo(new Vector2(c.x - s * 0.06f, c.y + s * 0.18f));
        p.ClosePath();
        p.Fill();

        p.BeginPath();
        p.MoveTo(new Vector2(c.x - s * 0.16f, c.y + s * 0.18f));
        p.LineTo(new Vector2(c.x + s * 0.16f, c.y + s * 0.18f));
        p.LineTo(new Vector2(c.x + s * 0.16f, c.y + s * 0.26f));
        p.LineTo(new Vector2(c.x - s * 0.16f, c.y + s * 0.26f));
        p.ClosePath();
        p.Fill();

        p.strokeColor = HudTheme.Gold;
        p.lineWidth = 2.2f;
        p.lineCap = LineCap.Round;
        p.BeginPath();
        p.MoveTo(new Vector2(c.x + s * 0.18f, c.y - s * 0.18f));
        p.BezierCurveTo(
            new Vector2(c.x + s * 0.34f, c.y - s * 0.18f),
            new Vector2(c.x + s * 0.34f, c.y + s * 0.02f),
            new Vector2(c.x + s * 0.12f, c.y + s * 0.02f));
        p.Stroke();
        p.BeginPath();
        p.MoveTo(new Vector2(c.x - s * 0.18f, c.y - s * 0.18f));
        p.BezierCurveTo(
            new Vector2(c.x - s * 0.34f, c.y - s * 0.18f),
            new Vector2(c.x - s * 0.34f, c.y + s * 0.02f),
            new Vector2(c.x - s * 0.12f, c.y + s * 0.02f));
        p.Stroke();
    }

    public static void PaintChevronDown(MeshGenerationContext ctx)
    {
        var r = ctx.visualElement.contentRect;
        var p = ctx.painter2D;
        Vector2 c = r.center;
        float s = Mathf.Min(r.width, r.height);
        float inset = s * 0.22f;

        p.fillColor = HudTheme.Ink;
        p.BeginPath();
        p.MoveTo(new Vector2(c.x - s * 0.5f + inset, c.y - s * 0.12f));
        p.LineTo(new Vector2(c.x + s * 0.5f - inset, c.y - s * 0.12f));
        p.LineTo(new Vector2(c.x, c.y + s * 0.38f));
        p.ClosePath();
        p.Fill();
    }

    public static void PaintLock(MeshGenerationContext ctx)
    {
        var r = ctx.visualElement.contentRect;
        var p = ctx.painter2D;
        Vector2 c = r.center;
        float s = Mathf.Min(r.width, r.height);

        p.strokeColor = HudTheme.Muted;
        p.lineWidth = Mathf.Max(1.6f, s * 0.12f);
        p.lineCap = LineCap.Round;
        p.BeginPath();
        p.Arc(new Vector2(c.x, c.y - s * 0.04f), s * 0.2f, 200f, 340f);
        p.Stroke();

        p.fillColor = HudTheme.Muted;
        p.BeginPath();
        p.MoveTo(new Vector2(c.x - s * 0.26f, c.y - s * 0.04f));
        p.LineTo(new Vector2(c.x + s * 0.26f, c.y - s * 0.04f));
        p.LineTo(new Vector2(c.x + s * 0.26f, c.y + s * 0.34f));
        p.LineTo(new Vector2(c.x - s * 0.26f, c.y + s * 0.34f));
        p.ClosePath();
        p.Fill();
    }

    public static void PaintWeather(MeshGenerationContext ctx, WeatherKind weather)
    {
        var r = ctx.visualElement.contentRect;
        var p = ctx.painter2D;
        Vector2 c = r.center;
        float s = Mathf.Min(r.width, r.height);

        if (weather == WeatherKind.Sunny)
        {
            p.fillColor = HudTheme.Gold;
            p.BeginPath();
            p.Arc(c, s * 0.22f, 0f, 360f);
            p.Fill();

            p.strokeColor = HudTheme.Gold;
            p.lineWidth = 2.4f;
            p.lineCap = LineCap.Round;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                p.BeginPath();
                p.MoveTo(c + dir * s * 0.32f);
                p.LineTo(c + dir * s * 0.44f);
                p.Stroke();
            }
            return;
        }

        if (weather == WeatherKind.PartlyCloudy)
        {
            p.fillColor = HudTheme.Gold;
            p.BeginPath();
            p.Arc(new Vector2(c.x + s * 0.12f, c.y - s * 0.08f), s * 0.16f, 0f, 360f);
            p.Fill();
        }

        PaintCloud(p, new Vector2(c.x, c.y + s * 0.04f), s);

        if (weather != WeatherKind.Rain)
            return;

        p.strokeColor = HudTheme.WaterDeep;
        p.lineWidth = 2f;
        p.lineCap = LineCap.Round;
        for (int i = 0; i < 3; i++)
        {
            float x = c.x + (i - 1) * s * 0.12f;
            p.BeginPath();
            p.MoveTo(new Vector2(x, c.y + s * 0.18f));
            p.LineTo(new Vector2(x - s * 0.04f, c.y + s * 0.34f));
            p.Stroke();
        }
    }

    static void PaintCloud(Painter2D p, Vector2 c, float s)
    {
        p.fillColor = Color.white;
        p.BeginPath();
        p.Arc(new Vector2(c.x - s * 0.12f, c.y), s * 0.16f, 0f, 360f);
        p.Fill();
        p.BeginPath();
        p.Arc(new Vector2(c.x + s * 0.1f, c.y - s * 0.02f), s * 0.18f, 0f, 360f);
        p.Fill();
        p.BeginPath();
        p.Arc(new Vector2(c.x + s * 0.22f, c.y + s * 0.04f), s * 0.13f, 0f, 360f);
        p.Fill();
        p.BeginPath();
        p.MoveTo(new Vector2(c.x - s * 0.28f, c.y + s * 0.04f));
        p.LineTo(new Vector2(c.x + s * 0.32f, c.y + s * 0.04f));
        p.LineTo(new Vector2(c.x + s * 0.32f, c.y + s * 0.16f));
        p.LineTo(new Vector2(c.x - s * 0.28f, c.y + s * 0.16f));
        p.ClosePath();
        p.Fill();
    }
}
