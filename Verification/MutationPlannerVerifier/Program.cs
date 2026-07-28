using Isley;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Require(MutationPlannerLogic.Catalog.Length == 41, "Current mutation catalog count failed");
Require(MutationPlannerLogic.Catalog.Select(entry => entry.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 41,
    "Mutation catalog id uniqueness failed");
Require(MutationPlannerLogic.Catalog.Select(entry => entry.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 41,
    "Mutation catalog name uniqueness failed");
Require(MutationPlannerLogic.FindById("traumatic-thrombosis") is null,
    "Temporarily removed Traumatic Thrombosis must not remain selectable");
Require(MutationPlannerLogic.Search("e", 6).Count == 0, "Short-query guard failed");
Require(MutationPlannerLogic.Search("efficient digestion", 6).First().Id == "efficient-digestion",
    "Exact mutation search failed");
Require(MutationPlannerLogic.Search("aquatic", 6).Any(entry => entry.Id == "hydrodynamic"),
    "Play-style tag search failed");
Require(MutationPlannerLogic.Search("nesting", 6).Any(entry => entry.Id == "advanced-gestation"),
    "Nesting tag search failed");
Require(MutationPlannerLogic.Search("jump 50", 6).First().Id == "reinforced-tendons",
    "Unlock-task search failed");
Require(MutationPlannerLogic.Search("water", 3).Count == 3, "Search limit failed");

var tactile = MutationPlannerLogic.FindById("tactile-endurance")!;
var cannibal = MutationPlannerLogic.FindById("cannibalistic")!;
var hydrodynamic = MutationPlannerLogic.FindById("hydrodynamic")!;
Require(MutationPlannerLogic.AllowedSlots(tactile).SequenceEqual([2]),
    "Slot-2-exclusive allocation failed");
Require(MutationPlannerLogic.AllowedSlots(cannibal).SequenceEqual([2, 4]),
    "Slots 2/4 allocation failed");
Require(MutationPlannerLogic.AllowedSlots(hydrodynamic).Count == MutationPlannerLogic.MaxLoadoutSize,
    "Unrestricted slot allocation failed");
Require(MutationPlannerLogic.NextFreeSlotForMutation(
            [new MutationLoadoutItem(2, "efficient-digestion", 0)], cannibal) == 4,
    "Next restriction-aware slot failed");
Require(MutationPlannerLogic.NextFreeSlotForMutation(
            [new MutationLoadoutItem(2, "efficient-digestion", 0)], tactile) == 0,
    "Occupied exclusive-slot guard failed");

var raw = new[]
{
    new MutationLoadoutItem(1, "efficient-digestion", 7),
    new MutationLoadoutItem(1, "hydrodynamic", 1),
    new MutationLoadoutItem(2, "efficient-digestion", 1),
    new MutationLoadoutItem(3, "not-a-mutation", 1),
    new MutationLoadoutItem(4, "tactile-endurance", 1),
    new MutationLoadoutItem(16, "hydrodynamic", -4),
    new MutationLoadoutItem(17, "wader", 1)
};
var normalized = MutationPlannerLogic.NormalizeLoadout(raw);
Require(normalized.Count == 2, "Loadout validation failed");
Require(normalized[0] == new MutationLoadoutItem(1, "efficient-digestion", 2), "Status upper clamp failed");
Require(normalized[1] == new MutationLoadoutItem(16, "hydrodynamic", 0), "Status lower clamp failed");
Require(MutationPlannerLogic.NextFreeSlot(normalized) == 2, "Next-free-slot selection failed");
Require(MutationPlannerLogic.EquippedCount(normalized) == 1, "Equipped count failed");
Require(MutationPlannerLogic.StatusLabel(0) == "PLANNED"
        && MutationPlannerLogic.StatusLabel(1) == "ACTIVE"
        && MutationPlannerLogic.StatusLabel(2) == "CARRIED",
    "Loadout status labels failed");

var full = MutationPlannerLogic.Catalog.Take(16)
    .Select((entry, index) => new MutationLoadoutItem(index + 1, entry.Id, 1))
    .ToArray();
Require(MutationPlannerLogic.NextFreeSlot(full) == 0, "Full-loadout guard failed");
Require(MutationPlannerLogic.EquippedCount(full) == 16, "Full equipped count failed");

Require(MutationBuildLogic.Focuses.Length == 8, "Build-focus catalog failed");
Require(MutationBuildLogic.CycleFocusIndex(0, -1) == 7
        && MutationBuildLogic.CycleFocusIndex(7, 1) == 0,
    "Build-focus wrap failed");
Require(MutationBuildLogic.IsDietCompatible(
            MutationPlannerLogic.FindById("accelerated-prey-drive")!, "Carnivore")
        && !MutationBuildLogic.IsDietCompatible(
            MutationPlannerLogic.FindById("accelerated-prey-drive")!, "Herbivore")
        && MutationBuildLogic.IsDietCompatible(
            MutationPlannerLogic.FindById("social-behavior")!, "Omnivore"),
    "Diet-class recommendation boundary failed");

var emptySurvival = MutationBuildLogic.Analyze(1, [], "Carnivore");
Require(emptySurvival.Focus.Id == "survival"
        && emptySurvival.HasRecommendation
        && emptySurvival.RecommendationSlot > 0
        && emptySurvival.FitPercent == 0,
    "Empty-build recommendation failed");
var aquaticBuild = MutationBuildLogic.Analyze(4,
    [new MutationLoadoutItem(1, "hydrodynamic", 1)], "Carnivore");
Require(aquaticBuild.RecommendationId == "increased-inspiratory-capacity"
        && aquaticBuild.RecommendationReason.Contains("SWIM SPEED + DIVE TIME", StringComparison.Ordinal),
    "Aquatic synergy completion failed");
var pairedAquaticBuild = MutationBuildLogic.Analyze(4,
    [
        new MutationLoadoutItem(1, "hydrodynamic", 1),
        new MutationLoadoutItem(2, "increased-inspiratory-capacity", 1)
    ], "Carnivore");
Require(pairedAquaticBuild.SynergyLabel == "SWIM SPEED + DIVE TIME"
        && pairedAquaticBuild.Insight.StartsWith("PAIR", StringComparison.Ordinal)
        && pairedAquaticBuild.RolePercent > 0,
    "Loaded synergy detection failed");
Require(MutationBuildLogic.CompactSummary(pairedAquaticBuild).StartsWith("BUILD AQUA", StringComparison.Ordinal)
        && !MutationBuildLogic.CompactSummary(pairedAquaticBuild).Contains("Carnivore", StringComparison.OrdinalIgnoreCase),
    "Anonymous compact build summary failed");

Console.WriteLine("Mutation planner: PASS (41-entry public catalog, restriction-aware slots, build focus, synergies, recommendations, validation, and states)");
