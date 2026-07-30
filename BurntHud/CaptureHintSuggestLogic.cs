namespace Isley;

// One-shot-per-session discoverability hint for universal coordinate capture
// ("Player Sync"). The honest mechanic: in The Isle, the GPS panel's Asset
// Location control copies X,Y,Z coordinates to the clipboard; Isley's
// clipboard poll picks that copy up while the game or the overlay is
// foreground. The hint only fires after the visible-HUD game feed has been
// continuously live for a while with zero captures this session, and it
// snoozes forever once a capture lands or the user dismisses it.
internal static class CaptureHintSuggestLogic
{
    internal const int LiveMinutesRequired = 3;

    // Tracks continuous feed liveness. A stale sensor resets the clock so the
    // "live for N minutes" claim is never earned across alt-tab gaps.
    internal static DateTimeOffset? TrackLiveSince(
        DateTimeOffset? liveSince,
        bool feedLive,
        DateTimeOffset now) =>
        feedLive ? liveSince ?? now : null;

    internal static bool ShouldHint(
        bool feedLive,
        DateTimeOffset? liveSince,
        DateTimeOffset now,
        int captureSuccessCount,
        bool captureEnabled,
        bool streamerMode,
        bool alreadyHinted,
        bool snoozed) =>
        feedLive
        && liveSince is { } since
        && now - since >= TimeSpan.FromMinutes(LiveMinutesRequired)
        && captureSuccessCount <= 0
        && captureEnabled
        && !streamerMode
        && !alreadyHinted
        && !snoozed;

    // Mirrors the in-product guidance ("IN THE ISLE: TAB → CLICK ASSET
    // LOCATION") so the hint teaches the real mechanic, nothing else.
    internal const string HintMessage =
        "PLAYER SYNC TIP · IN THE ISLE: TAB → CLICK ASSET LOCATION · ISLEY READS THE COPY · TAP TO DISMISS";
}
