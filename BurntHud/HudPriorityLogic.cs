namespace Isley;

internal readonly record struct HudPriorityContext(
    bool Enabled,
    double ViewportWidth,
    double ViewportHeight,
    bool SurvivalActive,
    bool MarkerAvailable,
    bool VoiceActive,
    bool VoiceProblem);

internal readonly record struct HudPriorityPresentation(
    bool IsCompactViewport,
    bool IsSafetyFocusActive,
    bool HideAmbientHud,
    bool HideWaitingNavigation,
    bool CompactPackHud,
    bool SuppressIdleVoice,
    string Status,
    string Tooltip);

internal static class HudPriorityLogic
{
    internal const double CompactWidth = 520;
    internal const double CompactHeight = 620;
    internal const double DefaultWidth = 472;
    internal const double DefaultHeight = 560;

    internal static HudPriorityPresentation Resolve(HudPriorityContext context)
    {
        var width = double.IsFinite(context.ViewportWidth) && context.ViewportWidth > 0
            ? context.ViewportWidth
            : DefaultWidth;
        var height = double.IsFinite(context.ViewportHeight) && context.ViewportHeight > 0
            ? context.ViewportHeight
            : DefaultHeight;
        var compactViewport = width <= CompactWidth || height <= CompactHeight;
        var safetyFocus = context.Enabled && compactViewport && context.SurvivalActive;

        if (!context.Enabled)
        {
            return new HudPriorityPresentation(
                compactViewport,
                false,
                false,
                false,
                false,
                false,
                "Manual · enabled HUD cards keep their normal detail",
                "Smart HUD is off; HUD detail and individual visibility controls remain authoritative");
        }

        if (!safetyFocus)
        {
            return new HudPriorityPresentation(
                compactViewport,
                false,
                false,
                false,
                false,
                false,
                compactViewport
                    ? "Ready · urgent guidance can fold ambient cards"
                    : "Ready · full layout has room for normal HUD detail",
                "Smart HUD activates only on compact layouts while a survival condition is active");
        }

        var suppressIdleVoice = !context.VoiceActive && !context.VoiceProblem;
        return new HudPriorityPresentation(
            compactViewport,
            true,
            true,
            !context.MarkerAvailable,
            true,
            suppressIdleVoice,
            "Safety focus · urgent guidance has the map",
            "Ambient run and field cards fold; pack detail compacts; offline navigation and idle voice yield to the active survival alert");
    }
}
