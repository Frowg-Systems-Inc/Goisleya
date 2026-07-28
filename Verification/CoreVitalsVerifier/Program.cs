using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var now = DateTimeOffset.UnixEpoch.AddHours(8);

Check(CoreVitalsLogic.Next(ReportedVitalState.Unknown) == ReportedVitalState.Stable
      && CoreVitalsLogic.Next(ReportedVitalState.Stable) == ReportedVitalState.Low
      && CoreVitalsLogic.Next(ReportedVitalState.Low) == ReportedVitalState.Empty
      && CoreVitalsLogic.Next(ReportedVitalState.Empty) == ReportedVitalState.Unknown,
    "vital cycle failed");
Check(CoreVitalsLogic.Label(ReportedVitalState.Stable) == "OK"
      && CoreVitalsLogic.Label(ReportedVitalState.Low) == "LOW"
      && CoreVitalsLogic.Label(ReportedVitalState.Empty) == "EMPTY",
    "vital labels failed");
Check(WoundCheckLogic.Options.Length == 4
      && WoundCheckLogic.Options.Select(option => option.Id).SequenceEqual(
          new[]
          {
              WoundCheckLogic.LightId,
              WoundCheckLogic.VisibleId,
              WoundCheckLogic.HeavyId,
              WoundCheckLogic.SevereId
          }),
    "wound observation order failed");
Check(WoundCheckLogic.Normalize(" HEAVY ") == WoundCheckLogic.HeavyId
      && WoundCheckLogic.Normalize("invented") == string.Empty
      && WoundCheckLogic.Find("invented") is null,
    "wound observation validation failed");
Check(WoundCheckLogic.Find(WoundCheckLogic.LightId) is
      {
          RangeLabel: "~90–100%",
          ManualHealth: ReportedHealthState.Stable,
          Severity: 0
      }
      && WoundCheckLogic.Find(WoundCheckLogic.VisibleId)?.ManualHealth
          == ReportedHealthState.Stable
      && WoundCheckLogic.Find(WoundCheckLogic.HeavyId) is
      {
          RangeLabel: "~40–70%",
          ManualHealth: ReportedHealthState.Hurt,
          Severity: 1
      }
      && WoundCheckLogic.Find(WoundCheckLogic.SevereId) is
      {
          RangeLabel: "~0–30%",
          ManualHealth: ReportedHealthState.Critical,
          Severity: 2
      },
    "wound estimate ranges or conservative health mapping failed");
Check(WoundCheckLogic.Options.All(option =>
        option.VisualCue.Length >= 40
        && option.Action.Length >= 35
        && option.RangeLabel.StartsWith('~')),
    "wound estimate evidence or uncertainty copy failed");
Check(WoundCheckLogic.IsCurrent(
          WoundCheckLogic.HeavyId,
          now.AddSeconds(-CoreVitalsLogic.FreshnessSeconds + 1),
          now)
      && !WoundCheckLogic.IsCurrent(
          WoundCheckLogic.HeavyId,
          now.AddSeconds(-CoreVitalsLogic.FreshnessSeconds),
          now)
      && !WoundCheckLogic.IsCurrent("invented", now.AddSeconds(-1), now)
      && !WoundCheckLogic.IsCurrent(WoundCheckLogic.LightId, default, now),
    "wound estimate expiry or invalid-state refusal failed");

var critical = CoreVitalsLogic.Evaluate(new CoreVitalsSnapshot(
    ReportedHealthState.Critical, now.AddSeconds(-20),
    ReportedVitalState.Empty, now.AddSeconds(-10),
    ReportedVitalState.Empty, now.AddSeconds(-5),
    ReportedVitalState.Empty, now.AddSeconds(-1),
    now));
Check(critical.Critical
      && critical.Heading == "CRITICAL HP REPORTED"
      && critical.Action == "DISENGAGE NOW"
      && critical.RoutePinType == "safe",
    "critical health priority failed");

var water = CoreVitalsLogic.Evaluate(new CoreVitalsSnapshot(
    ReportedHealthState.Stable, now.AddSeconds(-30),
    ReportedVitalState.Stable, now.AddSeconds(-20),
    ReportedVitalState.Empty, now.AddSeconds(-10),
    ReportedVitalState.Low, now.AddSeconds(-5),
    now));
Check(water.Critical
      && water.Heading == "WATER EMPTY"
      && water.RoutePinType == "water"
      && water.CompactLabel == "HP OK · F OK · W 0 · ST LOW",
    "water priority or compact label failed");

var lowStamina = CoreVitalsLogic.Evaluate(new CoreVitalsSnapshot(
    ReportedHealthState.Stable, now.AddSeconds(-10),
    ReportedVitalState.Stable, now.AddSeconds(-10),
    ReportedVitalState.Stable, now.AddSeconds(-10),
    ReportedVitalState.Low, now.AddSeconds(-10),
    now));
Check(lowStamina.Urgency == 1
      && lowStamina.Heading == "STAMINA LOW"
      && lowStamina.Action == "CONSERVE STAMINA"
      && lowStamina.BriefLabel.Contains("ST LOW", StringComparison.Ordinal),
    "low stamina guidance failed");

var stale = CoreVitalsLogic.Evaluate(new CoreVitalsSnapshot(
    ReportedHealthState.Critical, now.AddSeconds(-CoreVitalsLogic.FreshnessSeconds),
    ReportedVitalState.Empty, now.AddSeconds(-CoreVitalsLogic.FreshnessSeconds),
    ReportedVitalState.Empty, now.AddSeconds(-CoreVitalsLogic.FreshnessSeconds),
    ReportedVitalState.Empty, now.AddSeconds(-CoreVitalsLogic.FreshnessSeconds),
    now));
Check(!stale.HasFreshReport
      && !stale.Warning
      && stale.Health == ReportedHealthState.Unknown
      && stale.Food == ReportedVitalState.Unknown
      && stale.CompactLabel == "HP ? · F ? · W ? · ST ?",
    "stale report expiry failed");

var futureClock = CoreVitalsLogic.Evaluate(new CoreVitalsSnapshot(
    ReportedHealthState.Stable, now.AddMinutes(1),
    ReportedVitalState.Stable, now.AddMinutes(1),
    ReportedVitalState.Stable, now.AddMinutes(1),
    ReportedVitalState.Stable, now.AddMinutes(1),
    now));
Check(futureClock.HealthAgeSeconds == 0
      && futureClock.FoodAgeSeconds == 0
      && futureClock.WaterAgeSeconds == 0
      && futureClock.StaminaAgeSeconds == 0,
    "future clock clamping failed");
Check(CoreVitalsLogic.FormatAge(0) == "0s"
      && CoreVitalsLogic.FormatAge(59) == "59s"
      && CoreVitalsLogic.FormatAge(60) == "1m",
    "age formatting failed");

var liveSnapshot = PlayerSnapshotLogic.Evaluate(new PlayerSnapshotRaw(
    PlayerSnapshotSourceState.Live,
    "triceratops",
    67,
    25, 100,
    70, 100,
    90, 100,
    2, 3, 3,
    now.AddSeconds(-3)), now);
var liveGuidance = CoreVitalsLogic.Evaluate(new CoreVitalsSnapshot(
    liveSnapshot.HealthState, now.AddSeconds(-3),
    liveSnapshot.FoodState, now.AddSeconds(-3),
    liveSnapshot.WaterState, now.AddSeconds(-3),
    ReportedVitalState.Low, now.AddSeconds(-5),
    now));
var liveDock = DockVitalsLogic.Resolve(true, false, true, liveSnapshot, liveGuidance);
Check(liveDock.Visible
      && liveDock.SourceLabel == "LIVE 3S"
      && liveDock.ValuesLabel == "HP25  F70  W90  STLOW"
      && liveDock.Severity == 2
      && liveDock.Fresh,
    "live minimized-dock vitals failed");
var healingDock = DockVitalsLogic.Resolve(
    true,
    false,
    true,
    liveSnapshot,
    liveGuidance,
    new VitalMetricTrend(
        "HP",
        VitalTrendDirection.Rising,
        3,
        60,
        25,
        2,
        38,
        100,
        "FULL"));
Check(healingDock.ValuesLabel == "HP25↑  F70  W90  STLOW"
      && healingDock.Tooltip.Contains("about 38m to full", StringComparison.Ordinal)
      && healingDock.Tooltip.Contains("Damage resets", StringComparison.Ordinal),
    "minimized-dock healing evidence failed");

var manualDock = DockVitalsLogic.Resolve(
    true, false, false,
    PlayerSnapshotLogic.Evaluate(null, now),
    lowStamina);
Check(manualDock.Visible
      && manualDock.SourceLabel == "MANUAL CURRENT"
      && manualDock.ValuesLabel == "HPOK  FOK  WOK  STLOW"
      && manualDock.Severity == 1
      && manualDock.Fresh,
    "manual minimized-dock vitals failed");

var staleSnapshot = PlayerSnapshotLogic.Evaluate(new PlayerSnapshotRaw(
    PlayerSnapshotSourceState.Live,
    "triceratops",
    67,
    80, 100,
    70, 100,
    90, 100,
    2, 3, 3,
    now.AddSeconds(-PlayerSnapshotLogic.FreshnessSeconds)), now);
var staleDock = DockVitalsLogic.Resolve(true, false, true, staleSnapshot, stale);
Check(staleDock.Visible
      && staleDock.SourceLabel == "STALE / REPORT"
      && staleDock.ValuesLabel == "HP?  F?  W?  ST?"
      && !staleDock.Fresh,
    "stale minimized-dock vitals refusal failed");
Check(!DockVitalsLogic.Resolve(false, false, true, liveSnapshot, liveGuidance).Visible
      && !DockVitalsLogic.Resolve(true, true, true, liveSnapshot, liveGuidance).Visible,
    "minimized-dock vitals privacy or toggle failed");

var visibleHealthy = new VisibleHudSensorSample(
    now.AddSeconds(-1), 100, 96, 55, 98, 0.82, false);
Check(VisibleHudSensorLogic.IsFresh(visibleHealthy, now)
      && !VisibleHudSensorLogic.IsFresh(
          visibleHealthy with
          {
              CapturedAt = now.AddSeconds(-VisibleHudSensorLogic.FreshnessSeconds)
          },
          now),
    "visible HUD sensor freshness failed");
Check(VisibleHudSensorLogic.EstimateFillPercent(0.19, 0.38) == 50
      && VisibleHudSensorLogic.EstimateFillPercent(2, 0.38) == 100
      && VisibleHudSensorLogic.EstimateFillPercent(double.NaN, 0.38) == 0,
    "visible HUD fill estimate bounds failed");
Check(VisibleHudSensorLogic.EstimateHealthPercent(0) == 100
      && VisibleHudSensorLogic.EstimateHealthPercent(0.05) == 60
      && VisibleHudSensorLogic.EstimateHealthPercent(0.20) == 25
      && VisibleHudSensorLogic.HealthState(25) == ReportedHealthState.Critical
      && VisibleHudSensorLogic.HealthState(60) == ReportedHealthState.Hurt
      && VisibleHudSensorLogic.VitalState(8) == ReportedVitalState.Empty
      && VisibleHudSensorLogic.VitalState(30) == ReportedVitalState.Low
      && VisibleHudSensorLogic.VitalState(80) == ReportedVitalState.Stable,
    "visible HUD safety bands failed");
var visibleSmoothed = VisibleHudSensorLogic.Median(
    [
        visibleHealthy,
        visibleHealthy with { HealthPercent = 25, WaterPercent = 10 },
        visibleHealthy with { HealthPercent = 95, WaterPercent = 52 }
    ],
    now);
Check(visibleSmoothed.HealthPercent == 95
      && visibleSmoothed.WaterPercent == 52
      && visibleSmoothed.CapturedAt == now,
    "visible HUD median smoothing failed");
var defaultCalibration = VisibleHudSensorLogic.NormalizeCalibration(
    VisibleHudCalibration.Default);
var defaultRegion = VisibleHudSensorLogic.TransformRegion(
    0.85, 0.80, 0.95, 0.90,
    defaultCalibration);
Check(Math.Abs(defaultRegion.Left - 0.85) < 0.0001
      && Math.Abs(defaultRegion.Top - 0.80) < 0.0001
      && Math.Abs(defaultRegion.Right - 0.95) < 0.0001
      && Math.Abs(defaultRegion.Bottom - 0.90) < 0.0001,
    "default visible HUD calibration must preserve the reference geometry");
var scaledRegion = VisibleHudSensorLogic.TransformRegion(
    0.85, 0.80, 0.95, 0.90,
    new VisibleHudCalibration(1.1, 0, 0, 0.75, now));
Check(scaledRegion.Left < defaultRegion.Left
      && scaledRegion.Top < defaultRegion.Top
      && scaledRegion.Right < defaultRegion.Right,
    "larger HUD calibration should expand the bottom-right anchored scan area");
var boundedCalibration = VisibleHudSensorLogic.NormalizeCalibration(
    new VisibleHudCalibration(double.NaN, 2, -2, double.PositiveInfinity, now));
Check(boundedCalibration.Scale == 1
      && boundedCalibration.OffsetX == 0.05
      && boundedCalibration.OffsetY == -0.05
      && boundedCalibration.Score == 0,
    "visible HUD calibration must reject non-finite values and bound offsets");

var visibleText = VisibleHudTextLogic.Parse(
    "ASSET LOCATION: -123.5, 222.25, 9\n" +
    "Health 42%\nHunger 25 / 100\nThirst 81%\nStamina 70%\nGrowth 88%");
Check(visibleText.Position == new UniversalCoordinatePoint(-123.5, 222.25, 9)
      && visibleText.HealthPercent == 42
      && visibleText.FoodPercent == 25
      && visibleText.WaterPercent == 81
      && visibleText.StaminaPercent == 70
      && visibleText.GrowthPercent == 88
      && visibleText.FieldCount == 6,
    "visible-text allowlist parsing failed");
var unsupportedText = VisibleHudTextLogic.Parse(
    "Players 53\nPing 27\nEnemy at 10, 20, 30\nHealth 999%");
Check(unsupportedText.FieldCount == 0
      && unsupportedText.Summary == "NO SUPPORTED FIELDS",
    "visible-text reader must reject unsupported numbers and out-of-range vitals");

Console.WriteLine("Core vitals: PASS (cycles, calibrated visible HUD estimates, allowlisted OCR, independent freshness, priority, adaptive healing evidence, full and minimized strips, privacy, and stale expiry)");
