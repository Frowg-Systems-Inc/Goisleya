using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string Normalize(string value)
{
    Check(CommunityServerWatchLogic.TryNormalizeAddress(value, out var normalized), $"valid address: {value}");
    return normalized;
}

Check(Normalize(" 203.0.113.10:7777 ") == "203.0.113.10:7777", "IPv4 normalization");
Check(Normalize("PLAY.Example.COM:7777") == "play.example.com:7777", "DNS normalization");
Check(Normalize("[2001:db8::1]:7777") == "[2001:db8::1]:7777", "IPv6 normalization");
Check(!CommunityServerWatchLogic.TryNormalizeAddress("https://example.com:7777", out _), "scheme refusal");
Check(!CommunityServerWatchLogic.TryNormalizeAddress("example.com:7777/path", out _), "path refusal");
Check(!CommunityServerWatchLogic.TryNormalizeAddress("user@example.com:7777", out _), "userinfo refusal");
Check(!CommunityServerWatchLogic.TryNormalizeAddress("example.com:0", out _), "zero port refusal");
Check(!CommunityServerWatchLogic.TryNormalizeAddress("example.com:65536", out _), "high port refusal");
Check(!CommunityServerWatchLogic.TryNormalizeAddress("example..com:7777", out _), "empty DNS label refusal");
Check(!CommunityServerWatchLogic.TryNormalizeAddress("-example.com:7777", out _), "leading hyphen refusal");
Check(!CommunityServerWatchLogic.TryNormalizeAddress("example.com", out _), "missing port refusal");
Check(CommunityServerWatchLogic.SanitizeAddressInput("  host\u0001:7777  ") == "host:7777", "control stripping");
Check(CommunityServerWatchLogic.SanitizeAddressInput(new string('x', 120)).Length == 96, "input bound");
Check(IsleServerStatusClient.BuildStatusEndpoint("PLAY.Example.COM:7777") ==
      "https://api.gamemonitoring.net/servers?limit=5&game=376210&connect=play.example.com%3A7777",
    "fixed provider endpoint");

const string publicResponse = """
    {"response":{"items":[
      {"name":"Raptor Realm","status":true,"numplayers":99,"maxplayers":100,
       "map":"Gateway","version":"0.21.734","connect":"play.example.com:7777",
       "game":376210,"last_update":1784678400},
      {"name":"Wrong Game","status":true,"numplayers":1,"maxplayers":10,
       "map":"Gateway","version":"x","connect":"play.example.com:7777",
       "game":1,"last_update":0}
    ]}}
    """;
var parsed = IsleServerStatusClient.ParsePublic(
    publicResponse,
    new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero),
    "PLAY.Example.COM:7777",
    "Community server");
Check(parsed.Online && parsed.Players == 99 && parsed.Capacity == 100, "public population parse");
Check(parsed.ConnectAddress == "play.example.com:7777", "exact normalized match");
Check(parsed.DisplayName == "Raptor Realm" && parsed.Map == "Gateway", "public metadata parse");
try
{
    IsleServerStatusClient.ParsePublic(publicResponse, DateTimeOffset.UtcNow,
        "other.example.com:7777", "Other");
    throw new InvalidOperationException("exact address mismatch accepted");
}
catch (InvalidDataException)
{
}

const string invalidPopulationResponse = """
    {"response":{"items":[
      {"name":"Broken","status":true,"numplayers":101,"maxplayers":100,
       "map":"Gateway","version":"x","connect":"play.example.com:7777",
       "game":376210,"last_update":0}
    ]}}
    """;
try
{
    IsleServerStatusClient.ParsePublic(invalidPopulationResponse, DateTimeOffset.UtcNow,
        "play.example.com:7777", "Community server");
    throw new InvalidOperationException("invalid population accepted");
}
catch (InvalidDataException)
{
}

var initialOpen = CommunityServerWatchLogic.EvaluateSlotTransition(null, true, true, 99, 100);
Check(!initialOpen.Alert && !initialOpen.IsFull && initialOpen.OpenSlots == 1, "no startup alert");
var full = CommunityServerWatchLogic.EvaluateSlotTransition(false, true, true, 100, 100);
Check(!full.Alert && full.IsFull && full.OpenSlots == 0, "full rearm state");
var opened = CommunityServerWatchLogic.EvaluateSlotTransition(true, true, true, 99, 100);
Check(opened.Alert && !opened.IsFull && opened.OpenSlots == 1, "slot transition alert");
var muted = CommunityServerWatchLogic.EvaluateSlotTransition(true, false, true, 99, 100);
Check(!muted.Alert, "disabled alert boundary");
var offline = CommunityServerWatchLogic.EvaluateSlotTransition(true, true, false, 0, 100);
Check(!offline.Alert && offline.OpenSlots == 0, "offline boundary");
var invalid = CommunityServerWatchLogic.EvaluateSlotTransition(true, true, true, 101, 100);
Check(!invalid.Alert && invalid.OpenSlots == 0, "invalid population boundary");

var migratedProfiles = CommunityServerWatchLogic.NormalizeProfiles(
    null,
    "Raptor Realm",
    "PLAY.Example.COM:7777",
    true,
    true,
    2);
Check(migratedProfiles.Count == 1, "legacy singleton migration count");
Check(migratedProfiles[0].Name == "Raptor Realm", "legacy name migration");
Check(migratedProfiles[0].Address == "play.example.com:7777", "legacy address migration");
Check(migratedProfiles[0].WatchEnabled && migratedProfiles[0].SlotAlertEnabled,
    "legacy watch migration");
Check(migratedProfiles[0].GrowthMultiplierIndex == 2, "legacy growth migration");
var defaultProfiles = CommunityServerWatchLogic.NormalizeProfiles(
    null,
    null,
    null,
    false,
    false,
    -1);
Check(defaultProfiles.Count == 1
      && defaultProfiles[0].Name == "Any Isle server"
      && string.IsNullOrEmpty(defaultProfiles[0].Address)
      && !defaultProfiles[0].WatchEnabled,
    "private or unlisted server requires no name, address, or public watch");

var rawProfiles = Enumerable.Range(0, 8)
    .Select(index => new CommunityServerProfileSettings
    {
        Id = index < 2 ? "duplicate" : $"Server {index}",
        Name = index == 1 ? "  Second   Server " : $"Server {index}",
        Address = index == 2 ? "bad address" : $"play{index}.example.com:7777",
        WatchEnabled = true,
        SlotAlertEnabled = index % 2 == 0,
        GrowthMultiplierIndex = index,
        IsleyJoinLink = index == 1
            ? "https://relay.example/join/second-server"
            : index == 3
                ? "https://relay.example/join/bad\u0001id"
                : index == 4
                    ? new string('j', 1100)
                    : string.Empty
    })
    .ToList();
var normalizedProfiles = CommunityServerWatchLogic.NormalizeProfiles(
    rawProfiles, "legacy", string.Empty, false, false, -1);
Check(normalizedProfiles.Count == CommunityServerWatchLogic.MaximumProfiles, "saved profile cap");
Check(normalizedProfiles.Select(profile => profile.Id).Distinct().Count() == normalizedProfiles.Count,
    "unique normalized profile ids");
Check(normalizedProfiles[1].Name == "Second Server", "saved name normalization");
Check(normalizedProfiles[1].IsleyJoinLink == "https://relay.example/join/second-server",
    "saved Isley join link preserved");
Check(normalizedProfiles[3].IsleyJoinLink == "https://relay.example/join/badid",
    "control characters removed from Isley join link");
Check(string.IsNullOrEmpty(normalizedProfiles[4].IsleyJoinLink),
    "oversized Isley join link rejected");
Check(!normalizedProfiles[2].WatchEnabled && normalizedProfiles[2].Address == "bad address",
    "invalid saved address disables watch but remains editable");
Check(normalizedProfiles[^1].GrowthMultiplierIndex == 4, "saved growth upper bound");
Check(CommunityServerWatchLogic.FindProfileIndex(normalizedProfiles, normalizedProfiles[3].Id) == 3,
    "saved selection restoration");
Check(CommunityServerWatchLogic.FindProfileIndex(normalizedProfiles, "missing") == 0,
    "missing saved selection fallback");
Check(CommunityServerWatchLogic.MoveProfileIndex(3, 0, -1) == 2,
    "previous profile wrap");
Check(CommunityServerWatchLogic.MoveProfileIndex(3, 2, 1) == 0,
    "next profile wrap");
var createdProfile = CommunityServerWatchLogic.CreateProfile(normalizedProfiles.Take(2).ToList());
Check(normalizedProfiles.Take(2).All(profile => profile.Id != createdProfile.Id),
    "new profile unique id");
Check(createdProfile.Name.StartsWith("Any Isle server", StringComparison.Ordinal),
    "new profile uses universal Any Server language");
var removal = CommunityServerWatchLogic.RemoveProfileAt(normalizedProfiles.Take(3).ToList(), 1);
Check(removal.Profiles.Count == 2 && removal.SelectedIndex == 1,
    "selected profile removal handoff");
var onlyProfile = CommunityServerWatchLogic.RemoveProfileAt(migratedProfiles, 0);
Check(onlyProfile.Profiles.Count == 1 && onlyProfile.SelectedIndex == 0,
    "last profile cannot be removed");

Console.WriteLine(
    "Any Server public-status verification passed (optional name/address, private/unlisted defaults, " +
    "saved profiles, migration, fixed-provider parsing, safety boundaries, and slot transitions).");
