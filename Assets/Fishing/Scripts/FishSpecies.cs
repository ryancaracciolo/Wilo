using UnityEngine;

[CreateAssetMenu(menuName = "Wilo/Fish Species", fileName = "Species")]
public class FishSpecies : ScriptableObject
{
    [Tooltip("Stable key. Save data refers to this, so do not rename it casually.")]
    public string Id = "";

    public string DisplayName = "Bass";
    public GameObject Prefab;
    public float TypicalPounds = 2.5f;
    public float TypicalLengthInches = 16f;
    [Tooltip("World length of the prefab at its saved scale. Used to scale individuals.")]
    public float PrefabLengthInches = 19.5f;

    public float VisualScale(FishSize size, float readability)
    {
        float authored = Mathf.Max(1f, PrefabLengthInches);
        return (size.LengthInches / authored) * Mathf.Max(1f, readability);
    }
}
