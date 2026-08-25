using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class LakeMapElement : VisualElement
{
    readonly VisualElement mapImage;
    readonly VisualElement overlay;
    readonly List<CatchRecord> marked = new List<CatchRecord>();
    Texture2D mapTexture;
    Vector2 playerUv = new Vector2(0.5f, 0.5f);
    float playerYaw;
    bool hasPlayer;
    CatchRecord selected;
    float pinScale = 1f;
    bool panZoom;
    float viewZoom = 1f;
    Vector2 viewCenter = new Vector2(0.5f, 0.5f);
    bool dragging;
    bool dragMoved;
    Vector2 dragStart;
    Vector2 dragCenter;
    int dragPointer = -1;

    public event Action ExpandRequested;
    public event Action<IReadOnlyList<CatchRecord>> ClusterClicked;

    readonly List<MarkCluster> clusters = new List<MarkCluster>();

    public LakeMapElement()
    {
        AddToClassList("hud-map");
        pickingMode = PickingMode.Position;
        RegisterCallback<ClickEvent>(OnClicked);
        RegisterCallback<WheelEvent>(OnWheel);
        RegisterCallback<PointerDownEvent>(OnPointerDown);
        RegisterCallback<PointerMoveEvent>(OnPointerMove);
        RegisterCallback<PointerUpEvent>(OnPointerUp);
        RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        RegisterCallback<GeometryChangedEvent>(_ => ApplyView());

        mapImage = new VisualElement();
        mapImage.AddToClassList("hud-map-image");
        mapImage.pickingMode = PickingMode.Ignore;
        Add(mapImage);

        overlay = new VisualElement();
        overlay.AddToClassList("hud-map-overlay");
        overlay.pickingMode = PickingMode.Position;
        overlay.generateVisualContent += DrawOverlay;
        Add(overlay);
    }

    public void SetPinScale(float scale)
    {
        pinScale = Mathf.Max(0.6f, scale);
        overlay.MarkDirtyRepaint();
    }

    public void SetPanZoom(bool enabled)
    {
        panZoom = enabled;
        if (!enabled)
            ResetView();
    }

    public void ResetView()
    {
        viewZoom = 1f;
        viewCenter = new Vector2(0.5f, 0.5f);
        ApplyView();
    }

    public void Bake(float depthScale = 1f)
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null || terrain.terrainData == null)
            return;

        var water = GameObject.Find("Surface");
        float waterY = water != null && water.GetComponent<Renderer>() != null
            ? water.GetComponent<Renderer>().bounds.max.y
            : terrain.transform.position.y;
        float scale = depthScale > 0.05f ? depthScale : 1f;

        const int res = 256;
        if (mapTexture == null || mapTexture.width != res)
        {
            mapTexture = new Texture2D(res, res, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "WiloLakeMap"
            };
        }

        Vector3 origin = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        var pixels = new Color32[res * res];

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float u = x / (res - 1f);
                float v = y / (res - 1f);
                Vector3 world = origin + new Vector3(u * size.x, 0f, v * size.z);
                float ground = terrain.SampleHeight(world) + origin.y;
                pixels[y * res + x] = Shade((waterY - ground) * scale, ground, waterY);
            }
        }

        mapTexture.SetPixels32(pixels);
        mapTexture.Apply(false, false);
        mapImage.style.backgroundImage = new StyleBackground(mapTexture);
    }

    public void SetPlayer(Vector3 world, float yawDegrees)
    {
        if (!TryWorldToUv(world, out playerUv))
            return;
        playerYaw = yawDegrees;
        hasPlayer = true;
        overlay.MarkDirtyRepaint();
    }

    public void SetMarked(List<CatchRecord> records, CatchRecord selectedRecord)
    {
        marked.Clear();
        if (records != null)
            marked.AddRange(records);
        selected = selectedRecord;
        overlay.MarkDirtyRepaint();
    }

    public static bool TryWorldToUv(Vector3 world, out Vector2 uv)
    {
        uv = default;
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null || terrain.terrainData == null)
            return false;

        Vector3 origin = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        uv = new Vector2(
            Mathf.InverseLerp(origin.x, origin.x + size.x, world.x),
            Mathf.InverseLerp(origin.z, origin.z + size.z, world.z));
        return true;
    }

    static Color32 Shade(float depth, float ground, float waterY)
    {
        if (depth > 8f)
            return HudTheme.WaterDeep;
        if (depth > 3f)
            return Color.Lerp(HudTheme.WaterMid, HudTheme.WaterDeep, (depth - 3f) / 5f);
        if (depth > 0.35f)
            return Color.Lerp(HudTheme.WaterShallow, HudTheme.WaterMid, (depth - 0.35f) / 2.65f);
        if (depth > -0.15f)
            return HudTheme.Sand;

        float hill = Mathf.Clamp01((ground - waterY) / 18f);
        return Color.Lerp(HudTheme.Grass, HudTheme.Forest, hill);
    }

    void OnWheel(WheelEvent evt)
    {
        if (!panZoom)
            return;

        float factor = evt.delta.y < 0f ? 1.16f : 1f / 1.16f;
        ZoomAt(evt.localMousePosition, factor);
        evt.StopPropagation();
    }

    void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0)
            return;

        HudInput.NotifyUiPointerDown();
        evt.StopPropagation();

        if (!panZoom)
            return;

        dragging = true;
        dragMoved = false;
        dragPointer = evt.pointerId;
        dragStart = (Vector2)evt.localPosition;
        dragCenter = viewCenter;
    }

    void OnPointerMove(PointerMoveEvent evt)
    {
        if (!dragging || evt.pointerId != dragPointer)
            return;

        Vector2 delta = (Vector2)evt.localPosition - dragStart;
        if (!dragMoved && delta.sqrMagnitude > 16f)
        {
            dragMoved = true;
            this.CapturePointer(evt.pointerId);
        }

        if (!dragMoved)
            return;

        Rect rect = contentRect;
        if (rect.width < 2f || rect.height < 2f)
            return;

        viewCenter = dragCenter - new Vector2(
            delta.x / (rect.width * viewZoom),
            -delta.y / (rect.height * viewZoom));
        ClampCenter();
        ApplyView();
        evt.StopPropagation();
    }

    void OnPointerUp(PointerUpEvent evt)
    {
        if (evt.pointerId != dragPointer)
            return;

        bool moved = dragMoved;
        Vector2 local = (Vector2)evt.localPosition;
        EndDrag(evt.pointerId);

        if (panZoom && !moved)
            TryClickCluster(local);

        evt.StopPropagation();
    }

    void OnPointerCaptureOut(PointerCaptureOutEvent evt)
    {
        if (evt.pointerId == dragPointer)
            EndDrag(evt.pointerId);
    }

    void EndDrag(int pointerId)
    {
        if (this.HasPointerCapture(pointerId))
            this.ReleasePointer(pointerId);
        dragging = false;
        dragPointer = -1;
    }

    void OnClicked(ClickEvent evt)
    {
        if (panZoom || dragMoved)
        {
            dragMoved = false;
            evt.StopPropagation();
            return;
        }

        if (!TryClickCluster(this.WorldToLocal(evt.position)))
            ExpandRequested?.Invoke();
        evt.StopPropagation();
    }

    void ZoomAt(Vector2 local, float factor)
    {
        Rect rect = contentRect;
        if (rect.width < 2f || rect.height < 2f)
            return;

        Vector2 uv = WidgetToUv(local, rect);
        viewZoom = Mathf.Clamp(viewZoom * factor, 1f, 7f);
        viewCenter.x = uv.x - local.x / (rect.width * viewZoom) + 0.5f / viewZoom;
        viewCenter.y = uv.y + local.y / (rect.height * viewZoom) - 0.5f / viewZoom;
        ClampCenter();
        ApplyView();
    }

    Vector2 WidgetToUv(Vector2 local, Rect rect)
    {
        float z = viewZoom;
        float uvx = local.x / (rect.width * z) - 0.5f / z + viewCenter.x;
        float uvy = -local.y / (rect.height * z) + 0.5f / z + viewCenter.y;
        return new Vector2(uvx, uvy);
    }

    void ClampCenter()
    {
        float half = 0.5f / viewZoom;
        viewCenter.x = Mathf.Clamp(viewCenter.x, half, 1f - half);
        viewCenter.y = Mathf.Clamp(viewCenter.y, half, 1f - half);
    }

    void ApplyView()
    {
        Rect rect = contentRect;
        if (rect.width < 2f || rect.height < 2f)
            return;

        if (!panZoom || viewZoom <= 1.001f)
        {
            LayoutFill(mapImage);
            LayoutFill(overlay);
            overlay.MarkDirtyRepaint();
            return;
        }

        float z = viewZoom;
        float left = rect.width * (0.5f - viewCenter.x * z);
        float top = rect.height * (0.5f - (1f - viewCenter.y) * z);
        float width = rect.width * z;
        float height = rect.height * z;
        LayoutBox(mapImage, left, top, width, height);
        LayoutBox(overlay, left, top, width, height);
        overlay.MarkDirtyRepaint();
    }

    static void LayoutFill(VisualElement element)
    {
        element.style.left = 0;
        element.style.top = 0;
        element.style.right = 0;
        element.style.bottom = 0;
        element.style.width = StyleKeyword.Null;
        element.style.height = StyleKeyword.Null;
    }

    static void LayoutBox(VisualElement element, float left, float top, float width, float height)
    {
        element.style.right = StyleKeyword.Null;
        element.style.bottom = StyleKeyword.Null;
        element.style.left = left;
        element.style.top = top;
        element.style.width = width;
        element.style.height = height;
    }

    bool TryClickCluster(Vector2 widgetLocal)
    {
        BuildClusters(contentRect, overlaySpace: false);
        MarkCluster hit = HitCluster(widgetLocal);
        if (hit == null)
            return false;
        ClusterClicked?.Invoke(hit.Records);
        return true;
    }

    MarkCluster HitCluster(Vector2 widgetLocal)
    {
        float best = MergeRadius() + 4f * pinScale;
        MarkCluster found = null;
        for (int i = 0; i < clusters.Count; i++)
        {
            float dist = Vector2.Distance(widgetLocal, clusters[i].Pos);
            if (dist >= best)
                continue;
            best = dist;
            found = clusters[i];
        }

        return found;
    }

    float MergeRadius()
    {
        return 18f * pinScale;
    }

    void BuildClusters(Rect rect, bool overlaySpace)
    {
        clusters.Clear();
        if (rect.width < 2f || rect.height < 2f)
            return;

        float merge = MergeRadius();
        for (int i = 0; i < marked.Count; i++)
        {
            CatchRecord record = marked[i];
            if (record == null || !TryWorldToUv(record.WorldPosition, out Vector2 uv))
                continue;

            Vector2 pos = overlaySpace ? UvToOverlay(uv, rect) : UvToWidget(uv, rect);
            MarkCluster nearest = null;
            float best = merge;
            for (int c = 0; c < clusters.Count; c++)
            {
                float dist = Vector2.Distance(pos, clusters[c].Pos);
                if (dist >= best)
                    continue;
                best = dist;
                nearest = clusters[c];
            }

            if (nearest != null)
                nearest.Add(record, pos);
            else
                clusters.Add(new MarkCluster(record, pos));
        }
    }

    Vector2 UvToWidget(Vector2 uv, Rect rect)
    {
        float z = panZoom ? viewZoom : 1f;
        float x = (uv.x - viewCenter.x) * z * rect.width + rect.width * 0.5f;
        float y = (viewCenter.y - uv.y) * z * rect.height + rect.height * 0.5f;
        return new Vector2(x, y);
    }

    void DrawOverlay(MeshGenerationContext ctx)
    {
        Rect rect = overlay.contentRect;
        if (rect.width < 2f || rect.height < 2f)
            return;

        BuildClusters(rect, overlaySpace: true);
        var p = ctx.painter2D;
        for (int i = 0; i < clusters.Count; i++)
            DrawCluster(ctx, p, clusters[i]);

        if (hasPlayer)
            DrawPlayer(p, UvToOverlay(playerUv, rect));
    }

    static Vector2 UvToOverlay(Vector2 uv, Rect rect)
    {
        return new Vector2(
            Mathf.Lerp(rect.xMin, rect.xMax, uv.x),
            Mathf.Lerp(rect.yMax, rect.yMin, uv.y));
    }

    void DrawCluster(MeshGenerationContext ctx, Painter2D p, MarkCluster cluster)
    {
        bool on = cluster.Contains(selected);
        Vector2 pos = cluster.Pos;
        int n = cluster.Records.Count;
        CatchRecord front = cluster.Heaviest();
        Color fill = on ? HudTheme.Gold : front.LureColor;
        int stack = Mathf.Min(n, 3);

        for (int i = stack - 1; i >= 1; i--)
        {
            Vector2 back = pos + new Vector2(-3.2f, -3.2f) * i * pinScale;
            CatchRecord behind = cluster.Records[Mathf.Min(i, n - 1)];
            DrawDisc(p, back, 5.4f * pinScale, Color.white, behind.LureColor);
        }

        float size = (on || n > 1 ? 7.4f : 6.2f) * pinScale;
        DrawDisc(p, pos, size, Color.white, fill);

        if (on)
        {
            p.strokeColor = HudTheme.TealDeep;
            p.lineWidth = 2f * pinScale;
            p.BeginPath();
            p.Arc(pos, size + 3.4f * pinScale, 0f, 360f);
            p.Stroke();
        }

        if (n < 2)
            return;

        Vector2 badge = pos + new Vector2(6.4f, 6.4f) * pinScale;
        float badgeR = 7.2f * pinScale;
        p.fillColor = HudTheme.TealDeep;
        p.BeginPath();
        p.Arc(badge, badgeR, 0f, 360f);
        p.Fill();
        ctx.DrawText(
            n > 9 ? "9+" : n.ToString(),
            badge + new Vector2(-3.6f * pinScale, -6.2f * pinScale),
            11f * pinScale,
            Color.white,
            null);
    }

    static void DrawDisc(Painter2D p, Vector2 pos, float size, Color ring, Color fill)
    {
        p.fillColor = ring;
        p.BeginPath();
        p.Arc(pos, size + 2.2f, 0f, 360f);
        p.Fill();

        p.fillColor = fill;
        p.BeginPath();
        p.Arc(pos, size, 0f, 360f);
        p.Fill();
    }

    sealed class MarkCluster
    {
        public Vector2 Pos;
        public readonly List<CatchRecord> Records = new List<CatchRecord>();

        public MarkCluster(CatchRecord record, Vector2 pos)
        {
            Pos = pos;
            Records.Add(record);
        }

        public void Add(CatchRecord record, Vector2 pos)
        {
            int n = Records.Count;
            Pos = (Pos * n + pos) / (n + 1f);
            Records.Add(record);
        }

        public bool Contains(CatchRecord record)
        {
            return record != null && Records.Contains(record);
        }

        public CatchRecord Heaviest()
        {
            CatchRecord best = Records[0];
            for (int i = 1; i < Records.Count; i++)
            {
                if (Records[i].Pounds > best.Pounds)
                    best = Records[i];
            }

            return best;
        }
    }

    void DrawPlayer(Painter2D p, Vector2 pos)
    {
        float yawRad = playerYaw * Mathf.Deg2Rad;
        Vector2 forward = new Vector2(Mathf.Sin(yawRad), -Mathf.Cos(yawRad));
        Vector2 right = new Vector2(forward.y, -forward.x);
        float size = 8f * pinScale;

        p.fillColor = Color.white;
        p.BeginPath();
        p.Arc(pos, size + 2.4f, 0f, 360f);
        p.Fill();

        p.fillColor = HudTheme.PlayerPin;
        p.BeginPath();
        p.MoveTo(pos + forward * size);
        p.LineTo(pos - forward * size * 0.7f + right * size * 0.55f);
        p.LineTo(pos - forward * size * 0.35f);
        p.LineTo(pos - forward * size * 0.7f - right * size * 0.55f);
        p.ClosePath();
        p.Fill();
    }
}
