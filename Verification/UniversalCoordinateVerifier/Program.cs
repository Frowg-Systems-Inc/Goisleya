using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

Check(
    UniversalCoordinateLogic.TryParseClipboard(
        "-123,456.789, 234,567.125, 12,345.6",
        out var grouped),
    "grouped Evrima coordinates should parse");
Check(grouped == new UniversalCoordinatePoint(-123456.789, 234567.125, 12345.6),
    "grouped coordinate values");

Check(
    UniversalCoordinateLogic.TryParseClipboard(
        "ASSET LOCATION: -123456.789, 234567.125, 12345.6",
        out var prefixed),
    "optional Asset Location prefix should parse");
Check(prefixed == grouped, "prefix should not change coordinates");

Check(
    UniversalCoordinateLogic.TryParseDestinationWorldPoint(
        "ASSET LOCATION: -123456.789, 234567.125, 12345.6",
        out var routeX,
        out var routeY),
    "Asset Location should route as a destination world point");
Check(Math.Abs(routeX - grouped.X) < 0.001 && Math.Abs(routeY - grouped.Y) < 0.001,
    "destination routing should keep X/Y and discard altitude");
Check(
    UniversalCoordinateLogic.TryParseDestinationWorldPoint("-49000, 51000", out var pairX, out var pairY),
    "plain X,Y destination pairs should route");
Check(Math.Abs(pairX - -49000) < 0.001 && Math.Abs(pairY - 51000) < 0.001,
    "plain X,Y destination values");
Check(
    !UniversalCoordinateLogic.TryParseDestinationWorldPoint("Central Pond", out _, out _),
    "place names must not parse as destination coordinates");

Check(
    UniversalCoordinateLogic.TryParseClipboard(
        "-123456.789,234567.125,12345.6",
        out var compact),
    "compact invariant coordinates should parse");
Check(compact == grouped, "compact coordinate values");

Check(
    UniversalCoordinateLogic.TryParseClipboard(
        "-123.456,789; 234.567,125; 12.345,6",
        out var localized),
    "localized coordinate separators should parse");
Check(localized == grouped, "localized coordinate values");

foreach (var invalid in new[]
         {
             null,
             string.Empty,
             "hello from Discord",
             "12, 34",
             "12, 34, 56, 78",
             "X=12 Y=34 Z=56",
             "1000001, 2, 3",
             "1, 2, 200001",
             "NaN, 2, 3",
             "https://example.com/1, 2, 3"
         })
{
    Check(!UniversalCoordinateLogic.TryParseClipboard(invalid, out _),
        $"non-coordinate clipboard accepted: {invalid}");
}

var previous = new UniversalCoordinatePoint(10, 10, 100);
var current = new UniversalCoordinatePoint(13, 14, 85);
var movement = UniversalCoordinateLogic.DescribeMovement(previous, current, TimeSpan.FromSeconds(12));
Check(movement is not null, "movement should be available");
Check(Math.Abs(movement!.HorizontalDistance - 5) < 0.001, "horizontal movement distance");
Check(movement.AltitudeDelta == -15, "altitude delta");
Check(movement.ElapsedSeconds == 12, "movement interval");
Check(movement.AxisCourse == "+X / +Y", "axis course");
var hill = UniversalCoordinateLogic.DescribeHill(movement);
Check(hill is { Direction: "DESCENT" }, "descent direction");
Check(Math.Abs(hill!.GradePercent - 300) < 0.001, "descent grade");
Check(Math.Abs(hill.AngleDegrees - 71.565051) < 0.001, "descent angle");
Check(hill.RiseOrDrop == 15, "descent drop");
var climb = UniversalCoordinateLogic.DescribeMovement(
    new UniversalCoordinatePoint(0, 0, 20),
    new UniversalCoordinatePoint(30, 40, 40),
    TimeSpan.FromSeconds(8));
var climbHill = UniversalCoordinateLogic.DescribeHill(climb);
Check(climbHill is { Direction: "CLIMB" }, "climb direction");
Check(Math.Abs(climbHill!.GradePercent - 40) < 0.001, "climb grade");
Check(UniversalCoordinateLogic.DescribeHill(
        new UniversalCoordinateMovement(4.999, 20, 2, "+X")) is null,
    "short baseline should not fabricate hill evidence");
Check(UniversalCoordinateLogic.DescribeHill(
        new UniversalCoordinateMovement(20, 0.1, 2, "+X")) is { Direction: "LEVEL" },
    "sub-unit altitude noise should read level");
Check(UniversalCoordinateLogic.DescribeMovement(previous, current, TimeSpan.Zero) is null,
    "zero interval should not fabricate movement");
Check(UniversalCoordinateLogic.SamePoint(grouped, compact), "equivalent captures");
Check(!UniversalCoordinateLogic.SamePoint(grouped, current), "different captures");
var eastHeading = UniversalCoordinateLogic.ResolveHeading(
    new UniversalCoordinatePoint(0, 0, 0),
    new UniversalCoordinatePoint(10, 0, 0),
    0,
    previousHeadingAvailable: false);
Check(eastHeading is { Updated: true } && Math.Abs(eastHeading.Degrees) < 0.001,
    "eastward movement should produce a stable zero-degree map heading");
var northHeading = UniversalCoordinateLogic.ResolveHeading(
    new UniversalCoordinatePoint(10, 0, 0),
    new UniversalCoordinatePoint(10, 10, 0),
    eastHeading.Degrees,
    previousHeadingAvailable: true);
Check(northHeading is { Updated: true } && Math.Abs(northHeading.Degrees - 90) < 0.001,
    "northward movement should rotate the map heading");
var stationaryHeading = UniversalCoordinateLogic.ResolveHeading(
    new UniversalCoordinatePoint(10, 10, 0),
    new UniversalCoordinatePoint(10, 10, 0),
    northHeading.Degrees,
    previousHeadingAvailable: true);
Check(!stationaryHeading.Updated && Math.Abs(stationaryHeading.Degrees - 90) < 0.001,
    "a repeated coordinate copy must preserve the last trustworthy heading");
var trackStart = DateTimeOffset.UnixEpoch;
var straightTrack = UniversalCoordinateLogic.EstimateTrack(
    [
        new UniversalTrackSample(new UniversalCoordinatePoint(0, 0, 0), trackStart),
        new UniversalTrackSample(new UniversalCoordinatePoint(10, 0.2, 0), trackStart.AddSeconds(2)),
        new UniversalTrackSample(new UniversalCoordinatePoint(20, -0.1, 0), trackStart.AddSeconds(4)),
        new UniversalTrackSample(new UniversalCoordinatePoint(30, 0.1, 0), trackStart.AddSeconds(6))
    ]);
Check(straightTrack is
      {
          ConfidenceLabel: "HIGH",
          SegmentCount: 3,
          DirectionAgreement: > 0.99,
          SpeedWorldUnitsPerSecond: > 4.9 and < 5.2
      }
      && (straightTrack.HeadingDegrees < 2 || straightTrack.HeadingDegrees > 358),
    "multi-capture estimator should smooth a straight noisy course");
var turningTrack = UniversalCoordinateLogic.EstimateTrack(
    [
        new UniversalTrackSample(new UniversalCoordinatePoint(0, 0, 0), trackStart),
        new UniversalTrackSample(new UniversalCoordinatePoint(10, 0, 0), trackStart.AddSeconds(2)),
        new UniversalTrackSample(new UniversalCoordinatePoint(10, 10, 0), trackStart.AddSeconds(4)),
        new UniversalTrackSample(new UniversalCoordinatePoint(10, 20, 0), trackStart.AddSeconds(6))
    ]);
Check(turningTrack is { HeadingDegrees: > 65 and < 90 },
    "recent captures should dominate a turn without snapping to a stale segment");
Check(UniversalCoordinateLogic.EstimateTrack(
          [
              new UniversalTrackSample(new UniversalCoordinatePoint(1, 1, 0), trackStart),
              new UniversalTrackSample(new UniversalCoordinatePoint(1.001, 1.001, 0), trackStart.AddSeconds(1))
          ]) is null,
    "stationary jitter must not fabricate course or speed");
Check(UniversalCoordinateLogic.EstimateTrack(
          [
              new UniversalTrackSample(new UniversalCoordinatePoint(0, 0, 0), trackStart.AddSeconds(2)),
              new UniversalTrackSample(new UniversalCoordinatePoint(10, 0, 0), trackStart)
          ]) is null,
    "non-monotonic timestamps must not fabricate a track");

var probeOff = SlopeSafetyLogic.Present(false, movement, mapReady: true, obstacleLimitReached: false);
Check(probeOff.State == "OFF" && !probeOff.HasMeasurement && !probeOff.CanSaveAvoidance,
    "disabled Terrain Probe must ignore even valid prior evidence");

var probeWaiting = SlopeSafetyLogic.Present(true, null, mapReady: true, obstacleLimitReached: false);
Check(probeWaiting.State == "WAITING"
      && probeWaiting.Heading == "COPY TWO POINTS"
      && !probeWaiting.CanSaveAvoidance,
    "Terrain Probe should explain its two-capture evidence gate");

var probeLevel = SlopeSafetyLogic.Present(
    true,
    new UniversalCoordinateMovement(20, 0.1, 2, "+X"),
    mapReady: true,
    obstacleLimitReached: false);
Check(probeLevel.State == "LEVEL"
      && probeLevel.HasMeasurement
      && !probeLevel.CanSaveAvoidance
      && probeLevel.SaveLabel.Contains("NOTHING TO AVOID", StringComparison.Ordinal),
    "level evidence must not create a route obstacle");

var measuredDescent = SlopeSafetyLogic.Present(
    true,
    new UniversalCoordinateMovement(100, -10, 5, "+X"),
    mapReady: true,
    obstacleLimitReached: false);
Check(measuredDescent.State == "MEASURED"
      && measuredDescent.Severity == 1
      && measuredDescent.CanSaveAvoidance
      && measuredDescent.Guidance.Contains("sliding", StringComparison.OrdinalIgnoreCase),
    "a measured descent should surface the public hill-sliding caveat");

var elevatedDescent = SlopeSafetyLogic.Present(
    true,
    new UniversalCoordinateMovement(100, -20, 5, "+X"),
    mapReady: true,
    obstacleLimitReached: false);
Check(elevatedDescent.State == "ELEVATED"
      && elevatedDescent.Severity == 2
      && elevatedDescent.Heading == "ELEVATED DESCENT"
      && elevatedDescent.Guidance.Contains("fall-risk", StringComparison.Ordinal),
    "the documented Isley geometry band should promote an elevated descent");

var highClimb = SlopeSafetyLogic.Present(
    true,
    new UniversalCoordinateMovement(100, 50, 5, "+X"),
    mapReady: true,
    obstacleLimitReached: false);
Check(highClimb.State == "HIGH"
      && highClimb.Severity == 3
      && highClimb.Heading == "HIGH CLIMB"
      && highClimb.Guidance.Contains("quadrupeds", StringComparison.OrdinalIgnoreCase),
    "a high climb should explain the current uphill locomotion caveat");

var noMap = SlopeSafetyLogic.Present(
    true,
    new UniversalCoordinateMovement(100, -20, 5, "+X"),
    mapReady: false,
    obstacleLimitReached: false);
Check(!noMap.CanSaveAvoidance && noMap.SaveLabel == "MAP CALIBRATION NEEDED",
    "universal sessions must not pretend a route obstacle can be calibrated");

var atLimit = SlopeSafetyLogic.Present(
    true,
    new UniversalCoordinateMovement(100, -20, 5, "+X"),
    mapReady: true,
    obstacleLimitReached: true);
Check(!atLimit.CanSaveAvoidance && atLimit.SaveLabel == "NO-GO LIMIT REACHED",
    "the existing eight-area privacy and complexity bound must remain authoritative");

var mainWindowSource = string.Join("\n", Directory.GetFiles(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..", "..",
    "BurntHud"), "MainWindow*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText)) + "\n" + File.ReadAllText(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..", "..",
    "BurntHud", "Map", "isley-map-controller.js"));
var mainWindowXaml = File.ReadAllText(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..", "..",
    "BurntHud", "MainWindow.xaml"));
Check(mainWindowSource.Contains(
          "if (!_universalCoordinateCaptureEnabled || _streamerMode)",
          StringComparison.Ordinal)
      && mainWindowSource.Contains(
          "PlayFocusForeground.Game or PlayFocusForeground.Mapper",
          StringComparison.Ordinal)
      && mainWindowSource.Contains(
          "UniversalCoordinateLogic.TryParseClipboard",
          StringComparison.Ordinal),
    "Player Sync must remain user-controllable, foreground-gated, and coordinate-shaped on every server");
Check(mainWindowSource.Contains(
          "PlayerSyncSetupVersion < CurrentPlayerSyncSetupVersion",
          StringComparison.Ordinal)
      && mainWindowSource.Contains(
          "updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()",
          StringComparison.Ordinal)
      && mainWindowSource.Contains(
          "UniversalCoordinateLogic.ResolveHeading",
          StringComparison.Ordinal),
    "existing users must receive enabled Player Sync with timestamped updates and retained heading");
Check(mainWindowXaml.Contains("x:Name=\"TerrainProbePanel\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("x:Name=\"TerrainProbeToggleButton\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("x:Name=\"TerrainProbeClearButton\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("x:Name=\"TerrainProbeSaveAvoidanceButton\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains(
          "Explicitly converts two session-only world-coordinate endpoints",
          StringComparison.Ordinal),
    "the Live Map route workflow must expose accessible toggle, clear, and explicit-save controls");
var saveMethodStart = mainWindowSource.IndexOf(
    "private async Task<bool> SaveMeasuredSlopeAvoidanceAsync",
    StringComparison.Ordinal);
var saveMethodEnd = mainWindowSource.IndexOf(
    "private async Task SuspendLiveMapServicesAsync",
    saveMethodStart,
    StringComparison.Ordinal);
Check(saveMethodStart >= 0 && saveMethodEnd > saveMethodStart,
    "measured slope save method boundary");
var saveMethod = mainWindowSource[saveMethodStart..saveMethodEnd];
Check(saveMethod.Contains("saveMeasuredSlopeAvoidance(", StringComparison.Ordinal)
      && saveMethod.Contains("_universalCoordinatePreviousPoint.X", StringComparison.Ordinal)
      && saveMethod.Contains("_universalCoordinatePreviousPoint.Y", StringComparison.Ordinal)
      && saveMethod.Contains("_universalCoordinatePoint.X", StringComparison.Ordinal)
      && saveMethod.Contains("_universalCoordinatePoint.Y", StringComparison.Ordinal)
      && !saveMethod.Contains("_universalCoordinatePreviousPoint.Z", StringComparison.Ordinal)
      && !saveMethod.Contains("_universalCoordinatePoint.Z", StringComparison.Ordinal)
      && saveMethod.Contains("reversible local No-Go corridor", StringComparison.Ordinal),
    "explicit save may persist only derived map X/Y geometry, never raw elevation");
Check(mainWindowSource.Contains("noGoLastStatus = 'measured-slope-saved'", StringComparison.Ordinal)
      && mainWindowSource.Contains("scheduleTerrainCourseForObstacleChange()", StringComparison.Ordinal)
      && mainWindowSource.Contains("persistNoGoAreas()", StringComparison.Ordinal)
      && mainWindowSource.Contains("removeNoGoArea(id)", StringComparison.Ordinal),
    "a measured slope must become an existing reversible bounded No-Go area and replan an active course");

Console.WriteLine(
    "Universal coordinate capture verification passed (formats, bounds, privacy rejection, dedupe, smoothed course and speed, hill geometry, slope guidance, and bounded route-avoidance eligibility).");
