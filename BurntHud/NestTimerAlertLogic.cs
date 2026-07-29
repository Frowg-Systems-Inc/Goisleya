namespace Isley;

internal readonly record struct NestTimerAlertHit(
    int ThresholdMinutes,
    int RemainingSeconds,
    int MaskBit);

internal static class NestTimerAlertLogic
{
    internal const string Snapshot = "2026-07-28";
    internal const int MaximumThresholds = 4;
    internal const int MinimumThresholdMinutes = 1;
    internal const int MaximumThresholdMinutes = 120;

    // Sane default first: 10/5/1-minute warnings ahead of gestation and incubation
    // timers. Later presets are progressively quieter; the empty preset disables alerts.
    internal static readonly int[][] ThresholdPresets =
    [
        [10, 5, 1],
        [5, 1],
        []
    ];

    internal static int NormalizePresetIndex(int index) =>
        Math.Clamp(index, 0, ThresholdPresets.Length - 1);

    internal static IReadOnlyList<int> Thresholds(int presetIndex) =>
        ThresholdPresets[NormalizePresetIndex(presetIndex)];

    internal static string PresetLabel(int presetIndex)
    {
        var thresholds = Thresholds(presetIndex);
        return thresholds.Count == 0
            ? "OFF"
            : string.Join('/', thresholds.Select(minutes => $"{minutes}M"));
    }

    internal static int[] NormalizeThresholds(IEnumerable<int>? thresholds)
    {
        if (thresholds is null)
        {
            return [];
        }

        return thresholds
            .Select(minutes => Math.Clamp(minutes, MinimumThresholdMinutes, MaximumThresholdMinutes))
            .Distinct()
            .OrderByDescending(minutes => minutes)
            .Take(MaximumThresholds)
            .ToArray();
    }

    internal static int MaskForThreshold(IReadOnlyList<int> thresholds, int thresholdIndex)
    {
        if (thresholdIndex < 0 || thresholdIndex >= thresholds.Count || thresholdIndex >= MaximumThresholds)
        {
            return 0;
        }

        return 1 << thresholdIndex;
    }

    // Returns the largest configured threshold the countdown just crossed, or null when
    // nothing new should be announced. Thresholds at or beyond the timer duration are
    // skipped so starting a timer never instant-fires a warning.
    internal static NestTimerAlertHit? Evaluate(
        int durationSeconds,
        double remainingSeconds,
        IReadOnlyList<int> thresholds,
        int notifiedMask)
    {
        if (durationSeconds <= 0 || remainingSeconds <= 0 || thresholds.Count == 0)
        {
            return null;
        }

        var boundedRemaining = Math.Max(0, (int)Math.Ceiling(remainingSeconds));
        for (var index = 0; index < thresholds.Count && index < MaximumThresholds; index++)
        {
            var thresholdSeconds = thresholds[index] * 60;
            if (thresholdSeconds >= durationSeconds
                || boundedRemaining > thresholdSeconds
                || (notifiedMask & (1 << index)) != 0)
            {
                continue;
            }

            return new NestTimerAlertHit(
                thresholds[index],
                boundedRemaining,
                1 << index);
        }

        return null;
    }
}
