using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// First-run porch: lake, name, and look. Writes the player document, then
/// opens the lake. The 3D angler on the right is a stripped player prefab
/// so swatches match what will stand on the dock.
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(UIDocument))]
public class IntroHud : MonoBehaviour
{
    static readonly Color[] SkinSwatches =
    {
        new Color(0.98f, 0.88f, 0.80f),
        new Color(0.93f, 0.72f, 0.58f),
        new Color(0.90f, 0.68f, 0.50f),
        new Color(0.84f, 0.62f, 0.42f),
        new Color(0.72f, 0.50f, 0.34f),
        new Color(0.62f, 0.42f, 0.28f),
        new Color(0.50f, 0.32f, 0.22f),
        new Color(0.38f, 0.24f, 0.16f),
        new Color(0.26f, 0.16f, 0.12f),
        new Color(0.92f, 0.62f, 0.58f)
    };

    static readonly Color[] HatSwatches =
    {
        Color.white,
        new Color(0.93f, 0.80f, 0.46f),
        new Color(0.82f, 0.48f, 0.22f),
        new Color(0.78f, 0.28f, 0.24f),
        new Color(0.18f, 0.28f, 0.42f),
        new Color(0.31f, 0.61f, 0.58f),
        new Color(0.32f, 0.46f, 0.28f),
        new Color(0.94f, 0.88f, 0.74f),
        new Color(0.28f, 0.26f, 0.24f),
        new Color(0.90f, 0.62f, 0.70f)
    };

    static readonly Color[] VestSwatches =
    {
        new Color(0.322f, 0.373f, 0.235f),
        new Color(0.22f, 0.40f, 0.32f),
        new Color(0.18f, 0.42f, 0.45f),
        new Color(0.20f, 0.28f, 0.42f),
        new Color(0.62f, 0.28f, 0.22f),
        new Color(0.72f, 0.62f, 0.38f),
        new Color(0.82f, 0.46f, 0.22f),
        new Color(0.42f, 0.44f, 0.46f),
        new Color(0.48f, 0.22f, 0.28f),
        new Color(0.30f, 0.42f, 0.58f)
    };

    static readonly Color[] PocketSwatches =
    {
        new Color(0.639f, 0.545f, 0.373f),
        new Color(0.92f, 0.86f, 0.70f),
        new Color(0.42f, 0.28f, 0.18f),
        new Color(0.322f, 0.373f, 0.235f),
        new Color(0.72f, 0.38f, 0.22f),
        new Color(0.86f, 0.70f, 0.32f),
        new Color(0.28f, 0.26f, 0.24f),
        new Color(0.96f, 0.94f, 0.90f)
    };

    [SerializeField] StyleSheet styleSheet;
    [SerializeField] GameObject playerPrefab;

    UIDocument document;
    VisualElement root;
    VisualElement card;
    TextField nameField;
    Label nameError;
    Label comingSoon;
    VisualElement lakeRow;
    VisualElement skinRow;
    VisualElement hatRow;
    VisualElement vestRow;
    VisualElement pocketRow;
    VisualElement cardBody;
    bool creating;

    AppearanceData draft;
    PlayerAppearance previewLook;
    Transform previewRoot;
    GameObject porch;
    float previewYaw = 18f;
    static readonly Vector3 PreviewStand = new Vector3(0.85f, 0f, 0f);
    static readonly Vector3 CameraPos = new Vector3(-0.55f, 1.50f, 4.10f);
    static readonly Vector3 CameraAim = new Vector3(0.85f, 0.72f, 0f);
    bool dragging;
    float lastDragX;

    void Awake()
    {
        document = GetComponent<UIDocument>();
        creating = SaveService.Instance == null || SaveService.Instance.Slots.Count == 0;
        draft = creating
            ? PlayerAppearance.Defaults()
            : AppearanceOf(LastSlot());
    }

    void OnEnable()
    {
        Build();
        SpawnPreview();
    }

    void OnDisable()
    {
        HudInput.Reset();
        if (previewRoot != null)
            Destroy(previewRoot.gameObject);
        if (porch != null)
            Destroy(porch);
        previewRoot = null;
        previewLook = null;
        porch = null;
    }

    void Update()
    {
        HudInput.Tick();
        SpinPreview();
    }

    void Build()
    {
        if (document == null)
            document = GetComponent<UIDocument>();
        root = document.rootVisualElement;
        if (root == null)
            return;

        root.Clear();
        root.AddToClassList("hud-root");
        root.AddToClassList("hud-intro");
        root.pickingMode = PickingMode.Ignore;
        HudInput.Root = root;
        HudInput.Panel = root.panel;
        if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
            root.styleSheets.Add(styleSheet);

        card = new VisualElement();
        card.AddToClassList("hud-card");
        card.AddToClassList("hud-intro-card");
        cardBody = new ScrollView(ScrollViewMode.Vertical);
        cardBody.AddToClassList("hud-intro-scroll");
        card.Add(cardBody);
        FillCard();

        var stage = new VisualElement();
        stage.AddToClassList("hud-intro-stage");
        stage.pickingMode = PickingMode.Ignore;
        Label hint = HudUi.Muted("Drag to turn");
        hint.AddToClassList("hud-intro-hint");
        stage.Add(hint);

        root.Add(card);
        root.Add(stage);
        card.RegisterCallback<GeometryChangedEvent>(_ => FrameCamera());

        if (creating)
        {
            RefreshChoices();
            RefreshSwatches();
        }
    }

    void FillCard()
    {
        if (cardBody == null)
            return;

        cardBody.Clear();
        if (creating)
            FillCreateCard();
        else
            FillHomeCard();
    }

    void FillHomeCard()
    {
        Label title = HudUi.Title("Welcome to Willow Lake");
        title.AddToClassList("hud-intro-title");
        cardBody.Add(title);
        cardBody.Add(HudUi.Muted("Pick up where you left off, or start a new lake."));

        var saved = new VisualElement();
        saved.AddToClassList("hud-section");
        saved.Add(HudUi.Muted("Your lakes"));

        List<LakeSlot> slots = SortedSlots();
        for (int i = 0; i < slots.Count; i++)
        {
            LakeSlot slot = slots[i];
            if (slot == null || string.IsNullOrEmpty(slot.id))
                continue;
            saved.Add(SlotRow(slot));
        }

        cardBody.Add(saved);

        var actions = new VisualElement();
        actions.AddToClassList("hud-form-actions");
        actions.Add(HudUi.TextButton("New lake", OpenCreate, true));
        cardBody.Add(actions);
    }

    void FillCreateCard()
    {
        Label title = HudUi.Title("Welcome to Willow Lake");
        title.AddToClassList("hud-intro-title");
        cardBody.Add(title);
        cardBody.Add(HudUi.Muted("Pick your lake, sign your name, and dress for the water."));

        var lake = new VisualElement();
        lake.AddToClassList("hud-section");
        lake.Add(HudUi.Muted("Lake"));
        lakeRow = HudUi.Row();
        lakeRow.AddToClassList("hud-choice-row");
        lake.Add(lakeRow);
        comingSoon = HudUi.Muted("Coming soon!");
        comingSoon.AddToClassList("hud-intro-soon");
        comingSoon.style.display = DisplayStyle.None;
        lake.Add(comingSoon);
        cardBody.Add(lake);

        var name = new VisualElement();
        name.AddToClassList("hud-section");
        name.Add(HudUi.Muted("Your name"));
        nameError = HudUi.Muted("The lake should know what to call you.");
        nameError.style.display = DisplayStyle.None;
        nameField = HudUi.NameField("", PlayerProgress.MaxNameLength, TryCreate, autoFocus: false);
        name.Add(nameField);
        name.Add(nameError);
        cardBody.Add(name);

        var look = new VisualElement();
        look.AddToClassList("hud-section");
        look.Add(HudUi.Muted("Your look"));
        look.Add(SwatchBlock("Skin", out skinRow));
        look.Add(SwatchBlock("Hat", out hatRow));
        look.Add(SwatchBlock("Vest", out vestRow));
        look.Add(SwatchBlock("Pockets", out pocketRow));
        cardBody.Add(look);

        var actions = new VisualElement();
        actions.AddToClassList("hud-form-actions");
        if (SaveService.Instance != null && SaveService.Instance.Slots.Count > 0)
            actions.Add(HudUi.TextButton("Back", OpenHome));
        actions.Add(HudUi.TextButton("Head to the lake", TryCreate, true));
        cardBody.Add(actions);
    }

    VisualElement SlotRow(LakeSlot slot)
    {
        var row = new VisualElement();
        row.AddToClassList("hud-lake-slot");

        var text = new VisualElement();
        text.AddToClassList("hud-lake-slot-text");
        string angler = string.IsNullOrWhiteSpace(slot.displayName) ? "You" : slot.displayName;
        Label name = HudUi.Title(angler);
        name.AddToClassList("hud-lake-slot-name");
        text.Add(name);
        text.Add(HudUi.Muted($"{LakeChoice.DisplayName(slot.lakeKey)}  ·  Day {Mathf.Max(1, slot.dayIndex + 1)}"));
        row.Add(text);

        row.Add(HudUi.TextButton("Continue", () => ContinueSlot(slot), true));
        row.RegisterCallback<ClickEvent>(_ => PreviewSlot(slot));
        return row;
    }

    static VisualElement SwatchBlock(string label, out VisualElement row)
    {
        var block = new VisualElement();
        block.AddToClassList("hud-swatch-block");
        Label caption = HudUi.Muted(label);
        caption.AddToClassList("hud-swatch-label");
        block.Add(caption);
        row = HudUi.Row();
        row.AddToClassList("hud-swatch-row");
        block.Add(row);
        return block;
    }

    void RefreshChoices()
    {
        lakeRow.Clear();
        string selected = SelectedLake();
        lakeRow.Add(HudUi.Choice("Willow Lake", SelectWillow, selected == LakeChoice.Willow));
        lakeRow.Add(HudUi.Choice("Custom Lake", ShowComingSoon, false, locked: true));
    }

    void RefreshSwatches()
    {
        FillSwatches(skinRow, SkinSwatches, draft.skin, color =>
        {
            draft.skin = color;
            previewLook?.Apply(draft);
            RefreshSwatches();
        });
        FillSwatches(hatRow, HatSwatches, draft.hat, color =>
        {
            draft.hat = color;
            previewLook?.Apply(draft);
            RefreshSwatches();
        });
        FillSwatches(vestRow, VestSwatches, draft.vest, color =>
        {
            draft.vest = color;
            previewLook?.Apply(draft);
            RefreshSwatches();
        });
        FillSwatches(pocketRow, PocketSwatches, draft.pockets, color =>
        {
            draft.pockets = color;
            previewLook?.Apply(draft);
            RefreshSwatches();
        });
    }

    static void FillSwatches(VisualElement row, Color[] colors, Color current, System.Action<Color> pick)
    {
        row.Clear();
        for (int i = 0; i < colors.Length; i++)
        {
            Color color = colors[i];
            bool on = Nearly(color, current);
            row.Add(HudUi.Swatch(color, on, () => pick(color)));
        }
    }

    void SelectWillow()
    {
        comingSoon.style.display = DisplayStyle.None;
        if (SaveService.Instance != null)
            SaveService.Instance.Player.selectedLake = LakeChoice.Willow;
        RefreshChoices();
    }

    void ShowComingSoon()
    {
        comingSoon.style.display = DisplayStyle.Flex;
    }

    void OpenHome()
    {
        creating = false;
        draft = AppearanceOf(LastSlot());
        previewLook?.Apply(draft);
        FillCard();
        card.schedule.Execute(FrameCamera);
    }

    void OpenCreate()
    {
        creating = true;
        draft = PlayerAppearance.Defaults();
        previewLook?.Apply(draft);
        FillCard();
        RefreshChoices();
        RefreshSwatches();
        card.schedule.Execute(FrameCamera);
    }

    void PreviewSlot(LakeSlot slot)
    {
        draft = AppearanceOf(slot);
        previewLook?.Apply(draft);
    }

    void ContinueSlot(LakeSlot slot)
    {
        SaveService save = SaveService.Instance;
        if (save == null || slot == null || !save.OpenSlot(slot.id))
            return;

        GameFlow.ContinueToLake();
    }

    void TryCreate()
    {
        SaveService save = SaveService.Instance;
        if (save == null)
            return;

        string clean = nameField != null ? nameField.value : "";
        clean = string.IsNullOrEmpty(clean) ? "" : clean.Trim();
        if (clean.Length > PlayerProgress.MaxNameLength)
            clean = clean.Substring(0, PlayerProgress.MaxNameLength).TrimEnd();
        if (clean.Length == 0)
        {
            nameError.style.display = DisplayStyle.Flex;
            nameField?.Focus();
            return;
        }

        save.BeginNewSlot();
        save.Player.displayName = clean;
        save.Player.selectedLake = LakeChoice.Willow;
        save.Player.introComplete = true;
        save.Player.appearance = PlayerAppearance.Resolved(draft);
        save.Save();
        save.ActivateSession();
        GameFlow.ContinueToLake();
    }

    void SpawnPreview()
    {
        if (playerPrefab == null)
            return;

        var leftover = GameObject.Find("PreviewAngler");
        if (leftover != null)
            Destroy(leftover);

        var instance = Instantiate(playerPrefab);
        instance.name = "PreviewAngler";
        instance.tag = "Untagged";
        instance.transform.SetPositionAndRotation(PreviewStand, Quaternion.Euler(0f, previewYaw, 0f));
        StripGameplay(instance);

        previewRoot = instance.transform;
        previewLook = instance.GetComponent<PlayerAppearance>();
        if (previewLook == null)
            previewLook = instance.AddComponent<PlayerAppearance>();
        previewLook.Apply(draft);

        BuildPorch();
        FrameCamera();
    }

    void FrameCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.1f;

        Vector3 look = CameraAim;
        float leftFrac = CardRightAsScreenFraction();
        if (leftFrac > 0.05f && leftFrac < 0.85f)
        {
            float paneCenter = leftFrac + (1f - leftFrac) * 0.5f;
            float shift = paneCenter - 0.5f;
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            float vFov = cam.fieldOfView * Mathf.Deg2Rad;
            float hFov = 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f) * aspect);
            Vector3 toSubject = CameraAim - CameraPos;
            float dist = toSubject.magnitude;
            Vector3 forward = toSubject / Mathf.Max(dist, 0.01f);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            look = CameraAim - right * (Mathf.Tan(shift * hFov) * dist);
        }

        cam.transform.SetPositionAndRotation(CameraPos, Quaternion.LookRotation(look - CameraPos));
    }

    float CardRightAsScreenFraction()
    {
        if (card == null || root == null)
            return 0.28f;

        float width = root.resolvedStyle.width;
        if (width < 1f)
            width = Screen.width;
        float right = card.layout.x + card.layout.width;
        if (right < 1f)
            return 0.28f;
        return Mathf.Clamp01(right / width);
    }

    static void StripGameplay(GameObject instance)
    {
        var controller = instance.GetComponent<CharacterController>();
        if (controller != null)
            controller.enabled = false;

        var behaviours = instance.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null || behaviour is PlayerAppearance)
                continue;
            behaviour.enabled = false;
        }

        var input = instance.GetComponent<UnityEngine.InputSystem.PlayerInput>();
        if (input != null)
            input.enabled = false;
    }

    void BuildPorch()
    {
        if (porch != null)
            Destroy(porch);

        porch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        porch.name = "IntroPorch";
        porch.transform.SetPositionAndRotation(new Vector3(0.85f, -0.04f, 0f), Quaternion.identity);
        porch.transform.localScale = new Vector3(3.4f, 0.04f, 3.4f);
        var renderer = porch.GetComponent<Renderer>();
        if (renderer != null)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", new Color(0.78f, 0.70f, 0.50f));
            material.SetFloat("_Smoothness", 0.12f);
            renderer.sharedMaterial = material;
        }

        var collider = porch.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
    }

    void SpinPreview()
    {
        if (previewRoot == null || Mouse.current == null)
            return;

        bool overUi = HudInput.IsPointerOverUi();
        bool held = Mouse.current.leftButton.isPressed;
        float x = Mouse.current.position.ReadValue().x;

        if (held && !overUi)
        {
            if (dragging)
                previewYaw += (x - lastDragX) * 0.28f;
            dragging = true;
            lastDragX = x;
        }
        else
        {
            dragging = false;
            previewYaw += Time.deltaTime * 12f;
        }

        previewRoot.rotation = Quaternion.Euler(0f, previewYaw, 0f);
    }

    string SelectedLake()
    {
        return LakeChoice.Willow;
    }

    static LakeSlot LastSlot()
    {
        SaveService save = SaveService.Instance;
        if (save == null || save.Slots.Count == 0)
            return null;

        LakeSlot best = save.Slots[0];
        for (int i = 1; i < save.Slots.Count; i++)
        {
            if (save.Slots[i] != null && (best == null || save.Slots[i].lastPlayed > best.lastPlayed))
                best = save.Slots[i];
        }

        return best;
    }

    static List<LakeSlot> SortedSlots()
    {
        var list = new List<LakeSlot>();
        SaveService save = SaveService.Instance;
        if (save == null)
            return list;

        for (int i = 0; i < save.Slots.Count; i++)
        {
            if (save.Slots[i] != null)
                list.Add(save.Slots[i]);
        }

        list.Sort((a, b) => b.lastPlayed.CompareTo(a.lastPlayed));
        return list;
    }

    static AppearanceData AppearanceOf(LakeSlot slot)
    {
        return PlayerAppearance.Resolved(slot != null ? slot.appearance : null);
    }

    static bool Nearly(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.02f
            && Mathf.Abs(a.g - b.g) < 0.02f
            && Mathf.Abs(a.b - b.b) < 0.02f;
    }
}
