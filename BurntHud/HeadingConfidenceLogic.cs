namespace Isley;

internal enum HeadingConfidenceTier
{
    None,
    Stale,
    Degraded,
    Full
}

internal readonly record struct HeadingConfidenceView(
    HeadingConfidenceTier Tier,
    double HeldDegrees,
    bool Held,
    string Dot,
    string Suffix,
    string CompactSuffix);

internal static class HeadingConfidenceLogic
{
    internal const double FullMaxAgeMs = 2000;
    internal const double DegradedMaxAgeMs = 8000;

    /// <summary>
    /// Decays the displayed heading gracefully when the authorized position feed
    /// stalls. The held value is always the last good heading — it never jumps and
    /// never re-fires — and only the confidence treatment changes with age:
    /// full under 2 s, degraded from 2–8 s, stale (held) beyond 8 s or whenever the
    /// feed-wide stale alert is active.
    /// </summary>
    internal static HeadingConfidenceView Evaluate(
        bool headingAvailable,
        double lastGoodDegrees,
        double freshnessAgeMs,
        bool feedStale)
    {
        if (!headingAvailable)
        {
            return new HeadingConfidenceView(
                HeadingConfidenceTier.None, 0, false, string.Empty, string.Empty, string.Empty);
        }

        var heldDegrees = NormalizeDegrees(lastGoodDegrees);
        var ageMs = double.IsFinite(freshnessAgeMs) ? Math.Max(0, freshnessAgeMs) : 0;
        if (feedStale || ageMs > DegradedMaxAgeMs)
        {
            return new HeadingConfidenceView(
                HeadingConfidenceTier.Stale,
                heldDegrees,
                true,
                "○",
                $" · held · stale {FormatAge(ageMs)}",
                " ○ HELD");
        }

        if (ageMs > FullMaxAgeMs)
        {
            return new HeadingConfidenceView(
                HeadingConfidenceTier.Degraded,
                heldDegrees,
                false,
                "◐",
                " · degraded",
                " ◐");
        }

        return new HeadingConfidenceView(
            HeadingConfidenceTier.Full,
            heldDegrees,
            false,
            "●",
            string.Empty,
            string.Empty);
    }

    internal static string FormatAge(double ageMs)
    {
        var seconds = Math.Max(0, (int)Math.Floor(
            double.IsFinite(ageMs) ? ageMs / 1000 : 0));
        return seconds < 90
            ? $"{seconds}s"
            : $"{(int)Math.Round(seconds / 60.0)}m";
    }

    private static double NormalizeDegrees(double degrees)
    {
        if (!double.IsFinite(degrees))
        {
            return 0;
        }

        var normalized = degrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}
