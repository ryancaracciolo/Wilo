using UnityEngine;

/// <summary>
/// Held BOKI rod. The blank is already modeled with a reel and line guides;
/// child markers sit in those rings so the cast line can thread them.
/// </summary>
public class FishingPole : MonoBehaviour
{
    public static readonly string PrefabPath = "Assets/BOKI/LowPolyNature/Prefabs/models/fishing_pole.prefab";

    /// <summary>Reel-seat hold, just above the handle cork.</summary>
    static readonly Vector3 GripLocal = new Vector3(0f, 0.38f, 0.04f);

    /// <summary>Left hand on the reel, next to the right-hand grip.</summary>
    static readonly Vector3 ForegripLocal = new Vector3(-0.015f, 0.355f, 0.09f);

    static readonly Vector3 ReelLocal = new Vector3(0f, 0.355f, 0.102f);
    static readonly Vector3 TipLocal = new Vector3(0f, 1.996f, 0.66f);

    /// <summary>Radius that still counts as bare blank, in mesh units.</summary>
    const float BlankRadius = 0.008f;

    /// <summary>Radius past which hardware keeps its authored size.</summary>
    const float HardwareRadius = 0.025f;

    static readonly Vector3[] GuideLocals =
    {
        new Vector3(0f, 0.70f, 0.056f),
        new Vector3(0f, 1.048f, 0.143f),
        new Vector3(0f, 1.372f, 0.264f),
        new Vector3(0f, 1.622f, 0.398f),
        new Vector3(0f, 1.821f, 0.531f),
        TipLocal
    };

    Transform grip;
    Transform foregrip;
    Transform tip;
    Transform[] path;
    Vector3[] markerRest;
    Animator animator;
    Transform player;
    Material rodMaterial;
    UnityEngine.Mesh coloredMesh;
    Vector3[] baseVerts;
    Vector3[] restVerts;
    Vector3[] bentVerts;
    Vector2[] centreline;
    float builtThickness = -1f;
    float holdWeight;
    float leftWeight;
    bool meshIsStraight = true;

    public Vector3 TipPosition => tip != null ? tip.position : transform.position;
    public Vector3 GripPosition => grip != null ? grip.position : transform.position;
    public int PathCount => path != null ? path.Length : 0;

    public struct Motion
    {
        public Vector3 AimPoint;
        public Vector3 HoldLocal;
        public float Scale;

        /// <summary>Blank fattening, applied to the mesh so scale stays uniform.</summary>
        public float Thickness;
        public float Pitch;
        public float Yaw;

        /// <summary>Loaded-cast direction as aim-frame weights: outboard, up, back.</summary>
        public Vector3 CastLean;
        public float BackCast;
        public float LeftHand;
        public float Bend;
    }

    public static FishingPole Spawn(GameObject prefab, Transform owner, Animator body)
    {
        if (prefab == null || owner == null)
            return null;

        GameObject go = Instantiate(prefab, owner);
        go.name = "FishingPole";
        var collider = go.GetComponent<Collider>();
        if (collider != null)
        {
            if (Application.isPlaying)
                Destroy(collider);
            else
                DestroyImmediate(collider);
        }

        var pole = go.GetComponent<FishingPole>() ?? go.AddComponent<FishingPole>();
        pole.player = owner;
        pole.animator = body;
        pole.BuildMarkers();
        pole.ApplyReadableMaterial();
        go.SetActive(false);
        return pole;
    }

    /// <param name="poseArms">
    /// Editor previews sample the sweep by posing the rod repeatedly; they skip
    /// the arm solve so the character is not dragged through every sample.
    /// </param>
    public void Tick(bool held, Motion motion, bool poseArms = true)
    {
        if (!Application.isPlaying)
        {
            holdWeight = held ? 1f : 0f;
            leftWeight = held ? motion.LeftHand : 0f;
        }
        else
        {
            holdWeight = Mathf.MoveTowards(holdWeight, held ? 1f : 0f, Time.deltaTime * 8f);
            leftWeight = Mathf.MoveTowards(leftWeight, held ? motion.LeftHand : 0f, Time.deltaTime * 10f);
        }

        bool show = holdWeight > 0.02f;
        if (gameObject.activeSelf != show)
            gameObject.SetActive(show);
        if (!show)
            return;

        float thickness = motion.Thickness > 0.01f ? motion.Thickness : 1f;
        if (!Mathf.Approximately(thickness, builtThickness))
            Thicken(thickness);

        transform.localScale = Vector3.one * motion.Scale;
        Align(motion);
        ApplyBend(motion.Bend, motion.AimPoint);
        if (poseArms)
            PoseArms();
    }

    public void PutAway()
    {
        holdWeight = 0f;
        leftWeight = 0f;
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    /// <summary>
    /// Reach the left hand out to a lip-hold. Returns a pinch just past the
    /// fingers so the bass jaw sits in the grip instead of inside the palm.
    /// </summary>
    public Vector3 PoseCatchHold(
        Vector3 holdPoint, Vector3 bodyDir, float weight,
        float pinchAlong = 0.03f, float pinchOut = 0.01f)
    {
        if (animator == null || player == null || weight <= 0.01f)
            return holdPoint;

        Transform leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (leftUpper == null || leftLower == null || leftHand == null)
            return holdPoint;

        if (bodyDir.sqrMagnitude < 0.0001f)
            bodyDir = Vector3.down;
        bodyDir.Normalize();

        Vector3 hint = holdPoint - player.right * 0.16f - player.up * 0.08f + player.forward * 0.04f;
        SolveTwoBone(leftUpper, leftLower, leftHand, holdPoint, hint, weight);
        AlignHand(leftHand, holdPoint, holdPoint + bodyDir, 1f, weight);

        Vector3 pinch = PinchPoint(leftHand, bodyDir, 1f, pinchAlong, pinchOut);
        Vector3 wrist = holdPoint - (pinch - leftHand.position);
        SolveTwoBone(leftUpper, leftLower, leftHand, wrist, hint, weight);
        AlignHand(leftHand, wrist, wrist + bodyDir, 1f, weight);
        return PinchPoint(leftHand, bodyDir, 1f, pinchAlong, pinchOut);
    }

    /// <summary>Right hand under the belly so a longer bass does not sag through the deck.</summary>
    public void PoseCatchSupport(Vector3 supportPoint, Vector3 bodyDir, float weight)
    {
        if (animator == null || player == null || weight <= 0.01f)
            return;

        Transform rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (rightUpper == null || rightLower == null || rightHand == null)
            return;

        if (bodyDir.sqrMagnitude < 0.0001f)
            bodyDir = Vector3.down;
        bodyDir.Normalize();

        Vector3 hint = supportPoint + player.right * 0.12f - player.up * 0.06f + player.forward * 0.08f;
        Vector3 towardCam = player.forward;
        towardCam.y = 0f;
        if (towardCam.sqrMagnitude > 0.0001f)
            towardCam.Normalize();
        else
            towardCam = player.forward;
        Vector3 wrist = supportPoint + towardCam * 0.05f;
        SolveTwoBone(rightUpper, rightLower, rightHand, wrist, hint, weight);
        AlignHand(rightHand, wrist, wrist + bodyDir, -1f, weight);
    }

    Vector3 PinchPoint(Transform hand, Vector3 bodyDir, float side, float alongDist, float outDist)
    {
        Vector3 along = bodyDir.sqrMagnitude > 0.0001f ? bodyDir.normalized : player.forward;
        Vector3 outward = Vector3.Cross(along, player.up);
        if (outward.sqrMagnitude < 0.0001f)
            outward = player.forward;
        outward.Normalize();
        if (Vector3.Dot(outward, player.forward) < 0f)
            outward = -outward;
        return hand.position + along * alongDist + outward * (outDist * side);
    }

    /// <summary>World-space blank polyline, grip through tip, following the bend.</summary>
    public void CopyBlank(System.Collections.Generic.List<Vector3> dest)
    {
        dest.Clear();
        dest.Add(GripPosition);
        if (path == null)
            return;

        for (int i = 1; i < path.Length; i++)
        {
            if (path[i] != null)
                dest.Add(path[i].position);
        }
    }

    public int CopyPath(Vector3[] dest)
    {
        if (path == null || dest == null)
            return 0;

        int count = Mathf.Min(path.Length, dest.Length);
        for (int i = 0; i < count; i++)
            dest[i] = path[i].position;
        return count;
    }

    void BuildMarkers()
    {
        grip = MakeMarker("Grip", GripLocal);
        foregrip = MakeMarker("Foregrip", ForegripLocal);
        var reel = MakeMarker("Reel", ReelLocal);
        path = new Transform[1 + GuideLocals.Length];
        path[0] = reel;
        for (int i = 0; i < GuideLocals.Length; i++)
        {
            bool last = i == GuideLocals.Length - 1;
            path[i + 1] = MakeMarker(last ? "Tip" : "Guide" + (i + 1), GuideLocals[i]);
        }

        tip = path[path.Length - 1];
        markerRest = new Vector3[path.Length];
        for (int i = 0; i < path.Length; i++)
            markerRest[i] = path[i].localPosition;
    }

    Transform MakeMarker(string name, Vector3 local)
    {
        var existing = transform.Find(name);
        if (existing != null)
        {
            existing.localPosition = local;
            existing.localRotation = Quaternion.identity;
            existing.localScale = Vector3.one;
            return existing;
        }

        var marker = new GameObject(name);
        marker.transform.SetParent(transform, false);
        marker.transform.localPosition = local;
        return marker.transform;
    }

    void Align(Motion motion)
    {
        Vector3 hold = player.TransformPoint(motion.HoldLocal);
        Vector3 to = motion.AimPoint - hold;
        to.y = 0f;
        if (to.sqrMagnitude < 0.04f)
            to = player.forward;
        to.Normalize();
        if (Mathf.Abs(motion.Yaw) > 0.01f)
            to = Quaternion.AngleAxis(motion.Yaw, Vector3.up) * to;

        Vector3 right = Vector3.Cross(Vector3.up, to);
        if (right.sqrMagnitude < 0.001f)
            right = player.right;
        right.Normalize();

        Vector3 forwardAlong = Quaternion.AngleAxis(-motion.Pitch, right) * to;

        // Sidearm through a high-outboard midpoint, not a two-point slerp. A
        // direct back-to-forward blend on this chibi goes over the crown and
        // through vertical, which spins LookRotation around the blank.
        Vector3 lean = motion.CastLean;
        if (lean.sqrMagnitude < 0.0001f)
            lean = new Vector3(0.95f, 0.12f, 0.5f);
        Vector3 backAlong = (right * lean.x + Vector3.up * lean.y - to * lean.z).normalized;
        Vector3 midAlong = (right * Mathf.Max(0.7f, lean.x * 0.9f) + Vector3.up * 0.18f + to * 0.28f).normalized;
        float back = Mathf.Clamp01(motion.BackCast);
        Vector3 along;
        Vector3 hang;
        if (back > 0.5f)
        {
            float t = (back - 0.5f) * 2f;
            along = Vector3.Slerp(midAlong, backAlong, t);
            hang = Vector3.Slerp(HangFor(midAlong), HangFor(backAlong), t);
        }
        else
        {
            float t = back * 2f;
            along = Vector3.Slerp(forwardAlong, midAlong, t);
            hang = Vector3.Slerp(HangFor(forwardAlong), HangFor(midAlong), t);
        }

        if (along.sqrMagnitude < 0.001f)
            along = forwardAlong;
        along.Normalize();
        hang = Vector3.ProjectOnPlane(hang, along);
        if (hang.sqrMagnitude < 0.001f)
            hang = HangFor(along);
        hang.Normalize();

        Vector3 meshAlong = (TipLocal - GripLocal).normalized;
        Vector3 meshReel = Vector3.ProjectOnPlane(Vector3.forward, meshAlong).normalized;
        Quaternion meshToWorld =
            Quaternion.LookRotation(along, hang) *
            Quaternion.Inverse(Quaternion.LookRotation(meshAlong, meshReel));

        transform.SetPositionAndRotation(
            hold - meshToWorld * Vector3.Scale(GripLocal, transform.lossyScale),
            meshToWorld);
    }

    Vector3 HangFor(Vector3 along)
    {
        Vector3 hang = Vector3.ProjectOnPlane(Vector3.down, along);
        if (hang.sqrMagnitude < 0.05f)
            hang = Vector3.ProjectOnPlane(-player.right, along);
        return hang.sqrMagnitude > 0.0001f ? hang.normalized : -player.right;
    }

    void ApplyBend(float amount, Vector3 aimPoint)
    {
        amount = Mathf.Clamp01(amount);
        Vector3 meshAlong = (TipLocal - GripLocal).normalized;
        float alongLen = (TipLocal - GripLocal).magnitude;
        Vector3 side = Vector3.zero;
        float deflect = 0f;
        if (amount > 0.001f && restVerts != null)
        {
            Vector3 restTip = transform.TransformPoint(TipLocal);
            Vector3 pull = aimPoint - restTip;
            Vector3 localPull = transform.InverseTransformDirection(pull);
            side = Vector3.ProjectOnPlane(localPull, meshAlong);
            if (side.sqrMagnitude < 0.0001f)
                side = Vector3.ProjectOnPlane(Vector3.forward, meshAlong);
            side.Normalize();
            deflect = 0.48f * amount;
        }

        bool straight = deflect <= 0.0001f;
        if (restVerts != null && bentVerts != null && coloredMesh != null && !(straight && meshIsStraight))
        {
            if (straight)
            {
                coloredMesh.vertices = restVerts;
            }
            else
            {
                for (int i = 0; i < restVerts.Length; i++)
                {
                    float t = Vector3.Dot(restVerts[i] - GripLocal, meshAlong) / alongLen;
                    t = Mathf.Clamp01(t);
                    bentVerts[i] = restVerts[i] + side * (deflect * t * t);
                }

                coloredMesh.vertices = bentVerts;
            }

            coloredMesh.RecalculateNormals();
            coloredMesh.RecalculateBounds();
            meshIsStraight = straight;
        }

        if (path == null || markerRest == null)
            return;

        for (int i = 0; i < path.Length; i++)
        {
            if (path[i] == null)
                continue;
            if (deflect <= 0.0001f)
            {
                path[i].localPosition = markerRest[i];
                continue;
            }

            float t = Vector3.Dot(markerRest[i] - GripLocal, meshAlong) / alongLen;
            t = Mathf.Clamp01(t);
            path[i].localPosition = markerRest[i] + side * (deflect * t * t);
        }
    }

    void PoseArms()
    {
        if (animator == null || grip == null)
            return;

        Transform rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (rightUpper == null)
            return;

        Vector3 rightHint = grip.position + player.right * 0.12f - player.up * 0.08f - player.forward * 0.04f;
        SolveTwoBone(rightUpper, rightLower, rightHand, grip.position, rightHint, holdWeight);
        AlignHand(rightHand, grip.position, tip != null ? tip.position : grip.position + player.forward, -1f, holdWeight);

        if (leftUpper == null || foregrip == null || leftWeight <= 0.01f)
            return;

        Vector3 leftHint = foregrip.position - player.right * 0.12f - player.up * 0.06f + player.forward * 0.02f;
        SolveTwoBone(leftUpper, leftLower, leftHand, foregrip.position, leftHint, leftWeight);
        AlignHand(leftHand, foregrip.position, tip != null ? tip.position : foregrip.position + player.forward, 1f, leftWeight);
    }

    static void SolveTwoBone(
        Transform upper, Transform lower, Transform end, Vector3 target, Vector3 hint, float weight)
    {
        if (upper == null || lower == null || end == null || weight <= 0.01f)
            return;

        Quaternion upperAnim = upper.rotation;
        Quaternion lowerAnim = lower.rotation;

        Vector3 root = upper.position;
        float upperLen = Vector3.Distance(root, lower.position);
        float lowerLen = Vector3.Distance(lower.position, end.position);
        Vector3 toTarget = target - root;
        float reach = Mathf.Clamp(toTarget.magnitude, 0.05f, upperLen + lowerLen - 0.02f);
        Vector3 dir = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : upper.forward;

        Vector3 axis = Vector3.Cross(dir, hint - root);
        if (axis.sqrMagnitude < 0.0001f)
            axis = Vector3.Cross(dir, Vector3.up);
        axis.Normalize();

        float cos = (upperLen * upperLen + reach * reach - lowerLen * lowerLen) / (2f * upperLen * reach);
        float bend = Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * Mathf.Rad2Deg;
        Vector3 elbow = root + Quaternion.AngleAxis(bend, axis) * dir * upperLen;

        Vector3 currentUpper = lower.position - root;
        if (currentUpper.sqrMagnitude > 0.00001f)
            upper.rotation = Quaternion.FromToRotation(currentUpper, elbow - root) * upper.rotation;

        Vector3 currentLower = end.position - lower.position;
        Vector3 wantedLower = target - lower.position;
        if (currentLower.sqrMagnitude > 0.00001f && wantedLower.sqrMagnitude > 0.00001f)
            lower.rotation = Quaternion.FromToRotation(currentLower, wantedLower) * lower.rotation;

        if (weight < 0.999f)
        {
            upper.rotation = Quaternion.Slerp(upperAnim, upper.rotation, weight);
            lower.rotation = Quaternion.Slerp(lowerAnim, lower.rotation, weight);
        }
    }

    void AlignHand(Transform hand, Vector3 hold, Vector3 toward, float side, float weight)
    {
        if (hand == null || weight <= 0.01f)
            return;

        Quaternion anim = hand.rotation;
        Vector3 along = toward - hold;
        if (along.sqrMagnitude < 0.0001f)
            along = player.forward;
        along.Normalize();

        Vector3 finger = (hand.TransformPoint(Vector3.right * side) - hand.position).normalized;
        if (finger.sqrMagnitude < 0.0001f)
            finger = hand.right * side;
        hand.rotation = Quaternion.FromToRotation(finger, along) * hand.rotation;
        if (weight < 0.999f)
            hand.rotation = Quaternion.Slerp(anim, hand.rotation, weight);
    }

    void ApplyReadableMaterial()
    {
        var filter = GetComponent<MeshFilter>();
        var meshRenderer = GetComponent<MeshRenderer>();
        if (filter == null || meshRenderer == null || filter.sharedMesh == null)
            return;

        coloredMesh = Instantiate(filter.sharedMesh);
        coloredMesh.name = "FishingPoleColored";
        Vector3[] verts;
        try
        {
            verts = coloredMesh.vertices;
        }
        catch (System.Exception)
        {
            verts = System.Array.Empty<Vector3>();
        }

        if (verts.Length == 0)
        {
            DestroyMesh(coloredMesh);
            coloredMesh = null;
            rodMaterial = MakeRodMaterial(meshRenderer.sharedMaterial);
            meshRenderer.sharedMaterial = rodMaterial;
            return;
        }
        var colors = new Color[verts.Length];
        Color cork = new Color(0.78f, 0.56f, 0.34f);
        Color reel = new Color(0.18f, 0.2f, 0.22f);
        Color blank = new Color(0.36f, 0.26f, 0.14f);
        Color eye = new Color(0.72f, 0.73f, 0.7f);
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 p = verts[i];
            float t = Mathf.InverseLerp(0f, 2f, p.y);
            float blankZ = Mathf.Lerp(0f, 0.66f, t);
            float radial = Mathf.Sqrt(p.x * p.x + (p.z - blankZ) * (p.z - blankZ));
            if (p.y < 0.28f)
                colors[i] = cork;
            else if (p.y < 0.46f && p.z > 0.055f)
                colors[i] = reel;
            else if (radial > 0.016f && p.y > 0.55f)
                colors[i] = eye;
            else
                colors[i] = blank;
        }

        coloredMesh.colors = colors;
        baseVerts = verts;
        bentVerts = new Vector3[verts.Length];
        filter.sharedMesh = coloredMesh;

        Shader shader = Shader.Find("Wilo/Lure");
        rodMaterial = shader != null
            ? new Material(shader)
            : MakeRodMaterial(meshRenderer.sharedMaterial);
        meshRenderer.sharedMaterial = rodMaterial;
    }

    /// <summary>
    /// Pushes blank vertices out from the rod's own centreline so a hair-thin
    /// import reads at chibi scale. Reel and guides keep their authored size.
    /// </summary>
    void Thicken(float thickness)
    {
        if (baseVerts == null || coloredMesh == null)
            return;

        const int Bins = 64;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        for (int i = 0; i < baseVerts.Length; i++)
        {
            minY = Mathf.Min(minY, baseVerts[i].y);
            maxY = Mathf.Max(maxY, baseVerts[i].y);
        }

        float span = Mathf.Max(0.0001f, maxY - minY);
        centreline ??= Centreline(baseVerts, minY, span, Bins);
        restVerts = new Vector3[baseVerts.Length];
        for (int i = 0; i < baseVerts.Length; i++)
        {
            Vector3 v = baseVerts[i];
            Vector2 axis = centreline[BinOf(v.y, minY, span, Bins)];
            var radial = new Vector2(v.x - axis.x, v.z - axis.y);
            float taper = 1f - Mathf.InverseLerp(BlankRadius, HardwareRadius, radial.magnitude);
            Vector2 pushed = axis + radial * Mathf.Lerp(1f, thickness, taper);
            restVerts[i] = new Vector3(pushed.x, v.y, pushed.y);
        }

        builtThickness = thickness;
        meshIsStraight = false;
    }

    /// <summary>
    /// Mean cross-section per height slice, retaken about the first mean so the
    /// reel housing does not drag the centreline off the blank.
    /// </summary>
    static Vector2[] Centreline(Vector3[] verts, float minY, float span, int bins)
    {
        var rough = new Vector2[bins];
        var roughCount = new int[bins];
        for (int i = 0; i < verts.Length; i++)
        {
            int b = BinOf(verts[i].y, minY, span, bins);
            rough[b] += new Vector2(verts[i].x, verts[i].z);
            roughCount[b]++;
        }

        for (int i = 0; i < bins; i++)
        {
            if (roughCount[i] > 0)
                rough[i] /= roughCount[i];
        }

        var trimmed = new Vector2[bins];
        var trimmedCount = new int[bins];
        for (int i = 0; i < verts.Length; i++)
        {
            int b = BinOf(verts[i].y, minY, span, bins);
            var flat = new Vector2(verts[i].x, verts[i].z);
            if (roughCount[b] == 0 || Vector2.Distance(flat, rough[b]) > HardwareRadius)
                continue;
            trimmed[b] += flat;
            trimmedCount[b]++;
        }

        for (int i = 0; i < bins; i++)
            trimmed[i] = trimmedCount[i] > 0 ? trimmed[i] / trimmedCount[i] : rough[i];
        return trimmed;
    }

    static int BinOf(float y, float minY, float span, int bins)
    {
        return Mathf.Clamp(Mathf.FloorToInt((y - minY) / span * bins), 0, bins - 1);
    }

    static Material MakeRodMaterial(Material source)
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Material mat;
        if (lit != null)
            mat = new Material(lit);
        else if (source != null)
            mat = new Material(source);
        else
            mat = new Material(Shader.Find("Sprites/Default"));

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", new Color(0.36f, 0.26f, 0.14f));
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", new Color(0.36f, 0.26f, 0.14f));
        return mat;
    }

    void OnDestroy()
    {
        DestroyMesh(coloredMesh);
        coloredMesh = null;
        baseVerts = null;
        restVerts = null;
        bentVerts = null;
        centreline = null;
        if (rodMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(rodMaterial);
            else
                DestroyImmediate(rodMaterial);
            rodMaterial = null;
        }
    }

    static void DestroyMesh(UnityEngine.Mesh mesh)
    {
        if (mesh == null)
            return;
        if (Application.isPlaying)
            Destroy(mesh);
        else
            DestroyImmediate(mesh);
    }
}
