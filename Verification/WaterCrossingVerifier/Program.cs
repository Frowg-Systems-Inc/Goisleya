using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static WaterCrossingSnapshot Baseline() => new(
    StreamerMode: false,
    LiveMapAvailable: true,
    Active: true,
    MeasurementArmed: false,
    MeasurementHasStart: true,
    Distance: 15,
    SpeciesId: "allosaurus",
    SpeciesKnown: true,
    SurvivalUrgency: 0,
    SurvivalLabel: string.Empty,
    Health: ReportedHealthState.Stable,
    HealthFresh: true,
    Stamina: ReportedVitalState.Stable,
    StaminaFresh: true,
    Weather: FieldWeather.Clear,
    WeatherFresh: true,
    EncounterDistance: null,
    EncounterMotion: string.Empty,
    DangerActive: false,
    MarkedBoundaryCount: 0,
    InsideMarkedBoundary: false);

var hidden = WaterCrossingLogic.Evaluate(Baseline() with { StreamerMode = true });
Check(!hidden.IsVisible && hidden.State == WaterCrossingState.Hidden,
    "Streamer redaction failed");

var off = WaterCrossingLogic.Evaluate(Baseline() with { Active = false });
Check(off is { State: WaterCrossingState.Off, ActionId: "measure-crossing", ActionLabel: "START CHECK" }
      && off.Detail.Contains("not depth", StringComparison.OrdinalIgnoreCase),
    "Off-state discovery failed");

var mapWaiting = WaterCrossingLogic.Evaluate(Baseline() with { LiveMapAvailable = false });
Check(mapWaiting is { State: WaterCrossingState.Measure, ActionId: "navigation" }
      && mapWaiting.Detail.Contains("calibrated Live Map ruler", StringComparison.Ordinal),
    "Live-map boundary failed");

var entry = WaterCrossingLogic.Evaluate(Baseline() with
{
    Distance = null,
    MeasurementArmed = true,
    MeasurementHasStart = false
});
var exit = WaterCrossingLogic.Evaluate(Baseline() with
{
    Distance = null,
    MeasurementArmed = true,
    MeasurementHasStart = true
});
Check(entry.Heading == "MARK THE ENTRY BANK" && entry.HudLabel == "SELECT ENTRY"
      && exit.Heading == "MARK THE EXIT BANK" && exit.HudLabel == "SELECT EXIT",
    "Two-bank measurement sequence failed");
Check(WaterCrossingLogic.Evaluate(Baseline() with { Distance = double.NaN }).State
      == WaterCrossingState.Measure,
    "Invalid measurement refusal failed");

var recovery = WaterCrossingLogic.Evaluate(Baseline() with
{
    SurvivalUrgency = 2,
    SurvivalLabel = "Vomit sickness",
    EncounterDistance = 4,
    EncounterMotion = "closing"
});
Check(recovery is { State: WaterCrossingState.Hold, ActionId: "survival-assistant", Severity: 3 }
      && recovery.Detail.Contains("Vomit sickness", StringComparison.Ordinal),
    "Recovery priority failed");

var criticalHp = WaterCrossingLogic.Evaluate(Baseline() with { Health = ReportedHealthState.Critical });
Check(criticalHp is { State: WaterCrossingState.Hold, ActionId: "core-vitals", Severity: 3 },
    "Critical health hold failed");

var lowStamina = WaterCrossingLogic.Evaluate(Baseline() with { Stamina = ReportedVitalState.Low });
var emptyStamina = WaterCrossingLogic.Evaluate(Baseline() with { Stamina = ReportedVitalState.Empty });
Check(lowStamina is { State: WaterCrossingState.Hold, Severity: 2 }
      && emptyStamina is { State: WaterCrossingState.Hold, Severity: 3 }
      && emptyStamina.Detail.Contains("does not predict", StringComparison.OrdinalIgnoreCase),
    "Stamina gate failed");

var contact = WaterCrossingLogic.Evaluate(Baseline() with
{
    EncounterDistance = 25,
    EncounterMotion = "closing"
});
Check(contact is { State: WaterCrossingState.Hold, ActionId: "players", Severity: 3 },
    "Closing-contact boundary failed");
Check(WaterCrossingLogic.Evaluate(Baseline() with
{
    EncounterDistance = 25.1,
    EncounterMotion = "closing"
}).State == WaterCrossingState.Ready,
    "Closing-contact outer boundary failed");

var inside = WaterCrossingLogic.Evaluate(Baseline() with { InsideMarkedBoundary = true });
var marked = WaterCrossingLogic.Evaluate(Baseline() with { MarkedBoundaryCount = 2 });
Check(inside is { State: WaterCrossingState.Hold, ActionId: "no-go-areas", Severity: 3 }
      && marked is { State: WaterCrossingState.Caution, ActionId: "no-go-areas", Severity: 2 }
      && marked.Detail.Contains("2 saved warning boundaries", StringComparison.Ordinal),
    "Marked-boundary risk failed");

Check(WaterCrossingLogic.Evaluate(Baseline() with { DangerActive = true }) is
      { State: WaterCrossingState.Caution, ActionId: "alert-zones" },
    "Nearby Danger handoff failed");
Check(WaterCrossingLogic.Evaluate(Baseline() with { Weather = FieldWeather.Fog }) is
      { State: WaterCrossingState.Caution, ActionId: "field-conditions" },
    "Fresh weather warning failed");

var missingVitals = WaterCrossingLogic.Evaluate(Baseline() with
{
    HealthFresh = false,
    StaminaFresh = false
});
Check(missingVitals is { State: WaterCrossingState.Verify, ActionId: "core-vitals" }
      && missingVitals.Heading.Contains("HP + STAMINA", StringComparison.Ordinal),
    "Fresh-vitals honesty failed");

var missingSpecies = WaterCrossingLogic.Evaluate(Baseline() with { SpeciesKnown = false });
Check(missingSpecies is { State: WaterCrossingState.Verify, ActionId: "diet-coach" },
    "Current-species handoff failed");
var missingField = WaterCrossingLogic.Evaluate(Baseline() with { WeatherFresh = false });
Check(missingField is { State: WaterCrossingState.Verify, ActionId: "field-conditions" },
    "Unreported-field verification failed");

var aerial = WaterCrossingLogic.Evaluate(Baseline() with { SpeciesId = "pteranodon", Distance = 80 });
var aquatic = WaterCrossingLogic.Evaluate(Baseline() with { SpeciesId = "deinosuchus", Distance = 80 });
var semiAquatic = WaterCrossingLogic.Evaluate(Baseline() with { SpeciesId = "beipiaosaurus", Distance = 80 });
Check(aerial is { State: WaterCrossingState.Ready, Key: "ready-aerial" }
      && aquatic is { State: WaterCrossingState.Ready, Key: "ready-aquatic" }
      && semiAquatic is { State: WaterCrossingState.Ready, Key: "ready-semi-aquatic" }
      && aquatic.Detail.Contains("larger aquatic threats", StringComparison.OrdinalIgnoreCase),
    "Species-mode guidance failed");

var shortSpan = WaterCrossingLogic.Evaluate(Baseline() with { Distance = 20 });
var mediumSpan = WaterCrossingLogic.Evaluate(Baseline() with { Distance = 20.1 });
var mediumLimit = WaterCrossingLogic.Evaluate(Baseline() with { Distance = 50 });
var longSpan = WaterCrossingLogic.Evaluate(Baseline() with { Distance = 50.1 });
Check(shortSpan is { State: WaterCrossingState.Ready, Key: "ready-short-span" }
      && mediumSpan is { State: WaterCrossingState.Caution, Key: "caution-medium-span" }
      && mediumLimit.Key == "caution-medium-span"
      && longSpan is { State: WaterCrossingState.Caution, Key: "caution-long-span" }
      && WaterCrossingLogic.ExposureLabel(20) == "SHORT"
      && WaterCrossingLogic.ExposureLabel(50) == "MEDIUM"
      && WaterCrossingLogic.ExposureLabel(50.1) == "LONG",
    "Map-exposure band boundaries failed");
Check(longSpan.Detail.Contains("not a swim-range prediction", StringComparison.OrdinalIgnoreCase),
    "Long-span limitation failed");

var root = Directory.GetCurrentDirectory();
var mainWindowSource = string.Join("\n", Directory.GetFiles(Path.Combine(root, "BurntHud"), "MainWindow*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText)) + "\n" + File.ReadAllText(Path.Combine(root, "BurntHud", "Map", "isley-map-controller.js"));
var mainWindowXaml = File.ReadAllText(Path.Combine(root, "BurntHud", "MainWindow.xaml"));
Check(mainWindowXaml.Split("x:Name=\"WaterCrossingResultPanel\"").Length - 1 == 1
      && mainWindowXaml.Contains("x:Name=\"WaterCrossingToggleButton\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("x:Name=\"WaterCrossingHeadingText\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("x:Name=\"WaterCrossingDetailText\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("x:Name=\"WaterCrossingActionButton\"", StringComparison.Ordinal),
    "Single cardless Water Crossing surface failed");
Check(mainWindowXaml.Contains("x:Name=\"MeasurementHeadingText\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("MeasurementHeadingText.Text = \"WATER CROSSING\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("MeasurementHeadingText.Text = _waterCrossingCheckActive ? \"WATER CROSSING\" : \"MAP RULER\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("var measurementAccent = _waterCrossingCheckActive", StringComparison.Ordinal)
      && mainWindowSource.Contains("? $\"{crossingView.HudLabel} · VERIFY IN GAME\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("MeasurementDetailText.ToolTip = crossingView.Detail", StringComparison.Ordinal)
      && !mainWindowXaml.Contains("x:Name=\"WaterCrossingHudBorder\"", StringComparison.Ordinal),
    "Existing-ruler HUD reuse or live-state persistence failed");
Check(mainWindowSource.Contains("const calculateDirectRouteObstacleRisk", StringComparison.Ordinal)
      && mainWindowSource.Contains("measurementMarkedBoundaryCount", StringComparison.Ordinal)
      && mainWindowSource.Contains("measurementInsideMarkedBoundary", StringComparison.Ordinal)
      && mainWindowSource.Contains("...buildMeasurementState()", StringComparison.Ordinal),
    "Measurement-line obstacle bridge failed");
Check(mainWindowSource.Contains("new(\"water-crossing\", \"Start Water Crossing Check\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("case \"water-crossing\":", StringComparison.Ordinal)
      && mainWindowSource.Contains("\"water-crossing\" => WaterCrossingSectionAnchor", StringComparison.Ordinal),
    "Command discovery and focused navigation failed");
Check(mainWindowSource.Contains("WaterCrossingBriefLabel()", StringComparison.Ordinal)
      && mainWindowSource.Contains("var waterCrossing = CurrentWaterCrossingView();", StringComparison.Ordinal)
      && mainWindowSource.Contains("AddTacticalEvent(\n                    \"CROSSING\"", StringComparison.Ordinal),
    "Next Move, brief, or tactical-log integration failed");
Check(mainWindowSource.Contains("ResetWaterCrossingCheck(logEvent: false);", StringComparison.Ordinal)
      && mainWindowSource.Contains("UpdateWaterCrossingCheck(force: true)", StringComparison.Ordinal),
    "Session lifecycle cleanup or refresh failed");

Console.WriteLine("Water Crossing Check: PASS (bank measurement, recovery/HP/stamina/contact/weather/Danger/No-Go priorities, species modes, exposure bands, ruler HUD reuse, Next Move, brief, log, and session cleanup)");
