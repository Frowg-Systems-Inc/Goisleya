using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var start = DateTimeOffset.Parse("2026-07-21T20:00:00Z");

var ready = SafeLogoutLogic.Evaluate(new(
    SafeLogoutGuardState.Ready, start, 60, false, 0, start));
Check(ready.State == SafeLogoutGuardState.Ready && !ready.IsCounting, "ready state");
Check(ready.RemainingSeconds == 60 && ready.Progress == 0, "ready duration");

var monitoredGrace = SafeLogoutLogic.Evaluate(new(
    SafeLogoutGuardState.CountingMonitored, start, 60, true, 99, start.AddSeconds(1)));
Check(monitoredGrace.State == SafeLogoutGuardState.CountingMonitored, "movement grace");

var monitoredStill = SafeLogoutLogic.Evaluate(new(
    SafeLogoutGuardState.CountingMonitored, start, 60, true, 0.24, start.AddSeconds(10)));
Check(monitoredStill.State == SafeLogoutGuardState.CountingMonitored, "stationary threshold");
Check(monitoredStill.Label == "MONITORING · 0:50", "monitored label");

var moved = SafeLogoutLogic.Evaluate(new(
    SafeLogoutGuardState.CountingMonitored, start, 60, true, 0.25, start.AddSeconds(10)));
Check(moved.State == SafeLogoutGuardState.Interrupted && moved.IsWarning, "movement interruption");

var lost = SafeLogoutLogic.Evaluate(new(
    SafeLogoutGuardState.CountingMonitored, start, 60, false, 0, start.AddSeconds(10)));
Check(lost.State == SafeLogoutGuardState.MonitorLost && lost.IsTerminal, "marker-loss interruption");

var manual = SafeLogoutLogic.Evaluate(new(
    SafeLogoutGuardState.CountingManual, start, 90, false, 12, start.AddSeconds(30)));
Check(manual.State == SafeLogoutGuardState.CountingManual, "manual ignores unavailable movement monitor");
Check(manual.RemainingSeconds == 60 && manual.Detail.Contains("No live movement monitor"), "manual disclosure");

var complete = SafeLogoutLogic.Evaluate(new(
    SafeLogoutGuardState.CountingMonitored, start, 60, true, 0, start.AddSeconds(61)));
Check(complete.State == SafeLogoutGuardState.Complete, "completion");
Check(complete.Label == "VERIFY IN GAME" && !complete.IsWarning, "truthful completion label");

var terminalStaysTerminal = SafeLogoutLogic.Evaluate(new(
    SafeLogoutGuardState.Interrupted, start, 60, true, 0, start.AddSeconds(45)));
Check(terminalStaysTerminal.State == SafeLogoutGuardState.Interrupted, "terminal state persistence");

Check(SafeLogoutLogic.NormalizeDuration(75) == 60, "invalid duration fallback");
Check(SafeLogoutLogic.DurationOptions.SequenceEqual([60, 90, 120]), "duration options");
Check(SafeLogoutLogic.FormatRemaining(60) == "1:00", "remaining formatter");

Console.WriteLine(
    "Safe logout verification passed (manual/monitored countdowns, grace, movement, marker loss, completion, and truth-in-UI)." );
