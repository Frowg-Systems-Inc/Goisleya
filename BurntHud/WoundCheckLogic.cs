namespace Isley;

internal readonly record struct WoundObservationOption(
    string Id,
    string Label,
    string RangeLabel,
    string VisualCue,
    string Action,
    ReportedHealthState ManualHealth,
    int Severity);

internal static class WoundCheckLogic
{
    internal const string LightId = "light";
    internal const string VisibleId = "visible";
    internal const string HeavyId = "heavy";
    internal const string SevereId = "severe";
    internal const string SnapshotDate = "2026-05-28";

    internal static readonly WoundObservationOption[] Options =
    [
        new(
            LightId,
            "LIGHT",
            "~90–100%",
            "Cuts are barely visible and screen-edge splatter is light or muddy.",
            "Keep watching the screen edge; this broad visual estimate is not exact HP.",
            ReportedHealthState.Stable,
            0),
        new(
            VisibleId,
            "VISIBLE",
            "~70–90%",
            "Red wounds are visible and the screen-edge splatter is denser.",
            "Verify the in-game EKG and stamina before another commitment.",
            ReportedHealthState.Stable,
            0),
        new(
            HeavyId,
            "HEAVY",
            "~40–70%",
            "Deep bright wounds and heavy screen-edge splatter are obvious.",
            "Disengage, reach cover, and avoid re-entering until the in-game read improves.",
            ReportedHealthState.Hurt,
            1),
        new(
            SevereId,
            "SEVERE",
            "~0–30%",
            "Severe wounds cover the body and intense splatter floods the lower screen.",
            "Disengage now, hide, and follow the in-game EKG.",
            ReportedHealthState.Critical,
            2)
    ];

    internal static string Normalize(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return Options.Any(option => option.Id == normalized)
            ? normalized
            : string.Empty;
    }

    internal static WoundObservationOption? Find(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0
            ? null
            : Options.First(option => option.Id == normalized);
    }

    internal static bool IsCurrent(
        string? value,
        DateTimeOffset reportedAt,
        DateTimeOffset now)
    {
        if (Find(value) is null || reportedAt == default)
        {
            return false;
        }

        var ageSeconds = Math.Max(0, (now - reportedAt).TotalSeconds);
        return ageSeconds < CoreVitalsLogic.FreshnessSeconds;
    }
}
