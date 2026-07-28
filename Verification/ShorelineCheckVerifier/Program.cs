using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var now = new DateTimeOffset(2026, 7, 22, 18, 0, 0, TimeSpan.Zero);

ShorelineCheckSnapshot Baseline() => new(
    StreamerMode: false,
    Active: true,
    StartedAt: now - TimeSpan.FromSeconds(10),
    Now: now,
    LiveContactFeedAvailable: true,
    PositionFresh: true,
    SurvivalUrgency: 0,
    SurvivalId: string.Empty,
    SurvivalLabel: string.Empty,
    Health: ReportedHealthState.Stable,
    HealthFresh: true,
    Water: ReportedVitalState.Stable,
    WaterFresh: true,
    Stamina: ReportedVitalState.Stable,
    StaminaFresh: true,
    EncounterCount: 0,
    EncounterDistance: null,
    EncounterCardinal: string.Empty,
    EncounterMotion: string.Empty,
    EncounterMotionSampleCount: 0,
    DangerWarning: false,
    InsideAlertZone: false,
    Weather: FieldWeather.Clear,
    WeatherFresh: true,
    SpeciesId: "allosaurus",
    SpeciesKnown: true);

var hidden = ShorelineCheckLogic.Evaluate(Baseline() with { StreamerMode = true });
Check(hidden.State == ShorelineCheckState.Hidden && !hidden.IsVisible && string.IsNullOrEmpty(hidden.Detail),
    "Streamer redaction failed");

var off = ShorelineCheckLogic.Evaluate(Baseline() with { Active = false });
Check(off.State == ShorelineCheckState.Off && off.IsVisible && !off.IsCurrent
      && off.Detail.Contains("does not detect hidden animals", StringComparison.OrdinalIgnoreCase),
    "Inactive honesty failed");

var expired = ShorelineCheckLogic.Evaluate(Baseline() with { StartedAt = now - TimeSpan.FromSeconds(76) });
Check(expired is { State: ShorelineCheckState.Off, RemainingSeconds: 0, ActionId: "shoreline-check" }
      && expired.Heading.Contains("EXPIRED", StringComparison.Ordinal),
    "Snapshot expiry failed");

Check(ShorelineCheckLogic.RemainingSeconds(now + TimeSpan.FromMinutes(1), now) == 75
      && ShorelineCheckLogic.RemainingSeconds(now - TimeSpan.FromMinutes(3), now) == 0,
    "Timer bounds failed");

var waterEmpty = ShorelineCheckLogic.Evaluate(Baseline() with
{
    Water = ReportedVitalState.Empty,
    PositionFresh = false,
    EncounterCount = 1,
    EncounterDistance = 4,
    EncounterMotion = "closing",
    EncounterMotionSampleCount = 3,
    DangerWarning = true
});
Check(waterEmpty is { State: ShorelineCheckState.Urgent, Severity: 3, ActionId: "core-vitals" }
      && waterEmpty.Heading.Contains("DRINK NOW", StringComparison.Ordinal),
    "Empty-water priority failed");

var recovery = ShorelineCheckLogic.Evaluate(Baseline() with
{
    SurvivalUrgency = 2,
    SurvivalId = "vomit",
    SurvivalLabel = "Vomit sickness"
});
Check(recovery is { State: ShorelineCheckState.Hold, ActionId: "survival-assistant" }
      && recovery.Detail.Contains("Vomit sickness", StringComparison.Ordinal),
    "Recovery-first hold failed");

var dehydration = ShorelineCheckLogic.Evaluate(Baseline() with
{
    SurvivalUrgency = 3,
    SurvivalId = "dehydrated",
    SurvivalLabel = "Dehydrated",
    Water = ReportedVitalState.Low
});
Check(dehydration is { State: ShorelineCheckState.Caution, ActionId: "core-vitals" }
      && dehydration.Heading.Contains("DRINK", StringComparison.Ordinal),
    "Dehydration exception failed");

var hurt = ShorelineCheckLogic.Evaluate(Baseline() with { Health = ReportedHealthState.Hurt });
Check(hurt is { State: ShorelineCheckState.Hold, ActionId: "core-vitals", Severity: 2 },
    "Hurt-health hold failed");

var stamina = ShorelineCheckLogic.Evaluate(Baseline() with { Stamina = ReportedVitalState.Empty });
Check(stamina is { State: ShorelineCheckState.Hold, ActionId: "core-vitals", Severity: 3 }
      && stamina.Heading.Contains("STAMINA EMPTY", StringComparison.Ordinal),
    "Empty-stamina hold failed");

var universal = ShorelineCheckLogic.Evaluate(Baseline() with { LiveContactFeedAvailable = false });
Check(universal is { State: ShorelineCheckState.Verify, ActionId: "field-guide" }
      && universal.Detail.Contains("no authorized live contact feed", StringComparison.OrdinalIgnoreCase),
    "Universal-session honesty failed");

var waiting = ShorelineCheckLogic.Evaluate(Baseline() with { PositionFresh = false });
Check(waiting is { State: ShorelineCheckState.Verify, ActionId: "recenter" },
    "Stale-position refusal failed");

var danger = ShorelineCheckLogic.Evaluate(Baseline() with { InsideAlertZone = true });
Check(danger is { State: ShorelineCheckState.Hold, ActionId: "escape-route", Severity: 3 }
      && danger.Badge == "INSIDE WARNING",
    "Saved-boundary hold failed");

var missingRange = ShorelineCheckLogic.Evaluate(Baseline() with
{
    EncounterCount = 1,
    EncounterDistance = double.NaN
});
Check(missingRange is { State: ShorelineCheckState.Verify, ActionId: "players" },
    "Invalid contact-range refusal failed");

var close = ShorelineCheckLogic.Evaluate(Baseline() with
{
    EncounterCount = 1,
    EncounterDistance = 12,
    EncounterCardinal = "sw"
});
Check(close is { State: ShorelineCheckState.Hold, ActionId: "escape-route", Severity: 3 }
      && close.Detail.Contains("12.0 MU SW", StringComparison.Ordinal),
    "Close-contact hold failed");

var closing = ShorelineCheckLogic.Evaluate(Baseline() with
{
    EncounterCount = 1,
    EncounterDistance = 29.5,
    EncounterMotion = "closing",
    EncounterMotionSampleCount = 3
});
Check(closing is { State: ShorelineCheckState.Hold, Badge: "CONTACT CLOSING" },
    "Calibrated closing-contact hold failed");

var uncalibrated = ShorelineCheckLogic.Evaluate(Baseline() with
{
    EncounterCount = 1,
    EncounterDistance = 20,
    EncounterMotion = "closing",
    EncounterMotionSampleCount = 2
});
Check(uncalibrated is { State: ShorelineCheckState.Caution, Badge: "CONTACT NEAR" },
    "Uncalibrated-motion restraint failed");

var waterLow = ShorelineCheckLogic.Evaluate(Baseline() with { Water = ReportedVitalState.Low });
Check(waterLow is { State: ShorelineCheckState.Caution, ActionId: "core-vitals" }
      && waterLow.Heading.Contains("DRINK", StringComparison.Ordinal),
    "Low-water drinking guidance failed");

var fog = ShorelineCheckLogic.Evaluate(Baseline() with { Weather = FieldWeather.Fog });
Check(fog is { State: ShorelineCheckState.Caution, ActionId: "field-conditions" }
      && fog.Heading.Contains("VISIBILITY", StringComparison.Ordinal),
    "Fog caution failed");

var waterUnknown = ShorelineCheckLogic.Evaluate(Baseline() with { WaterFresh = false });
Check(waterUnknown is { State: ShorelineCheckState.Verify, ActionId: "core-vitals" }
      && waterUnknown.Badge == "WATER UNKNOWN",
    "Missing-water report failed");

var window = ShorelineCheckLogic.Evaluate(Baseline());
Check(window is { State: ShorelineCheckState.Window, Severity: 0, ActionId: "shoreline-check-clear" }
      && window.Heading.Contains("NO REPORTED BLOCKER", StringComparison.Ordinal)
      && window.Detail.Contains("not a safety guarantee", StringComparison.OrdinalIgnoreCase)
      && window.Detail.Contains("hidden animals", StringComparison.OrdinalIgnoreCase),
    "Bounded drinking window failed");

Check(ShorelineCheckLogic.SpeciesCue("pteranodon", true).Contains("takeoff", StringComparison.OrdinalIgnoreCase)
      && ShorelineCheckLogic.SpeciesCue("deinosuchus", true).Contains("another aquatic", StringComparison.OrdinalIgnoreCase)
      && ShorelineCheckLogic.SpeciesCue("", false).Contains("both banks", StringComparison.OrdinalIgnoreCase),
    "Species cue selection failed");

Check(ShorelineCheckLogic.BriefLabel(window) == "SHORELINE WINDOW"
      && string.IsNullOrEmpty(ShorelineCheckLogic.BriefLabel(expired))
      && !ShorelineCheckLogic.BriefLabel(window).Contains("MU", StringComparison.Ordinal),
    "Anonymous brief label failed");

var root = Directory.GetCurrentDirectory();
var source = string.Join("\n", Directory.GetFiles(Path.Combine(root, "BurntHud"), "MainWindow*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText));
var xaml = File.ReadAllText(Path.Combine(root, "BurntHud", "MainWindow.xaml"));
Check(xaml.Split("x:Name=\"ShorelineCheckSectionAnchor\"").Length - 1 == 1
      && xaml.Contains("x:Name=\"ShorelineCheckToggleButton\"", StringComparison.Ordinal)
      && xaml.Contains("x:Name=\"ShorelineCheckResultPanel\"", StringComparison.Ordinal)
      && xaml.Contains("x:Name=\"ShorelineCheckActionButton\"", StringComparison.Ordinal),
    "Single cardless Shoreline Check surface failed");
Check(xaml.IndexOf("x:Name=\"ShorelineCheckSectionAnchor\"", StringComparison.Ordinal)
      > xaml.IndexOf("x:Name=\"CoreVitalsSectionAnchor\"", StringComparison.Ordinal)
      && xaml.IndexOf("x:Name=\"ShorelineCheckSectionAnchor\"", StringComparison.Ordinal)
      < xaml.IndexOf("x:Name=\"SurvivalAssistantSectionAnchor\"", StringComparison.Ordinal)
      && !xaml.Contains("x:Name=\"ShorelineCheckHud", StringComparison.Ordinal),
    "Drawer placement or permanent-HUD exclusion failed");
Check(source.Contains("private ShorelineCheckView CurrentShorelineCheckView", StringComparison.Ordinal)
      && source.Contains("private void UpdateShorelineCheck", StringComparison.Ordinal)
      && source.Contains("StartShorelineCheckAsync", StringComparison.Ordinal)
      && source.Contains("ResetShorelineCheck", StringComparison.Ordinal),
    "Shoreline lifecycle wiring failed");
Check(source.Contains("new(\"shoreline-check\", \"Start Shoreline Check\"", StringComparison.Ordinal)
      && source.Contains("case \"shoreline-check\":", StringComparison.Ordinal)
      && source.Contains("\"shoreline-check\" => ShorelineCheckSectionAnchor", StringComparison.Ordinal),
    "Quick Commands discovery and exact drawer jump failed");
Check(source.Split("UpdateShorelineCheck();").Length - 1 >= 1
      && source.Split("UpdateShorelineCheck(force: true)").Length - 1 >= 4
      && source.Contains("ShorelineCheckBriefLabel", StringComparison.Ordinal)
      && source.Contains("AddTacticalEvent(\n                    \"SHORELINE\"", StringComparison.Ordinal),
    "Continuous refresh, brief, or private-log integration failed");

Console.WriteLine("Shoreline Check: PASS (75-second expiry, hydration-first triage, recovery/vitals/contact/boundary/weather priorities, species cues, universal fallback, privacy, one action, and no permanent map card)");
