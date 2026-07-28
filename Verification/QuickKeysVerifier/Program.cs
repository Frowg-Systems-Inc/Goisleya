using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var survival = QuickKeysLogic.Present(0, 472);
Check(survival is { ModeIndex: 0, ModeId: "survival", ModeLabel: "SURVIVAL", IsCompact: false }
      && survival.Entries.Count == 5
      && survival.Entries[0] == new QuickKeyEntry("Q HOLD", "SCENT")
      && survival.Entries[^1] == new QuickKeyEntry("TAB", "STATUS"),
    "Survival defaults failed");

var combat = QuickKeysLogic.Present(1, 472);
Check(combat is { ModeId: "combat", ModeLabel: "COMBAT" }
      && combat.Entries.Any(entry => entry.Keys == "ALT + CLICK" && entry.Action == "SPECIES ACTION")
      && combat.Entries.All(entry => !entry.Action.Contains("HITBOX", StringComparison.Ordinal)),
    "Combat restraint failed");

var calls = QuickKeysLogic.Present(2, 472);
Check(calls is { ModeId: "calls", ModeLabel: "CALLS" }
      && calls.Entries.Select(entry => entry.Keys).SequenceEqual(["1", "2", "3", "4", "F"]),
    "Call defaults failed");

var narrow = QuickKeysLogic.Present(0, 420);
var tiny = QuickKeysLogic.Present(0, 260);
Check(narrow.IsCompact && narrow.Entries.Count == 4
      && tiny.IsCompact && tiny.Entries.Count == 3,
    "Responsive trimming failed");
Check(QuickKeysLogic.Present(-1, double.NaN).ModeIndex == 0
      && QuickKeysLogic.Present(99, double.PositiveInfinity).ModeIndex == 0,
    "Invalid-input normalization failed");
Check(QuickKeysLogic.ReferenceSnapshot == "2026-05-29", "Reference snapshot failed");

var root = Directory.GetCurrentDirectory();
var source = string.Join("\n", Directory.GetFiles(Path.Combine(root, "BurntHud"), "MainWindow*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText));
var xaml = File.ReadAllText(Path.Combine(root, "BurntHud", "MainWindow.xaml"));
var links = File.ReadAllText(Path.Combine(root, "BurntHud", "OverlayLinks.cs"));

Check(xaml.Contains("x:Name=\"QuickKeysHudBorder\"", StringComparison.Ordinal)
      && xaml.Contains("IsHitTestVisible=\"False\"", StringComparison.Ordinal)
      && xaml.Contains("Text=\"DEFAULT · REBINDABLE\"", StringComparison.Ordinal)
      && xaml.Contains("x:Name=\"HudQuickKeysButton\"", StringComparison.Ordinal)
      && xaml.Contains("x:Name=\"QuickKeysModeButton\"", StringComparison.Ordinal),
    "Click-through HUD or controls failed");
Check(source.Contains("public bool QuickKeysHudVisible { get; set; }", StringComparison.Ordinal)
      && source.Contains("public int QuickKeysModeIndex { get; set; }", StringComparison.Ordinal)
      && source.Contains("QuickKeysHudVisible = _quickKeysHudVisible", StringComparison.Ordinal)
      && source.Contains("QuickKeysModeIndex = _quickKeysModeIndex", StringComparison.Ordinal)
      && source.Contains("HudSurfaceLogic.Show(_quickKeysHudVisible, _streamerMode)", StringComparison.Ordinal),
    "Persistence or Streamer Mode boundary failed");
Check(source.Contains("new(\"quick-keys\"", StringComparison.Ordinal)
      && source.Contains("case \"quick-keys\":", StringComparison.Ordinal)
      && source.Contains("private void HudQuickKeysButton_Click", StringComparison.Ordinal)
      && source.Contains("private void QuickKeysModeButton_Click", StringComparison.Ordinal),
    "Discovery or interaction failed");
Check(links.Contains("https://www.theisle.info/guide/controls", StringComparison.Ordinal)
      && xaml.Contains("Click=\"OpenControlsGuideButton_Click\"", StringComparison.Ordinal)
      && xaml.Contains("Default key reference only", StringComparison.Ordinal),
    "Update-sensitive reference handoff failed");

Console.WriteLine("Quick Keys: PASS (three modes, responsive trim, default-off persistence, click-through HUD, Streamer Mode, discoverability, and rebindable-source boundary)");
