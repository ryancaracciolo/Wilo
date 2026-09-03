using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shared lake-ripple impulses. Gameplay emits a kind; the water shader draws
/// expanding rings. Use this for casts, wakes, wading, jumps, and anything
/// else that should disturb the surface.
/// </summary>
[DefaultExecutionOrder(80)]
public class WaterRipples : MonoBehaviour
{
    public const int MaxRipples = 32;
    const int ReservedRipples = 9;

    [SerializeField] WaterRippleProfile castRipple = WaterRippleProfile.Cast;
    [SerializeField] WaterRippleProfile reelRipple = WaterRippleProfile.Reel;
    [SerializeField] WaterRippleProfile boatRipple = WaterRippleProfile.Boat;
    [SerializeField] WaterRippleProfile wadeRipple = WaterRippleProfile.Wade;
    [SerializeField] WaterRippleProfile jumpRipple = WaterRippleProfile.Jump;

    static WaterRipples instance;

    readonly Vector4[] positions = new Vector4[MaxRipples];
    readonly Vector4[] parameters = new Vector4[MaxRipples];
    readonly Vector4[] gpuPositions = new Vector4[MaxRipples];
    readonly Vector4[] gpuParameters = new Vector4[MaxRipples];
    int writeIndex;
    int reservedWrite;
    int lastLive = -1;

    public static WaterRipples Instance
    {
        get
        {
            if (instance == null)
                instance = FindFirstObjectByType<WaterRipples>();
            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        EnsureOnSurface();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureOnSurface();
    }

    static void EnsureOnSurface()
    {
        if (FindFirstObjectByType<WaterRipples>() != null)
            return;

        var surface = GameObject.Find("Surface");
        if (surface != null)
            surface.AddComponent<WaterRipples>();
    }

    void OnEnable()
    {
        instance = this;
        ExpireUnused();
        PushToShader();
    }

    void OnDisable()
    {
        if (instance == this)
            instance = null;
        Shader.SetGlobalFloat("_WiloRippleCount", 0f);
    }

    void LateUpdate()
    {
        PushToShader();
    }

    public static void Emit(Vector3 worldPosition, WaterRippleKind kind, float scale = 1f)
    {
        Emit(worldPosition, kind, scale, false);
    }

    public static void Emit(Vector3 worldPosition, WaterRippleKind kind, float scale, bool reserved)
    {
        WaterRipples system = Instance;
        if (system == null)
            return;

        WaterRippleProfile profile = system.ProfileFor(kind);
        if (Mathf.Abs(scale - 1f) > 0.01f)
            profile = profile.Scaled(scale);
        system.EmitProfile(worldPosition, profile, reserved);
    }

    public static void Emit(Vector3 worldPosition, in WaterRippleProfile profile)
    {
        Instance?.EmitProfile(worldPosition, profile, false);
    }

    WaterRippleProfile ProfileFor(WaterRippleKind kind)
    {
        switch (kind)
        {
            case WaterRippleKind.Reel: return reelRipple;
            case WaterRippleKind.Boat: return boatRipple;
            case WaterRippleKind.Wade: return wadeRipple;
            case WaterRippleKind.Jump: return jumpRipple;
            default: return castRipple;
        }
    }

    void EmitProfile(Vector3 worldPosition, WaterRippleProfile profile, bool reserved)
    {
        int rings = Mathf.Max(1, profile.rings);
        for (int i = 0; i < rings; i++)
            Push(worldPosition, profile, i * Mathf.Max(0f, profile.ringDelay), reserved);
    }

    void Push(Vector3 worldPosition, WaterRippleProfile profile, float ageOffset, bool reserved)
    {
        int index;
        if (reserved)
        {
            index = reservedWrite;
            reservedWrite = (reservedWrite + 1) % ReservedRipples;
        }
        else
        {
            int ambient = MaxRipples - ReservedRipples;
            index = ReservedRipples + writeIndex;
            writeIndex = (writeIndex + 1) % ambient;
        }

        positions[index] = new Vector4(
            worldPosition.x,
            worldPosition.z,
            Time.time + ageOffset,
            Mathf.Max(0.05f, profile.lifetime));
        parameters[index] = new Vector4(
            Mathf.Max(0.05f, profile.speed),
            Mathf.Max(0.05f, profile.width),
            profile.amplitude,
            Mathf.Clamp01(profile.circularity));
    }

    void PushToShader()
    {
        float now = Time.time;
        int live = 0;
        for (int i = 0; i < MaxRipples; i++)
        {
            float age = now - positions[i].z;
            if (age < 0f || age > positions[i].w)
                continue;

            gpuPositions[live] = positions[i];
            gpuParameters[live] = parameters[i];
            live++;
        }

        if (live == 0 && lastLive == 0)
        {
            Shader.SetGlobalFloat("_WiloRippleTime", now);
            return;
        }

        lastLive = live;
        Shader.SetGlobalVectorArray("_WiloRipplePos", gpuPositions);
        Shader.SetGlobalVectorArray("_WiloRippleParams", gpuParameters);
        Shader.SetGlobalFloat("_WiloRippleCount", live);
        Shader.SetGlobalFloat("_WiloRippleTime", now);
    }

    void ExpireUnused()
    {
        float dead = Time.time - 30f;
        for (int i = 0; i < MaxRipples; i++)
        {
            positions[i] = new Vector4(0f, 0f, dead, 0.05f);
            parameters[i] = Vector4.zero;
        }
    }
}

public enum WaterRippleKind
{
    Cast,
    Reel,
    Boat,
    Wade,
    Jump
}

[System.Serializable]
public struct WaterRippleProfile
{
    public float speed;
    public float width;
    public float amplitude;
    public float lifetime;
    public int rings;
    public float ringDelay;
    [Range(0f, 1f)] public float circularity;

    public static WaterRippleProfile Cast => new WaterRippleProfile
    {
        speed = 4.2f,
        width = 0.38f,
        amplitude = 1.05f,
        lifetime = 1.65f,
        rings = 2,
        ringDelay = 0.14f,
        circularity = 0.92f
    };

    public static WaterRippleProfile Reel => new WaterRippleProfile
    {
        speed = 2.5f,
        width = 0.22f,
        amplitude = 0.42f,
        lifetime = 0.85f,
        rings = 1,
        ringDelay = 0f,
        circularity = 0.78f
    };

    public static WaterRippleProfile Boat => new WaterRippleProfile
    {
        speed = 3.8f,
        width = 0.62f,
        amplitude = 1.2f,
        lifetime = 1.55f,
        rings = 1,
        ringDelay = 0f,
        circularity = 0.28f
    };

    public static WaterRippleProfile Wade => new WaterRippleProfile
    {
        speed = 2.2f,
        width = 0.26f,
        amplitude = 0.38f,
        lifetime = 0.9f,
        rings = 1,
        ringDelay = 0f,
        circularity = 0.6f
    };

    public static WaterRippleProfile Jump => new WaterRippleProfile
    {
        speed = 5.2f,
        width = 0.55f,
        amplitude = 1.15f,
        lifetime = 2.1f,
        rings = 3,
        ringDelay = 0.2f,
        circularity = 0.88f
    };

    public WaterRippleProfile Scaled(float scale)
    {
        scale = Mathf.Clamp(scale, 0.4f, 2.5f);
        float t = Mathf.InverseLerp(0.4f, 2.5f, scale);
        rings = Mathf.Max(1, rings + (scale > 1.55f ? 1 : 0));
        speed *= Mathf.Lerp(0.88f, 1.2f, t);
        width *= scale;
        amplitude *= Mathf.Lerp(0.68f, 1.4f, t);
        lifetime *= Mathf.Lerp(0.82f, 1.3f, t);
        return this;
    }
}
