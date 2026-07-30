namespace Isley;

// Voice CONNECTING sub-state, derived read-only from state the client already
// owns (engine state strings, server-readiness flags, reconnect backoff
// timestamp, session flags). Nothing here changes connect/reconnect behavior,
// and no phase claims more precision than the underlying signals have: the
// pill stays terse and the tooltip carries the honest explanation.
internal enum VoiceConnectPhase
{
    // No refinement; the existing OFF / READY / ERROR / CONNECTED labels stand.
    None,
    // Bundled local host (or its readiness probe) is still coming up.
    HostStarting,
    // Built-in engine page is loading (host already verified by this point).
    EngineStarting,
    // Server verified reachable; session connect + room join still in flight.
    JoiningRoom,
    // Auto-reconnect backoff armed; next attempt in N seconds.
    Retrying
}

internal readonly record struct VoiceConnectPhasePresentation(
    VoiceConnectPhase Phase,
    string Pill,
    string BridgeLabel,
    string Detail);

internal static class VoiceConnectPhaseLogic
{
    internal static readonly VoiceConnectPhasePresentation None =
        new(VoiceConnectPhase.None, string.Empty, string.Empty, string.Empty);

    // Mirrors the RefreshVoiceStatus auto-reconnect gate minus the timer
    // check, so a backoff countdown is only shown when a retry is genuinely
    // armed to fire.
    internal static bool AutoRetryArmed(
        bool voiceEnabled,
        bool autoOpen,
        bool streamerMode,
        bool userDisconnectedThisSession,
        bool sessionConnectedThisSession,
        bool bridgeRunning,
        bool connecting,
        bool autoConnectInFlight,
        string engineState) =>
        voiceEnabled
        && autoOpen
        && !streamerMode
        && !userDisconnectedThisSession
        && sessionConnectedThisSession
        && !bridgeRunning
        && !connecting
        && !autoConnectInFlight
        && engineState is "DISCONNECTED" or "ERROR";

    internal static int RetrySecondsRemaining(DateTimeOffset now, DateTimeOffset notBefore) =>
        notBefore <= now
            ? 0
            : Math.Max(1, (int)Math.Ceiling((notBefore - now).TotalSeconds));

    internal static VoiceConnectPhasePresentation Present(
        bool voiceEnabled,
        bool bridgeRunning,
        bool connecting,
        bool autoConnectInFlight,
        string engineState,
        bool bundledServer,
        bool serverVerified,
        bool retryArmed,
        int retrySeconds)
    {
        if (!voiceEnabled || bridgeRunning)
        {
            return None;
        }

        if (retryArmed && retrySeconds > 0)
        {
            return new VoiceConnectPhasePresentation(
                VoiceConnectPhase.Retrying,
                $"RETRY {retrySeconds}S",
                $"ISLEY VOICE · RETRY IN {retrySeconds}S",
                $"Voice connection dropped · auto-retry in {retrySeconds} s · " +
                "Start voice retries immediately · microphone stays off");
        }

        if (!connecting && !autoConnectInFlight)
        {
            return None;
        }

        return engineState switch
        {
            "CONNECTING" when serverVerified => new VoiceConnectPhasePresentation(
                VoiceConnectPhase.JoiningRoom,
                "JOINING",
                "ISLEY VOICE · JOINING ROOM",
                "Voice server verified · requesting microphone and joining the room · " +
                "microphone stays off until connected"),
            "CONNECTING" => new VoiceConnectPhasePresentation(
                VoiceConnectPhase.JoiningRoom,
                "JOINING",
                "ISLEY VOICE · JOINING ROOM",
                "Connecting the voice session · microphone stays off until connected"),
            "STARTING" => new VoiceConnectPhasePresentation(
                VoiceConnectPhase.EngineStarting,
                "STARTING",
                "ISLEY VOICE · STARTING ENGINE",
                "Loading the built-in voice engine · microphone stays off"),
            // Before the engine starts loading, a bundled-server connect is
            // still waiting on the local host readiness probe.
            _ when bundledServer && !serverVerified => new VoiceConnectPhasePresentation(
                VoiceConnectPhase.HostStarting,
                "STARTING HOST",
                "ISLEY VOICE · STARTING LOCAL HOST",
                "Starting the bundled local voice host · microphone stays off"),
            _ => None
        };
    }
}
