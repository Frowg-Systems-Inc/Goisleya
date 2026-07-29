namespace Isley;

/// <summary>
/// Bounded view of simultaneous guidance that competes for a single HUD slot.
/// <see cref="Shown"/> keeps the top ≤3 recommendations in deterministic
/// priority order; <see cref="OverflowCount"/> and the overflow copy honestly
/// report how many more active recommendations the slot could not show.
/// </summary>
internal readonly record struct StackedGuidanceView(
    IReadOnlyList<NextMoveRecommendation> Shown,
    int TotalActive,
    int OverflowCount,
    string OverflowSuffix,
    string OverflowTooltip)
{
    internal NextMoveRecommendation Top => Shown[0];
    internal bool HasOverflow => OverflowCount > 0;
}

internal static class StackedGuidanceLogic
{
    internal const int MaxShown = 3;

    /// <summary>
    /// Ranks every active guidance candidate deterministically: highest declared
    /// priority first (the Next Move ladder already encodes safety &gt;
    /// vitals-critical &gt; timers &gt; planners &gt; informational), with the
    /// cascade's declaration order as a stable tiebreak. The result is bounded
    /// to <paramref name="maxShown"/> (clamped to 1..<see cref="MaxShown"/>)
    /// and any remaining candidates surface as an honest "+N more" affordance.
    /// </summary>
    internal static StackedGuidanceView Rank(
        IReadOnlyList<NextMoveRecommendation> candidates,
        int maxShown = MaxShown)
    {
        if (candidates is null || candidates.Count == 0)
        {
            throw new ArgumentException(
                "At least one guidance candidate is required.",
                nameof(candidates));
        }

        var boundedMaxShown = Math.Clamp(maxShown, 1, MaxShown);
        // OrderByDescending is documented stable: equal priorities keep the
        // cascade's declaration order, so equal-priority stacks never shuffle.
        var ranked = candidates
            .OrderByDescending(candidate => candidate.Priority)
            .ToArray();
        var shown = ranked.Take(boundedMaxShown).ToArray();
        var overflow = ranked.Skip(boundedMaxShown).ToArray();
        return new StackedGuidanceView(
            shown,
            ranked.Length,
            overflow.Length,
            overflow.Length > 0 ? $"+{overflow.Length}" : string.Empty,
            BuildOverflowTooltip(overflow));
    }

    private static string BuildOverflowTooltip(IReadOnlyList<NextMoveRecommendation> overflow)
    {
        if (overflow.Count == 0)
        {
            return string.Empty;
        }

        var labels = overflow
            .Select(candidate => candidate.Category)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct()
            .Take(3)
            .ToArray();
        var summary = string.Join(" · ", labels);
        if (overflow.Count > labels.Length)
        {
            summary = $"{summary} · and {overflow.Count - labels.Length} more";
        }

        return $"Also active: {summary}. The Next Move slot shows the highest-priority guidance first.";
    }
}
