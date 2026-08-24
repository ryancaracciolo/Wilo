using UnityEngine;
using UnityEngine.UIElements;

public class ConditionsStrip : VisualElement
{
    readonly VisualElement weatherGlyph;
    readonly Label weather;
    readonly Label temp;
    readonly Label wind;
    readonly Label time;
    readonly Label date;
    readonly Label depth;
    readonly Label speed;
    WeatherKind paintedWeather;

    public ConditionsStrip()
    {
        AddToClassList("hud-strip");

        weatherGlyph = new VisualElement();
        weatherGlyph.AddToClassList("hud-weather-glyph");
        weatherGlyph.pickingMode = PickingMode.Ignore;
        weatherGlyph.generateVisualContent += ctx => HudUi.PaintWeather(ctx, paintedWeather);

        weather = Chip("");
        temp = Chip("");
        wind = Chip("");
        time = Chip("");
        date = Chip("");
        date.AddToClassList("hud-strip-date");
        depth = Chip("");
        speed = Chip("");

        Add(weatherGlyph);
        Add(weather);
        Add(Dot());
        Add(temp);
        Add(Dot());
        Add(wind);
        Add(Dot());
        Add(time);
        Add(date);
        Add(depth);
        Add(speed);
    }

    public void Refresh(WorldConditions conditions)
    {
        if (paintedWeather != conditions.Weather)
        {
            paintedWeather = conditions.Weather;
            weatherGlyph.MarkDirtyRepaint();
        }

        weather.text = conditions.WeatherLabel;
        temp.text = $"{conditions.AirTempF:0}°";
        wind.text = conditions.WindLabel;
        time.text = conditions.TimeLabel;
        date.text = conditions.DateLabel;

        bool showDepth = conditions.OnBoat || conditions.OverWater;
        depth.style.display = showDepth ? DisplayStyle.Flex : DisplayStyle.None;
        depth.style.marginLeft = showDepth ? 12 : 0;
        depth.text = showDepth ? $"{conditions.DepthFeet:0.0} ft" : "";

        speed.style.display = conditions.OnBoat ? DisplayStyle.Flex : DisplayStyle.None;
        speed.style.marginLeft = conditions.OnBoat ? 12 : 0;
        speed.text = conditions.OnBoat ? $"{conditions.BoatSpeedMph:0.0} mph" : "";
    }

    static Label Chip(string text)
    {
        var label = new Label(text);
        label.AddToClassList("hud-strip-label");
        label.pickingMode = PickingMode.Ignore;
        return label;
    }

    static VisualElement Dot()
    {
        var dot = new VisualElement();
        dot.AddToClassList("hud-strip-dot");
        dot.pickingMode = PickingMode.Ignore;
        return dot;
    }
}
