namespace Isley;

internal readonly record struct GrowthSpeciesTiming(
    string Id,
    string Name,
    double BaseHours,
    bool Approximate = false);

internal readonly record struct GrowthMilestone(
    int Percent,
    string Label);

internal readonly record struct GrowthPlannerSnapshot(
    int SpeciesIndex,
    int GrowthPercent,
    int ServerMultiplierIndex,
    int DietFilledCount,
    bool Paused,
    int PrimeCount,
    int PrimeRequired);

internal readonly record struct GrowthPlannerResult(
    GrowthPlannerSnapshot Snapshot,
    GrowthSpeciesTiming? Species,
    GrowthMilestone Milestone,
    double ServerMultiplier,
    int DietMultiplier,
    double? EstimatedMinutes,
    string EtaLabel,
    string Advice);

internal static class GrowthPlannerLogic
{
    internal const string SnapshotDate = "2026-05-28";
    internal const int DefaultLiveMapMultiplierIndex = 0;

    internal static readonly double[] ServerMultipliers = [1, 1.5, 2, 3, 5];

    // Order intentionally matches DietCoachLogic.Species so an active Diet Coach
    // selection can drive this calculator without another species selector.
    internal static readonly GrowthSpeciesTiming[] SpeciesTimings =
    [
        new("allosaurus", "Allosaurus", 10),
        new("carnotaurus", "Carnotaurus", 7.67),
        new("ceratosaurus", "Ceratosaurus", 6),
        new("deinosuchus", "Deinosuchus", 23.05),
        new("dilophosaurus", "Dilophosaurus", 6),
        new("herrerasaurus", "Herrerasaurus", 5.42),
        new("omniraptor", "Omniraptor", 5.92),
        new("pteranodon", "Pteranodon", 4.5),
        new("troodon", "Troodon", 3.17),
        new("tyrannosaurus", "Tyrannosaurus", 35.55),
        new("diabloceratops", "Diabloceratops", 7.75),
        new("dryosaurus", "Dryosaurus", 4.42),
        new("hypsilophodon", "Hypsilophodon", 1.83),
        new("kentrosaurus", "Kentrosaurus", 11, true),
        new("maiasaura", "Maiasaura", 7),
        new("pachycephalosaurus", "Pachycephalosaurus", 6.25),
        new("stegosaurus", "Stegosaurus", 17.92),
        new("tenontosaurus", "Tenontosaurus", 5.67),
        new("triceratops", "Triceratops", 12, true),
        new("beipiaosaurus", "Beipiaosaurus", 3.67),
        new("gallimimus", "Gallimimus", 5.42)
    ];

    internal static readonly GrowthMilestone[] Milestones =
    [
        new(25, "JUVENILE"),
        new(50, "SUBADULT / SLOT 3"),
        new(75, "PRIME GATE"),
        new(87, "PRIME PEAK"),
        new(100, "ELDER / ENTOMB")
    ];

    internal static GrowthPlannerSnapshot Normalize(GrowthPlannerSnapshot snapshot) => snapshot with
    {
        SpeciesIndex = Math.Clamp(snapshot.SpeciesIndex, 0, SpeciesTimings.Length),
        GrowthPercent = Math.Clamp(snapshot.GrowthPercent, 0, 100),
        ServerMultiplierIndex = Math.Clamp(snapshot.ServerMultiplierIndex, 0, ServerMultipliers.Length - 1),
        DietFilledCount = Math.Clamp(snapshot.DietFilledCount, 0, 3),
        PrimeCount = Math.Clamp(snapshot.PrimeCount, 0, 10),
        PrimeRequired = Math.Clamp(snapshot.PrimeRequired, 1, 10)
    };

    internal static GrowthSpeciesTiming? Species(int speciesIndex)
    {
        var normalized = Math.Clamp(speciesIndex, 0, SpeciesTimings.Length);
        return normalized == 0 ? null : SpeciesTimings[normalized - 1];
    }

    internal static GrowthMilestone NextMilestone(int growthPercent)
    {
        var growth = Math.Clamp(growthPercent, 0, 100);
        return Milestones.FirstOrDefault(item => item.Percent > growth, Milestones[^1]);
    }

    internal static int StageIndex(int growthPercent) => Math.Clamp(growthPercent, 0, 100) switch
    {
        < 25 => 0,
        < 50 => 1,
        < 75 => 2,
        < 100 => 3,
        _ => 4
    };

    internal static int StageAnchor(int stageIndex) => Math.Clamp(stageIndex, 0, 4) switch
    {
        0 => 10,
        1 => 25,
        2 => 50,
        3 => 75,
        _ => 100
    };

    internal static GrowthPlannerResult Analyze(GrowthPlannerSnapshot snapshot)
    {
        var normalized = Normalize(snapshot);
        var species = Species(normalized.SpeciesIndex);
        var milestone = NextMilestone(normalized.GrowthPercent);
        var serverMultiplier = ServerMultipliers[normalized.ServerMultiplierIndex];
        var dietMultiplier = normalized.DietFilledCount;
        double? estimatedMinutes = null;
        if (!normalized.Paused
            && species is { } selected
            && dietMultiplier > 0
            && milestone.Percent > normalized.GrowthPercent)
        {
            var remainingFraction = (milestone.Percent - normalized.GrowthPercent) / 100d;
            estimatedMinutes = selected.BaseHours * 60d * remainingFraction
                               / serverMultiplier / dietMultiplier;
        }

        var eta = FormatEta(estimatedMinutes, normalized);
        var advice = Advice(normalized, species, milestone);
        return new GrowthPlannerResult(
            normalized,
            species,
            milestone,
            serverMultiplier,
            dietMultiplier,
            estimatedMinutes,
            eta,
            advice);
    }

    internal static string FormatEta(double? minutes, GrowthPlannerSnapshot snapshot)
    {
        var normalized = Normalize(snapshot);
        if (normalized.GrowthPercent >= 100) return "AT ELDER";
        if (normalized.Paused) return "PAUSED";
        if (normalized.SpeciesIndex == 0) return "CHOOSE SPECIES";
        if (normalized.DietFilledCount == 0) return "LOG DIET";
        if (minutes is null || !double.IsFinite(minutes.Value)) return "ESTIMATE UNAVAILABLE";
        var totalMinutes = Math.Max(1, (int)Math.Ceiling(minutes.Value));
        if (totalMinutes < 60) return $"~{totalMinutes}M";
        var hours = totalMinutes / 60;
        var remainder = totalMinutes % 60;
        return remainder == 0 ? $"~{hours}H" : $"~{hours}H {remainder}M";
    }

    internal static string Advice(
        GrowthPlannerSnapshot snapshot,
        GrowthSpeciesTiming? species,
        GrowthMilestone milestone)
    {
        var normalized = Normalize(snapshot);
        if (species is null) return "Choose this dinosaur in Diet Coach to load its current base-time estimate.";
        if (normalized.Paused) return "Growth paused: restore food and water, then resume this manual clock.";
        if (normalized.DietFilledCount == 0) return "Log the nutrient icons shown in game; one, two, and three macros set the 1x, 2x, or 3x diet rate.";
        if (normalized.GrowthPercent < 50) return "Keep food and water above zero; growth stops when either survival floor is empty.";
        if (normalized.GrowthPercent < 75)
        {
            var needed = Math.Max(0, normalized.PrimeRequired - normalized.PrimeCount);
            return needed == 0
                ? "Prime plan ready: reach 75% and verify the fourth mutation slot in game."
                : $"Prime deadline: complete {needed} more condition{(needed == 1 ? string.Empty : "s")} before 75%.";
        }
        if (normalized.GrowthPercent < 87) return "Prime window: strength ramps toward its reported peak near 87%.";
        if (normalized.GrowthPercent < 100) return "Aging toward Elder: verify eligibility in game before planning an Entomb cycle.";
        return "At 100%: verify Elder or Prime Elder and Entomb eligibility in game.";
    }

    internal static string CompactSummary(GrowthPlannerResult result)
    {
        var snapshot = result.Snapshot;
        if (snapshot.GrowthPercent >= 100) return "GROW 100% ELDER CHECK";
        if (snapshot.Paused) return $"GROW {snapshot.GrowthPercent}% PAUSED";
        return $"GROW {snapshot.GrowthPercent}>{result.Milestone.Percent}% {result.EtaLabel}";
    }
}
