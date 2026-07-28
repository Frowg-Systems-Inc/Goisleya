namespace Isley;

internal enum PlayerSnapshotSourceState
{
    Unavailable,
    Live,
    LastKnown
}

internal readonly record struct PlayerSnapshotRaw(
    PlayerSnapshotSourceState State,
    string? SpeciesId,
    double? GrowthPercent,
    double? HealthCurrent,
    double? HealthMaximum,
    double? FoodCurrent,
    double? FoodMaximum,
    double? WaterCurrent,
    double? WaterMaximum,
    int? PrimeCompleted,
    int? PrimeRequired,
    int? PrimeTotal,
    DateTimeOffset ReceivedAt);

internal readonly record struct PlayerSnapshotEvaluation(
    PlayerSnapshotSourceState State,
    bool HasValidData,
    bool IsFresh,
    bool LiveFresh,
    bool LastKnown,
    bool Stale,
    int AgeSeconds,
    bool SpeciesAvailable,
    string SpeciesId,
    int GrowthPercent,
    int HealthPercent,
    int FoodPercent,
    int WaterPercent,
    bool PrimeAvailable,
    int PrimeCompleted,
    int PrimeRequired,
    int PrimeTotal,
    ReportedHealthState HealthState,
    ReportedVitalState FoodState,
    ReportedVitalState WaterState);

internal static class PlayerSnapshotLogic
{
    internal const int FullLiveRefreshMilliseconds = 2_000;
    internal const int LiteLiveRefreshMilliseconds = 5_000;
    internal const int LastKnownRefreshMilliseconds = 60_000;
    internal const int InitialRefreshMilliseconds = 250;
    internal const int ErrorRetryMilliseconds = 5_000;
    internal const int MaximumErrorRetryMilliseconds = 60_000;
    internal const int FreshnessSeconds = 15;

    internal static int LiveRefreshMilliseconds(bool liteMode) =>
        liteMode ? LiteLiveRefreshMilliseconds : FullLiveRefreshMilliseconds;

    internal static PlayerSnapshotEvaluation Evaluate(PlayerSnapshotRaw? raw, DateTimeOffset now)
    {
        if (raw is null || raw.Value.State == PlayerSnapshotSourceState.Unavailable)
        {
            return Unavailable();
        }

        var snapshot = raw.Value;
        var valuesValid = IsPercent(snapshot.GrowthPercent)
                          && IsRatio(snapshot.HealthCurrent, snapshot.HealthMaximum)
                          && IsRatio(snapshot.FoodCurrent, snapshot.FoodMaximum)
                          && IsRatio(snapshot.WaterCurrent, snapshot.WaterMaximum);
        if (!valuesValid)
        {
            return Unavailable();
        }

        var ageSeconds = AgeSeconds(snapshot.ReceivedAt, now);
        var isFresh = ageSeconds < FreshnessSeconds;
        var liveFresh = snapshot.State == PlayerSnapshotSourceState.Live && isFresh;
        var lastKnown = snapshot.State == PlayerSnapshotSourceState.LastKnown;
        var stale = snapshot.State == PlayerSnapshotSourceState.Live && !isFresh;
        var growthPercent = Percent(snapshot.GrowthPercent!.Value);
        var speciesId = NormalizeSpeciesIdentifier(snapshot.SpeciesId);
        var healthPercent = RatioPercent(snapshot.HealthCurrent!.Value, snapshot.HealthMaximum!.Value);
        var foodPercent = RatioPercent(snapshot.FoodCurrent!.Value, snapshot.FoodMaximum!.Value);
        var waterPercent = RatioPercent(snapshot.WaterCurrent!.Value, snapshot.WaterMaximum!.Value);
        var primeAvailable = PrimeValid(
            snapshot.PrimeCompleted,
            snapshot.PrimeRequired,
            snapshot.PrimeTotal);

        return new PlayerSnapshotEvaluation(
            snapshot.State,
            true,
            isFresh,
            liveFresh,
            lastKnown,
            stale,
            ageSeconds,
            !string.IsNullOrEmpty(speciesId),
            speciesId,
            growthPercent,
            healthPercent,
            foodPercent,
            waterPercent,
            primeAvailable,
            primeAvailable ? snapshot.PrimeCompleted!.Value : 0,
            primeAvailable ? snapshot.PrimeRequired!.Value : 0,
            primeAvailable ? snapshot.PrimeTotal!.Value : 0,
            liveFresh ? HealthState(healthPercent) : ReportedHealthState.Unknown,
            liveFresh ? VitalState(foodPercent) : ReportedVitalState.Unknown,
            liveFresh ? VitalState(waterPercent) : ReportedVitalState.Unknown);
    }

    internal static string CompactLabel(
        PlayerSnapshotEvaluation snapshot,
        ReportedVitalState stamina) =>
        snapshot.LiveFresh
            ? $"HP {snapshot.HealthPercent} · F {snapshot.FoodPercent} · W {snapshot.WaterPercent} · ST {CoreVitalsLogic.ShortLabel(stamina)}"
            : string.Empty;

    internal static string FormatAge(int seconds)
    {
        var safe = Math.Max(0, seconds);
        return safe < 60 ? $"{safe}S" : $"{safe / 60}M";
    }

    private static PlayerSnapshotEvaluation Unavailable() => new(
        PlayerSnapshotSourceState.Unavailable,
        false,
        false,
        false,
        false,
        false,
        int.MaxValue,
        false,
        string.Empty,
        0,
        0,
        0,
        0,
        false,
        0,
        0,
        0,
        ReportedHealthState.Unknown,
        ReportedVitalState.Unknown,
        ReportedVitalState.Unknown);

    private static bool IsPercent(double? value) =>
        value is >= 0 and <= 100 && double.IsFinite(value.Value);

    private static bool IsRatio(double? current, double? maximum) =>
        current is >= 0
        && maximum is > 0 and <= 1_000_000
        && double.IsFinite(current.Value)
        && double.IsFinite(maximum.Value)
        && current <= maximum;

    private static bool PrimeValid(int? completed, int? required, int? total) =>
        completed is >= 0
        && required is >= 1
        && total is >= 1 and <= 10
        && completed <= total
        && required <= total;

    private static string NormalizeSpeciesIdentifier(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length is > 0 and <= 32
               && normalized.All(character => character is >= 'a' and <= 'z')
            ? normalized
            : string.Empty;
    }

    private static int Percent(double value) =>
        (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 100);

    private static int RatioPercent(double current, double maximum) =>
        Percent(current / maximum * 100);

    private static ReportedHealthState HealthState(int percent) => percent switch
    {
        <= 25 => ReportedHealthState.Critical,
        <= 60 => ReportedHealthState.Hurt,
        _ => ReportedHealthState.Stable
    };

    private static ReportedVitalState VitalState(int percent) => percent switch
    {
        <= 10 => ReportedVitalState.Empty,
        <= 35 => ReportedVitalState.Low,
        _ => ReportedVitalState.Stable
    };

    private static int AgeSeconds(DateTimeOffset receivedAt, DateTimeOffset now)
    {
        if (receivedAt == default) return int.MaxValue;
        return (int)Math.Clamp(Math.Floor((now - receivedAt).TotalSeconds), 0, int.MaxValue);
    }
}
