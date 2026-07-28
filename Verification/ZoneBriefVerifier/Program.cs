using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static ZoneBriefSnapshot Baseline(PlayerZone zone) => new(
    LifeRunActive: true,
    StreamerMode: false,
    LiveMapAvailable: true,
    Zone: zone,
    StageIndex: 1,
    SpeciesSelected: true,
    DietClass: "Herbivore",
    DietFilledCount: 0);

var hidden = ZoneBriefLogic.Evaluate(Baseline(PlayerZone.Migration) with { LifeRunActive = false });
Check(!hidden.IsVisible, "Inactive-life hiding failed");
var streamer = ZoneBriefLogic.Evaluate(Baseline(PlayerZone.Migration) with { StreamerMode = true });
Check(!streamer.IsVisible, "Streamer hiding failed");

var outside = ZoneBriefLogic.Evaluate(Baseline(PlayerZone.Outside));
Check(outside is { IsVisible: true, ZoneLabel: "OUTSIDE", ActionId: "layers", RequiresAttention: false }
      && string.IsNullOrEmpty(outside.NextObjective),
    "Outside/manual prompt failed");

var missingSpecies = ZoneBriefLogic.Evaluate(Baseline(PlayerZone.Migration) with { SpeciesSelected = false });
Check(missingSpecies is { ActionId: "field-guide", RequiresAttention: true, NextObjective: "SET SPECIES FOR ZONE" },
    "Missing-species restraint failed");

var juvenileSanctuary = ZoneBriefLogic.Evaluate(Baseline(PlayerZone.Sanctuary));
Check(juvenileSanctuary is { ActionId: "diet-coach", Tone: ZoneBriefTone.Active }
      && juvenileSanctuary.Heading.Contains("THREE-NUTRIENT", StringComparison.Ordinal),
    "Juvenile Sanctuary diet guidance failed");

var carnivoreSanctuary = ZoneBriefLogic.Evaluate(Baseline(PlayerZone.Sanctuary) with { DietClass = "Carnivore" });
Check(carnivoreSanctuary is { ActionId: "fight-check", RequiresAttention: false }
      && carnivoreSanctuary.Detail.Contains("does not guarantee", StringComparison.OrdinalIgnoreCase),
    "Carnivore Sanctuary uncertainty failed");

var lateSanctuary = ZoneBriefLogic.Evaluate(Baseline(PlayerZone.Sanctuary) with { StageIndex = 2 });
Check(lateSanctuary is { Tone: ZoneBriefTone.Warning, RequiresAttention: true, NextObjective: "LEAVE SANCTUARY" }
      && lateSanctuary.Heading.Contains("BEES", StringComparison.Ordinal),
    "Subadult Sanctuary eviction failed");

var herbMigration = ZoneBriefLogic.Evaluate(Baseline(PlayerZone.Migration));
Check(herbMigration is { ActionId: "diet-coach", NextObjective: "FILL MIGRATION DIET" }
      && herbMigration.Detail.Contains("does not auto-fill", StringComparison.OrdinalIgnoreCase),
    "Migration yield honesty failed");
var fullMigration = ZoneBriefLogic.Evaluate(Baseline(PlayerZone.Migration) with { DietFilledCount = 3 });
Check(fullMigration is { ActionId: "layers", NextObjective: "FOLLOW ACTIVE MIGRATION" },
    "Full-diet Migration move-on guidance failed");
var carnivoreMigration = ZoneBriefLogic.Evaluate(Baseline(PlayerZone.Migration) with { DietClass = "Carnivore" });
Check(carnivoreMigration is { ActionId: "fight-check" }
      && carnivoreMigration.Heading.Contains("NOT GUARANTEED", StringComparison.Ordinal),
    "Carnivore Migration uncertainty failed");

var patrol = ZoneBriefLogic.Evaluate(Baseline(PlayerZone.Patrol) with { DietClass = "Carnivore" });
Check(patrol is { ActionId: "layers", NextObjective: "FOLLOW ASSIGNED PATROL" }
      && patrol.Detail.Contains("group-leader", StringComparison.OrdinalIgnoreCase)
      && patrol.Detail.Contains("not a guaranteed player", StringComparison.OrdinalIgnoreCase),
    "Personal/group Patrol behavior failed");

var universal = ZoneBriefLogic.Evaluate(Baseline(PlayerZone.Patrol) with { LiveMapAvailable = false });
Check(universal is { ActionId: "current-zones-guide", ActionLabel: "CURRENT GUIDE" },
    "Universal guide fallback failed");
Check(ZoneBriefLogic.NormalizeZone(99) == PlayerZone.Outside, "Invalid-zone normalization failed");
Check(ZoneBriefLogic.CompactSummary(patrol) == "ZONE PATROL"
      && string.IsNullOrEmpty(ZoneBriefLogic.CompactSummary(outside)),
    "Compact summary failed");

var root = Directory.GetCurrentDirectory();
var source = string.Join("\n", Directory.GetFiles(Path.Combine(root, "BurntHud"), "MainWindow*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText));
var xaml = File.ReadAllText(Path.Combine(root, "BurntHud", "MainWindow.xaml"));
var links = File.ReadAllText(Path.Combine(root, "BurntHud", "OverlayLinks.cs"));

Check(xaml.Split("x:Name=\"ZoneBriefAnchor\"").Length - 1 == 1
      && xaml.Contains("x:Name=\"ZoneBriefActionButton\"", StringComparison.Ordinal)
      && xaml.Contains("Click=\"ZoneBriefZoneButton_Click\"", StringComparison.Ordinal)
      && xaml.IndexOf("x:Name=\"ZoneBriefAnchor\"", StringComparison.Ordinal)
         > xaml.IndexOf("x:Name=\"SpawnPlanAnchor\"", StringComparison.Ordinal),
    "Single nested Zone Brief surface failed");
Check(source.Contains("private ZoneBriefView CurrentZoneBriefView()", StringComparison.Ordinal)
      && source.Contains("private void UpdateZoneBrief(bool force = false)", StringComparison.Ordinal)
      && source.Contains("private async void ZoneBriefActionButton_Click", StringComparison.Ordinal),
    "Zone Brief presentation wiring failed");
Check(source.Contains("new(\"zone-brief\", \"Open Zone Brief\"", StringComparison.Ordinal)
      && source.Contains("case \"zone-brief\":", StringComparison.Ordinal)
      && source.Contains("\"zone-brief\" => _lifeRunActive ? ZoneBriefAnchor : LifeRunSectionAnchor", StringComparison.Ordinal),
    "Quick Command discovery and exact jump failed");
Check(source.Contains("public int CurrentZoneIndex { get; set; }", StringComparison.Ordinal)
      && source.Contains("CurrentZoneIndex = _zoneBriefIndex", StringComparison.Ordinal)
      && source.Contains("saved?.CurrentZoneIndex ?? 0", StringComparison.Ordinal)
      && source.Split("_zoneBriefIndex = 0;").Length - 1 == 2,
    "Per-life persistence or reset failed");
Check(links.Contains("https://www.theisle.info/guide/zones", StringComparison.Ordinal)
      && source.Contains("OpenExternalUri(OverlayLinks.ZonesGuide)", StringComparison.Ordinal)
      && source.Contains("ZoneBriefLogic.CompactSummary(CurrentZoneBriefView())", StringComparison.Ordinal),
    "Current guide or Tactical Brief integration failed");
Check(xaml.Contains("player-reported compass signal", StringComparison.OrdinalIgnoreCase) == false
      && xaml.Contains("Manual per-life signal", StringComparison.Ordinal)
      && xaml.Contains("remain authoritative", StringComparison.Ordinal)
      && !xaml[..xaml.IndexOf("x:Name=\"LifeRunActiveControls\"", StringComparison.Ordinal)]
          .Contains("x:Name=\"ZoneBrief", StringComparison.Ordinal),
    "Truth boundary or permanent-HUD exclusion failed");

Console.WriteLine("Zone Brief: PASS (manual signal, species/stage context, Sanctuary/Migration/Patrol behavior, guide fallback, persistence, Tactical Brief integration, and no permanent map card)");
