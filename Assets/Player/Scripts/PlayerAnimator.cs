using UnityEngine;

/// <summary>
/// Drives the chibi Idle / Walk / Run clips from planar motor speed.
/// </summary>
[RequireComponent(typeof(PlayerMotor))]
public class PlayerAnimator : MonoBehaviour
{
    static readonly int SpeedId = Animator.StringToHash("Speed");

    const float DampTime = 0.1f;

    Animator animator;
    PlayerMotor motor;

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        animator = GetComponentInChildren<Animator>();
    }

    void LateUpdate()
    {
        if (animator == null || !animator.isActiveAndEnabled)
            return;

        float speed = 0f;
        if (motor != null && motor.enabled)
        {
            Vector3 planar = motor.Velocity;
            planar.y = 0f;
            speed = planar.magnitude;
        }

        animator.SetFloat(SpeedId, speed, DampTime, Time.deltaTime);
    }
}
