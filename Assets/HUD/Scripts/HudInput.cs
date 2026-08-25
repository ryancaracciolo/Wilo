using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// World systems query this so HUD clicks and open menus don't start a cast
/// or move the boat. Every flag is rebuilt from live mouse state each frame:
/// a missed pointer-up (overlay opening or closing under the cursor) must
/// never leave world input latched off.
/// </summary>
public static class HudInput
{
    public static VisualElement Root { get; set; }
    public static IPanel Panel { get; set; }
    public static bool PopupOpen { get; set; }
    public static bool PointerOverUi { get; private set; }

    static bool pressedOnHud;
    static int hudPressFrame = -1;

    /// <summary>True while a left click that started on the HUD is still owned by the HUD.</summary>
    public static bool AteWorldClick => pressedOnHud || Time.frameCount <= hudPressFrame;

    public static bool BlocksWorldClick => PopupOpen || AteWorldClick || PointerOverUi;

    public static void NotifyUiPointerDown()
    {
        pressedOnHud = true;
        hudPressFrame = Time.frameCount;
        PointerOverUi = true;
    }

    public static void NotifyUiPointerUp()
    {
        EndPress();
    }

    /// <summary>GameHud calls this once a frame before world systems read the flags.</summary>
    public static void Tick()
    {
        if (pressedOnHud && !LeftButtonHeld())
            EndPress();

        PointerOverUi = PickHud();
    }

    public static void Reset()
    {
        PopupOpen = false;
        PointerOverUi = false;
        pressedOnHud = false;
        hudPressFrame = -1;
        Root = null;
        Panel = null;
    }

    public static bool IsPointerOverUi()
    {
        return PointerOverUi || PickHud();
    }

    static void EndPress()
    {
        if (!pressedOnHud)
            return;
        pressedOnHud = false;
        hudPressFrame = Time.frameCount;
    }

    static bool LeftButtonHeld()
    {
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
    }

    static bool PickHud()
    {
        IPanel panel = Panel ?? Root?.panel;
        if (panel == null || Mouse.current == null)
            return false;

        Vector2 pos = RuntimePanelUtils.ScreenToPanel(panel, Mouse.current.position.ReadValue());
        VisualElement hit = panel.Pick(pos);
        return hit != null && hit != Root && hit != panel.visualTree;
    }
}
