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
