using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

Check(LiteModeSuggestLogic.StarvationRatio == 1.75, "starvation ratio stays 1.75x");
Check(LiteModeSuggestLogic.MaximumMeasuredRatio == 20, "measured ratio stays bounded at 20x");
Check(LiteModeSuggestLogic.WarmupSamples == 12, "warmup requires twelve samples");
Check(LiteModeSuggestLogic.StarvedStreakRequired == 6, "a six-sample starved streak is required");

var calm = LiteModeSuggestLogic.Sample(250, 250);
Check(!calm.Starved && calm.Ratio == 1, "on-time timers are not starved");
var starved = LiteModeSuggestLogic.Sample(250, 500);
Check(starved.Starved && starved.Ratio == 2, "2x lag is starved");
Check(!LiteModeSuggestLogic.Sample(250, 437.4).Starved, "just under 1.75x is not starved");
Check(LiteModeSuggestLogic.Sample(250, 437.5).Starved, "exactly 1.75x is starved");
Check(LiteModeSuggestLogic.Sample(100, 100_000).Ratio == 20, "extreme lag is clamped to the measured cap");
foreach (var (expected, actual) in new[]
         {
             (0d, 250d), (-1d, 250d), (double.NaN, 250d), (double.PositiveInfinity, 250d),
             (250d, 0d), (250d, -1d), (250d, double.NaN), (250d, double.PositiveInfinity)
         })
{
    var sample = LiteModeSuggestLogic.Sample(expected, actual);
    Check(!sample.Starved && sample.Ratio == 0,
        $"non-finite or non-positive samples are rejected ({expected}, {actual})");
}

Check(LiteModeSuggestLogic.ShouldSuggest(12, 6, false, false, false),
    "all gates open suggests Lite Mode");
Check(!LiteModeSuggestLogic.ShouldSuggest(11, 6, false, false, false), "warmup gate holds");
Check(!LiteModeSuggestLogic.ShouldSuggest(12, 5, false, false, false), "streak gate holds");
Check(!LiteModeSuggestLogic.ShouldSuggest(12, 6, true, false, false),
    "Lite Mode already on stays silent");
Check(!LiteModeSuggestLogic.ShouldSuggest(12, 6, false, true, false),
    "already offered stays silent");
Check(!LiteModeSuggestLogic.ShouldSuggest(12, 6, false, false, true), "snoozed stays silent");

Check(LiteModeSuggestLogic.OfferMessage(2) == "TIMERS LAG 2.0× · TAP TO TRY LITE MODE",
    "a measured starved offer reports the observed lag");
Check(LiteModeSuggestLogic.OfferMessage(1) == "TIMERS FALLING BEHIND · TAP TO TRY LITE MODE",
    "an unmeasured offer stays generic");

Console.WriteLine(
    "Lite Mode suggestion verification passed (starvation sampling, ratio cap, all five suggestion gates, and honest offer copy).");
