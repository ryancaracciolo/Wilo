/// <summary>
/// The lakes a player can pick at the door. Custom is listed so the porch
/// can say "coming soon" without inventing a second lake scene yet.
/// </summary>
public static class LakeChoice
{
    public const string Willow = "willow";
    public const string Custom = "custom";

    public static string DisplayName(string id)
    {
        if (id == Custom)
            return "Custom Lake";
        return "Willow Lake";
    }
}
