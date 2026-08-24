using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Lightweight wander for a physical fish tied to a lake cell.
/// Nearby lures can pull it off wander into a follow / strike / reject.
/// </summary>
public class FishAgent : MonoBehaviour
{
    enum Mood
    {
        Wander,
        Following,
        Hooked,
        ShowingCatch
    }

    const float NoticeRadius = 8f;
    const float GiveUpRadius = 14f;
    const float StrikeRadius = 2.1f;
    static readonly int ForceVisibleId = Shader.PropertyToID("_ForceVisible");

    LakeSimulation lake;
    Vector3 home;
    Vector3 destination;
    Vector3 prefabScale;
    Vector3 surgeTarget;
    Transform hook;
    Transform angler;
    Quaternion presentBaseRot;
    Renderer[] renderers;
    MaterialPropertyBlock block;
    Animator animator;
    Mood mood;
    float wanderRadius;
    float speed;
    float turnRate;
    float nextPickTime;
    float ignoreUntil;
    float columnT;
    float fightTime;
    float nextSurgeTime;
    float nextJumpTime;
    float jumpElapsed;
    float jumpDuration;
    float jumpPeak;
    float leash;
    float swimY;
    bool jumping;
    Vector3 jumpStart;
    Vector3 jumpEnd;

    public FishSpecies Species { get; private set; }
    public FishSize Size { get; private set; }
    public bool IsHooked => mood == Mood.Hooked;
    public Vector3 LinePoint => MouthPoint();

    public void Bind(FishSpecies species)
    {
        Species = species;
        if (prefabScale.sqrMagnitude < 0.0001f)
            prefabScale = transform.localScale;
        CacheRenderers();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        SetCastShadows(false);
    }

    public void Activate(
        LakeSimulation lake,
        FishSpecies species,
        FishSize size,
        Vector3 worldPosition,
        float wanderRadius,
        float speed,
        float yawDegrees,
        float columnT,
        float visualScale)
    {
        this.lake = lake;
        Species = species;
        Size = size;
        home = worldPosition;
        this.wanderRadius = wanderRadius;
        this.speed = speed;
        this.columnT = Mathf.Clamp01(columnT);
        turnRate = 4.5f;
        mood = Mood.Wander;
        hook = null;
        angler = null;
        jumping = false;
        ignoreUntil = 0f;
        SetAnimatorSpeed(1f);
        if (prefabScale.sqrMagnitude < 0.0001f)
            prefabScale = Vector3.one * 0.25f;
        transform.localScale = prefabScale * visualScale;
        transform.SetPositionAndRotation(
            worldPosition,
            Quaternion.Euler(0f, yawDegrees, 0f));
        SetForceVisible(false);
        SetCastShadows(false);
        PickDestination();
        SnapToColumn();
    }

    public void Sleep()
    {
        lake = null;
        hook = null;
        angler = null;
        jumping = false;
        mood = Mood.Wander;
        SetForceVisible(false);
        SetCastShadows(false);
        SetAnimatorSpeed(1f);
        if (prefabScale.sqrMagnitude > 0.0001f)
            transform.localScale = prefabScale;
    }

    public void Hook(Transform lurePoint, Transform anglerPoint)
    {
        mood = Mood.Hooked;
        hook = lurePoint;
        angler = anglerPoint;
        jumping = false;
        fightTime = 0f;
        nextSurgeTime = 0.05f;
        nextJumpTime = 0.55f + Random.value * 0.7f;
        Vector3 from = AnglerPlanar();
        leash = Mathf.Clamp(
            DistanceXZ(transform.position, from) * Mathf.Lerp(0.92f, 1.18f, FightHeft()),
            7f,
            34f);
        swimY = transform.position.y;
        SetForceVisible(true);
        SetAnimatorSpeed(1.7f);
        PickSurge(true);
    }

    public void PresentCatch(Transform holder)
    {
        mood = Mood.ShowingCatch;
        angler = holder;
        hook = null;
        jumping = false;
        SetForceVisible(true);
        SetCastShadows(true);
        SetAnimatorSpeed(0.55f);
        PoseForCatch();
    }

    void CacheRenderers()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);
        if (block == null)
            block = new MaterialPropertyBlock();
    }

    void SetForceVisible(bool on)
    {
        CacheRenderers();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;
            renderer.GetPropertyBlock(block);
            block.SetFloat(ForceVisibleId, on ? 1f : 0f);
            renderer.SetPropertyBlock(block);
        }
    }

    void SetCastShadows(bool on)
    {
        CacheRenderers();
        var mode = on ? ShadowCastingMode.On : ShadowCastingMode.Off;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].shadowCastingMode = mode;
        }
    }

    void Update()
    {
        if (lake == null)
            return;

        if (mood == Mood.ShowingCatch)
        {
            TickPresent();
            return;
        }

        if (mood == Mood.Hooked)
        {
            TickFight();
            return;
        }

        TickLure();
        if (mood == Mood.Hooked)
        {
            TickFight();
            return;
        }

        if (Time.time >= nextPickTime && mood == Mood.Wander)
            PickDestination();

        Vector3 pos = transform.position;
        float move = mood == Mood.Following ? Mathf.Max(speed * 2.4f, 6.5f) : speed;
        Vector3 planarTarget = new Vector3(destination.x, destination.y, destination.z);
        if (mood != Mood.Following)
            planarTarget.y = pos.y;
        Vector3 next = Vector3.MoveTowards(pos, planarTarget, move * Time.deltaTime);
        Vector3 heading = planarTarget - pos;
        if (mood != Mood.Following)
            heading.y = 0f;
        if (heading.sqrMagnitude > 0.0001f)
        {
            Quaternion look = Quaternion.LookRotation(heading.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                look,
                1f - Mathf.Exp(-turnRate * Time.deltaTime));
        }

        transform.position = next;
        if (mood == Mood.Wander)
            SnapToColumn();
    }

    void TickLure()
    {
        LurePresence lure = lake.Lure;
        if (lure == null || !lure.IsActive)
        {
            if (mood == Mood.Following)
            {
                mood = Mood.Wander;
                PickDestination();
            }

            return;
        }

        if (Time.time < ignoreUntil)
            return;

        float planar = DistanceXZ(transform.position, lure.Position);
        if (mood == Mood.Wander)
        {
            if (planar > NoticeRadius)
                return;

            float activity = lake.SampleAt(lure.Position).Activity;
            float notice = 0.18f + activity * 0.7f;
            if (Random.value > notice)
            {
                ignoreUntil = Time.time + 7f;
                return;
            }

            mood = Mood.Following;
        }

        if (planar > GiveUpRadius)
        {
            mood = Mood.Wander;
            ignoreUntil = Time.time + 4f;
            PickDestination();
            return;
        }

        destination = lure.Position;
        if (Vector3.Distance(transform.position, lure.Position) > StrikeRadius)
            return;

        float bite = 0.22f + lake.SampleAt(lure.Position).Activity * 0.65f;
        if (Random.value <= bite && lure.OfferStrike(this))
        {
            mood = Mood.Hooked;
            SetForceVisible(true);
            return;
        }

        mood = Mood.Wander;
        ignoreUntil = Time.time + 8f;
        PickDestination();
    }

    void TickFight()
    {
        float dt = Time.deltaTime;
        fightTime += dt;
        float heft = FightHeft();

        if (jumping)
            TickJump(dt, heft);
        else
        {
            if (fightTime >= nextSurgeTime)
                PickSurge(false);
            TickRun(dt, heft);
            if (fightTime >= nextJumpTime)
                BeginJump(heft);
            FaceMove(surgeTarget, 5.5f);
        }

        PullBobber();
    }

    void TickPresent()
    {
        PoseForCatch();
        float t = Time.time;
        float yaw = Mathf.Sin(t * 5.2f) * 4.2f;
        float pitch = Mathf.Sin(t * 3.6f) * 2.4f + Mathf.Sin(t * 11f) * 0.7f;
        float roll = Mathf.Sin(t * 6.4f) * 3.1f;
        transform.rotation = presentBaseRot * Quaternion.Euler(pitch, yaw, roll);
    }

    void PoseForCatch()
    {
        if (angler == null)
            return;

        Vector3 chest = angler.position + Vector3.up * 0.95f;
        Vector3 holdSide = -Flatten(angler.right);
        Vector3 fwd = Flatten(angler.forward);
        if (holdSide.sqrMagnitude < 0.0001f)
            holdSide = Vector3.right;

        float visualMeters = Size.LengthInches * 0.0254f;
        if (prefabScale.x > 0.01f)
            visualMeters *= transform.localScale.x / prefabScale.x;

        Vector3 pos = chest
            + holdSide * (0.52f + visualMeters * 0.1f)
            + fwd * 0.16f
            + Vector3.up * (0.04f + visualMeters * 0.06f);
        pos.y += Mathf.Sin(Time.time * 2.3f) * 0.014f;

        Vector3 nose = Flatten(angler.right);
        if (nose.sqrMagnitude < 0.0001f)
            nose = -holdSide;
        presentBaseRot = Quaternion.LookRotation(nose, Vector3.up) * Quaternion.Euler(-14f, 0f, 10f);
        transform.position = pos;
        transform.rotation = presentBaseRot;
    }

    void TickRun(float dt, float heft)
    {
        float run = Mathf.Lerp(3.4f, 11.2f, heft);
        Vector3 pos = transform.position;
        Vector3 planar = Vector3.MoveTowards(
            new Vector3(pos.x, 0f, pos.z),
            new Vector3(surgeTarget.x, 0f, surgeTarget.z),
            run * dt);
        planar.y = swimY;
        SnapFightDepth(ref planar);
        transform.position = planar;
    }

    void PickSurge(bool first)
    {
        Vector3 from = AnglerPlanar();
        Vector3 here = transform.position;
        Vector3 away = Flatten(here - from);
        if (away.sqrMagnitude < 0.01f)
            away = transform.forward;

        for (int i = 0; i < 8; i++)
        {
            Vector3 side = Vector3.Cross(Vector3.up, away);
            Vector3 dir = (
                away * Random.Range(first ? 0.4f : -0.35f, 1f) +
                side * Random.Range(-1f, 1f)).normalized;
            float dist = leash * Random.Range(0.42f, 1.05f);
            dist = Mathf.Max(Mathf.Lerp(3.6f, 6.5f, FightHeft()), dist);
            Vector3 candidate = from + dir * dist;
            if (lake != null && lake.GeometricDepthMeters(candidate) < 0.45f)
                continue;

            surgeTarget = candidate;
            nextSurgeTime = fightTime + Random.Range(0.35f, Mathf.Lerp(1.1f, 0.55f, FightHeft()));
            SetAnimatorSpeed(Mathf.Lerp(1.45f, 2.55f, FightHeft()));
            Splash(transform.position, WaterRippleKind.Reel);
            return;
        }

        surgeTarget = here + away * 3f;
        nextSurgeTime = fightTime + 0.45f;
    }

    void BeginJump(float heft)
    {
        Vector3 pos = transform.position;
        Vector3 ahead = Flatten(surgeTarget - pos);
        if (ahead.sqrMagnitude < 0.01f)
            ahead = transform.forward;
        jumpStart = pos;
        jumpEnd = pos + ahead * Mathf.Lerp(1.1f, 3.7f, heft);
        if (lake != null && lake.GeometricDepthMeters(jumpEnd) < 0.35f)
            jumpEnd = pos + ahead * Mathf.Lerp(0.8f, 1.4f, heft);

        jumping = true;
        jumpElapsed = 0f;
        jumpDuration = Mathf.Lerp(0.4f, 0.82f, heft);
        jumpPeak = Mathf.Lerp(0.32f, 1.85f, heft);
        nextJumpTime = fightTime + jumpDuration + Random.Range(0.9f, Mathf.Lerp(2.4f, 1.3f, heft));
        SetAnimatorSpeed(2.6f);
        Splash(pos, WaterRippleKind.Jump);
    }

    void TickJump(float dt, float heft)
    {
        jumpElapsed += dt;
        float t = Mathf.Clamp01(jumpElapsed / jumpDuration);
        float eased = t * t * (3f - 2f * t);
        Vector3 pos = Vector3.Lerp(jumpStart, jumpEnd, eased);
        float waterY = lake != null ? lake.SurfaceY : pos.y;
        pos.y = Mathf.Lerp(jumpStart.y, waterY - 0.22f, eased) + Mathf.Sin(t * Mathf.PI) * jumpPeak;

        float pitch = Mathf.Lerp(22f, 48f, heft) * Mathf.Sin(t * Mathf.PI);
        if (t > 0.5f)
            pitch *= -1f;
        Vector3 planar = Flatten(jumpEnd - jumpStart);
        if (planar.sqrMagnitude > 0.01f)
        {
            Quaternion yaw = Quaternion.LookRotation(planar, Vector3.up);
            transform.rotation = yaw * Quaternion.Euler(pitch, 0f, 0f);
        }

        transform.position = pos;
        if (t < 1f)
            return;

        jumping = false;
        swimY = waterY - 0.28f;
        SnapFightDepth(ref pos);
        transform.position = pos;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        Splash(pos, WaterRippleKind.Jump);
        SetAnimatorSpeed(Mathf.Lerp(1.6f, 2.2f, heft));
        PickSurge(false);
    }

    void SnapFightDepth(ref Vector3 pos)
    {
        if (lake == null)
            return;

        float depth = lake.GeometricDepthMeters(pos);
        float waterY = lake.SurfaceY;
        float below = Mathf.Clamp(DepthBelowSurface(depth, 0.35f), 0.16f, 1.15f);
        pos.y = waterY - below;
        swimY = pos.y;
    }

    void FaceMove(Vector3 target, float rate)
    {
        Vector3 heading = Flatten(target - transform.position);
        if (heading.sqrMagnitude < 0.0001f)
            return;

        Quaternion look = Quaternion.LookRotation(heading, Vector3.up);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            look,
            1f - Mathf.Exp(-rate * Time.deltaTime));
    }

    void PullBobber()
    {
        if (hook == null)
            return;

        hook.position = MouthPoint();
    }

    Vector3 MouthPoint()
    {
        float visualMeters = Size.LengthInches * 0.0254f;
        if (prefabScale.x > 0.01f)
            visualMeters *= transform.localScale.x / prefabScale.x;
        return transform.position + transform.forward * Mathf.Max(0.18f, visualMeters * 0.38f);
    }

    Vector3 AnglerPlanar()
    {
        Vector3 p = angler != null ? angler.position : (hook != null ? hook.position : transform.position);
        p.y = 0f;
        return p;
    }

    float FightHeft()
    {
        return Mathf.InverseLerp(0.6f, 8f, Size.Pounds);
    }

    void Splash(Vector3 world, WaterRippleKind kind)
    {
        Vector3 at = world;
        if (lake != null)
            at.y = lake.SurfaceY;
        WaterRipples.Emit(at, kind, SplashScale());
    }

    float SplashScale()
    {
        return Mathf.Lerp(0.48f, 2.2f, FightHeft());
    }

    void SetAnimatorSpeed(float value)
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (animator != null)
            animator.speed = value;
    }

    static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        return value.sqrMagnitude > 0.001f ? value.normalized : Vector3.forward;
    }

    void PickDestination()
    {
        nextPickTime = Time.time + Random.Range(2.4f, 5.2f);
        if (lake == null)
            return;

        Vector3 best = home;
        float bestDensity = -1f;
        for (int i = 0; i < 5; i++)
        {
            Vector2 offset = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = home + new Vector3(offset.x, 0f, offset.y);
            HabitatSample sample = lake.SampleAt(candidate);
            if (!sample.HasFish || sample.FishPerThousandSqMeters <= bestDensity)
                continue;

            bestDensity = sample.FishPerThousandSqMeters;
            best = candidate;
        }

        destination = bestDensity > 0f ? best : home;
    }

    void SnapToColumn()
    {
        if (lake == null)
            return;

        Vector3 pos = transform.position;
        float depth = lake.GeometricDepthMeters(pos);
        pos.y = lake.SurfaceY - DepthBelowSurface(depth, columnT);
        transform.position = pos;
    }

    static float DistanceXZ(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// 0 sits near the surface, 1 hugs the bottom. Squaring toward 1 so
    /// most bass hold low, with some suspended.
    /// </summary>
    public static float BottomWeightedColumn(float u01)
    {
        u01 = Mathf.Clamp01(u01);
        return 1f - Mathf.Pow(1f - u01, 3f);
    }

    public static float DepthBelowSurface(float depthMeters, float columnT)
    {
        const float minAboveBed = 0.22f;
        const float minBelowSurface = 0.18f;
        float usable = depthMeters - minAboveBed - minBelowSurface;
        if (usable <= 0.05f)
        {
            return Mathf.Clamp(
                depthMeters * 0.5f,
                0.12f,
                Mathf.Max(0.12f, depthMeters - 0.08f));
        }

        float t = Mathf.Clamp01(columnT);
        float aboveBed = minAboveBed + (1f - t) * usable;
        return depthMeters - aboveBed;
    }
}
