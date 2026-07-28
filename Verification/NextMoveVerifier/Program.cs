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

var hidden = NextMoveLogic.Evaluate(Baseline() with
{
    StreamerMode = true,
    SurvivalLabel = "Bleeding",
    SurvivalPriority = "Stop sprinting",
    SurvivalUrgency = 3,
    EncounterDistance = 2
});
Require(!hidden.HasAction
        && hidden.Category == "HIDDEN"
        && !hidden.Detail.Contains("Bleeding", StringComparison.OrdinalIgnoreCase),
    "Streamer redaction priority failed");

var critical = NextMoveLogic.Evaluate(Baseline() with
{
    SurvivalLabel = "Bleeding",
    SurvivalPriority = "STOP SPRINTING",
    SurvivalUrgency = 3,
    EncounterDistance = 4,
    EncounterMotion = "closing"
});
Require(critical.Category == "SURVIVAL"
        && critical.ActionId == "survival-assistant"
        && critical.Tone == NextMoveTone.Critical,
    "Critical survival precedence failed");

var criticalVitals = NextMoveLogic.Evaluate(Baseline() with
{
    CoreVitalsUrgency = 3,
    CoreVitalsHeading = "WATER EMPTY",
    CoreVitalsDetail = "Find water now.",
    EncounterDistance = 4
});
Require(criticalVitals.Category == "VITALS"
        && criticalVitals.Heading == "WATER EMPTY"
        && criticalVitals.ActionId == "core-vitals"
        && criticalVitals.Priority < critical.Priority,
    "Critical-vitals priority failed");

var contact = NextMoveLogic.Evaluate(Baseline() with
{
    SurvivalLabel = "Fracture",
    SurvivalPriority = "HIDE AND REST",
    SurvivalUrgency = 2,
    EncounterDistance = 24.5,
    EncounterCardinal = "sw",
    EncounterMotion = "closing"
});
Require(contact.Category == "CONTACT"
        && contact.ActionId == "escape-route"
        && contact.ActionLabel == "PLAN ESCAPE"
        && contact.Detail.Contains("24.5 MU SW", StringComparison.Ordinal)
        && contact.Detail.Contains("route away", StringComparison.Ordinal),
    "Closing-contact precedence failed");

var manualClose = NextMoveLogic.Evaluate(Baseline() with
{
    LiveMapServicesActive = false,
    SelfAvailable = false,
    ManualSightingActive = true,
    ManualSightingUrgency = 3,
    ManualSightingHeading = "CREATE SPACE",
    ManualSightingDetail = "Player-reported close contact ahead.",
    RestartWatchActive = true,
    RestartWatchRemainingSeconds = 45
});
Require(manualClose.Category == "SIGHTING"
        && manualClose.ActionId == "sighting-check"
        && manualClose.ActionLabel == "UPDATE SIGHTING"
        && manualClose.Priority == 940
        && manualClose.Tone == NextMoveTone.Critical,
    "Manual close-sighting priority failed");

var liveContactWins = NextMoveLogic.Evaluate(Baseline() with
{
    EncounterDistance = 8,
    EncounterCardinal = "e",
    EncounterMotion = "steady",
    ManualSightingActive = true,
    ManualSightingUrgency = 3,
    ManualSightingHeading = "CREATE SPACE",
    ManualSightingDetail = "Player-reported close contact ahead."
});
Require(liveContactWins.Category == "CONTACT"
        && liveContactWins.ActionId == "escape-route"
        && !liveContactWins.Detail.Contains("Player-reported", StringComparison.Ordinal),
    "Authorized live-contact precedence over manual sighting failed");

var finalMinuteRestart = NextMoveLogic.Evaluate(Baseline() with
{
    SurvivalLabel = "Fracture",
    SurvivalPriority = "HIDE AND REST",
    SurvivalUrgency = 2,
    RestartWatchActive = true,
    RestartWatchRemainingSeconds = 45,
    RestartWatchHeading = "SAFE LOGOUT NOW",
    RestartWatchDetail = "Use the in-game safe-log flow.",
    RestartWatchActionId = "safe-logout",
    RestartWatchActionLabel = "START LOGOUT"
});
Require(finalMinuteRestart.Category == "RESTART"
        && finalMinuteRestart.ActionId == "safe-logout"
        && finalMinuteRestart.Priority > 900,
    "Final-minute restart priority failed");

var contactWaitingForSelf = NextMoveLogic.Evaluate(Baseline() with
{
    EncounterDistance = 7.5,
    EncounterCardinal = "n",
    EncounterMotion = "steady",
    SelfAvailable = false
});
Require(contactWaitingForSelf.Category == "CONTACT"
        && contactWaitingForSelf.ActionId == "players"
        && contactWaitingForSelf.ActionLabel == "OPEN CONTACTS"
        && contactWaitingForSelf.Detail.Contains("position is still calibrating", StringComparison.Ordinal),
    "Contact-without-self fallback failed");

var communityContact = NextMoveLogic.Evaluate(Baseline() with
{
    LiveMapServicesActive = false,
    SelfAvailable = false,
    EncounterDistance = 3,
    EncounterMotion = "closing"
});
Require(communityContact.Category == "LIFE"
        && communityContact.ActionId == "life-run",
    "Unauthorized-session contact refusal failed");

var moderateSurvival = NextMoveLogic.Evaluate(Baseline() with
{
    SurvivalLabel = "Fracture",
    SurvivalPriority = "HIDE AND REST",
    SurvivalUrgency = 2,
    EncounterDistance = 40,
    EncounterMotion = "steady"
});
Require(moderateSurvival.Category == "SURVIVAL"
        && moderateSurvival.Priority < critical.Priority,
    "Moderate survival fallback failed");

var lowVitals = NextMoveLogic.Evaluate(Baseline() with
{
    CoreVitalsUrgency = 1,
    CoreVitalsHeading = "STAMINA LOW",
    CoreVitalsDetail = "Conserve stamina.",
    PackSpreadAlertActive = true,
    PackFriendCount = 4,
    PackSpread = 80
});
Require(lowVitals.Category == "VITALS"
        && lowVitals.ActionId == "core-vitals"
        && lowVitals.Priority < moderateSurvival.Priority,
    "Low-vitals warning priority failed");

var resourceTrend = NextMoveLogic.Evaluate(Baseline() with
{
    ResourceTrendWarning = true,
    ResourceTrendHeading = "WATER LOW IN ABOUT 8M",
    ResourceTrendDetail = "Three fresh samples show a steady decline.",
    PackSpreadAlertActive = true,
    PackFriendCount = 4,
    PackSpread = 80
});
Require(resourceTrend.Category == "RESOURCES"
        && resourceTrend.Heading == "WATER LOW IN ABOUT 8M"
        && resourceTrend.ActionId == "core-vitals"
        && resourceTrend.Priority < lowVitals.Priority,
    "Early resource-trend priority failed");

var shorelineWarning = NextMoveLogic.Evaluate(Baseline() with
{
    ShorelineCheckActive = true,
    ShorelineCheckSeverity = 3,
    ShorelineCheckHeading = "BACK OFF · KEEP THE EXIT",
    ShorelineCheckDetail = "An authorized contact is close to the waterline.",
    ShorelineCheckActionId = "escape-route",
    ShorelineCheckActionLabel = "PLAN ESCAPE",
    PackSpreadAlertActive = true,
    PackFriendCount = 4,
    PackSpread = 80
});
Require(shorelineWarning.Category == "SHORELINE"
        && shorelineWarning.ActionId == "escape-route"
        && shorelineWarning.Priority < resourceTrend.Priority
        && shorelineWarning.Tone == NextMoveTone.Critical,
    "Shoreline warning priority failed");

var crossingWarning = NextMoveLogic.Evaluate(Baseline() with
{
    WaterCrossingActive = true,
    WaterCrossingSeverity = 2,
    WaterCrossingHeading = "CAUTION · LONG WATER EXPOSURE",
    WaterCrossingDetail = "72 MU between the selected banks; find a shorter span.",
    WaterCrossingActionId = "measure-crossing",
    WaterCrossingActionLabel = "RESET BANKS",
    PackSpreadAlertActive = true,
    PackFriendCount = 4,
    PackSpread = 80
});
Require(crossingWarning.Category == "CROSSING"
        && crossingWarning.ActionId == "measure-crossing"
        && crossingWarning.Priority < resourceTrend.Priority,
    "Water-crossing warning priority failed");

var pack = NextMoveLogic.Evaluate(Baseline() with
{
    PackSpreadAlertActive = true,
    PackFriendCount = 4,
    PackSpread = 87.25,
    WaypointActive = true,
    WaypointTrend = "away"
});
Require(pack.Category == "PACK"
        && pack.ActionId == "players"
        && pack.Detail.Contains("4 authorized friends", StringComparison.Ordinal),
    "Pack-boundary precedence failed");

var manualNear = NextMoveLogic.Evaluate(Baseline() with
{
    ManualSightingActive = true,
    ManualSightingUrgency = 2,
    ManualSightingHeading = "HOLD AN EXIT",
    ManualSightingDetail = "Player-reported near contact to your right.",
    WaypointActive = true,
    WaypointTrend = "away"
});
Require(manualNear.Category == "SIGHTING"
        && manualNear.ActionId == "sighting-check"
        && manualNear.Priority == 845
        && manualNear.Tone == NextMoveTone.Warning,
    "Manual near-sighting priority failed");

var manualFar = NextMoveLogic.Evaluate(Baseline() with
{
    ManualSightingActive = true,
    ManualSightingUrgency = 1,
    ManualSightingHeading = "MONITOR THE CONTACT",
    ManualSightingDetail = "Player-reported far contact behind you."
});
Require(manualFar.Category == "LIFE"
        && manualFar.ActionId == "life-run",
    "Far manual sighting should not displace the normal Next Move");

var fiveMinuteRestart = NextMoveLogic.Evaluate(Baseline() with
{
    RestartWatchActive = true,
    RestartWatchRemainingSeconds = 300,
    RestartWatchHeading = "FINISH AND FIND COVER",
    RestartWatchDetail = "Avoid a new fight.",
    RestartWatchActionId = "safe-logout-setup",
    RestartWatchActionLabel = "OPEN LOGOUT",
    PackSpreadAlertActive = true,
    PackFriendCount = 3,
    PackSpread = 80
});
Require(fiveMinuteRestart.Category == "RESTART"
        && fiveMinuteRestart.ActionId == "safe-logout-setup"
        && fiveMinuteRestart.Priority > pack.Priority,
    "Five-minute restart priority failed");

var away = NextMoveLogic.Evaluate(Baseline() with
{
    WaypointActive = true,
    WaypointDistance = 42.75,
    WaypointTrend = "away",
    SoonestTimerSeconds = 20
});
Require(away.Heading == "CORRECT COURSE"
        && away.ActionId == "routes",
    "Moving-away route priority failed");

var timer = NextMoveLogic.Evaluate(Baseline() with { SoonestTimerSeconds = 42 });
Require(timer.Category == "TIMER"
        && timer.Detail.Contains("42s", StringComparison.Ordinal)
        && timer.ActionId == "timers",
    "Due-soon timer priority failed");

var earlyRestart = NextMoveLogic.Evaluate(Baseline() with
{
    RestartWatchActive = true,
    RestartWatchRemainingSeconds = 540,
    RestartWatchHeading = "RESTART REPORTED",
    RestartWatchDetail = "Player-reported estimate.",
    RestartWatchActionId = "restart-watch",
    RestartWatchActionLabel = "OPEN WATCH"
});
Require(earlyRestart.Category == "RESTART"
        && earlyRestart.ActionId == "restart-watch"
        && earlyRestart.Priority < timer.Priority,
    "Early restart watch priority failed");

var elapsedRestart = NextMoveLogic.Evaluate(Baseline() with
{
    RestartWatchActive = true,
    RestartWatchRemainingSeconds = 0,
    RestartWatchHeading = "RESTART WINDOW ELAPSED",
    RestartWatchDetail = "Verify the in-game server state.",
    RestartWatchActionId = "restart-watch",
    RestartWatchActionLabel = "REVIEW WATCH"
});
Require(elapsedRestart.Category == "RESTART"
        && elapsedRestart.Heading == "RESTART WINDOW ELAPSED"
        && elapsedRestart.ActionId == "restart-watch",
    "Elapsed restart verification handoff failed");

var lifeTransition = NextMoveLogic.Evaluate(Baseline() with
{
    LifeRunActive = true,
    LifeTransitionPending = true,
    LifeTransitionHeading = "LIVE DINOSAUR CHANGED",
    LifeTransitionDetail = "The live feed moved from Carnotaurus to Dilophosaurus.",
    FieldConditionsWarning = true,
    SpeciesMismatch = true
});
Require(lifeTransition.Category == "LIFE"
        && lifeTransition.Heading == "LIVE DINOSAUR CHANGED"
        && lifeTransition.ActionId == "life-run"
        && lifeTransition.ActionLabel == "REVIEW LIFE"
        && lifeTransition.Priority < timer.Priority,
    "Life-transition review priority failed");

var transitionBehindTimer = NextMoveLogic.Evaluate(Baseline() with
{
    LifeRunActive = true,
    LifeTransitionPending = true,
    SoonestTimerSeconds = 20
});
Require(transitionBehindTimer.Category == "TIMER",
    "Due-soon timer must remain ahead of a lifecycle review");

var growthGate = NextMoveLogic.Evaluate(Baseline() with
{
    LifeRunActive = true,
    GrowthGatePending = true,
    GrowthGateHeading = "PRIME WINDOW OPEN",
    GrowthGateDetail = "Growth crossed 75%; verify Prime in game.",
    GrowthGateActionId = "prime-planner",
    GrowthGateActionLabel = "OPEN PRIME",
    FieldConditionsWarning = true,
    SpeciesMismatch = true
});
Require(growthGate.Category == "GROWTH"
        && growthGate.Heading == "PRIME WINDOW OPEN"
        && growthGate.ActionId == "prime-planner"
        && growthGate.ActionLabel == "OPEN PRIME"
        && growthGate.Priority < lifeTransition.Priority,
    "Live growth-gate priority failed");

var transitionBeforeGrowthGate = NextMoveLogic.Evaluate(Baseline() with
{
    LifeRunActive = true,
    LifeTransitionPending = true,
    GrowthGatePending = true,
    GrowthGateActionId = "prime-planner"
});
Require(transitionBeforeGrowthGate.Heading == "CHECK NEW DINOSAUR",
    "Lifecycle review must remain ahead of a growth gate");

var urgentApproach = NextMoveLogic.Evaluate(Baseline() with
{
    ApproachBriefActive = true,
    ApproachBriefUrgency = 2,
    ApproachBriefHeading = "THREAT APPROACH",
    ApproachBriefDetail = "A Death marker is 40 MU away; verify the area in game.",
    ApproachBriefActionId = "routes",
    ApproachBriefActionLabel = "OPEN ROUTE",
    FieldConditionsWarning = true,
    FieldConditionsHeading = "FOG REPORTED"
});
Require(urgentApproach.Category == "APPROACH"
        && urgentApproach.Heading == "THREAT APPROACH"
        && urgentApproach.ActionId == "routes"
        && urgentApproach.Priority < growthGate.Priority,
    "Urgent destination approach priority failed");

var activeCrossing = NextMoveLogic.Evaluate(Baseline() with
{
    WaterCrossingActive = true,
    WaterCrossingSeverity = 1,
    WaterCrossingHeading = "MARK THE EXIT BANK",
    WaterCrossingDetail = "Select the intended exit point.",
    WaterCrossingActionId = "clear-crossing-check",
    WaterCrossingActionLabel = "CANCEL",
    FieldConditionsWarning = true,
    FieldConditionsHeading = "STORM REPORTED"
});
Require(activeCrossing.Category == "CROSSING"
        && activeCrossing.Heading == "MARK THE EXIT BANK"
        && activeCrossing.Priority < urgentApproach.Priority,
    "Active Water Crossing workflow priority failed");

var activeShoreline = NextMoveLogic.Evaluate(Baseline() with
{
    ShorelineCheckActive = true,
    ShorelineCheckSeverity = 0,
    ShorelineCheckHeading = "NO REPORTED BLOCKER · VERIFY IN GAME",
    ShorelineCheckDetail = "The short shoreline snapshot is active.",
    ShorelineCheckActionId = "shoreline-check-clear",
    ShorelineCheckActionLabel = "END CHECK",
    WaterCrossingActive = true,
    WaterCrossingSeverity = 1,
    WaterCrossingHeading = "MARK THE EXIT BANK"
});
Require(activeShoreline.Category == "SHORELINE"
        && activeShoreline.ActionId == "shoreline-check-clear"
        && activeShoreline.Priority > activeCrossing.Priority
        && activeShoreline.Tone == NextMoveTone.Active,
    "Active Shoreline Check workflow priority failed");

var growthGateBeforeApproach = NextMoveLogic.Evaluate(Baseline() with
{
    LifeRunActive = true,
    GrowthGatePending = true,
    GrowthGateActionId = "prime-planner",
    ApproachBriefActive = true,
    ApproachBriefUrgency = 2,
    ApproachBriefActionId = "routes"
});
Require(growthGateBeforeApproach.Category == "GROWTH",
    "A live growth-gate review must remain ahead of an approach warning");

var fieldConditions = NextMoveLogic.Evaluate(Baseline() with
{
    FieldConditionsWarning = true,
    FieldConditionsHeading = "STORM REPORTED",
    FieldConditionsDetail = "Hold cover and move by compass."
});
Require(fieldConditions.Category == "FIELD"
        && fieldConditions.Heading == "STORM REPORTED"
        && fieldConditions.ActionId == "field-conditions"
        && fieldConditions.Priority < timer.Priority,
    "Field-conditions warning priority failed");

var speciesMismatch = NextMoveLogic.Evaluate(Baseline() with
{
    LifeRunActive = true,
    SpeciesMismatch = true,
    LiveSpeciesName = "Carnotaurus",
    GrowthPaused = true
});
Require(speciesMismatch.Category == "PROFILE"
        && speciesMismatch.Heading == "SYNC LIVE SPECIES"
        && speciesMismatch.Detail.Contains("CARNOTAURUS", StringComparison.Ordinal)
        && speciesMismatch.ActionId == "diet-coach"
        && speciesMismatch.Priority < fieldConditions.Priority,
    "Live-species mismatch priority failed");

var pausedGrowth = NextMoveLogic.Evaluate(Baseline() with
{
    LifeRunActive = true,
    GrowthPaused = true,
    LifeRunNextObjective = "PERFECT DIET"
});
Require(pausedGrowth.Category == "GROWTH"
        && pausedGrowth.ActionId == "growth-clock",
    "Paused-growth priority failed");

var normalApproach = NextMoveLogic.Evaluate(Baseline() with
{
    LifeRunActive = true,
    GrowthPercent = 100,
    PrimeConditionsReady = true,
    PrimeConfirmed = true,
    ElderConfirmed = false,
    NestActive = true,
    ApproachBriefActive = true,
    ApproachBriefUrgency = 1,
    ApproachBriefHeading = "WATER APPROACH",
    ApproachBriefDetail = "Stop short, scent, listen, and verify in game.",
    ApproachBriefActionId = "routes",
    ApproachBriefActionLabel = "OPEN ROUTE"
});
Require(normalApproach.Category == "APPROACH"
        && normalApproach.Heading == "WATER APPROACH"
        && normalApproach.Tone == NextMoveTone.Active,
    "Normal destination approach priority failed");

var fieldBeforeNormalApproach = NextMoveLogic.Evaluate(Baseline() with
{
    FieldConditionsWarning = true,
    FieldConditionsHeading = "STORM REPORTED",
    ApproachBriefActive = true,
    ApproachBriefUrgency = 1,
    ApproachBriefActionId = "routes"
});
Require(fieldBeforeNormalApproach.Category == "FIELD",
    "A field warning must remain ahead of a normal approach brief");

var elder = NextMoveLogic.Evaluate(Baseline() with
{
    LifeRunActive = true,
    GrowthPercent = 100,
    PrimeConditionsReady = true,
    PrimeConfirmed = true,
    ElderConfirmed = false,
    LifeRunNextObjective = "ALL TRACKED"
});
Require(elder.Category == "ELDER"
        && elder.ActionId == "elder-lineage",
    "Elder verification priority failed");

var prime = NextMoveLogic.Evaluate(Baseline() with
{
    LifeRunActive = true,
    GrowthPercent = 75,
    PrimeConditionsReady = true,
    PrimeConfirmed = false,
    LifeRunNextObjective = "ALL TRACKED"
});
Require(prime.Category == "PRIME"
        && prime.ActionId == "prime-planner",
    "Prime verification priority failed");

var nest = NextMoveLogic.Evaluate(Baseline() with
{
    LifeRunActive = true,
    NestActive = true,
    NestPhase = "INCUBATE",
    NestNextAction = "Keep the nest warm.",
    LifeRunNextObjective = "RAISE YOUNG"
});
Require(nest.Category == "NEST"
        && nest.Heading == "NEST · INCUBATE"
        && nest.ActionId == "nest-planner",
    "Active-nest priority failed");

var arriving = NextMoveLogic.Evaluate(Baseline() with
{
    WaypointActive = true,
    WaypointDistance = 12.4,
    WaypointTrend = "closing"
});
Require(arriving.Heading == "ARRIVAL SOON"
        && arriving.ActionId == "routes",
    "Arrival-soon priority failed");

var activeRoute = NextMoveLogic.Evaluate(Baseline() with
{
    WaypointActive = true,
    WaypointDistance = 120,
    WaypointTrend = "closing"
});
Require(activeRoute.Heading == "STAY ON ROUTE"
        && activeRoute.Tone == NextMoveTone.Active,
    "Active-route fallback failed");

var life = NextMoveLogic.Evaluate(Baseline() with
{
    LifeRunActive = true,
    LifeRunNextObjective = "MIGRATION 1/2"
});
Require(life.Category == "LIFE"
        && life.Heading == "MIGRATION 1/2"
        && life.ActionId == "life-run",
    "Life Run objective fallback failed");

var waiting = NextMoveLogic.Evaluate(Baseline() with
{
    SelfAvailable = false,
    LiveMapServicesActive = true
});
Require(waiting.Category == "RECOVERY"
        && waiting.ActionId == "recovery",
    "Authorized-position waiting fallback failed");

var startLife = NextMoveLogic.Evaluate(Baseline() with
{
    LiveMapServicesActive = false,
    SelfAvailable = false
});
Require(startLife.Heading == "START A LIFE RUN"
        && startLife.ActionId == "life-run",
    "Universal-session fallback failed");

var malformed = NextMoveLogic.Evaluate(Baseline() with
{
    EncounterDistance = double.NaN,
    PackSpread = double.PositiveInfinity,
    WaypointDistance = -5,
    EncounterMotion = "closing<script>",
    LiveMapServicesActive = false
});
Require(malformed.Category == "LIFE"
        && !malformed.Detail.Contains("script", StringComparison.OrdinalIgnoreCase),
    "Malformed live-value refusal failed");

Require(NextMoveLogic.CompactSummary(contact) == "NEXT CONTACT"
        && !NextMoveLogic.CompactSummary(contact).Contains("24.5", StringComparison.Ordinal),
    "Anonymous compact summary failed");

Console.WriteLine("Next Move: PASS (privacy, 28-level priority ladder, restart-watch, Shoreline Check, and Water Crossing escalation, lifecycle, growth-gate, and destination-approach review, live-species mismatch, resource forecast, direct Escape Route, self-position fallback, safe live-value handling, and compact summary)");
