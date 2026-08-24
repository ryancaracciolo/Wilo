using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class MapSonarPanel : VisualElement
{
    public enum View
    {
        Map,
        Sonar
    }

    readonly Button mapTab;
    readonly Button sonarTab;
    readonly VisualElement tabRow;
    readonly LakeMapElement map;
    readonly SonarElement sonar;
    readonly Label sonarDepth;
    View view = View.Map;
    bool sonarAvailable;
    float sonarAccum;

    public event Action ExpandRequested;
    public event Action<IReadOnlyList<CatchRecord>> ClusterClicked;

    public MapSonarPanel()
    {
        AddToClassList("hud-gadget");

        tabRow = HudUi.Row();
        tabRow.AddToClassList("hud-tab-row");

        mapTab = MakeTab("Map", () => Show(View.Map));
        sonarTab = MakeTab("Sonar", () => Show(View.Sonar));
        tabRow.Add(mapTab);
        tabRow.Add(sonarTab);
        Add(tabRow);

        map = new LakeMapElement();
        map.ExpandRequested += () => ExpandRequested?.Invoke();
        map.ClusterClicked += cluster => ClusterClicked?.Invoke(cluster);
        sonar = new SonarElement();
        sonarDepth = new Label();
        sonarDepth.AddToClassList("hud-sonar-depth");
        sonarDepth.pickingMode = PickingMode.Ignore;

        var sonarWrap = new VisualElement();
        sonarWrap.AddToClassList("hud-sonar-wrap");
        sonarWrap.pickingMode = PickingMode.Position;
        sonarWrap.RegisterCallback<ClickEvent>(OnSonarClicked);
        sonarWrap.Add(sonar);
        sonarWrap.Add(sonarDepth);

        Add(map);
        Add(sonarWrap);

        Show(View.Map);
        SetSonarAvailable(false);
    }

    public void SetMarked(List<CatchRecord> records, CatchRecord selected)
    {
        map.SetMarked(records, selected);
    }

    public void BakeMap(float depthScale = 1f)
    {
        map.Bake(depthScale);
    }

    public void SetSonarAvailable(bool available)
    {
        bool boarded = available && !sonarAvailable;
        sonarAvailable = available;
        tabRow.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
        if (!available && view == View.Sonar)
            Show(View.Map);
        else if (boarded)
            Show(View.Sonar);
    }

    public void Toggle()
    {
        if (!sonarAvailable)
            return;
        Show(view == View.Map ? View.Sonar : View.Map);
    }

    public void Tick(WorldConditions conditions, float dt)
    {
        Transform player = conditions.PlayerTransform;
        if (player != null)
        {
            Transform marker = conditions.OnBoat && conditions.OccupiedBoat != null
                ? conditions.OccupiedBoat.transform
                : player;
            map.SetPlayer(marker.position, marker.eulerAngles.y);
        }

        if (!conditions.OnBoat)
            return;

        sonarAccum += dt;
        if (sonarAccum < 0.08f)
            return;
        sonarAccum = 0f;
        sonar.Push(conditions.DepthFeet);
        sonarDepth.text = $"{conditions.DepthFeet:0.0} ft";
    }

    void Show(View next)
    {
        view = next;
        bool mapOn = view == View.Map;
        map.style.display = mapOn ? DisplayStyle.Flex : DisplayStyle.None;
        sonar.parent.style.display = mapOn ? DisplayStyle.None : DisplayStyle.Flex;
        mapTab.EnableInClassList("hud-tab--on", mapOn);
        sonarTab.EnableInClassList("hud-tab--on", !mapOn);
    }

    void OnSonarClicked(ClickEvent evt)
    {
        ExpandRequested?.Invoke();
        evt.StopPropagation();
    }

    static Button MakeTab(string label, System.Action click)
    {
        var tab = new Button { text = label };
        tab.AddToClassList("hud-tab");
        tab.focusable = false;
        tab.clicked += click;
        return tab;
    }
}
