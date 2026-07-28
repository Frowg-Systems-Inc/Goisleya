using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var inactive = ElderLineageLogic.Analyze(new ElderLineageSnapshot(
    false, 100, 10, 5, 99, true, true, 20, 20));
Check(inactive.State == ElderLineageState.NoLife && inactive.Snapshot.EntombCount == 0,
    "inactive lineage normalization");

var primePrep = ElderLineageLogic.Analyze(new ElderLineageSnapshot(
    true, 60, 3, 5, 0, false, false, 2, 0));
Check(primePrep.State == ElderLineageState.PrimePreparation
      && primePrep.NextAction.Contains("2 PRIME CONDITIONS", StringComparison.Ordinal),
    "Prime preparation guidance");

var primeReady = ElderLineageLogic.Analyze(new ElderLineageSnapshot(
    true, 60, 5, 5, 0, false, false, 2, 0));
Check(primeReady.State == ElderLineageState.PrimeVerification
      && primeReady.NextAction.Contains("FOURTH SLOT", StringComparison.Ordinal),
    "Prime verification guidance");

var primeCheck = ElderLineageLogic.Analyze(new ElderLineageSnapshot(
    true, 80, 5, 5, 1, false, false, 5, 3));
Check(primeCheck.State == ElderLineageState.PrimeVerification
      && primeCheck.CanConfirmPrime
      && primeCheck.NextAction.Contains("FOURTH MUTATION SLOT", StringComparison.Ordinal),
    "manual Prime verification gate");

var primeWindow = ElderLineageLogic.Analyze(new ElderLineageSnapshot(
    true, 80, 5, 5, 1, true, false, 5, 3));
Check(primeWindow.State == ElderLineageState.PrimeWindow
      && primeWindow.Heading == "PRIME WINDOW · 80%"
      && primeWindow.MutationLabel == "3/5 INHERITED",
    "Prime window and inherited-mutation summary");

var frail = ElderLineageLogic.Analyze(new ElderLineageSnapshot(
    true, 80, 2, 5, 0, false, false, 0, 0));
Check(frail.State == ElderLineageState.FrailPath
      && frail.NextAction.Contains("NEXT LINEAGE", StringComparison.Ordinal),
    "Frail path honesty");

var aging = ElderLineageLogic.Analyze(new ElderLineageSnapshot(
    true, 92, 5, 5, 2, true, false, 6, 5));
Check(aging.State == ElderLineageState.Aging
      && aging.Heading == "PRIME AGING · 92%"
      && aging.LineageLabel.Contains("BOOST CAP REPORTED", StringComparison.Ordinal),
    "aging and reported boost cap");

var elderCheck = ElderLineageLogic.Analyze(new ElderLineageSnapshot(
    true, 100, 5, 5, 2, true, false, 6, 5));
Check(elderCheck.State == ElderLineageState.ElderVerification
      && elderCheck.CanConfirmElder
      && !elderCheck.CanRecordEntomb,
    "in-game Elder verification gate");

var ready = ElderLineageLogic.Analyze(new ElderLineageSnapshot(
    true, 100, 5, 5, 2, true, true, 6, 5));
Check(ready.State == ElderLineageState.EntombReady
      && ready.CanRecordEntomb
      && ElderLineageLogic.CompactSummary(ready) == "LINEAGE 3 · ENT 2 · CARRY 5",
    "Entomb-ready state and compact summary");

Check(ElderLineageLogic.AdjustEntombCount(0, -1) == 0
      && ElderLineageLogic.AdjustEntombCount(15, 1) == 15
      && ElderLineageLogic.AdjustEntombCount(4, 1) == 5,
    "lineage correction bounds");
Check(ElderLineageLogic.CarryForwardMutationStatus(0) == 0
      && ElderLineageLogic.CarryForwardMutationStatus(1) == 2
      && ElderLineageLogic.CarryForwardMutationStatus(2) == 2,
    "mutation carry-forward states");
var capped = ElderLineageLogic.Analyze(new ElderLineageSnapshot(
    true, 100, 5, 5, ElderLineageLogic.MaximumEntombCount, true, true, 16, 16));
Check(capped.State == ElderLineageState.EntombReady
      && !capped.CanRecordEntomb
      && capped.NextAction.Contains("LEDGER AT CAP", StringComparison.Ordinal),
    "lineage ledger cap");
Check(!ElderLineageLogic.CompactSummary(ready).Contains("Allosaurus", StringComparison.OrdinalIgnoreCase),
    "compact lineage summary must remain identity-free");

Console.WriteLine("Elder lineage verification passed (manual Prime check, Prime window, Elder gate, Entomb readiness, carry-forward, caps, and privacy)." );
