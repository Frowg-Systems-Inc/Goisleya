namespace Isley;

internal readonly record struct HudSurfacePreferences(
    bool Navigation,
    bool Vitals,
    bool Pack,
    bool Encounters,
    bool Survival,
    bool Voice,
    bool Alerts,
    bool Nearby,
    bool Aim,
    bool QuickKeys);

internal readonly record struct HudSurfacePresentation(
    int EnabledCount,
    int TotalCount,
    string Status,
    bool PrivacyHidden);

internal static class HudSurfaceLogic
{
    internal const int SurfaceCount = 10;

    internal static bool Show(bool preference, bool streamerMode) =>
        preference && !streamerMode;

    internal static HudSurfacePresentation Present(
        HudSurfacePreferences preferences,
        bool streamerMode)
    {
        var enabledCount = new[]
        {
            preferences.Navigation,
            preferences.Vitals,
            preferences.Pack,
            preferences.Encounters,
            preferences.Survival,
            preferences.Voice,
            preferences.Alerts,
            preferences.Nearby,
            preferences.Aim,
            preferences.QuickKeys
        }.Count(enabled => enabled);

        return streamerMode
            ? new(
                enabledCount,
                SurfaceCount,
                "PRIVACY HIDES MAP HUD · PREFERENCES PRESERVED",
                true)
            : new(
                enabledCount,
                SurfaceCount,
                $"{enabledCount} / {SurfaceCount} ON · HIDDEN VISUALS KEEP THEIR TOOLS RUNNING",
                false);
    }
}
