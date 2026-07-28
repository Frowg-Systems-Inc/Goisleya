namespace Isley.Telemetry;

public enum TelemetryStreamState
{
    Waiting,
    Live,
    Delayed,
    Stalled
}

public readonly record struct TelemetryStreamHealth(
    TelemetryStreamState State,
    double SilenceMilliseconds,
    double EffectiveAgeMilliseconds,
    double? UpdateRateHz);

public static class TelemetryStreamHealthLogic
{
    public const double TargetUpdateRateHz = 5;
    public const double MinimumContinuousUpdateRateHz = 2;
    public const double DelayedAfterMilliseconds = 1_000;
    public const double StalledAfterMilliseconds = 3_000;

    public static TelemetryStreamHealth Assess(
        DateTimeOffset? lastAppliedAt,
        DateTimeOffset now,
        double? updateRateHz,
        double relayAgeMilliseconds)
    {
        if (lastAppliedAt is null)
        {
            return new TelemetryStreamHealth(
                TelemetryStreamState.Waiting,
                double.PositiveInfinity,
                double.PositiveInfinity,
                NormalizeRate(updateRateHz));
        }

        var silence = Math.Max(0, (now - lastAppliedAt.Value).TotalMilliseconds);
        var relayAge = double.IsFinite(relayAgeMilliseconds)
            ? Math.Max(0, relayAgeMilliseconds)
            : 0;
        var effectiveAge = Math.Max(silence, relayAge);
        var rate = NormalizeRate(updateRateHz);
        var state = effectiveAge >= StalledAfterMilliseconds
            ? TelemetryStreamState.Stalled
            : effectiveAge >= DelayedAfterMilliseconds
              || rate is > 0 and < MinimumContinuousUpdateRateHz
                ? TelemetryStreamState.Delayed
                : TelemetryStreamState.Live;
        return new TelemetryStreamHealth(state, silence, effectiveAge, rate);
    }

    private static double? NormalizeRate(double? value) =>
        value is double rate && double.IsFinite(rate) && rate >= 0
            ? rate
            : null;
}
