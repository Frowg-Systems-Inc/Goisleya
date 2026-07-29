using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Reject(string json, DateTimeOffset now, string message)
{
    try
    {
        _ = IsleyReleaseLogic.ParseManifest(json, now);
        throw new InvalidOperationException(message);
    }
    catch (InvalidDataException)
    {
    }
}

var now = new DateTimeOffset(2026, 7, 23, 20, 0, 0, TimeSpan.Zero);
const string validJson = """
{
  "manifestVersion": 1,
  "channel": "stable",
  "version": "1.1.0",
  "publishedAt": "2026-07-23T19:30:00Z",
  "downloadUrl": "https://isley-download.gmith.chatgpt.site/Isley-Windows-x64.zip",
  "sha256": "814223C46E98F97F3FDFB8BA00DE296ACA90DE0CB5578B1C989ED78AC2950211",
  "bytes": 8699939,
  "notes": "Automatic update notifications and verified one-click installation.",
  "required": false
}
""";

var release = IsleyReleaseLogic.ParseManifest(validJson, now);
Check(release.VersionText == "1.1.0"
      && release.Version == new Version(1, 1, 0)
      && release.DownloadUri.AbsoluteUri == IsleyReleaseLogic.StableDownloadUrl
      && release.Sha256.Length == 64
      && release.Bytes == 8699939
      && !release.Required,
    "The stable release manifest must round-trip only trusted update metadata.");
Check(IsleyReleaseLogic.IsNewer(new Version(1, 0, 9, 99), release.Version)
      && !IsleyReleaseLogic.IsNewer(new Version(1, 1, 0, 0), release.Version)
      && !IsleyReleaseLogic.IsNewer(new Version(2, 0, 0), release.Version),
    "Release comparison must ignore assembly revision and only offer newer builds.");
Check(IsleyReleaseLogic.DisplayVersion(new Version(1, 1, 0, 0)) == "1.1.0",
    "Displayed versions must be short and stable.");

Reject(
    validJson.Replace(
        IsleyReleaseLogic.StableDownloadUrl,
        "https://example.com/Isley-Windows-x64.zip",
        StringComparison.Ordinal),
    now,
    "A release from another host must be rejected.");
Reject(
    validJson.Replace(
        "814223C46E98F97F3FDFB8BA00DE296ACA90DE0CB5578B1C989ED78AC2950211",
        new string('0', 63),
        StringComparison.Ordinal),
    now,
    "A malformed archive fingerprint must be rejected.");
Reject(
    validJson.Replace(
        "2026-07-23T19:30:00Z",
        "2026-08-23T19:30:00Z",
        StringComparison.Ordinal),
    now,
    "A future-dated release must be rejected.");
Reject(
    validJson.Replace("\"bytes\": 8699939", "\"bytes\": 500", StringComparison.Ordinal),
    now,
    "An implausible archive size must be rejected.");

// --- Beta channel: same pinned host, same verification posture ---
const string validBetaJson = """
{
  "manifestVersion": 1,
  "channel": "beta",
  "version": "1.2.0",
  "publishedAt": "2026-07-23T19:30:00Z",
  "downloadUrl": "https://isley-download.gmith.chatgpt.site/Isley-Windows-x64-beta.zip",
  "sha256": "814223C46E98F97F3FDFB8BA00DE296ACA90DE0CB5578B1C989ED78AC2950211",
  "bytes": 8699939,
  "notes": "Beta preview.",
  "required": false
}
""";

var betaRelease = IsleyReleaseLogic.ParseManifest(
    validBetaJson,
    now,
    IsleyReleaseLogic.BetaChannel);
Check(betaRelease.Channel == IsleyReleaseLogic.BetaChannel
      && betaRelease.DownloadUri.AbsoluteUri == IsleyReleaseLogic.BetaDownloadUrl
      && betaRelease.Delta is null,
    "The beta manifest must round-trip on the pinned beta download address.");
Check(IsleyReleaseLogic.BetaReleaseEndpoint.StartsWith(
          "https://" + IsleyReleaseLogic.TrustedDownloadHost + "/", StringComparison.Ordinal)
      && IsleyReleaseLogic.BetaDownloadUrl.StartsWith(
          "https://" + IsleyReleaseLogic.TrustedDownloadHost + "/", StringComparison.Ordinal),
    "Beta endpoints must stay on the pinned trusted host over HTTPS.");
Reject(
    validBetaJson,
    now,
    "A beta manifest must be rejected when the stable channel was requested.");
try
{
    _ = IsleyReleaseLogic.ParseManifest(validJson, now, IsleyReleaseLogic.BetaChannel);
    throw new InvalidOperationException("A stable manifest must be rejected for the beta channel.");
}
catch (InvalidDataException)
{
}
Reject(
    validBetaJson.Replace(
        IsleyReleaseLogic.BetaDownloadUrl,
        IsleyReleaseLogic.StableDownloadUrl,
        StringComparison.Ordinal),
    now,
    "The beta channel must not accept the stable download address.");

// --- Delta offer inside the release manifest ---
const string deltaBlock = """
  "delta": {
    "fromVersion": "1.0.0",
    "url": "https://isley-download.gmith.chatgpt.site/Isley-delta-1.0.0-1.1.0.zip",
    "sha256": "914223C46E98F97F3FDFB8BA00DE296ACA90DE0CB5578B1C989ED78AC2950212",
    "bytes": 4096
  }
""";
var deltaJson = validJson.Replace(
    "\"required\": false",
    "\"required\": false,\n" + deltaBlock,
    StringComparison.Ordinal);
var deltaRelease = IsleyReleaseLogic.ParseManifest(deltaJson, now);
Check(deltaRelease.Delta is { } offer
      && offer.FromVersion == new Version(1, 0, 0)
      && offer.Bytes == 4096
      && offer.Sha256.Length == 64
      && offer.DownloadUri.Host == IsleyReleaseLogic.TrustedDownloadHost
      && offer.DownloadUri.Scheme == Uri.UriSchemeHttps,
    "A valid delta offer must round-trip from the release manifest.");
Reject(
    deltaJson.Replace(
        "isley-download.gmith.chatgpt.site/Isley-delta",
        "evil.example/Isley-delta",
        StringComparison.Ordinal),
    now,
    "A delta hosted off the pinned host must be rejected.");
Reject(
    deltaJson.Replace("\"fromVersion\": \"1.0.0\"", "\"fromVersion\": \"1.1.0\"", StringComparison.Ordinal),
    now,
    "A delta whose base is not older than the release must be rejected.");
Reject(
    deltaJson.Replace("\"bytes\": 4096", "\"bytes\": 12", StringComparison.Ordinal),
    now,
    "An implausible delta size must be rejected.");
Check(IsleyReleaseLogic.IsSameVersion(new Version(1, 2, 0, 77), new Version(1, 2, 0))
      && !IsleyReleaseLogic.IsSameVersion(new Version(1, 2, 1), new Version(1, 2, 0)),
    "Delta base matching must compare exact three-part versions, ignoring assembly revision.");
Check(IsleyReleaseLogic.IsValidVersionText("1.2.0")
      && !IsleyReleaseLogic.IsValidVersionText("1.2.0; rm -rf")
      && !IsleyReleaseLogic.IsValidVersionText(null),
    "Version text validation must only accept the release version pattern.");

// --- Delta package file list (inside the verified delta zip) ---
var plan = IsleyReleaseLogic.ParseDeltaManifest(
    """
    {
      "format": 1,
      "fromVersion": "1.0.0",
      "toVersion": "1.1.0",
      "deletedFiles": ["Voice/old.js", "Map\\legacy.dat"]
    }
    """,
    new Version(1, 0, 0),
    new Version(1, 1, 0));
Check(plan.DeletedFiles.Count == 2
      && plan.DeletedFiles[0] == "Voice\\old.js"
      && plan.DeletedFiles[1] == "Map\\legacy.dat",
    "The delta file list must normalize separators and keep validated entries.");
const string validDeltaManifest = """
{
  "format": 1,
  "fromVersion": "1.0.0",
  "toVersion": "1.1.0",
  "deletedFiles": []
}
""";
static void RejectDelta(string json, Version from, Version to, string message)
{
    try
    {
        _ = IsleyReleaseLogic.ParseDeltaManifest(json, from, to);
        throw new InvalidOperationException(message);
    }
    catch (InvalidDataException)
    {
    }
}
RejectDelta(
    validDeltaManifest.Replace("\"format\": 1", "\"format\": 2", StringComparison.Ordinal),
    new Version(1, 0, 0),
    new Version(1, 1, 0),
    "An unknown delta file list format must be rejected.");
RejectDelta(
    validDeltaManifest.Replace("\"toVersion\": \"1.1.0\"", "\"toVersion\": \"1.1.1\"", StringComparison.Ordinal),
    new Version(1, 0, 0),
    new Version(1, 1, 0),
    "A delta file list for another target version must be rejected.");
RejectDelta(
    validDeltaManifest.Replace("[]", "[\"../Isley.exe\"]", StringComparison.Ordinal),
    new Version(1, 0, 0),
    new Version(1, 1, 0),
    "Traversal in the delta delete list must be rejected.");
RejectDelta(
    validDeltaManifest.Replace("[]", "[\"C:/Windows/System32/ntdll.dll\"]", StringComparison.Ordinal),
    new Version(1, 0, 0),
    new Version(1, 1, 0),
    "Rooted paths in the delta delete list must be rejected.");
RejectDelta(
    validDeltaManifest.Replace("[]", "[\"IsleyData/settings.json\"]", StringComparison.Ordinal),
    new Version(1, 0, 0),
    new Version(1, 1, 0),
    "The delta delete list must never touch IsleyData.");

// --- Boot-ok marker: bounded write, validated read ---
var markerDirectory = Path.Combine(
    Path.GetTempPath(),
    "isley-boot-ok-verifier-" + Guid.NewGuid().ToString("N"));
try
{
    var markerPath = Path.Combine(markerDirectory, "last-boot-ok.json");
    IsleyUpdateClient.WriteBootOkMarker(markerPath, "1.2.0");
    Check(IsleyUpdateClient.TryReadBootOkMarker(markerPath, out var confirmedVersion)
          && confirmedVersion == "1.2.0",
        "The boot-ok marker must round-trip a validated version.");
    File.WriteAllText(markerPath, "{\"version\":\"1.2.0;evil\",\"confirmedAt\":\"2026-07-23T20:00:00Z\"}");
    Check(!IsleyUpdateClient.TryReadBootOkMarker(markerPath, out _),
        "A marker with an invalid version must be rejected.");
    File.WriteAllText(markerPath, "{\"version\":\"1.2.0\"}");
    Check(!IsleyUpdateClient.TryReadBootOkMarker(markerPath, out _),
        "A marker without a timestamp must be rejected.");
    File.WriteAllText(markerPath, new string('x', 2048));
    Check(!IsleyUpdateClient.TryReadBootOkMarker(markerPath, out _),
        "An oversized or malformed marker must be rejected.");
    Check(!IsleyUpdateClient.TryReadBootOkMarker(
              Path.Combine(markerDirectory, "missing.json"),
              out _),
        "A missing marker must read as unconfirmed without throwing.");
    try
    {
        IsleyUpdateClient.WriteBootOkMarker(markerPath, "1.2.0 or 1=1");
        throw new InvalidOperationException("An invalid marker version must be rejected.");
    }
    catch (InvalidDataException)
    {
    }
}
finally
{
    try { Directory.Delete(markerDirectory, recursive: true); } catch { }
}


var safeRoot = Path.Combine(Path.GetTempPath(), "isley-update-verifier");
var safePath = IsleyReleaseLogic.ResolveSafePackageEntry(
    safeRoot,
    "Updater/Isley.Updater.exe");
Check(safePath.StartsWith(Path.GetFullPath(safeRoot), StringComparison.OrdinalIgnoreCase),
    "Normal archive paths must remain under the staging directory.");
try
{
    _ = IsleyReleaseLogic.ResolveSafePackageEntry(safeRoot, "../IsleyData/settings.json");
    throw new InvalidOperationException("Archive traversal must be rejected.");
}
catch (InvalidDataException)
{
}

var root = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory,
    "..",
    "..",
    "..",
    "..",
    ".."));
var mainSource = string.Join("\n", Directory.GetFiles(Path.Combine(root, "BurntHud"), "MainWindow*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText));
var xaml = File.ReadAllText(Path.Combine(root, "BurntHud", "MainWindow.xaml"));
var client = File.ReadAllText(Path.Combine(root, "BurntHud", "IsleyUpdateClient.cs"));
var updater = File.ReadAllText(Path.Combine(root, "Isley.Updater", "Program.cs"));
var project = File.ReadAllText(Path.Combine(root, "BurntHud", "BurntHud.csproj"));

Check(mainSource.Contains("public bool AutomaticUpdatesEnabled", StringComparison.Ordinal)
      && mainSource.Contains("_automaticUpdatesEnabled = settings.AutomaticUpdatesEnabled", StringComparison.Ordinal)
      && mainSource.Contains("AutomaticUpdatesEnabled = _automaticUpdatesEnabled", StringComparison.Ordinal)
      && mainSource.Contains("TimeSpan.FromMinutes(30)", StringComparison.Ordinal)
      && mainSource.Contains("CheckForIsleyUpdateAfterStartupAsync", StringComparison.Ordinal)
      && mainSource.Contains("ConsumeUpdaterResult", StringComparison.Ordinal),
    "Automatic update checks must be on by default, persistent, periodic, and acknowledged after restart.");
Check(xaml.Contains("x:Name=\"IsleyUpdatePromptBorder\"", StringComparison.Ordinal)
      && xaml.Contains("x:Name=\"IsleyUpdateNowButton\"", StringComparison.Ordinal)
      && xaml.Contains("UPDATE &amp; RESTART", StringComparison.Ordinal)
      && xaml.Contains("x:Name=\"AutomaticUpdatesButton\"", StringComparison.Ordinal)
      && xaml.Contains("x:Name=\"CheckForIsleyUpdateButton\"", StringComparison.Ordinal),
    "Isley must provide a visible update notification, one-click action, snooze, toggle, and manual check.");
Check(client.Contains("FixedTimeEquals", StringComparison.Ordinal)
      && client.Contains("SHA256.HashData", StringComparison.Ordinal)
      && client.Contains("MaximumExpandedBytes", StringComparison.Ordinal)
      && client.Contains("ResolveSafePackageEntry", StringComparison.Ordinal)
      && client.Contains("ResolveUpdaterExecutable", StringComparison.Ordinal)
      && client.Contains("CreateNoWindow = false", StringComparison.Ordinal)
      && client.Contains("Isley.Updater.exe", StringComparison.Ordinal)
      && client.Contains("\"Voice\", \"voice.html\"", StringComparison.Ordinal)
      && client.Contains("\"Voice\", \"voice-crypto.js\"", StringComparison.Ordinal)
      && client.Contains("\"VoiceServer\", \"Isley.VoiceServer.exe\"", StringComparison.Ordinal)
      && client.Contains("\"VoiceServer\", \"appsettings.json\"", StringComparison.Ordinal),
    "Update staging must verify the exact hash, bound extraction, block traversal, prefer the installed updater when hashes match, avoid hidden helper launches, and require updater plus voice assets.");
Check(client.Contains("StageDeltaAsync", StringComparison.Ordinal)
      && client.Contains("BetaFallback", StringComparison.Ordinal)
      && client.Contains("FetchManifestAsync", StringComparison.Ordinal)
      && client.Contains("ValidateDeltaPackage", StringComparison.Ordinal)
      && client.Contains("WriteBootOkMarker", StringComparison.Ordinal)
      && client.Contains("TryReadBootOkMarker", StringComparison.Ordinal)
      && client.Contains("never bricks an update", StringComparison.Ordinal)
      && client.Contains("\"--mode\"", StringComparison.Ordinal),
    "The client must fetch per-channel pinned manifests with honest beta fallback, stage verified deltas with full-package fallback, pass the delta mode to the helper, and read/write the boot-ok marker.");
Check(mainSource.Contains("ConfirmUpdatedBootAsync", StringComparison.Ordinal)
      && mainSource.Contains("BOOT CONFIRMED", StringComparison.Ordinal)
      && mainSource.Contains("BOOT NOT CONFIRMED", StringComparison.Ordinal)
      && mainSource.Contains("ResolveBootOkMarkerPath", StringComparison.Ordinal)
      && mainSource.Contains("BETA CHANNEL UNAVAILABLE", StringComparison.Ordinal)
      && mainSource.Contains("BETA RELEASES PREFERRED WHEN PUBLISHED", StringComparison.Ordinal)
      && mainSource.Contains("_preferBetaUpdates,", StringComparison.Ordinal),
    "The update UI must confirm boots after updates, surface missing confirmations honestly, and drive the real beta channel with honest fallback copy.");
Check(updater.Contains("WaitForExit", StringComparison.Ordinal)
      && updater.Contains("IsleyData", StringComparison.Ordinal)
      && updater.Contains("ApplyPackageWithBackup", StringComparison.Ordinal)
      && updater.Contains("RestoreBackup", StringComparison.Ordinal)
      && updater.Contains("RemoveOrphanedPackageFiles", StringComparison.Ordinal)
      && updater.Contains("Removed {removed} obsolete install files.", StringComparison.Ordinal)
      && updater.Contains("Restoring the previous Isley installation after an update failure.", StringComparison.Ordinal)
      && updater.Contains("Reopened the existing Isley installation", StringComparison.Ordinal),
    "The updater must wait for Isley, preserve IsleyData, back up before replace, roll back on failure, remove orphans, and recover by reopening.");
Check(updater.Contains("ApplyDeltaDeleteList", StringComparison.Ordinal)
      && updater.Contains("isley-delta-manifest.json", StringComparison.Ordinal)
      && updater.Contains("\"--mode\"", StringComparison.Ordinal)
      && updater.Contains("escaped the install folder", StringComparison.Ordinal)
      && updater.Contains("Removed {removed} delta-listed install files.", StringComparison.Ordinal)
      && updater.Contains("must not run here", StringComparison.Ordinal),
    "Delta mode must skip the orphan sweep, require its verified file list, validate every delete path inside the install folder, and keep backups for rollback.");
var packaging = File.ReadAllText(Path.Combine(root, "scripts", "package-isley-1.3.ps1"));
Check(packaging.Contains("PreviousClientArchive", StringComparison.Ordinal)
      && packaging.Contains("isley-delta-manifest.json", StringComparison.Ordinal)
      && packaging.Contains("Isley-delta-", StringComparison.Ordinal)
      && packaging.Contains("deletedFiles", StringComparison.Ordinal)
      && packaging.Contains("Updater\\Isley.Updater.exe", StringComparison.Ordinal)
      && packaging.Contains("saves nothing must not be published", StringComparison.Ordinal),
    "Packaging must emit a file-level delta (changed/new files + bounded delete list + forced updater helper) and refuse deltas that save nothing.");
Check(System.Text.RegularExpressions.Regex.IsMatch(
          project,
          @"<Version>\d+\.\d+\.\d+</Version>",
          System.Text.RegularExpressions.RegexOptions.CultureInvariant)
      && project.Contains("CopyIsleyUpdater", StringComparison.Ordinal)
      && project.Contains("PublishIsleyCompanions", StringComparison.Ordinal)
      && project.Contains(@"Updater\%(Filename)%(Extension)", StringComparison.Ordinal)
      && project.Contains(@"VoiceServer\%(Filename)%(Extension)", StringComparison.Ordinal)
      && !project.Contains(@"Updater\%(RecursiveDir)", StringComparison.Ordinal)
      && !project.Contains(@"VoiceServer\%(RecursiveDir)", StringComparison.Ordinal),
    "The release build must carry a comparable version and flatten companion helpers without nested RID folders.");

Console.WriteLine(
    "Isley release update verification passed " +
    "(trusted stable/beta manifests with honest fallback, version policy, " +
    "startup/30-minute notifications, one-click verified staging, optional " +
    "verified delta packages with full-package fallback, validated delta " +
    "delete lists, boot-ok confirmation, traversal limits, settings " +
    "preservation, backup/rollback replace, flat companion packaging, and " +
    "restart helper).");
