using UnityEngine;

/// <summary>
/// Stardew-style vertical fight. 0 is the bottom of the track, 1 is the top.
/// </summary>
public class FishFight
{
    public enum Result
    {
        Playing,
        Won,
        Lost
    }

    float fishY;
    float fishTarget;
    float barY;
    float barVel;
    float progress;
    float stamina;
    float nextMoveTime;
    float dartUntil;
    float dartSpeed;
    float size;
    float time;

    public Result Status { get; private set; }
    public bool Playing => Status == Result.Playing;
    public float FishY => fishY;
    public float BarY => barY;
    public float BarHeight { get; private set; }
    public float FishHeight => Mathf.Lerp(0.08f, 0.10f, size);
    public float Progress => progress;
    public float Size => size;

    public void Begin(float pounds)
    {
        size = FightSize(pounds);
        BarHeight = Mathf.Lerp(0.24f, 0.18f, size);
        fishY = 0.52f;
        fishTarget = 0.52f;
        barY = 0.38f;
        barVel = 0f;
        progress = 0.28f;
        stamina = 1f;
        time = 0f;
        dartUntil = 0f;
        nextMoveTime = 0.15f;
        Status = Result.Playing;
        PickTarget(true);
    }

    public Result Tick(bool held, float dt)
    {
        if (Status != Result.Playing)
            return Status;

        dt = Mathf.Max(0f, dt);
        time += dt;
        stamina = Mathf.Max(0.18f, stamina - dt * Mathf.Lerp(0.035f, 0.06f, size));

        TickFish(dt);
        TickBar(held, dt);
        TickProgress(dt);
        return Status;
    }

    void TickFish(float dt)
    {
        if (time >= nextMoveTime)
            PickTarget(false);

        float speed = dartUntil > time
            ? dartSpeed
            : Mathf.Lerp(0.32f, 0.60f, size) * Mathf.Lerp(0.45f, 1f, stamina);
        fishY = Mathf.MoveTowards(fishY, fishTarget, speed * dt);
        fishY = Mathf.Clamp01(fishY);
    }

    void PickTarget(bool first)
    {
        float dartChance = first ? 0.15f : Mathf.Lerp(0.18f, 0.34f, size) * stamina;
        if (Random.value < dartChance)
        {
            float away = fishY + (Random.value < 0.5f ? -1f : 1f) * Mathf.Lerp(0.22f, 0.36f, size);
            fishTarget = Mathf.Clamp01(away + Random.Range(-0.08f, 0.08f));
            dartSpeed = Mathf.Lerp(1.35f, 1.90f, size) * Mathf.Lerp(0.55f, 1f, stamina);
            dartUntil = time + Mathf.Lerp(0.16f, 0.38f, Random.value);
            nextMoveTime = dartUntil + Mathf.Lerp(0.12f, 0.35f, Random.value);
            return;
        }

        fishTarget = Mathf.Clamp01(fishY + Random.Range(-0.18f, 0.18f));
        dartUntil = 0f;
        nextMoveTime = time + Mathf.Lerp(0.35f, 1.05f, Random.value) * Mathf.Lerp(1.15f, 0.78f, size);
    }

    void TickBar(bool held, float dt)
    {
        float accel = 5.4f;
        float gravity = 4.1f;
        float maxVel = 2.35f;
        if (held)
            barVel += accel * dt;
        else
            barVel -= gravity * dt;

        barVel *= 1f - 2.4f * dt;
        barVel = Mathf.Clamp(barVel, -maxVel, maxVel);

        float half = BarHeight * 0.5f;
        barY += barVel * dt;
        if (barY < half)
        {
            barY = half;
            barVel = Mathf.Abs(barVel) * 0.28f;
        }
        else if (barY > 1f - half)
        {
            barY = 1f - half;
            barVel = -Mathf.Abs(barVel) * 0.28f;
        }
    }

    void TickProgress(float dt)
    {
        float half = BarHeight * 0.5f + FishHeight * 0.45f;
        bool overlap = Mathf.Abs(barY - fishY) <= half;
        float fill = Mathf.Lerp(0.26f, 0.13f, size);
        float drain = Mathf.Lerp(0.12f, 0.18f, size);
        progress += (overlap ? fill : -drain) * dt;
        progress = Mathf.Clamp01(progress);

        if (progress >= 1f)
            Status = Result.Won;
        else if (progress <= 0f && time > 0.45f)
            Status = Result.Lost;
    }

    /// <summary>
    /// 1 lb is the easy baseline, 8 lb is about twice as hard. Heavier fish
    /// add a little more, but stay on the same curve instead of going wild.
    /// </summary>
    static float FightSize(float pounds)
    {
        if (pounds <= 8f)
            return Mathf.InverseLerp(1f, 8f, Mathf.Clamp(pounds, 1f, 8f));
        return 1f + Mathf.InverseLerp(8f, 12f, Mathf.Clamp(pounds, 8f, 12f)) * 0.12f;
    }
}
