const fs = require("fs");
const path = require("path");

const root = path.join(__dirname, "..");
const runtimeRoots = [
  path.join(root, "BurntHud"),
  path.join(root, "distribution"),
  path.join(root, "download-site", "app"),
];
const allowedExtensions = new Set([
  ".cs", ".xaml", ".csproj", ".html", ".js", ".json", ".txt", ".tsx",
]);
const ignoredDirectories = new Set([
  "bin", "obj", "bin-verify", ".next", "node_modules", "VerificationProfiles",
]);
const forbidden = new RegExp([
  "the", "burnt", "isle", "|", "isle", "pilot", "|", "66\\.51\\.96\\.73",
].join(""), "i");

const files = [];
const walk = directory => {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    if (entry.isDirectory() && ignoredDirectories.has(entry.name)) continue;
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) walk(fullPath);
    else if (allowedExtensions.has(path.extname(entry.name).toLowerCase())) files.push(fullPath);
  }
};
for (const directory of runtimeRoots) walk(directory);

const violations = files
  .map(file => ({ file, text: fs.readFileSync(file, "utf8") }))
  .filter(item => forbidden.test(item.text));
if (violations.length) {
  throw new Error(
    "Forbidden server-specific dependency text remains:\n"
    + violations.map(item => path.relative(root, item.file)).join("\n"));
}

const main = fs.readdirSync(path.join(root, "BurntHud"))
  .filter(name => name.startsWith("MainWindow") && name.endsWith(".cs"))
  .sort()
  .map(name => fs.readFileSync(path.join(root, "BurntHud", name), "utf8"))
  .join("\n")
  + "\n" + fs.readFileSync(path.join(root, "BurntHud", "Map", "isley-map-controller.js"), "utf8");
const project = fs.readFileSync(path.join(root, "BurntHud", "BurntHud.csproj"), "utf8");
const map = fs.readFileSync(path.join(root, "BurntHud", "Map", "index.html"), "utf8");
const provider = fs.readFileSync(
  path.join(root, "BurntHud", "IsleyLiveDataProvider.cs"), "utf8");
const relayClient = fs.readFileSync(
  path.join(root, "BurntHud", "IsleyRelayClient.cs"), "utf8");
const telemetryContracts = fs.readFileSync(
  path.join(root, "Isley.Telemetry", "TelemetryContracts.cs"), "utf8");
const gatewayMap = fs.readFileSync(
  path.join(root, "BurntHud", "GatewayMapOverlayClient.cs"), "utf8");
const terrain = fs.readFileSync(
  path.join(root, "BurntHud", "TerrainRoadNetworkClient.cs"), "utf8");

const required = [
  [main, 'LocalMapHost = "isley.local"', "local virtual host"],
  [main, 'LocalMapUri = "https://isley.local/map/index.html"', "local map address"],
  [main, "SetVirtualHostNameToFolderMapping(", "local map-shell mapping"],
  [main, "RefreshIndependentLiveDataAsync", "local provider polling"],
  [main, "IndependentLiveDataPath", "portable and installed provider path"],
  [main, "SyncUniversalCoordinateToLocalMapAsync", "opt-in coordinate map bridge"],
  [main, "window.__isleyLocalMap?.setSnapshot", "local snapshot bridge"],
  [main, "ApplyIsleyRelaySnapshotAsync", "authenticated relay snapshot bridge"],
  [main, "DrainIsleyRelaySnapshotsAsync", "one-frame relay snapshot coalesce"],
  [main, "TelemetryStreamHealthLogic.Assess", "delayed and stalled live-map honesty"],
  [main, "snapshot.SampledAt);", "relay vitals use sample time"],
  [main, "IsleyRelayFriendSharingButton_Click", "player-controlled friend visibility"],
  [main, "explicitRole === 'other'", "other-provider-animal support"],
  [project, 'Content Include="Map\\**\\*"', "local map-shell packaging"],
  [project, 'Content Include="Voice\\**\\*"', "local voice client packaging"],
  [project, 'GlobalPropertiesToRemove="RuntimeIdentifier;SelfContained"', "RID-safe VoiceServer packaging"],
  [project, 'ProjectReference Include="..\\Isley.Telemetry\\Isley.Telemetry.csproj"', "shared telemetry contract"],
  [
    fs.readFileSync(path.join(root, "Isley.ServerBridge", "BridgeRuntime.cs"), "utf8"),
    "Kind: not TelemetryEntityKind.AiAnimal",
    "privacy-filtered player Server-scope clamp"
  ],
  [
    fs.readFileSync(path.join(root, "Isley.Relay", "SteamFriendResolver.cs"), "utf8"),
    "|| !targetPrivacy.ShareWithSteamFriends",
    "player Steam-friend opt-out"
  ],
  [
    fs.readFileSync(path.join(root, "Isley.Relay", "BridgeAuthentication.cs"), "utf8"),
    "Consume the nonce only after the HMAC passes",
    "signature-before-nonce ingest defense"
  ],
  [
    fs.readFileSync(path.join(root, "scripts", "Start-IsleyServerBridge.ps1"), "utf8"),
    "Read-Host $Prompt -AsSecureString",
    "secure guided bridge launcher"
  ],
  [
    fs.readFileSync(path.join(root, "Isley.Telemetry", "TelemetryContracts.cs"), "utf8"),
    "now - frame.SampledAt > TelemetryProtocol.MaximumFrameAge",
    "stale SampledAt refusal"
  ],
  [
    fs.readFileSync(path.join(root, "Isley.ServerBridge", "Program.cs"), "utf8"),
    "status.Sampled(frame);",
    "plugin source status honesty"
  ],
  [
    fs.readFileSync(path.join(root, "Isley.Updater", "Program.cs"), "utf8"),
    "RemoveOrphanedPackageFiles",
    "updater orphan cleanup"
  ],
  [
    fs.readFileSync(path.join(root, "BurntHud", "IsleyRelayClient.cs"), "utf8"),
    "ReadTrustedVerificationUri",
    "trusted Steam verification URI pin"
  ],
  [
    fs.readFileSync(path.join(root, "BurntHud", "IsleyRelayClient.cs"), "utf8"),
    "AllowAutoRedirect = false",
    "relay HTTP redirect refusal"
  ],
  [
    fs.readFileSync(path.join(root, "Isley.Relay", "Program.cs"), "utf8"),
    'app.MapGet("/join/{serverId}"',
    "relay join landing page"
  ],
  [map, "window.__isleyLocalMap", "local map API"],
  [map, "setTerrainDataset", "validated current map-data bridge"],
  [map, "myislemap-current-gamefiles", "current independent map source"],
  [map, "SCHEMATIC OFFLINE FALLBACK", "honest offline fallback label"],
  [map, "gateway-current-gamefiles-2026-07-18", "current coordinate space"],
  [map, "dataset.isleyRole", "explicit marker roles"],
  [gatewayMap, "NormalizeObjectLiteralToJson", "non-evaluating current map parser"],
  [gatewayMap, "MinimumWorldX = -607_000", "current Gateway calibration"],
  [terrain, "GatewayMap = gatewayMap", "single typed terrain and map payload"],
  [provider, "MaximumPlayers = 512", "bounded provider roster"],
  [provider, "FreshnessLimit = TimeSpan.FromSeconds(10)", "fail-closed freshness"],
  [provider, "MaximumBytes = 256 * 1024", "bounded provider document"],
  [relayClient, "ClientWebSocket", "authenticated realtime relay transport"],
  [relayClient, "IsleyWindowsCredentialStore", "protected Steam session storage"],
  [relayClient, "UpdatePrivacyAsync", "Steam friend consent control"],
  [telemetryContracts, "ConnectedPlayerNodes", "awareness network node metadata"],
  [telemetryContracts, "TelemetryVisibilityPolicy", "explicit visibility policy"],
];
for (const [source, token, label] of required) {
  if (!source.includes(token)) throw new Error(`Missing ${label}: ${token}`);
}
if (!/SetVirtualHostNameToFolderMapping\(\s*LocalMapHost,\s*AppContext\.BaseDirectory,/s.test(main)) {
  throw new Error("The local map virtual host must map the application root for /map/index.html.");
}

const forbiddenRuntimeTokens = [
  "IsleServerStatusClient.FetchAsync",
  "originalFetch('/me'",
  "location.hostname !==",
  "legacyBurntHudUserDataFolder",
  "theisle.info/maps/",
  "theisle-info-online",
];
for (const token of forbiddenRuntimeTokens) {
  if (main.includes(token)) throw new Error(`Obsolete runtime dependency remains: ${token}`);
}

console.log(
  `Independent provider verification passed (${files.length} runtime/source files checked).`);
