using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var knownSpecies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "allosaurus",
    "triceratops"
};

Check(AimViewportLogic.TryResolveClientArea(
          1600,
          900,
          -1920,
          100,
          out var secondaryMonitorViewport)
      && secondaryMonitorViewport == new AimViewportBounds(-1920, 100, 1600, 900),
    "windowed game client alignment must preserve exact negative-origin multi-monitor bounds");
Check(AimViewportLogic.TryResolveClientArea(
          3440,
          1440,
          320,
          -1440,
          out var ultrawideViewport)
      && ultrawideViewport == new AimViewportBounds(320, -1440, 3440, 1440),
    "borderless ultrawide game client alignment must preserve the exact viewport");
Check(!AimViewportLogic.TryResolveClientArea(0, 900, 0, 0, out _)
      && !AimViewportLogic.TryResolveClientArea(63, 63, 0, 0, out _)
      && !AimViewportLogic.TryResolveClientArea(32769, 900, 0, 0, out _)
      && !AimViewportLogic.TryResolveClientArea(1920, 1080, int.MaxValue, 0, out _),
    "invalid, tiny, absurd, and overflowing client rectangles must fail closed");

Check(AimCalibrationLogic.NormalizeAttackIndex(-1) == 0, "negative attack index");
Check(AimCalibrationLogic.NormalizeAttackIndex(99) == 2, "high attack index");
Check(AimCalibrationLogic.NextAttackIndex(2) == 0, "attack slots must cycle back to primary");
Check(AimCalibrationLogic.AttackId(1) == "secondary", "secondary attack id");
Check(AimCalibrationLogic.AttackLabel(2) == "ALT / SPECIAL", "special attack label");
Check(AimCalibrationLogic.NormalizeGrowthIndex(-1) == 0
      && AimCalibrationLogic.NextGrowthIndex(2) == 0
      && AimCalibrationLogic.NextGrowthIndex(0) == 3
      && AimCalibrationLogic.NextGrowthIndex(3) == 1
      && AimCalibrationLogic.NextGrowthIndex(1) == 4
      && AimCalibrationLogic.NextGrowthIndex(4) == 2
      && AimCalibrationLogic.GrowthLabel(1) == "ADULT",
    "five growth contexts must cycle in lifecycle order while legacy indices remain stable");
Check(AimCalibrationLogic.GrowthLabel(2) == "HATCHLING"
      && AimCalibrationLogic.GrowthLabel(3) == "SUBADULT"
      && AimCalibrationLogic.GrowthLabel(4) == "ELDER",
    "new growth contexts must remain readable");
Check(AimCalibrationLogic.GrowthIndexForPercent(0) == 2
      && AimCalibrationLogic.GrowthIndexForPercent(24) == 2
      && AimCalibrationLogic.GrowthIndexForPercent(25) == 0
      && AimCalibrationLogic.GrowthIndexForPercent(49) == 0
      && AimCalibrationLogic.GrowthIndexForPercent(50) == 3
      && AimCalibrationLogic.GrowthIndexForPercent(74) == 3
      && AimCalibrationLogic.GrowthIndexForPercent(75) == 1
      && AimCalibrationLogic.GrowthIndexForPercent(99) == 1
      && AimCalibrationLogic.GrowthIndexForPercent(100) == 4,
    "current growth milestones must choose the matching calibration context");
Check(AimCalibrationLogic.ResolveGrowthIndex(true, 63, true, 1) == 3
      && AimCalibrationLogic.ResolveGrowthIndex(true, 63, false, 1) == 1
      && AimCalibrationLogic.ResolveGrowthIndex(false, 63, true, 0) == 0,
    "live growth sync must remain explicit and preserve the manual fallback");
Check(AimCalibrationLogic.GrowthRangeLabel(2) == "0-24%"
      && AimCalibrationLogic.GrowthRangeLabel(0) == "25-49%"
      && AimCalibrationLogic.GrowthRangeLabel(3) == "50-74%"
      && AimCalibrationLogic.GrowthRangeLabel(1) == "75-99%"
      && AimCalibrationLogic.GrowthRangeLabel(4) == "100%",
    "growth evidence ranges must be unambiguous");
Check(AimCalibrationLogic.NormalizeCameraIndex(99) == 2
      && AimCalibrationLogic.NextCameraIndex(2) == 0
      && AimCalibrationLogic.CameraLabel(1) == "NORMAL CAMERA",
    "camera context must remain bounded and readable");
Check(AimCalibrationLogic.ConfidenceLabel(0) == "UNTESTED"
      && AimCalibrationLogic.ConfidenceLabel(2) == "TENTATIVE"
      && AimCalibrationLogic.ConfidenceLabel(4) == "USER TESTED"
      && AimCalibrationLogic.ConfidenceLabel(9) == "REPEATEDLY TESTED",
    "user-reported evidence labels must never imply live hitbox telemetry");
var untestedEvidence = AimCalibrationLogic.EvaluateEvidence(0, 0, 0);
var holdEvidence = AimCalibrationLogic.EvaluateEvidence(4, 0, 0);
var retestEvidence = AimCalibrationLogic.EvaluateEvidence(4, 1, 0);
var narrowEvidence = AimCalibrationLogic.EvaluateEvidence(4, 2, 0);
var widenEvidence = AimCalibrationLogic.EvaluateEvidence(4, 0, 2);
var mixedEvidence = AimCalibrationLogic.EvaluateEvidence(4, 2, 2);
Check(untestedEvidence.Advice == AimCalibrationAdviceState.Untested
      && holdEvidence.Advice == AimCalibrationAdviceState.Hold
      && retestEvidence.Advice == AimCalibrationAdviceState.Retest
      && narrowEvidence.Advice == AimCalibrationAdviceState.Narrow
      && widenEvidence.Advice == AimCalibrationAdviceState.Widen
      && mixedEvidence.Advice == AimCalibrationAdviceState.Mixed,
    "evidence advisor states");
Check(narrowEvidence.HasContradiction
      && narrowEvidence.EffectiveMatches == 0
      && AimCalibrationLogic.ConfidenceLabel(4, 2, 0) == "CONFLICT FOUND"
      && AimCalibrationLogic.ConfidenceLabel(4, 1, 0) == "RETEST",
    "contradictions must reduce confidence instead of accumulating fake certainty");

Check(AimCalibrationLogic.ResolveSpeciesId(true, "TROODON", "allosaurus", knownSpecies.Contains) == "allosaurus",
    "unknown live species must not replace a known manual selection");
knownSpecies.Add("troodon");
Check(AimCalibrationLogic.ResolveSpeciesId(true, "TROODON", "allosaurus", knownSpecies.Contains) == "troodon",
    "fresh recognized live species must drive the calibration profile");
Check(AimCalibrationLogic.ResolveSpeciesId(false, "troodon", "triceratops", knownSpecies.Contains) == "triceratops",
    "manual Field Guide selection must remain the offline fallback");

var normalized = AimCalibrationLogic.NormalizeProfiles(
    [
        new AimCalibrationProfile("ALLOSAURUS", "primary", 1, 1, 2, 999, 9, -999, -999, 99, 99, 99, 10),
        new AimCalibrationProfile("allosaurus", "primary", 1, 1, 0, 180, 0.8, 16, 12, 3, 1, 0, 20),
        new AimCalibrationProfile("allosaurus", "primary", 0, 1, 1, 200, 1.1, 0, 4, 1, 0, 1, 18),
        new AimCalibrationProfile("unknown", "primary", 1, 1, 1, 220, 1, 0, 0, 0, 0, 0, 30),
        new AimCalibrationProfile("triceratops", "not-an-attack", 1, 1, 1, 220, 1, 0, 0, 0, 0, 0, 40)
    ],
    knownSpecies.Contains);
Check(normalized.Count == 2,
    "unknown species, invalid attacks, and same-context duplicates must be removed while growth variants remain");
Check(AimCalibrationLogic.TryFind(normalized, "ALLOSAURUS", 0, 1, 1, out var adultPrimary)
      && adultPrimary.ModeIndex == 0,
    "newest valid species, attack, growth, and camera calibration must win");
Check(adultPrimary.Size == 180
      && adultPrimary.DepthScale == 0.8
      && adultPrimary.HorizontalOffset == 16
      && adultPrimary.VerticalOffset == 12
      && adultPrimary.ConfirmedMatches == 3
      && adultPrimary.InsideMisses == 1
      && adultPrimary.OutsideHits == 0,
    "valid calibration geometry and user evidence must be preserved");
Check(AimCalibrationLogic.TryFind(normalized, "allosaurus", 0, 0, 1, out _),
    "juvenile and adult profiles must remain independent");

var profiles = normalized.ToList();
AimCalibrationLogic.Upsert(profiles, new AimCalibrationProfile(
    "allosaurus", "secondary", 1, 1, 2, 540, 5, 300, -300, 99, 99, 99, 50));
Check(AimCalibrationLogic.TryFind(profiles, "ALLOSAURUS", 1, 1, 1, out var secondary),
    "full-context calibration lookup");
Check(secondary.ModeIndex == 2
      && secondary.Size == 520
      && secondary.DepthScale == 1.40
      && secondary.HorizontalOffset == 240
      && secondary.VerticalOffset == -240
      && secondary.ConfirmedMatches == AimCalibrationLogic.MaxConfirmedMatches
      && secondary.InsideMisses == AimCalibrationLogic.MaxEvidenceReports
      && secondary.OutsideHits == AimCalibrationLogic.MaxEvidenceReports,
    "saved calibration must be constrained to the visual guide range");
Check(AimCalibrationLogic.Matches(secondary, 2, 520, 1.4, 240, -240),
    "saved calibration match state");
Check(!AimCalibrationLogic.Matches(secondary, 2, 520, 1.3, 240, -240),
    "depth modification must invalidate the tested geometry state");
Check(AimCalibrationLogic.Remove(profiles, "allosaurus", 1, 1, 1),
    "profile reset removes only the current full context");
Check(!AimCalibrationLogic.TryFind(profiles, "allosaurus", 1, 1, 1, out _),
    "removed context must stay removed");

for (var i = 0; i < AimCalibrationLogic.MaxProfiles + 10; i++)
{
    AimCalibrationLogic.Upsert(profiles, new AimCalibrationProfile(
        $"species-{i}", "primary", i % 2, i % 3, 1, 220, 1, 0, 0, 0, 0, 0, 100 + i));
}
Check(profiles.Count == AimCalibrationLogic.MaxProfiles, "profile collection must remain bounded");

Console.WriteLine(
    "Aim calibration verification passed (exact game-client viewport, species, attack, five-stage live growth, camera, geometry, contradiction-aware evidence, reset, and bounds)." );
