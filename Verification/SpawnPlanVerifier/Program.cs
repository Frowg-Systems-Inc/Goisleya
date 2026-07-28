using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static SpawnPlanSnapshot Baseline() => new(
    LifeRunActive: true,
    StreamerMode: false,
    LiveMapAvailable: true,
    SpeciesSelected: true,
    CoverReady: false,
    ScentChecked: false,
    WaterFound: false,
    FoodFound: false,
    Water: ReportedVitalState.Unknown,
    WaterFresh: false,
    Food: ReportedVitalState.Unknown,
    FoodFresh: false);

var noLife = SpawnPlanLogic.Evaluate(Baseline() with { LifeRunActive = false });
Check(!noLife.IsVisible && noLife.State == SpawnPlanState.Hidden, "Inactive-life redaction failed");

var streamer = SpawnPlanLogic.Evaluate(Baseline() with { StreamerMode = true });
Check(!streamer.IsVisible && streamer.State == SpawnPlanState.Hidden, "Streamer redaction failed");

var setup = SpawnPlanLogic.Evaluate(Baseline() with { SpeciesSelected = false });
Check(setup is { State: SpawnPlanState.Setup, ActionId: "field-guide", CurrentTask: "SET SPECIES" },
    "Species setup priority failed");

var cover = SpawnPlanLogic.Evaluate(Baseline());
Check(cover is { State: SpawnPlanState.Active, Completed: 0, Total: 4, ActionId: "pins", CurrentTask: "SECURE COVER" },
    "Cover-first priority failed");

var scent = SpawnPlanLogic.Evaluate(Baseline() with { CoverReady = true });
Check(scent is { Completed: 1, ActionId: "scent-finder", CurrentTask: "CHECK SCENT" },
    "Scent-second priority failed");

var water = SpawnPlanLogic.Evaluate(Baseline() with { CoverReady = true, ScentChecked = true });
Check(water is { Completed: 2, ActionId: "spawn-water-scent", CurrentTask: "FIND WATER" },
    "Water-third priority failed");

var food = SpawnPlanLogic.Evaluate(Baseline() with
{
    CoverReady = true,
    ScentChecked = true,
    WaterFound = true
});
Check(food is { Completed: 3, ActionId: "diet-coach", CurrentTask: "FIND FOOD" },
    "Food-fourth priority failed");

var complete = SpawnPlanLogic.Evaluate(Baseline() with
{
    CoverReady = true,
    ScentChecked = true,
    WaterFound = true,
    FoodFound = true
});
Check(complete is { State: SpawnPlanState.Complete, Completed: 4, ActionId: "trip-check", CurrentTask: "READY TO TRAVEL" }
      && complete.IsComplete,
    "Completion state failed");

var urgentWater = SpawnPlanLogic.Evaluate(Baseline() with
{
    Water = ReportedVitalState.Low,
    WaterFresh = true
});
Check(urgentWater is { CurrentTask: "FIND WATER", ActionId: "spawn-water-scent" }
      && urgentWater.Heading == "WATER FIRST",
    "Fresh low-water override failed");

var urgentFood = SpawnPlanLogic.Evaluate(Baseline() with
{
    Food = ReportedVitalState.Empty,
    FoodFresh = true
});
Check(urgentFood is { CurrentTask: "FIND FOOD", ActionId: "diet-coach" }
      && urgentFood.Heading == "FOOD FIRST",
    "Fresh empty-food override failed");

var staleWater = SpawnPlanLogic.Evaluate(Baseline() with
{
    Water = ReportedVitalState.Empty,
    WaterFresh = false
});
Check(staleWater.CurrentTask == "SECURE COVER", "Stale-vital restraint failed");

var universalCover = SpawnPlanLogic.Evaluate(Baseline() with { LiveMapAvailable = false });
Check(universalCover.ActionId == "current-first-hour-guide"
      && !universalCover.ActionId.Contains("pins", StringComparison.Ordinal),
    "Universal cover fallback failed");

var universalWater = SpawnPlanLogic.Evaluate(Baseline() with
{
    LiveMapAvailable = false,
    CoverReady = true,
    ScentChecked = true
});
Check(universalWater.ActionId == "current-first-hour-guide", "Universal water fallback failed");

var universalComplete = SpawnPlanLogic.Evaluate(Baseline() with
{
    LiveMapAvailable = false,
    CoverReady = true,
    ScentChecked = true,
    WaterFound = true,
    FoodFound = true
});
Check(universalComplete.ActionId == "current-first-hour-guide", "Universal completion fallback failed");
Check(SpawnPlanLogic.CompactSummary(complete) == "SPAWN 4/4", "Compact summary failed");

var root = Directory.GetCurrentDirectory();
var source = string.Join("\n", Directory.GetFiles(Path.Combine(root, "BurntHud"), "MainWindow*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText));
var xaml = File.ReadAllText(Path.Combine(root, "BurntHud", "MainWindow.xaml"));
var links = File.ReadAllText(Path.Combine(root, "BurntHud", "OverlayLinks.cs"));

Check(xaml.Split("x:Name=\"SpawnPlanAnchor\"").Length - 1 == 1
      && xaml.Contains("x:Name=\"SpawnPlanHeadingText\"", StringComparison.Ordinal)
      && xaml.Contains("x:Name=\"SpawnPlanActionButton\"", StringComparison.Ordinal)
      && xaml.IndexOf("x:Name=\"SpawnPlanAnchor\"", StringComparison.Ordinal)
         > xaml.IndexOf("x:Name=\"LifeRunActiveControls\"", StringComparison.Ordinal),
    "Single nested Spawn Plan surface failed");
Check(xaml.IndexOf("x:Name=\"SpawnPlanAnchor\"", StringComparison.Ordinal)
      < xaml.IndexOf("Click=\"LifeRunMilestoneButton_Click\"", StringComparison.Ordinal),
    "Spawn Plan hierarchy failed");
Check(source.Contains("private SpawnPlanView CurrentSpawnPlanView()", StringComparison.Ordinal)
      && source.Contains("private void UpdateSpawnPlan(bool force = false)", StringComparison.Ordinal)
      && source.Contains("private void SpawnPlanTaskButton_Click", StringComparison.Ordinal)
      && source.Contains("private async void SpawnPlanActionButton_Click", StringComparison.Ordinal),
    "Spawn Plan presentation wiring failed");
Check(source.Contains("new(\"spawn-plan\", \"Open Spawn Plan\"", StringComparison.Ordinal)
      && source.Contains("case \"spawn-plan\":", StringComparison.Ordinal)
      && source.Contains("\"spawn-plan\" => _lifeRunActive ? SpawnPlanAnchor : LifeRunSectionAnchor", StringComparison.Ordinal),
    "Spawn Plan discovery and exact jump failed");
Check(source.Contains("if (spawnPlan.IsVisible && !spawnPlan.IsComplete)", StringComparison.Ordinal)
      && source.Contains("return spawnPlan.CurrentTask;", StringComparison.Ordinal)
      && source.Contains("var spawnPlanGuidesNext = spawnPlan.IsVisible && !spawnPlan.IsComplete;", StringComparison.Ordinal)
      && source.Contains("UpdateNextMove(force: true);", StringComparison.Ordinal),
    "Next Move or compact HUD integration failed");
Check(source.Contains("public bool SpawnCoverReady { get; set; }", StringComparison.Ordinal)
      && source.Contains("SpawnCoverReady = _spawnPlanCoverReady", StringComparison.Ordinal)
      && source.Contains("saved?.SpawnCoverReady == true", StringComparison.Ordinal)
      && source.Split("_spawnPlanCoverReady = false;").Length - 1 == 2,
    "Per-life persistence or reset failed");
Check(links.Contains("https://www.theisle.info/guide/how-to-play", StringComparison.Ordinal)
      && source.Contains("OpenExternalUri(OverlayLinks.FirstHourGuide)", StringComparison.Ordinal)
      && source.Contains("LiveMapServicesActive", StringComparison.Ordinal),
    "Current guide or server-aware handoff failed");
Check(xaml.Contains("Manual per-life checks", StringComparison.Ordinal)
      && xaml.Contains("remain authoritative", StringComparison.Ordinal)
      && !xaml[..xaml.IndexOf("x:Name=\"LifeRunActiveControls\"", StringComparison.Ordinal)]
          .Contains("x:Name=\"SpawnPlan", StringComparison.Ordinal),
    "Truth boundary or permanent-HUD exclusion failed");

var commandStart = source.IndexOf("CommandPaletteActions =", StringComparison.Ordinal);
var commandEnd = source.IndexOf("private static readonly", commandStart + 1, StringComparison.Ordinal);
var commandBlock = source[commandStart..commandEnd];
Check(commandBlock.Split("new(\"").Length - 1 == 111, "Quick Command catalog count failed");

Console.WriteLine("Spawn Plan: PASS (species, cover, scent, water, food, fresh-vital priority, universal fallback, persistence, HUD/Next Move integration, and no permanent map card)");
