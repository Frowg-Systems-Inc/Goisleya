using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var now = DateTimeOffset.Parse("2026-07-22T10:00:00Z");

Check(RecoveryMonitorLogic.GuidanceSnapshot == "2026-07-22", "guidance snapshot");

Check(RecoveryMonitorLogic.Supports("bleeding")
      && RecoveryMonitorLogic.Supports("fracture")
      && RecoveryMonitorLogic.Supports("wounded")
      && !RecoveryMonitorLogic.Supports("vomit")
      && !RecoveryMonitorLogic.Supports(null),
    "supported incident scope");

var hidden = RecoveryMonitorLogic.Evaluate(new(
    "vomit", false, true, true, 0, null, now));
Check(hidden.State == RecoveryMovementState.Hidden && !hidden.IsVisible,
    "unrelated condition must stay hidden");

var streamer = RecoveryMonitorLogic.Evaluate(new(
    "bleeding", true, true, true, 0, null, now));
Check(streamer.State == RecoveryMovementState.Hidden && !streamer.IsVisible,
    "streamer redaction");

var manual = RecoveryMonitorLogic.Evaluate(new(
    "fracture", false, false, false, 20, null, now));
Check(manual.State == RecoveryMovementState.Manual
      && manual.IsVisible
      && manual.Detail.Contains("verify", StringComparison.OrdinalIgnoreCase),
    "universal manual fallback");

var waiting = RecoveryMonitorLogic.Evaluate(new(
    "wounded", false, true, false, 0, now.AddSeconds(-20), now));
Check(waiting.State == RecoveryMovementState.Waiting
      && waiting.StillSince is null
      && waiting.Detail.Contains("marker", StringComparison.OrdinalIgnoreCase),
    "missing marker refusal");

var moving = RecoveryMonitorLogic.Evaluate(new(
    "bleeding", false, true, true, 0.25, now.AddSeconds(-30), now));
Check(moving.State == RecoveryMovementState.Moving
      && moving.IsWarning
      && moving.StillSince is null
      && moving.PriorityOverride == "STOP MOVING · REST NOW"
      && moving.Detail.Contains("bleed", StringComparison.OrdinalIgnoreCase),
    "movement warning boundary");

var settling = RecoveryMonitorLogic.Evaluate(new(
    "bleeding", false, true, true, 0.24, now.AddSeconds(-2), now));
Check(settling.State == RecoveryMovementState.Settling
      && settling.RestSeconds == 2
      && settling.Label == "HOLD STILL · 2/3S",
    "stationary settling window");

var resting = RecoveryMonitorLogic.Evaluate(new(
    "fracture", false, true, true, 0, now.AddSeconds(-67), now));
Check(resting.State == RecoveryMovementState.Resting
      && resting.RestSeconds == 67
      && resting.Label == "RESTING · 1:07"
      && resting.Detail.Contains("game-authoritative", StringComparison.OrdinalIgnoreCase),
    "verified resting streak");

var futureStillSince = RecoveryMonitorLogic.Evaluate(new(
    "wounded", false, true, true, 0, now.AddMinutes(1), now));
Check(futureStillSince.State == RecoveryMovementState.Settling
      && futureStillSince.StillSince == now,
    "future timestamp normalization");

Check(RecoveryMonitorLogic.FormatElapsed(0) == "0:00"
      && RecoveryMonitorLogic.FormatElapsed(3599) == "59:59"
      && RecoveryMonitorLogic.FormatElapsed(3600) == "1:00:00",
    "elapsed formatting");

Console.WriteLine(
    "Rest & Recovery Monitor: PASS (condition scope, movement boundary, settling, resting, manual/waiting honesty, privacy, and formatting)");
