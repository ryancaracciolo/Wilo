using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Draws <see cref="HudCues"/> as floating chips and bangs. Action chips sit
/// above the player; alerts follow their world anchor when one is given.
/// </summary>
public class HudCueOverlay : VisualElement
{
    const float ScreenPad = 56f;
    const float ActionHeightStanding = 2.05f;
    const float ActionHeightSeated = 1.55f;
    const float ActionStackPx = 38f;
    const float ActionBobPx = 6f;
    const float ActionBobSpeed = 2.6f;
    const float AnchorLiftPx = 16f;

    readonly Dictionary<string, Chip> chips = new Dictionary<string, Chip>(8);
    readonly List<string> stale = new List<string>(8);
    Transform player;
    int actionSlot;

    public HudCueOverlay()
    {
        AddToClassList("hud-cues");
        pickingMode = PickingMode.Ignore;
    }

    public void Tick(Camera camera, Transform follow, bool hidden)
    {
        HudCues.Tick();
        player = follow;
        actionSlot = 0;
        IReadOnlyList<HudCue> active = HudCues.Active;

        if (hidden)
        {
            foreach (var pair in chips)
                pair.Value.Root.style.display = DisplayStyle.None;
        }
        else
        {
            for (int i = 0; i < active.Count; i++)
                Present(active[i], camera);
        }

        stale.Clear();
        foreach (var pair in chips)
        {
            bool found = false;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].Id == pair.Key)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                stale.Add(pair.Key);
        }

        for (int i = 0; i < stale.Count; i++)
        {
            if (!chips.TryGetValue(stale[i], out Chip chip))
                continue;
            chip.Root.RemoveFromHierarchy();
            chips.Remove(stale[i]);
        }
    }

    void Present(HudCue cue, Camera camera)
    {
        Chip chip = Ensure(cue);
        chip.Root.style.display = DisplayStyle.Flex;

        if (cue.Kind == HudCueKind.Action)
        {
            if (chip.Key != null)
                chip.Key.text = cue.Key;
            if (chip.Label != null)
                chip.Label.text = cue.Label;
            chip.Root.pickingMode = cue.Activate != null ? PickingMode.Position : PickingMode.Ignore;
        }
        else if (chip.Bang != null)
        {
            chip.Bang.text = string.IsNullOrEmpty(cue.Label) ? "!" : cue.Label;
            AnimateAlert(chip.Root, cue);
        }

        if (TryWorldPanel(camera, cue, out Vector2 panel))
        {
            if (cue.Kind == HudCueKind.Action)
                panel.y -= actionSlot++ * ActionStackPx;
            float bob = cue.Kind == HudCueKind.Action
                ? Mathf.Sin(Time.unscaledTime * ActionBobSpeed) * ActionBobPx
                : 0f;
            PlaceWorld(chip, panel, true, bob);
            return;
        }

        if (cue.Kind == HudCueKind.Action)
            PlaceScreenAction(chip, actionSlot++);
        else
            PlaceScreenAlert(chip);
    }

    Chip Ensure(HudCue cue)
    {
        if (chips.TryGetValue(cue.Id, out Chip chip) && chip.Kind == cue.Kind)
            return chip;

        if (chip != null)
            chip.Root.RemoveFromHierarchy();

        chip = cue.Kind == HudCueKind.Action ? BuildAction(cue.Id) : BuildAlert();
        chips[cue.Id] = chip;
        Add(chip.Root);
        if (cue.Kind == HudCueKind.Alert)
            chip.Root.BringToFront();
        return chip;
    }

    Chip BuildAction(string id)
    {
        var root = new VisualElement();
        root.AddToClassList("hud-cue");
        root.AddToClassList("hud-cue--action");
        root.userData = id;
        root.usageHints = UsageHints.DynamicTransform;
        PinWorldLayout(root);
        root.RegisterCallback<ClickEvent>(OnActionClicked);
        root.RegisterCallback<GeometryChangedEvent>(OnChipGeometry);

        var key = new Label();
        key.AddToClassList("hud-cue-key");
        key.pickingMode = PickingMode.Ignore;
        root.Add(key);

        var label = new Label();
        label.AddToClassList("hud-cue-label");
        label.pickingMode = PickingMode.Ignore;
        root.Add(label);

        return new Chip
        {
            Kind = HudCueKind.Action,
            Root = root,
            Key = key,
            Label = label
        };
    }

    Chip BuildAlert()
    {
        var root = new VisualElement();
        root.AddToClassList("hud-cue");
        root.AddToClassList("hud-cue--alert");
        root.pickingMode = PickingMode.Ignore;
        root.usageHints = UsageHints.DynamicTransform;
        PinWorldLayout(root);
        root.RegisterCallback<GeometryChangedEvent>(OnChipGeometry);

        var bang = new Label("!");
        bang.AddToClassList("hud-cue-bang");
        bang.pickingMode = PickingMode.Ignore;
        root.Add(bang);

        return new Chip
        {
            Kind = HudCueKind.Alert,
            Root = root,
            Bang = bang
        };
    }

    static void PinWorldLayout(VisualElement root)
    {
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = StyleKeyword.Auto;
        root.style.bottom = StyleKeyword.Auto;
        root.style.translate = new Translate(0, 0);
    }

    void OnChipGeometry(GeometryChangedEvent evt)
    {
        if (evt.target is not VisualElement root)
            return;

        foreach (var pair in chips)
        {
            if (pair.Value.Root != root)
                continue;
            pair.Value.Width = evt.newRect.width;
            pair.Value.Height = evt.newRect.height;
            return;
        }
    }

    static void OnActionClicked(ClickEvent evt)
    {
        if (evt.currentTarget is not VisualElement element || element.userData is not string id)
            return;
        evt.StopPropagation();
        HudCues.TryActivate(id);
    }

    static void AnimateAlert(VisualElement root, HudCue cue)
    {
        float age = Mathf.Max(0f, Time.unscaledTime - cue.StartedAt);
        float life = Mathf.Max(0.55f, cue.ExpireAt - cue.StartedAt);

        // Pop, settle, pop again in half a second, then fade.
        const float bounce = 0.5f;
        float scale;
        if (age < 0.12f)
            scale = Mathf.Lerp(0.18f, 1.45f, EaseOut(age / 0.12f));
        else if (age < 0.26f)
            scale = Mathf.Lerp(1.45f, 0.84f, EaseInOut((age - 0.12f) / 0.14f));
        else if (age < bounce)
            scale = Mathf.Lerp(0.84f, 1.22f, EaseOut((age - 0.26f) / 0.24f));
        else
            scale = 1.22f;

        float fade = 1f;
        if (age < 0.05f)
            fade = Mathf.Clamp01(age / 0.05f);
        else if (age > bounce)
            fade = 1f - Mathf.InverseLerp(bounce, life, age);

        root.style.scale = new Scale(new Vector2(scale, scale));
        root.style.opacity = fade;
    }

    static float EaseOut(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - (1f - t) * (1f - t);
    }

    static float EaseInOut(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    bool TryWorldPanel(Camera camera, HudCue cue, out Vector2 panel)
    {
        panel = default;
        if (camera == null || this.panel == null)
            return false;

        Transform anchor;
        Vector3 offset;
        if (cue.Kind == HudCueKind.Action)
        {
            if (player == null)
                return false;
            anchor = player;
            offset = Vector3.up * (player.parent != null ? ActionHeightSeated : ActionHeightStanding);
        }
        else
        {
            if (cue.Anchor == null)
                return false;
            anchor = cue.Anchor;
            offset = cue.WorldOffset;
        }

        Vector3 world = anchor.position + offset;
        Vector3 screen = camera.WorldToScreenPoint(world);
        if (screen.z <= 0.05f)
            return false;

        panel = RuntimePanelUtils.CameraTransformWorldToPanel(this.panel, world, camera);

        Rect rect = contentRect;
        if (rect.width > 8f && rect.height > 8f)
        {
            panel.x = Mathf.Clamp(panel.x, ScreenPad, rect.width - ScreenPad);
            panel.y = Mathf.Clamp(panel.y, ScreenPad, rect.height - ScreenPad);
        }

        return true;
    }

    static void PlaceWorld(Chip chip, Vector2 panel, bool above, float bobPx)
    {
        if (!chip.WorldPinned)
        {
            PinWorldLayout(chip.Root);
            chip.WorldPinned = true;
        }

        float width = chip.Width > 1f ? chip.Width : chip.Root.resolvedStyle.width;
        float height = chip.Height > 1f ? chip.Height : chip.Root.resolvedStyle.height;
        if (width > 1f)
            chip.Width = width;
        if (height > 1f)
            chip.Height = height;

        float ox = width > 1f ? -width * 0.5f : 0f;
        float oy = height > 1f
            ? (above ? -height - AnchorLiftPx : -height * 0.5f)
            : 0f;

        chip.Root.transform.position = new Vector3(panel.x + ox, panel.y + oy - bobPx, 0f);
    }

    static void PlaceScreenAction(Chip chip, int slot)
    {
        chip.WorldPinned = false;
        chip.Root.transform.position = Vector3.zero;
        chip.Root.style.left = Length.Percent(50);
        chip.Root.style.bottom = Length.Percent(22 + slot * 8);
        chip.Root.style.top = StyleKeyword.Auto;
        chip.Root.style.right = StyleKeyword.Auto;
        chip.Root.style.translate = new Translate(Length.Percent(-50), new Length(0));
        chip.Root.style.scale = new Scale(Vector2.one);
        chip.Root.style.opacity = 1f;
    }

    static void PlaceScreenAlert(Chip chip)
    {
        chip.WorldPinned = false;
        chip.Root.transform.position = Vector3.zero;
        chip.Root.style.left = Length.Percent(50);
        chip.Root.style.top = Length.Percent(28);
        chip.Root.style.right = StyleKeyword.Auto;
        chip.Root.style.bottom = StyleKeyword.Auto;
        chip.Root.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
    }

    sealed class Chip
    {
        public HudCueKind Kind;
        public VisualElement Root;
        public Label Key;
        public Label Label;
        public Label Bang;
        public float Width;
        public float Height;
        public bool WorldPinned;
    }
}
