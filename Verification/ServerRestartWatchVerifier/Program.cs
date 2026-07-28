using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var start = DateTimeOffset.Parse("2026-07-22T12:00:00Z");

var idle = ServerRestartWatchLogic.Evaluate(new(false, start, 600, start));
Check(!idle.Visible && idle.Phase == ServerRestartWatchPhase.Idle, "idle state");
Check(idle.Detail.Contains("latest in-game", StringComparison.Ordinal), "idle source disclosure");

var planning = ServerRestartWatchLogic.Evaluate(new(true, start, 600, start.AddSeconds(1)));
Check(planning.Phase == ServerRestartWatchPhase.Planning, "planning phase");
Check(planning.Countdown == "9:59" && planning.ActionId == "restart-watch", "planning display");

var five = ServerRestartWatchLogic.Evaluate(new(true, start, 600, start.AddSeconds(300)));
Check(five.Phase == ServerRestartWatchPhase.FinalFive, "five-minute phase");
Check(five.NoticeLevel == 1 && five.ActionId == "safe-logout-setup", "five-minute preparation handoff");

var two = ServerRestartWatchLogic.Evaluate(new(true, start, 600, start.AddSeconds(480)));
Check(two.Phase == ServerRestartWatchPhase.FinalTwo, "two-minute phase");
Check(two.NoticeLevel == 2 && two.Heading == "PREPARE SAFE LOGOUT", "two-minute escalation");

var minute = ServerRestartWatchLogic.Evaluate(new(true, start, 600, start.AddSeconds(540)));
Check(minute.Phase == ServerRestartWatchPhase.FinalMinute, "final-minute phase");
Check(minute.Pulse && minute.NoticeLevel == 3 && minute.ActionLabel == "START LOGOUT", "final-minute escalation");

var due = ServerRestartWatchLogic.Evaluate(new(true, start, 600, start.AddSeconds(601)));
Check(due.Phase == ServerRestartWatchPhase.Verify && due.Countdown == "VERIFY", "elapsed phase");
Check(due.Detail.Contains("Verify", StringComparison.Ordinal) && due.NoticeLevel == 4, "truthful elapsed state");

Check(ServerRestartWatchLogic.WarningOptions.SequenceEqual([1800, 900, 600, 300]), "warning options");
Check(ServerRestartWatchLogic.NormalizeDuration(42) == 600, "invalid duration fallback");
Check(ServerRestartWatchLogic.FormatRemaining(61) == "1:01", "countdown formatter");

Console.WriteLine("Server restart watch: PASS (manual source boundary, 30/15/10/5-minute reports, 5/2/1 escalation, Safe Logout handoff, and truthful verification state)");
