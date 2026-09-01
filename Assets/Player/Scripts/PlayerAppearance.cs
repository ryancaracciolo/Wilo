using UnityEngine;

/// <summary>
/// Tints the chibi at runtime so the intro look is the same one that walks
/// the dock. Vest and hat stay on URP Lit; skin uses a remap shader so
/// clothes and eyes on the atlas do not pick up the body color.
/// </summary>
public class PlayerAppearance : MonoBehaviour
{
    public static readonly Color DefaultSkin = new Color(0.93f, 0.72f, 0.58f, 1f);
    public static readonly Color DefaultHat = Color.white;
    public static readonly Color DefaultVest = new Color(0.322f, 0.373f, 0.235f, 1f);
    public static readonly Color DefaultPockets = new Color(0.639f, 0.545f, 0.373f, 1f);

    static readonly Vector3 LakeHatLocalPosition = new Vector3(0.591f, -0.225f, 0.003f);
    static readonly Vector3 LakeHatLocalScale = new Vector3(0.73100775f, 0.73100775f, 0.73100775f);
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int SkinColorId = Shader.PropertyToID("_SkinColor");

    [SerializeField] Shader skinShader;

    Renderer[] bodyRenderers;
    Renderer vestRenderer;
    Renderer hatRenderer;
    Material skinMaterial;
    Material hatMaterial;
    MaterialPropertyBlock vestShellBlock;
    MaterialPropertyBlock vestPocketBlock;
    MaterialPropertyBlock hatBlock;

    public AppearanceData Current { get; private set; }

    void Awake()
    {
        Current = Defaults();
        vestShellBlock = new MaterialPropertyBlock();
        vestPocketBlock = new MaterialPropertyBlock();
        hatBlock = new MaterialPropertyBlock();
        ResolveRenderers();

        SaveService save = SaveService.Instance;
        if (save != null && save.Player != null && save.Player.appearance != null && save.Player.appearance.HasColors)
            Apply(save.Player.appearance);
    }

    void OnDestroy()
    {
        if (skinMaterial != null)
            Destroy(skinMaterial);
        if (hatMaterial != null)
            Destroy(hatMaterial);
    }

    public static AppearanceData Defaults()
    {
        return new AppearanceData
        {
            skin = DefaultSkin,
            hat = DefaultHat,
            vest = DefaultVest,
            pockets = DefaultPockets
        };
    }

    public static AppearanceData Resolved(AppearanceData raw)
    {
        AppearanceData look = Defaults();
        if (raw == null)
            return look;

        if (raw.skin.a > 0.01f)
            look.skin = raw.skin;
        if (raw.hat.a > 0.01f)
            look.hat = raw.hat;
        if (raw.vest.a > 0.01f)
            look.vest = raw.vest;
        if (raw.pockets.a > 0.01f)
            look.pockets = raw.pockets;
        return look;
    }

    public static AppearanceData FromSave(SaveService save)
    {
        if (save == null || save.Player == null)
            return Defaults();
        return Resolved(save.Player.appearance);
    }

    public void Apply(AppearanceData look)
    {
        look = Resolved(look);
        Current = look;
        if (vestShellBlock == null)
        {
            vestShellBlock = new MaterialPropertyBlock();
            vestPocketBlock = new MaterialPropertyBlock();
            hatBlock = new MaterialPropertyBlock();
        }
        if (bodyRenderers == null)
            ResolveRenderers();

        ApplySkin(look.skin);
        ApplyHat(look.hat);
        ApplyColor(vestRenderer, vestShellBlock, look.vest, 0);
        ApplyColor(vestRenderer, vestPocketBlock, look.pockets, 1);
    }

    public void CaptureTo(PlayerSave save)
    {
        if (save == null)
            return;
        save.appearance = Current;
    }

    void ResolveRenderers()
    {
        var bodies = new System.Collections.Generic.List<Renderer>();
        var renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            string name = renderer.gameObject.name;
            if (name == "FishingVest")
                vestRenderer = renderer;
            else if (name.IndexOf("hat", System.StringComparison.OrdinalIgnoreCase) >= 0)
                hatRenderer = renderer;
            else
                bodies.Add(renderer);
        }

        bodyRenderers = bodies.ToArray();
        ApplyLakeHatPose();
    }

    void ApplyLakeHatPose()
    {
        if (hatRenderer == null)
            return;

        Transform hat = hatRenderer.transform;
        hat.localPosition = LakeHatLocalPosition;
        hat.localScale = LakeHatLocalScale;
    }

    void ApplySkin(Color skin)
    {
        if (bodyRenderers == null || bodyRenderers.Length == 0)
            return;

        Shader shader = skinShader != null ? skinShader : Shader.Find("Wilo/Chibi Skin");
        if (shader == null)
            return;

        if (skinMaterial == null)
        {
            Texture albedo = null;
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                Material shared = bodyRenderers[i].sharedMaterial;
                if (shared != null && shared.HasProperty("_BaseMap"))
                {
                    albedo = shared.GetTexture("_BaseMap");
                    if (albedo != null)
                        break;
                }
            }

            skinMaterial = new Material(shader);
            if (albedo != null)
                skinMaterial.SetTexture("_BaseMap", albedo);
        }

        skinMaterial.SetColor(SkinColorId, skin);
        if (skinMaterial.HasProperty("_HueRange"))
            skinMaterial.SetFloat("_HueRange", 0.18f);
        for (int i = 0; i < bodyRenderers.Length; i++)
            bodyRenderers[i].sharedMaterial = skinMaterial;
    }

    void ApplyHat(Color color)
    {
        if (hatRenderer == null)
            return;

        if (hatMaterial == null)
        {
            Material source = hatRenderer.sharedMaterial;
            hatMaterial = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            hatMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
            hatRenderer.sharedMaterial = hatMaterial;
        }

        hatMaterial.SetColor(BaseColorId, color);
        if (hatMaterial.HasProperty("_Color"))
            hatMaterial.SetColor("_Color", color);
    }

    static void ApplyColor(Renderer renderer, MaterialPropertyBlock block, Color color, int materialIndex)
    {
        if (renderer == null)
            return;

        renderer.GetPropertyBlock(block, materialIndex);
        block.SetColor(BaseColorId, color);
        renderer.SetPropertyBlock(block, materialIndex);
    }
}
