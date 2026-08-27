using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Runtime low-poly lure. Meshes are built from the equipped bait kind so the
/// in-water bait matches the tackle-box picture.
/// </summary>
public class LurePlaceholder : MonoBehaviour
{
    static Material sharedMat;

    Transform blade;
    float spinDegreesPerSecond;
    Mesh bodyMesh;
    Mesh bladeMesh;
    Vector3[] restVerts;
    Vector3[] wiggleVerts;
    Vector3 lastPos;
    bool wiggle;
    bool haveLastPos;

    public void Apply(LureDefinition lure)
    {
        blade = null;
        spinDegreesPerSecond = 0f;
        wiggle = false;
        restVerts = null;
        wiggleVerts = null;
        haveLastPos = false;
        ClearChildren();
        ReleaseMeshes();

        Color color = lure != null ? lure.Color : new Color(0.7f, 0.22f, 0.2f);
        LureKind kind = lure != null ? lure.Kind : LureKind.Worm;
        LureMeshBuilder.Result built = LureMeshBuilder.Build(kind, color);
        bodyMesh = built.Body;
        AddPart("Body", bodyMesh, Vector3.zero, Quaternion.identity);

        if (kind == LureKind.Worm)
        {
            wiggle = true;
            restVerts = bodyMesh.vertices;
            wiggleVerts = new Vector3[restVerts.Length];
        }

        if (built.Blade != null)
        {
            bladeMesh = built.Blade;
            var pivot = new GameObject("Blade");
            pivot.transform.SetParent(transform, false);
            pivot.transform.localPosition = built.BladeLocalPosition;
            blade = pivot.transform;
            spinDegreesPerSecond = built.BladeSpinDegreesPerSecond;
            AddPart("Blade", bladeMesh, Vector3.zero, Quaternion.identity).transform.SetParent(blade, false);
        }
    }

    void Update()
    {
        if (blade != null)
            blade.Rotate(0f, 0f, spinDegreesPerSecond * Time.deltaTime, Space.Self);

        if (wiggle && bodyMesh != null && restVerts != null)
            TickWiggle();
    }

    void TickWiggle()
    {
        float speed = 0f;
        if (haveLastPos && Time.deltaTime > 0.0001f)
            speed = Vector3.Distance(transform.position, lastPos) / Time.deltaTime;
        lastPos = transform.position;
        haveLastPos = true;

        float motion = Mathf.Clamp01(speed / 1.8f);
        float t = Time.time * 9f;
        for (int i = 0; i < restVerts.Length; i++)
        {
            Vector3 v = restVerts[i];
            float u = Mathf.Clamp(v.z / 0.20f, -1f, 1f);
            float ends = u * u;
            float wave = Mathf.Sin(t + u * 2.4f);
            v.y += wave * 0.026f * ends * Mathf.Lerp(0.12f, 1f, motion);
            v.x += Mathf.Sin(t * 0.65f + u * 1.7f) * 0.012f * ends * motion;
            wiggleVerts[i] = v;
        }

        bodyMesh.vertices = wiggleVerts;
        bodyMesh.RecalculateNormals();
        bodyMesh.RecalculateBounds();
    }

    void OnDestroy()
    {
        ReleaseMeshes();
    }

    GameObject AddPart(string name, Mesh mesh, Vector3 localPos, Quaternion localRot)
    {
        var part = new GameObject(name);
        part.transform.SetParent(transform, false);
        part.transform.localPosition = localPos;
        part.transform.localRotation = localRot;
        int ignore = LayerMask.NameToLayer("Ignore Raycast");
        if (ignore >= 0)
            part.layer = ignore;

        var filter = part.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = SharedMat();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return part;
    }

    void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
    }

    void ReleaseMeshes()
    {
        if (bodyMesh != null)
        {
            if (Application.isPlaying)
                Destroy(bodyMesh);
            else
                DestroyImmediate(bodyMesh);
            bodyMesh = null;
        }

        if (bladeMesh != null)
        {
            if (Application.isPlaying)
                Destroy(bladeMesh);
            else
                DestroyImmediate(bladeMesh);
            bladeMesh = null;
        }
    }

    static Material SharedMat()
    {
        if (sharedMat != null)
            return sharedMat;

        Shader shader = Shader.Find("Wilo/Lure");
        if (shader == null)
            shader = Shader.Find("HS_LowPoly/VertexColor");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        sharedMat = new Material(shader);
        sharedMat.name = "LureVertexColor";
        sharedMat.hideFlags = HideFlags.HideAndDontSave;
        if (sharedMat.HasProperty("_BaseColor"))
            sharedMat.SetColor("_BaseColor", Color.white);
        return sharedMat;
    }
}
