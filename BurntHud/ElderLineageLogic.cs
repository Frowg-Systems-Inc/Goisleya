namespace Isley;

internal enum ElderLineageState
{
    NoLife,
    PrimePreparation,
    PrimeVerification,
    PrimeWindow,
    FrailPath,
    Aging,
    ElderVerification,
    EntombReady
}

internal readonly record struct ElderLineageSnapshot(
    bool LifeActive,
    int GrowthPercent,
    int PrimeCount,
    int PrimeRequired,
    int EntombCount,
    bool PrimeConfirmed,
    bool ElderConfirmed,
    int MutationCount,
    int InheritedMutationCount);

internal readonly record struct ElderLineagePresentation(
    ElderLineageSnapshot Snapshot,
    ElderLineageState State,
    string Heading,
    string NextAction,
    string LineageLabel,
    string MutationLabel,
    double Progress,
    bool CanConfirmPrime,
    bool CanConfirmElder,
    bool CanRecordEntomb);

internal static class ElderLineageLogic
{
    internal const int MaximumEntombCount = 15;
    internal const int ReportedMutationBoostCap = 2;

    internal static ElderLineageSnapshot Normalize(ElderLineageSnapshot snapshot)
    {
        var active = snapshot.LifeActive;
        var growth = active ? Math.Clamp(snapshot.GrowthPercent, 0, 100) : 0;
        var primeCount = active ? Math.Clamp(snapshot.PrimeCount, 0, 10) : 0;
        var primeRequired = Math.Clamp(snapshot.PrimeRequired, 1, 10);
        var mutationCount = active
            ? Math.Clamp(snapshot.MutationCount, 0, MutationPlannerLogic.MaxLoadoutSize)
            : 0;
        return snapshot with
        {
            GrowthPercent = growth,
            PrimeCount = primeCount,
            PrimeRequired = primeRequired,
            EntombCount = active ? Math.Clamp(snapshot.EntombCount, 0, MaximumEntombCount) : 0,
            PrimeConfirmed = active
                             && growth >= 75
                             && primeCount >= primeRequired
                             && snapshot.PrimeConfirmed,
            ElderConfirmed = active && growth >= 100 && snapshot.ElderConfirmed,
            MutationCount = mutationCount,
            InheritedMutationCount = active
                ? Math.Clamp(snapshot.InheritedMutationCount, 0, mutationCount)
                : 0
        };
    }

    internal static ElderLineagePresentation Analyze(ElderLineageSnapshot raw)
    {
        var snapshot = Normalize(raw);
        var runNumber = snapshot.EntombCount + 1;
        var lineageLabel = LineageLabel(snapshot.EntombCount);
        var mutationLabel = snapshot.MutationCount == 0
            ? "NO MUTATIONS LOGGED"
            : $"{snapshot.InheritedMutationCount}/{snapshot.MutationCount} INHERITED";
        if (!snapshot.LifeActive)
        {
            return new ElderLineagePresentation(
                snapshot,
                ElderLineageState.NoLife,
                "NO ACTIVE LINEAGE",
                "START LIFE RUN",
                "LINEAGE WAITING",
                "NO MUTATIONS LOGGED",
                0,
                false,
                false,
                false);
        }

        var primeReady = snapshot.PrimeCount >= snapshot.PrimeRequired;
        if (snapshot.GrowthPercent < 75)
        {
            var needed = Math.Max(0, snapshot.PrimeRequired - snapshot.PrimeCount);
            return new ElderLineagePresentation(
                snapshot,
                primeReady ? ElderLineageState.PrimeVerification : ElderLineageState.PrimePreparation,
                $"RUN {runNumber} · {snapshot.GrowthPercent}% · " +
                (primeReady ? "PRIME PLAN READY" : $"PRIME {snapshot.PrimeCount}/{snapshot.PrimeRequired}"),
                primeReady
                    ? "REACH 75% · VERIFY THE FOURTH SLOT"
                    : $"COMPLETE {needed} PRIME CONDITION{(needed == 1 ? string.Empty : "S")} BEFORE 75%",
                lineageLabel,
                mutationLabel,
                snapshot.GrowthPercent / 100d,
                false,
                false,
                false);
        }

        if (snapshot.GrowthPercent < 87)
        {
            if (primeReady && !snapshot.PrimeConfirmed)
            {
                return new ElderLineagePresentation(
                    snapshot,
                    ElderLineageState.PrimeVerification,
                    $"{snapshot.GrowthPercent}% · PRIME CHECK",
                    "TRY THE FOURTH MUTATION SLOT IN GAME",
                    lineageLabel,
                    mutationLabel,
                    snapshot.GrowthPercent / 100d,
                    true,
                    false,
                    false);
            }

            return new ElderLineagePresentation(
                snapshot,
                snapshot.PrimeConfirmed ? ElderLineageState.PrimeWindow : ElderLineageState.FrailPath,
                snapshot.PrimeConfirmed
                    ? $"PRIME WINDOW · {snapshot.GrowthPercent}%"
                    : $"FRAIL PATH · {snapshot.GrowthPercent}%",
                snapshot.PrimeConfirmed
                    ? "BUILD TOWARD THE REPORTED 87% PEAK"
                    : "GROW TO 100% · PLAN THE NEXT LINEAGE",
                lineageLabel,
                mutationLabel,
                snapshot.GrowthPercent / 100d,
                primeReady,
                false,
                false);
        }

        if (snapshot.GrowthPercent < 100)
        {
            return new ElderLineagePresentation(
                snapshot,
                ElderLineageState.Aging,
                $"{(snapshot.PrimeConfirmed ? "PRIME" : "FRAIL")} AGING · {snapshot.GrowthPercent}%",
                snapshot.PrimeConfirmed ? "PROTECT THE PRIME LINEAGE TO 100%" : "REACH 100% · VERIFY ELDER IN GAME",
                lineageLabel,
                mutationLabel,
                snapshot.GrowthPercent / 100d,
                primeReady,
                false,
                false);
        }

        if (!snapshot.ElderConfirmed)
        {
            return new ElderLineagePresentation(
                snapshot,
                ElderLineageState.ElderVerification,
                snapshot.PrimeConfirmed ? "100% · PRIME ELDER CHECK" : "100% · ELDER CHECK",
                "VERIFY ENTOMB IS AVAILABLE IN GAME",
                lineageLabel,
                mutationLabel,
                1,
                primeReady,
                true,
                false);
        }

        var ledgerAtCap = snapshot.EntombCount >= MaximumEntombCount;
        return new ElderLineagePresentation(
            snapshot,
            ElderLineageState.EntombReady,
            $"ENTOMB READY · RUN {runNumber}",
            ledgerAtCap
                ? "LINEAGE LEDGER AT CAP · KEEP THE CURRENT ELDER"
                : "RECORD ONLY AFTER THE IN-GAME ENTOMB COMPLETES",
            lineageLabel,
            mutationLabel,
            1,
            primeReady,
            true,
            !ledgerAtCap);
    }

    internal static string LineageLabel(int entombCount)
    {
        var normalized = Math.Clamp(entombCount, 0, MaximumEntombCount);
        var runNumber = normalized + 1;
        if (normalized == 0)
        {
            return $"LINEAGE {runNumber} · FRESH";
        }
        var boostTier = Math.Min(normalized, ReportedMutationBoostCap);
        return normalized >= ReportedMutationBoostCap
            ? $"LINEAGE {runNumber} · BOOST CAP REPORTED"
            : $"LINEAGE {runNumber} · BOOST {boostTier}/{ReportedMutationBoostCap}";
    }

    internal static int AdjustEntombCount(int current, int delta) =>
        Math.Clamp(current + Math.Clamp(delta, -1, 1), 0, MaximumEntombCount);

    internal static int CarryForwardMutationStatus(int status) => status switch
    {
        1 or 2 => 2,
        _ => 0
    };

    internal static string CompactSummary(ElderLineagePresentation presentation) =>
        presentation.Snapshot.LifeActive
            ? $"LINEAGE {presentation.Snapshot.EntombCount + 1} · ENT {presentation.Snapshot.EntombCount}" +
              (presentation.Snapshot.InheritedMutationCount > 0
                  ? $" · CARRY {presentation.Snapshot.InheritedMutationCount}"
                  : string.Empty)
            : string.Empty;
}
