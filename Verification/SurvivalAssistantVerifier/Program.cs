using Isley;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Require(SurvivalAssistantLogic.Incidents.Length == 11, "Incident catalog count failed");
Require(SurvivalAssistantLogic.Incidents.Select(item => item.Id)
        .Distinct(StringComparer.OrdinalIgnoreCase).Count() == 11,
    "Incident id uniqueness failed");
Require(SurvivalAssistantLogic.Incidents.All(item => item.Urgency is >= 1 and <= 3),
    "Urgency range failed");
Require(SurvivalAssistantLogic.Incidents.All(item => item.Steps.Length == 3),
    "Pressure-first action count failed");
Require(SurvivalAssistantLogic.Incidents.All(item =>
        !string.IsNullOrWhiteSpace(SurvivalAssistantLogic.ResolveRecoveryRemedy(
            item.Id, liveMapServicesAvailable: true, lifeRunActive: true).ActionLabel)),
    "Recovery remedy coverage failed");

Require(SurvivalAssistantLogic.Find("BLEEDING")?.ExpectedSeconds == 0,
    "Bleeding variable-duration contract failed");
Require(SurvivalAssistantLogic.Find("fracture")?.ExpectedSeconds == 600,
    "Fracture timer contract failed");
Require(SurvivalAssistantLogic.Find("venom")?.ExpectedSeconds == 60,
    "Venom safety timer contract failed");
Require(SurvivalAssistantLogic.Find("vomit")?.ExpectedSeconds == 300,
    "Vomit sickness timer contract failed");
Require(SurvivalAssistantLogic.Find("food-poisoning")?.ExpectedSeconds == 420,
    "Rotten-food poisoning timer contract failed");
Require(SurvivalAssistantLogic.Find("long-sickness")?.ExpectedSeconds == 1200,
    "Long sickness timer contract failed");
Require(SurvivalAssistantLogic.NormalizeIncidentId("not-real") == string.Empty
        && SurvivalAssistantLogic.Find(null) is null,
    "Invalid incident normalization failed");

var fracture = SurvivalAssistantLogic.Find("fracture")!.Value;
var start = DateTimeOffset.UnixEpoch;
Require(SurvivalAssistantLogic.RemainingSeconds(fracture, start, start.AddSeconds(-5)) == 600,
    "Future-clock clamping failed");
Require(SurvivalAssistantLogic.RemainingSeconds(fracture, start, start.AddSeconds(90)) == 510,
    "Countdown calculation failed");
Require(SurvivalAssistantLogic.RemainingSeconds(fracture, start, start.AddHours(1)) == 0,
    "Expired countdown clamping failed");
Require(SurvivalAssistantLogic.FormatRemaining(0) == "0:00"
        && SurvivalAssistantLogic.FormatRemaining(65) == "1:05"
        && SurvivalAssistantLogic.FormatRemaining(3661) == "1:01:01",
    "Countdown formatting failed");
Require(SurvivalAssistantLogic.CompactSummary(fracture, start, start.AddSeconds(90))
        == "STATUS FRACTURE · HIDE AND REST · 8:30 EST",
    "Compact status summary failed");
var vomitRemedy = SurvivalAssistantLogic.ResolveRecoveryRemedy(
    "vomit", liveMapServicesAvailable: true, lifeRunActive: false);
var bacteriaRemedy = SurvivalAssistantLogic.ResolveRecoveryRemedy(
    "bacteria", liveMapServicesAvailable: true, lifeRunActive: false);
var universalVomitRemedy = SurvivalAssistantLogic.ResolveRecoveryRemedy(
    "vomit", liveMapServicesAvailable: false, lifeRunActive: false);
var waterRemedy = SurvivalAssistantLogic.ResolveRecoveryRemedy(
    "dehydrated", liveMapServicesAvailable: true, lifeRunActive: false);
Require(vomitRemedy is { Kind: RecoveryRemedyKind.ResourceFinder, Target: "salt", ActionLabel: "FIND SALT LICK" }
        && bacteriaRemedy is { Kind: RecoveryRemedyKind.ResourceFinder, Target: "salt" }
        && universalVomitRemedy is { Kind: RecoveryRemedyKind.SavedPin, Target: "safe" }
        && waterRemedy is { Kind: RecoveryRemedyKind.SavedPin, Target: "water" },
    "Condition-aware recovery remedy failed");
var configuredFoodRemedy = SurvivalAssistantLogic.ResolveRecoveryRemedy(
    "starving", liveMapServicesAvailable: true, lifeRunActive: true);
var unconfiguredFoodRemedy = SurvivalAssistantLogic.ResolveRecoveryRemedy(
    "starving", liveMapServicesAvailable: true, lifeRunActive: false);
var universalFoodRemedy = SurvivalAssistantLogic.ResolveRecoveryRemedy(
    "starving", liveMapServicesAvailable: false, lifeRunActive: false);
Require(configuredFoodRemedy is { Kind: RecoveryRemedyKind.ResourceFinder, Target: "diet" }
        && unconfiguredFoodRemedy.Kind == RecoveryRemedyKind.FoodLayer
        && universalFoodRemedy.Kind == RecoveryRemedyKind.DietCoach,
    "Species-aware food recovery handoff failed");

Require(SurvivalAssistantLogic.NextHealthState(ReportedHealthState.Unknown) == ReportedHealthState.Stable
        && SurvivalAssistantLogic.NextHealthState(ReportedHealthState.Stable) == ReportedHealthState.Hurt
        && SurvivalAssistantLogic.NextHealthState(ReportedHealthState.Hurt) == ReportedHealthState.Critical
        && SurvivalAssistantLogic.NextHealthState(ReportedHealthState.Critical) == ReportedHealthState.Unknown,
    "Reported health cycle failed");
Require(SurvivalAssistantLogic.HealthLabel(ReportedHealthState.Unknown) == "?"
        && SurvivalAssistantLogic.HealthLabel(ReportedHealthState.Stable) == "OK"
        && SurvivalAssistantLogic.HealthLabel(ReportedHealthState.Hurt) == "HURT"
        && SurvivalAssistantLogic.HealthLabel(ReportedHealthState.Critical) == "CRIT",
    "Reported health labels failed");
var vomit = SurvivalAssistantLogic.Find("vomit")!.Value;
Require(SurvivalAssistantLogic.AddVomitStack(0) == 300
        && SurvivalAssistantLogic.AddVomitStack(300) == 600
        && SurvivalAssistantLogic.AddVomitStack(int.MaxValue)
            == SurvivalAssistantLogic.MaximumAdditionalVomitSeconds,
    "Additional-vomit stacking bounds failed");
var activeVomitClock = SurvivalAssistantLogic.ReportAdditionalVomit(
    start, 0, start.AddSeconds(60));
var expiredVomitClock = SurvivalAssistantLogic.ReportAdditionalVomit(
    start, 300, start.AddSeconds(1000));
Require(!activeVomitClock.Restarted
        && activeVomitClock.StartedAt == start
        && activeVomitClock.AdditionalSeconds == 300
        && expiredVomitClock.Restarted
        && expiredVomitClock.StartedAt == start.AddSeconds(1000)
        && expiredVomitClock.AdditionalSeconds == 0
        && SurvivalAssistantLogic.RemainingSeconds(
            vomit,
            expiredVomitClock.StartedAt,
            start.AddSeconds(1000),
            expiredVomitClock.AdditionalSeconds) == 300,
    "Expired additional-vomit restart failed");
Require(SurvivalAssistantLogic.TotalExpectedSeconds(vomit, 300) == 600
        && SurvivalAssistantLogic.RemainingSeconds(vomit, start, start.AddSeconds(60), 300) == 540,
    "Stacked vomit countdown failed");
Require(Math.Abs(SurvivalAssistantLogic.RemainingRatio(
            vomit, start, start.AddSeconds(60), 300) - 0.9) < 0.0001,
    "Stacked vomit progress failed");
Require(SurvivalAssistantLogic.NormalizeAdditionalSeconds(fracture, 300) == 0,
    "Non-vomit duration extension failed");
Require(SurvivalAssistantLogic.IsFinalMinute(60)
        && SurvivalAssistantLogic.IsFinalMinute(1)
        && !SurvivalAssistantLogic.IsFinalMinute(61)
        && !SurvivalAssistantLogic.IsFinalMinute(0),
    "Final-minute phase failed");
Require(SurvivalAssistantLogic.FooterLabel(null, start, start) == "VOMIT WARNING? START 5M"
        && SurvivalAssistantLogic.FooterLabel(vomit, start, start.AddSeconds(60)) == "SICK 4:00"
        && SurvivalAssistantLogic.FooterLabel(vomit, start, start.AddSeconds(301)) == "SICK CHECK",
    "One-click sickness footer failed");
var activeVomitPresentation = SurvivalAssistantLogic.Presentation(
    vomit, start, start.AddSeconds(60));
var expiredVomitPresentation = SurvivalAssistantLogic.Presentation(
    vomit, start, start.AddSeconds(301));
var foodPoisoning = SurvivalAssistantLogic.Find("food-poisoning")!.Value;
var expiredFoodPresentation = SurvivalAssistantLogic.Presentation(
    foodPoisoning, start, start.AddSeconds(421));
Require(activeVomitPresentation.StopEatingWarningActive
        && !activeVomitPresentation.RequiresGameCheck
        && activeVomitPresentation.Priority.Contains("STOP EATING", StringComparison.Ordinal),
    "Fresh player-reported in-game warning must retain immediate stop-eating guidance");
Require(!expiredVomitPresentation.StopEatingWarningActive
        && expiredVomitPresentation.RequiresGameCheck
        && expiredVomitPresentation.Priority == "CHECK IN-GAME WARNING"
        && !string.Join(' ', expiredVomitPresentation.Steps)
            .Contains("STOP EATING", StringComparison.OrdinalIgnoreCase)
        && !expiredVomitPresentation.HudSteps
            .Contains("STOP EATING", StringComparison.OrdinalIgnoreCase)
        && !expiredFoodPresentation.StopEatingWarningActive
        && expiredFoodPresentation.RequiresGameCheck,
    "Expired stop-eating guidance must withdraw and defer to the game warning");
Require(SurvivalAssistantLogic.ShouldRestoreIncident(
            vomit, start, start.AddSeconds(60))
        && !SurvivalAssistantLogic.ShouldRestoreIncident(
            vomit, start, start.AddSeconds(301))
        && SurvivalAssistantLogic.ShouldRestoreIncident(
            fracture, start, start.AddHours(1)),
    "Only current stop-eating evidence may restore after an app restart");
Require(SurvivalAssistantLogic.CompactSummary(vomit, start, start.AddSeconds(301))
        == "STATUS SICK · CHECK IN-GAME WARNING · CHECK GAME",
    "Expired sickness summary must not retain the stop-eating warning");
var idleQuickAction = SurvivalAssistantLogic.QuickAction(null, start, start);
var activeQuickAction = SurvivalAssistantLogic.QuickAction(vomit, start, start.AddSeconds(60));
var expiredQuickAction = SurvivalAssistantLogic.QuickAction(vomit, start, start.AddSeconds(301));
var fractureQuickAction = SurvivalAssistantLogic.QuickAction(fracture, start, start.AddSeconds(60));
Require(idleQuickAction is
        {
            Kind: SurvivalQuickActionKind.StartVomit,
            Label: "VOMIT WARNING? START 5M"
        }
        && activeQuickAction is
        {
            Kind: SurvivalQuickActionKind.ReportAdditionalVomit,
            Label: "SICK 4:00 · +5M"
        }
        && expiredQuickAction is
        {
            Kind: SurvivalQuickActionKind.ReportAdditionalVomit,
            Label: "WARNING STILL ON? · +5M"
        }
        && fractureQuickAction.Kind == SurvivalQuickActionKind.OpenActiveIncident,
    "Vomit report/restart quick-action contract failed");
Require(SurvivalAssistantLogic.HudSteps(vomit).Split('\n').Length == 3
        && SurvivalAssistantLogic.HudSteps(vomit).Contains("salt lick", StringComparison.OrdinalIgnoreCase),
    "Always-visible recovery instruction failed");
Require(expiredVomitPresentation.HudSteps.Split('\n').Length == 3
        && expiredVomitPresentation.HudSteps.Split('\n').All(line => line.Length <= 64),
    "Expired game-check HUD must remain compact");
foreach (var incident in SurvivalAssistantLogic.Incidents)
{
    var hudLines = SurvivalAssistantLogic.HudSteps(incident).Split('\n');
    Require(hudLines.Length == 3
            && hudLines.Select((line, index) => line.StartsWith($"{index + 1}  ", StringComparison.Ordinal))
                .All(valid => valid)
            && hudLines.All(line => line.Length <= 64),
        $"Compact pressure-readable HUD copy failed for {incident.Id}");
}
var expandedHud = SurvivalAssistantLogic.HudPresentation("vomit", requestedCollapsed: false);
var collapsedHud = SurvivalAssistantLogic.HudPresentation("vomit", requestedCollapsed: true);
var idleHud = SurvivalAssistantLogic.HudPresentation(string.Empty, requestedCollapsed: true);
Require(!expandedHud.IsCollapsed && expandedHud.ShowDetails && expandedHud.ToggleLabel == "LESS",
    "Expanded recovery HUD presentation failed");
Require(collapsedHud.IsCollapsed && !collapsedHud.ShowDetails && collapsedHud.ToggleLabel == "MORE",
    "Compact recovery HUD presentation failed");
Require(!idleHud.IsCollapsed && idleHud.ShowDetails,
    "Inactive recovery HUD normalization failed");
Require(SurvivalAssistantLogic.StatusBeaconLabel(
            ReportedHealthState.Hurt, vomit, start, start.AddSeconds(60))
        == "HP HURT · SICK 4:00",
    "Active status beacon failed");
Require(SurvivalAssistantLogic.StatusBeaconLabel(
            ReportedHealthState.Unknown, null, start, start)
        == "HP ? · REPORT",
    "Idle status beacon failed");
Require(SurvivalAssistantLogic.StatusBeaconLabel(
            ReportedHealthState.Unknown, vomit, start, start.AddSeconds(301))
        == "HP ? · SICK CHECK",
    "Expired sickness beacon must defer to an in-game check");

var root = Directory.GetCurrentDirectory();
var mainWindowSource = string.Join("\n", Directory.GetFiles(Path.Combine(root, "BurntHud"), "MainWindow*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText));
var mainWindowXaml = File.ReadAllText(Path.Combine(root, "BurntHud", "MainWindow.xaml"));
var appXaml = File.ReadAllText(Path.Combine(root, "BurntHud", "App.xaml"));
Require(mainWindowSource.Contains("SurvivalRecoveryButton.Content = remedy.ActionLabel", StringComparison.Ordinal)
        && mainWindowSource.Contains("SurvivalRecoveryButton.ToolTip = remedy.Tooltip", StringComparison.Ordinal),
    "Recovery action presentation wiring failed");
Require(mainWindowXaml.Contains("x:Name=\"SurvivalVomitStartButton\"", StringComparison.Ordinal)
        && mainWindowXaml.Contains("Content=\"IN-GAME VOMIT WARNING · START 5:00\"", StringComparison.Ordinal)
        && mainWindowXaml.Contains("Content=\"VOMIT WARNING? START 5M\"", StringComparison.Ordinal)
        && mainWindowXaml.Contains("Click=\"SurvivalQuickButton_Click\"", StringComparison.Ordinal)
        && mainWindowSource.Contains("SurvivalVomitStartButton.Visibility", StringComparison.Ordinal)
        && mainWindowSource.Contains("await ReportAdditionalVomitAsync();", StringComparison.Ordinal),
    "Discoverable one-press Vomit Help surface failed");
Require(mainWindowSource.Contains("OpenResourceFinderForQueryAsync(", StringComparison.Ordinal)
        && mainWindowSource.Contains("await LoadGatewayResourceNetworkAsync();", StringComparison.Ordinal)
        && mainWindowSource.Contains("RecoveryRemedyKind.ResourceFinder", StringComparison.Ordinal),
    "Current resource-source handoff failed");
Require(mainWindowSource.Contains("RecoveryRemedyKind.FoodLayer", StringComparison.Ordinal)
        && mainWindowSource.Contains("RecoveryRemedyKind.DietCoach", StringComparison.Ordinal)
        && mainWindowSource.Contains("CurrentDietResourceQuery()", StringComparison.Ordinal),
    "Food recovery branch wiring failed");
Require(!mainWindowSource.Contains("incident.RoutePinType", StringComparison.Ordinal)
        && mainWindowXaml.Split("x:Name=\"SurvivalRecoveryButton\"").Length - 1 == 1,
    "Single-action recovery surface failed");
Require(appXaml.Contains(
            "<Setter Property=\"Foreground\" Value=\"{StaticResource PrimaryTextBrush}\" />",
            StringComparison.Ordinal)
        && appXaml.Contains(
            "<SolidColorBrush x:Key=\"ActiveToggleBrush\" Color=\"#FF075985\" />",
            StringComparison.Ordinal)
        && mainWindowSource.Contains(
            "button.Foreground = (Brush)FindResource(\"PrimaryTextBrush\");",
            StringComparison.Ordinal)
        && mainWindowXaml.Contains("FontSize=\"8.25\"", StringComparison.Ordinal)
        && mainWindowXaml.Contains("LineHeight=\"10.5\"", StringComparison.Ordinal)
        && mainWindowXaml.Contains("TextTrimming=\"CharacterEllipsis\"", StringComparison.Ordinal),
    "Live-audited hover contrast or compact reading treatment failed");

Console.WriteLine("Survival assistant: PASS (11 conditions, game-warning-gated stop-eating guidance, stale restore refusal, concise HUD steps, explicit vomit start/reconfirm actions, contextual remedies, expandable guidance, stacked countdowns, manual health beacon, hover contrast, and summaries)");
