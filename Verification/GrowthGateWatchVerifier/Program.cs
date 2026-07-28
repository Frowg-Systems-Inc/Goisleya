using Isley;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var started = new DateTimeOffset(2026, 7, 22, 13, 0, 0, TimeSpan.Zero);
LiveGrowthGateSample Sample(int growth, int seconds = 0, string species = "carnotaurus") =>
    new(species, growth, started.AddSeconds(seconds));

Require(!GrowthGateWatchLogic.Analyze(null, Sample(49)).Detected,
    "The first fresh sample must establish a baseline");

var mutation = GrowthGateWatchLogic.Analyze(Sample(49), Sample(50, 30));
Require(mutation.Detected
        && mutation.Kind == GrowthGateKind.Mutation
        && mutation.GatePercent == 50
        && mutation.ActionId == "mutation-planner",
    "The 50 percent mutation gate failed");

var prime = GrowthGateWatchLogic.Analyze(Sample(74), Sample(75, 30));
Require(prime.Detected
        && prime.Kind == GrowthGateKind.PrimeWindow
        && prime.Heading == "PRIME WINDOW OPEN"
        && prime.ActionId == "prime-planner"
        && prime.Detail.Contains("Verify Prime", StringComparison.Ordinal),
    "The 75 percent Prime-window gate failed");

var peak = GrowthGateWatchLogic.Analyze(Sample(86), Sample(87, 30));
Require(peak.Detected
        && peak.Kind == GrowthGateKind.PrimePeak
        && peak.GatePercent == 87
        && peak.Detail.Contains("community guidance", StringComparison.OrdinalIgnoreCase),
    "The 87 percent update-sensitive Prime-peak gate failed");

var elder = GrowthGateWatchLogic.Analyze(Sample(99), Sample(100, 30));
Require(elder.Detected
        && elder.Kind == GrowthGateKind.Elder
        && elder.ActionId == "elder-lineage"
        && elder.Detail.Contains("Verify Elder", StringComparison.Ordinal),
    "The 100 percent Elder gate failed");

var multiGate = GrowthGateWatchLogic.Analyze(Sample(49), Sample(88, 30));
Require(multiGate.GatePercent == 87 && multiGate.Kind == GrowthGateKind.PrimePeak,
    "A skipped refresh must surface only the highest newly crossed gate");

Require(!GrowthGateWatchLogic.Analyze(Sample(75), Sample(75, 30)).Detected,
    "An unchanged display must not repeat a gate");
Require(!GrowthGateWatchLogic.Analyze(Sample(76), Sample(74, 30)).Detected,
    "A downward reset must not masquerade as a growth gate");
Require(!GrowthGateWatchLogic.Analyze(Sample(49), Sample(50, 30, "dilophosaurus")).Detected,
    "A species transition must not create a growth-gate signal");
Require(!GrowthGateWatchLogic.Analyze(Sample(49), Sample(50, 4)).Detected,
    "Duplicate-fast samples must fail closed");
Require(!GrowthGateWatchLogic.Analyze(Sample(49), Sample(50, 181)).Detected,
    "Disconnected samples must fail closed");
Require(!GrowthGateWatchLogic.Analyze(
        Sample(49),
        Sample(50, 30, "futuremoddedplayable")).Detected,
    "Unknown species must fail closed");
Require(GrowthGateWatchLogic.Gates.SequenceEqual([50, 75, 87, 100])
        && new[] { mutation, prime, peak, elder }.All(result =>
            result.ActionId is "mutation-planner" or "prime-planner" or "elder-lineage")
        && new[] { mutation, prime, peak, elder }.All(result =>
            !result.Detail.Contains("automatic", StringComparison.OrdinalIgnoreCase)),
    "The fixed gate and explicit-action boundary failed");

Console.WriteLine("Growth Gate Watch verification passed: four live gates, highest-crossed selection, same-species continuity, freshness bounds, and explicit in-game verification");
