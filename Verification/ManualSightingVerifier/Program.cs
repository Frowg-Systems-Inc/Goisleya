using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var now = new DateTimeOffset(2026, 7, 22, 20, 0, 0, TimeSpan.Zero);
var empty = ManualSightingLogic.Evaluate(
    new ManualSightingSnapshot(
        ManualSightingDirection.None,
        ManualSightingRange.None,
        null),
    now);
Check(empty is
    {
        State: ManualSightingState.Ready,
        IsCurrent: false,
        RemainingSeconds: ManualSightingLogic.FreshnessSeconds
    },
    "Ready state failed");

var close = ManualSightingLogic.Evaluate(
    new ManualSightingSnapshot(
        ManualSightingDirection.Ahead,
        ManualSightingRange.Close,
        now.AddSeconds(-12.25)),
    now);
Check(close is
    {
        State: ManualSightingState.Current,
        Urgency: 3,
        DirectionLabel: "AHEAD",
        RangeLabel: "CLOSE",
        AgeSeconds: 12,
        RemainingSeconds: 33
    }
    && close.Detail.Contains("No identity, exact distance, count, motion, or species", StringComparison.Ordinal)
    && close.BriefLabel == "SIGHTING CLOSE AHEAD 33S",
    "Close sighting presentation failed");

var near = ManualSightingLogic.Evaluate(
    new ManualSightingSnapshot(
        ManualSightingDirection.Right,
        ManualSightingRange.Near,
        now.AddSeconds(-1)),
    now);
Check(near is { IsCurrent: true, Urgency: 2, Heading: "HOLD AN EXIT" }
      && near.Detail.Contains("to your right", StringComparison.Ordinal),
    "Near sighting response failed");

var far = ManualSightingLogic.Evaluate(
    new ManualSightingSnapshot(
        ManualSightingDirection.Behind,
        ManualSightingRange.Far,
        now.AddSeconds(-44)),
    now);
Check(far is { IsCurrent: true, Urgency: 1, RemainingSeconds: 1 }
      && far.Badge == "FAR BEHIND",
    "Far sighting boundary failed");

var expired = ManualSightingLogic.Evaluate(
    new ManualSightingSnapshot(
        ManualSightingDirection.Left,
        ManualSightingRange.Close,
        now.AddSeconds(-ManualSightingLogic.FreshnessSeconds)),
    now);
Check(expired is
    {
        State: ManualSightingState.Expired,
        IsCurrent: false,
        CanClear: true,
        Urgency: 0,
        RemainingSeconds: 0
    }
    && string.IsNullOrEmpty(expired.BriefLabel),
    "Exact expiry boundary failed");

var hidden = ManualSightingLogic.Evaluate(
    new ManualSightingSnapshot(
        ManualSightingDirection.Ahead,
        ManualSightingRange.Close,
        now),
    now,
    streamerMode: true);
Check(hidden is { State: ManualSightingState.Hidden, IsVisible: false, IsCurrent: false },
    "Streamer redaction failed");

Check(ManualSightingLogic.ParseDirection(" RIGHT ") == ManualSightingDirection.Right
      && ManualSightingLogic.ParseDirection("north") == ManualSightingDirection.None
      && ManualSightingLogic.ParseRange("close") == ManualSightingRange.Close
      && ManualSightingLogic.ParseRange("10") == ManualSightingRange.None,
    "Input normalization failed");

var root = Directory.GetCurrentDirectory();
var mainWindowSource = string.Join("\n", Directory.GetFiles(Path.Combine(root, "BurntHud"), "MainWindow*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText));
var mainWindowXaml = File.ReadAllText(Path.Combine(root, "BurntHud", "MainWindow.xaml"));
Check(mainWindowXaml.Contains("x:Name=\"ManualSightingSectionAnchor\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("x:Name=\"ManualSightingPanel\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("x:Name=\"ManualSightingReportButton\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("x:Name=\"UniversalSessionSightingButton\"", StringComparison.Ordinal),
    "Manual Sighting UI contract failed");
Check(mainWindowSource.Contains("private ManualSightingView CurrentManualSightingView", StringComparison.Ordinal)
      && mainWindowSource.Contains("private void UpdateManualSighting", StringComparison.Ordinal)
      && mainWindowSource.Contains("private async void ManualSightingReportButton_Click", StringComparison.Ordinal)
      && mainWindowSource.Contains("ClearManualSighting(", StringComparison.Ordinal),
    "Manual Sighting presentation or lifecycle wiring failed");
Check(mainWindowSource.Contains("new(\"sighting-check\", \"Open Sighting Check\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("case \"sighting-check\":", StringComparison.Ordinal)
      && mainWindowSource.Contains("ManualSightingLogic.FreshnessSeconds", StringComparison.Ordinal),
    "Manual Sighting discovery or expiry contract failed");
Check(mainWindowXaml.Contains("session-only", StringComparison.OrdinalIgnoreCase)
      && mainWindowXaml.Contains("No identity, exact distance, count, motion, or species", StringComparison.Ordinal),
    "Manual Sighting privacy or uncertainty copy failed");

Console.WriteLine(
    "Manual Sighting: PASS (direction/range normalization, 45-second expiry, privacy boundary, streamer redaction, compact UI, and discovery)");
