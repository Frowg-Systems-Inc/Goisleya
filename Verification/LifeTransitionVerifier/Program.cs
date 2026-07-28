using Isley;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var started = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
var carno45 = new LiveDinoSample("carnotaurus", 45, started);

Require(!LifeTransitionLogic.Analyze(null, carno45).Detected,
    "A first sample must establish a baseline without signaling");

var speciesSwitch = LifeTransitionLogic.Analyze(
    carno45,
    new LiveDinoSample("dilophosaurus", 45, started.AddSeconds(30)));
Require(speciesSwitch.Detected
        && speciesSwitch.Reason == LifeTransitionReason.SpeciesChanged
        && speciesSwitch.Heading == "LIVE DINOSAUR CHANGED"
        && speciesSwitch.Detail.Contains("Carnotaurus", StringComparison.Ordinal)
        && speciesSwitch.Detail.Contains("Dilophosaurus", StringComparison.Ordinal),
    "A fresh recognized species change must create a neutral player-review signal");

var speciesAndGrowth = LifeTransitionLogic.Analyze(
    carno45,
    new LiveDinoSample("dilophosaurus", 8, started.AddSeconds(30)));
Require(speciesAndGrowth.Detected
        && speciesAndGrowth.Reason == LifeTransitionReason.SpeciesAndGrowth
        && speciesAndGrowth.DropPercent == 37,
    "A species change with a large growth drop must preserve both facts");

var sameSpeciesReset = LifeTransitionLogic.Analyze(
    carno45,
    new LiveDinoSample("CARNOTAURUS", 42, started.AddSeconds(30)));
Require(sameSpeciesReset.Detected
        && sameSpeciesReset.Reason == LifeTransitionReason.GrowthReset
        && sameSpeciesReset.Detail.Contains("45% to 42%", StringComparison.Ordinal),
    "A same-species drop at the conservative threshold must signal");

Require(!LifeTransitionLogic.Analyze(
        carno45,
        new LiveDinoSample("carnotaurus", 43, started.AddSeconds(30))).Detected,
    "Two-point display jitter must not signal");
Require(!LifeTransitionLogic.Analyze(
        carno45,
        new LiveDinoSample("carnotaurus", 50, started.AddSeconds(30))).Detected,
    "Normal growth must not signal");
Require(!LifeTransitionLogic.Analyze(
        carno45,
        new LiveDinoSample("dilophosaurus", 5, started.AddSeconds(4))).Detected,
    "Duplicate-fast samples must not signal");
Require(!LifeTransitionLogic.Analyze(
        carno45,
        new LiveDinoSample("dilophosaurus", 5, started.AddSeconds(181))).Detected,
    "A long disconnected gap must establish a new baseline instead of signaling");
Require(!LifeTransitionLogic.Analyze(
        carno45,
        new LiveDinoSample("futuremoddedplayable", 5, started.AddSeconds(30))).Detected,
    "Unknown species must fail closed");
Require(!LifeTransitionLogic.Analyze(
        new LiveDinoSample("carnotaurus", 101, started),
        new LiveDinoSample("dilophosaurus", 5, started.AddSeconds(30))).Detected,
    "Out-of-range growth must fail closed");
Require(!speciesSwitch.Key.Contains('<')
        && !speciesSwitch.Key.Contains('>')
        && Enum.GetNames<LifeTransitionReason>().All(name =>
            !name.Contains("death", StringComparison.OrdinalIgnoreCase)),
    "The detector must carry bounded identifiers and never infer death");

Console.WriteLine("Life transition verification passed: consecutive-live gating, recognized species, jitter restraint, gap closure, and no death inference");
