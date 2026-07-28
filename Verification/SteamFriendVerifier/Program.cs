using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var now = new DateTimeOffset(2026, 7, 21, 20, 0, 0, TimeSpan.Zero);
const string steamId = "76561198000000000";

Check(SteamFriendLogic.TryParseTarget(steamId, out var numeric), "SteamID64 should parse");
Check(numeric.SteamId64 == steamId, "SteamID64 should be retained");
Check(numeric.CanonicalProfileUrl == $"https://steamcommunity.com/profiles/{steamId}", "numeric canonical URL");
Check(SteamFriendLogic.TryParseTarget(
    $"http://www.steamcommunity.com/profiles/{steamId}/",
    out var profileUrl), "profile URL should parse");
Check(profileUrl.CanonicalProfileUrl == numeric.CanonicalProfileUrl, "profile URL canonicalization");
Check(SteamFriendLogic.TryParseTarget(
    "steamcommunity.com/id/Map_Friend-7",
    out var vanity), "vanity URL should parse");
Check(vanity.VanityName == "Map_Friend-7", "vanity name");

foreach (var invalid in new[]
         {
             "123456789",
             "https://example.com/profiles/76561198000000000",
             "https://steamcommunity.com.evil.test/profiles/76561198000000000",
             "https://user@steamcommunity.com/profiles/76561198000000000",
             "https://steamcommunity.com/profiles/76561198000000000?x=1",
             "https://steamcommunity.com/profiles/not-a-steam-id",
             "ftp://steamcommunity.com/profiles/76561198000000000"
         })
{
    Check(!SteamFriendLogic.TryParseTarget(invalid, out _), $"unsafe target accepted: {invalid}");
}

Check(SteamFriendLogic.TryCreateEntry(
    steamId,
    "  Pack\tFriend  ",
    now,
    out var created,
    out var error), $"entry creation failed: {error}");
Check(created.MapName == "Pack Friend", "map-name sanitation");
Check(created.Id.Length == 16, "stable entry id");
Check(SteamFriendLogic.BuildAddClientUri(created) == $"steam://friends/add/{steamId}", "direct add URI");
Check(SteamFriendLogic.BuildProfileClientUri(created) ==
      $"steam://openurl/https://steamcommunity.com/profiles/{steamId}", "profile client URI");

Check(!SteamFriendLogic.TryCreateEntry(steamId, "", now, out _, out _), "empty map name accepted");
Check(!SteamFriendLogic.TryCreateEntry("not steam", "Friend", now, out _, out _), "invalid Steam target accepted");
Check(SteamFriendLogic.FindLiveMatch("pack friend", new[] { "Other", "Pack Friend" }) == "Pack Friend",
    "case-insensitive live match");
Check(SteamFriendLogic.FindLiveMatch("Offline", new[] { "Other" }) is null, "offline match");

var autoOff = SteamFriendLogic.EvaluateAutoFollow(
    string.Empty, created, new[] { "Pack Friend" }, false, true, false, string.Empty);
Check(autoOff.State == SteamFriendAutoFollowState.Off && !autoOff.ShouldStart, "auto-follow off state");
var autoHidden = SteamFriendLogic.EvaluateAutoFollow(
    created.Id, created, new[] { "Pack Friend" }, true, true, false, string.Empty);
Check(autoHidden.State == SteamFriendAutoFollowState.Hidden, "streamer auto-follow suppression");
var autoServerPaused = SteamFriendLogic.EvaluateAutoFollow(
    created.Id, created, new[] { "Pack Friend" }, false, false, false, string.Empty);
Check(autoServerPaused.State == SteamFriendAutoFollowState.ServerPaused, "non-Live-Map auto-follow pause");
var autoWaiting = SteamFriendLogic.EvaluateAutoFollow(
    created.Id, created, new[] { "Other" }, false, true, false, string.Empty);
Check(autoWaiting.State == SteamFriendAutoFollowState.WaitingForFriend, "offline auto-follow wait");
var autoBusy = SteamFriendLogic.EvaluateAutoFollow(
    created.Id, created, new[] { "Pack Friend" }, false, true, true, string.Empty);
Check(autoBusy.State == SteamFriendAutoFollowState.RouteBusy, "unrelated-route arbitration");
var autoFollowing = SteamFriendLogic.EvaluateAutoFollow(
    created.Id, created, new[] { "Pack Friend" }, false, true, true, "pack friend");
Check(autoFollowing.State == SteamFriendAutoFollowState.Following && autoFollowing.LiveName == "Pack Friend",
    "existing matching route retention");
var autoReady = SteamFriendLogic.EvaluateAutoFollow(
    created.Id, created, new[] { "Other", "Pack Friend" }, false, true, false, string.Empty);
Check(autoReady.State == SteamFriendAutoFollowState.Ready
      && autoReady.ShouldStart
      && autoReady.LiveName == "Pack Friend", "auto-follow start decision");

var entries = Enumerable.Range(0, 18)
    .Select(index => new SteamFriendWatchEntry
    {
        ProfileUrl = $"https://steamcommunity.com/profiles/{76561198000000000UL + (ulong)index}",
        MapName = $"Friend {index}",
        AddedAtUnixMs = now.AddMinutes(index).ToUnixTimeMilliseconds()
    })
    .ToList();
entries.Add(new SteamFriendWatchEntry
{
    ProfileUrl = entries[17].ProfileUrl,
    MapName = "Duplicate profile",
    AddedAtUnixMs = now.AddMinutes(30).ToUnixTimeMilliseconds()
});
entries.Add(new SteamFriendWatchEntry
{
    ProfileUrl = "https://steamcommunity.com/id/unique-vanity",
    MapName = "Friend 17",
    AddedAtUnixMs = now.AddMinutes(29).ToUnixTimeMilliseconds()
});

var normalized = SteamFriendLogic.NormalizeEntries(entries, now.AddHours(2));
Check(normalized.Count == SteamFriendLogic.MaximumEntries, "watchlist cap");
Check(normalized.Select(entry => entry.ProfileUrl).Distinct(StringComparer.OrdinalIgnoreCase).Count() == normalized.Count,
    "profile deduplication");
Check(normalized.Select(entry => entry.MapName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == normalized.Count,
    "map-name deduplication");
Check(normalized.Zip(normalized.Skip(1), (left, right) => left.AddedAtUnixMs >= right.AddedAtUnixMs).All(value => value),
    "newest-first order");

var updated = SteamFriendLogic.Upsert(normalized, created, now.AddHours(3));
Check(updated[0].Id == created.Id, "upsert should prioritize the new watch");
Check(updated.Count <= SteamFriendLogic.MaximumEntries, "upsert cap");

Console.WriteLine("Steam friend watch verification passed (profile validation, trusted launch URIs, privacy bounds, dedupe, live-name matching, and conservative auto-follow arbitration).");
