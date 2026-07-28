namespace Isley;

internal sealed record OnboardingTutorialStep(
    string Kicker,
    string Title,
    string Body,
    string Tip);

internal static class OnboardingTutorialLogic
{
    internal const int CurrentVersion = 6;

    internal static readonly IReadOnlyList<OnboardingTutorialStep> Steps =
    [
        new(
            "SERVER COMPATIBILITY",
            "Choose where you are playing",
            "Live Map mode opens Isley's bundled map. It works independently on official, community, private, passworded, and unlisted servers; Official and Any Server modes keep a simpler manual workspace.",
            "A participating server can also give you an Isley join link. Paste it under Isley Live Network and sign in through Steam for authorized continuous position, facing, vitals, conditions, friend, and animal updates."),
        new(
            "YOUR MAP ICON",
            "Get your position on the map",
            "Live Map mode shows your authorized circle and blue direction arrow. Select FIND ME / RECENTER or press Ctrl+Shift+R after exploring. Drag the top bar to move Isley, drag the // corner for any size, or select - to dock it.",
            "For automatic location after login, connect a participating Isley Live Network link once with Steam. Without a join link, keep Auto location on and turn SYNC ON (Player Sync). In The Isle, press Tab and click Asset Location while the game or Isley is focused. Two different captures place your circle, infer travel direction, and unlock Terrain Probe slope checks. Isley never invents a position or extracts hidden game data. Use click-through when setup is complete."),
        new(
            "ROUTES AND AWARENESS",
            "Use the tools your server can support",
            "Live Map mode can plot a road-and-trail course around known marked obstacles and show only authorized contacts. The network status tells you its speed, age, connected nodes, and whether coverage is consent-filtered or server-wide.",
            "Use Friend Sharing or an explicit SteamID64 allow for consent-filtered servers. Verify cliffs, water, weather, and server changes in game."),
        new(
            "SURVIVAL TOOLS",
            "Keep essentials close",
            "On every server, use the small vitals strip to report health, food, water, and stamina. Recovery guidance, timers, restart warnings, safe logout, and automatic proximity push-to-talk voice stay nearby without covering the game.",
            "Proximity voice connects automatically; hold your PTT key to talk. Only start sickness guidance when the matching warning is visible in The Isle."),
        new(
            "YOU ARE READY",
            "Find any tool quickly",
            "Open TOOLS for the universal player kit and server mode. Use Quick Commands with Ctrl+Shift+P by default. Turn on Lite Mode for lower background work while keeping every compatible tool available.",
            "Streamer Mode hides sensitive labels. Replay this tour or change server mode any time from App > Getting Started.")
    ];

    internal static bool ShouldShow(int completedVersion) =>
        completedVersion < CurrentVersion;

    internal static int NormalizeIndex(int index) =>
        Math.Clamp(index, 0, Steps.Count - 1);

    internal static int Move(int index, int delta) =>
        NormalizeIndex(NormalizeIndex(index) + delta);

    internal static bool IsFirst(int index) =>
        NormalizeIndex(index) == 0;

    internal static bool IsLast(int index) =>
        NormalizeIndex(index) == Steps.Count - 1;

    internal static OnboardingTutorialStep Step(int index) =>
        Steps[NormalizeIndex(index)];

    internal static string ProgressLabel(int index) =>
        $"{NormalizeIndex(index) + 1} OF {Steps.Count}";

    internal static string NextLabel(int index) =>
        IsLast(index) ? "START MAPPING" : "NEXT";
}
