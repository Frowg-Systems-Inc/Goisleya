using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var now = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

// AutoRetryArmed mirrors the RefreshVoiceStatus gate minus the timer check.
Check(VoiceConnectPhaseLogic.AutoRetryArmed(
        true, true, false, false, true, false, false, false, "DISCONNECTED"),
    "a dropped session with auto proximity armed retries");
Check(VoiceConnectPhaseLogic.AutoRetryArmed(
        true, true, false, false, true, false, false, false, "ERROR"),
    "an errored session with auto proximity armed retries");
Check(!VoiceConnectPhaseLogic.AutoRetryArmed(
        true, true, false, false, true, false, false, false, "READY"),
    "a ready session has no retry to arm");
Check(!VoiceConnectPhaseLogic.AutoRetryArmed(
        false, true, false, false, true, false, false, false, "DISCONNECTED"),
    "voice disabled never retries");
Check(!VoiceConnectPhaseLogic.AutoRetryArmed(
        true, false, false, false, true, false, false, false, "DISCONNECTED"),
    "manual-start mode never auto-retries");
Check(!VoiceConnectPhaseLogic.AutoRetryArmed(
        true, true, true, false, true, false, false, false, "DISCONNECTED"),
    "Streamer Mode never retries");
Check(!VoiceConnectPhaseLogic.AutoRetryArmed(
        true, true, false, true, true, false, false, false, "DISCONNECTED"),
    "a user-initiated disconnect never retries");
Check(!VoiceConnectPhaseLogic.AutoRetryArmed(
        true, true, false, false, false, false, false, false, "DISCONNECTED"),
    "a session that never connected has nothing to retry");
Check(!VoiceConnectPhaseLogic.AutoRetryArmed(
        true, true, false, false, true, true, false, false, "DISCONNECTED"),
    "a running bridge never retries");
Check(!VoiceConnectPhaseLogic.AutoRetryArmed(
        true, true, false, false, true, false, true, false, "DISCONNECTED"),
    "an in-flight connect never retries");
Check(!VoiceConnectPhaseLogic.AutoRetryArmed(
        true, true, false, false, true, false, false, true, "DISCONNECTED"),
    "an in-flight auto-connect never retries");

// Backoff countdown math is whole-second, ceil-rounded, and never negative.
Check(VoiceConnectPhaseLogic.RetrySecondsRemaining(now, now.AddSeconds(5)) == 5,
    "a five-second backoff shows five seconds");
Check(VoiceConnectPhaseLogic.RetrySecondsRemaining(now, now.AddSeconds(4.2)) == 5,
    "a fractional backoff rounds up");
Check(VoiceConnectPhaseLogic.RetrySecondsRemaining(now, now.AddMilliseconds(400)) == 1,
    "a sub-second backoff still shows one second");
Check(VoiceConnectPhaseLogic.RetrySecondsRemaining(now, now) == 0,
    "an expired backoff shows nothing");
Check(VoiceConnectPhaseLogic.RetrySecondsRemaining(now, now.AddSeconds(-3)) == 0,
    "a past backoff never goes negative");

// Phase precedence: disabled and connected never refine.
Check(VoiceConnectPhaseLogic.Present(
        false, false, true, false, "CONNECTING", true, true, true, 7).Phase
        == VoiceConnectPhase.None,
    "voice disabled keeps the OFF label");
Check(VoiceConnectPhaseLogic.Present(
        true, true, false, false, "CONNECTED", true, true, false, 0).Phase
        == VoiceConnectPhase.None,
    "a running bridge keeps the CONNECTED label");

// Backoff countdown beats stale CONNECTING-era labels and shows seconds.
var retrying = VoiceConnectPhaseLogic.Present(
    true, false, false, false, "DISCONNECTED", true, true, true, 17);
Check(retrying.Phase == VoiceConnectPhase.Retrying, "an armed backoff reports Retrying");
Check(retrying.Pill == "RETRY 17S", "the retry pill carries the seconds");
Check(retrying.BridgeLabel == "ISLEY VOICE · RETRY IN 17S", "the retry banner carries the seconds");
Check(retrying.Detail.Contains("auto-retry in 17 s", StringComparison.Ordinal),
    "the retry tooltip explains the countdown honestly");
Check(VoiceConnectPhaseLogic.Present(
        true, false, false, false, "DISCONNECTED", true, true, true, 0).Phase
        == VoiceConnectPhase.None,
    "an expired backoff falls back to the plain labels");
Check(VoiceConnectPhaseLogic.Present(
        true, false, false, false, "DISCONNECTED", true, true, false, 9).Phase
        == VoiceConnectPhase.None,
    "no armed retry means no retry label even with seconds");

// Host start: bundled server, connect flow active, readiness not yet passed.
var host = VoiceConnectPhaseLogic.Present(
    true, false, false, true, "READY", true, false, false, 0);
Check(host.Phase == VoiceConnectPhase.HostStarting, "a bundled readiness wait reports HostStarting");
Check(host.Pill == "STARTING HOST", "the host pill stays terse");
Check(host.Detail.Contains("bundled local voice host", StringComparison.Ordinal),
    "the host tooltip names the bundled host");

// Engine load: STARTING is always the built-in engine, never the host.
var engine = VoiceConnectPhaseLogic.Present(
    true, false, true, false, "STARTING", true, true, false, 0);
Check(engine.Phase == VoiceConnectPhase.EngineStarting, "STARTING reports EngineStarting");
Check(engine.Pill == "STARTING", "the engine pill stays terse");

// Room join: CONNECTING with a verified server says so honestly.
var join = VoiceConnectPhaseLogic.Present(
    true, false, true, false, "CONNECTING", true, true, false, 0);
Check(join.Phase == VoiceConnectPhase.JoiningRoom, "CONNECTING with a verified server reports JoiningRoom");
Check(join.Pill == "JOINING", "the join pill stays terse");
Check(join.Detail.Contains("Voice server verified", StringComparison.Ordinal),
    "the join tooltip credits the verified server");
var joinUnverified = VoiceConnectPhaseLogic.Present(
    true, false, true, false, "CONNECTING", false, false, false, 0);
Check(joinUnverified.Phase == VoiceConnectPhase.JoiningRoom,
    "CONNECTING without verification still reports JoiningRoom");
Check(!joinUnverified.Detail.Contains("verified", StringComparison.Ordinal),
    "an unverified join never claims a verified server");

// A remote-server readiness wait stays on the plain CONNECTING label.
Check(VoiceConnectPhaseLogic.Present(
        true, false, false, true, "READY", false, false, false, 0).Phase
        == VoiceConnectPhase.None,
    "a non-bundled readiness wait keeps the plain CONNECTING label");

// No connect flow and no retry means no refinement.
Check(VoiceConnectPhaseLogic.Present(
        true, false, false, false, "READY", true, false, false, 0).Phase
        == VoiceConnectPhase.None,
    "an idle ready session keeps the READY label");

Console.WriteLine(
    "Voice connect phase verification passed (retry gate parity, countdown math, host/engine/join sub-states, and honest copy).");
