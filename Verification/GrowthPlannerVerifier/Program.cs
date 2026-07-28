using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Check(GrowthPlannerLogic.SpeciesTimings.Length == 21, "current species catalog");
Check(GrowthPlannerLogic.SpeciesTimings.Select(item => item.Id).Distinct().Count() == 21,
    "unique species ids");
Check(GrowthPlannerLogic.SpeciesTimings.All(item => item.BaseHours > 0), "positive base times");
Check(GrowthPlannerLogic.SpeciesTimings.Count(item => item.Approximate) == 2,
    "approximate timing disclosure");
Check(GrowthPlannerLogic.ServerMultipliers.SequenceEqual(new[] { 1d, 1.5d, 2d, 3d, 5d }),
    "server multiplier presets");
Check(GrowthPlannerLogic.StageIndex(10) == 0 && GrowthPlannerLogic.StageIndex(25) == 1
      && GrowthPlannerLogic.StageIndex(50) == 2 && GrowthPlannerLogic.StageIndex(75) == 3
      && GrowthPlannerLogic.StageIndex(100) == 4,
    "growth stage mapping");
Check(GrowthPlannerLogic.StageAnchor(0) == 10 && GrowthPlannerLogic.StageAnchor(4) == 100,
    "stage anchors");
Check(GrowthPlannerLogic.NextMilestone(25).Percent == 50
      && GrowthPlannerLogic.NextMilestone(50).Percent == 75
      && GrowthPlannerLogic.NextMilestone(75).Percent == 87
      && GrowthPlannerLogic.NextMilestone(87).Percent == 100,
    "lifecycle milestone sequence");

var estimate = GrowthPlannerLogic.Analyze(new GrowthPlannerSnapshot(
    1, 25, GrowthPlannerLogic.DefaultLiveMapMultiplierIndex, 3, false, 4, 5));
Check(estimate.Species?.Id == "allosaurus", "species lookup");
Check(Math.Abs(estimate.ServerMultiplier - 1) < 0.001 && estimate.DietMultiplier == 3,
    "effective rates");
Check(Math.Abs(estimate.EstimatedMinutes!.Value - 50) < 0.001, "next-gate estimate");
Check(estimate.EtaLabel == "~50M", "eta formatting");
Check(estimate.Advice.Contains("food and water", StringComparison.OrdinalIgnoreCase),
    "survival-floor advice");
Check(GrowthPlannerLogic.CompactSummary(estimate) == "GROW 25>50% ~50M", "compact summary");

var primeDeadline = GrowthPlannerLogic.Analyze(new GrowthPlannerSnapshot(1, 60, 2, 3, false, 3, 5));
Check(primeDeadline.Advice.Contains("2 more", StringComparison.OrdinalIgnoreCase),
    "Prime deadline advice");
var primeReady = GrowthPlannerLogic.Analyze(new GrowthPlannerSnapshot(1, 60, 2, 3, false, 5, 5));
Check(primeReady.Advice.Contains("fourth mutation slot", StringComparison.OrdinalIgnoreCase),
    "Prime verification advice");
var paused = GrowthPlannerLogic.Analyze(new GrowthPlannerSnapshot(1, 60, 2, 3, true, 5, 5));
Check(paused.EstimatedMinutes is null && paused.EtaLabel == "PAUSED", "paused estimate");
var missingDiet = GrowthPlannerLogic.Analyze(new GrowthPlannerSnapshot(1, 60, 2, 0, false, 5, 5));
Check(missingDiet.EstimatedMinutes is null && missingDiet.EtaLabel == "LOG DIET",
    "missing diet honesty");
var elder = GrowthPlannerLogic.Analyze(new GrowthPlannerSnapshot(1, 100, 2, 3, false, 5, 5));
Check(elder.EtaLabel == "AT ELDER" && GrowthPlannerLogic.StageIndex(100) == 4,
    "Elder completion");

Console.WriteLine("Growth planner verification passed (21 species, lifecycle gates, rates, ETA, pause, and Prime advice).");
