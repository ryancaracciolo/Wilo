using UnityEngine;

/// <summary>
/// Decorative rival hull: drive to a lake spot, sit, move on, and be back
/// at camp by lines-out. No fishing — presence only.
/// </summary>
[DefaultExecutionOrder(10)]
public class TournamentBoatAgent : MonoBehaviour
{
    enum Plan
    {
        LeaveCamp,
        Cruise,
        Hold,
        Return,
        Stage
    }

    TournamentBoatDirector director;
    BoatMotor motor;
    Transform angler;
    Transform seat;
    Transform helm;
    System.Random rng;

    Plan plan;
    Vector3 destination;
    float holdUntilHour;
    float leaveHour;
    float returnHour;
    float stuckSeconds;
    Vector3 lastProgress;

    public string AnglerName { get; private set; } = "";

    public void Bind(
        TournamentBoatDirector owner,
        BoatMotor hull,
        Transform rider,
        string anglerName,
        int seed,
        float startHour,
        float endHour,
        float hour)
    {
        director = owner;
        motor = hull;
        angler = rider;
        AnglerName = anglerName ?? "";
        rng = new System.Random(seed);
        seat = hull != null ? hull.Seat : null;
        helm = hull != null ? hull.Helm : null;

        leaveHour = startHour + (float)rng.NextDouble() * 0.18f;
        returnHour = endHour - Mathf.Lerp(0.45f, 1.15f, (float)rng.NextDouble());

        if (motor != null)
        {
            motor.SetBoardable(false);
            motor.SetOccupied(true);
            motor.SetAiDrive(Vector2.zero);
        }

        lastProgress = transform.position;
        SnapAngler(false);

        if (hour >= endHour - 0.05f)
        {
            EnterStage();
            return;
        }

        if (hour >= returnHour)
        {
            BeginReturn();
            return;
        }

        if (hour <= leaveHour)
        {
            plan = Plan.LeaveCamp;
            holdUntilHour = leaveHour;
            return;
        }

        if (director != null && director.TryLakeSpot(this, true, out destination))
        {
            transform.position = destination;
            if (motor != null)
            {
                float yaw = (float)rng.NextDouble() * 360f;
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }

            EnterHold(hour);
            return;
        }

        plan = Plan.LeaveCamp;
        holdUntilHour = hour;
    }

    public void Recall()
    {
        if (plan == Plan.Return || plan == Plan.Stage)
            return;
        BeginReturn();
    }

    void Update()
    {
        if (director == null || motor == null)
            return;

        float hour = director.Hour;
        if (hour >= returnHour && plan != Plan.Return && plan != Plan.Stage)
            BeginReturn();

        switch (plan)
        {
            case Plan.LeaveCamp:
                if (hour >= holdUntilHour)
                    BeginCruise();
                else
                    motor.SetAiDrive(Vector2.zero);
                break;
            case Plan.Cruise:
            case Plan.Return:
                DriveToward(destination);
                if (Reached(destination, plan == Plan.Return ? 8f : 6f))
                {
                    if (plan == Plan.Return)
                        EnterStage();
                    else
                        EnterHold(hour);
                }
                break;
            case Plan.Hold:
                motor.SetAiDrive(Vector2.zero);
                if (hour >= holdUntilHour)
                    BeginCruise();
                break;
            case Plan.Stage:
                StageAtCamp();
                break;
        }

        SnapAngler(motor.Speed > 1.2f);
    }

    void BeginCruise()
    {
        if (director == null || !director.TryLakeSpot(this, false, out destination))
        {
            EnterHold(director != null ? director.Hour : 0f);
            return;
        }

        plan = Plan.Cruise;
        stuckSeconds = 0f;
        lastProgress = transform.position;
    }

    void BeginReturn()
    {
        plan = Plan.Return;
        stuckSeconds = 0f;
        lastProgress = transform.position;
        if (director == null || !director.TryCampSpot(this, out destination))
            destination = transform.position;
    }

    void EnterHold(float hour)
    {
        plan = Plan.Hold;
        float linger = Mathf.Lerp(0.28f, 0.72f, (float)rng.NextDouble());
        holdUntilHour = hour + linger;
        motor.SetAiDrive(Vector2.zero);
    }

    void EnterStage()
    {
        plan = Plan.Stage;
        if (director != null)
            director.TryCampSpot(this, out destination);
        motor.SetAiDrive(Vector2.zero);
        stuckSeconds = 0f;
    }

    void StageAtCamp()
    {
        float gap = DistanceXZ(transform.position, destination);
        if (gap > 10f)
        {
            DriveToward(destination);
            return;
        }

        if (gap > 3.5f)
            DriveToward(destination, 0.22f);
        else
            motor.SetAiDrive(Vector2.zero);
    }

    void DriveToward(Vector3 target, float throttleScale = 1f)
    {
        Vector3 to = target - transform.position;
        to.y = 0f;
        float distance = to.magnitude;
        Vector3 bow = -transform.forward;
        bow.y = 0f;
        if (bow.sqrMagnitude < 0.0001f)
            bow = Vector3.forward;
        bow.Normalize();

        float angle = distance > 0.05f
            ? Vector3.SignedAngle(bow, to / distance, Vector3.up)
            : 0f;
        float steer = Mathf.Clamp(angle / 40f, -1f, 1f);

        Vector3 probe = transform.position + bow * 3.2f;
        if (motor.WouldBlock(probe) || (director != null && !director.IsNavigable(probe)))
            steer = Mathf.Clamp(steer + (SteerClear(bow) >= 0f ? 1f : -1f), -1f, 1f);

        float facing = 1f - Mathf.Clamp01(Mathf.Abs(angle) / 90f);
        float throttle = Mathf.Lerp(0.18f, 0.72f, facing) * throttleScale;
        if (distance < 14f)
            throttle *= Mathf.Lerp(0.25f, 1f, distance / 14f);

        motor.SetAiDrive(new Vector2(steer, throttle));
        TrackStuck();
    }

    float SteerClear(Vector3 bow)
    {
        Vector3 left = Quaternion.Euler(0f, -40f, 0f) * bow;
        Vector3 right = Quaternion.Euler(0f, 40f, 0f) * bow;
        Vector3 leftAt = transform.position + left * 4f;
        Vector3 rightAt = transform.position + right * 4f;
        bool leftOk = !motor.WouldBlock(leftAt) && (director == null || director.IsNavigable(leftAt));
        bool rightOk = !motor.WouldBlock(rightAt) && (director == null || director.IsNavigable(rightAt));
        if (leftOk == rightOk)
            return rng.NextDouble() < 0.5 ? -1f : 1f;
        return leftOk ? -1f : 1f;
    }

    void TrackStuck()
    {
        if (DistanceXZ(transform.position, lastProgress) > 0.8f)
        {
            lastProgress = transform.position;
            stuckSeconds = 0f;
            return;
        }

        stuckSeconds += Time.deltaTime;
        if (stuckSeconds < 2.8f)
            return;

        stuckSeconds = 0f;
        if (plan == Plan.Return || plan == Plan.Stage)
        {
            if (director != null)
                director.TryCampSpot(this, out destination);
            return;
        }

        BeginCruise();
    }

    void SnapAngler(bool driving)
    {
        if (angler == null)
            return;

        Transform stance = driving && helm != null ? helm : seat;
        if (stance == null)
            return;

        if (angler.parent != transform)
            angler.SetParent(transform, true);

        angler.localPosition = stance.localPosition;
        angler.localRotation = stance.localRotation;
    }

    bool Reached(Vector3 target, float radius)
    {
        return DistanceXZ(transform.position, target) <= radius;
    }

    static float DistanceXZ(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
