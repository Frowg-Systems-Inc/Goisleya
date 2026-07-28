using Isley;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var juvenile = new LifeRunSnapshot(1, false, false, false, false, 0, 0, false, 0, 0, 0);
Require(LifeRunLogic.TrackedMilestoneCount(juvenile) == 0, "Empty run count failed");
Require(LifeRunLogic.NextObjective(juvenile) == "SANCTUARY", "Juvenile Sanctuary priority failed");
Require(LifeRunLogic.PrimeConditionCount(juvenile) == 0, "Empty Prime count failed");
Require(LifeRunLogic.PrimeRequiredConditionCount(juvenile) == 5, "Default Prime threshold failed");
Require(LifeRunLogic.PrimeNextObjective(juvenile) == "SANCTUARY AS JUVENILE", "Prime Sanctuary priority failed");

var subadult = juvenile with { StageIndex = 2 };
Require(LifeRunLogic.NextObjective(subadult) == "PERFECT DIET", "Subadult safety priority failed");
var diet = subadult with { PerfectDiet = true };
Require(LifeRunLogic.NextObjective(diet) == "MIGRATION 0/2", "Migration priority failed");
var migration = diet with { MigrationVisits = 2 };
Require(LifeRunLogic.NextObjective(migration) == "PATROL 0/4", "Patrol priority failed");
var patrol = migration with { PatrolVisits = 4 };
Require(LifeRunLogic.NextObjective(patrol) == "NESTING", "Nesting priority failed");
var nested = patrol with { NestedIn = true };
Require(LifeRunLogic.NextObjective(nested) == "RAISE YOUNG", "Raise-young priority failed");
var raisedWithoutSanctuary = nested with { RaisedYoung = true };
Require(LifeRunLogic.NextObjective(raisedWithoutSanctuary) == "LOG SANCTUARY", "Late Sanctuary log failed");
var complete = raisedWithoutSanctuary with { SanctuaryVisited = true };
Require(LifeRunLogic.TrackedMilestoneCount(complete) == 6, "Complete run count failed");
Require(LifeRunLogic.NextObjective(complete) == "ALL TRACKED", "Complete run objective failed");

var primePath = juvenile with
{
    StageIndex = 2,
    SanctuaryVisited = true,
    PerfectDiet = true,
    MassMigrationVisited = true
};
Require(LifeRunLogic.PrimeConditionCount(primePath) == 3, "Prime active-condition count failed");
Require(LifeRunLogic.PrimeNextObjective(primePath) == "MIGRATION 0/2", "Prime migration target failed");
primePath = primePath with { MigrationVisits = 2 };
Require(LifeRunLogic.PrimeConditionCount(primePath) == 4, "Prime migration threshold failed");
Require(LifeRunLogic.PrimeNextObjective(primePath) == "PATROL 0/4", "Prime patrol target failed");
primePath = primePath with { PatrolVisits = 4 };
Require(LifeRunLogic.PrimeConditionCount(primePath) == 5, "Prime patrol threshold failed");
Require(LifeRunLogic.PrimeNextObjective(primePath) == "VERIFY 4TH SLOT AT 75%", "Prime readiness failed");

var passiveStates = juvenile with { StageIndex = 2, FertilityStatus = 1, SpasmStatus = 2 };
Require(LifeRunLogic.PrimeConditionCount(passiveStates) == 1, "Prime tri-state credit failed");
passiveStates = passiveStates with { SpasmStatus = 1 };
Require(LifeRunLogic.PrimeConditionCount(passiveStates) == 2, "Prime clear-passive credit failed");
var smallSpecies = passiveStates with { SpeciesClass = 1 };
Require(LifeRunLogic.PrimeConditionCount(smallSpecies) == 3, "Small-species condition credit failed");
Require(LifeRunLogic.PrimeRequiredConditionCount(smallSpecies) == 4, "Small-species threshold failed");

Require(LifeRunLogic.FormatElapsed(TimeSpan.FromMinutes(-2), true) == "0M", "Negative elapsed clamp failed");
Require(LifeRunLogic.FormatElapsed(TimeSpan.FromMinutes(42), false) == "42m", "Minute formatting failed");
Require(LifeRunLogic.FormatElapsed(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(5), true) == "2H 05M",
    "Hour formatting failed");
Require(LifeRunLogic.FormatElapsed(TimeSpan.FromDays(1) + TimeSpan.FromHours(3), false) == "1d 3h",
    "Day formatting failed");

var historyNow = new DateTimeOffset(2026, 7, 21, 16, 0, 0, TimeSpan.Zero);
var rawHistory = Enumerable.Range(0, 27).Select(index => new LifeRunHistoryEntry
{
    Id = index < 2 ? "duplicate" : $"Life {index}",
    EndedAtUnixMs = historyNow.AddHours(-index).ToUnixTimeMilliseconds(),
    SpeciesId = "allo!saurus",
    SpeciesName = index == 0 ? "  Allosaurus\n Prime " : $"Species {index}",
    Outcome = index % 3 == 0
        ? LifeRunHistoryLogic.DeathOutcome
        : index % 3 == 1
            ? LifeRunHistoryLogic.SurvivedOutcome
            : LifeRunHistoryLogic.EndedOutcome,
    DurationSeconds = (index + 1) * 60,
    FinalGrowthPercent = index * 5,
    StageIndex = index % 5,
    TrackedMilestones = index,
    PrimeConditions = index,
    PrimeRequired = index % 2 == 0 ? 4 : 5,
    ServerName = "  Community | One  "
}).ToList();
rawHistory.Add(new LifeRunHistoryEntry
{
    Id = "future",
    EndedAtUnixMs = historyNow.AddDays(2).ToUnixTimeMilliseconds()
});
var normalizedHistory = LifeRunHistoryLogic.NormalizeEntries(rawHistory, historyNow);
Require(normalizedHistory.Count == LifeRunHistoryLogic.MaximumEntries, "History cap failed");
Require(normalizedHistory[0].EndedAtUnixMs > normalizedHistory[^1].EndedAtUnixMs,
    "Newest-first history ordering failed");
Require(normalizedHistory.Select(entry => entry.Id).Distinct().Count() == normalizedHistory.Count,
    "History ID uniqueness failed");
Require(normalizedHistory[0].SpeciesId == "allosaurus", "History species ID sanitation failed");
Require(normalizedHistory[0].SpeciesName == "Allosaurus Prime", "History species label sanitation failed");
Require(normalizedHistory[0].ServerName == "Community / One", "History server label sanitation failed");
Require(normalizedHistory[^1].TrackedMilestones == 6 && normalizedHistory[^1].PrimeConditions == 10,
    "History progress bounds failed");

var historySummary = LifeRunHistoryLogic.Summarize(normalizedHistory);
Require(historySummary.Total == 25, "History total failed");
Require(historySummary.Deaths == 9 && historySummary.Survived == 8 && historySummary.Ended == 8,
    "History outcome summary failed");
Require(historySummary.Entombed == 0, "Unexpected Entomb history count");
Require(historySummary.AverageDurationSeconds == 13 * 60, "History average duration failed");
Require(historySummary.BestGrowthPercent == 100, "History best growth failed");
Require(LifeRunHistoryLogic.FormatDuration(30) == "<1M", "Sub-minute history duration failed");
Require(LifeRunHistoryLogic.FormatDuration(2 * 3600 + 5 * 60) == "2H 05M",
    "Hour history duration failed");
Require(LifeRunHistoryLogic.NormalizeOutcome("unexpected") == LifeRunHistoryLogic.EndedOutcome,
    "Unknown history outcome fallback failed");

var createdHistory = LifeRunHistoryLogic.CreateEntry(
    historyNow,
    "allosaurus",
    "Allosaurus",
    LifeRunHistoryLogic.SurvivedOutcome,
    5400,
    100,
    3,
    6,
    5,
    5,
    "Live Map");
Require(createdHistory.Outcome == LifeRunHistoryLogic.SurvivedOutcome
        && createdHistory.DurationSeconds == 5400
        && createdHistory.FinalGrowthPercent == 100,
    "History entry creation failed");
var historyExport = LifeRunHistoryLogic.BuildExport([createdHistory], historyNow);
Require(historyExport.Contains("Isley survival journal", StringComparison.Ordinal)
        && historyExport.Contains("SURVIVED | Allosaurus | 100%", StringComparison.Ordinal)
        && historyExport.Contains("no game memory", StringComparison.OrdinalIgnoreCase),
    "History export failed");
Require(!historyExport.Contains(" X ", StringComparison.Ordinal)
        && !historyExport.Contains(" Y ", StringComparison.Ordinal),
    "History export coordinate privacy failed");

var entombedHistory = LifeRunHistoryLogic.CreateEntry(
    historyNow,
    "allosaurus",
    "Allosaurus",
    LifeRunHistoryLogic.EntombedOutcome,
    7200,
    100,
    4,
    6,
    5,
    5,
    "Live Map");
Require(entombedHistory.Outcome == LifeRunHistoryLogic.EntombedOutcome
        && LifeRunHistoryLogic.OutcomeLabel(entombedHistory.Outcome) == "ENTOMBED"
        && LifeRunHistoryLogic.Summarize([entombedHistory]).Entombed == 1,
    "Entomb history outcome failed");

Console.WriteLine("Life Run logic: PASS (current-run priorities plus bounded, normalized, private survival-history fixtures)");
