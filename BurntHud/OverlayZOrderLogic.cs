namespace Isley;

/// <summary>
/// Decides when Isley should reassert HWND_TOPMOST so the overlay stays above
/// The Isle on machines where WPF Topmost alone is not enough.
/// </summary>
internal static class OverlayZOrderLogic
{
    internal static bool ShouldHoldAboveGame(
        bool alwaysOnTop,
        bool windowVisible,
        bool windowLoaded) =>
        alwaysOnTop && windowVisible && windowLoaded;
}
