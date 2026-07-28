using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static FightCheckSnapshot Baseline() => new(
    StreamerMode: false,
    LiveContactFeedAvailable: true,
    PositionFresh: true,
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
    EncounterCount: 1,
    EncounterDistance: 72.5,
    EncounterCardinal: "NE",
    EncounterMotion: "steady",
    EncounterMotionSampleCount: 3,
    PackSpreadAlert: false,
    PackFriendCount: 2,
    PackSpread: 20,
    AbortCondition: "Abort when your exit closes.");

var hidden = FightCheckLogic.Evaluate(Baseline() with { StreamerMode = true });
Check(!hidden.IsVisible && hidden.State == FightCheckState.Hidden && string.IsNullOrEmpty(hidden.ActionId),
    "Streamer redaction failed");

var recovery = FightCheckLogic.Evaluate(Baseline() with
{
    SurvivalUrgency = 3,
    SurvivalLabel = "Vomit sickness",
    EncounterDistance = 4,
    EncounterMotion = "closing"
});
Check(recovery is { State: FightCheckState.Hold, ActionId: "survival-assistant", Severity: 3 }
      && recovery.Detail.Contains("Vomit sickness", StringComparison.Ordinal),
    "Recovery-first priority failed");

var hurt = FightCheckLogic.Evaluate(Baseline() with { Health = ReportedHealthState.Hurt });
Check(hurt is { State: FightCheckState.Hold, ActionId: "core-vitals" }
      && hurt.Heading.Contains("HP HURT", StringComparison.Ordinal),
    "Hurt-health hold failed");

var emptyStamina = FightCheckLogic.Evaluate(Baseline() with { Stamina = ReportedVitalState.Empty });
Check(emptyStamina is { State: FightCheckState.Hold, ActionId: "core-vitals", Severity: 3 }
      && emptyStamina.Heading.Contains("STAMINA EMPTY", StringComparison.Ordinal),
    "Empty-stamina hold failed");

var lowStamina = FightCheckLogic.Evaluate(Baseline() with { Stamina = ReportedVitalState.Low });
Check(lowStamina is { State: FightCheckState.Hold, ActionId: "core-vitals", Severity: 2 }
      && lowStamina.Heading.Contains("REGEN STAMINA", StringComparison.Ordinal),
    "Low-stamina hold failed");

var partial = Baseline() with { FoodFresh = false, WaterFresh = false };
var verify = FightCheckLogic.Evaluate(partial);
Check(verify is { State: FightCheckState.Verify, Badge: "VITALS 2/4", ActionId: "core-vitals" }
      && FightCheckLogic.VitalCoverage(partial) == 2,
    "Incomplete-vitals verification failed");

var manual = FightCheckLogic.Evaluate(Baseline() with { LiveContactFeedAvailable = false });
Check(manual is { State: FightCheckState.Manual, ActionId: "current-combat-guide" }
      && manual.Detail.Contains("no authorized live player feed", StringComparison.OrdinalIgnoreCase),
    "Universal-session honesty failed");

var manualClose = FightCheckLogic.Evaluate(Baseline() with
{
    LiveContactFeedAvailable = false,
    EncounterCount = 0,
    EncounterDistance = null,
    ManualSightingActive = true,
    ManualSightingUrgency = 3,
    ManualSightingHeading = "CREATE SPACE",
    ManualSightingDetail = "Player-reported close contact ahead."
});
Check(manualClose is
    {
        State: FightCheckState.Hold,
        ActionId: "sighting-check",
        ActionLabel: "UPDATE SIGHTING",
        Severity: 3
    }
    && manualClose.Detail.Contains("Player-reported close contact ahead", StringComparison.Ordinal),
    "Manual close-sighting fallback failed");

var manualFar = FightCheckLogic.Evaluate(Baseline() with
{
    LiveContactFeedAvailable = false,
    EncounterCount = 0,
    EncounterDistance = null,
    ManualSightingActive = true,
    ManualSightingUrgency = 1,
    ManualSightingHeading = "MONITOR THE CONTACT",
    ManualSightingDetail = "Player-reported far contact behind you."
});
Check(manualFar is { State: FightCheckState.Watch, ActionId: "sighting-check", Severity: 1 },
    "Manual far-sighting posture failed");

var liveContactWins = FightCheckLogic.Evaluate(Baseline() with
{
    ManualSightingActive = true,
    ManualSightingUrgency = 3,
    ManualSightingHeading = "CREATE SPACE",
    ManualSightingDetail = "Player-reported close contact ahead."
});
Check(liveContactWins.ActionId == "players"
      && liveContactWins.Detail.Contains("72.5 MU", StringComparison.Ordinal)
      && !liveContactWins.Detail.Contains("Player-reported", StringComparison.Ordinal),
    "Authorized live-contact precedence failed");

var waiting = FightCheckLogic.Evaluate(Baseline() with { PositionFresh = false });
Check(waiting is { State: FightCheckState.Waiting, ActionId: "recenter" },
    "Missing-position handoff failed");

var split = FightCheckLogic.Evaluate(Baseline() with { PackSpread = 50.1 });
Check(split is { State: FightCheckState.Caution, ActionId: "players", Badge: "PACK SPLIT" }
      && split.Detail.Contains("50.1 MU", StringComparison.Ordinal),
    "Pack-cohesion gate failed");

var noContact = FightCheckLogic.Evaluate(Baseline() with { EncounterCount = 0, EncounterDistance = null });
Check(noContact is { State: FightCheckState.Watch, ActionId: "focus-combat", Severity: 0 }
      && noContact.Detail.Contains("not proof", StringComparison.OrdinalIgnoreCase),
    "No-contact uncertainty boundary failed");

var missingRange = FightCheckLogic.Evaluate(Baseline() with { EncounterDistance = double.NaN });
Check(missingRange is { State: FightCheckState.Waiting, ActionId: "players" },
    "Invalid contact-range refusal failed");

var close = FightCheckLogic.Evaluate(Baseline() with { EncounterDistance = 10 });
Check(close is { State: FightCheckState.Hold, ActionId: "escape-route", Severity: 3 },
    "Very-close escape handoff failed");

var closing = FightCheckLogic.Evaluate(Baseline() with
{
    EncounterDistance = 25,
    EncounterMotion = "closing"
});
Check(closing is { State: FightCheckState.Hold, ActionId: "escape-route" }
      && closing.Detail.Contains("closing", StringComparison.OrdinalIgnoreCase),
    "Closing-contact boundary failed");

var uncalibratedMotion = FightCheckLogic.Evaluate(Baseline() with
{
    EncounterDistance = 20,
    EncounterMotion = "closing",
    EncounterMotionSampleCount = 2
});
Check(uncalibratedMotion is { State: FightCheckState.Caution, ActionId: "players" },
    "Uncalibrated-motion restraint failed");

var tracked = FightCheckLogic.Evaluate(Baseline());
Check(tracked is { State: FightCheckState.Watch, ActionId: "players", Severity: 0 }
      && tracked.Detail.Contains("not a predicted outcome", StringComparison.OrdinalIgnoreCase)
      && tracked.Detail.Contains("Abort when your exit closes", StringComparison.Ordinal),
    "Tracked-contact bounded guidance failed");

var root = Directory.GetCurrentDirectory();
var mainWindowSource = string.Join("\n", Directory.GetFiles(Path.Combine(root, "BurntHud"), "MainWindow*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText));
var mainWindowXaml = File.ReadAllText(Path.Combine(root, "BurntHud", "MainWindow.xaml"));
Check(mainWindowXaml.Split("x:Name=\"FightCheckAnchor\"").Length - 1 == 1
      && mainWindowXaml.Contains("x:Name=\"FightCheckHeadingText\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("x:Name=\"FightCheckDetailText\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("x:Name=\"FightCheckActionButton\"", StringComparison.Ordinal),
    "Single Fight Check surface failed");
Check(mainWindowXaml.IndexOf("x:Name=\"FightCheckAnchor\"", StringComparison.Ordinal)
      > mainWindowXaml.IndexOf("x:Name=\"GuideCombatBriefAnchor\"", StringComparison.Ordinal),
    "Fight Check escaped the existing Guide combat surface");
Check(mainWindowSource.Contains("private FightCheckView CurrentFightCheckView()", StringComparison.Ordinal)
      && mainWindowSource.Contains("private void UpdateFightCheck(bool force = false)", StringComparison.Ordinal)
      && mainWindowSource.Contains("private async void FightCheckActionButton_Click", StringComparison.Ordinal),
    "Fight Check presentation wiring failed");
Check(mainWindowSource.Contains("new(\"fight-check\", \"Open Fight Check\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("new(\"sighting-check\", \"Open Sighting Check\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("case \"fight-check\":", StringComparison.Ordinal)
      && mainWindowSource.Contains("private void OpenFightCheck()", StringComparison.Ordinal),
    "Fight Check discovery and exact drawer jump failed");
Check(mainWindowSource.Split("UpdateFightCheck();").Length - 1 >= 2
      && mainWindowSource.Contains("UpdateFightCheck(force: true)", StringComparison.Ordinal)
      && mainWindowSource.Contains("OverlayLinks.CombatGuide", StringComparison.Ordinal),
    "Live refresh or current-guide handoff failed");
Check(mainWindowXaml.Contains("never predicts a winner or a safe fight", StringComparison.Ordinal)
      && !mainWindowXaml[..mainWindowXaml.IndexOf("x:Name=\"GuideCombatBriefAnchor\"", StringComparison.Ordinal)]
          .Contains("x:Name=\"FightCheck", StringComparison.Ordinal),
    "Combat-outcome boundary or permanent-HUD exclusion failed");

Console.WriteLine("Fight Check: PASS (recovery/vitals/contact/pack priorities, authorized-live precedence, manual sighting fallback, freshness and motion honesty, species abort cue, one action, and no permanent map card)");
