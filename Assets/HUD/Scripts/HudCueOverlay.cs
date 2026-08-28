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
            PlaceAt(chip.Root, panel, true);
            return;
        }

        if (cue.Kind == HudCueKind.Action)
            PlaceScreenAction(chip.Root, actionSlot++);
        else
            PlaceScreenAlert(chip.Root);
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
        root.RegisterCallback<ClickEvent>(OnActionClicked);

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

    static Chip BuildAlert()
    {
        var root = new VisualElement();
        root.AddToClassList("hud-cue");
        root.AddToClassList("hud-cue--alert");
        root.pickingMode = PickingMode.Ignore;

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
        if (cue.Kind == HudCueKind.Action)
            world.y += Mathf.Sin(Time.unscaledTime * 2.6f) * 0.08f;

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

    static void PlaceAt(VisualElement element, Vector2 panel, bool above)
    {
        element.style.left = panel.x;
        element.style.top = panel.y;
        element.style.right = StyleKeyword.Auto;
        element.style.bottom = StyleKeyword.Auto;

        float width = element.resolvedStyle.width;
        float height = element.resolvedStyle.height;
        if (above && width > 1f && height > 1f)
            element.style.translate = new Translate(-width * 0.5f, -height - 16f);
        else if (above)
            element.style.translate = new Translate(Length.Percent(-50), Length.Percent(-100));
        else
            element.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
    }

    static void PlaceScreenAction(VisualElement element, int slot)
    {
        element.style.left = Length.Percent(50);
        element.style.bottom = Length.Percent(22 + slot * 8);
        element.style.top = StyleKeyword.Auto;
        element.style.right = StyleKeyword.Auto;
        element.style.translate = new Translate(Length.Percent(-50), new Length(0));
        element.style.scale = new Scale(Vector2.one);
        element.style.opacity = 1f;
    }

    static void PlaceScreenAlert(VisualElement element)
    {
        element.style.left = Length.Percent(50);
        element.style.top = Length.Percent(28);
        element.style.right = StyleKeyword.Auto;
        element.style.bottom = StyleKeyword.Auto;
        element.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
    }

    sealed class Chip
    {
        public HudCueKind Kind;
        public VisualElement Root;
        public Label Key;
        public Label Label;
        public Label Bang;
    }
}
