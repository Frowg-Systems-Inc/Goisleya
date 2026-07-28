namespace Isley;

internal enum SafeLogoutGuardState
{
    Ready,
    CountingMonitored,
    CountingManual,
    Interrupted,
    MonitorLost,
    Complete
}

internal sealed record SafeLogoutGuardSnapshot(
    SafeLogoutGuardState State,
    DateTimeOffset StartedAt,
    int DurationSeconds,
    bool MarkerAvailable,
    double MovementSpeed,
    DateTimeOffset Now);

internal sealed record SafeLogoutGuardView(
    SafeLogoutGuardState State,
    int RemainingSeconds,
    double Progress,
    string Label,
    string Detail,
    bool IsCounting,
    bool IsTerminal,
    bool IsWarning);

internal static class SafeLogoutLogic
{
    internal const int DefaultDurationSeconds = 60;
    internal const int MovementGraceSeconds = 2;
    internal const double MovementThresholdMuPerMinute = 0.25;
    internal static readonly int[] DurationOptions = [60, 90, 120];

    internal static SafeLogoutGuardView Evaluate(SafeLogoutGuardSnapshot snapshot)
    {
        var duration = NormalizeDuration(snapshot.DurationSeconds);
        var elapsed = Math.Clamp(
            (int)Math.Floor((snapshot.Now - snapshot.StartedAt).TotalSeconds),
            0,
            duration);
        var remaining = Math.Max(0, duration - elapsed);
        var progress = Math.Clamp(elapsed / (double)duration, 0, 1);

        if (snapshot.State is SafeLogoutGuardState.Interrupted
            or SafeLogoutGuardState.MonitorLost
            or SafeLogoutGuardState.Complete)
        {
            return BuildTerminal(snapshot.State, remaining, progress);
        }

        if (snapshot.State == SafeLogoutGuardState.Ready)
        {
            return new SafeLogoutGuardView(
                SafeLogoutGuardState.Ready,
                duration,
                0,
                "READY",
                "Start after holding your in-game rest / logout control.",
                false,
                false,
                false);
        }

        if (snapshot.State == SafeLogoutGuardState.CountingMonitored)
        {
            if (!snapshot.MarkerAvailable)
            {
                return BuildTerminal(SafeLogoutGuardState.MonitorLost, remaining, progress);
            }

            if (elapsed >= MovementGraceSeconds
                && double.IsFinite(snapshot.MovementSpeed)
                && snapshot.MovementSpeed >= MovementThresholdMuPerMinute)
            {
                return BuildTerminal(SafeLogoutGuardState.Interrupted, remaining, progress);
            }
        }

        if (remaining == 0)
        {
            return BuildTerminal(SafeLogoutGuardState.Complete, 0, 1);
        }

        var monitored = snapshot.State == SafeLogoutGuardState.CountingMonitored;
        return new SafeLogoutGuardView(
            snapshot.State,
            remaining,
            progress,
            monitored ? $"MONITORING · {FormatRemaining(remaining)}" : $"MANUAL · {FormatRemaining(remaining)}",
            monitored
                ? "Movement or marker loss will interrupt this guard."
                : "No live movement monitor on this session; stay still and verify in game.",
            true,
            false,
            false);
    }

    internal static int NormalizeDuration(int seconds) =>
        DurationOptions.Contains(seconds) ? seconds : DefaultDurationSeconds;

    internal static string FormatRemaining(int seconds)
    {
        var safeSeconds = Math.Max(0, seconds);
        return $"{safeSeconds / 60}:{safeSeconds % 60:00}";
    }

    private static SafeLogoutGuardView BuildTerminal(
        SafeLogoutGuardState state,
        int remaining,
        double progress) => state switch
        {
            SafeLogoutGuardState.Interrupted => new SafeLogoutGuardView(
                state,
                remaining,
                progress,
                "INTERRUPTED",
                "Movement detected. Restart only after you are still and resting again.",
                false,
                true,
                true),
            SafeLogoutGuardState.MonitorLost => new SafeLogoutGuardView(
                state,
                remaining,
                progress,
                "MONITOR LOST",
                "Your authorized self-marker disappeared. Restart after the feed returns.",
                false,
                true,
                true),
            _ => new SafeLogoutGuardView(
                SafeLogoutGuardState.Complete,
                0,
                1,
                "VERIFY IN GAME",
                "Countdown complete. The game/server remains authoritative for logout.",
                false,
                true,
                false)
        };
}
