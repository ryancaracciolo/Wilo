/// <summary>
/// World systems query this so HUD clicks and open menus don't start a cast
/// or move the boat. GameHud is the only writer.
/// </summary>
public static class HudInput
{
    public static bool PointerOverUi { get; set; }
    public static bool PopupOpen { get; set; }

    public static bool BlocksWorldClick => PointerOverUi || PopupOpen;
}
