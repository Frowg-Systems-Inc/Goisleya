namespace Isley;

internal enum ServerRestartWatchPhase
{
    Idle,
    Planning,
    FinalFive,
    FinalTwo,
    FinalMinute,
    Verify
}

internal readonly record struct ServerRestartWatchSnapshot(
    bool Active,
    DateTimeOffset StartedAt,
    int DurationSeconds,
    DateTimeOffset Now);

internal readonly record struct ServerRestartWatchView(
    bool Visible,
    ServerRestartWatchPhase Phase,
    int RemainingSeconds,
    double RemainingFraction,
    string Heading,
    string Countdown,
    string Detail,
    string ActionId,
    string ActionLabel,
    int NoticeLevel,
    bool Pulse);

internal static class ServerRestartWatchLogic
{
    internal static readonly int[] WarningOptions = [1800, 900, 600, 300];

    internal static ServerRestartWatchView Evaluate(ServerRestartWatchSnapshot snapshot)
    {
        var duration = NormalizeDuration(snapshot.DurationSeconds);
        if (!snapshot.Active)
        {
            return new ServerRestartWatchView(
                false,
                ServerRestartWatchPhase.Idle,
                duration,
                0,
                "NO RESTART REPORTED",
                "IDLE",
                "Report the latest in-game server warning when one appears.",
                "restart-watch",
                "REPORT WARNING",
                0,
                false);
        }

        var elapsed = Math.Max(0, (snapshot.Now - snapshot.StartedAt).TotalSeconds);
        var remaining = Math.Max(0, (int)Math.Ceiling(duration - elapsed));
        var remainingFraction = Math.Clamp(remaining / (double)duration, 0, 1);
        var phase = remaining switch
        {
            <= 0 => ServerRestartWatchPhase.Verify,
            <= 60 => ServerRestartWatchPhase.FinalMinute,
            <= 120 => ServerRestartWatchPhase.FinalTwo,
            <= 300 => ServerRestartWatchPhase.FinalFive,
            _ => ServerRestartWatchPhase.Planning
        };

        return phase switch
        {
            ServerRestartWatchPhase.Verify => new ServerRestartWatchView(
                true,
                phase,
                remaining,
                0,
                "RESTART WINDOW ELAPSED",
                "VERIFY",
                "The reported window elapsed. Verify the in-game server state or cancel the watch.",
                "restart-watch",
                "REVIEW WATCH",
                4,
                false),
            ServerRestartWatchPhase.FinalMinute => new ServerRestartWatchView(
                true,
                phase,
                remaining,
                remainingFraction,
                "SAFE LOGOUT NOW",
                FormatRemaining(remaining),
                "Stay still, use the in-game safe-log flow, and keep the server warning visible.",
                "safe-logout",
                "START LOGOUT",
                3,
                true),
            ServerRestartWatchPhase.FinalTwo => new ServerRestartWatchView(
                true,
                phase,
                remaining,
                remainingFraction,
                "PREPARE SAFE LOGOUT",
                FormatRemaining(remaining),
                "Stop traveling or fighting. Reach cover and begin the in-game safe-log flow.",
                "safe-logout",
                "START LOGOUT",
                2,
                false),
            ServerRestartWatchPhase.FinalFive => new ServerRestartWatchView(
                true,
                phase,
                remaining,
                remainingFraction,
                "FINISH AND FIND COVER",
                FormatRemaining(remaining),
                "Avoid long crossings and new fights. Choose cover before the warning gets short.",
                "safe-logout-setup",
                "OPEN LOGOUT",
                1,
                false),
            _ => new ServerRestartWatchView(
                true,
                phase,
                remaining,
                remainingFraction,
                "RESTART REPORTED",
                FormatRemaining(remaining),
                "Finish the current objective, choose cover, and watch for a newer in-game warning.",
                "restart-watch",
                "OPEN WATCH",
                0,
                false)
        };
    }

    internal static int NormalizeDuration(int seconds) =>
        WarningOptions.Contains(seconds) ? seconds : 600;

    internal static string FormatRemaining(int seconds)
    {
        var safe = Math.Max(0, seconds);
        return $"{safe / 60}:{safe % 60:00}";
    }

    internal static string WarningLabel(int seconds) =>
        $"{NormalizeDuration(seconds) / 60} MIN";
}
