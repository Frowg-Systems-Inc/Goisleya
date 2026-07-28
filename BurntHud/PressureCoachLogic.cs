namespace Isley;

internal readonly record struct PressureCoachPresentation(
    bool Show,
    string Title,
    string Detail,
    string CoachId);

internal static class PressureCoachLogic
{
    public const string FirstDeathId = "first-death";
    public const string FirstNestId = "first-nest";
    public const string ConsentRosterId = "consent-roster";
    public const string PreStreamId = "pre-stream";

    public static PressureCoachPresentation FirstDeath(bool alreadySeen) =>
        alreadySeen
            ? default
            : new(
                true,
                "DEATH MARKER SAVED",
                "Use ROUTE TO BODY for a road/trail course back. Streamer Mode hides markers.",
                FirstDeathId);

    public static PressureCoachPresentation FirstNest(bool alreadySeen, bool nestActive) =>
        !nestActive || alreadySeen
            ? default
            : new(
                true,
                "NEST ACTIVE",
                "Try Nest focus for a wide perimeter, food layers, and friend trails.",
                FirstNestId);

    public static PressureCoachPresentation ConsentRoster(
        bool alreadySeen,
        bool liveNetworkConnected,
        bool consentFiltered,
        bool friendSharingOn,
        int grantCount,
        int friendCount) =>
        alreadySeen
        || !liveNetworkConnected
        || !consentFiltered
        || friendCount > 0
            ? default
            : new(
                true,
                "NO FRIENDS VISIBLE YET",
                friendSharingOn || grantCount > 0
                    ? "Sharing is on — waiting for verified Steam friends or grants online. This is not a broken connection."
                    : "Consent-filtered server · turn FRIEND SHARING ON or ALLOW a SteamID64.",
                ConsentRosterId);

    public static PressureCoachPresentation PreStream(bool alreadySeen) =>
        alreadySeen
            ? default
            : new(
                true,
                "BEFORE STREAMER MODE",
                "Streamer Mode hides live positions, names, and sensitive map chrome. Toggle again to restore.",
                PreStreamId);
}
