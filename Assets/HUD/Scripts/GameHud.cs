using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Runtime HUD shell. Layout regions (top-left icons, map gadget, bottom strip)
/// are owned here so later modes (fight, tournament, cabin) can show/hide panels
/// without rebuilding the tree.
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(UIDocument))]
public class GameHud : MonoBehaviour
{
    [SerializeField] StyleSheet styleSheet;
    [SerializeField] WorldConditions conditions;
    [SerializeField] DayCycle dayCycle;
    [SerializeField] TournamentDirector director;
    [SerializeField] PlayerProgress progress;
    [SerializeField] TackleBox tackle;

    UIDocument document;
    VisualElement root;
    VisualElement modalLayer;
    VisualElement modalCard;
    VisualElement popoverCatcher;
    VisualElement lurePopover;
    Button lureChip;
    VisualElement lureSwatch;
    Label lureName;
    MapSonarPanel mapSonar;
    FishFightPanel fightPanel;
    LureDepthPanel lureDepth;
    ConditionsStrip strip;
    Label noticeToast;
    Label tourneyChip;
    VisualElement dockPrompt;
    VisualElement fadeLayer;
    float noticeUntil;
    float fadeFrom;
    float fadeTarget;
    float fadeStart;
    float fadeSpan;
    PlayerFishing fishing;
    Label catchToast;
    VisualElement catchSheet;
    VisualElement catchCatcher;
    Button catchMark;
    CatchRecord shownCatch;
    CatchRecord selectedMarked;
    readonly List<CatchRecord> selectedCluster = new List<CatchRecord>();
    VisualElement mapJournalLayer;
    VisualElement mapJournalCard;
    VisualElement mapJournalDetail;
    LakeMapElement journalMap;
    readonly List<CatchRecord> markedScratch = new List<CatchRecord>();
    float markListScroll;
    float catchUntil;
    bool built;

    void Awake()
    {
        document = GetComponent<UIDocument>();
        if (conditions == null)
            conditions = GetComponent<WorldConditions>() ?? gameObject.AddComponent<WorldConditions>();
        if (dayCycle == null)
            dayCycle = GetComponent<DayCycle>() ?? gameObject.AddComponent<DayCycle>();
        if (director == null)
            director = GetComponent<TournamentDirector>() ?? gameObject.AddComponent<TournamentDirector>();
    }

    void OnEnable()
    {
        ResolvePlayerData();
        Build();
        if (tackle != null)
            tackle.Changed += RefreshLureChip;
        if (progress != null)
        {
            progress.Caught += ShowCatch;
            progress.MarkedChanged += RefreshMapMarks;
        }
        if (fishing != null)
            fishing.Escaped += ShowEscape;
        if (dayCycle != null)
        {
            dayCycle.Notice += ShowNotice;
            dayCycle.FadeRequested += RequestFade;
            dayCycle.Morning += ShowMorning;
        }
        if (director != null)
        {
            director.Notice += ShowNotice;
            director.BagChanged += RefreshTournamentChip;
            director.Finished += ShowTournamentResult;
        }
    }

    void OnDisable()
    {
        if (tackle != null)
            tackle.Changed -= RefreshLureChip;
        if (progress != null)
        {
            progress.Caught -= ShowCatch;
            progress.MarkedChanged -= RefreshMapMarks;
        }
        if (fishing != null)
            fishing.Escaped -= ShowEscape;
        if (dayCycle != null)
        {
            dayCycle.Notice -= ShowNotice;
            dayCycle.FadeRequested -= RequestFade;
            dayCycle.Morning -= ShowMorning;
        }
        if (director != null)
        {
            director.Notice -= ShowNotice;
            director.BagChanged -= RefreshTournamentChip;
            director.Finished -= ShowTournamentResult;
        }

        HudInput.Reset();
        shownCatch = null;
        built = false;
    }

    void Start()
    {
        if (!built)
            Build();
        if (fishing == null)
        {
            ResolvePlayerData();
            if (fishing != null)
                fishing.Escaped += ShowEscape;
        }
        mapSonar?.BakeMap(conditions != null ? conditions.GameplayDepthScale : 0.5f);
        journalMap?.Bake(conditions != null ? conditions.GameplayDepthScale : 0.5f);
        RefreshMapMarks();
    }

    void Update()
    {
        if (!built)
            return;

        HudInput.Root = root;
        HudInput.Panel = root?.panel;
        HudInput.Tick();
        strip.Refresh(conditions);
        mapSonar.SetSonarAvailable(conditions.OnBoat);
        mapSonar.Tick(conditions, Time.deltaTime);
        TickJournalMap();
        fightPanel?.Tick(fishing != null ? fishing.Fight : null);
        lureDepth?.Tick(fishing);
        if (catchToast != null && Time.unscaledTime >= catchUntil)
            catchToast.style.display = DisplayStyle.None;
        if (noticeToast != null && Time.unscaledTime >= noticeUntil)
            noticeToast.style.display = DisplayStyle.None;

        TickDayCycle();

        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            if (CatchSheetOpen)
                ContinueCatch();
            else
                CloseAllOverlays();
        }
        else if (keyboard.tabKey.wasPressedThisFrame && !HudInput.PopupOpen)
            mapSonar.Toggle();
        else if (keyboard.enterKey.wasPressedThisFrame && TurnInAvailable)
            dayCycle.TurnIn(false);
    }

    bool TurnInAvailable => dayCycle != null && dayCycle.CanTurnIn && !HudInput.PopupOpen;

    void TickDayCycle()
    {
        bool turning = dayCycle != null && dayCycle.IsTurningIn;
        if (conditions != null)
            conditions.HoldClock = HudInput.PopupOpen || turning;

        if (dockPrompt != null)
        {
            bool show = TurnInAvailable && !turning;
            dockPrompt.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        TickFade();
    }

    void TickFade()
    {
        if (fadeLayer == null)
            return;

        float t = fadeSpan <= 0.001f
            ? 1f
            : Mathf.Clamp01((Time.unscaledTime - fadeStart) / fadeSpan);
        float alpha = Mathf.Lerp(fadeFrom, fadeTarget, t * t * (3f - 2f * t));
        fadeLayer.style.opacity = alpha;
        fadeLayer.style.display = alpha > 0.001f ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void RequestFade(float target, float duration)
    {
        if (fadeLayer == null)
            return;

        fadeFrom = fadeLayer.resolvedStyle.opacity;
        fadeTarget = Mathf.Clamp01(target);
        fadeStart = Time.unscaledTime;
        fadeSpan = Mathf.Max(0f, duration);
        if (fadeTarget > 0.001f)
        {
            fadeLayer.style.display = DisplayStyle.Flex;
            fadeLayer.BringToFront();
        }
    }

    void ShowNotice(string message)
    {
        if (noticeToast == null || string.IsNullOrEmpty(message))
            return;

        noticeToast.text = message;
        noticeToast.style.display = DisplayStyle.Flex;
        noticeToast.BringToFront();
        noticeUntil = Time.unscaledTime + 4.5f;
    }

    void ShowMorning(DayReport report)
    {
        string season = string.IsNullOrEmpty(report.SeasonLabel)
            ? ""
            : report.SeasonLabel.ToLowerInvariant();
        string opener = report.Forced ? "You drifted home after dark." : "Good morning.";
        ShowNotice(JoinFacts(opener, report.DateLabel, season));
    }

    void ResolvePlayerData()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return;
        if (progress == null)
            progress = player.GetComponent<PlayerProgress>() ?? player.AddComponent<PlayerProgress>();
        if (tackle == null)
            tackle = player.GetComponent<TackleBox>() ?? player.AddComponent<TackleBox>();
        if (fishing == null)
            fishing = player.GetComponent<PlayerFishing>();
    }

    void Build()
    {
        if (document == null)
            document = GetComponent<UIDocument>();
        root = document.rootVisualElement;
        if (root == null)
            return;

        root.Clear();
        root.AddToClassList("hud-root");
        root.pickingMode = PickingMode.Ignore;
        HudInput.Root = root;
        HudInput.Panel = root.panel;
        BindHudPointer(root);
        if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
            root.styleSheets.Add(styleSheet);

        var topLeft = new VisualElement();
        topLeft.AddToClassList("hud-top-left");
        topLeft.Add(HudUi.IconButton("Profile", HudUi.PaintProfile, OpenProfile));
        topLeft.Add(HudUi.IconButton("Tournaments", HudUi.PaintTrophy, OpenTournaments));
        root.Add(topLeft);

        var topRight = new VisualElement();
        topRight.AddToClassList("hud-top-right");
        mapSonar = new MapSonarPanel();
        mapSonar.ExpandRequested += OpenMapJournal;
        mapSonar.ClusterClicked += OnMiniMapClusterClicked;
        topRight.Add(mapSonar);
        root.Add(topRight);

        var bottom = new VisualElement();
        bottom.AddToClassList("hud-bottom");
        strip = new ConditionsStrip();
        bottom.Add(strip);
        bottom.Add(BuildLureChip());
        root.Add(bottom);

        catchToast = new Label();
        catchToast.AddToClassList("hud-catch-toast");
        catchToast.pickingMode = PickingMode.Ignore;
        catchToast.style.display = DisplayStyle.None;
        root.Add(catchToast);

        noticeToast = new Label();
        noticeToast.AddToClassList("hud-notice-toast");
        noticeToast.pickingMode = PickingMode.Ignore;
        noticeToast.style.display = DisplayStyle.None;
        root.Add(noticeToast);

        tourneyChip = new Label();
        tourneyChip.AddToClassList("hud-tourney-chip");
        tourneyChip.pickingMode = PickingMode.Ignore;
        tourneyChip.style.display = DisplayStyle.None;
        root.Add(tourneyChip);

        dockPrompt = BuildDockPrompt();
        root.Add(dockPrompt);

        catchSheet = new VisualElement();
        catchSheet.AddToClassList("hud-catch-sheet");
        catchSheet.pickingMode = PickingMode.Position;
        catchSheet.style.display = DisplayStyle.None;
        catchSheet.RegisterCallback<ClickEvent>(_ => ContinueCatch());
        root.Add(catchSheet);

        catchCatcher = new VisualElement();
        catchCatcher.AddToClassList("hud-catch-catcher");
        catchCatcher.pickingMode = PickingMode.Position;
        catchCatcher.style.display = DisplayStyle.None;
        catchCatcher.RegisterCallback<ClickEvent>(_ => ContinueCatch());
        root.Add(catchCatcher);

        fightPanel = new FishFightPanel();
        root.Add(fightPanel);

        lureDepth = new LureDepthPanel();
        root.Add(lureDepth);

        modalLayer = new VisualElement();
        modalLayer.AddToClassList("hud-modal");
        modalLayer.style.display = DisplayStyle.None;
        modalLayer.RegisterCallback<ClickEvent>(OnModalBackgroundClicked);
        modalCard = new VisualElement();
        modalCard.AddToClassList("hud-card");
        modalCard.pickingMode = PickingMode.Position;
        modalLayer.Add(modalCard);
        root.Add(modalLayer);

        popoverCatcher = new VisualElement();
        popoverCatcher.AddToClassList("hud-popover-catcher");
        popoverCatcher.style.display = DisplayStyle.None;
        popoverCatcher.RegisterCallback<ClickEvent>(_ => CloseLurePicker());
        root.Add(popoverCatcher);

        lurePopover = new VisualElement();
        lurePopover.AddToClassList("hud-lure-popover");
        lurePopover.style.display = DisplayStyle.None;
        root.Add(lurePopover);

        BuildMapJournal();

        fadeLayer = new VisualElement();
        fadeLayer.AddToClassList("hud-fade");
        fadeLayer.pickingMode = PickingMode.Ignore;
        fadeLayer.style.display = DisplayStyle.None;
        fadeLayer.style.opacity = 0f;
        root.Add(fadeLayer);

        RefreshLureChip();
        RefreshTournamentChip();
        built = true;
    }

    VisualElement BuildDockPrompt()
    {
        var prompt = new VisualElement();
        prompt.AddToClassList("hud-dock-prompt");
        prompt.style.display = DisplayStyle.None;
        prompt.RegisterCallback<ClickEvent>(_ =>
        {
            if (TurnInAvailable)
                dayCycle.TurnIn(false);
        });

        var title = HudUi.Body("Turn in for the night");
        title.pickingMode = PickingMode.Ignore;
        prompt.Add(title);

        var hint = HudUi.Muted("Enter");
        hint.pickingMode = PickingMode.Ignore;
        prompt.Add(hint);
        return prompt;
    }

    void BindHudPointer(VisualElement hudRoot)
    {
        hudRoot.UnregisterCallback<PointerDownEvent>(OnHudPointerDown, TrickleDown.TrickleDown);
        hudRoot.UnregisterCallback<PointerUpEvent>(OnHudPointerUp, TrickleDown.TrickleDown);
        hudRoot.RegisterCallback<PointerDownEvent>(OnHudPointerDown, TrickleDown.TrickleDown);
        hudRoot.RegisterCallback<PointerUpEvent>(OnHudPointerUp, TrickleDown.TrickleDown);
    }

    static void OnHudPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0)
            return;
        HudInput.NotifyUiPointerDown();
    }

    static void OnHudPointerUp(PointerUpEvent evt)
    {
        if (evt.button != 0)
            return;
        HudInput.NotifyUiPointerUp();
    }

    void BuildMapJournal()
    {
        mapJournalLayer = new VisualElement();
        mapJournalLayer.AddToClassList("hud-modal");
        mapJournalLayer.pickingMode = PickingMode.Ignore;
        mapJournalLayer.style.display = DisplayStyle.None;
        mapJournalLayer.RegisterCallback<ClickEvent>(OnJournalBackgroundClicked);

        mapJournalCard = new VisualElement();
        mapJournalCard.AddToClassList("hud-map-journal");
        mapJournalCard.pickingMode = PickingMode.Position;

        var mapWrap = new VisualElement();
        mapWrap.AddToClassList("hud-map-journal-map");
        journalMap = new LakeMapElement();
        journalMap.SetPinScale(1.35f);
        journalMap.SetPanZoom(true);
        journalMap.ClusterClicked += OnJournalClusterClicked;
        mapWrap.Add(journalMap);
        mapJournalCard.Add(mapWrap);

        mapJournalDetail = new VisualElement();
        mapJournalDetail.AddToClassList("hud-map-journal-detail");
        mapJournalCard.Add(mapJournalDetail);

        mapJournalLayer.Add(mapJournalCard);
        root.Add(mapJournalLayer);
        FillJournalDetail();
    }

    VisualElement BuildLureChip()
    {
        lureChip = new Button();
        lureChip.AddToClassList("hud-lure-chip");
        lureChip.focusable = false;
        lureChip.clicked += ToggleLurePicker;

        lureSwatch = new VisualElement();
        lureSwatch.AddToClassList("hud-lure-swatch");
        lureSwatch.pickingMode = PickingMode.Ignore;
        lureName = new Label();
        lureName.AddToClassList("hud-lure-name");
        lureName.pickingMode = PickingMode.Ignore;
        lureChip.Add(lureSwatch);
        lureChip.Add(lureName);
        return lureChip;
    }

    void RefreshLureChip()
    {
        if (lureName == null)
            return;
        LureDefinition lure = tackle != null ? tackle.Equipped : null;
        lureName.text = lure != null ? lure.DisplayName : "Lure";
        lureSwatch.style.backgroundColor = lure != null ? lure.Color : HudTheme.Teal;
        lureChip.tooltip = lure != null ? lure.Hint : "";
    }

    void ShowCatch(CatchRecord record)
    {
        if (catchToast != null)
            catchToast.style.display = DisplayStyle.None;

        shownCatch = record;
        if (catchSheet == null || record == null)
            return;

        CloseLurePicker();
        if (modalLayer != null)
            modalLayer.style.display = DisplayStyle.None;

        catchSheet.Clear();
        catchSheet.Add(HudUi.Muted(record.PersonalBest ? "Personal best!" : "Nice one"));
        catchSheet.Add(HudUi.Title(record.SpeciesName));

        var size = HudUi.Body($"{record.Pounds:0.00} lb   ·   {record.LengthInches:0.0} in");
        size.AddToClassList("hud-catch-size");
        catchSheet.Add(size);

        var lureRow = new VisualElement();
        lureRow.AddToClassList("hud-catch-lure");
        var swatch = new VisualElement();
        swatch.AddToClassList("hud-lure-swatch");
        swatch.style.backgroundColor = record.LureColor;
        swatch.pickingMode = PickingMode.Ignore;
        lureRow.Add(swatch);
        lureRow.Add(HudUi.Body($"Caught on {record.LureName}"));
        catchSheet.Add(lureRow);

        var facts = new VisualElement();
        facts.AddToClassList("hud-section");
        facts.Add(HudUi.Muted("Spot"));
        if (record.DepthFeet > 0.05f)
            facts.Add(HudUi.Body($"{record.DepthFeet:0.0} ft"));
        string when = JoinFacts(record.TimeLabel, record.WeatherLabel);
        if (when.Length > 0)
            facts.Add(HudUi.Body(when));
        if (record.WaterTempF > 1f)
            facts.Add(HudUi.Body($"{record.WaterTempF:0}° water"));
        catchSheet.Add(facts);

        var actions = new VisualElement();
        actions.AddToClassList("hud-catch-actions");
        catchMark = HudUi.TextButton(record.Marked ? "Marked" : "Mark fish", MarkShownCatch);
        catchMark.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
        actions.Add(catchMark);
        var continueButton = HudUi.TextButton("Continue", ContinueCatch, true);
        continueButton.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
        actions.Add(continueButton);
        catchSheet.Add(actions);

        catchCatcher.style.display = DisplayStyle.Flex;
        catchSheet.style.display = DisplayStyle.Flex;
        catchCatcher.BringToFront();
        catchSheet.BringToFront();
        HudInput.PopupOpen = true;
    }

    static string JoinFacts(params string[] parts)
    {
        var bits = new System.Collections.Generic.List<string>();
        for (int i = 0; i < parts.Length; i++)
        {
            if (!string.IsNullOrEmpty(parts[i]))
                bits.Add(parts[i]);
        }

        return string.Join("  ·  ", bits);
    }

    static string SpeciesShort(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "Bass";
        int space = name.IndexOf(' ');
        return space > 0 ? name.Substring(0, space) : name;
    }

    bool CatchSheetOpen => catchSheet != null && catchSheet.style.display == DisplayStyle.Flex;

    void MarkShownCatch()
    {
        if (shownCatch == null || shownCatch.Marked)
            return;

        progress?.MarkCatch(shownCatch);
        if (catchMark != null)
            catchMark.text = "Marked";
        RefreshMapMarks();
    }

    void ContinueCatch()
    {
        if (!CatchSheetOpen)
            return;
        if (catchSheet != null)
            catchSheet.style.display = DisplayStyle.None;
        if (catchCatcher != null)
            catchCatcher.style.display = DisplayStyle.None;
        shownCatch = null;
        catchMark = null;
        HudInput.PopupOpen = false;
        fishing?.DismissCatch();
    }

    void ShowEscape()
    {
        if (catchToast == null)
            return;
        catchToast.text = "It got away!";
        catchToast.style.display = DisplayStyle.Flex;
        catchUntil = Time.unscaledTime + 2.4f;
    }

    void OpenProfile()
    {
        if (CatchSheetOpen || MapJournalOpen)
            return;
        CloseLurePicker();
        modalCard.Clear();
        AddCardHeader("Profile", CloseAllOverlays);

        string money = progress != null ? $"${progress.Money}" : "$0";
        string name = progress != null ? progress.DisplayName : "You";
        modalCard.Add(HudUi.Title(name));
        modalCard.Add(HudUi.Body(money));

        var pb = new VisualElement();
        pb.AddToClassList("hud-section");
        pb.Add(HudUi.Muted("Personal best"));
        if (progress != null && progress.HasPersonalBest)
            pb.Add(HudUi.Body($"{progress.BestSpecies}  ·  {progress.BestBassPounds:0.00} lb"));
        else
            pb.Add(HudUi.Body("No trophy yet. The lake is waiting."));
        modalCard.Add(pb);

        var history = new VisualElement();
        history.AddToClassList("hud-section");
        history.Add(HudUi.Muted("Past tournaments"));
        if (director == null || director.History.Count == 0)
        {
            history.Add(HudUi.Body("No results yet. The Saturday Open is free to enter."));
        }
        else
        {
            int wins = 0;
            foreach (TournamentResult past in director.History)
            {
                if (past.Won)
                    wins++;
            }

            history.Add(HudUi.Body(wins == 1 ? "1 win" : $"{wins} wins"));
            int shown = 0;
            foreach (TournamentResult past in director.History)
            {
                if (shown++ >= 5)
                    break;
                history.Add(HudUi.Muted(
                    $"{past.DisplayName}  ·  {past.PlaceLabel}  ·  {past.Pounds:0.00} lb" +
                    (past.Payout > 0 ? $"  ·  ${past.Payout}" : "")));
            }
        }

        modalCard.Add(history);
        ShowModal();
    }

    void OpenTournaments()
    {
        if (CatchSheetOpen || MapJournalOpen)
            return;
        CloseLurePicker();
        modalCard.Clear();
        AddCardHeader("Tournaments", CloseAllOverlays);

        if (director == null)
        {
            modalCard.Add(HudUi.Body("Nothing on the calendar just now."));
            ShowModal();
            return;
        }

        if (director.Phase != TournamentPhase.Idle)
        {
            var live = new VisualElement();
            live.AddToClassList("hud-section");
            live.Add(HudUi.Muted("On the water now"));
            live.Add(HudUi.Body(director.StatusLine));
            modalCard.Add(live);
        }

        modalCard.Add(HudUi.Muted("Upcoming on Wilo Lake"));
        IReadOnlyList<TournamentOccurrence> schedule = director.Upcoming;
        if (schedule.Count == 0)
        {
            modalCard.Add(HudUi.Body("Nothing on the calendar just now."));
            ShowModal();
            return;
        }

        for (int i = 0; i < schedule.Count; i++)
            modalCard.Add(MakeTournamentRow(schedule[i]));

        ShowModal();
    }

    VisualElement MakeTournamentRow(TournamentOccurrence occurrence)
    {
        TournamentDefinition def = occurrence.Definition;
        var row = new VisualElement();
        row.AddToClassList("hud-section");
        row.AddToClassList("hud-tourney-row");
        row.Add(HudUi.Body(def.DisplayName));

        string when = conditions != null
            ? TournamentSchedule.WhenLabel(conditions.Calendar, occurrence)
            : def.Weekday.ToString();
        row.Add(HudUi.Muted($"{when}  ·  {def.FormatLabel}  ·  {def.EntryLabel}"));
        row.Add(HudUi.Muted($"Purse ${def.PayoutFor(1)} to win  ·  {def.FieldSize + 1} anglers"));

        bool registered = director.IsRegistered(occurrence);
        if (registered)
        {
            row.Add(HudUi.TextButton("Withdraw", () =>
            {
                director.Withdraw(occurrence);
                OpenTournaments();
            }));
        }
        else if (director.CanRegister(occurrence))
        {
            row.Add(HudUi.TextButton(def.EntryFee > 0 ? $"Enter · ${def.EntryFee}" : "Enter", () =>
            {
                director.Register(occurrence);
                OpenTournaments();
            }, true));
        }
        else if (!director.AffordableFee(def))
        {
            row.Add(HudUi.Muted("Not enough money"));
        }

        return row;
    }

    void ShowTournamentResult(TournamentResult result)
    {
        if (modalCard == null || result == null)
            return;

        CloseLurePicker();
        modalCard.Clear();
        AddCardHeader(result.DisplayName, CloseAllOverlays);

        if (result.Forfeited)
        {
            modalCard.Add(HudUi.Title("Missed the weigh-in"));
            modalCard.Add(HudUi.Body("The scales closed before you got back."));
        }
        else
        {
            modalCard.Add(HudUi.Title(result.Won ? "You won!" : result.PlaceLabel));
            modalCard.Add(HudUi.Body($"{result.Pounds:0.00} lb  ·  {result.Fish} fish"));
        }

        if (result.Penalty > 0.001f)
            modalCard.Add(HudUi.Muted($"Late penalty −{result.Penalty:0.00} lb from {result.RawPounds:0.00} lb"));

        var purse = new VisualElement();
        purse.AddToClassList("hud-section");
        purse.Add(HudUi.Muted("Purse"));
        purse.Add(HudUi.Body(result.Payout > 0 ? $"${result.Payout}" : "No payout"));
        if (result.EntryFee > 0)
            purse.Add(HudUi.Muted($"Entry was ${result.EntryFee}  ·  net {(result.Net >= 0 ? "+" : "−")}${Mathf.Abs(result.Net)}"));
        modalCard.Add(purse);

        if (!string.IsNullOrEmpty(result.WinnerName))
        {
            var top = new VisualElement();
            top.AddToClassList("hud-section");
            top.Add(HudUi.Muted("Big bag"));
            top.Add(HudUi.Body($"{result.WinnerName}  ·  {result.WinnerPounds:0.00} lb"));
            modalCard.Add(top);
        }

        ShowModal();
    }

    void RefreshTournamentChip()
    {
        if (tourneyChip == null)
            return;

        string status = director != null ? director.StatusLine : "";
        tourneyChip.text = status;
        tourneyChip.style.display = string.IsNullOrEmpty(status) ? DisplayStyle.None : DisplayStyle.Flex;
    }

    void ToggleLurePicker()
    {
        if (CatchSheetOpen || MapJournalOpen)
            return;
        if (lurePopover.style.display == DisplayStyle.Flex)
        {
            CloseLurePicker();
            return;
        }

        CloseAllOverlays();
        lurePopover.Clear();
        lurePopover.Add(HudUi.Muted("Tied on"));

        if (tackle != null)
        {
            foreach (LureDefinition lure in tackle.Lures)
            {
                var option = new Button();
                option.AddToClassList("hud-lure-option");
                if (lure == tackle.Equipped)
                    option.AddToClassList("hud-lure-option--on");
                option.focusable = false;

                var swatch = new VisualElement();
                swatch.AddToClassList("hud-lure-swatch");
                swatch.style.backgroundColor = lure.Color;
                swatch.pickingMode = PickingMode.Ignore;
                var label = new Label(lure.DisplayName);
                label.AddToClassList("hud-lure-name");
                label.pickingMode = PickingMode.Ignore;
                option.Add(swatch);
                option.Add(label);
                LureDefinition chosen = lure;
                option.clicked += () =>
                {
                    tackle.Equip(chosen);
                    CloseLurePicker();
                };
                lurePopover.Add(option);
            }
        }

        lurePopover.style.display = DisplayStyle.Flex;
        popoverCatcher.style.display = DisplayStyle.Flex;
        lurePopover.RegisterCallback<GeometryChangedEvent>(PlaceLurePopover);
        HudInput.PopupOpen = false;
    }

    void PlaceLurePopover(GeometryChangedEvent _)
    {
        lurePopover.UnregisterCallback<GeometryChangedEvent>(PlaceLurePopover);
        if (lureChip == null)
            return;

        Rect chip = lureChip.worldBound;
        Rect panel = root.worldBound;
        lurePopover.style.right = panel.xMax - chip.xMax;
        lurePopover.style.bottom = panel.yMax - chip.yMin + 8f;
    }

    void CloseLurePicker()
    {
        if (lurePopover != null)
            lurePopover.style.display = DisplayStyle.None;
        if (popoverCatcher != null)
            popoverCatcher.style.display = DisplayStyle.None;
    }

    void AddCardHeader(string title, System.Action close)
    {
        var header = new VisualElement();
        header.AddToClassList("hud-card-header");
        header.Add(HudUi.Title(title));
        var x = new Button { text = "✕" };
        x.AddToClassList("hud-close");
        x.focusable = false;
        x.clicked += close;
        header.Add(x);
        modalCard.Add(header);
    }

    void OnModalBackgroundClicked(ClickEvent evt)
    {
        if (evt.target == modalLayer)
            CloseAllOverlays();
    }

    void ShowModal()
    {
        modalLayer.style.display = DisplayStyle.Flex;
        HudInput.PopupOpen = true;
    }

    void CloseAllOverlays()
    {
        CloseLurePicker();
        if (modalLayer != null)
            modalLayer.style.display = DisplayStyle.None;
        if (mapJournalLayer != null)
        {
            mapJournalLayer.style.display = DisplayStyle.None;
            mapJournalLayer.pickingMode = PickingMode.Ignore;
        }
        if (!CatchSheetOpen)
            HudInput.PopupOpen = false;
    }

    bool MapJournalOpen => mapJournalLayer != null && mapJournalLayer.style.display == DisplayStyle.Flex;

    void OnMiniMapClusterClicked(IReadOnlyList<CatchRecord> cluster)
    {
        SelectCluster(cluster);
        OpenMapJournal();
    }

    void OnJournalClusterClicked(IReadOnlyList<CatchRecord> cluster)
    {
        SelectCluster(cluster);
        RefreshMapMarks();
        FillJournalDetail();
    }

    void SelectCluster(IReadOnlyList<CatchRecord> cluster, CatchRecord prefer = null)
    {
        selectedCluster.Clear();
        if (cluster != null)
        {
            for (int i = 0; i < cluster.Count; i++)
            {
                if (cluster[i] != null)
                    selectedCluster.Add(cluster[i]);
            }
        }

        SortByWeight(selectedCluster);
        if (prefer != null && selectedCluster.Contains(prefer))
            selectedMarked = prefer;
        else
            selectedMarked = selectedCluster.Count > 0 ? selectedCluster[0] : null;
        markListScroll = 0f;
    }

    static void SortByWeight(List<CatchRecord> records)
    {
        records.Sort((a, b) => b.Pounds.CompareTo(a.Pounds));
    }

    void OpenMapJournal()
    {
        if (CatchSheetOpen || mapJournalLayer == null)
            return;

        fishing?.CancelCastClick();
        if (fishing != null && fishing.Fight != null && fishing.Fight.Playing)
            return;

        CloseLurePicker();
        if (modalLayer != null)
            modalLayer.style.display = DisplayStyle.None;

        RefreshMapMarks();
        mapJournalLayer.pickingMode = PickingMode.Position;
        mapJournalLayer.style.display = DisplayStyle.Flex;
        mapJournalLayer.BringToFront();
        journalMap?.ResetView();
        FillJournalDetail();
        HudInput.PopupOpen = true;
    }

    void OnJournalBackgroundClicked(ClickEvent evt)
    {
        if (evt.target == mapJournalLayer)
            CloseAllOverlays();
    }

    void TickJournalMap()
    {
        if (journalMap == null || conditions == null || !MapJournalOpen)
            return;

        Transform player = conditions.PlayerTransform;
        if (player == null)
            return;

        Transform marker = conditions.OnBoat && conditions.OccupiedBoat != null
            ? conditions.OccupiedBoat.transform
            : player;
        journalMap.SetPlayer(marker.position, marker.eulerAngles.y);
    }

    void RefreshMapMarks()
    {
        progress?.CopyMarked(markedScratch);
        if (selectedMarked != null && !selectedMarked.Marked)
            selectedMarked = null;

        for (int i = selectedCluster.Count - 1; i >= 0; i--)
        {
            if (selectedCluster[i] == null || !selectedCluster[i].Marked)
                selectedCluster.RemoveAt(i);
        }

        if (selectedMarked != null && !selectedCluster.Contains(selectedMarked))
            selectedCluster.Clear();

        mapSonar?.SetMarked(markedScratch, selectedMarked);
        journalMap?.SetMarked(markedScratch, selectedMarked);
        if (MapJournalOpen)
            FillJournalDetail();
    }

    void FillJournalDetail()
    {
        if (mapJournalDetail == null)
            return;

        mapJournalDetail.Clear();
        var header = new VisualElement();
        header.AddToClassList("hud-card-header");
        int n = markedScratch.Count;
        header.Add(HudUi.Title(n == 0 ? "Marked fish" : $"Marked fish  ·  {n}"));
        var x = new Button { text = "✕" };
        x.AddToClassList("hud-close");
        x.focusable = false;
        x.clicked += CloseAllOverlays;
        header.Add(x);
        mapJournalDetail.Add(header);

        if (selectedCluster.Count > 1)
        {
            FillClusterList();
            if (selectedMarked != null)
            {
                var facts = new VisualElement();
                facts.AddToClassList("hud-mark-facts");
                FillCatchFacts(facts, selectedMarked);
                mapJournalDetail.Add(facts);
            }

            return;
        }

        if (selectedMarked == null)
        {
            mapJournalDetail.Add(HudUi.Muted(
                n == 0
                    ? "Land a bass and tap Mark fish to pin it here."
                    : "Scroll to zoom, drag to pan. Click a pin for the catch."));
            return;
        }

        FillCatchFacts(mapJournalDetail, selectedMarked);
    }

    void FillClusterList()
    {
        mapJournalDetail.Add(HudUi.Muted($"{selectedCluster.Count} fish in this area"));
        var list = new ScrollView(ScrollViewMode.Vertical);
        list.AddToClassList("hud-mark-list");
        list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        list.mouseWheelScrollSize = 22f;
        list.verticalScroller.valueChanged += value => markListScroll = value;
        list.RegisterCallback<GeometryChangedEvent>(_ =>
        {
            if (list.contentRect.height > 1f)
                list.scrollOffset = new Vector2(0f, markListScroll);
        });

        for (int i = 0; i < selectedCluster.Count; i++)
        {
            CatchRecord record = selectedCluster[i];
            var row = new Button();
            row.AddToClassList("hud-mark-row");
            if (record == selectedMarked)
                row.AddToClassList("hud-mark-row--on");
            row.focusable = false;
            CatchRecord pick = record;
            row.clicked += () =>
            {
                selectedMarked = pick;
                journalMap?.SetMarked(markedScratch, selectedMarked);
                mapSonar?.SetMarked(markedScratch, selectedMarked);
                FillJournalDetail();
            };

            var swatch = new VisualElement();
            swatch.AddToClassList("hud-lure-swatch");
            swatch.style.backgroundColor = record.LureColor;
            swatch.pickingMode = PickingMode.Ignore;
            row.Add(swatch);
            row.Add(HudUi.Body($"{SpeciesShort(record.SpeciesName)}  {record.Pounds:0.00} lb"));
            list.Add(row);
        }

        mapJournalDetail.Add(list);
    }

    void FillCatchFacts(VisualElement parent, CatchRecord record)
    {
        parent.Add(HudUi.Title(record.SpeciesName));
        parent.Add(HudUi.Body($"{record.Pounds:0.00} lb   ·   {record.LengthInches:0.0} in"));

        var lureRow = new VisualElement();
        lureRow.AddToClassList("hud-catch-lure");
        var swatch = new VisualElement();
        swatch.AddToClassList("hud-lure-swatch");
        swatch.style.backgroundColor = record.LureColor;
        swatch.pickingMode = PickingMode.Ignore;
        lureRow.Add(swatch);
        lureRow.Add(HudUi.Body($"Caught on {record.LureName}"));
        parent.Add(lureRow);

        var facts = new VisualElement();
        facts.AddToClassList("hud-section");
        facts.Add(HudUi.Muted("Spot"));
        if (record.DepthFeet > 0.05f)
            facts.Add(HudUi.Body($"{record.DepthFeet:0.0} ft"));
        string when = JoinFacts(record.TimeLabel, record.WeatherLabel, record.SeasonLabel);
        if (when.Length > 0)
            facts.Add(HudUi.Body(when));
        if (record.WaterTempF > 1f)
            facts.Add(HudUi.Body($"{record.WaterTempF:0}° water"));
        parent.Add(facts);
    }
}
