using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static TripReadinessSnapshot Baseline() => new(
    StreamerMode: false,
    LiveMapAvailable: true,
    HasDestination: true,
    PositionFresh: true,
    RemainingDistance: 87.5,
    SurvivalUrgency: 0,
    SurvivalLabel: string.Empty,
    Health: ReportedHealthState.Stable,
    HealthFresh: true,
    Food: ReportedVitalState.Stable,
    FoodFresh: true,
    Water: ReportedVitalState.Stable,
    WaterFresh: true,
    Stamina: ReportedVitalState.Stable,
    StaminaFresh: true,
    Weather: FieldWeather.Clear,
    WeatherFresh: true,
    Light: FieldLight.Day,
    LightFresh: true,
    EncounterDistance: null,
    EncounterMotion: string.Empty,
    DangerActive: false,
    InsideAlertZone: false,
    RouteObstacleCount: 0,
    InsideRouteObstacle: false,
    TerrainCourseReady: true,
    MovingAway: false);

var hidden = TripReadinessLogic.Evaluate(Baseline() with { StreamerMode = true });
Check(!hidden.IsVisible && hidden.State == TripReadinessState.Hidden && string.IsNullOrEmpty(hidden.ActionId),
    "Streamer redaction failed");
Check(TripReadinessLogic.Evaluate(Baseline() with { LiveMapAvailable = false }).State == TripReadinessState.Hidden,
    "Live-map boundary failed");

var plan = TripReadinessLogic.Evaluate(Baseline() with { HasDestination = false });
Check(plan is { State: TripReadinessState.Plan, ActionId: "navigation", ActionLabel: "SET ROUTE" },
    "No-trip planning handoff failed");

var recovery = TripReadinessLogic.Evaluate(Baseline() with
{
    SurvivalUrgency = 2,
    SurvivalLabel = "Vomit sickness",
    EncounterDistance = 4,
    EncounterMotion = "closing"
});
Check(recovery is { State: TripReadinessState.Hold, ActionId: "survival-assistant", Severity: 3 }
      && recovery.Detail.Contains("Vomit sickness", StringComparison.Ordinal),
    "Recovery priority failed");

var hp = TripReadinessLogic.Evaluate(Baseline() with
{
    Health = ReportedHealthState.Critical
});
Check(hp is { State: TripReadinessState.Hold, ActionId: "core-vitals", Severity: 3 },
    "Critical HP hold failed");

var emptyStamina = TripReadinessLogic.Evaluate(Baseline() with
{
    Stamina = ReportedVitalState.Empty
});
Check(emptyStamina.Heading == "HOLD · STAMINA EMPTY" && emptyStamina.ActionId == "core-vitals",
    "Empty stamina hold failed");

var contact = TripReadinessLogic.Evaluate(Baseline() with
{
    EncounterDistance = 25,
    EncounterMotion = "closing"
});
Check(contact is { State: TripReadinessState.Hold, ActionId: "players" }
      && contact.Detail.Contains("25.0 MU", StringComparison.Ordinal),
    "Closing-contact boundary failed");
Check(TripReadinessLogic.Evaluate(Baseline() with
{
    EncounterDistance = 25.1,
    EncounterMotion = "closing"
}).State == TripReadinessState.Ready,
    "Closing-contact outer boundary failed");

var waiting = TripReadinessLogic.Evaluate(Baseline() with { PositionFresh = false });
Check(waiting is { State: TripReadinessState.Waiting, ActionId: "recenter" },
    "Missing-position honesty failed");
Check(TripReadinessLogic.Evaluate(Baseline() with { RemainingDistance = double.NaN }).State
      == TripReadinessState.Waiting,
    "Invalid-distance refusal failed");

var zone = TripReadinessLogic.Evaluate(Baseline() with { InsideAlertZone = true });
Check(zone is { State: TripReadinessState.Hold, ActionId: "alert-zones" },
    "Entered alert-zone hold failed");
var danger = TripReadinessLogic.Evaluate(Baseline() with { DangerActive = true });
Check(danger is { State: TripReadinessState.Caution, ActionId: "alert-zones" },
    "Nearby danger caution failed");

var insideMarkedArea = TripReadinessLogic.Evaluate(Baseline() with
{
    InsideRouteObstacle = true,
    RouteObstacleCount = 2
});
Check(insideMarkedArea is { State: TripReadinessState.Hold, ActionId: "no-go-areas", Severity: 3 }
      && insideMarkedArea.Heading.Contains("INSIDE MARKED AREA", StringComparison.Ordinal),
    "Inside marked route area hold failed");
var markedCrossing = TripReadinessLogic.Evaluate(Baseline() with { RouteObstacleCount = 2 });
Check(markedCrossing is { State: TripReadinessState.Caution, ActionId: "terrain-course", ActionLabel: "PLOT COURSE" }
      && markedCrossing.Detail.Contains("2 marked boundaries", StringComparison.Ordinal),
    "Obstacle-aware course handoff failed");
var markedCrossingWithoutNetwork = TripReadinessLogic.Evaluate(Baseline() with
{
    RouteObstacleCount = 1,
    TerrainCourseReady = false
});
Check(markedCrossingWithoutNetwork is { State: TripReadinessState.Caution, ActionId: "no-go-areas", ActionLabel: "ROUTE CHECK" },
    "Unavailable-course route-check fallback failed");

var away = TripReadinessLogic.Evaluate(Baseline() with { MovingAway = true });
Check(away is { State: TripReadinessState.Caution, ActionId: "recenter" },
    "Moving-away correction failed");
var storm = TripReadinessLogic.Evaluate(Baseline() with { Weather = FieldWeather.Storm });
Check(storm is { State: TripReadinessState.Caution, ActionId: "field-conditions" }
      && storm.Heading.Contains("STORM", StringComparison.Ordinal),
    "Field-condition caution failed");

var lowStamina = TripReadinessLogic.Evaluate(Baseline() with { Stamina = ReportedVitalState.Low });
Check(lowStamina is { State: TripReadinessState.Caution, ActionId: "core-vitals" },
    "Low-stamina caution failed");

var resourceTrend = TripReadinessLogic.Evaluate(Baseline() with
{
    ResourceTrendWarning = true,
    ResourceTrendHeading = "FOOD LOW IN ABOUT 7M",
    ResourceTrendDetail = "Three fresh samples show food falling steadily."
});
Check(resourceTrend is { State: TripReadinessState.Caution, ActionId: "core-vitals", Severity: 2 }
      && resourceTrend.Heading == "FOOD LOW IN ABOUT 7M"
      && resourceTrend.Detail.Contains("87.5 MU remain", StringComparison.Ordinal),
    "Early resource-trend caution failed");

var partial = Baseline() with { FoodFresh = false, WaterFresh = false };
var verify = TripReadinessLogic.Evaluate(partial);
Check(verify is { State: TripReadinessState.Verify, ActionId: "core-vitals" }
      && verify.Heading == "VERIFY · VITALS 2/4"
      && TripReadinessLogic.VitalCoverage(partial) == 2,
    "Partial-vitals verification failed");

var ready = TripReadinessLogic.Evaluate(Baseline());
Check(ready is { State: TripReadinessState.Ready, ActionId: "close-tools", Severity: 0 }
      && ready.Detail.Contains("87.5 MU remain", StringComparison.Ordinal)
      && ready.Detail.Contains("verify", StringComparison.OrdinalIgnoreCase),
    "Ready-with-boundary state failed");

var root = Directory.GetCurrentDirectory();
var mainWindowSource = string.Join("\n", Directory.GetFiles(Path.Combine(root, "BurntHud"), "MainWindow*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText)) + "\n" + File.ReadAllText(Path.Combine(root, "BurntHud", "Map", "isley-map-controller.js"));
var mainWindowXaml = File.ReadAllText(Path.Combine(root, "BurntHud", "MainWindow.xaml"));
Check(mainWindowXaml.Split("x:Name=\"TripReadinessPanel\"").Length - 1 == 1
      && mainWindowXaml.Contains("x:Name=\"TripReadinessHeadingText\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("x:Name=\"TripReadinessDetailText\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("x:Name=\"TripReadinessActionButton\"", StringComparison.Ordinal),
    "Single cardless Trip Check surface failed");
Check(mainWindowSource.Contains("private TripReadinessView CurrentTripReadinessView()", StringComparison.Ordinal)
      && mainWindowSource.Contains("private void UpdateTripReadiness(bool force = false)", StringComparison.Ordinal)
      && mainWindowSource.Contains("private async void TripReadinessActionButton_Click", StringComparison.Ordinal),
    "Trip Check presentation wiring failed");
Check(mainWindowSource.Contains("new(\"trip-check\", \"Open Trip Check\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("\"trip-check\" => TripReadinessSectionAnchor", StringComparison.Ordinal)
      && mainWindowSource.Contains("case \"trip-check\":", StringComparison.Ordinal),
    "Trip Check discovery and focused navigation failed");
Check(mainWindowSource.Split("UpdateTripReadiness();").Length - 1 >= 3
      && mainWindowSource.Contains("UpdateTripReadiness(force: true)", StringComparison.Ordinal)
      && mainWindowSource.Contains("string.Equals(actionId, \"close-tools\"", StringComparison.Ordinal),
    "Live refresh or map-return handoff failed");
Check(mainWindowSource.Contains("const calculateDirectRouteObstacleRisk", StringComparison.Ordinal)
      && mainWindowSource.Contains("const buildTripRouteRiskState", StringComparison.Ordinal)
      && mainWindowSource.Contains("...buildTripRouteRiskState()", StringComparison.Ordinal)
      && mainWindowSource.Contains("_tripRouteObstacleCount", StringComparison.Ordinal)
      && mainWindowSource.Contains("_tripRouteInsideObstacle", StringComparison.Ordinal),
    "Authorized direct-route obstacle bridge failed");
Check(!mainWindowXaml[..mainWindowXaml.IndexOf("x:Name=\"TripReadinessPanel\"", StringComparison.Ordinal)]
          .Contains("x:Name=\"TripReadiness", StringComparison.Ordinal),
    "Trip Check duplicated into the permanent map HUD");

Console.WriteLine("Trip Check: PASS (route gate, recovery/vitals/resource-trend/contact/danger/marked-crossing/field priorities, fresh-position honesty, currentness coverage, and explicit handoffs)");
