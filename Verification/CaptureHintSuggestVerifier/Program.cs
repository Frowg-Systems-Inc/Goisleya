using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var now = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
var liveThreeMinutes = now.AddMinutes(-3);
var liveAlmostThree = now.AddMinutes(-2.999);

// Continuous-liveness tracking: starts on the first live tick, holds while
// live, resets on any stale tick so alt-tab gaps never count.
Check(CaptureHintSuggestLogic.TrackLiveSince(null, true, now) == now,
    "the first live tick starts the clock");
Check(CaptureHintSuggestLogic.TrackLiveSince(liveThreeMinutes, true, now) == liveThreeMinutes,
    "a live tick preserves the original start");
Check(CaptureHintSuggestLogic.TrackLiveSince(liveThreeMinutes, false, now) is null,
    "a stale tick resets the clock");
Check(CaptureHintSuggestLogic.TrackLiveSince(null, false, now) is null,
    "a stale tick without a clock stays empty");

// All gates open suggests the hint.
Check(CaptureHintSuggestLogic.ShouldHint(true, liveThreeMinutes, now, 0, true, false, false, false),
    "three live minutes with zero captures suggests the hint");
Check(CaptureHintSuggestLogic.ShouldHint(true, now.AddMinutes(-10), now, 0, true, false, false, false),
    "longer liveness also suggests the hint");
Check(!CaptureHintSuggestLogic.ShouldHint(true, liveAlmostThree, now, 0, true, false, false, false),
    "just under three minutes stays silent");
Check(!CaptureHintSuggestLogic.ShouldHint(false, liveThreeMinutes, now, 0, true, false, false, false),
    "a stale feed stays silent");
Check(!CaptureHintSuggestLogic.ShouldHint(true, null, now, 0, true, false, false, false),
    "no liveness clock stays silent");
Check(!CaptureHintSuggestLogic.ShouldHint(true, liveThreeMinutes, now, 1, true, false, false, false),
    "a successful capture retires the hint");
Check(!CaptureHintSuggestLogic.ShouldHint(true, liveThreeMinutes, now, 0, false, false, false, false),
    "capture disabled stays silent");
Check(!CaptureHintSuggestLogic.ShouldHint(true, liveThreeMinutes, now, 0, true, true, false, false),
    "Streamer Mode stays silent");
Check(!CaptureHintSuggestLogic.ShouldHint(true, liveThreeMinutes, now, 0, true, false, true, false),
    "already hinted stays silent (one-shot per session)");
Check(!CaptureHintSuggestLogic.ShouldHint(true, liveThreeMinutes, now, 0, true, false, false, true),
    "snoozed stays silent forever");

// The hint teaches the real mechanic and nothing else.
Check(CaptureHintSuggestLogic.HintMessage.Contains("TAB", StringComparison.Ordinal)
      && CaptureHintSuggestLogic.HintMessage.Contains("ASSET LOCATION", StringComparison.Ordinal),
    "the hint names the in-game Asset Location copy");
Check(CaptureHintSuggestLogic.HintMessage.Contains("TAP TO DISMISS", StringComparison.Ordinal),
    "the hint tells the user how to dismiss it");
Check(CaptureHintSuggestLogic.LiveMinutesRequired == 3,
    "the liveness requirement stays at three minutes");

Console.WriteLine(
    "Capture hint suggestion verification passed (liveness tracking, all eight gates, one-shot/snooze semantics, and honest mechanic copy).");
