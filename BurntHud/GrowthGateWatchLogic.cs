namespace Isley;

internal readonly record struct LiveGrowthGateSample(
    string SpeciesId,
    int GrowthPercent,
    DateTimeOffset ObservedAt);

internal enum GrowthGateKind
{
    None,
    Mutation,
    PrimeWindow,
    PrimePeak,
    Elder
}

internal readonly record struct GrowthGateWatchAnalysis(
    bool Detected,
    GrowthGateKind Kind,
    int GatePercent,
    int PreviousGrowthPercent,
    int CurrentGrowthPercent,
    string Heading,
    string Detail,
    string ActionId,
    string ActionLabel,
    string Key);

internal static class GrowthGateWatchLogic
{
    internal const int MinimumSampleGapSeconds = 5;
    internal const int MaximumSampleGapSeconds = 180;
    internal static readonly int[] Gates = [50, 75, 87, 100];

    internal static GrowthGateWatchAnalysis Analyze(
        LiveGrowthGateSample? previous,
        LiveGrowthGateSample current)
    {
        if (previous is null
            || !TryNormalize(previous.Value, out var prior)
            || !TryNormalize(current, out var latest))
        {
            return None();
        }

        var gapSeconds = (latest.ObservedAt - prior.ObservedAt).TotalSeconds;
        if (gapSeconds < MinimumSampleGapSeconds
            || gapSeconds > MaximumSampleGapSeconds
            || !string.Equals(prior.SpeciesId, latest.SpeciesId, StringComparison.OrdinalIgnoreCase)
            || latest.GrowthPercent <= prior.GrowthPercent)
        {
            return None();
        }

        var gate = Gates
            .Where(candidate => prior.GrowthPercent < candidate
                                && latest.GrowthPercent >= candidate)
            .DefaultIfEmpty(0)
            .Max();
        if (gate == 0)
        {
            return None();
        }

        var (kind, heading, detail, actionId, actionLabel) = gate switch
        {
            50 => (
                GrowthGateKind.Mutation,
                "50% GATE REACHED",
                "A lifecycle mutation gate may now be available. Verify the slot and current server rules in game.",
                "mutation-planner",
                "OPEN MUTATIONS"),
            75 => (
                GrowthGateKind.PrimeWindow,
                "PRIME WINDOW OPEN",
                "Growth crossed 75%. Verify Prime with the in-game mutation slot before trusting planned status.",
                "prime-planner",
                "OPEN PRIME"),
            87 => (
                GrowthGateKind.PrimePeak,
                "PRIME PEAK CHECK",
                "Growth crossed 87%. Current community guidance marks this as the Prime peak; verify in game and avoid assuming an advantage.",
                "prime-planner",
                "OPEN PRIME"),
            _ => (
                GrowthGateKind.Elder,
                "ELDER CHECK READY",
                "Growth reached 100%. Verify Elder and Entomb eligibility in game before recording anything.",
                "elder-lineage",
                "OPEN ELDER")
        };
        return new GrowthGateWatchAnalysis(
            true,
            kind,
            gate,
            prior.GrowthPercent,
            latest.GrowthPercent,
            heading,
            detail,
            actionId,
            actionLabel,
            $"{latest.SpeciesId}:{gate}:{latest.GrowthPercent}");
    }

    private static bool TryNormalize(
        LiveGrowthGateSample sample,
        out LiveGrowthGateSample normalized)
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

    private static GrowthGateWatchAnalysis None() => new(
        false,
        GrowthGateKind.None,
        0,
        0,
        0,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);
}
