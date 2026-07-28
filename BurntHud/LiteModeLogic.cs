namespace Isley;

internal readonly record struct LiteModeProfile(
    bool Enabled,
    int GamePollMilliseconds,
    int PlayFocusMilliseconds,
    int SurvivalRefreshMilliseconds,
    int VoiceStatusMilliseconds,
    int MarkerPollMilliseconds,
    int ControllerRefreshMilliseconds,
    bool UseShellShadow,
    bool UseContinuousAnimations,
    string ButtonLabel,
    string Status,
    string Tooltip);

internal static class LiteModeLogic
{
    internal const int FullMarkerPollMilliseconds = 500;
    internal const int LiteMarkerPollMilliseconds = 1000;
    internal const int FullControllerRefreshMilliseconds = 250;
    internal const int LiteControllerRefreshMilliseconds = 1000;

    internal static LiteModeProfile Resolve(bool enabled) => enabled
        ? new LiteModeProfile(
            Enabled: true,
            GamePollMilliseconds: 4000,
            PlayFocusMilliseconds: 750,
            SurvivalRefreshMilliseconds: 1000,
            VoiceStatusMilliseconds: 1000,
            MarkerPollMilliseconds: LiteMarkerPollMilliseconds,
            ControllerRefreshMilliseconds: LiteControllerRefreshMilliseconds,
            UseShellShadow: false,
            UseContinuousAnimations: false,
            ButtonLabel: "Lite Mode · On",
            Status: "LIGHTWEIGHT · 1s live map · reduced effects · all tools available",
            Tooltip:
                "Lower CPU, GPU, and network activity while keeping the live marker, heading, " +
                "follow, routes, vitals, voice, alerts, and saved tools available.")
        : new LiteModeProfile(
            Enabled: false,
            GamePollMilliseconds: 2000,
            PlayFocusMilliseconds: 250,
            SurvivalRefreshMilliseconds: 250,
            VoiceStatusMilliseconds: 1000,
            MarkerPollMilliseconds: FullMarkerPollMilliseconds,
            ControllerRefreshMilliseconds: FullControllerRefreshMilliseconds,
            UseShellShadow: true,
            UseContinuousAnimations: true,
            ButtonLabel: "Lite Mode · Off",
            Status: "FULL · 0.5s live map · full motion and effects",
            Tooltip:
                "Use the fastest authorized live-map cadence and full visual effects. " +
                "Turn on Lite Mode to reduce background work.");
}
