using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var full = LiteModeLogic.Resolve(enabled: false);
var lite = LiteModeLogic.Resolve(enabled: true);

Check(!full.Enabled
      && full.MarkerPollMilliseconds == 500
      && full.ControllerRefreshMilliseconds == 250
      && full.PlayFocusMilliseconds == 250
      && full.SurvivalRefreshMilliseconds == 250
      && full.UseShellShadow
      && full.UseContinuousAnimations,
    "Full mode must preserve the fastest authorized map path and complete presentation.");

Check(lite.Enabled
      && lite.MarkerPollMilliseconds == 1000
      && lite.ControllerRefreshMilliseconds == 1000
      && lite.GamePollMilliseconds == 4000
      && lite.PlayFocusMilliseconds == 750
      && lite.SurvivalRefreshMilliseconds == 1000
      && lite.VoiceStatusMilliseconds == 1000
      && !lite.UseShellShadow
      && !lite.UseContinuousAnimations,
    "Lite Mode must reduce recurring native, map, and visual work without slowing voice status.");

Check(lite.MarkerPollMilliseconds <= 1000
      && lite.Status.Contains("all tools available", StringComparison.OrdinalIgnoreCase)
      && lite.Tooltip.Contains("live marker", StringComparison.OrdinalIgnoreCase)
      && lite.Tooltip.Contains("routes", StringComparison.OrdinalIgnoreCase)
      && lite.Tooltip.Contains("vitals", StringComparison.OrdinalIgnoreCase)
      && lite.Tooltip.Contains("voice", StringComparison.OrdinalIgnoreCase)
      && lite.Tooltip.Contains("alerts", StringComparison.OrdinalIgnoreCase),
    "Lite Mode must keep its one-second heading target and clearly preserve core features.");

static double RecurringNativeTicksPerSecond(LiteModeProfile profile) =>
    1000d / profile.GamePollMilliseconds
    + 1000d / profile.PlayFocusMilliseconds
    + 1000d / profile.SurvivalRefreshMilliseconds
    + 1000d / profile.VoiceStatusMilliseconds;

var fullNativeTicks = RecurringNativeTicksPerSecond(full);
var liteNativeTicks = RecurringNativeTicksPerSecond(lite);
var fullMapTicks = 1000d / full.MarkerPollMilliseconds
                   + 1000d / full.ControllerRefreshMilliseconds;
var liteMapTicks = 1000d / lite.MarkerPollMilliseconds
                   + 1000d / lite.ControllerRefreshMilliseconds;
Check(liteNativeTicks <= fullNativeTicks * 0.4
      && liteMapTicks <= fullMapTicks / 3,
    "Lite Mode must materially reduce recurring native and embedded-map wakeups.");

var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
var source = string.Join("\n", Directory.GetFiles(Path.Combine(root, "BurntHud"), "MainWindow*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText)) + "\n" + File.ReadAllText(Path.Combine(root, "BurntHud", "Map", "isley-map-controller.js"));
var xaml = File.ReadAllText(Path.Combine(root, "BurntHud", "MainWindow.xaml"));

Check(source.Contains("public bool LiteModeEnabled", StringComparison.Ordinal)
      && source.Contains("_liteModeEnabled = settings.LiteModeEnabled", StringComparison.Ordinal)
      && source.Contains("LiteModeEnabled = _liteModeEnabled", StringComparison.Ordinal),
    "Lite Mode must round-trip through normal Isley preference storage.");
Check(xaml.Contains("x:Name=\"LiteModeButton\"", StringComparison.Ordinal)
      && xaml.Contains("x:Name=\"LiteModeStatusText\"", StringComparison.Ordinal)
      && xaml.Contains("x:Name=\"BrandNameText\"", StringComparison.Ordinal)
      && source.Contains("LiteModeButton_Click", StringComparison.Ordinal)
      && source.Contains("\"ISLEY · LITE\"", StringComparison.Ordinal)
      && source.Contains("new(\"lite-mode\"", StringComparison.Ordinal),
    "Lite Mode must be visible, toggleable, and discoverable from Quick Commands.");
Check(source.Contains("liteMode = _liteModeEnabled", StringComparison.Ordinal),
    "The native Lite Mode preference must be included in map options.");
Check(source.Contains("applyLiteMode(Boolean(options.liteMode))", StringComparison.Ordinal),
    "The embedded controller must apply the native Lite Mode preference.");
Check(source.Contains("data-isley-lite", StringComparison.Ordinal),
    "The embedded map must expose a reduced-effects Lite Mode selector.");
Check(source.Contains("nextLiteMode ? 1000 : 500", StringComparison.Ordinal),
    "Lite Mode must change the authorized marker target from 0.5 seconds to one second.");
Check(source.Contains("liteMode ? 1000 : 250", StringComparison.Ordinal),
    "Lite Mode must change continuous map maintenance from 250 ms to one second.");
Check(source.Contains("Shell.Effect = profile.UseShellShadow ? ShellShadowEffect : null", StringComparison.Ordinal)
      && source.Contains("_survivalTimerTick.Interval", StringComparison.Ordinal)
      && source.Contains("_playFocusTimer.Interval", StringComparison.Ordinal)
      && source.Contains("shouldPulse = shouldPulse && !_liteModeEnabled", StringComparison.Ordinal),
    "Lite Mode must reduce native visual and dispatcher work as well as map work.");

Console.WriteLine(
    $"Lite Mode verification passed (persistent toggle, 1s heading target, " +
    $"{(1 - liteNativeTicks / fullNativeTicks) * 100:0}% fewer native timer wakeups, " +
    $"{(1 - liteMapTicks / fullMapTicks) * 100:0}% fewer scheduled map/feed wakeups, " +
    "reduced effects, and core-feature preservation).");
