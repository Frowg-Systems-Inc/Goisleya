using System.IO;
using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var html = """
    <html><script src="map-data.js?v=52"></script></html>
    """;
var assetUri = GatewayResourceClient.ResolveAssetUri(html);
Check(assetUri.AbsoluteUri == "https://myislemap.com/map-data.js?v=52", "versioned resource discovery");

var asset = """
    const MAP_OVERLAYS = {
      resourceGroups: { animals: [{ key: "boar", name: "Boar", count: 1 }] },
      animals: [
        { bucket: "animals", group: "Animal (terrestrial)", key: "boar", name: "Boar",
          x: 200, y: 100.3, updated: "2026/07/18", respawnS: 300, source: "gamefiles" }
      ],
      herbs: [
        { bucket: "herbs", group: "Flowers", key: "fireweed", name: "Fireweed",
          x: 500, y: 501.5, updated: "2026/07/17", source: "gamefiles" }
      ],
      earth: [
        { bucket: "earth", group: "Earthworks", key: "saltrock", name: "Salt Lick",
          x: 100, y: 100.3, updated: "2026/07/18", source: "gamefiles" },
        { bucket: "earth", group: "Earthworks", key: "saltrock", name: "Salt Lick",
          x: 120, y: 100.3, updated: "2026/07/18", source: "gamefiles" },
        { bucket: "earth", group: "Earthworks", key: "mudwallow", name: "Mud Wallow",
          x: 850, y: 1003, updated: "2026/07/16", source: "gamefiles" }
      ]
    };
    window.MAP_OVERLAYS = MAP_OVERLAYS;
    """;
var retrievedAt = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
var network = GatewayResourceClient.ParseJavaScriptAsset(asset, assetUri, retrievedAt);
Check(network.Version == "52", "resource source version");
Check(network.PointCount == 5, "resource sites parsed without group definitions");
Check(network.LatestSiteDate == new DateOnly(2026, 7, 18), "newest site date");
Check(Math.Abs(network.Points.Single(point => point.Key == "mudwallow").Y - 1000) < 0.001,
    "1000x1003 source normalized to mapper space");
Check(network.Points.Single(point => point.Key == "boar").RespawnSeconds == 300,
    "bounded optional source respawn retained");

var nearestSalt = ResourceFinderLogic.Select(network.Points, "salt", 105, 100, 0);
Check(nearestSalt is { Site.Key: "saltrock", MatchCount: 2, SelectedIndex: 0 },
    "salt alias and nearest site");
Check(nearestSalt!.Distance is > 4.9 and < 5.1, "nearest map distance");
var alternateSalt = ResourceFinderLogic.Select(network.Points, "salt lick", 105, 100, 1);
Check(alternateSalt is { Site.X: 120, SelectedIndex: 1, Cardinal: "E" },
    "alternate site and bearing");
Check(ResourceFinderLogic.Select(network.Points, "prey", 0, 0)?.Site.Key == "boar",
    "prey bucket alias");
Check(ResourceFinderLogic.Select(network.Points, "plant", null, null)?.Site.Key == "fireweed",
    "public site selection works while self is offline");
Check(ResourceFinderLogic.Select(network.Points, "unknown thing", 0, 0) is null,
    "unknown search refuses a fabricated result");
Check(ResourceFinderLogic.SuggestedDietQuery(1, DietCoachLogic.Protein, network.Points) == "boar",
    "diet handoff chooses the first mapped food");
Check(ResourceFinderLogic.SuggestedFoodQuery("Schooling Fish") == "fish",
    "food-name alias");
Check(ResourceFinderLogic.ApproachKind(nearestSalt.Site) == "salt"
      && ResourceFinderLogic.ApproachKind(
          network.Points.Single(point => point.Key == "mudwallow")) == "mud"
      && ResourceFinderLogic.ApproachKind(
          network.Points.Single(point => point.Key == "boar")) == "food"
      && ResourceFinderLogic.ApproachKind(new GatewayResourcePoint(
          "earth", "gastro", "Gastrolith", "Earthworks", 1, 1, null, null)) == "gastrolith"
      && ResourceFinderLogic.ApproachKind(null) == "resource",
    "resource approach provenance");

foreach (var invalid in new[]
         {
             "const MAP_OVERLAYS = {}; window.MAP_OVERLAYS = MAP_OVERLAYS;",
             "const MAP_OVERLAYS = { earth: [{ bucket: \"earth\", group: \"Earthworks\", key: \"saltrock\", name: \"Salt Lick\", x: -1, y: 1 }] }; window.MAP_OVERLAYS = MAP_OVERLAYS;",
             "const MAP_OVERLAYS = { earth: [{ bucket: \"other\", group: \"Earthworks\", key: \"saltrock\", name: \"Salt Lick\", x: 1, y: 1 }] }; window.MAP_OVERLAYS = MAP_OVERLAYS;",
             "alert('not data');"
         })
{
    try
    {
        GatewayResourceClient.ParseJavaScriptAsset(invalid, assetUri, retrievedAt);
        throw new InvalidOperationException("invalid resource asset accepted");
    }
    catch (InvalidDataException)
    {
    }
}

try
{
    GatewayResourceClient.ResolveAssetUri(
        "<script src=\"https://example.com/map-data.js?v=1\"></script>");
    throw new InvalidOperationException("cross-origin resource asset accepted");
}
catch (InvalidDataException)
{
}

Console.WriteLine("Gateway Resource Finder: PASS (strict source parsing, aliases, nearest/alternate sites, bearings, diet handoff, and refusal)");

if (args.Contains("--live", StringComparer.OrdinalIgnoreCase))
{
    var live = await GatewayResourceClient.FetchAsync(CancellationToken.None);
    Check(live.PointCount > 100, "live public resource source content");
    Check(live.Points.Any(point => point.Key == "saltrock"), "live salt sites");
    Check(live.Points.Any(point => point.Bucket == "animals"), "live AI-zone sites");
    Console.WriteLine(
        $"Gateway Resource Finder live source: PASS ({live.PointCount} sites, version {live.Version}, newest {live.LatestSiteDate:yyyy-MM-dd})");
}
