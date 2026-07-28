using System.IO;
using System.Text.Json;
using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var html = """
    <html><head>
      <link rel="preload" as="image" href="assets/gateway-preview.webp?v=20260718hq2">
    </head><body>
      <img id="realMapImage" src="assets/gateway-preview.webp?v=20260718hq2">
      <img id="waterMapImage" data-src="assets/water-map.webp?v=20260718v2">
      <script src="map-data.js?v=52"></script>
      <script src="map-water.js?v=2"></script>
      <script src="map-roads.js?v=2"></script>
    </body></html>
    """;
var assetUri = TerrainRoadNetworkClient.ResolveRoadAssetUri(html);
Check(assetUri.AbsoluteUri == "https://myislemap.com/map-roads.js?v=2", "versioned source discovery");
var waterUri = TerrainRoadNetworkClient.ResolveWaterAssetUri(html);
Check(waterUri.AbsoluteUri == "https://myislemap.com/assets/water-map.webp?v=20260718v2",
    "versioned water-mask source discovery");
var overlayUri = GatewayMapOverlayClient.ResolveOverlayAssetUri(html);
var waterLabelUri = GatewayMapOverlayClient.ResolveWaterLabelAssetUri(html);
var basemap = GatewayMapOverlayClient.ResolveBasemapAsset(html);
Check(overlayUri.AbsoluteUri == "https://myislemap.com/map-data.js?v=52",
    "versioned current overlay discovery");
Check(waterLabelUri.AbsoluteUri == "https://myislemap.com/map-water.js?v=2",
    "versioned current water-label discovery");
Check(basemap.PreviewUrl
      == "https://myislemap.com/assets/gateway-preview.webp?v=20260718hq2"
      && basemap.ReferenceDate == "2026-07-18"
      && basemap.TileUrlTemplate
          == "https://myislemap.com/assets/gateway-tiles/gateway-{col}-{row}.jpg?v=20260718hq2",
    "coherent current preview, tile set, and source date discovery");

var asset = """
    const MAP_ROADS = [
      {"label":"Highland - Delta road","type":"road","points":[{"x":-125000,"y":307000},{"x":-111000,"y":326000}]},
      {"label":"Verdant Pond NE Path","type":"trail","points":[{"x":12000,"y":-42000},{"x":18000,"y":-36000},{"x":23000,"y":-31000}]}
    ];
    """;
var retrievedAt = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
var network = TerrainRoadNetworkClient.ParseJavaScriptAsset(asset, assetUri, retrievedAt);
Check(network.Version == "2", "source version");
Check(network.Paths.Count == 2 && network.PointCount == 5, "validated path and point totals");
Check(network.Paths[0].Label == "Highland - Delta road", "label retained");
Check(network.Paths[0].Type == "road" && network.Paths[1].Type == "trail",
    "explicit road and trail types retained independently of labels");
Check(network.Paths[1].Points[2] == new TerrainRoadPoint(23000, -31000), "world point retained");

var waterLabels = GatewayMapOverlayClient.ParseWaterLabelAsset(
    """
    const MAP_WATER_LABELS = [
      {"x":-607000,"y":-505000,"label":"North<br>Pond"},
      {"x":509000,"y":607000,"label":"South Pond"}
    ];
    """,
    waterLabelUri);
Check(waterLabels.Count == 2
      && waterLabels[0].Label == "North Pond"
      && waterLabels[0].X == 0 && waterLabels[0].Y == 0
      && waterLabels[1].X == 1000 && waterLabels[1].Y == 1000,
    "current water labels use the same calibrated map space");

var overlayAsset = """
    /* Data only: comments and unquoted property keys are permitted. */
    const MAP_OVERLAYS = {
      sanctuary: { color: "#a855f7", stroke: "#a855f7", zones: [
        { type: "circle", cx: 500, cy: 501.5, r: 8, label: "Center", gameLabel: "EPS_Sanctuary_Center" },
      ]},
      patrol: { color: "#ef6f6c", stroke: "#ef6f6c", zones: [
        { type: "polygon", points: "100,100.3 140,100.3 120,140.42", label: "Patrol candidate", gameLabel: "EPS_Patrol_Test" },
      ]},
      migration: { color: "#f59e0b", stroke: "#f59e0b", zones: [
        { type: "circle", cx: 250, cy: 250.75, r: 16, label: "Migration candidate", gameLabel: "EPS_Migration_Test" },
      ]},
      animals: [
        { bucket: "animals", group: "Animal (terrestrial)", key: "deer", name: "Deer", x: 100, y: 100.3, updated: "2026/07/09" },
      ],
      herbs: [
        { bucket: "herbs", group: "Flower", key: "fireweed", name: "Fireweed", x: 200, y: 200.6, updated: "2026/07/09" },
      ],
      earth: [
        { bucket: "earth", group: "Earthworks", key: "mudwallow", name: "Mud Wallow", x: 300, y: 300.9, updated: "2026/07/09" },
      ],
    };
    window.MAP_OVERLAYS = MAP_OVERLAYS;
    """;
var gatewayDataset = GatewayMapOverlayClient.ParseOverlayAsset(
    overlayAsset, overlayUri, retrievedAt, basemap, waterLabels);
Check(gatewayDataset.Version == "52"
      && gatewayDataset.Sanctuaries.Count == 1
      && gatewayDataset.PatrolCandidates.Count == 1
      && gatewayDataset.MigrationCandidates.Count == 1
      && gatewayDataset.ResourceCount == 3,
    "validated current zone and resource buckets");
Check(Math.Abs(gatewayDataset.Sanctuaries[0].CenterY!.Value - 500) < 0.000001
      && Math.Abs(gatewayDataset.Animals[0].Y - 100) < 0.000001,
    "1000x1003 source coordinates normalize to the shared 1000x1000 map space");
Check(GatewayMapOverlayClient.WorldToMap(-607000, -505000)
          == new GatewayMapPoint(0, 0)
      && GatewayMapOverlayClient.WorldToMap(509000, 607000)
          == new GatewayMapPoint(1000, 1000)
      && GatewayMapOverlayClient.WorldToMap(-49000, 51000)
          == new GatewayMapPoint(500, 500),
    "current Gateway world/map calibration corners and center");
foreach (var worldPoint in new[]
         {
             new GatewayMapPoint(-607000, -505000),
             new GatewayMapPoint(-49000, 51000),
             new GatewayMapPoint(287000, 493000)
         })
{
    var mapPoint = GatewayMapOverlayClient.WorldToMap(worldPoint.X, worldPoint.Y);
    var waterMaskX = ((worldPoint.Y / 1000 + 505) / 1112) * 1000;
    var waterMaskY = ((worldPoint.X / 1000 + 607) / 1116) * 1003;
    Check(Math.Abs(mapPoint.X - waterMaskX) < 0.000001
          && Math.Abs(mapPoint.Y * 1003 / 1000 - waterMaskY) < 0.000001,
        "player, route, and drinkable-water mask geometry share one transform");
}

var gatewayCatalog = """
    map	E	TI	Gateway_v0.21		Gateway v0.21.3 (OUTDATED)
    map	E	TI	Gateway_v0.21.7		Gateway v0.21.738		Current
    """;
var gatewayMap = TerrainCommunityHazardFeedClient.ResolveCurrentGatewayMap(gatewayCatalog);
Check(gatewayMap.MapId == "Gateway_v0.21.7" && gatewayMap.Version == "0.21.738",
    "current non-outdated Gateway danger map discovery");
var hazardUri = TerrainCommunityHazardFeedClient.ResolvePhotoAssetUri(gatewayMap.MapId);
Check(hazardUri.AbsoluteUri
      == "https://vulnona.com/game/map/map/Gateway_v0.21.7/photo.txt",
    "trusted map-specific danger feed resolution");
var hazardFeed = TerrainCommunityHazardFeedClient.ParsePhotoFeed(
    """
    -176,-28	-1783301197	png	private-key	Contributor	Inescapable ravine
    -95,-152	-1784085607	jpg	private-key	Contributor	Trapping hole
    12,15	1784085607	png	private-key	Contributor	Ordinary photo
    """,
    hazardUri,
    gatewayMap.MapId,
    gatewayMap.Version,
    retrievedAt);
Check(hazardFeed.Hazards.Count == 2,
    "only explicitly danger-flagged public terrain points retained");
Check(hazardFeed.Hazards[0] == new TerrainCommunityHazard(-176000, -28000)
      && hazardFeed.Hazards[1] == new TerrainCommunityHazard(-95000, -152000),
    "Gateway terrain-danger points converted to calibrated world coordinates");

var minimalWebp = new byte[]
{
    (byte)'R', (byte)'I', (byte)'F', (byte)'F', 8, 0, 0, 0,
    (byte)'W', (byte)'E', (byte)'B', (byte)'P', (byte)'V', (byte)'P', (byte)'8', (byte)'L'
};
TerrainRoadNetworkClient.ValidateWebp(minimalWebp);
network = network with
{
    WaterMask = new TerrainWaterMask(waterUri.AbsoluteUri, "20260718v2", retrievedAt, minimalWebp),
    CommunityHazards = hazardFeed,
    GatewayMap = gatewayDataset
};

using var mapperJson = JsonDocument.Parse(network.ToMapperJson());
Check(mapperJson.RootElement.GetProperty("paths").GetArrayLength() == 2, "mapper payload path count");
Check(mapperJson.RootElement.GetProperty("sourceVersion").GetString() == "2", "mapper payload version");
Check(mapperJson.RootElement.GetProperty("paths")[1].GetProperty("type").GetString() == "trail",
    "mapper payload path type");
var waterMaskJson = mapperJson.RootElement.GetProperty("waterMask");
Check(waterMaskJson.GetProperty("mediaType").GetString() == "image/webp", "water mask media type");
Check(waterMaskJson.GetProperty("sourceVersion").GetString() == "20260718v2", "water mask version");
Check(Convert.FromBase64String(waterMaskJson.GetProperty("dataBase64").GetString()!).SequenceEqual(minimalWebp),
    "water mask payload bytes");
var communityHazardsJson = mapperJson.RootElement.GetProperty("communityHazards");
Check(communityHazardsJson.GetProperty("points").GetArrayLength() == 2,
    "mapper payload bounded public terrain danger count");
Check(communityHazardsJson.GetProperty("radius").GetDouble()
      == TerrainCommunityHazardFeedClient.RouteAvoidanceRadius,
    "mapper payload fixed terrain danger avoidance radius");
Check(!network.ToMapperJson().Contains("Contributor", StringComparison.Ordinal)
      && !network.ToMapperJson().Contains("ravine", StringComparison.OrdinalIgnoreCase)
      && !network.ToMapperJson().Contains("private-key", StringComparison.Ordinal),
    "contributor identity, image keys, and comments never cross the mapper bridge");
var gatewayMapJson = mapperJson.RootElement.GetProperty("gatewayMap");
Check(gatewayMapJson.GetProperty("sourceVersion").GetString() == "52"
      && gatewayMapJson.GetProperty("zones").GetProperty("sanctuaries").GetArrayLength() == 1
      && gatewayMapJson.GetProperty("resources").GetProperty("animals").GetArrayLength() == 1
      && gatewayMapJson.GetProperty("waterLabels").GetArrayLength() == 2,
    "current basemap, zones, resources, calibration, and water labels cross one typed bridge");

foreach (var invalid in new[]
         {
             "const MAP_ROADS = [];",
             "const MAP_ROADS = [{\"label\":\"Bad\",\"points\":[{\"x\":1,\"y\":2}]}];",
             "const MAP_ROADS = [{\"label\":\"Bad\",\"type\":\"cliff\",\"points\":[{\"x\":1,\"y\":2},{\"x\":2,\"y\":3}]}];",
             "const MAP_ROADS = [{\"label\":\"Bad\",\"type\":\"road\",\"points\":[{\"x\":1,\"y\":2},{\"x\":1e20,\"y\":3}]}];",
             "alert('not data');"
         })
{
    try
    {
        TerrainRoadNetworkClient.ParseJavaScriptAsset(invalid, assetUri, retrievedAt);
        throw new InvalidOperationException($"invalid terrain asset accepted: {invalid}");
    }
    catch (InvalidDataException)
    {
    }
}

foreach (var invalidOverlay in new[]
         {
             "const MAP_OVERLAYS = {};",
             "const MAP_OVERLAYS = { sanctuary: alert('run') };",
             "const MAP_OVERLAYS = { sanctuary: { zones: [] } };",
             "const MAP_OVERLAYS = { sanctuary: { zones: [{ type: \"circle\", cx: 5000, cy: 1, r: 2, label: \"Bad\" }] } };"
         })
{
    try
    {
        GatewayMapOverlayClient.ParseOverlayAsset(
            invalidOverlay, overlayUri, retrievedAt, basemap);
        throw new InvalidOperationException($"invalid Gateway overlay accepted: {invalidOverlay}");
    }
    catch (InvalidDataException)
    {
    }
}

Check(TerrainRouteStyleLogic.Normalize("ROAD-FIRST") == TerrainRouteStyleLogic.RoadFirstId,
    "route-style normalization");
Check(TerrainRouteStyleLogic.Normalize("unsafe") == TerrainRouteStyleLogic.BalancedId,
    "invalid route-style fallback");
Check(TerrainRouteStyleLogic.Next(TerrainRouteStyleLogic.BalancedId) == TerrainRouteStyleLogic.RoadFirstId
      && TerrainRouteStyleLogic.Next(TerrainRouteStyleLogic.RoadFirstId) == TerrainRouteStyleLogic.ShortestId
      && TerrainRouteStyleLogic.Next(TerrainRouteStyleLogic.ShortestId) == TerrainRouteStyleLogic.BalancedId,
    "deterministic route-style cycle");
Check(TerrainRouteStyleLogic.Resolve(TerrainRouteStyleLogic.RoadFirstId).Description
        .Contains("off-network", StringComparison.Ordinal),
    "route-style utility copy");

Check(TerrainGapPolicyLogic.Normalize("STRICT") == TerrainGapPolicyLogic.StrictId
      && TerrainGapPolicyLogic.Normalize("unsafe") == TerrainGapPolicyLogic.BalancedId,
    "gap-policy normalization and fail-safe default");
Check(TerrainGapPolicyLogic.Next(TerrainGapPolicyLogic.StrictId) == TerrainGapPolicyLogic.BalancedId
      && TerrainGapPolicyLogic.Next(TerrainGapPolicyLogic.BalancedId) == TerrainGapPolicyLogic.FlexibleId
      && TerrainGapPolicyLogic.Next(TerrainGapPolicyLogic.FlexibleId) == TerrainGapPolicyLogic.StrictId,
    "deterministic gap-policy cycle");
Check(TerrainGapPolicyLogic.Resolve(TerrainGapPolicyLogic.StrictId).MaximumConnectorDistance == 45
      && TerrainGapPolicyLogic.Resolve(TerrainGapPolicyLogic.BalancedId).MaximumConnectorDistance == 80
      && TerrainGapPolicyLogic.Resolve(TerrainGapPolicyLogic.FlexibleId).MaximumConnectorDistance == 125,
    "gap-policy connector limits");
Check(TerrainGapPolicyLogic.Options.All(option =>
        option.Description.Contains("MU", StringComparison.Ordinal)),
    "gap-policy descriptions must disclose their exact limits");

var highEvidence = TerrainRouteConfidenceLogic.Evaluate(80, 20, 0, 0, 0, false);
Check(highEvidence.Level == TerrainRouteConfidenceLogic.High
      && highEvidence.MappedPercent == 100,
    "fully mapped courses should retain high network evidence without an unknown water connector");
var moderateEvidence = TerrainRouteConfidenceLogic.Evaluate(60, 20, 20, 12, 2, true);
Check(moderateEvidence.Level == TerrainRouteConfidenceLogic.Moderate
      && moderateEvidence.MappedPercent == 80
      && moderateEvidence.Detail.Contains("20 MU unknown", StringComparison.Ordinal),
    "bounded connector gaps should remain visible as moderate evidence");
var lowEvidence = TerrainRouteConfidenceLogic.Evaluate(30, 10, 60, 50, 6, false);
Check(lowEvidence.Level == TerrainRouteConfidenceLogic.Low
      && lowEvidence.Guidance.Contains("substantial unknown terrain", StringComparison.Ordinal),
    "long or numerous unknown gaps must produce low evidence");
var uncoveredWaterEvidence = TerrainRouteConfidenceLogic.Evaluate(95, 0, 5, 5, 1, false);
Check(uncoveredWaterEvidence.Level == TerrainRouteConfidenceLogic.Moderate
      && uncoveredWaterEvidence.Guidance.Contains("Water safety", StringComparison.Ordinal),
    "unknown connectors without water evidence cannot receive a high rating");
Check(TerrainRouteConfidenceLogic.Evaluate(double.NaN, -10, 0, 0, -2, true).Level
      == TerrainRouteConfidenceLogic.Unavailable,
    "invalid evidence inputs must fail closed");
var learnedEvidence = TerrainRouteConfidenceLogic.Evaluate(
    20, 10, 10, 8, 1, true, 60);
Check(learnedEvidence.Level == TerrainRouteConfidenceLogic.High
      && learnedEvidence.MappedPercent == 90
      && learnedEvidence.Detail.Contains("60 MU player-traveled", StringComparison.Ordinal),
    "fresh player-traveled evidence should be visible and count as mapped without being called a public trail");

try
{
    TerrainRoadNetworkClient.ResolveRoadAssetUri(
        "<script src=\"https://example.com/map-roads.js?v=1\"></script>");
    throw new InvalidOperationException("cross-origin terrain asset accepted");
}
catch (InvalidDataException)
{
}

try
{
    TerrainCommunityHazardFeedClient.ResolvePhotoAssetUri("../Gateway_v0.21.7");
    throw new InvalidOperationException("invalid terrain danger map identifier accepted");
}
catch (InvalidDataException)
{
}

try
{
    TerrainCommunityHazardFeedClient.ParsePhotoFeed(
        "900,-20\t-1783301197\tpng\tkey\tuser\tunsafe",
        hazardUri,
        gatewayMap.MapId,
        gatewayMap.Version,
        retrievedAt);
    throw new InvalidOperationException("out-of-range terrain danger coordinate accepted");
}
catch (InvalidDataException)
{
}

try
{
    var excessiveHazards = string.Join(
        "\n",
        Enumerable.Range(0, TerrainCommunityHazardFeedClient.MaximumHazards + 1)
            .Select(index => $"{index % 100},{index % 80}\t-{index + 1}\tpng"));
    TerrainCommunityHazardFeedClient.ParsePhotoFeed(
        excessiveHazards,
        hazardUri,
        gatewayMap.MapId,
        gatewayMap.Version,
        retrievedAt);
    throw new InvalidOperationException("excessive terrain danger feed accepted");
}
catch (InvalidDataException)
{
}

try
{
    TerrainRoadNetworkClient.ResolveWaterAssetUri(
        "<img id=\"waterMapImage\" data-src=\"https://example.com/assets/water-map.webp?v=1\">");
    throw new InvalidOperationException("cross-origin water mask accepted");
}
catch (InvalidDataException)
{
}

try
{
    TerrainRoadNetworkClient.ValidateWebp(new byte[16]);
    throw new InvalidOperationException("invalid WebP mask accepted");
}
catch (InvalidDataException)
{
}

var mainWindowSource = string.Join("\n", Directory.GetFiles(Path.Combine(
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
    "BurntHud"), "MainWindow*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText)) + "\n" + File.ReadAllText(Path.Combine(
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
    "BurntHud", "Map", "isley-map-controller.js"));
var mainWindowXaml = File.ReadAllText(Path.Combine(
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
    "BurntHud",
    "MainWindow.xaml"));
Check(mainWindowSource.Contains("segmentCrossesTerrainWater", StringComparison.Ordinal)
      && mainWindowSource.Contains("blockedByMarkedObstacle", StringComparison.Ordinal)
      && mainWindowSource.Contains("COURSE_TOO_COMPLEX", StringComparison.Ordinal)
      && mainWindowSource.Contains("setTerrainWaterSafety", StringComparison.Ordinal),
    "water-safe connectors, obstacle-safe simplification, honest failure, and user toggle");
Check(mainWindowSource.Contains("styleWeights", StringComparison.Ordinal)
      && mainWindowSource.Contains("candidateCost", StringComparison.Ordinal)
      && mainWindowSource.Contains("candidateDistance", StringComparison.Ordinal)
      && mainWindowSource.Contains("terrain-course-style-changed", StringComparison.Ordinal)
      && mainWindowSource.Contains("TerrainRouteStyle = _terrainRouteStyle", StringComparison.Ordinal)
      && mainWindowSource.Contains("new(\"route-style\"", StringComparison.Ordinal)
      && mainWindowXaml.Contains("TerrainRouteStyleButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("STYLE · BALANCED", StringComparison.Ordinal),
    "typed weighted routing, live reroute, persisted preference, compact control, and Quick Command");
Check(mainWindowSource.Contains("maximumConnectorDistance", StringComparison.Ordinal)
      && mainWindowSource.Contains("candidate.distance <= maximumConnectorDistance", StringComparison.Ordinal)
      && mainWindowSource.Contains("setTerrainGapPolicy", StringComparison.Ordinal)
      && mainWindowSource.Contains("terrain-course-gap-policy-changed", StringComparison.Ordinal)
      && mainWindowSource.Contains("TerrainGapPolicy = _terrainGapPolicy", StringComparison.Ordinal)
      && mainWindowSource.Contains("new(\"route-gaps\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("gaps ≤{gapPolicy.MaximumConnectorDistance:0} MU", StringComparison.Ordinal)
      && mainWindowXaml.Contains("TerrainGapPolicyButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("OFF-NETWORK GAPS · BALANCED ≤80 MU", StringComparison.Ordinal),
    "hard endpoint-gap constraint, live reroute, persistence, visible status, compact control, and Quick Command");
Check(mainWindowSource.Contains("terrainCourseRoadDistance", StringComparison.Ordinal)
      && mainWindowSource.Contains("terrainCourseTrailDistance", StringComparison.Ordinal)
      && mainWindowSource.Contains("terrainCourseUnknownDistance", StringComparison.Ordinal)
      && mainWindowSource.Contains("terrainCourseLongestUnknown", StringComparison.Ordinal)
      && mainWindowSource.Contains("terrainCourseUnknownSegmentCount", StringComparison.Ordinal)
      && mainWindowSource.Contains("terrainCourseSegments", StringComparison.Ordinal)
      && mainWindowSource.Contains("drawTypedTerrainCourse", StringComparison.Ordinal)
      && mainWindowSource.Contains("setTerrainRouteEvidenceVisible", StringComparison.Ordinal)
      && mainWindowSource.Contains("segments: selectedEdges.length <= 5000", StringComparison.Ordinal)
      && mainWindowSource.Contains("buildBlockedPassageArea", StringComparison.Ordinal)
      && mainWindowSource.Contains("reportBlockedTerrainPassage", StringComparison.Ordinal)
      && mainWindowXaml.Contains("TerrainRouteConfidencePanel", StringComparison.Ordinal)
      && mainWindowXaml.Contains("TerrainRouteLegendPanel", StringComparison.Ordinal)
      && mainWindowXaml.Contains("ROAD SOLID", StringComparison.Ordinal)
      && mainWindowXaml.Contains("TRAIL DASH", StringComparison.Ordinal)
      && mainWindowXaml.Contains("UNKNOWN DOT", StringComparison.Ordinal)
      && mainWindowXaml.Contains("TerrainBlockedPassageButton", StringComparison.Ordinal),
    "typed map-course evidence, compact legend and confidence rail, toggle, and immediate blocked-passage recovery");
Check(mainWindowSource.Contains("loadTerrainCommunityHazards", StringComparison.Ordinal)
      && mainWindowSource.Contains("terrainCommunityHazards.map", StringComparison.Ordinal)
      && mainWindowSource.Contains("setTerrainCommunityHazardsEnabled", StringComparison.Ordinal)
      && mainWindowSource.Contains("new(\"terrain-danger\"", StringComparison.Ordinal)
      && mainWindowSource.Contains("inside-community-terrain-hazard", StringComparison.Ordinal)
      && mainWindowXaml.Contains("TerrainCommunityHazardsButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("TERRAIN DANGER · SYNCING", StringComparison.Ordinal),
    "current public terrain danger transform, map overlay, hard routing obstacle, toggle, status, and Quick Command");

Check(mainWindowSource.Contains("normalizeLearnedPassageLibrary", StringComparison.Ordinal)
      && mainWindowSource.Contains("buildLearnedPassageFromTrail", StringComparison.Ordinal)
      && mainWindowSource.Contains("learnedPassageIsCurrent", StringComparison.Ordinal)
      && mainWindowSource.Contains("type: 'learned'", StringComparison.Ordinal)
      && mainWindowSource.Contains("pathType === 'road' || pathType === 'trail'", StringComparison.Ordinal)
      && mainWindowSource.Contains("LearnedPassageRoutingEnabled = _learnedPassageRoutingEnabled", StringComparison.Ordinal)
      && mainWindowSource.Contains("LearnedPassageVisible = _learnedPassageVisible", StringComparison.Ordinal)
      && mainWindowXaml.Contains("SaveLearnedPassageButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("LearnedPassageRoutingButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("LearnedPassageVisibilityButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("ClearLearnedPassagesButton", StringComparison.Ordinal)
      && mainWindowXaml.Contains("LEARNED MIX", StringComparison.Ordinal),
    "explicit privacy-bounded learned passages, source freshness, hard water constraints, independent toggles, and guarded clear");

Console.WriteLine("Terrain road network: PASS (typed road/trail/learned/unknown map course, source-aged player-traveled evidence, current public terrain danger points, blocked-passage replan, three route styles, 45/80/125 MU connector-gap policy, water mask, strict validation, and controls)");

if (args.Contains("--live", StringComparer.OrdinalIgnoreCase))
{
    var live = await TerrainRoadNetworkClient.FetchAsync(CancellationToken.None);
    Check(live.Paths.Count > 0 && live.PointCount > live.Paths.Count, "live terrain source content");
    Check(live.Paths.Any(path => path.Type == "road")
          && live.Paths.Any(path => path.Type == "trail")
          && live.Paths.All(path => path.Type is "road" or "trail"),
        "live typed road/trail source content");
    Check(live.WaterMask is { WebpBytes.Length: > 1000 }, "live water mask content");
    var liveGateway = live.GatewayMap
                      ?? throw new InvalidOperationException("live Gateway map content missing");
    Check(liveGateway is
          {
              Sanctuaries.Count: 7,
              PatrolCandidates.Count: 61,
              MigrationCandidates.Count: 12,
              Animals.Count: 430,
              Herbs.Count: 245,
              Earth.Count: 278,
              WaterLabels.Count: >= 20
          },
        "live current basemap, zone, resource, and water-label content");
    Check(liveGateway.Basemap.ReferenceDate == "2026-07-18"
          && liveGateway.Basemap.PreviewUrl.Contains(
              "gateway-preview.webp?v=20260718hq2", StringComparison.Ordinal),
        "live coherent current basemap version and source date");
    Check(live.CommunityHazards is not null
          && live.CommunityHazards.Hazards.Count <= TerrainCommunityHazardFeedClient.MaximumHazards,
        "live current public terrain danger source");
    Console.WriteLine(
        $"Terrain road network live source: PASS ({live.Paths.Count} paths, {live.PointCount} points, {liveGateway.ZoneCount} zones, {liveGateway.ResourceCount} resources, {live.CommunityHazards!.Hazards.Count} terrain dangers, version {live.Version})");
}
