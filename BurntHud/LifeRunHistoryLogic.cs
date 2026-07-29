namespace Isley;

internal sealed class LifeRunHistoryEntry
{
    public string Id { get; set; } = string.Empty;
    public long EndedAtUnixMs { get; set; }
    public string SpeciesId { get; set; } = string.Empty;
    public string SpeciesName { get; set; } = "Unknown / server mod";
    public string Outcome { get; set; } = LifeRunHistoryLogic.EndedOutcome;
    public int DurationSeconds { get; set; }
    public int FinalGrowthPercent { get; set; }
    public int StageIndex { get; set; }
    public int TrackedMilestones { get; set; }
    public int PrimeConditions { get; set; }
    public int PrimeRequired { get; set; } = 5;
    public string ServerName { get; set; } = "Unspecified server";
    public int BestCaptureStreak { get; set; }
}

internal readonly record struct LifeRunHistorySummary(
    int Total,
    int Deaths,
    int Survived,
    int Entombed,
    int Ended,
    int AverageDurationSeconds,
    int BestGrowthPercent,
    int AdultOrHigher);

internal static class LifeRunHistoryLogic
{
    internal const int MaximumEntries = 25;
    internal const int VisibleEntries = 3;
    internal const string DeathOutcome = "death";
    internal const string SurvivedOutcome = "survived";
    internal const string EntombedOutcome = "entombed";
    internal const string EndedOutcome = "ended";
    private const int MaximumDurationSeconds = 30 * 24 * 60 * 60;
    private const int MaximumCaptureStreakValue = 9999;

    internal static LifeRunHistoryEntry CreateEntry(
        DateTimeOffset endedAt,
        string? speciesId,
        string? speciesName,
        string? outcome,
        int durationSeconds,
        int finalGrowthPercent,
        int stageIndex,
        int trackedMilestones,
        int primeConditions,
        int primeRequired,
        string? serverName) =>
        NormalizeEntries(
            [new LifeRunHistoryEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                EndedAtUnixMs = endedAt.ToUnixTimeMilliseconds(),
                SpeciesId = speciesId ?? string.Empty,
                SpeciesName = speciesName ?? string.Empty,
                Outcome = outcome ?? EndedOutcome,
                DurationSeconds = durationSeconds,
                FinalGrowthPercent = finalGrowthPercent,
                StageIndex = stageIndex,
                TrackedMilestones = trackedMilestones,
                PrimeConditions = primeConditions,
                PrimeRequired = primeRequired,
                ServerName = serverName ?? string.Empty
            }],
            endedAt)[0];

    internal static List<LifeRunHistoryEntry> NormalizeEntries(
        IEnumerable<LifeRunHistoryEntry>? entries,
        DateTimeOffset now)
    {
        var earliest = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var latest = now.AddDays(1);
        var normalized = new List<LifeRunHistoryEntry>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var fallbackIndex = 0;
        foreach (var source in entries ?? [])
        {
            if (source is null)
            {
                continue;
            }

            DateTimeOffset endedAt;
            try
            {
                endedAt = DateTimeOffset.FromUnixTimeMilliseconds(source.EndedAtUnixMs);
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }
            if (endedAt < earliest || endedAt > latest)
            {
                continue;
            }

            var id = SanitizeId(source.Id);
            if (string.IsNullOrEmpty(id) || !usedIds.Add(id))
            {
                do
                {
                    fallbackIndex++;
                    id = $"life-{source.EndedAtUnixMs}-{fallbackIndex}";
                } while (!usedIds.Add(id));
            }

            normalized.Add(new LifeRunHistoryEntry
            {
                Id = id,
                EndedAtUnixMs = source.EndedAtUnixMs,
                SpeciesId = SanitizeId(source.SpeciesId),
                SpeciesName = SanitizeLabel(source.SpeciesName, "Unknown / server mod", 40),
                Outcome = NormalizeOutcome(source.Outcome),
                DurationSeconds = Math.Clamp(source.DurationSeconds, 0, MaximumDurationSeconds),
                FinalGrowthPercent = Math.Clamp(source.FinalGrowthPercent, 0, 100),
                StageIndex = Math.Clamp(source.StageIndex, 0, 4),
                TrackedMilestones = Math.Clamp(source.TrackedMilestones, 0, 6),
                PrimeConditions = Math.Clamp(source.PrimeConditions, 0, 10),
                PrimeRequired = Math.Clamp(source.PrimeRequired, 4, 5),
                ServerName = SanitizeLabel(source.ServerName, "Unspecified server", 40),
                BestCaptureStreak = Math.Clamp(source.BestCaptureStreak, 0, MaximumCaptureStreakValue)
            });
        }

        return normalized
            .OrderByDescending(entry => entry.EndedAtUnixMs)
            .Take(MaximumEntries)
            .ToList();
    }

    internal static string NormalizeOutcome(string? outcome) => outcome?.Trim().ToLowerInvariant() switch
    {
        DeathOutcome => DeathOutcome,
        SurvivedOutcome => SurvivedOutcome,
        EntombedOutcome => EntombedOutcome,
        _ => EndedOutcome
    };

    internal static string OutcomeLabel(string? outcome) => NormalizeOutcome(outcome) switch
    {
        DeathOutcome => "DEATH",
        SurvivedOutcome => "SURVIVED",
        EntombedOutcome => "ENTOMBED",
        _ => "ENDED"
    };

    internal static LifeRunHistorySummary Summarize(IReadOnlyList<LifeRunHistoryEntry> entries)
    {
        if (entries.Count == 0)
        {
            return new LifeRunHistorySummary(0, 0, 0, 0, 0, 0, 0, 0);
        }

        return new LifeRunHistorySummary(
            entries.Count,
            entries.Count(entry => NormalizeOutcome(entry.Outcome) == DeathOutcome),
            entries.Count(entry => NormalizeOutcome(entry.Outcome) == SurvivedOutcome),
            entries.Count(entry => NormalizeOutcome(entry.Outcome) == EntombedOutcome),
            entries.Count(entry => NormalizeOutcome(entry.Outcome) == EndedOutcome),
            (int)Math.Round(entries.Average(entry => Math.Max(0, entry.DurationSeconds))),
            entries.Max(entry => Math.Clamp(entry.FinalGrowthPercent, 0, 100)),
            entries.Count(entry => entry.StageIndex >= 3));
    }

    internal static string FormatDuration(int durationSeconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Clamp(durationSeconds, 0, MaximumDurationSeconds));
        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays}D {duration.Hours}H";
        }
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}H {duration.Minutes:00}M";
        }
        return duration.TotalMinutes >= 1
            ? $"{Math.Max(1, (int)Math.Round(duration.TotalMinutes))}M"
            : "<1M";
    }

    internal static string BuildExport(
        IEnumerable<LifeRunHistoryEntry>? entries,
        DateTimeOffset generatedAt)
    {
        var normalized = NormalizeEntries(entries, generatedAt);
        var summary = Summarize(normalized);
        var lines = new List<string>
        {
            "Isley survival journal",
            $"Lives {summary.Total} | survived {summary.Survived} | entombed {summary.Entombed} | deaths {summary.Deaths} | " +
            $"average {FormatDuration(summary.AverageDurationSeconds)} | best growth {summary.BestGrowthPercent}%"
        };
        lines.AddRange(normalized.Select((entry, index) =>
        {
            var ended = DateTimeOffset.FromUnixTimeMilliseconds(entry.EndedAtUnixMs).ToLocalTime();
            var streakSegment = entry.BestCaptureStreak > 0
                ? $"sync best {Math.Clamp(entry.BestCaptureStreak, 0, MaximumCaptureStreakValue)} | "
                : string.Empty;
            return $"{index + 1}. {ended:yyyy-MM-dd} | {OutcomeLabel(entry.Outcome)} | " +
                   $"{entry.SpeciesName} | {entry.FinalGrowthPercent}% | {FormatDuration(entry.DurationSeconds)} | " +
                   $"tracked {entry.TrackedMilestones}/6 | Prime {entry.PrimeConditions}/{entry.PrimeRequired} | " +
                   streakSegment +
                   entry.ServerName;
        }));
        lines.Add("Private manual history; no game memory, automatic death detection, player identity, or coordinates.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string SanitizeId(string? value)
    {
        var sanitized = new string((value ?? string.Empty)
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            .Select(char.ToLowerInvariant)
            .ToArray())
            .Trim('-', '_');
        return sanitized.Length <= 64 ? sanitized : sanitized[..64];
    }

    private static string SanitizeLabel(string? value, string fallback, int maximumLength)
    {
        var sanitized = string.Join(' ', (value ?? string.Empty)
            .Replace('|', '/')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        sanitized = new string(sanitized.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return fallback;
        }
        return sanitized.Length <= maximumLength ? sanitized : sanitized[..maximumLength].TrimEnd();
    }
}
