using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var catalog = Enumerable.Range(1, 12).Select(index => $"tool-{index}").ToArray();
var normalizedFavorites = CommandQuickAccessLogic.NormalizeFavorites(
    [" tool-2 ", "TOOL-2", "missing", "", "tool-3", "tool-4", "tool-5", "tool-6",
     "tool-7", "tool-8", "tool-9", "tool-10"],
    catalog);
Check(normalizedFavorites.SequenceEqual(
        ["tool-2", "tool-3", "tool-4", "tool-5", "tool-6", "tool-7", "tool-8", "tool-9"],
        StringComparer.OrdinalIgnoreCase),
    "Favorites must be trimmed, unique, catalog-valid, and bounded");

var added = CommandQuickAccessLogic.ToggleFavorite(["tool-2"], "tool-3", catalog);
Check(added.Changed && added.Added && !added.LimitReached
      && added.Items.SequenceEqual(["tool-3", "tool-2"]),
    "A newly starred command must move to the front");
var removed = CommandQuickAccessLogic.ToggleFavorite(added.Items, "TOOL-2", catalog);
Check(removed.Changed && !removed.Added && !removed.LimitReached
      && removed.Items.SequenceEqual(["tool-3"]),
    "A starred command must be removable case-insensitively");
var full = CommandQuickAccessLogic.ToggleFavorite(normalizedFavorites, "tool-10", catalog);
Check(!full.Changed && !full.Added && full.LimitReached
      && full.Items.SequenceEqual(normalizedFavorites),
    "The favorite limit must refuse silent eviction");
var invalid = CommandQuickAccessLogic.ToggleFavorite(["tool-2"], "missing", catalog);
Check(!invalid.Changed && !invalid.LimitReached && invalid.Items.SequenceEqual(["tool-2"]),
    "Invalid action IDs must never enter Quick Access");

IReadOnlyList<string> recents = [];
foreach (var actionId in catalog.Take(10))
{
    recents = CommandQuickAccessLogic.RecordRecent(recents, actionId, catalog);
}
Check(recents.Count == CommandQuickAccessLogic.MaximumRecents
      && recents[0] == "tool-10"
      && recents[^1] == "tool-3",
    "Recents must stay bounded in most-recent-first order");
recents = CommandQuickAccessLogic.RecordRecent(recents, "tool-7", catalog);
Check(recents[0] == "tool-7" && recents.Count(item => item == "tool-7") == 1,
    "Re-running a command must move it to the front without duplicates");
var unchangedRecents = CommandQuickAccessLogic.RecordRecent(recents, "missing", catalog);
Check(unchangedRecents.SequenceEqual(recents), "Invalid recent IDs must be ignored");

var ordered = CommandQuickAccessLogic.BuildDefaultOrder(
    catalog,
    ["tool-4", "tool-2"],
    ["tool-2", "tool-7", "tool-1"],
    maximumResults: 7);
Check(ordered.SequenceEqual(
        ["tool-4", "tool-2", "tool-7", "tool-1", "tool-3", "tool-5", "tool-6"]),
    "Default results must show favorites, then deduplicated recents, then catalog tools");
Check(CommandQuickAccessLogic.BuildDefaultOrder(catalog, null, null, 0).Count == 0,
    "A non-positive result capacity must be safe");

var root = Directory.GetCurrentDirectory();
var source = string.Join("\n", Directory.GetFiles(Path.Combine(root, "BurntHud"), "MainWindow*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText));
var xaml = File.ReadAllText(Path.Combine(root, "BurntHud", "MainWindow.xaml"));
Check(xaml.Contains("x:Name=\"CommandPaletteClearRecentButton\"", StringComparison.Ordinal)
      && xaml.Contains("Click=\"CommandPaletteClearRecentButton_Click\"", StringComparison.Ordinal)
      && xaml.Contains("STAR TO PIN", StringComparison.Ordinal),
    "Quick Commands must expose clear recent and star guidance");
Check(source.Contains("CommandPaletteFavoriteButton_Click", StringComparison.Ordinal)
      && source.Contains("Content = isFavorite ? \"★\" : \"☆\"", StringComparison.Ordinal)
      && source.Contains("AutomationProperties.SetName", StringComparison.Ordinal)
      && source.Contains("favoriteBonus", StringComparison.Ordinal)
      && source.Contains("recentBonus", StringComparison.Ordinal),
    "Favorite controls must be accessible and influence search ordering");
Check(source.Contains("CommandFavoriteActionIds = _commandFavoriteActionIds.ToList()", StringComparison.Ordinal)
      && source.Contains("CommandRecentActionIds = _commandRecentActionIds.ToList()", StringComparison.Ordinal)
      && source.Contains("NormalizeFavorites(", StringComparison.Ordinal)
      && source.Contains("NormalizeRecents(", StringComparison.Ordinal),
    "Favorites and recents must restore through validated local settings");
Check(source.Contains("_commandFavoriteActionIds.Clear();", StringComparison.Ordinal)
      && source.Contains("_commandRecentActionIds.Clear();", StringComparison.Ordinal)
      && source.Contains("RecordRecentCommandAction(actionId);", StringComparison.Ordinal),
    "Reset and execution must manage the personal quick-access state");

Console.WriteLine(
    "Command Quick Access: PASS (validated persistence, favorites, bounded recents, ordering, discovery, accessibility, and reset)");
