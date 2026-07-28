namespace Isley;

internal enum RecoveryMovementState
{
    Hidden,
    Manual,
    Waiting,
    Moving,
    Settling,
    Resting
}

internal readonly record struct RecoveryMonitorSnapshot(
    string IncidentId,
    bool StreamerMode,
    bool LiveMapServicesActive,
    bool MarkerFresh,
    double MovementSpeed,
    DateTimeOffset? StillSince,
    DateTimeOffset Now);

internal readonly record struct RecoveryMonitorView(
    RecoveryMovementState State,
    DateTimeOffset? StillSince,
    int RestSeconds,
    string Label,
    string Detail,
    string PriorityOverride,
    bool IsVisible,
    bool IsWarning);

internal static class RecoveryMonitorLogic
{
    internal const string GuidanceSnapshot = "2026-07-22";
    internal const double MovementThresholdMuPerMinute = 0.25;
    internal const int SettlingSeconds = 3;
    internal const int QualifiedRestSeconds = 10;
    private static readonly HashSet<string> SupportedIncidentIds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "bleeding",
            "fracture",
            "wounded"
        };

    internal static bool Supports(string? incidentId) =>
        !string.IsNullOrWhiteSpace(incidentId)
        && SupportedIncidentIds.Contains(incidentId);

    internal static RecoveryMonitorView Evaluate(RecoveryMonitorSnapshot snapshot)
    {
        var incidentId = (snapshot.IncidentId ?? string.Empty).Trim().ToLowerInvariant();
        if (snapshot.StreamerMode || !Supports(incidentId))
        {
            return Hidden();
        }

        if (!snapshot.LiveMapServicesActive)
        {
            return new RecoveryMonitorView(
                RecoveryMovementState.Manual,
                null,
                0,
                "MANUAL REST CHECK",
                "No authorized movement monitor on this session. Rest and verify the condition in game.",
                string.Empty,
                true,
                false);
        }

        if (!snapshot.MarkerFresh || !double.IsFinite(snapshot.MovementSpeed))
        {
            return new RecoveryMonitorView(
                RecoveryMovementState.Waiting,
                null,
                0,
                "MOVEMENT CHECK WAITING",
                "Your fresh authorized self marker is unavailable. Rest manually; monitoring resumes with the feed.",
                string.Empty,
                true,
                false);
        }

        var safeSpeed = Math.Max(0, snapshot.MovementSpeed);
        if (safeSpeed >= MovementThresholdMuPerMinute)
        {
            return new RecoveryMonitorView(
                RecoveryMovementState.Moving,
                null,
                0,
                $"MOVING · {safeSpeed:0.0}/MIN",
                MovingDetail(incidentId),
                "STOP MOVING · REST NOW",
                true,
                true);
        }

        var stillSince = snapshot.StillSince is { } previous
                         && previous <= snapshot.Now
                         && snapshot.Now - previous <= TimeSpan.FromHours(24)
            ? previous
            : snapshot.Now;
        var restSeconds = Math.Clamp(
            (int)Math.Floor((snapshot.Now - stillSince).TotalSeconds),
            0,
            24 * 60 * 60);
        if (restSeconds < SettlingSeconds)
        {
            return new RecoveryMonitorView(
                RecoveryMovementState.Settling,
                stillSince,
                restSeconds,
                $"HOLD STILL · {restSeconds}/{SettlingSeconds}S",
                "Establishing a reliable stationary streak from the authorized marker.",
                string.Empty,
                true,
                false);
        }

        return new RecoveryMonitorView(
            RecoveryMovementState.Resting,
            stillSince,
            restSeconds,
            $"RESTING · {FormatElapsed(restSeconds)}",
            RestingDetail(incidentId),
            string.Empty,
            true,
            false);
    }

    internal static string FormatElapsed(int totalSeconds)
    {
        var safeSeconds = Math.Max(0, totalSeconds);
        return safeSeconds >= 3600
            ? $"{safeSeconds / 3600}:{safeSeconds % 3600 / 60:00}:{safeSeconds % 60:00}"
            : $"{safeSeconds / 60}:{safeSeconds % 60:00}";
    }

    private static RecoveryMonitorView Hidden() => new(
        RecoveryMovementState.Hidden,
        null,
        0,
        string.Empty,
        string.Empty,
        string.Empty,
        false,
        false);

    private static string MovingDetail(string incidentId) => incidentId switch
    {
        "bleeding" => "Authorized movement resumed while bleeding is active. Stop and lie down; watch the in-game bleed and EKG.",
        "fracture" => "Authorized movement resumed during fracture recovery. Hide and rest again; verify locked health in game.",
        _ => "Authorized movement resumed during low-health recovery. Rest and watch the in-game EKG before moving again."
    };

    private static string RestingDetail(string incidentId) => incidentId switch
    {
        "bleeding" => "Authorized marker is stationary. Keep resting and watch the in-game bleed and EKG; this is not a heal timer.",
        "fracture" => "Authorized marker is stationary. Keep resting; fracture and locked-health recovery remain game-authoritative.",
        _ => "Authorized marker is stationary. Keep resting and verify health on the in-game EKG before moving."
    };
}
