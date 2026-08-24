using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// World systems query this so HUD clicks and open menus don't start a cast
/// or move the boat. GameHud assigns the panel; anyone can pick live.
/// </summary>
public static class HudInput
{
    public static VisualElement Root { get; set; }
    public static IPanel Panel { get; set; }
    public static bool PointerOverUi { get; set; }
    public static bool PopupOpen { get; set; }

    public static bool BlocksWorldClick => PopupOpen || IsPointerOverUi();

    public static bool IsPointerOverUi()
    {
        IPanel panel = Panel ?? Root?.panel;
        if (panel == null || Mouse.current == null)
            return PointerOverUi;

        Vector2 pos = RuntimePanelUtils.ScreenToPanel(panel, Mouse.current.position.ReadValue());
        VisualElement hit = panel.Pick(pos);
        return hit != null && hit != Root && hit != panel.visualTree;
    }
}
