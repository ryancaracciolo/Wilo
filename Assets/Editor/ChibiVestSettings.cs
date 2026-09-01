using System;
using UnityEngine;

/// <summary>
/// Tuning for the chibi's fishing vest. All distances are in the chibi mesh's local
/// space, where the whole character stands 0.475 tall: hips 0.077, shoulders 0.184,
/// head 0.210. Angles run clockwise from the front centre, so 0 is the sternum and
/// 90 is the right flank. Edit in the Inspector and press Rebuild Vest.
/// </summary>
public class ChibiVestSettings : ScriptableObject
{
    [Serializable]
    public struct Pocket
    {
        public string name;

        [Tooltip("Angle from the front centre. Larger swings the pocket toward the flank.")]
        [Range(0f, 90f)] public float centerDeg;

        [Tooltip("Angular half width. Larger makes the pocket wider around the body.")]
        [Range(2f, 45f)] public float halfDeg;

        [Tooltip("Height on the body. The hem and collar bracket the usable range.")]
        [Range(0.05f, 0.20f)] public float centerY;

        [Tooltip("Half height. Larger makes the pocket taller.")]
        [Range(0.002f, 0.05f)] public float halfY;

        [Tooltip("How far the pocket stands off the shell.")]
        [Range(0.001f, 0.03f)] public float lift;

        [Tooltip("How much the outer face is pulled in, which rounds the corners.")]
        [Range(0.5f, 1f)] public float inset;
    }

    [Header("Shell")]
    [Tooltip("Bottom of the vest. Lower covers more belly but risks the legs.")]
    [Range(0.04f, 0.14f)] public float hemY = 0.074f;

    [Tooltip("Top of the vest at the front and back.")]
    [Range(0.12f, 0.21f)] public float collarY = 0.178f;

    [Tooltip("How far the top edge dips at the flanks to clear the arms.")]
    [Range(0f, 0.09f)] public float armholeDrop = 0.046f;

    [Tooltip("Clearance between the shell and the skin.")]
    [Range(0f, 0.02f)] public float skinGap = 0.002f;

    [Tooltip("Thickness of the shell, which sets how chunky the open edges read.")]
    [Range(0.002f, 0.03f)] public float thickness = 0.008f;

    [Tooltip("Half width of the gap down the front.")]
    [Range(0f, 60f)] public float openingDeg = 20f;

    [Header("Shoulders")]
    [Tooltip("Straps that go over the shoulders and join the front of the vest to the back.")]
    public bool addShoulders = true;

    [Tooltip("Where each strap meets the chest, in degrees from the front centre.")]
    [Range(20f, 80f)] public float shoulderFrontDeg = 42f;

    [Tooltip("How far down the vest the strap is rooted. Lower values overlap more of the shell.")]
    [Range(0.4f, 1f)] public float shoulderOverlap = 0.68f;

    [Tooltip("Height of the strap at the top of the shoulder, next to the neck.")]
    [Range(0.15f, 0.22f)] public float shoulderPeakY = 0.186f;

    [Tooltip("Width of the strap.")]
    [Range(0.008f, 0.05f)] public float shoulderWidth = 0.022f;

    [Tooltip("Thickness of the strap.")]
    [Range(0.003f, 0.02f)] public float shoulderThickness = 0.007f;

    [Header("Resolution")]
    [Range(9, 49)] public int columns = 25;
    [Range(3, 12)] public int rings = 6;

    [Header("Colours")]
    public Color shellColor = new Color(0.322f, 0.373f, 0.235f);
    public Color pocketColor = new Color(0.639f, 0.545f, 0.373f);

    [Header("Pockets")]
    [Tooltip("Each entry is built on the right and mirrored to the left.")]
    public Pocket[] pockets =
    {
        new Pocket
        {
            name = "Pocket",
            centerDeg = 38f,
            halfDeg = 15f,
            centerY = 0.104f,
            halfY = 0.014f,
            lift = 0.008f,
            inset = 0.80f
        },
        new Pocket
        {
            name = "Flap",
            centerDeg = 38f,
            halfDeg = 17f,
            centerY = 0.122f,
            halfY = 0.0045f,
            lift = 0.011f,
            inset = 0.86f
        }
    };
}
