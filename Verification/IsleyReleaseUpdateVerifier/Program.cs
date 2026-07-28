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
Check(updater.Contains("WaitForExit", StringComparison.Ordinal)
      && updater.Contains("IsleyData", StringComparison.Ordinal)
      && updater.Contains("ApplyPackageWithBackup", StringComparison.Ordinal)
      && updater.Contains("RestoreBackup", StringComparison.Ordinal)
      && updater.Contains("RemoveOrphanedPackageFiles", StringComparison.Ordinal)
      && updater.Contains("Removed {removed} obsolete install files.", StringComparison.Ordinal)
      && updater.Contains("Restoring the previous Isley installation after an update failure.", StringComparison.Ordinal)
      && updater.Contains("Reopened the existing Isley installation", StringComparison.Ordinal),
    "The updater must wait for Isley, preserve IsleyData, back up before replace, roll back on failure, remove orphans, and recover by reopening.");
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
    "(trusted manifest, version policy, startup/30-minute notifications, " +
    "one-click verified staging, traversal limits, settings preservation, " +
    "backup/rollback replace, flat companion packaging, and restart helper).");
