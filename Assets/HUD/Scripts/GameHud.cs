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
    Label moneyLabel;
    Label reputationLabel;
    int shownMoney = int.MinValue;
    int shownReputation = int.MinValue;
    Label noticeToast;
    Button tourneyChip;
    Label tourneyChipLabel;
    VisualElement tourneyChevron;
    VisualElement bagPopover;
    HudCueOverlay cueOverlay;
    VisualElement fadeLayer;
    VisualElement summaryLayer;
    VisualElement summaryCard;
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
    readonly List<TournamentOccurrence> skipScratch = new List<TournamentOccurrence>();
    readonly List<TournamentOccurrence> enteredScratch = new List<TournamentOccurrence>();
    TourneyTab tourneyTab;
    bool tourneyPastPlacedOnly;
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
        if (GetComponent<HudCuePresenter>() == null)
            gameObject.AddComponent<HudCuePresenter>();
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
            dayCycle.DayEnded += ShowDaySummary;
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
            dayCycle.DayEnded -= ShowDaySummary;
            dayCycle.Morning -= ShowMorning;
        }
        if (director != null)
        {
            director.Notice -= ShowNotice;
            director.BagChanged -= RefreshTournamentChip;
            director.Finished -= ShowTournamentResult;
        }

        HudInput.Reset();
        HudCues.Reset();
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
        RefreshWallet();
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

        // The night's recap is the only thing that can be on screen; it has to be
        // acknowledged, so both keys advance it and nothing else is reachable.
        if (DaySummaryOpen)
        {
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame)
                ContinueToNextDay();
            return;
        }

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

    // GameHud Update must stay early for input flags. Cue chips project through
    // the orbit camera, so they tick after that LateUpdate (see HudCuePresenter).
    internal void PresentCues()
    {
        if (!built)
            return;

        Transform follow = fishing != null
            ? fishing.transform
            : conditions != null ? conditions.PlayerTransform : null;
        cueOverlay?.Tick(
            Camera.main,
            follow,
            HudInput.PopupOpen || DaySummaryOpen || CatchSheetOpen);
    }

    bool TurnInAvailable => dayCycle != null && dayCycle.CanTurnIn && !HudInput.PopupOpen;

    void TickDayCycle()
    {
        bool turning = dayCycle != null && dayCycle.IsTurningIn;

        // The recap belongs to the paused night. If the night resumed by any other
        // route, drop the page rather than stranding the player behind it.
        if (DaySummaryOpen && (dayCycle == null || !dayCycle.AwaitingContinue))
            HideDaySummary();
        if (conditions != null)
            conditions.HoldClock = HudInput.PopupOpen || turning;

        if (TurnInAvailable && !turning)
            HudCues.ShowAction("turn-in", "Enter", "Turn in for the night", onActivate: TurnInFromPrompt);
        else
            HudCues.Clear("turn-in");

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

    void ShowNotice(string message) => ShowNotice(message, 4.5f);

    void ShowNotice(string message, float holdSeconds)
    {
        if (noticeToast == null || string.IsNullOrEmpty(message))
            return;

        if (holdSeconds <= 4.51f && message.IndexOf("left out", System.StringComparison.OrdinalIgnoreCase) >= 0)
            holdSeconds = 8f;

        noticeToast.text = message;
        noticeToast.style.display = DisplayStyle.Flex;
        noticeToast.BringToFront();
        noticeUntil = Time.unscaledTime + holdSeconds;
    }

    void ShowDaySummary(DaySummary summary)
    {
        if (summaryLayer == null || summary == null)
            return;

        CloseAllOverlays();
        ContinueCatch();
        summaryCard.Clear();

        summaryCard.Add(HudUi.Muted(JoinFacts(summary.DateLabel, summary.SeasonLabel, summary.WeatherLabel)));
        summaryCard.Add(HudUi.Title(summary.Headline));
        if (summary.Forced)
            summaryCard.Add(HudUi.Muted("You drifted home after dark."));

        var day = new VisualElement();
        day.AddToClassList("hud-section");
        day.Add(HudUi.Muted("On the water"));
        day.Add(HudUi.Body(summary.CatchLine));
        if (!summary.Blanked)
        {
            day.Add(HudUi.Muted("Best fish"));
            day.Add(HudUi.Body(summary.BestLine));
        }
        if (summary.TopLureFish > 0)
        {
            day.Add(HudUi.Muted("Best lure"));
            day.Add(HudUi.Body(summary.LureLine));
        }
        summaryCard.Add(day);

        if (summary.Tournaments.Count > 0)
        {
            var events = new VisualElement();
            events.AddToClassList("hud-section");
            events.Add(HudUi.Muted(summary.Tournaments.Count == 1 ? "Tournament" : "Tournaments"));
            for (int i = 0; i < summary.Tournaments.Count; i++)
            {
                TournamentResult r = summary.Tournaments[i];
                events.Add(HudUi.Body($"{r.DisplayName}  ·  {r.PlaceLabel}"));
                events.Add(HudUi.Muted($"{r.Pounds:0.00} lb  ·  " +
                    (r.Payout > 0 ? $"${r.Payout}" : "no payout")));
            }

            string earned = summary.EarnedLine;
            if (!string.IsNullOrEmpty(earned))
                events.Add(HudUi.Body(earned));
            summaryCard.Add(events);
        }

        var actions = new VisualElement();
        actions.AddToClassList("hud-row");
        actions.Add(HudUi.TextButton("Continue", ContinueToNextDay, true));
        summaryCard.Add(actions);

        summaryLayer.style.display = DisplayStyle.Flex;
        summaryLayer.BringToFront();
        HudInput.PopupOpen = true;
    }

    bool DaySummaryOpen => summaryLayer != null && summaryLayer.style.display == DisplayStyle.Flex;

    void ContinueToNextDay()
    {
        if (!DaySummaryOpen)
            return;

        dayCycle?.ContinueToNextDay();
        HideDaySummary();
    }

    void HideDaySummary()
    {
        summaryLayer.style.display = DisplayStyle.None;
        if (!CatchSheetOpen)
            HudInput.PopupOpen = false;
    }

    void ShowMorning(DayReport report)
    {
        // Tournament mornings skip the greeting; the director toasts the camp reminder.
        if (director != null && conditions != null
            && director.Phase == TournamentPhase.Idle
            && director.HasRegistrationOn(conditions.DayIndex))
            return;

        string season = string.IsNullOrEmpty(report.SeasonLabel)
            ? ""
            : report.SeasonLabel.ToLowerInvariant();
        ShowNotice(JoinFacts("Good morning.", report.DateLabel, season));
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
        topLeft.Add(HudUi.StatChip("Money", null, out moneyLabel));
        topLeft.Add(HudUi.StatChip("Reputation", HudUi.PaintStar, out reputationLabel));
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

        tourneyChip = new Button();
        tourneyChip.AddToClassList("hud-tourney-chip");
        tourneyChip.focusable = false;
        tourneyChip.style.display = DisplayStyle.None;
        tourneyChip.clicked += ToggleBagPopover;
        tourneyChipLabel = new Label();
        tourneyChipLabel.AddToClassList("hud-tourney-chip-label");
        tourneyChipLabel.pickingMode = PickingMode.Ignore;
        tourneyChip.Add(tourneyChipLabel);
        tourneyChevron = HudUi.Glyph("hud-tourney-chevron", HudUi.PaintChevronDown);
        tourneyChip.Add(tourneyChevron);
        root.Add(tourneyChip);

        cueOverlay = new HudCueOverlay();
        root.Add(cueOverlay);

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
        popoverCatcher.RegisterCallback<ClickEvent>(_ => ClosePopovers());
        root.Add(popoverCatcher);

        lurePopover = new VisualElement();
        lurePopover.AddToClassList("hud-lure-popover");
        lurePopover.style.display = DisplayStyle.None;
        root.Add(lurePopover);

        bagPopover = new VisualElement();
        bagPopover.AddToClassList("hud-bag-popover");
        bagPopover.style.display = DisplayStyle.None;
        root.Add(bagPopover);

        BuildMapJournal();

        fadeLayer = new VisualElement();
        fadeLayer.AddToClassList("hud-fade");
        fadeLayer.pickingMode = PickingMode.Ignore;
        fadeLayer.style.display = DisplayStyle.None;
        fadeLayer.style.opacity = 0f;
        root.Add(fadeLayer);

        // Added after the blackout so the night's recap reads over the top of it.
        summaryLayer = new VisualElement();
        summaryLayer.AddToClassList("hud-summary");
        summaryLayer.style.display = DisplayStyle.None;
        summaryCard = new VisualElement();
        summaryCard.AddToClassList("hud-summary-card");
        summaryCard.pickingMode = PickingMode.Position;
        summaryLayer.Add(summaryCard);
        root.Add(summaryLayer);

        RefreshLureChip();
        RefreshTournamentChip();
        shownMoney = int.MinValue;
        shownReputation = int.MinValue;
        RefreshWallet();
        built = true;
    }

    void TurnInFromPrompt()
    {
        if (TurnInAvailable)
            dayCycle.TurnIn(false);
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

    void RefreshWallet()
    {
        int money = progress != null ? progress.Money : 0;
        int reputation = progress != null ? progress.Reputation : 0;
        if (money == shownMoney && reputation == shownReputation)
            return;

        shownMoney = money;
        shownReputation = reputation;
        if (moneyLabel != null)
            moneyLabel.text = $"${money}";
        if (reputationLabel != null)
            reputationLabel.text = reputation.ToString();
    }

    void RefreshLureChip()
    {
        if (lureName == null)
            return;
        LureDefinition lure = tackle != null ? tackle.Equipped : null;
        lureName.text = lure != null ? lure.DisplayName : "Lure";
        ApplyLureSwatch(lureSwatch, lure != null ? lure.Icon : null, lure != null ? lure.Color : HudTheme.Teal);
        lureChip.tooltip = lure != null ? lure.Hint : "";
    }

    void ShowCatch(CatchRecord record)
    {
        if (catchToast != null)
            catchToast.style.display = DisplayStyle.None;

        shownCatch = record;
        if (catchSheet == null || record == null)
            return;

        ClosePopovers();
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
        swatch.pickingMode = PickingMode.Ignore;
        ApplyLureSwatch(swatch, IconForCatch(record), record.LureColor);
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
        ClosePopovers();
        VisualElement body = BeginCard("Profile", CloseAllOverlays);

        string name = progress != null ? progress.DisplayName : "You";
        int money = progress != null ? progress.Money : 0;
        int reputation = progress != null ? progress.Reputation : 0;

        var hero = new VisualElement();
        hero.AddToClassList("hud-profile-hero");
        hero.Add(HudUi.Glyph("hud-profile-avatar", HudUi.PaintProfile));

        var identity = new VisualElement();
        identity.AddToClassList("hud-profile-identity");
        Label nameLabel = HudUi.Title(name);
        nameLabel.AddToClassList("hud-profile-name");
        identity.Add(nameLabel);
        identity.Add(HudUi.Muted(progress != null && progress.HasName
            ? LakeChoice.DisplayName(SaveService.Instance != null ? SaveService.Instance.Player.selectedLake : LakeChoice.Willow)
            : "Needed on the tournament board"));
        hero.Add(identity);

        if (progress != null)
        {
            Button rename = HudUi.TextButton(
                progress.HasName ? "Rename" : "Set name",
                OpenRenameSheet,
                !progress.HasName);
            if (progress.HasName)
                rename.AddToClassList("hud-text-button--quiet");
            hero.Add(rename);
        }

        body.Add(hero);

        var stats = new VisualElement();
        stats.AddToClassList("hud-profile-stats");
        stats.Add(HudUi.StatTile($"${money}", "Money"));
        stats.Add(HudUi.StatTile(reputation.ToString(), "Reputation", HudUi.PaintStar));
        body.Add(stats);

        var pb = new VisualElement();
        pb.AddToClassList("hud-section");
        pb.AddToClassList("hud-profile-best");
        pb.Add(HudUi.Muted("Personal best"));
        if (progress != null && progress.HasPersonalBest)
        {
            var weightRow = HudUi.Row();
            weightRow.Add(HudUi.Glyph("hud-profile-best-mark", HudUi.PaintTrophy));
            Label weight = HudUi.Title($"{progress.BestBassPounds:0.00} lb");
            weight.AddToClassList("hud-profile-best-weight");
            weightRow.Add(weight);
            pb.Add(weightRow);
            pb.Add(HudUi.Body(progress.BestSpecies));
        }
        else
        {
            pb.Add(HudUi.Body("No trophy yet."));
            pb.Add(HudUi.Muted("The lake is waiting."));
        }

        body.Add(pb);

        var history = new VisualElement();
        history.AddToClassList("hud-section");

        int wins = 0;
        if (director != null)
        {
            foreach (TournamentResult past in director.History)
            {
                if (past.Won)
                    wins++;
            }
        }

        var historyHead = HudUi.Row();
        historyHead.AddToClassList("hud-profile-section-head");
        Label historyTitle = HudUi.Muted("Tournaments");
        historyTitle.AddToClassList("hud-profile-section-title");
        historyHead.Add(historyTitle);
        if (wins > 0)
            historyHead.Add(HudUi.Pill(wins == 1 ? "1 win" : $"{wins} wins", true));
        history.Add(historyHead);

        if (director == null || director.History.Count == 0)
        {
            history.Add(HudUi.Body("No results yet."));
            history.Add(HudUi.Muted("The Saturday Club is free to enter."));
        }
        else
        {
            int shown = 0;
            foreach (TournamentResult past in director.History)
            {
                if (shown++ >= 5)
                    break;
                history.Add(ProfileResultRow(past));
            }
        }

        body.Add(history);
        ShowModal();
    }

    static VisualElement ProfileResultRow(TournamentResult past)
    {
        var row = new VisualElement();
        row.AddToClassList("hud-profile-result");

        var head = HudUi.Row();
        Label eventName = HudUi.Body(past.DisplayName);
        eventName.AddToClassList("hud-profile-result-name");
        head.Add(eventName);
        head.Add(HudUi.Pill(past.Won ? "Win" : past.PlaceLabel, past.Won || past.Placed));
        row.Add(head);

        string meta = past.Forfeited ? "Missed the weigh-in" : $"{past.Pounds:0.00} lb";
        if (past.Payout > 0)
            meta += $"  ·  ${past.Payout}";
        if (past.Reputation > 0)
            meta += $"  ·  +{past.Reputation} Rep";
        row.Add(HudUi.Muted(meta));
        return row;
    }

    void OpenTournaments()
    {
        if (CatchSheetOpen || MapJournalOpen)
            return;
        ClosePopovers();
        VisualElement body = BeginCard("Tournaments", CloseAllOverlays);
        modalCard.EnableInClassList("hud-card--tall", true);

        if (director == null)
        {
            body.Add(HudUi.Body("Nothing on the calendar just now."));
            ShowModal();
            return;
        }

        if (progress != null)
            body.Add(HudUi.Muted($"${progress.Money}  ·  {progress.Reputation} Reputation"));

        if (director.Phase != TournamentPhase.Idle)
        {
            var live = new VisualElement();
            live.AddToClassList("hud-section");
            live.Add(HudUi.Muted("On the water now"));
            live.Add(HudUi.Body(director.StatusLine));
            body.Add(live);
        }

        var tabs = HudUi.TabRow();
        tabs.AddToClassList("hud-card-tabs");
        tabs.Add(HudUi.Tab("Board", () =>
        {
            tourneyTab = TourneyTab.Board;
            OpenTournaments();
        }, tourneyTab == TourneyTab.Board));
        tabs.Add(HudUi.Tab("Entered", () =>
        {
            tourneyTab = TourneyTab.Entered;
            OpenTournaments();
        }, tourneyTab == TourneyTab.Entered));
        tabs.Add(HudUi.Tab("Past", () =>
        {
            tourneyTab = TourneyTab.Past;
            OpenTournaments();
        }, tourneyTab == TourneyTab.Past));
        modalCard.Insert(1, tabs);

        if (tourneyTab == TourneyTab.Past)
        {
            var filter = HudUi.TabRow();
            filter.AddToClassList("hud-past-filter");
            filter.Add(HudUi.Tab("All", () =>
            {
                tourneyPastPlacedOnly = false;
                OpenTournaments();
            }, !tourneyPastPlacedOnly));
            filter.Add(HudUi.Tab("Placed", () =>
            {
                tourneyPastPlacedOnly = true;
                OpenTournaments();
            }, tourneyPastPlacedOnly));
            modalCard.Insert(2, filter);
            FillPast(body);
        }
        else if (tourneyTab == TourneyTab.Entered)
            FillEntered(body);
        else
            FillBoard(body);

        ShowModal();
    }

    void FillBoard(VisualElement body)
    {
        IReadOnlyList<TournamentOccurrence> schedule = director.Upcoming;
        if (schedule.Count == 0)
        {
            body.Add(HudUi.Body("Nothing on the calendar just now."));
            return;
        }

        string group = "";
        for (int i = 0; i < schedule.Count; i++)
        {
            string week = conditions != null
                ? TournamentSchedule.WeekLabel(conditions.Calendar, schedule[i])
                : "";
            if (week.Length > 0 && week != group)
            {
                group = week;
                body.Add(GroupLabel(week));
            }

            body.Add(MakeTournamentRow(schedule[i]));
        }
    }

    void FillEntered(VisualElement body)
    {
        director.CopyRegistrations(enteredScratch);
        enteredScratch.Sort((a, b) =>
        {
            int day = a.DayIndex.CompareTo(b.DayIndex);
            if (day != 0)
                return day;
            string nameA = a.Definition != null ? a.Definition.DisplayName : "";
            string nameB = b.Definition != null ? b.Definition.DisplayName : "";
            return string.CompareOrdinal(nameA, nameB);
        });

        if (enteredScratch.Count == 0)
        {
            body.Add(HudUi.Body("Nothing entered. Sign up on the board."));
            return;
        }

        string group = "";
        for (int i = 0; i < enteredScratch.Count; i++)
        {
            string week = conditions != null
                ? TournamentSchedule.WeekLabel(conditions.Calendar, enteredScratch[i])
                : "";
            if (week.Length > 0 && week != group)
            {
                group = week;
                body.Add(GroupLabel(week));
            }

            body.Add(MakeTournamentRow(enteredScratch[i]));
        }
    }

    void FillPast(VisualElement body)
    {
        IReadOnlyList<TournamentResult> history = director.History;
        int shown = 0;
        string group = "";
        for (int i = 0; i < history.Count; i++)
        {
            TournamentResult past = history[i];
            if (past == null)
                continue;
            if (tourneyPastPlacedOnly && !past.Placed)
                continue;

            string week = conditions != null
                ? TournamentSchedule.PastWeekLabel(conditions.Calendar, past.DayIndex)
                : "";
            if (week.Length > 0 && week != group)
            {
                group = week;
                body.Add(GroupLabel(week));
            }

            body.Add(MakePastRow(past));
            shown++;
        }

        if (shown > 0)
            return;

        body.Add(HudUi.Body(tourneyPastPlacedOnly
            ? "Nothing placed yet."
            : "No tournaments yet. Sign up on the board."));
    }

    VisualElement MakePastRow(TournamentResult result)
    {
        var row = new VisualElement();
        row.AddToClassList("hud-section");
        row.AddToClassList("hud-tourney-row");
        if (result.Placed)
            row.AddToClassList("hud-tourney-row--placed");

        var head = new VisualElement();
        head.AddToClassList("hud-tourney-head");
        Label name = HudUi.Body(result.DisplayName);
        name.AddToClassList("hud-tourney-name");
        head.Add(name);
        string place = result.Forfeited
            ? "Forfeited"
            : result.Pounds <= 0.01f
                ? "No weight"
                : TournamentResult.Ordinal(result.Place);
        head.Add(HudUi.Pill(place, result.Placed));
        row.Add(head);

        if (!string.IsNullOrEmpty(result.DateLabel))
            row.Add(HudUi.Muted(result.DateLabel));
        if (!result.Forfeited)
            row.Add(HudUi.Muted($"{result.Pounds:0.00} lb  ·  {result.Fish} fish"));

        if (result.WonLunkerLargemouth || result.WonLunkerSmallmouth)
        {
            var lunkers = HudUi.Row();
            if (result.WonLunkerLargemouth)
                lunkers.Add(HudUi.Pill("LM lunker", true));
            if (result.WonLunkerSmallmouth)
                lunkers.Add(HudUi.Pill("SM lunker", true));
            row.Add(lunkers);
        }

        if (result.Payout > 0 || result.Reputation > 0)
        {
            string haul = result.Payout > 0 ? $"+${result.Payout}" : "";
            if (result.Reputation > 0)
                haul = haul.Length > 0
                    ? $"{haul}  ·  +{result.Reputation} Rep"
                    : $"+{result.Reputation} Rep";
            row.Add(HudUi.Muted(haul));
        }

        return row;
    }

    static Label GroupLabel(string text)
    {
        Label label = HudUi.Muted(text);
        label.AddToClassList("hud-group-label");
        return label;
    }

    VisualElement MakeTournamentRow(TournamentOccurrence occurrence)
    {
        TournamentDefinition def = occurrence.Definition;
        GameCalendar calendar = conditions != null ? conditions.Calendar : default;
        bool registered = director.IsRegistered(occurrence);

        var row = new VisualElement();
        row.AddToClassList("hud-section");
        row.AddToClassList("hud-tourney-row");
        if (registered)
            row.AddToClassList("hud-tourney-row--entered");

        var head = new VisualElement();
        head.AddToClassList("hud-tourney-head");
        Label name = HudUi.Body(def.DisplayName);
        name.AddToClassList("hud-tourney-name");
        head.Add(name);
        if (registered)
            head.Add(HudUi.Pill("Entered", true));
        head.Add(HudUi.Pill(def.TierLabel));
        if (conditions != null)
            head.Add(HudUi.Pill(TournamentSchedule.CountdownLabel(calendar, occurrence)));
        row.Add(head);

        row.Add(HudUi.Muted(conditions != null
            ? TournamentSchedule.WhenLabel(calendar, occurrence)
            : $"{def.Weekday}  ·  {def.WindowLabel}"));
        row.Add(HudUi.Muted($"{def.FormatLabel}  ·  {def.EntryLabel}"));
        row.Add(HudUi.Muted($"{def.PlacesPurseLabel}  ·  {def.FieldSize + 1} anglers"));
        row.Add(HudUi.Muted(def.LunkerPurseLabel));

        bool locked = !director.MeetsReputation(def);
        if (locked)
            row.Add(HudUi.LockLine(def.ReputationLockLabel));
        else if (!registered && !director.AffordableFee(def))
            row.Add(HudUi.Muted("Not enough money"));
        else if (!registered && director.HasRegistrationOn(occurrence.DayIndex) && !director.HasPassed(occurrence))
            row.Add(HudUi.Muted("Already entered a tournament that day"));

        var actions = new VisualElement();
        actions.AddToClassList("hud-tourney-actions");
        if (registered)
        {
            actions.Add(HudUi.TextButton("Withdraw", () =>
            {
                director.Withdraw(occurrence);
                SaveService.Instance?.Save();
                OpenTournaments();
            }));
        }
        else if (director.CanRegister(occurrence))
        {
            actions.Add(HudUi.TextButton(def.EntryFee > 0 ? $"Enter · ${def.EntryFee}" : "Enter", () =>
            {
                // The board is where a name first matters, so that is where it is asked for.
                if (progress != null && !progress.HasName)
                    OpenSignUpSheet(occurrence);
                else
                    RegisterFor(occurrence);
            }, true));
        }

        if (director.CanSkipTo(occurrence))
            actions.Add(HudUi.TextButton("Skip to", () => OpenSkipSheet(occurrence)));
        if (actions.childCount > 0)
            row.Add(actions);

        return row;
    }

    void RegisterFor(TournamentOccurrence occurrence)
    {
        director.Register(occurrence);
        // An entry fee has just left the wallet; don't make a crash refund it.
        SaveService.Instance?.Save();
        tourneyTab = TourneyTab.Entered;
        OpenTournaments();
    }

    /// <summary>
    /// Skipping ahead costs the days in between, so the board says what they were
    /// worth before the player sleeps through them.
    /// </summary>
    void OpenSkipSheet(TournamentOccurrence occurrence)
    {
        TournamentDefinition def = occurrence.Definition;
        if (def == null || conditions == null || !director.CanSkipTo(occurrence))
            return;

        GameCalendar calendar = conditions.Calendar;
        int days = TournamentSchedule.DaysAway(calendar, occurrence);
        VisualElement body = BeginCard("Skip ahead", OpenTournaments);
        body.Add(HudUi.Body($"Sleep through to the {def.DisplayName}?"));
        body.Add(HudUi.Muted(days == 1
            ? $"You wake at the cabin on {calendar.DateLabelFor(occurrence.DayIndex)}, one day from now."
            : $"You wake at the cabin on {calendar.DateLabelFor(occurrence.DayIndex)}, {days} days from now."));
        body.Add(HudUi.Muted($"Boat to the camp by {GameCalendar.FormatHour(def.StartHour)} or you're left out."));

        director.RegistrationsBefore(occurrence.DayIndex, skipScratch);
        if (skipScratch.Count > 0)
        {
            var dropped = new VisualElement();
            dropped.AddToClassList("hud-section");
            dropped.Add(HudUi.Muted("Withdrawn along the way"));

            int refund = 0;
            for (int i = 0; i < skipScratch.Count; i++)
            {
                TournamentDefinition entered = skipScratch[i].Definition;
                if (entered == null)
                    continue;

                refund += entered.EntryFee;
                dropped.Add(HudUi.Body(
                    $"{entered.DisplayName}  ·  {calendar.DateLabelFor(skipScratch[i].DayIndex)}"));
            }

            if (refund > 0)
                dropped.Add(HudUi.Muted($"${refund} refunded"));
            body.Add(dropped);
        }

        var actions = new VisualElement();
        actions.AddToClassList("hud-form-actions");
        actions.Add(HudUi.TextButton("Back", OpenTournaments));
        actions.Add(HudUi.TextButton("Skip ahead", () =>
        {
            CloseAllOverlays();
            director.SkipTo(occurrence);
        }, true));
        body.Add(actions);

        ShowModal();
    }

    void OpenSignUpSheet(TournamentOccurrence occurrence)
    {
        TournamentDefinition def = occurrence.Definition;
        BuildNameCard(
            "Sign-up sheet",
            def != null
                ? $"The {def.DisplayName} wants a name for the board."
                : "The board wants a name.",
            "",
            def != null && def.EntryFee > 0 ? $"Sign in · ${def.EntryFee}" : "Sign in",
            OpenTournaments,
            () => RegisterFor(occurrence));
    }

    void OpenRenameSheet()
    {
        if (progress == null)
            return;

        BuildNameCard(
            "Angler name",
            "Tournament boards use this name.",
            progress.HasName ? progress.DisplayName : "",
            "Save",
            OpenProfile,
            () =>
            {
                SaveService.Instance?.Save();
                OpenProfile();
            });
    }

    /// <summary>Prompt, field, and a confirm that refuses a blank board entry.</summary>
    void BuildNameCard(
        string title,
        string prompt,
        string seed,
        string confirmLabel,
        System.Action back,
        System.Action confirmed)
    {
        if (progress == null)
            return;

        ClosePopovers();
        VisualElement body = BeginCard(title, back);
        body.Add(HudUi.Body(prompt));

        var sheet = new VisualElement();
        sheet.AddToClassList("hud-section");
        sheet.Add(HudUi.Muted("Angler"));

        var error = HudUi.Muted("Give the weigh-in something to read.");
        error.style.display = DisplayStyle.None;

        TextField field = null;
        void Submit()
        {
            if (!progress.SetDisplayName(field.value))
            {
                error.style.display = DisplayStyle.Flex;
                return;
            }

            confirmed();
        }

        field = HudUi.NameField(seed, PlayerProgress.MaxNameLength, Submit);
        sheet.Add(field);
        sheet.Add(error);
        body.Add(sheet);

        var actions = new VisualElement();
        actions.AddToClassList("hud-form-actions");
        actions.Add(HudUi.TextButton("Back", back));
        actions.Add(HudUi.TextButton(confirmLabel, Submit, true));
        body.Add(actions);

        ShowModal();
    }

    void ShowTournamentResult(TournamentResult result)
    {
        if (modalCard == null || result == null)
            return;

        ClosePopovers();
        if (result.Placed || result.Paid)
            ShowPrize(result);
        else
            ShowTournamentStanding(result);
    }

    void ShowPrize(TournamentResult result)
    {
        VisualElement body = BeginCard(result.DisplayName, CloseAllOverlays);
        modalCard.EnableInClassList("hud-card--prize", true);

        var prize = new VisualElement();
        prize.AddToClassList("hud-prize");
        prize.Add(HudUi.Glyph("hud-prize-mark", HudUi.PaintTrophy));
        Label headline = HudUi.Title(result.PrizeHeadline);
        headline.AddToClassList("hud-prize-place");
        prize.Add(headline);
        prize.Add(HudUi.Body($"{result.Pounds:0.00} lb  ·  {result.Fish} fish"));
        AddLunkerNotes(prize, result);
        if (result.Penalty > 0.001f)
            prize.Add(HudUi.Muted($"Late penalty −{result.Penalty:0.00} lb"));

        var haul = new VisualElement();
        haul.AddToClassList("hud-prize-haul");
        Label money = HudUi.Title(result.Payout > 0 ? $"+${result.Payout}" : "No payout");
        money.AddToClassList("hud-prize-money");
        haul.Add(money);
        if (result.PlacePayout > 0 && result.LunkerPayout > 0)
            haul.Add(HudUi.Muted($"Place ${result.PlacePayout}  ·  Lunkers ${result.LunkerPayout}"));
        else if (result.LunkerPayout > 0)
            haul.Add(HudUi.Muted("Lunker side pot"));
        if (result.Reputation > 0)
        {
            Label rep = HudUi.Body($"+{result.Reputation} Reputation");
            rep.AddToClassList("hud-prize-rep");
            haul.Add(rep);
        }

        prize.Add(haul);
        body.Add(prize);

        var actions = new VisualElement();
        actions.AddToClassList("hud-form-actions");
        if (result.HasStandings)
            actions.Add(HudUi.TextButton("View standings", () => ShowTournamentStandings(result)));
        actions.Add(HudUi.TextButton("Collect", CloseAllOverlays, true));
        body.Add(actions);
        ShowModal();
    }

    void ShowTournamentStanding(TournamentResult result)
    {
        VisualElement body = BeginCard(result.DisplayName, CloseAllOverlays);

        if (result.Forfeited)
        {
            body.Add(HudUi.Title("Missed the weigh-in"));
            body.Add(HudUi.Body("The camp scales closed before you got back."));
        }
        else
        {
            body.Add(HudUi.Title(result.PlaceLabel));
            body.Add(HudUi.Body($"{result.Pounds:0.00} lb  ·  {result.Fish} fish"));
            AddLunkerNotes(body, result);
        }

        if (result.Penalty > 0.001f)
            body.Add(HudUi.Muted($"Late penalty −{result.Penalty:0.00} lb from {result.RawPounds:0.00} lb"));

        var purse = new VisualElement();
        purse.AddToClassList("hud-section");
        purse.Add(HudUi.Muted("Purse"));
        purse.Add(HudUi.Body("No payout"));
        if (result.EntryFee > 0)
            purse.Add(HudUi.Muted($"Entry was ${result.EntryFee}"));
        body.Add(purse);

        var actions = new VisualElement();
        actions.AddToClassList("hud-form-actions");
        if (result.HasStandings)
            actions.Add(HudUi.TextButton("View standings", () => ShowTournamentStandings(result), true));
        body.Add(actions);
        ShowModal();
    }

    static void AddLunkerNotes(VisualElement parent, TournamentResult result)
    {
        if (result.WonLunkerLargemouth)
            parent.Add(HudUi.Body($"Largemouth lunker  ·  {result.LunkerLargemouth:0.00} lb"));
        if (result.WonLunkerSmallmouth)
            parent.Add(HudUi.Body($"Smallmouth lunker  ·  {result.LunkerSmallmouth:0.00} lb"));
    }

    void ShowTournamentStandings(TournamentResult result)
    {
        VisualElement body = BeginCard("Standings", CloseAllOverlays);
        modalCard.EnableInClassList("hud-card--standings", true);
        body.Add(HudUi.Muted(result.DisplayName));

        var table = new VisualElement();
        table.AddToClassList("hud-standings");
        table.Add(StandingsHeader());
        IReadOnlyList<TournamentStanding> rows = result.Standings;
        for (int i = 0; i < rows.Count; i++)
            table.Add(StandingsRow(i + 1, rows[i]));
        body.Add(table);

        var actions = new VisualElement();
        actions.AddToClassList("hud-form-actions");
        actions.Add(HudUi.TextButton("Back", () => ShowTournamentResult(result)));
        body.Add(actions);
        ShowModal();
    }

    static VisualElement StandingsHeader()
    {
        var row = new VisualElement();
        row.AddToClassList("hud-standings-row");
        row.AddToClassList("hud-standings-row--head");
        row.Add(StandingsCell("#", "hud-standings-place"));
        row.Add(StandingsCell("Angler", "hud-standings-name"));
        row.Add(StandingsCell("Fish", "hud-standings-fish"));
        row.Add(StandingsCell("Weight", "hud-standings-weight"));
        Label lm = StandingsCell("LM", "hud-standings-lunker");
        lm.tooltip = "Largemouth lunker";
        row.Add(lm);
        Label sm = StandingsCell("SM", "hud-standings-lunker");
        sm.tooltip = "Smallmouth lunker";
        row.Add(sm);
        return row;
    }

    static VisualElement StandingsRow(int place, TournamentStanding standing)
    {
        var row = new VisualElement();
        row.AddToClassList("hud-standings-row");
        if (standing.IsPlayer)
            row.AddToClassList("hud-standings-row--player");

        row.Add(StandingsCell(place.ToString(), "hud-standings-place"));
        row.Add(StandingsCell(string.IsNullOrEmpty(standing.Name) ? "—" : standing.Name, "hud-standings-name"));
        row.Add(StandingsCell(standing.Fish.ToString(), "hud-standings-fish"));
        row.Add(StandingsCell(FormatPounds(standing.Pounds), "hud-standings-weight"));

        Label lm = StandingsCell(FormatPounds(standing.LunkerLargemouth), "hud-standings-lunker");
        if (standing.WonLunkerLargemouth)
            lm.AddToClassList("hud-standings-lunker--won");
        row.Add(lm);

        Label sm = StandingsCell(FormatPounds(standing.LunkerSmallmouth), "hud-standings-lunker");
        if (standing.WonLunkerSmallmouth)
            sm.AddToClassList("hud-standings-lunker--won");
        row.Add(sm);
        return row;
    }

    static Label StandingsCell(string text, string className)
    {
        var label = new Label(text);
        label.AddToClassList("hud-standings-cell");
        label.AddToClassList(className);
        label.pickingMode = PickingMode.Ignore;
        return label;
    }

    static string FormatPounds(float pounds) => pounds > 0.01f ? $"{pounds:0.00}" : "—";

    void RefreshTournamentChip()
    {
        if (tourneyChip == null)
            return;

        string status = director != null ? director.StatusLine : "";
        bool live = !string.IsNullOrEmpty(status);
        if (tourneyChipLabel != null)
            tourneyChipLabel.text = status;
        tourneyChip.style.display = live ? DisplayStyle.Flex : DisplayStyle.None;
        RefreshTourneyChevron();
        if (!live)
            CloseBagPopover();
        else if (BagPopoverOpen)
            FillBagPopover();
    }

    bool BagPopoverOpen => bagPopover != null && bagPopover.style.display == DisplayStyle.Flex;

    void ToggleBagPopover()
    {
        if (CatchSheetOpen || MapJournalOpen)
            return;
        if (BagPopoverOpen)
        {
            CloseBagPopover();
            return;
        }

        if (director == null || string.IsNullOrEmpty(director.StatusLine))
            return;

        CloseLurePicker();
        FillBagPopover();
        bagPopover.style.display = DisplayStyle.Flex;
        popoverCatcher.style.display = DisplayStyle.Flex;
        bagPopover.BringToFront();
        bagPopover.RegisterCallback<GeometryChangedEvent>(PlaceBagPopover);
        RefreshTourneyChevron();
        HudInput.PopupOpen = false;
    }

    void FillBagPopover()
    {
        if (bagPopover == null)
            return;

        bagPopover.Clear();
        bagPopover.Add(HudUi.Muted("Live bag"));

        IReadOnlyList<CatchRecord> kept = director != null ? director.Bag : null;
        if (kept == null || kept.Count == 0)
        {
            bagPopover.Add(HudUi.Body("No keepers yet"));
            return;
        }

        float bestLm = 0f;
        float bestSm = 0f;
        for (int i = 0; i < kept.Count; i++)
        {
            CatchRecord fish = kept[i];
            if (TournamentBag.IsLargemouth(fish))
                bestLm = Mathf.Max(bestLm, fish.Pounds);
            else if (TournamentBag.IsSmallmouth(fish))
                bestSm = Mathf.Max(bestSm, fish.Pounds);
        }

        for (int i = 0; i < kept.Count; i++)
        {
            CatchRecord fish = kept[i];
            var row = new VisualElement();
            row.AddToClassList("hud-bag-row");

            var name = HudUi.Body($"{SpeciesShort(fish.SpeciesName)}  {fish.Pounds:0.00} lb");
            name.AddToClassList("hud-bag-name");
            row.Add(name);

            if (TournamentBag.IsLargemouth(fish) && fish.Pounds >= bestLm - 0.001f)
                row.Add(HudUi.Pill("LM"));
            else if (TournamentBag.IsSmallmouth(fish) && fish.Pounds >= bestSm - 0.001f)
                row.Add(HudUi.Pill("SM"));

            bagPopover.Add(row);
        }

        bagPopover.Add(HudUi.Muted($"{director.BagFish}/{director.BagLimit}  ·  {director.BagPounds:0.00} lb"));
    }

    void PlaceBagPopover(GeometryChangedEvent _)
    {
        bagPopover.UnregisterCallback<GeometryChangedEvent>(PlaceBagPopover);
        if (tourneyChip == null)
            return;

        Rect chip = tourneyChip.worldBound;
        Rect panel = root.worldBound;
        float width = bagPopover.resolvedStyle.width;
        if (width < 1f)
            width = 280f;
        bagPopover.style.left = chip.center.x - width * 0.5f - panel.x;
        bagPopover.style.top = chip.yMax - panel.yMin + 8f;
    }

    void CloseBagPopover()
    {
        if (bagPopover != null)
            bagPopover.style.display = DisplayStyle.None;
        RefreshTourneyChevron();
        if (lurePopover == null || lurePopover.style.display != DisplayStyle.Flex)
        {
            if (popoverCatcher != null)
                popoverCatcher.style.display = DisplayStyle.None;
        }
    }

    void RefreshTourneyChevron()
    {
        if (tourneyChevron == null)
            return;
        tourneyChevron.EnableInClassList("hud-tourney-chevron--open", BagPopoverOpen);
    }

    void ClosePopovers()
    {
        CloseLurePicker();
        CloseBagPopover();
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
                swatch.pickingMode = PickingMode.Ignore;
                ApplyLureSwatch(swatch, lure.Icon, lure.Color);
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
        if (popoverCatcher != null && !BagPopoverOpen)
            popoverCatcher.style.display = DisplayStyle.None;
    }

    static void ApplyLureSwatch(VisualElement swatch, Sprite icon, Color fallback)
    {
        if (swatch == null)
            return;
        if (icon != null)
        {
            swatch.style.backgroundImage = new StyleBackground(icon);
            swatch.style.backgroundColor = StyleKeyword.Null;
            swatch.EnableInClassList("hud-lure-swatch--photo", true);
        }
        else
        {
            swatch.style.backgroundImage = new StyleBackground(StyleKeyword.None);
            swatch.style.backgroundColor = fallback;
            swatch.EnableInClassList("hud-lure-swatch--photo", false);
        }
    }

    static Sprite IconForCatch(CatchRecord record)
    {
        if (record == null)
            return null;
        ContentRegistry registry = ContentRegistry.Instance;
        LureDefinition lure = registry != null ? registry.LureNamed(record.LureName) : null;
        return lure != null ? lure.Icon : null;
    }

    /// <summary>
    /// Starts a fresh modal page: a pinned header over a scrolling body. Pages
    /// fill the body, so a long one scrolls instead of squeezing its rows into
    /// each other.
    /// </summary>
    VisualElement BeginCard(string title, System.Action close)
    {
        modalCard.Clear();
        modalCard.EnableInClassList("hud-card--prize", false);
        modalCard.EnableInClassList("hud-card--tall", false);
        modalCard.EnableInClassList("hud-card--standings", false);
        AddCardHeader(title, close);

        var body = new ScrollView(ScrollViewMode.Vertical);
        body.AddToClassList("hud-card-body");
        body.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        body.mouseWheelScrollSize = 28f;
        modalCard.Add(body);
        return body;
    }

    void AddCardHeader(string title, System.Action close)
    {
        // Every card rebuild runs through here, and the field it replaces may have
        // been holding keyboard focus.
        HudInput.Typing = false;

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
        ClosePopovers();
        HudInput.Typing = false;
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

        ClosePopovers();
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
        if (selectedMarked == null && selectedCluster.Count > 0)
            selectedMarked = selectedCluster[0];

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
                AddRemovePinButton(facts);
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
        AddRemovePinButton(mapJournalDetail);
    }

    void AddRemovePinButton(VisualElement parent)
    {
        parent.Add(HudUi.TextButton("Remove", UnmarkSelectedMark));
    }

    void UnmarkSelectedMark()
    {
        if (selectedMarked == null || !selectedMarked.Marked)
            return;
        progress?.UnmarkCatch(selectedMarked);
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
            swatch.pickingMode = PickingMode.Ignore;
            ApplyLureSwatch(swatch, IconForCatch(record), record.LureColor);
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
        swatch.pickingMode = PickingMode.Ignore;
        ApplyLureSwatch(swatch, IconForCatch(record), record.LureColor);
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

    enum TourneyTab
    {
        Board,
        Entered,
        Past
    }
}

[DefaultExecutionOrder(50)]
sealed class HudCuePresenter : MonoBehaviour
{
    GameHud hud;

    void Awake()
    {
        hud = GetComponent<GameHud>();
    }

    void LateUpdate()
    {
        hud?.PresentCues();
    }
}
