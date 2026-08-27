using UnityEngine;

/// <summary>
/// A walk-on landing the player can step onto when the boat is close.
/// Yaw comes from <see cref="facing"/>, or this transform if that is unset.
/// </summary>
public class BoatDock : MonoBehaviour
{
    [SerializeField] Transform facing;

    public Transform Facing => facing != null ? facing : transform;
}
