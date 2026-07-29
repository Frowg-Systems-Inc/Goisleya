using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

Check(NestTimerAlertLogic.MaximumThresholds == 4, "alert roster is bounded at four thresholds");
Check(NestTimerAlertLogic.MinimumThresholdMinutes == 1, "minimum threshold is one minute");
Check(NestTimerAlertLogic.MaximumThresholdMinutes == 120, "maximum threshold is two hours");
Check(NestTimerAlertLogic.Snapshot.Length > 0, "snapshot marker is recorded");

Check(NestTimerAlertLogic.NormalizePresetIndex(-1) == 0, "preset index clamps low");
Check(NestTimerAlertLogic.NormalizePresetIndex(99) == NestTimerAlertLogic.ThresholdPresets.Length - 1,
    "preset index clamps high");
Check(NestTimerAlertLogic.Thresholds(0).SequenceEqual(new[] { 10, 5, 1 }),
    "sane default preset warns 10/5/1 minutes ahead");
Check(NestTimerAlertLogic.Thresholds(1).SequenceEqual(new[] { 5, 1 }), "quieter middle preset");
Check(NestTimerAlertLogic.Thresholds(2).Count == 0, "empty preset disables alerts");
Check(NestTimerAlertLogic.PresetLabel(0) == "10M/5M/1M", "preset label lists thresholds");
Check(NestTimerAlertLogic.PresetLabel(2) == "OFF", "empty preset labels OFF");

var normalized = NestTimerAlertLogic.NormalizeThresholds(new[] { 5, 5, 0, 200, 10, 3, 2, 1 });
Check(normalized.SequenceEqual(new[] { 120, 10, 5, 3 }),
    "threshold normalization clamps, dedupes, sorts descending, and caps at four");
Check(NestTimerAlertLogic.NormalizeThresholds(null).Length == 0, "null thresholds normalize to empty");

Check(NestTimerAlertLogic.MaskForThreshold(new[] { 10, 5, 1 }, 0) == 1, "mask bit for first threshold");
Check(NestTimerAlertLogic.MaskForThreshold(new[] { 10, 5, 1 }, 2) == 4, "mask bit for third threshold");
Check(NestTimerAlertLogic.MaskForThreshold(new[] { 10, 5, 1 }, 3) == 0, "out-of-roster index has no bit");
Check(NestTimerAlertLogic.MaskForThreshold(new[] { 10, 5, 1 }, -1) == 0, "negative index has no bit");
Check(NestTimerAlertLogic.MaskForThreshold(new[] { 10, 5, 1, 1 }, 4) == 0,
    "index beyond the maximum threshold count has no bit");

var thresholds = new[] { 10, 5, 1 };
Check(NestTimerAlertLogic.Evaluate(3600, 601, thresholds, 0) is null,
    "no alert while the countdown is above every threshold");
var hit = NestTimerAlertLogic.Evaluate(3600, 600, thresholds, 0);
Check(hit is { ThresholdMinutes: 10, RemainingSeconds: 600, MaskBit: 1 },
    "crossing the largest threshold announces it exactly once");
Check(NestTimerAlertLogic.Evaluate(3600, 600, thresholds, 1) is null,
    "an already-notified threshold is not repeated");
Check(NestTimerAlertLogic.Evaluate(3600, 300, thresholds, 1) is { ThresholdMinutes: 5, MaskBit: 2 },
    "the next threshold fires once the countdown crosses it");
Check(NestTimerAlertLogic.Evaluate(3600, 600, thresholds, 0b111) is null,
    "a fully notified mask stays silent");
Check(NestTimerAlertLogic.Evaluate(3600, 599.2, thresholds, 0) is { ThresholdMinutes: 10 },
    "fractional seconds round up to the crossing boundary");
Check(NestTimerAlertLogic.Evaluate(300, 300, thresholds, 0) is null,
    "thresholds at or beyond the timer duration never instant-fire");
Check(NestTimerAlertLogic.Evaluate(300, 60, thresholds, 0) is { ThresholdMinutes: 1, MaskBit: 4 },
    "only thresholds inside the timer duration can fire");
Check(NestTimerAlertLogic.Evaluate(0, 60, thresholds, 0) is null, "a zero-duration timer never fires");
Check(NestTimerAlertLogic.Evaluate(3600, 0, thresholds, 0) is null, "an expired timer never fires");
Check(NestTimerAlertLogic.Evaluate(3600, 60, Array.Empty<int>(), 0) is null,
    "an empty threshold roster never fires");

Console.WriteLine(
    "Nest timer alert verification passed (preset roster, threshold normalization, notify-mask one-shot gating, and duration-boundary honesty).");
