using UnityEngine;

public enum LureKind
{
    Worm,
    Spinnerbait,
    Jig
}

[CreateAssetMenu(menuName = "Wilo/Lure", fileName = "Lure")]
public class LureDefinition : ScriptableObject
{
    public string DisplayName = "Lure";
    [TextArea]
    public string Hint = "";
    public Color Color = new Color(0.55f, 0.38f, 0.22f);
    public LureKind Kind = LureKind.Worm;
    [Min(0f)]
    public float SinkSpeed = 0.35f;
}
