using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var unavailable = LiveGrowthBridgeLogic.Analyze(new LiveGrowthBridgeSnapshot(
    false, true, 42, 3, 5, 87, true, 5, 5, 10));
Check(unavailable.State == LiveGrowthBridgeState.Unavailable
      && !unavailable.Available
      && !unavailable.UsesLiveGrowth
      && !unavailable.CanAdopt
      && unavailable.EffectiveGrowthPercent == 42
      && unavailable.PrimeCompleted == 3
      && unavailable.ActionLabel == "LIVE WAITING",
    "offline and stale data must fail closed to manual values");

var ready = LiveGrowthBridgeLogic.Analyze(new LiveGrowthBridgeSnapshot(
    true, false, 25, 2, 5, 36, true, 4, 5, 10));
Check(ready.State == LiveGrowthBridgeState.ReadyToStart
      && ready.Available
      && ready.UsesLiveGrowth
      && ready.CanAdopt
      && ready.EffectiveGrowthPercent == 36
      && ready.PrimeCompleted == 4
      && ready.PrimeRequired == 5
      && ready.ActionLabel == "START @ 36%",
    "fresh live data should offer an explicit start action");

var behind = LiveGrowthBridgeLogic.Analyze(new LiveGrowthBridgeSnapshot(
    true, true, 35, 1, 5, 48, true, 5, 5, 10));
Check(behind.State == LiveGrowthBridgeState.Drifted
      && behind.DriftPercent == 13
      && behind.CanAdopt
      && behind.PrimeReady
      && behind.StateLabel.Contains("13% BEHIND", StringComparison.Ordinal)
      && behind.ActionLabel == "SYNC @ 48%",
    "behind-run drift failed");

var ahead = LiveGrowthBridgeLogic.Analyze(new LiveGrowthBridgeSnapshot(
    true, true, 70, 4, 5, 66, false, 10, 10, 10));
Check(ahead.State == LiveGrowthBridgeState.Drifted
      && ahead.DriftPercent == -4
      && ahead.StateLabel.Contains("4% AHEAD", StringComparison.Ordinal)
      && !ahead.PrimeAvailable
      && ahead.PrimeCompleted == 4
      && ahead.PrimeRequired == 5,
    "ahead drift and manual Prime fallback failed");

var matched = LiveGrowthBridgeLogic.Analyze(new LiveGrowthBridgeSnapshot(
    true, true, 75, 2, 5, 75, true, 5, 5, 10));
Check(matched.State == LiveGrowthBridgeState.Matched
      && !matched.CanAdopt
      && matched.ActionLabel == "MATCHED"
      && matched.EffectiveStageIndex == GrowthPlannerLogic.StageIndex(75),
    "matched state failed");

var malformedPrime = LiveGrowthBridgeLogic.Analyze(new LiveGrowthBridgeSnapshot(
    true, true, 25, 2, 5, 150, true, 9, 8, 4));
Check(malformedPrime.EffectiveGrowthPercent == 100
      && !malformedPrime.PrimeAvailable
      && malformedPrime.PrimeCompleted == 2
      && malformedPrime.PrimeRequired == 5,
    "bounded growth and malformed Prime fallback failed");

Console.WriteLine("Live growth bridge verification passed: fresh authority, explicit adoption, drift, match, stale closure, and Prime fallback");
