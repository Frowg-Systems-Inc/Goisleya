namespace Isley;

internal readonly record struct LifeRunSnapshot(
    int StageIndex,
    bool SanctuaryVisited,
    bool PerfectDiet,
    bool NestedIn,
    bool RaisedYoung,
    int MigrationVisits,
    int PatrolVisits,
    bool MassMigrationVisited,
    int FertilityStatus,
    int SpasmStatus,
    int SpeciesClass);

internal readonly record struct LifeRunCaptureStreak(int Current, int Best);

internal static class LifeRunLogic
{
    internal const int MaximumCaptureStreak = 9999;

    internal static LifeRunCaptureStreak NormalizeCaptureStreak(LifeRunCaptureStreak streak)
    {
        var current = Math.Clamp(streak.Current, 0, MaximumCaptureStreak);
        var best = Math.Clamp(Math.Max(streak.Best, current), 0, MaximumCaptureStreak);
        return new LifeRunCaptureStreak(current, best);
    }

    internal static LifeRunCaptureStreak RecordCaptureSuccess(LifeRunCaptureStreak streak, int successes = 1)
    {
        var normalized = NormalizeCaptureStreak(streak);
        var current = Math.Clamp(
            normalized.Current + Math.Clamp(successes, 1, 100),
            0,
            MaximumCaptureStreak);
        return new LifeRunCaptureStreak(current, Math.Max(normalized.Best, current));
    }

    internal static LifeRunCaptureStreak RecordCaptureFailure(LifeRunCaptureStreak streak)
    {
        var normalized = NormalizeCaptureStreak(streak);
        return new LifeRunCaptureStreak(0, normalized.Best);
    }

    internal static string CaptureStreakLabel(LifeRunCaptureStreak streak)
    {
        var normalized = NormalizeCaptureStreak(streak);
        return normalized.Best <= 0
            ? string.Empty
            : $"STREAK {normalized.Current} · BEST {normalized.Best}";
    }

    internal static int TrackedMilestoneCount(LifeRunSnapshot run) =>
        (run.SanctuaryVisited ? 1 : 0) +
        (run.PerfectDiet ? 1 : 0) +
        (run.NestedIn ? 1 : 0) +
        (run.RaisedYoung ? 1 : 0) +
        (run.MigrationVisits > 0 ? 1 : 0) +
        (run.PatrolVisits > 0 ? 1 : 0);

    internal static string NextObjective(LifeRunSnapshot run)
    {
        if (run.StageIndex <= 1 && !run.SanctuaryVisited) return "SANCTUARY";
        if (!run.PerfectDiet) return "PERFECT DIET";
        if (run.MigrationVisits < 2) return $"MIGRATION {run.MigrationVisits}/2";
        if (run.PatrolVisits < 4) return $"PATROL {run.PatrolVisits}/4";
        if (!run.NestedIn) return "NESTING";
        if (!run.RaisedYoung) return "RAISE YOUNG";
        if (!run.SanctuaryVisited) return "LOG SANCTUARY";
        return "ALL TRACKED";
    }

    internal static int PrimeConditionCount(LifeRunSnapshot run) =>
        (run.SanctuaryVisited ? 1 : 0) +
        (run.NestedIn ? 1 : 0) +
        (run.PerfectDiet ? 1 : 0) +
        (run.MassMigrationVisited ? 1 : 0) +
        (run.MigrationVisits >= 2 ? 1 : 0) +
        (run.PatrolVisits >= 4 ? 1 : 0) +
        (run.FertilityStatus == 1 ? 1 : 0) +
        (run.SpasmStatus == 1 ? 1 : 0) +
        (run.RaisedYoung ? 1 : 0) +
        (run.SpeciesClass == 1 ? 1 : 0);

    internal static int PrimeRequiredConditionCount(LifeRunSnapshot run) =>
        run.SpeciesClass == 1 ? 4 : 5;

    internal static string PrimeNextObjective(LifeRunSnapshot run)
    {
        var completed = PrimeConditionCount(run);
        if (completed >= PrimeRequiredConditionCount(run)) return "VERIFY 4TH SLOT AT 75%";
        if (run.StageIndex <= 1 && !run.SanctuaryVisited) return "SANCTUARY AS JUVENILE";
        if (!run.PerfectDiet) return "PERFECT DIET";
        if (!run.MassMigrationVisited) return "MASS MIGRATION";
        if (run.MigrationVisits < 2) return $"MIGRATION {run.MigrationVisits}/2";
        if (run.PatrolVisits < 4) return $"PATROL {run.PatrolVisits}/4";
        if (!run.NestedIn) return "GET NESTED IN";
        if (!run.RaisedYoung) return "RAISE YOUNG";
        if (run.FertilityStatus == 0) return "CHECK INFERTILITY";
        if (run.SpasmStatus == 0) return "CHECK MUSCLE SPASMS";
        if (run.SpeciesClass == 0) return "SET SPECIES CLASS";
        return "NEED ANOTHER CONDITION";
    }

    internal static string FormatElapsed(TimeSpan elapsed, bool compact)
    {
        elapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        if (elapsed.TotalDays >= 1)
        {
            return compact
                ? $"{(int)elapsed.TotalDays}D {elapsed.Hours}H"
                : $"{(int)elapsed.TotalDays}d {elapsed.Hours}h";
        }
        if (elapsed.TotalHours >= 1)
        {
            return compact
                ? $"{(int)elapsed.TotalHours}H {elapsed.Minutes:00}M"
                : $"{(int)elapsed.TotalHours}h {elapsed.Minutes:00}m";
        }
        return $"{Math.Max(0, (int)elapsed.TotalMinutes)}{(compact ? "M" : "m")}";
    }
}
