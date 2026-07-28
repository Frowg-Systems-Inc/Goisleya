using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var unavailable = LiveSpeciesBridgeLogic.Analyze(new LiveSpeciesBridgeSnapshot(
    false, true, 3, "carnotaurus"));
Check(unavailable.State == LiveSpeciesBridgeState.Unavailable
      && !unavailable.Available
      && !unavailable.CanAdopt
      && unavailable.EffectiveSpeciesIndex == 3,
    "stale source must fail closed to the saved species");

var unknown = LiveSpeciesBridgeLogic.Analyze(new LiveSpeciesBridgeSnapshot(
    true, true, 2, "futuremoddedplayable"));
Check(unknown.State == LiveSpeciesBridgeState.Unavailable
      && unknown.EffectiveSpeciesIndex == 2
      && LiveSpeciesBridgeLogic.DisplayName("futuremoddedplayable") == string.Empty,
    "unknown source species must be refused");

var ready = LiveSpeciesBridgeLogic.Analyze(new LiveSpeciesBridgeSnapshot(
    true, false, 0, "kentrosaurus"));
Check(ready.State == LiveSpeciesBridgeState.ReadyToStart
      && ready.Available
      && ready.CanAdopt
      && ready.LiveSpeciesName == "Kentrosaurus"
      && ready.EffectiveSpeciesIndex == LiveSpeciesBridgeLogic.SpeciesIndex("kentrosaurus")
      && ready.ActionLabel == "USE KENTROSAURUS",
    "fresh recognized species should be ready for explicit start adoption");

var drifted = LiveSpeciesBridgeLogic.Analyze(new LiveSpeciesBridgeSnapshot(
    true, true, 1, "carnotaurus"));
Check(drifted.State == LiveSpeciesBridgeState.Drifted
      && drifted.Available
      && drifted.CanAdopt
      && drifted.LiveSpeciesIndex != drifted.SavedSpeciesIndex
      && drifted.StateLabel.Contains("RUN DIFFERS", StringComparison.Ordinal),
    "active-run species drift failed");

var carnotaurusIndex = LiveSpeciesBridgeLogic.SpeciesIndex("carnotaurus");
var matched = LiveSpeciesBridgeLogic.Analyze(new LiveSpeciesBridgeSnapshot(
    true, true, carnotaurusIndex, "CARNOTAURUS"));
Check(matched.State == LiveSpeciesBridgeState.Matched
      && matched.Available
      && !matched.CanAdopt
      && matched.ActionLabel == "LIVE MATCHED"
      && matched.LiveSpeciesId == "carnotaurus",
    "matched source failed");

Check(LiveSpeciesBridgeLogic.SpeciesIndex("carnotaurus<script>") == 0
      && LiveSpeciesBridgeLogic.SpeciesIndex(new string('a', 33)) == 0
      && LiveSpeciesBridgeLogic.SpeciesIndex("  Carnotaurus  ") == carnotaurusIndex,
    "identifier bounds failed");

Console.WriteLine("Live species bridge verification passed: exact allowlist, effective guidance, explicit adoption, match, drift, and stale closure");
