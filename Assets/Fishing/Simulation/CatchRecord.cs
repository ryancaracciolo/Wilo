using UnityEngine;

/// <summary>
/// One landed fish. Marked records are for the later lake-map journal;
/// every catch is kept so a history can use the same data.
/// </summary>
public class CatchRecord
{
    public string SpeciesName;
    public float Pounds;
    public float LengthInches;
    public string LureName;
    public Color LureColor;
    public Vector3 WorldPosition;
    public float DepthFeet;
    public float Hour;
    public string TimeLabel;
    public string WeatherLabel;
    public float WaterTempF;
    public string SeasonLabel;
    public bool Marked;
    public bool PersonalBest;
}
