using Isley;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static NextMoveSnapshot Baseline() => new(
    StreamerMode: false,
    SurvivalLabel: string.Empty,
    SurvivalPriority: string.Empty,
    SurvivalUrgency: 0,
    EncounterDistance: null,
    EncounterCardinal: string.Empty,
    EncounterMotion: string.Empty,
    PackSpreadAlertActive: false,
    PackFriendCount: 0,
    PackSpread: null,
    WaypointActive: false,
    WaypointDistance: null,
    WaypointTrend: "waiting",
    SoonestTimerSeconds: -1,
    GrowthPaused: false,
    GrowthPercent: 25,
    PrimeConditionsReady: false,
    PrimeConfirmed: false,
    ElderConfirmed: false,
    NestActive: false,
    NestPhase: string.Empty,
    NestNextAction: string.Empty,
    LifeRunActive: false,
    LifeRunNextObjective: string.Empty,
    LiveMapServicesActive: true,
    SelfAvailable: true);

// --- Deterministic ranking by the declared priority ladder -------------------
var stacked = NextMoveLogic.EvaluateStacked(Baseline() with
{
    SurvivalLabel = "Fracture",
    SurvivalPriority = "HIDE AND REST",
    SurvivalUrgency = 2,
    CoreVitalsUrgency = 1,
    CoreVitalsHeading = "STAMINA LOW",
    SoonestTimerSeconds = 42
}, maxShown: 3);
Require(stacked.TotalActive == 3
        && stacked.Shown.Count == 3
        && stacked.Shown[0].Category == "SURVIVAL"
        && stacked.Shown[1].Category == "VITALS"
        && stacked.Shown[2].Category == "TIMER"
        && !stacked.HasOverflow,
    "safety > vitals-critical > timers ordering failed");

// --- Bounded top N with an honest "+N more" overflow -------------------------
var crowded = NextMoveLogic.EvaluateStacked(Baseline() with
{
    PackSpreadAlertActive = true,
    PackFriendCount = 4,
    PackSpread = 87.25,
    SoonestTimerSeconds = 42,
    WaypointActive = true,
    WaypointDistance = 42.75,
    WaypointTrend = "away",
    RestartWatchActive = true,
    RestartWatchRemainingSeconds = 540,
    RestartWatchHeading = "RESTART REPORTED",
    RestartWatchActionId = "restart-watch",
    FieldConditionsWarning = true,
    FieldConditionsHeading = "STORM REPORTED"
}, maxShown: 3);
Require(crowded.TotalActive == 5
        && crowded.Shown.Count == StackedGuidanceLogic.MaxShown
        && crowded.Shown[0].Category == "PACK"
        && crowded.Shown[1].Category == "ROUTE"
        && crowded.Shown[2].Category == "TIMER"
        && crowded.OverflowCount == 2
        && crowded.OverflowSuffix == "+2"
        && crowded.HasOverflow,
    "bounded top-3 with +2 overflow failed");
Require(crowded.OverflowTooltip.Contains("RESTART", StringComparison.Ordinal)
        && crowded.OverflowTooltip.Contains("FIELD", StringComparison.Ordinal)
        && crowded.OverflowTooltip.Contains("highest-priority", StringComparison.Ordinal),
    "overflow tooltip must honestly list the hidden guidance");

// The single-slot HUD mode shows the top item and counts every runner-up.
var singleSlot = NextMoveLogic.EvaluateStacked(Baseline() with
{
    PackSpreadAlertActive = true,
    PackFriendCount = 4,
    PackSpread = 87.25,
    SoonestTimerSeconds = 42,
    WaypointActive = true,
    WaypointTrend = "away"
});
Require(singleSlot.Shown.Count == 1
        && singleSlot.Top.Category == "PACK"
        && singleSlot.OverflowCount == 2
        && singleSlot.OverflowSuffix == "+2",
    "single-slot HUD overflow count failed");

// --- Stable ordering: equal priorities keep declaration order ----------------
var synthetic = new[]
{
    new NextMoveRecommendation("ALPHA", "A", "a", "a", "A", 500, NextMoveTone.Active),
    new NextMoveRecommendation("BETA", "B", "b", "b", "B", 900, NextMoveTone.Warning),
    new NextMoveRecommendation("GAMMA", "G", "g", "g", "G", 900, NextMoveTone.Warning),
    new NextMoveRecommendation("DELTA", "D", "d", "d", "D", 100, NextMoveTone.Neutral)
};
var ranked = StackedGuidanceLogic.Rank(synthetic, maxShown: 3);
Require(ranked.Shown[0].Category == "BETA"
        && ranked.Shown[1].Category == "GAMMA"
        && ranked.Shown[2].Category == "ALPHA"
        && ranked.OverflowCount == 1
        && ranked.OverflowTooltip.Contains("DELTA", StringComparison.Ordinal),
    "equal-priority stacks must keep a stable declaration order");
Require(StackedGuidanceLogic.Rank(synthetic, maxShown: 99).Shown.Count == 3
        && StackedGuidanceLogic.Rank(synthetic, maxShown: 2).Shown.Count == 2
        && StackedGuidanceLogic.Rank(synthetic, maxShown: 0).Shown.Count == 1,
    "maxShown must clamp into the bounded 1..3 window");

var emptyRejected = false;
try
{
    StackedGuidanceLogic.Rank(Array.Empty<NextMoveRecommendation>());
}
catch (ArgumentException)
{
    emptyRejected = true;
}
Require(emptyRejected, "an empty candidate list must be rejected, never fabricated");

// --- Evaluate parity: the stack top is always the historical cascade winner --
Require(NextMoveLogic.Evaluate(Baseline() with
        {
            SurvivalLabel = "Bleeding",
            SurvivalPriority = "STOP SPRINTING",
            SurvivalUrgency = 3,
            EncounterDistance = 4,
            EncounterMotion = "closing"
        }).Category == "SURVIVAL",
    "critical survival must stay on top of live contact");
Require(NextMoveLogic.Evaluate(Baseline() with
        {
            WaypointActive = true,
            WaypointDistance = 42.75,
            WaypointTrend = "away",
            SoonestTimerSeconds = 20
        }).Heading == "CORRECT COURSE",
    "moving-away route must stay ahead of a due-soon timer");
Require(NextMoveLogic.Evaluate(Baseline() with { SoonestTimerSeconds = 42 }).Category == "TIMER",
    "a lone due-soon timer must still surface");
Require(NextMoveLogic.Evaluate(Baseline() with
        {
            WaypointActive = true,
            WaypointDistance = 120,
            WaypointTrend = "closing"
        }).Heading == "STAY ON ROUTE",
    "ambient route fallback parity failed");
Require(NextMoveLogic.Evaluate(Baseline() with
        {
            LifeRunActive = true,
            LifeRunNextObjective = "MIGRATION 1/2"
        }).Heading == "MIGRATION 1/2",
    "ambient Life Run objective parity failed");
Require(NextMoveLogic.Evaluate(Baseline() with { SelfAvailable = false }).Category == "RECOVERY",
    "ambient position-waiting parity failed");
Require(NextMoveLogic.Evaluate(Baseline() with
        {
            LiveMapServicesActive = false,
            SelfAvailable = false
        }).Heading == "START A LIFE RUN",
    "ambient start-a-run parity failed");

// --- Ambient default content never counts as competition ---------------------
var loneTimer = NextMoveLogic.EvaluateStacked(Baseline() with { SoonestTimerSeconds = 42 });
Require(loneTimer.TotalActive == 1
        && !loneTimer.HasOverflow
        && loneTimer.OverflowSuffix.Length == 0
        && loneTimer.OverflowTooltip.Length == 0,
    "ambient fallbacks must never inflate the \"+N more\" affordance");

var arrivalIsCompetition = NextMoveLogic.EvaluateStacked(Baseline() with
{
    WaypointActive = true,
    WaypointDistance = 12.4,
    WaypointTrend = "closing",
    SoonestTimerSeconds = 20
});
Require(arrivalIsCompetition.TotalActive == 2
        && arrivalIsCompetition.Top.Category == "TIMER"
        && arrivalIsCompetition.OverflowSuffix == "+1"
        && arrivalIsCompetition.OverflowTooltip.Contains("ROUTE", StringComparison.Ordinal),
    "an imminent arrival must count as competing guidance behind the timer");

// --- Streamer Mode stays a single redacted candidate, never a leaking stack --
var streamerStack = NextMoveLogic.EvaluateStacked(Baseline() with
{
    StreamerMode = true,
    SurvivalLabel = "Bleeding",
    SurvivalPriority = "Stop sprinting",
    SurvivalUrgency = 3,
    EncounterDistance = 2
});
Require(streamerStack.TotalActive == 1
        && streamerStack.Top.Category == "HIDDEN"
        && !streamerStack.HasOverflow
        && !streamerStack.OverflowTooltip.Contains("Bleeding", StringComparison.OrdinalIgnoreCase),
    "Streamer Mode must collapse the stack to the redacted candidate");

// --- Integration: the HUD renders the ranked stack, not the lone cascade -----
var root = Directory.GetCurrentDirectory();
var survival = File.ReadAllText(Path.Combine(root, "BurntHud", "MainWindow.Survival.cs"));
Require(survival.Contains("var stack = NextMoveLogic.EvaluateStacked(CurrentNextMoveSnapshot());", StringComparison.Ordinal)
        && survival.Contains("stack.HasOverflow", StringComparison.Ordinal)
        && survival.Contains("stack.OverflowSuffix", StringComparison.Ordinal)
        && survival.Contains("NextMoveCategoryText.ToolTip = stack.HasOverflow", StringComparison.Ordinal)
        && !survival.Contains("CurrentNextMoveRecommendation", StringComparison.Ordinal),
    "the Next Move HUD must render the ranked stack with the honest \"+N\" affordance");

Console.WriteLine(
    "Stacked guidance: PASS (deterministic priority-ladder ranking, stable equal-priority order, bounded top-3, honest \"+N more\" overflow, cascade parity, ambient-tail exclusion, streamer redaction, HUD integration)");
