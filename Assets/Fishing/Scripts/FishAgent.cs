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

    const float GiveUpRadius = 14f;
    const float StrikeRadius = 2.1f;

    /// <summary>Need this much water over the ground to hold. Emerged rock tops fail it.</summary>
    public const float MinHoldDepthMeters = 0.4f;
    const float MinBelowSurface = 0.18f;
    const float MinAboveGround = 0.22f;

    /// <summary>How long a fish will shadow a lure before losing interest.</summary>
    const float FollowPatience = 22f;

    /// <summary>How far off the bait a following fish holds. Well inside the strike radius.</summary>
    const float FollowStandoff = 0.9f;

    static readonly int ForceVisibleId = Shader.PropertyToID("_ForceVisible");

    LakeSimulation lake;
    Vector3 home;
    Vector3 destination;
    Vector3 prefabScale;
    Vector3 surgeTarget;
    Transform hook;
    Transform angler;
    Quaternion presentBaseRot;
    Vector3 presentLip;
    Vector3 presentHang;
    Transform[] tailBones;
    Quaternion[] tailRest;
    Transform headBone;
    float tailFloorBend;
    float tailFloorBendVel;
    Renderer[] renderers;
    MaterialPropertyBlock block;
    Animator animator;
    Mood mood;
    float wanderRadius;
    float speed;
    float turnRate;
    float nextPickTime;
    float ignoreUntil;
    float followUntil;
    float followPhase;
    float columnT;
    float biteActivity;
    float biteAt;
    bool judgedBite;
    float fightTime;
    float nextSurgeTime;
    float nextJumpTime;
    float jumpElapsed;
    float jumpDuration;
    float jumpPeak;
    float leash;
    float swimY;
    float fightDepthVel;
    bool jumping;
    Vector3 jumpStart;
    Vector3 jumpEnd;
    Vector3 groundCacheAt;
    float groundCacheDepth;
    float groundCacheTime;

    public FishSpecies Species { get; private set; }
    public FishSize Size { get; private set; }
    public bool IsHooked => mood == Mood.Hooked;
    public Vector3 LinePoint => MouthPoint();
    public Vector3 CatchFocusPoint => Vector3.Lerp(presentLip, transform.position, 0.55f);
    public bool WantsTwoHandHold => false;
    public Vector3 CatchSupportPoint
    {
        get
        {
            Vector3 along = Vector3.Lerp(presentLip, transform.position, catchSupportAlong);
            Vector3 outward = angler != null ? Flatten(angler.forward) : Vector3.forward;
            return along + outward * catchSupportOut + Vector3.down * catchSupportDown;
        }
    }

    float catchLipAlong = 0.48f;
    float catchLipBelly = 0.02f;
    float catchSupportAlong = 0.52f;
    float catchSupportOut = 0.11f;
    float catchSupportDown = 0.06f;

    public void ApplyCatchFit(
        float lipAlong, float lipBelly, float supportAlong, float supportOut, float supportDown)
    {
        catchLipAlong = lipAlong;
        catchLipBelly = lipBelly;
        catchSupportAlong = supportAlong;
        catchSupportOut = supportOut;
        catchSupportDown = supportDown;
    }

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
        followUntil = 0f;
        followPhase = Random.value * Mathf.PI * 2f;
        judgedBite = false;
        biteActivity = 0.5f;
        biteAt = 0f;
        SetCatchAnimator(false);
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
        judgedBite = false;
        SetForceVisible(false);
        SetCastShadows(false);
        SetCatchAnimator(false);
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
        fightDepthVel = 0f;
        SetForceVisible(true);
        SetAnimatorSpeed(1.7f);
        PickSurge(true);
    }

    public void PresentCatch(Transform holder, Vector3 lipWorld)
    {
        mood = Mood.ShowingCatch;
        angler = holder;
        hook = null;
        jumping = false;
        SetForceVisible(true);
        SetCastShadows(true);
        SetCatchAnimator(true);
        HangFromLip(lipWorld, holder, Vector3.down);
    }

    /// <summary>
    /// Pin the lower jaw to a pinch point and lay the body along hangDir so a
    /// long bass stays off the deck. Tail bones curl if they still meet the hull.
    /// </summary>
    public void HangFromLip(Vector3 lipWorld, Transform holder, Vector3 hangDir)
    {
        if (holder != null)
            angler = holder;
        presentLip = lipWorld;
        presentHang = hangDir.sqrMagnitude > 0.0001f ? hangDir.normalized : Vector3.down;
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
            return;

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

        SetAnimatorSpeed(mood == Mood.Following ? 1.35f : 1f);

        Vector3 pos = transform.position;
        float move = mood == Mood.Following ? ChaseSpeed() : speed;
        Vector3 planarTarget = new Vector3(destination.x, destination.y, destination.z);
        if (mood != Mood.Following)
            planarTarget.y = pos.y;
        Vector3 next = Vector3.MoveTowards(pos, planarTarget, move * Time.deltaTime);
        if (!HasSwimRoom(GroundDepth(next)))
        {
            next.x = pos.x;
            next.z = pos.z;
        }

        Vector3 heading = planarTarget - pos;
        if (mood == Mood.Following)
        {
            // Keep its nose on the bait while it circles, rather than facing the
            // hold point it is already sitting on.
            LurePresence bait = lake.Lure;
            if (bait != null && bait.IsActive)
                heading = bait.Position - pos;
        }
        else
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
            EaseToColumn();
        else
            LiftOutOfGround();
    }

    void TickLure()
    {
        LurePresence lure = lake.Lure;
        if (lure == null || !lure.IsActive)
        {
            if (mood == Mood.Following)
            {
                mood = Mood.Wander;
                judgedBite = false;
                PickDestination();
            }

            return;
        }

        float planar = DistanceXZ(transform.position, lure.Position);
        float range = Vector3.Distance(transform.position, lure.Position);
        bool inStrike = range <= StrikeRadius;

        if (mood == Mood.Wander)
        {
            // Refuse / give-up still sulk, but a bait that then lands on them
            // is always in play. Missed interest rolls do not lock anyone out.
            if (Time.time < ignoreUntil && !inStrike)
                return;

            if (!TryInterest(lure, range, inStrike))
                return;

            mood = Mood.Following;
            judgedBite = false;
            followUntil = Time.time + FollowPatience;
            biteActivity = lake.CurrentActivity;
        }

        if (planar > GiveUpRadius || Time.time >= followUntil || DistanceXZ(transform.position, home) > ChaseLeash())
        {
            mood = Mood.Wander;
            judgedBite = false;
            ignoreUntil = Time.time + 4f;
            PickDestination();
            return;
        }

        if (!inStrike)
        {
            destination = ShadowPoint(lure.Position);
            return;
        }

        float depth = LureDepthFit(lure);
        if (!judgedBite)
        {
            judgedBite = true;
            float clock = 1f;
            if (lure.Lure != null && lake.Conditions != null)
            {
                clock = lure.Lure.TimeOfDayTakeMul(
                    lake.Conditions.Hour,
                    lake.Conditions.DawnHour,
                    lake.Conditions.DuskHour);
            }

            float take = Mathf.Clamp01(
                (0.32f + biteActivity * 0.5f)
                * Mathf.Lerp(0.35f, 1f, lure.Liveliness)
                * depth
                * clock);
            if (Random.value > take)
            {
                mood = Mood.Wander;
                ignoreUntil = Time.time + 5.5f;
                PickDestination();
                return;
            }

            biteAt = Time.time + Random.Range(0.45f, 1.2f);
        }

        // They've decided to eat: run the bait down instead of orbiting it.
        destination = lure.Position;
        if (Time.time < biteAt)
            return;

        // Hook() runs inside OfferStrike and is what actually sets Hooked.
        // If nobody took the strike, hand the lure back and try again soon.
        if (lure.OfferStrike(this))
        {
            if (mood == Mood.Hooked)
                return;
            lure.ReleaseClaim(this);
        }

        mood = Mood.Wander;
        judgedBite = false;
        ignoreUntil = Time.time + 0.75f;
        PickDestination();
    }

    /// <summary>
    /// Time × distance. Draw reach is where pull hits zero (moving vs rest).
    /// Inside that, pull is linear with 3D range. The per-second chance is
    /// applied every frame so a spinner that only spends 0.8 s near a fish
    /// still gets a roll. A miss just waits; soaking a stump keeps rolling.
    /// </summary>
    bool TryInterest(LurePresence lure, float range, bool inStrike)
    {
        if (inStrike)
            return true;

        float reach = lure.NoticeRadius;
        if (range >= reach || reach < 0.05f)
            return false;

        float perSecond = (1f - range / reach) * LureDepthFit(lure);
        if (perSecond <= 0.0001f)
            return false;

        // Time.deltaTime is play/wall seconds (controller time), not the
        // compressed day clock. A 40-minute game day does not speed this up.
        float miss = Mathf.Pow(1f - Mathf.Clamp01(perSecond), Time.deltaTime);
        return Random.value > miss;
    }

    /// <summary>
    /// A following fish shadows the bait instead of parking on it. Steering at
    /// the lure itself lands the fish exactly on target, where it stops dead and
    /// stops turning — which reads as a frozen fish for the seconds it spends
    /// deciding whether to eat.
    /// </summary>
    Vector3 ShadowPoint(Vector3 lurePosition)
    {
        float t = Time.time * 1.9f + followPhase;
        return lurePosition + new Vector3(
            Mathf.Cos(t) * FollowStandoff,
            Mathf.Sin(t * 0.7f) * 0.25f,
            Mathf.Sin(t) * FollowStandoff);
    }

    /// <summary>
    /// A committed fish bursts hard enough to run a lure down. Without that a
    /// fast bait could never be caught at all; what actually limits fast lures
    /// is how quickly they drag a fish off its spot, not that it cannot keep up.
    /// </summary>
    float ChaseSpeed()
    {
        float burst = Mathf.Max(speed * 2.4f, 6.5f);
        LurePresence lure = lake.Lure;
        if (lure != null)
            burst = Mathf.Max(burst, lure.Speed * 1.6f);
        return Mathf.Min(burst, 12f);
    }

    /// <summary>
    /// How far a fish will let a lure pull it off its spot. This is what turns
    /// retrieve speed into time in the strike zone: a fast bait spends the whole
    /// budget in seconds, a bait left sitting never spends any of it.
    /// </summary>
    float ChaseLeash() => Mathf.Max(wanderRadius * 1.6f, 12f);

    /// <summary>
    /// Bass feed upward. A lure riding above the fish is in play; one passing
    /// below it mostly is not. Clear water widens the window.
    /// </summary>
    float LureDepthFit(LurePresence lure)
    {
        HabitatProfile profile = lake.Profile;
        if (profile == null)
            return 1f;

        WorldConditions conditions = lake.Conditions;
        float scale = conditions != null ? conditions.GameplayDepthScale : 0.5f;
        float visibility = conditions != null ? conditions.WaterVisibility : 10f;
        float aboveFeet = (lure.Position.y - transform.position.y) * scale * 3.28084f;
        return profile.LureDepthFit(aboveFeet, visibility);
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
            {
                if (CloseEnoughToJump())
                    BeginJump(heft);
                else
                    nextJumpTime = fightTime + Random.Range(0.55f, 1.2f);
            }
            FaceMove(surgeTarget, 5.5f);
        }

        PullBobber();
    }

    void PoseForCatch()
    {
        Vector3 hang = FlattenedHang(HangDir());
        Vector3 mouthDir = -hang;
        Vector3 towardCam = angler != null ? Flatten(angler.forward) : Vector3.forward;
        Vector3 dorsal = Vector3.Cross(mouthDir, towardCam);
        if (dorsal.sqrMagnitude < 0.0001f)
            dorsal = angler != null ? angler.right : Vector3.right;
        dorsal.Normalize();
        if (Vector3.Dot(dorsal, Vector3.up) < 0f)
            dorsal = -dorsal;

        presentBaseRot = Quaternion.LookRotation(mouthDir, dorsal);
        bool vertical = Vector3.Dot(hang, Vector3.down) > 0.85f;
        float t = Time.time;
        if (vertical)
        {
            float yaw = Mathf.Sin(t * 3.1f) * 1.6f;
            float roll = Mathf.Sin(t * 4.4f) * 2.2f;
            transform.rotation = presentBaseRot * Quaternion.Euler(0f, yaw, roll);
        }
        else
        {
            float roll = Mathf.Sin(t * 3.4f) * 2.4f;
            transform.rotation = presentBaseRot * Quaternion.AngleAxis(roll, Vector3.forward);
        }
        SnapLipToHand();
        ResetTailRest();
        PoseCatchTail();
    }

    Vector3 HangDir()
    {
        if (presentHang.sqrMagnitude > 0.0001f)
            return presentHang.normalized;
        if (angler != null)
            return (angler.right * 0.72f + Vector3.down * 0.52f).normalized;
        return Vector3.down;
    }

    Vector3 FlattenedHang(Vector3 hang)
    {
        if (Vector3.Dot(hang.normalized, Vector3.down) > 0.85f)
            return Vector3.down;

        float floorY = CatchFloorY() + 0.06f;
        float length = VisualLength();
        float room = presentLip.y - floorY;
        if (room >= length * 0.88f)
            return SquareHangToCamera(hang);

        Vector3 flat = angler != null ? Flatten(angler.right) : Vector3.right;
        if (flat.sqrMagnitude < 0.0001f)
            flat = Vector3.right;
        float t = 1f - Mathf.Clamp01(room / Mathf.Max(0.12f, length));
        hang = Vector3.Slerp(hang, flat, Mathf.Clamp01(0.28f + t * 0.8f)).normalized;
        hang = (hang + Vector3.down * Mathf.Lerp(0.38f, 0.1f, t)).normalized;
        return SquareHangToCamera(hang);
    }

    Vector3 SquareHangToCamera(Vector3 hang)
    {
        if (angler == null)
            return hang;

        Vector3 outward = Flatten(angler.forward);
        hang -= outward * Vector3.Dot(hang, outward);
        return hang.sqrMagnitude > 0.0001f ? hang.normalized : Vector3.down;
    }

    void SnapLipToHand()
    {
        transform.position += presentLip - LipOnFish();
    }

    Vector3 LipOnFish()
    {
        float length = VisualLength();
        Vector3 axis = transform.forward;
        Vector3 lip = transform.position + axis * (length * Mathf.Clamp(catchLipAlong, 0.3f, 0.7f));
        return lip - transform.up * (length * catchLipBelly);
    }

    float VisualLength()
    {
        // World length of this instance. Scale already encodes the fish's size,
        // so use the prefab's authored inches — multiplying by Size.LengthInches
        // again made big bass hang below the hand.
        float authoredInches = Species != null ? Species.PrefabLengthInches : 19.5f;
        float meters = Mathf.Max(1f, authoredInches) * 0.0254f;
        if (prefabScale.x > 0.01f)
            meters *= transform.localScale.x / prefabScale.x;
        return Mathf.Max(0.22f, meters);
    }

    float CatchFloorY()
    {
        float y = angler != null ? angler.position.y : transform.position.y;
        if (angler == null)
            return y;

        var boat = angler.GetComponentInParent<BoatMotor>();
        if (boat == null)
            return y;

        var hull = boat.GetComponent<Collider>();
        if (hull != null)
            y = Mathf.Max(y, hull.bounds.max.y);
        return y;
    }

    void CacheCatchBones()
    {
        if (tailBones != null)
            return;

        Transform[] all = GetComponentsInChildren<Transform>(true);
        int count = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (IsTailBone(all[i].name))
                count++;
            else if (headBone == null && all[i].name == "Head")
                headBone = all[i];
        }

        tailBones = new Transform[count];
        tailRest = new Quaternion[count];
        int n = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (!IsTailBone(all[i].name))
                continue;
            tailBones[n] = all[i];
            n++;
        }

        for (int i = 0; i < tailBones.Length; i++)
        {
            int best = i;
            int bestDepth = BoneDepth(tailBones[i]);
            for (int j = i + 1; j < tailBones.Length; j++)
            {
                int d = BoneDepth(tailBones[j]);
                if (d >= bestDepth)
                    continue;
                best = j;
                bestDepth = d;
            }

            if (best == i)
                continue;
            Transform swap = tailBones[i];
            tailBones[i] = tailBones[best];
            tailBones[best] = swap;
        }

        for (int i = 0; i < tailBones.Length; i++)
            tailRest[i] = tailBones[i] != null ? tailBones[i].localRotation : Quaternion.identity;
    }

    static bool IsTailBone(string name)
    {
        return name.StartsWith("Tail", System.StringComparison.OrdinalIgnoreCase);
    }

    static int BoneDepth(Transform t)
    {
        int depth = 0;
        while (t != null)
        {
            depth++;
            t = t.parent;
        }
        return depth;
    }

    void ResetTailRest()
    {
        CacheCatchBones();
        if (tailBones == null || tailRest == null)
            return;
        for (int i = 0; i < tailBones.Length; i++)
        {
            if (tailBones[i] != null)
                tailBones[i].localRotation = tailRest[i];
        }
    }

    void PoseCatchTail()
    {
        if (tailBones == null || tailBones.Length == 0)
            return;

        Vector3 axis = CatchLiftAxis();
        float wag = Mathf.Sin(Time.time * 3.6f) * 5f;

        float floorY = CatchFloorY() + 0.05f;
        float lowest = LowestTailY();
        float need = Mathf.Max(0f, floorY - lowest);
        float bendTarget = Mathf.Clamp(need * 28f, 0f, 12f);
        tailFloorBend = Mathf.SmoothDamp(tailFloorBend, bendTarget, ref tailFloorBendVel, 0.22f);

        float combined = tailFloorBend + wag;
        for (int i = 0; i < tailBones.Length; i++)
        {
            if (tailBones[i] == null)
                continue;
            float w = (i + 1f) / tailBones.Length;
            tailBones[i].Rotate(axis, combined * w, Space.World);
        }
    }

    float LowestTailY()
    {
        float y = transform.position.y;
        if (tailBones == null || tailBones.Length == 0)
            return y - VisualLength() * 0.5f;

        for (int i = 0; i < tailBones.Length; i++)
        {
            if (tailBones[i] != null)
                y = Mathf.Min(y, tailBones[i].position.y);
        }

        return y - VisualLength() * 0.1f;
    }

    Vector3 CatchLiftAxis()
    {
        Vector3 along = HangDir();
        Vector3 axis = Vector3.Cross(along, Vector3.up);
        if (axis.sqrMagnitude < 0.0001f)
            axis = angler != null ? angler.forward : Vector3.forward;
        axis.Normalize();
        return axis;
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
        if (lake != null && !HasSwimRoom(lake.GroundDepthMeters(planar)))
        {
            planar.x = pos.x;
            planar.z = pos.z;
        }
        EaseFightDepth(ref planar, dt, heft);
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
            if (lake != null && (lake.GeometricDepthMeters(candidate) < 0.45f ||
                    !HasSwimRoom(lake.GroundDepthMeters(candidate))))
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
        if (lake != null && (lake.GeometricDepthMeters(jumpEnd) < 0.35f ||
                !HasSwimRoom(lake.GroundDepthMeters(jumpEnd))))
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
        fightDepthVel = 0f;
        EaseFightDepth(ref pos, dt, heft);
        transform.position = pos;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        Splash(pos, WaterRippleKind.Jump);
        SetAnimatorSpeed(Mathf.Lerp(1.6f, 2.2f, heft));
        PickSurge(false);
    }

    /// <summary>Climb toward the near-surface fight band instead of snapping there.</summary>
    void EaseFightDepth(ref Vector3 pos, float dt, float heft)
    {
        if (lake == null)
            return;

        float ground = lake.GroundDepthMeters(pos);
        float waterY = lake.SurfaceY;
        float targetBelow = Mathf.Clamp(DepthBelowSurface(ground, 0.35f), 0.16f, 1.15f);
        float targetY = waterY - targetBelow;

        float startBelow = waterY - swimY;
        float smoothTime = Mathf.Lerp(1.8f, 5.5f, Mathf.InverseLerp(1.2f, 16f, startBelow));
        smoothTime *= Mathf.Lerp(0.9f, 1.35f, heft);
        float maxRise = Mathf.Lerp(3.4f, 1.8f, heft);

        swimY = Mathf.SmoothDamp(swimY, targetY, ref fightDepthVel, smoothTime, maxRise, dt);
        pos.y = swimY;
        StayInWater(ref pos, ground);
        swimY = pos.y;
    }

    bool CloseEnoughToJump()
    {
        if (lake == null)
            return false;
        return lake.SurfaceY - transform.position.y <= 2.6f;
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
        return transform.position + transform.forward * Mathf.Max(0.18f, VisualLength() * 0.38f);
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
        if (animator == null || Mathf.Approximately(animator.speed, value))
            return;
        animator.speed = value;
    }

    void SetCatchAnimator(bool presenting)
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (animator == null)
            return;

        if (presenting)
        {
            if (animator.enabled)
            {
                animator.Rebind();
                animator.Update(0f);
            }
            animator.enabled = false;
            tailBones = null;
            tailFloorBend = 0f;
            tailFloorBendVel = 0f;
            CacheCatchBones();
            return;
        }

        animator.enabled = true;
        animator.speed = 1f;
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
            if (!HasSwimRoom(lake.GroundDepthMeters(candidate)))
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
        float ground = GroundDepth(pos);
        pos.y = ColumnY(ground);
        StayInWater(ref pos, ground);
        transform.position = pos;
    }

    /// <summary>Settle back to holding depth. A fish that rose for a lure swims down, it does not blink.</summary>
    void EaseToColumn()
    {
        if (lake == null)
            return;

        Vector3 pos = transform.position;
        float ground = GroundDepth(pos);
        StayInWater(ref pos, ground);
        pos.y = Mathf.MoveTowards(pos.y, ColumnY(ground), Mathf.Max(1f, speed * 2f) * Time.deltaTime);
        StayInWater(ref pos, ground);
        transform.position = pos;
    }

    void LiftOutOfGround()
    {
        if (lake == null)
            return;

        Vector3 pos = transform.position;
        StayInWater(ref pos, GroundDepth(pos));
        transform.position = pos;
    }

    float GroundDepth(Vector3 world)
    {
        if (lake == null)
            return 0f;
        if (Time.time - groundCacheTime < 0.12f
            && (world - groundCacheAt).sqrMagnitude < 0.36f)
            return groundCacheDepth;

        groundCacheAt = world;
        groundCacheTime = Time.time;
        groundCacheDepth = lake.GroundDepthMeters(world);
        return groundCacheDepth;
    }

    /// <summary>
    /// Rocks and timber are ground, but never above the waterline. A boulder
    /// that breaks the surface is treated as dry: stay in the wet water beside it.
    /// </summary>
    void StayInWater(ref Vector3 pos, float groundDepth)
    {
        if (lake == null)
            return;

        float maxY = lake.SurfaceY - MinBelowSurface;
        if (HasSwimRoom(groundDepth))
        {
            float floor = lake.SurfaceY - groundDepth + MinAboveGround;
            if (pos.y < floor)
                pos.y = floor;
        }

        if (pos.y > maxY)
            pos.y = maxY;
    }

    float ColumnY(float groundDepth)
    {
        float y = lake.SurfaceY - DepthBelowSurface(groundDepth, columnT);
        float maxY = lake.SurfaceY - MinBelowSurface;
        return y > maxY ? maxY : y;
    }

    public static bool HasSwimRoom(float groundDepth)
    {
        return groundDepth >= MinHoldDepthMeters;
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
        const float minAboveBed = MinAboveGround;
        const float minBelowSurface = MinBelowSurface;
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
