namespace Isley;

internal readonly record struct ResponsiveOverlayPresentation(
    bool IsMicroLayout,
    bool ShowSurvivalDetails,
    string SurvivalDetailAction,
    string SurvivalDetailTooltip,
    double VitalsMinimumWidth,
    double FooterSizeColumnWidth,
    bool StretchToolsDrawer,
    bool ShowToolsDrawerSubtitle,
    bool ShowMapSectionJumpBar,
    double ToolsDrawerTopInset,
    double ToolsDrawerPadding,
    double ToolsBodyTopInset,
    double ToolsHeaderButtonHeight,
    double ToolsCategoryButtonHeight);

internal static class ResponsiveLayoutLogic
{
    internal const double MicroMaximumWidth = 420;
    internal const double MicroMaximumHeight = 440;
    internal const double DefaultWidth = 472;
    internal const double DefaultHeight = 560;

    internal static ResponsiveOverlayPresentation Resolve(
        double viewportWidth,
        double viewportHeight,
        bool requestedSurvivalDetails)
    {
        var width = double.IsFinite(viewportWidth) && viewportWidth > 0
            ? viewportWidth
            : DefaultWidth;
        var height = double.IsFinite(viewportHeight) && viewportHeight > 0
            ? viewportHeight
            : DefaultHeight;
        var isMicroLayout = width <= MicroMaximumWidth || height <= MicroMaximumHeight;

        return new ResponsiveOverlayPresentation(
            isMicroLayout,
            requestedSurvivalDetails && !isMicroLayout,
            isMicroLayout ? "OPEN" : requestedSurvivalDetails ? "LESS" : "MORE",
            isMicroLayout
                ? "Open every recovery instruction in the scrollable Survival Assistant"
                : requestedSurvivalDetails
                    ? "Keep only the urgent recovery action on the map"
                    : "Show all recovery instructions on the map",
            isMicroLayout ? 94 : 132,
            isMicroLayout ? 54 : 70,
            StretchToolsDrawer: isMicroLayout,
            ShowToolsDrawerSubtitle: !isMicroLayout,
            ShowMapSectionJumpBar: !isMicroLayout,
            ToolsDrawerTopInset: isMicroLayout ? 0 : 48,
            ToolsDrawerPadding: isMicroLayout ? 4 : 10,
            ToolsBodyTopInset: isMicroLayout ? 3 : 7,
            ToolsHeaderButtonHeight: isMicroLayout ? 22 : 26,
            ToolsCategoryButtonHeight: isMicroLayout ? 20 : 28);
    }

    internal static string FooterHotkeyStatus(
        ResponsiveOverlayPresentation presentation,
        int registeredCount,
        int enabledCount,
        bool allRegistered,
        bool clickThrough,
        bool capturing,
        string interactionShortcut)
    {
        if (!presentation.IsMicroLayout)
        {
            return capturing
                ? "PRESS SHORTCUT · ESC CANCELS"
                : !allRegistered
                    ? $"KEYS {registeredCount}/{enabledCount} · FIX IN APP"
                    : clickThrough
                        ? $"{interactionShortcut} · INTERACT"
                        : "KEYS READY";
        }

        return capturing
            ? "PRESS KEY"
            : !allRegistered
                ? $"KEYS {registeredCount}/{enabledCount} !"
                : clickThrough
                    ? $"{interactionShortcut} USE"
                    : $"KEYS {enabledCount}/{enabledCount}";
    }
}
