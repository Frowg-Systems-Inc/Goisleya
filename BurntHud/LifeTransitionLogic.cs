namespace Isley;

internal readonly record struct LiveDinoSample(
    string SpeciesId,
    int GrowthPercent,
    DateTimeOffset ObservedAt);

internal enum LifeTransitionReason
{
    None,
    SpeciesChanged,
    GrowthReset,
    SpeciesAndGrowth
}

internal readonly record struct LifeTransitionAnalysis(
    bool Detected,
    LifeTransitionReason Reason,
    string PreviousSpeciesId,
    string CurrentSpeciesId,
    int PreviousGrowthPercent,
    int CurrentGrowthPercent,
    int DropPercent,
    string Heading,
    string Detail,
    string Key);

internal static class LifeTransitionLogic
{
    internal const int MinimumSampleGapSeconds = 5;
    internal const int MaximumSampleGapSeconds = 180;
    internal const int GrowthResetThreshold = 3;

    internal static LifeTransitionAnalysis Analyze(
        LiveDinoSample? previous,
        LiveDinoSample current)
    {
        if (previous is null
            || !TryNormalize(previous.Value, out var prior)
            || !TryNormalize(current, out var latest))
        {
            return None();
        }

        var gapSeconds = (latest.ObservedAt - prior.ObservedAt).TotalSeconds;
        if (gapSeconds < MinimumSampleGapSeconds || gapSeconds > MaximumSampleGapSeconds)
        {
            return None();
        }

        var speciesChanged = !string.Equals(
            prior.SpeciesId,
            latest.SpeciesId,
            StringComparison.OrdinalIgnoreCase);
        var drop = prior.GrowthPercent - latest.GrowthPercent;
        var growthReset = drop >= GrowthResetThreshold;
        if (!speciesChanged && !growthReset)
        {
            return None();
        }

        var reason = speciesChanged && growthReset
            ? LifeTransitionReason.SpeciesAndGrowth
            : speciesChanged
                ? LifeTransitionReason.SpeciesChanged
                : LifeTransitionReason.GrowthReset;
        var previousName = LiveSpeciesBridgeLogic.DisplayName(prior.SpeciesId);
        var currentName = LiveSpeciesBridgeLogic.DisplayName(latest.SpeciesId);
        var heading = reason == LifeTransitionReason.GrowthReset
            ? "LIVE GROWTH RESTARTED"
            : "LIVE DINOSAUR CHANGED";
        var detail = reason switch
        {
            LifeTransitionReason.SpeciesChanged =>
                $"The live feed moved from {previousName} to {currentName}.",
            LifeTransitionReason.GrowthReset =>
                $"{currentName} live growth moved from {prior.GrowthPercent}% to {latest.GrowthPercent}%.",
            _ =>
                $"The live feed moved from {previousName} {prior.GrowthPercent}% to {currentName} {latest.GrowthPercent}%."
        };
        var key = $"{reason}:{prior.SpeciesId}:{prior.GrowthPercent}:{latest.SpeciesId}:{latest.GrowthPercent}";
        return new LifeTransitionAnalysis(
            true,
            reason,
            prior.SpeciesId,
            latest.SpeciesId,
            prior.GrowthPercent,
            latest.GrowthPercent,
            Math.Max(0, drop),
            heading,
            detail,
            key);
    }

    private static bool TryNormalize(LiveDinoSample sample, out LiveDinoSample normalized)
    {
        var speciesIndex = LiveSpeciesBridgeLogic.SpeciesIndex(sample.SpeciesId);
        if (speciesIndex == 0
            || sample.GrowthPercent is < 0 or > 100
            || sample.ObservedAt == default)
        {
            normalized = default;
            return false;
        }

        normalized = sample with
        {
            SpeciesId = DietCoachLogic.Species[speciesIndex - 1].Id
        };
        return true;
    }

    private static LifeTransitionAnalysis None() => new(
        false,
        LifeTransitionReason.None,
        string.Empty,
        string.Empty,
        0,
        0,
        0,
        string.Empty,
        string.Empty,
        string.Empty);
}
