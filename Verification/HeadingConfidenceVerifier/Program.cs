using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var unavailable = HeadingConfidenceLogic.Evaluate(false, 123, 0, false);
Check(unavailable.Tier == HeadingConfidenceTier.None
      && unavailable.Dot.Length == 0
      && unavailable.Suffix.Length == 0
      && unavailable.CompactSuffix.Length == 0,
    "unavailable heading must show no dot and no suffix");

var fresh = HeadingConfidenceLogic.Evaluate(true, 123.4, 1500, false);
Check(fresh.Tier == HeadingConfidenceTier.Full
      && fresh.Dot == "●"
      && !fresh.Held
      && fresh.HeldDegrees > 123.3 && fresh.HeldDegrees < 123.5
      && fresh.Suffix.Length == 0,
    "fresh heading must be full confidence with no suffix");

var boundary = HeadingConfidenceLogic.Evaluate(true, 10, HeadingConfidenceLogic.FullMaxAgeMs, false);
Check(boundary.Tier == HeadingConfidenceTier.Full,
    "heading exactly at the full boundary must stay full");

var degraded = HeadingConfidenceLogic.Evaluate(true, 200, 5000, false);
Check(degraded.Tier == HeadingConfidenceTier.Degraded
      && degraded.Dot == "◐"
      && degraded.Suffix.Contains("degraded", StringComparison.Ordinal)
      && degraded.CompactSuffix.Contains("◐", StringComparison.Ordinal)
      && !degraded.Held,
    "degraded heading must carry the half dot and honest copy");

var staleByAge = HeadingConfidenceLogic.Evaluate(true, 270, 12000, false);
Check(staleByAge.Tier == HeadingConfidenceTier.Stale
      && staleByAge.Held
      && staleByAge.Dot == "○"
      && staleByAge.HeldDegrees == 270
      && staleByAge.Suffix.Contains("held", StringComparison.Ordinal)
      && staleByAge.Suffix.Contains("stale 12s", StringComparison.Ordinal)
      && staleByAge.CompactSuffix.Contains("HELD", StringComparison.Ordinal),
    "stale heading must hold the last good value with a stale indicator");

var staleByFeedAlert = HeadingConfidenceLogic.Evaluate(true, 45, 500, true);
Check(staleByFeedAlert.Tier == HeadingConfidenceTier.Stale && staleByFeedAlert.Held,
    "feed-wide stale alert must stale the heading even when the sample age looks fresh");

var wrapped = HeadingConfidenceLogic.Evaluate(true, -30, 500, false);
Check(wrapped.HeldDegrees == 330,
    "held heading must normalize into 0-360 without jumping");
var nonFinite = HeadingConfidenceLogic.Evaluate(true, double.NaN, double.NaN, false);
Check(nonFinite.HeldDegrees == 0 && nonFinite.Tier == HeadingConfidenceTier.Full,
    "non-finite input must degrade to a safe held value");

Check(HeadingConfidenceLogic.FormatAge(0) == "0s"
      && HeadingConfidenceLogic.FormatAge(89000) == "89s"
      && HeadingConfidenceLogic.FormatAge(120000) == "2m"
      && HeadingConfidenceLogic.FormatAge(double.NaN) == "0s",
    "heading staleness age formatting failed");

Console.WriteLine(
    "Heading confidence: PASS (no-dot unknown, full/degraded/stale tiers, hold-last with stale indicator, feed-alert staleness, normalization, age copy)");
