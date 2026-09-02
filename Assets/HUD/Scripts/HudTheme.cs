using UnityEngine;

/// <summary>
/// Shared palette for the cozy HUD. Visual sizes live in Hud.uss;
/// keep gameplay colors here so map/sonar painters stay in sync.
/// </summary>
public static class HudTheme
{
    public static readonly Color Panel = new Color(1f, 0.973f, 0.925f);
    public static readonly Color Ink = new Color(0.3f, 0.24f, 0.18f);
    public static readonly Color Muted = new Color(0.54f, 0.45f, 0.36f);
    public static readonly Color Teal = new Color(0.31f, 0.61f, 0.58f);
    public static readonly Color TealDeep = new Color(0.18f, 0.42f, 0.45f);
    public static readonly Color WaterShallow = new Color(0.55f, 0.82f, 0.84f);
    public static readonly Color WaterMid = new Color(0.36f, 0.69f, 0.74f);
    public static readonly Color WaterDeep = new Color(0.22f, 0.48f, 0.58f);
    public static readonly Color Grass = new Color(0.55f, 0.76f, 0.45f);
    public static readonly Color Forest = new Color(0.38f, 0.58f, 0.34f);
    public static readonly Color Sand = new Color(0.91f, 0.84f, 0.66f);
    public static readonly Color Gold = new Color(0.94f, 0.76f, 0.29f);
    public static readonly Color PlayerPin = new Color(0.93f, 0.38f, 0.32f);
    public static readonly Color SonarSand = new Color(0.93f, 0.82f, 0.55f);
    public static readonly Color SonarRock = new Color(0.62f, 0.54f, 0.43f);
    public static readonly Color SonarWater = new Color(0.12f, 0.32f, 0.36f);
    public static readonly Color FightFill = new Color(0.42f, 0.78f, 0.48f);
    public static readonly Color FightFillHot = new Color(0.55f, 0.86f, 0.4f);
}
