using UnityEngine;

/// <summary>
/// Turns the calendar hour into sun, sky, ambient, and fog.
/// Reads WorldConditions; does not own the clock.
/// </summary>
public class DayNightVisuals : MonoBehaviour
{
    [SerializeField] WorldConditions conditions;
    [SerializeField] Light sun;

    [Header("Sun")]
    [SerializeField] float azimuth = -30f;
    [SerializeField] float maxElevation = 58f;
    [SerializeField] float twilightDegrees = 22f;
    [SerializeField] float dayIntensity = 1.15f;
    [SerializeField] Color dayColor = new Color(1f, 0.96f, 0.88f);
    [SerializeField] Color dawnColor = new Color(1f, 0.62f, 0.38f);
    [SerializeField] Color duskColor = new Color(1f, 0.42f, 0.24f);

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
    [SerializeField] Color dayEquator = new Color(0.48f, 0.58f, 0.48f);
    [SerializeField] Color dayGroundAmbient = new Color(0.22f, 0.2f, 0.16f);
    [SerializeField] Color nightSky = new Color(0.06f, 0.08f, 0.14f);
    [SerializeField] Color nightEquator = new Color(0.08f, 0.1f, 0.12f);
    [SerializeField] Color nightGroundAmbient = new Color(0.03f, 0.03f, 0.04f);

    [Header("Fog")]
    [SerializeField] Color dayFog = new Color(0.62f, 0.78f, 0.84f);
    [SerializeField] Color nightFog = new Color(0.08f, 0.11f, 0.16f);
    [SerializeField] float dayFogDensity = 0.00028f;
    [SerializeField] float nightFogDensity = 0.00055f;

    Material skyRuntime;
    Material skyShared;

    void OnEnable()
    {
        if (sun == null)
            sun = GetComponent<Light>();
        if (conditions == null)
            conditions = FindFirstObjectByType<WorldConditions>();
        CaptureSky();
        Apply();
    }

    void OnDisable()
    {
        RestoreSky();
    }

    void LateUpdate()
    {
        Apply();
    }

    void Apply()
    {
        if (sun == null)
            sun = GetComponent<Light>();
        if (sun == null)
            return;
        if (conditions == null)
            conditions = FindFirstObjectByType<WorldConditions>();

        float hour = conditions != null ? conditions.Hour : 12f;
        Season season = conditions != null ? conditions.Season : Season.Summer;
        DawnDusk(season, out float dawn, out float dusk);

        float span = Mathf.Max(0.5f, dusk - dawn);
        float u = (hour - dawn) / span;
        bool daytime = u > 0f && u < 1f;

        float look;
        if (daytime)
        {
            float elevation = Mathf.Sin(u * Mathf.PI) * maxElevation;
            float sunFade = Mathf.Clamp01(elevation / Mathf.Max(1f, twilightDegrees));
            look = SkyLook(u);
            sun.transform.rotation = Quaternion.Euler(elevation, azimuth, 0f);
            sun.color = SunColor(u);
            sun.intensity = Mathf.Lerp(moonIntensity, dayIntensity, Smooth(sunFade));
            sun.shadowStrength = Mathf.Lerp(0.25f, 1f, look);
        }
        else
        {
            float nightSpan = 24f - span;
            float nightU = hour < dawn
                ? (hour + 24f - dusk) / nightSpan
                : (hour - dusk) / nightSpan;
            float elevation = Mathf.Sin(Mathf.Clamp01(nightU) * Mathf.PI) * moonElevation;
            look = 0f;
            sun.transform.rotation = Quaternion.Euler(Mathf.Max(8f, elevation), azimuth + 180f, 0f);
            sun.color = moonColor;
            sun.intensity = moonIntensity;
            sun.shadowStrength = 0.35f;
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Color.Lerp(nightSky, daySky, look);
        RenderSettings.ambientEquatorColor = Color.Lerp(nightEquator, dayEquator, look);
        RenderSettings.ambientGroundColor = Color.Lerp(nightGroundAmbient, dayGroundAmbient, look);
        RenderSettings.fogColor = Color.Lerp(nightFog, dayFog, look);
        RenderSettings.fogDensity = Mathf.Lerp(nightFogDensity, dayFogDensity, look);
        ApplySky(look);
    }

    Color SunColor(float u)
    {
        if (u < 0.18f)
            return Color.Lerp(dawnColor, dayColor, Smooth(u / 0.18f));
        if (u > 0.82f)
            return Color.Lerp(dayColor, duskColor, Smooth((u - 0.82f) / 0.18f));
        return dayColor;
    }

    void CaptureSky()
    {
        if (!Application.isPlaying)
            return;
        if (skyRuntime != null)
            return;

        skyShared = RenderSettings.skybox;
        if (skyShared == null)
            return;

        skyRuntime = new Material(skyShared);
        skyRuntime.name = skyShared.name + " (Night)";
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

    void ApplySky(float look)
    {
        Material sky = Application.isPlaying ? skyRuntime : RenderSettings.skybox;
        if (sky == null)
            return;

        if (sky.HasProperty("_SkyTint"))
            sky.SetColor("_SkyTint", Color.Lerp(nightSkyTint, daySkyTint, look));
        if (sky.HasProperty("_GroundColor"))
            sky.SetColor("_GroundColor", Color.Lerp(nightGround, dayGround, look));
        if (sky.HasProperty("_Exposure"))
            sky.SetFloat("_Exposure", Mathf.Lerp(nightExposure, dayExposure, look));
        if (sky.HasProperty("_AtmosphereThickness"))
            sky.SetFloat("_AtmosphereThickness", Mathf.Lerp(nightAtmosphere, dayAtmosphere, look));
        if (sky.HasProperty("_SunSize"))
            sky.SetFloat("_SunSize", Mathf.Lerp(0.02f, 0.04f, look));
    }

    static float SkyLook(float u)
    {
        const float edge = 0.2f;
        if (u < edge)
            return Smooth(u / edge);
        if (u > 1f - edge)
            return Smooth((1f - u) / edge);
        return 1f;
    }

    static void DawnDusk(Season season, out float dawn, out float dusk)
    {
        switch (season)
        {
            case Season.Summer:
                dawn = 5f;
                dusk = 20.5f;
                return;
            case Season.Winter:
                dawn = 7.5f;
                dusk = 17f;
                return;
            default:
                dawn = 6f;
                dusk = 19f;
                return;
        }
    }

    static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
