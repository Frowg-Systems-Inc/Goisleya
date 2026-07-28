using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

Check(
    OverlayZOrderLogic.ShouldHoldAboveGame(
        alwaysOnTop: true,
        windowVisible: true,
        windowLoaded: true),
    "Visible always-on-top overlay must hold above the game");
Check(
    !OverlayZOrderLogic.ShouldHoldAboveGame(
        alwaysOnTop: false,
        windowVisible: true,
        windowLoaded: true),
    "Normal window level must not force HWND_TOPMOST");
Check(
    !OverlayZOrderLogic.ShouldHoldAboveGame(
        alwaysOnTop: true,
        windowVisible: false,
        windowLoaded: true),
    "Hidden overlay must not reassert topmost");
Check(
    !OverlayZOrderLogic.ShouldHoldAboveGame(
        alwaysOnTop: true,
        windowVisible: true,
        windowLoaded: false),
    "Unloaded overlay must not reassert topmost");

var mainWindowSource = string.Join("\n", Directory.GetFiles(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..", "..",
    "BurntHud"), "MainWindow*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText));
var nativeSource = File.ReadAllText(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..", "..",
    "BurntHud", "NativeMethods.cs"));
Check(mainWindowSource.Contains("EnsureOverlayPriority()", StringComparison.Ordinal)
      && mainWindowSource.Contains("EnsureOverlayPriority(forceToggle: true)", StringComparison.Ordinal)
      && mainWindowSource.Contains("NativeMethods.TryReassertTopMost", StringComparison.Ordinal),
    "Main window must reassert native topmost while Always on top is enabled");
Check(nativeSource.Contains("HwndTopMost", StringComparison.Ordinal)
      && nativeSource.Contains("TryReassertTopMost", StringComparison.Ordinal)
      && nativeSource.Contains("SwpNoActivate", StringComparison.Ordinal),
    "Native topmost reassert must avoid stealing focus from The Isle");

Console.WriteLine(
    "Overlay z-order verification passed (always-on-top hold, hide/normal gates, and native reassert wiring).");
