using UnityEngine;

/// <summary>
/// Runtime stand-in mesh for a lure until real models exist.
/// </summary>
public class LurePlaceholder : MonoBehaviour
{
    Transform blade;
    float spinDegreesPerSecond;

    public void Apply(LureDefinition lure)
    {
        blade = null;
        spinDegreesPerSecond = 0f;
        ClearChildren();

        Color color = lure != null ? lure.Color : new Color(0.7f, 0.22f, 0.2f);
        LureKind kind = lure != null ? lure.Kind : LureKind.Worm;
        switch (kind)
        {
            case LureKind.Spinnerbait:
                BuildSpinner(color);
                break;
            case LureKind.Jig:
                BuildJig(color);
                break;
            default:
                BuildWorm(color);
                break;
        }
    }

    void Update()
    {
        if (blade != null)
            blade.Rotate(spinDegreesPerSecond * Time.deltaTime, 0f, 0f, Space.Self);
    }

    void BuildWorm(Color color)
    {
        AddPart(PrimitiveType.Sphere, Vector3.zero, new Vector3(0.1f, 0.1f, 0.38f), Quaternion.identity, color);
        AddPart(
            PrimitiveType.Sphere,
            new Vector3(0f, 0.01f, 0.14f),
            new Vector3(0.08f, 0.08f, 0.16f),
            Quaternion.identity,
            Color.Lerp(color, Color.black, 0.12f));
    }

    void BuildSpinner(Color color)
    {
        Color metal = new Color(0.86f, 0.84f, 0.72f);
        AddPart(PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.16f, Quaternion.identity, color);
        AddPart(
            PrimitiveType.Cube,
            new Vector3(0.1f, 0f, 0f),
            new Vector3(0.14f, 0.018f, 0.018f),
            Quaternion.identity,
            metal);

        var pivot = new GameObject("Blade");
        pivot.transform.SetParent(transform, false);
        pivot.transform.localPosition = new Vector3(0.18f, 0f, 0f);
        blade = pivot.transform;
        spinDegreesPerSecond = 720f;

        var disc = AddPart(
            PrimitiveType.Cylinder,
            Vector3.zero,
            new Vector3(0.11f, 0.007f, 0.045f),
            Quaternion.Euler(0f, 0f, 90f),
            metal);
        disc.transform.SetParent(blade, false);
        disc.transform.localPosition = Vector3.zero;
    }

    void BuildJig(Color color)
    {
        Color lead = Color.Lerp(color, new Color(0.22f, 0.21f, 0.2f), 0.62f);
        AddPart(PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.18f, Quaternion.identity, lead);
        AddPart(
            PrimitiveType.Sphere,
            new Vector3(0f, -0.1f, 0.02f),
            new Vector3(0.14f, 0.22f, 0.14f),
            Quaternion.identity,
            color);
    }

    GameObject AddPart(PrimitiveType type, Vector3 localPos, Vector3 localScale, Quaternion localRot, Color color)
    {
        var part = GameObject.CreatePrimitive(type);
        part.name = type.ToString();
        part.transform.SetParent(transform, false);
        part.transform.localPosition = localPos;
        part.transform.localRotation = localRot;
        part.transform.localScale = localScale;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        int ignore = LayerMask.NameToLayer("Ignore Raycast");
        if (ignore >= 0)
            part.layer = ignore;

        var renderer = part.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var mat = new Material(FindLitShader());
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", color);
        renderer.sharedMaterial = mat;
        return part;
    }

    void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
    }

    static Shader FindLitShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        return shader != null ? shader : Shader.Find("Sprites/Default");
    }
}
