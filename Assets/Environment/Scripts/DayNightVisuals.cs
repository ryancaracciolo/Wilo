using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Per-season colour grade. Tints multiply the shared day/night palette so a
/// season reads as a mood shift rather than a separate set of colours.
/// </summary>
[Serializable]
public class SeasonLook
{
    public string label = "Season";
    [Tooltip("Multiplies sun, ambient, and fog. Keep near white; this is a mood nudge.")]
    public Color lightTint = Color.white;
    [Tooltip("Multiplies the lake's shallow and deep colours.")]
    public Color waterTint = Color.white;
    [Tooltip("Scales fog density. Winter and fall sit hazier than summer.")]
    [Range(0.25f, 4f)] public float fogScale = 1f;
}

/// <summary>
/// Turns the calendar into sun, sky, ambient, fog, and water colour.
/// Reads WorldConditions for hour and season; does not own the clock.
/// </summary>
public class DayNightVisuals : MonoBehaviour
{
    [SerializeField] WorldConditions conditions;
    [SerializeField] Light sun;
    [SerializeField] Transform waterSurface;

    [Header("Sun")]
    [SerializeField] float azimuth = -30f;
    [SerializeField] float maxElevation = 58f;
    [SerializeField] float dayIntensity = 1.15f;
    [SerializeField] Color dayColor = new Color(1f, 0.96f, 0.88f);
    [SerializeField] Color dawnColor = new Color(1f, 0.62f, 0.38f);
    [SerializeField] Color duskColor = new Color(1f, 0.42f, 0.24f);
    [Tooltip("Fraction of the visual day spent easing in and out of full daylight.")]
    [SerializeField, Range(0.05f, 0.4f)] float twilightFraction = 0.2f;
    [Tooltip("Extra hours of light before dawn and after dusk, so 5–7 AM and 6–8 PM stay readable.")]
    [SerializeField, Min(0f)] float visualTwilightHours = 1.25f;
    [Tooltip("How bright the visual day is at its edges. 0 is as dark as night.")]
    [SerializeField, Range(0f, 1f)] float twilightFloor = 0.38f;

    [Header("Moon")]
    [SerializeField] float moonIntensity = 0.12f;
    [SerializeField] float moonElevation = 38f;
    [SerializeField] Color moonColor = new Color(0.55f, 0.64f, 0.85f);

    [Header("Sky")]
    [SerializeField] Color daySkyTint = new Color(0.52f, 0.68f, 0.82f);
    [SerializeField] Color nightSkyTint = new Color(0.08f, 0.1f, 0.18f);
    [SerializeField] Color dayGround = new Color(0.37f, 0.35f, 0.28f);
    [SerializeField] Color nightGround = new Color(0.04f, 0.05f, 0.07f);
    [SerializeField] float dayExposure = 1.25f;
    [SerializeField] float nightExposure = 0.28f;
    [SerializeField] float dayAtmosphere = 1.05f;
    [SerializeField] float nightAtmosphere = 0.55f;

    [Header("Ambient")]
    [SerializeField] Color daySky = new Color(0.55f, 0.72f, 0.85f);
    [SerializeField] Color dayEquator = new Color(0.50f, 0.64f, 0.70f);
    [SerializeField] Color dayGroundAmbient = new Color(0.22f, 0.2f, 0.16f);
    [SerializeField] Color nightSky = new Color(0.06f, 0.08f, 0.14f);
    [SerializeField] Color nightEquator = new Color(0.08f, 0.1f, 0.12f);
    [SerializeField] Color nightGroundAmbient = new Color(0.03f, 0.03f, 0.04f);

    [Header("Fog")]
    [SerializeField] Color dayFog = new Color(0.62f, 0.78f, 0.84f);
    [SerializeField] Color nightFog = new Color(0.08f, 0.11f, 0.16f);
    [SerializeField] float dayFogDensity = 0.00028f;
    [SerializeField] float nightFogDensity = 0.00055f;

    [Header("Water")]
    [Tooltip("How far the lake darkens at night, 0 keeps the daytime colour.")]
    [SerializeField, Range(0f, 1f)] float nightWaterDarken = 0.72f;
    [SerializeField] Color nightWaterTint = new Color(0.32f, 0.42f, 0.6f);

    [Header("Seasons")]
    [SerializeField]
    SeasonLook[] seasons =
    {
        new SeasonLook
        {
            label = "Spring",
            lightTint = new Color(0.98f, 0.99f, 1f),
            waterTint = new Color(0.97f, 0.99f, 1f),
            fogScale = 1.25f
        },
        new SeasonLook
        {
            label = "Summer",
            lightTint = new Color(1f, 0.99f, 0.96f),
            waterTint = new Color(1f, 1f, 1f),
            fogScale = 0.8f
        },
        new SeasonLook
        {
            label = "Fall",
            lightTint = new Color(1f, 0.96f, 0.88f),
            waterTint = new Color(0.98f, 0.97f, 0.96f),
            fogScale = 1.35f
        },
        new SeasonLook
        {
            label = "Winter",
            lightTint = new Color(0.86f, 0.92f, 1f),
            waterTint = new Color(0.88f, 0.94f, 1f),
            fogScale = 1.9f
        }
    };

    static readonly int ShallowId = Shader.PropertyToID("_ShallowColor");
    static readonly int DeepId = Shader.PropertyToID("_DeepColor");

    Material skyRuntime;
    Material skyShared;
    Renderer waterRenderer;
    Material waterRuntime;
    Material waterShared;
    Color waterShallowBase;
    Color waterDeepBase;
    bool hasWaterBase;

    void OnEnable()
    {
        Resolve();
        CaptureSky();
        CaptureWater();
        Apply();
    }

    void OnDisable()
    {
        RestoreSky();
        RestoreWater();
    }

    void LateUpdate()
    {
        Apply();
    }

    void Resolve()
    {
        if (sun == null)
            sun = GetComponent<Light>();
        if (conditions == null)
            conditions = FindFirstObjectByType<WorldConditions>();
        if (waterSurface == null)
        {
            var surface = GameObject.Find("Surface");
            if (surface != null)
                waterSurface = surface.transform;
        }
    }

    void Apply()
    {
        Resolve();
        if (sun == null)
            return;

        float hour = conditions != null ? conditions.Hour : GameCalendar.SolarNoonHour;
        float dawn = conditions != null ? conditions.DawnHour : 6f;
        float dusk = conditions != null ? conditions.DuskHour : 19f;
        float pad = Mathf.Max(0f, visualTwilightHours);
        float visualDawn = dawn - pad;
        float visualDusk = dusk + pad;

        float span = Mathf.Max(0.5f, visualDusk - visualDawn);
        float u = (hour - visualDawn) / span;
        float look = u > 0f && u < 1f ? DayCurve(u) : 0f;
        look = Mathf.Max(look, ShoulderLook(hour));
        SeasonLook season = BlendSeason();

        if (u > 0f && u < 1f)
        {
            float elevation = Mathf.Sin(u * Mathf.PI) * maxElevation;
            sun.transform.rotation = Quaternion.Euler(elevation, azimuth, 0f);
            sun.color = Tint(SunColor(u), season.lightTint);
            sun.intensity = Mathf.Lerp(moonIntensity, dayIntensity, look);
            sun.shadowStrength = Mathf.Lerp(0.2f, 1f, look);
        }
        else if (look > 0.04f)
        {
            bool morning = hour < GameCalendar.SolarNoonHour;
            float elevation = Mathf.Lerp(10f, 22f, look);
            sun.transform.rotation = Quaternion.Euler(elevation, azimuth, 0f);
            sun.color = Tint(morning ? dawnColor : duskColor, season.lightTint);
            sun.intensity = Mathf.Lerp(moonIntensity, dayIntensity, look);
            sun.shadowStrength = Mathf.Lerp(0.2f, 0.7f, look);
        }
        else
        {
            float nightSpan = Mathf.Max(0.5f, 24f - span);
            float nightU = hour < visualDawn
                ? (hour + 24f - visualDusk) / nightSpan
                : (hour - visualDusk) / nightSpan;
            float elevation = Mathf.Sin(Mathf.Clamp01(nightU) * Mathf.PI) * moonElevation;
            sun.transform.rotation = Quaternion.Euler(Mathf.Max(8f, elevation), azimuth + 180f, 0f);
            sun.color = Tint(moonColor, season.lightTint);
            sun.intensity = moonIntensity;
            sun.shadowStrength = 0.2f;
        }

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Grade(nightSky, daySky, look, season.lightTint);
        RenderSettings.ambientEquatorColor = Grade(nightEquator, dayEquator, look, season.lightTint);
        RenderSettings.ambientGroundColor = Grade(nightGroundAmbient, dayGroundAmbient, look, season.lightTint);
        RenderSettings.fogColor = Grade(nightFog, dayFog, look, season.lightTint);
        RenderSettings.fogDensity = Mathf.Lerp(nightFogDensity, dayFogDensity, look) * season.fogScale;

        ApplySky(look, season);
        ApplyWater(look, season);
    }

    /// <summary>Season palette blended so whole seasons hold their own mood and only the seams cross-fade.</summary>
    SeasonLook BlendSeason()
    {
        if (seasons == null || seasons.Length == 0)
            return new SeasonLook();
        if (seasons.Length == 1 || conditions == null)
            return seasons[0];

        float pos = conditions.SeasonBlend;
        int from = Mathf.FloorToInt(pos);
        float f = Smooth(pos - from);
        SeasonLook a = seasons[Mod(from, seasons.Length)];
        SeasonLook b = seasons[Mod(from + 1, seasons.Length)];

        return new SeasonLook
        {
            lightTint = Color.Lerp(a.lightTint, b.lightTint, f),
            waterTint = Color.Lerp(a.waterTint, b.waterTint, f),
            fogScale = Mathf.Lerp(a.fogScale, b.fogScale, f)
        };
    }

    Color SunColor(float u)
    {
        float edge = twilightFraction;
        if (u < edge)
            return Color.Lerp(dawnColor, dayColor, Smooth(u / edge));
        if (u > 1f - edge)
            return Color.Lerp(dayColor, duskColor, Smooth((u - (1f - edge)) / edge));
        return dayColor;
    }

    float DayCurve(float u)
    {
        float edge = twilightFraction;
        float floor = twilightFloor;
        if (u < edge)
            return Mathf.Lerp(floor, 1f, Smooth(u / edge));
        if (u > 1f - edge)
            return Mathf.Lerp(floor, 1f, Smooth((1f - u) / edge));
        return 1f;
    }

    /// <summary>
    /// Soft extra light in the 5–7 AM and 6–8 PM windows so those hours never
    /// read as full night, even when seasonal dawn or dusk sits later.
    /// </summary>
    static float ShoulderLook(float hour)
    {
        float morning = GlowBand(hour, 4.5f, 7.5f, 0.22f, 0.48f);
        float evening = GlowBand(hour, 17.5f, 20.5f, 0.48f, 0.22f);
        return Mathf.Max(morning, evening);
    }

    static float GlowBand(float hour, float from, float to, float fromLook, float toLook)
    {
        if (hour < from || hour > to)
            return 0f;
        return Mathf.Lerp(fromLook, toLook, Smooth(Mathf.InverseLerp(from, to, hour)));
    }

    void CaptureSky()
    {
        if (!Application.isPlaying || skyRuntime != null)
            return;

        skyShared = RenderSettings.skybox;
        if (skyShared == null)
            return;

        skyRuntime = new Material(skyShared) { name = skyShared.name + " (Day Night)" };
        RenderSettings.skybox = skyRuntime;
        DynamicGI.UpdateEnvironment();
    }

    void RestoreSky()
    {
        if (!Application.isPlaying)
            return;
        if (skyShared != null)
            RenderSettings.skybox = skyShared;
        if (skyRuntime != null)
            Destroy(skyRuntime);
        skyRuntime = null;
        skyShared = null;
    }

    void ApplySky(float look, SeasonLook season)
    {
        Material sky = Application.isPlaying ? skyRuntime : null;
        if (sky == null)
            return;

        if (sky.HasProperty("_SkyTint"))
            sky.SetColor("_SkyTint", Grade(nightSkyTint, daySkyTint, look, season.lightTint));
        if (sky.HasProperty("_GroundColor"))
            sky.SetColor("_GroundColor", Grade(nightGround, dayGround, look, season.lightTint));
        if (sky.HasProperty("_Exposure"))
            sky.SetFloat("_Exposure", Mathf.Lerp(nightExposure, dayExposure, look));
        if (sky.HasProperty("_AtmosphereThickness"))
            sky.SetFloat("_AtmosphereThickness", Mathf.Lerp(nightAtmosphere, dayAtmosphere, look));
    }

    void CaptureWater()
    {
        if (!Application.isPlaying || waterRuntime != null || waterSurface == null)
            return;

        waterRenderer = waterSurface.GetComponent<Renderer>();
        waterShared = waterRenderer != null ? waterRenderer.sharedMaterial : null;
        if (waterShared == null || !waterShared.HasProperty(ShallowId))
            return;

        waterShallowBase = waterShared.GetColor(ShallowId);
        waterDeepBase = waterShared.HasProperty(DeepId)
            ? waterShared.GetColor(DeepId)
            : waterShallowBase;
        hasWaterBase = true;

        waterRuntime = new Material(waterShared) { name = waterShared.name + " (Day Night)" };
        waterRenderer.sharedMaterial = waterRuntime;
    }

    void RestoreWater()
    {
        if (!Application.isPlaying)
            return;
        if (waterRenderer != null && waterShared != null)
            waterRenderer.sharedMaterial = waterShared;
        if (waterRuntime != null)
            Destroy(waterRuntime);
        waterRuntime = null;
        waterShared = null;
        hasWaterBase = false;
    }

    void ApplyWater(float look, SeasonLook season)
    {
        if (waterRuntime == null || !hasWaterBase)
            return;

        waterRuntime.SetColor(ShallowId, WaterColor(waterShallowBase, look, season));
        if (waterRuntime.HasProperty(DeepId))
            waterRuntime.SetColor(DeepId, WaterColor(waterDeepBase, look, season));
    }

    /// <summary>Darkens toward moonlit water at night, then applies the seasonal tint. Alpha holds so transparency is unchanged.</summary>
    Color WaterColor(Color day, float look, SeasonLook season)
    {
        Color nightMul = Color.Lerp(Color.white, nightWaterTint, nightWaterDarken);
        Color lit = Color.Lerp(Tint(day, nightMul), day, look);
        Color graded = Tint(lit, season.waterTint);
        graded.a = day.a;
        return graded;
    }

    static Color Grade(Color night, Color day, float look, Color tint)
    {
        return Tint(Color.Lerp(night, day, look), tint);
    }

    static Color Tint(Color c, Color tint)
    {
        return new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a);
    }

    static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    static int Mod(int value, int modulus)
    {
        int r = value % modulus;
        return r < 0 ? r + modulus : r;
    }
}
