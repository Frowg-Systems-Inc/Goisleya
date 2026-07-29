namespace Isley;

internal readonly record struct LiteModeSuggestSample(bool Starved, double Ratio);

internal static class LiteModeSuggestLogic
{
    internal const double StarvationRatio = 1.75;
    internal const double MaximumMeasuredRatio = 20;
    internal const int WarmupSamples = 12;
    internal const int StarvedStreakRequired = 6;

    internal static LiteModeSuggestSample Sample(double expectedMilliseconds, double actualMilliseconds)
    {
        if (!double.IsFinite(expectedMilliseconds)
            || expectedMilliseconds <= 0
            || !double.IsFinite(actualMilliseconds)
            || actualMilliseconds <= 0)
        {
            return new LiteModeSuggestSample(false, 0);
        }

        var ratio = Math.Min(
            actualMilliseconds / expectedMilliseconds,
            MaximumMeasuredRatio);
        return new LiteModeSuggestSample(ratio >= StarvationRatio, ratio);
    }

    internal static bool ShouldSuggest(
        int sampleCount,
        int starvedStreak,
        bool liteModeEnabled,
        bool alreadyOffered,
        bool snoozed) =>
        !liteModeEnabled
        && !alreadyOffered
        && !snoozed
        && sampleCount >= WarmupSamples
        && starvedStreak >= StarvedStreakRequired;

    internal static string OfferMessage(double observedRatio) =>
        observedRatio >= StarvationRatio
            ? $"TIMERS LAG {observedRatio:0.0}× · TAP TO TRY LITE MODE"
            : "TIMERS FALLING BEHIND · TAP TO TRY LITE MODE";
}
