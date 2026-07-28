namespace Isley;

internal enum ManualSightingDirection
{
    None,
    Ahead,
    Right,
    Behind,
    Left
}

internal enum ManualSightingRange
{
    None,
    Far,
    Near,
    Close
}

internal enum ManualSightingState
{
    Hidden,
    Ready,
    Current,
    Expired
}

internal readonly record struct ManualSightingSnapshot(
    ManualSightingDirection Direction,
    ManualSightingRange Range,
    DateTimeOffset? ReportedAt);

internal readonly record struct ManualSightingView(
    ManualSightingState State,
    ManualSightingDirection Direction,
    ManualSightingRange Range,
    string DirectionLabel,
    string RangeLabel,
    string Badge,
    string Heading,
    string Detail,
    string BriefLabel,
    int AgeSeconds,
    int RemainingSeconds,
    int Urgency)
{
    internal bool IsVisible => State != ManualSightingState.Hidden;
    internal bool IsCurrent => State == ManualSightingState.Current;
    internal bool CanClear => State is ManualSightingState.Current or ManualSightingState.Expired;
}

internal static class ManualSightingLogic
{
    internal const int FreshnessSeconds = 45;

    internal static ManualSightingView Evaluate(
        ManualSightingSnapshot raw,
        DateTimeOffset now,
        bool streamerMode = false)
    {
        if (streamerMode)
        {
            return View(
                ManualSightingState.Hidden,
                ManualSightingDirection.None,
                ManualSightingRange.None,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                0,
                0,
                0);
        }

        var direction = NormalizeDirection(raw.Direction);
        var range = NormalizeRange(raw.Range);
        if (raw.ReportedAt is null
            || direction == ManualSightingDirection.None
            || range == ManualSightingRange.None)
        {
            return View(
                ManualSightingState.Ready,
                direction,
                range,
                DirectionLabel(direction),
                RangeLabel(range),
                "READY",
                "REPORT A SIGHTING",
                "Choose a relative direction and broad range, then report only what you personally observed.",
                string.Empty,
                0,
                FreshnessSeconds,
                0);
        }

        var elapsed = now - raw.ReportedAt.Value;
        var elapsedSeconds = Math.Max(0, elapsed.TotalSeconds);
        var ageSeconds = Math.Max(0, (int)Math.Floor(elapsedSeconds));
        var directionLabel = DirectionLabel(direction);
        var rangeLabel = RangeLabel(range);
        if (elapsedSeconds >= FreshnessSeconds)
        {
            return View(
                ManualSightingState.Expired,
                direction,
                range,
                directionLabel,
                rangeLabel,
                "EXPIRED",
                "SIGHTING EXPIRED",
                "Isley stopped using this report. Report again only if the contact is still personally observed.",
                string.Empty,
                ageSeconds,
                0,
                0);
        }

        var remainingSeconds = Math.Clamp(
            (int)Math.Ceiling(FreshnessSeconds - elapsedSeconds),
            1,
            FreshnessSeconds);
        var urgency = range switch
        {
            ManualSightingRange.Close => 3,
            ManualSightingRange.Near => 2,
            _ => 1
        };
        var heading = range switch
        {
            ManualSightingRange.Close => "CREATE SPACE",
            ManualSightingRange.Near => "HOLD AN EXIT",
            _ => "MONITOR THE CONTACT"
        };
        var response = range switch
        {
            ManualSightingRange.Close =>
                "Preserve stamina, avoid a blind chase, and keep an immediate exit.",
            ManualSightingRange.Near =>
                "Keep terrain and an exit in view; do not assume the contact is alone.",
            _ =>
                "Keep observing from safety and update the report only if the range changes."
        };
        var badge = $"{rangeLabel} {directionLabel}";
        return View(
            ManualSightingState.Current,
            direction,
            range,
            directionLabel,
            rangeLabel,
            badge,
            heading,
            $"Player-reported {rangeLabel.ToLowerInvariant()} contact {DirectionPhrase(direction)}. " +
            $"{response} No identity, exact distance, count, motion, or species is inferred.",
            $"SIGHTING {badge} {remainingSeconds}S",
            ageSeconds,
            remainingSeconds,
            urgency);
    }

    internal static ManualSightingDirection ParseDirection(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "ahead" => ManualSightingDirection.Ahead,
            "right" => ManualSightingDirection.Right,
            "behind" => ManualSightingDirection.Behind,
            "left" => ManualSightingDirection.Left,
            _ => ManualSightingDirection.None
        };

    internal static ManualSightingRange ParseRange(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "close" => ManualSightingRange.Close,
            "near" => ManualSightingRange.Near,
            "far" => ManualSightingRange.Far,
            _ => ManualSightingRange.None
        };

    internal static string DirectionLabel(ManualSightingDirection direction) =>
        NormalizeDirection(direction) switch
        {
            ManualSightingDirection.Ahead => "AHEAD",
            ManualSightingDirection.Right => "RIGHT",
            ManualSightingDirection.Behind => "BEHIND",
            ManualSightingDirection.Left => "LEFT",
            _ => "DIRECTION"
        };

    internal static string RangeLabel(ManualSightingRange range) =>
        NormalizeRange(range) switch
        {
            ManualSightingRange.Close => "CLOSE",
            ManualSightingRange.Near => "NEAR",
            ManualSightingRange.Far => "FAR",
            _ => "RANGE"
        };

    private static ManualSightingDirection NormalizeDirection(ManualSightingDirection direction) =>
        Enum.IsDefined(direction) ? direction : ManualSightingDirection.None;

    private static ManualSightingRange NormalizeRange(ManualSightingRange range) =>
        Enum.IsDefined(range) ? range : ManualSightingRange.None;

    private static string DirectionPhrase(ManualSightingDirection direction) =>
        NormalizeDirection(direction) switch
        {
            ManualSightingDirection.Ahead => "ahead",
            ManualSightingDirection.Right => "to your right",
            ManualSightingDirection.Behind => "behind you",
            ManualSightingDirection.Left => "to your left",
            _ => "nearby"
        };

    private static ManualSightingView View(
        ManualSightingState state,
        ManualSightingDirection direction,
        ManualSightingRange range,
        string directionLabel,
        string rangeLabel,
        string badge,
        string heading,
        string detail,
        string briefLabel,
        int ageSeconds,
        int remainingSeconds,
        int urgency) =>
        new(
            state,
            direction,
            range,
            directionLabel,
            rangeLabel,
            badge,
            heading,
            detail,
            briefLabel,
            Math.Max(0, ageSeconds),
            Math.Max(0, remainingSeconds),
            Math.Clamp(urgency, 0, 3));
}
