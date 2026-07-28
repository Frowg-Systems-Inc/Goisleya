using System.IO;
using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Reject(Action action, string message)
{
    try
    {
        action();
    }
    catch (InvalidDataException)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
var json = """
{
  "appnews": {
    "appid": 376210,
    "newsitems": [
      { "gid": "1836506165564326", "title": "DevBlog #70", "date": 1782881867, "feedname": "steam_community_announcements", "appid": 376210, "tags": ["mod_reviewed"] },
      { "gid": "1819386365103785", "title": "Patch 0.21.321", "date": 1766486138, "feedname": "steam_community_announcements", "appid": 376210, "tags": ["patchnotes"] },
      { "gid": "1836506165584411", "title": "Patch 0.21.734 Now Available!", "date": 1783624103, "url": "https://untrusted.example/fake", "feedname": "steam_community_announcements", "appid": 376210, "tags": ["patchnotes"] },
      { "gid": "1836506165569583", "title": "Patch 0.21.720 Now Available!", "date": 1783055127, "feedname": "steam_community_announcements", "appid": 376210, "tags": ["patchnotes"] }
    ]
  }
}
""";

var latest = OfficialPatchWatchClient.Parse(json, now);
Check(latest.Version == "0.21.734", "latest patch selection failed");
Check(latest.Title == "Patch 0.21.734 Now Available!", "patch title normalization failed");
Check(latest.AnnouncementId == "1836506165584411", "announcement id failed");
Check(latest.NotesUrl == "https://steamcommunity.com/ogg/376210/announcements/detail/1836506165584411",
    "trusted official notes URL failed");
Check(latest.RetrievedAt == now, "retrieval time failed");

Reject(() => OfficialPatchWatchClient.Parse(
    "{\"appnews\":{\"appid\":1,\"newsitems\":[{\"gid\":\"1836506165584411\",\"title\":\"Patch 9.9.9\",\"date\":1783624103,\"feedname\":\"steam_community_announcements\",\"appid\":376210,\"tags\":[\"patchnotes\"]}]}}",
    now), "wrong-app response accepted");
Reject(() => OfficialPatchWatchClient.Parse(
    "{\"appnews\":{\"appid\":376210,\"newsitems\":[{\"gid\":\"1836506165584411\",\"title\":\"DevBlog #70\",\"date\":1783624103,\"feedname\":\"steam_community_announcements\",\"appid\":376210,\"tags\":[\"mod_reviewed\"]}]}}",
    now), "non-patch response accepted");
Reject(() => OfficialPatchWatchClient.Parse(
    "{\"appnews\":{\"appid\":376210,\"newsitems\":[{\"gid\":\"not-an-id\",\"title\":\"Patch 0.21.900\",\"date\":1783624103,\"feedname\":\"steam_community_announcements\",\"appid\":376210,\"tags\":[\"patchnotes\"]}]}}",
    now), "invalid announcement id accepted");
Reject(() => OfficialPatchWatchClient.Parse(
    "{\"appnews\":{\"appid\":376210,\"newsitems\":[{\"gid\":\"1836506165584411\",\"title\":\"Patch 0.21.900\",\"date\":1999999999,\"feedname\":\"steam_community_announcements\",\"appid\":376210,\"tags\":[\"patchnotes\"]}]}}",
    now), "future announcement accepted");
Reject(() => OfficialPatchWatchClient.Parse(
    "{\"appnews\":{\"appid\":376210,\"newsitems\":[{\"gid\":\"1836506165584411\",\"title\":\"Patch 0.21.900\",\"date\":1783624103,\"feedname\":\"workshop\",\"appid\":376210,\"tags\":[\"patchnotes\"]}]}}",
    now), "non-announcement feed accepted");
Reject(() => OfficialPatchWatchClient.Parse(
    new string(' ', OfficialPatchWatchClient.MaxPayloadBytes + 1),
    now), "oversized response accepted");

Check(PatchWatchLogic.TryParseVersion("0.21.734", out var parsedVersion)
      && parsedVersion == (0, 21, 734), "valid version parsing failed");
Check(!PatchWatchLogic.TryParseVersion("0.21", out _)
      && !PatchWatchLogic.TryParseVersion("0.21.7beta", out _)
      && !PatchWatchLogic.TryParseVersion("-1.21.7", out _), "invalid version accepted");
Check(PatchWatchLogic.CompareVersions("0.21.800", "0.21.734") > 0
      && PatchWatchLogic.CompareVersions("0.21.734", "0.21.734") == 0
      && PatchWatchLogic.CompareVersions("0.20.9999", "0.21.1") < 0,
    "semantic version ordering failed");
Check(PatchWatchLogic.TryExtractVersion("evrima 0.21.738", out var serverVersion)
      && serverVersion == "0.21.738"
      && !PatchWatchLogic.TryExtractVersion("version unavailable", out _),
    "server-version extraction failed");
Check(IsleContentBaseline.PublicBranch == CombatGuideLogic.PublicBranch,
    "combat guide baseline drifted from patch watch");

var current = PatchWatchLogic.Evaluate(latest, false, false, now);
Check(current.State == PatchWatchState.Current
      && current.Heading == "GUIDES MATCH PUBLIC"
      && current.VersionLine == "PUBLIC 0.21.734 · ISLEY 0.21.734"
      && current.HasNotes
      && !current.NeedsReview,
    "current-patch guidance failed");
var currentImpact = PatchWatchLogic.BuildImpact(current, latest.NotesUrl);
Check(!currentImpact.Visible
      && currentImpact.CopyText.Length == 0,
    "aligned patch impact should stay collapsed");

var newerSnapshot = latest with { Version = "0.21.800", Title = "Patch 0.21.800" };
var newer = PatchWatchLogic.Evaluate(newerSnapshot, false, false, now);
Check(newer.State == PatchWatchState.ReviewNeeded
      && newer.NeedsReview
      && newer.Heading == "REVIEW PATCH 0.21.800"
      && newer.Detail.Contains("update-sensitive", StringComparison.Ordinal),
    "new-patch warning failed");
var newerImpact = PatchWatchLogic.BuildImpact(newer, latest.NotesUrl);
Check(newerImpact.Visible
      && newerImpact.Heading == "VERSION GUARD · PUBLIC PATCH"
      && newerImpact.ScopeLine.Contains("TERRAIN / ROUTES", StringComparison.Ordinal)
      && newerImpact.CopyText.Contains("combat, species abilities, and aim calibration", StringComparison.Ordinal)
      && newerImpact.CopyText.Contains(latest.NotesUrl, StringComparison.Ordinal),
    "public-patch impact checklist failed");
var untrustedImpact = PatchWatchLogic.BuildImpact(newer, "https://untrusted.example/notes");
Check(untrustedImpact.Visible
      && !untrustedImpact.CopyText.Contains("untrusted.example", StringComparison.Ordinal),
    "untrusted patch URL entered the copied checklist");

var lastGood = PatchWatchLogic.Evaluate(newerSnapshot, false, true, now.AddHours(2));
Check(lastGood.State == PatchWatchState.ReviewNeeded
      && lastGood.Heading.StartsWith("LAST GOOD", StringComparison.Ordinal)
      && lastGood.FreshnessLine.Contains("last good checked 2h ago", StringComparison.Ordinal)
      && lastGood.Detail.Contains("refresh failed", StringComparison.Ordinal),
    "last-good failure disclosure failed");

var serverAhead = PatchWatchLogic.Evaluate(
    latest,
    false,
    false,
    now,
    "evrima 0.21.738");
Check(serverAhead.State == PatchWatchState.ServerAhead
      && serverAhead.NeedsReview
      && serverAhead.ReviewVersion == "0.21.738"
      && serverAhead.Heading == "SERVER BUILD AHEAD"
      && serverAhead.VersionLine == "SERVER 0.21.738 · PUBLIC 0.21.734 · ISLEY 0.21.734"
      && serverAhead.Detail.Contains("newer build", StringComparison.Ordinal),
    "server-build divergence failed");
var serverImpact = PatchWatchLogic.BuildImpact(serverAhead, latest.NotesUrl);
Check(serverImpact.Visible
      && serverImpact.Heading == "VERSION GUARD · SERVER BUILD"
      && serverImpact.Detail.Contains("server rules", StringComparison.Ordinal)
      && serverImpact.CopyText.Contains("SERVER 0.21.738", StringComparison.Ordinal)
      && serverImpact.CopyText.Contains("server-specific rules, rates, and multipliers", StringComparison.Ordinal),
    "server-build impact checklist failed");

var serverAheadWithoutFeed = PatchWatchLogic.Evaluate(
    null,
    false,
    true,
    now,
    "0.21.738");
Check(serverAheadWithoutFeed.State == PatchWatchState.ServerAhead
      && !serverAheadWithoutFeed.HasNotes
      && serverAheadWithoutFeed.ReviewVersion == "0.21.738",
    "server-ahead unavailable-feed fallback failed");

var baselineAhead = PatchWatchLogic.Evaluate(latest with { Version = "0.21.720" }, false, false, now);
Check(baselineAhead.State == PatchWatchState.BaselineAhead
      && baselineAhead.Heading == "ISLEY BASELINE AHEAD",
    "baseline-ahead handling failed");

var checking = PatchWatchLogic.Evaluate(null, true, false, now);
var unavailable = PatchWatchLogic.Evaluate(null, false, true, now);
Check(checking.State == PatchWatchState.Checking && !checking.HasNotes,
    "first-check state failed");
Check(unavailable.State == PatchWatchState.Unavailable
      && unavailable.Detail.Contains("unverified", StringComparison.Ordinal),
    "unavailable state failed");
Check(PatchWatchLogic.FormatAge(TimeSpan.FromMinutes(-4)) == "just now"
      && PatchWatchLogic.FormatAge(TimeSpan.FromMinutes(59)) == "59m"
      && PatchWatchLogic.FormatAge(TimeSpan.FromHours(3.9)) == "3h"
      && PatchWatchLogic.FormatAge(TimeSpan.FromDays(2.2)) == "2d",
    "freshness formatting failed");

Console.WriteLine("Official Patch Watch: PASS (Steam parsing, trusted notes, semantic versions, impact checklist, baseline alignment, stale fallback, and refusal boundaries)");
