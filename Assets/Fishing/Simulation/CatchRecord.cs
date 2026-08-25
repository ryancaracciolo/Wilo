using System;
using UnityEngine;

/// <summary>
/// One landed fish. Marked records are for the lake-map journal; every catch is
/// kept so a history can use the same data.
/// Serialized straight into the save file, so these fields are a save format:
/// keep them flat and plain, and do not rename them casually.
/// </summary>
[Serializable]
public class CatchRecord
{
    public string SpeciesName;
    public float Pounds;
    public float LengthInches;
    public string LureName;
    public Color LureColor;
    public Vector3 WorldPosition;
    public float DepthFeet;

    /// <summary>Calendar day this fish was landed on, so a day can be totalled up.</summary>
    public int DayIndex;
    public float Hour;
    public string TimeLabel;
    public string WeatherLabel;
    public float WaterTempF;
    public string SeasonLabel;
    public bool Marked;
    public bool PersonalBest;
}
