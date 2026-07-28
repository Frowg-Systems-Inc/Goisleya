namespace Isley;

internal sealed record HudDockPlan(
    string NavigationSide,
    string IntelSide,
    string SurvivalSide,
    string VoiceSide,
    double IntelBottomInset,
    string Label,
    string Description);

internal static class HudDockLogic
{
    internal const double EdgeInset = 9;
    internal const double DockGap = 6;
    internal const double DefaultVoiceHeight = 46;

    internal static HudDockPlan Resolve(
        bool mirrored,
        bool voiceVisible,
        double voiceHeight,
        double viewportHeight)
    {
        var resolvedVoiceHeight = double.IsFinite(voiceHeight) && voiceHeight > 0
            ? voiceHeight
            : DefaultVoiceHeight;
        var resolvedViewportHeight = double.IsFinite(viewportHeight) && viewportHeight > 0
            ? viewportHeight
            : 436;
        var maximumBottomInset = Math.Max(
            EdgeInset + DefaultVoiceHeight + DockGap,
            resolvedViewportHeight * 0.34);
        var intelBottomInset = voiceVisible
            ? Math.Min(EdgeInset + resolvedVoiceHeight + DockGap, maximumBottomInset)
            : EdgeInset;

        return mirrored
            ? new HudDockPlan(
                "right",
                "left",
                "right",
                "left",
                intelBottomInset,
                "LEFT",
                "Pack and contact left · navigation right · voice clears the intel rail")
            : new HudDockPlan(
                "left",
                "right",
                "left",
                "right",
                intelBottomInset,
                "RIGHT",
                "Navigation left · pack and contact right · voice clears the intel rail");
    }
}
