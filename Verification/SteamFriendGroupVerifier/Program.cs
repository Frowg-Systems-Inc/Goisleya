using System.Security.Cryptography;
using System.Text;
using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static string ExpectedGroupId(string normalizedLowerName)
{
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
        $"isley-friend-group-v1\n{normalizedLowerName}"));
    return Convert.ToHexString(hash)[..16].ToLowerInvariant();
}

static SteamFriendGroupEntry Group(string name, long updatedAt, params string[] members) =>
    new()
    {
        Id = SteamFriendGroupLogic.StableGroupId(SteamFriendGroupLogic.NormalizeGroupName(name)),
        Name = name,
        MemberWatchIds = members.ToList(),
        UpdatedAtUnixMs = updatedAt
    };

var now = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
var nowMs = now.ToUnixTimeMilliseconds();
var alpha = new string('a', 16);
var bravo = new string('b', 16);
var carol = new string('c', 16);

Check(SteamFriendGroupLogic.MaximumGroups == 16, "the group roster stays bounded at sixteen");
Check(SteamFriendGroupLogic.MaximumTotalMembers == 64, "total memberships stay bounded at 64");
Check(SteamFriendGroupLogic.MaximumNameLength == 24, "group names stay short");
Check(SteamFriendGroupLogic.GroupIdLength == 16, "group ids stay 16 lowercase hex characters");

Check(SteamFriendGroupLogic.NormalizeGroupName("  Pack\t\nAlpha  ") == "Pack Alpha",
    "group names collapse whitespace");
Check(SteamFriendGroupLogic.NormalizeGroupName("badname") == "badname",
    "control characters are stripped");
Check(SteamFriendGroupLogic.NormalizeGroupName(null) == string.Empty, "null normalizes to empty");
Check(SteamFriendGroupLogic.NormalizeGroupName(new string('x', 40)).Length == 24,
    "names are capped at 24 characters");
Check(SteamFriendGroupLogic.NormalizeGroupName(new string('x', 23) + " yz") == new string('x', 23),
    "truncation trims trailing whitespace");

Check(SteamFriendGroupLogic.StableGroupId("Pack Alpha") == ExpectedGroupId("pack alpha"),
    "group ids pin the isley-friend-group-v1 hash domain over the lowercase normalized name");
Check(SteamFriendGroupLogic.IsValidGroupId(alpha), "computed ids validate");
Check(!SteamFriendGroupLogic.IsValidGroupId(alpha.ToUpperInvariant()), "uppercase ids are rejected");
Check(!SteamFriendGroupLogic.IsValidGroupId("xyz"), "short ids are rejected");
Check(!SteamFriendGroupLogic.IsValidGroupId(null), "null ids are rejected");

Check(!SteamFriendGroupLogic.TryCreateGroup("   ", null, now, out _, out var error)
      && error == "Enter a group name first",
    "blank names are refused with guidance");
var fullRoster = Enumerable.Range(0, 16).Select(index => Group($"Squad {index}", nowMs)).ToList();
Check(!SteamFriendGroupLogic.TryCreateGroup("New Squad", fullRoster, now, out _, out error)
      && error == "Group limit reached (16)",
    "the sixteen-group cap is enforced");
Check(!SteamFriendGroupLogic.TryCreateGroup("pack ALPHA ", new[] { Group("Pack Alpha", nowMs) }, now, out _, out error)
      && error == "A group with that name already exists",
    "duplicate names are refused case-insensitively after normalization");
Check(SteamFriendGroupLogic.TryCreateGroup("Pack Alpha", null, now, out var created, out error)
      && error.Length == 0
      && created.Id == ExpectedGroupId("pack alpha")
      && created.Name == "Pack Alpha"
      && created.UpdatedAtUnixMs == nowMs
      && created.MemberWatchIds.Count == 0,
    "a valid creation produces a stable id, normalized name, and fresh timestamp");

Check(SteamFriendGroupLogic.NormalizeGroups(null, null, now).Count == 0, "null normalizes to empty");
var normalizedGroups = SteamFriendGroupLogic.NormalizeGroups(new[]
{
    Group("Pack Alpha", nowMs, alpha, bravo),
    Group("Pack Alpha", nowMs - 1000, carol),
    Group("   ", nowMs, alpha),
    Group("Squad Bravo", nowMs - 10, alpha, alpha, "INVALID"),
    Group("Squad Carol", DateTimeOffset.UnixEpoch.ToUnixTimeMilliseconds(), carol)
}, new[] { alpha, bravo }, now);
Check(normalizedGroups.Count == 3, "duplicate and blank-named groups are dropped");
Check(normalizedGroups[0].Name == "Pack Alpha"
      && normalizedGroups[0].MemberWatchIds.SequenceEqual(new[] { alpha, bravo }),
    "members are kept in order against the valid watch roster");
Check(normalizedGroups[1].Name == "Squad Bravo"
      && normalizedGroups[1].MemberWatchIds.SequenceEqual(new[] { alpha }),
    "unknown, invalid, and duplicate members are pruned");
Check(normalizedGroups[2].UpdatedAtUnixMs == nowMs,
    "out-of-range timestamps are re-stamped to now");
Check(normalizedGroups.All(group => group.Id == SteamFriendGroupLogic.StableGroupId(group.Name)),
    "ids are recomputed from the normalized name");

var memberTotal = SteamFriendGroupLogic.NormalizeGroups(new[]
{
    Group("One", nowMs, Enumerable.Range(0, 40).Select(i => i.ToString("x2").PadLeft(16, '0')).ToArray()),
    Group("Two", nowMs, Enumerable.Range(40, 40).Select(i => i.ToString("x2").PadLeft(16, '0')).ToArray())
}, null, now);
Check(memberTotal.Sum(group => group.MemberWatchIds.Count) == 64,
    "the 64-membership total cap spans groups");

var editable = new List<SteamFriendGroupEntry> { Group("Pack Alpha", nowMs, alpha) };
Check(!SteamFriendGroupLogic.TryAddMember(editable, "nope", bravo, now, out error)
      && error == "Choose a group first",
    "adding to a missing group fails");
Check(!SteamFriendGroupLogic.TryAddMember(editable, editable[0].Id, "nope", now, out error)
      && error == "Choose a watched Steam friend first",
    "adding an invalid watch id fails");
Check(!SteamFriendGroupLogic.TryAddMember(editable, editable[0].Id, alpha, now, out error)
      && error == "That friend is already in this group",
    "adding a duplicate member fails");
var later = now.AddMinutes(1);
Check(SteamFriendGroupLogic.TryAddMember(editable, editable[0].Id, bravo, later, out error)
      && error.Length == 0
      && editable[0].MemberWatchIds.SequenceEqual(new[] { alpha, bravo })
      && editable[0].UpdatedAtUnixMs == later.ToUnixTimeMilliseconds(),
    "a valid add appends and bumps the timestamp");
var saturated = new List<SteamFriendGroupEntry>
{
    Group("Full", nowMs, Enumerable.Range(0, 64).Select(i => i.ToString("x2").PadLeft(16, '0')).ToArray())
};
Check(!SteamFriendGroupLogic.TryAddMember(saturated, saturated[0].Id, alpha, now, out error)
      && error == "Group member limit reached (64)",
    "the 64-membership cap blocks further adds");

var watchEntries = new[]
{
    new SteamFriendWatchEntry { Id = alpha, MapName = "Rex Runner" },
    new SteamFriendWatchEntry { Id = bravo, MapName = "Trike Tracker" },
    new SteamFriendWatchEntry { Id = carol, MapName = "Stego Scout" }
};
var liveNames = new[] { "rex runner", "someone else" };
Check(SteamFriendGroupLogic.CountLiveMembers(Group("Pack Alpha", nowMs, alpha, bravo, carol), watchEntries, liveNames) == 1,
    "only members live in the authorized roster count");
Check(SteamFriendGroupLogic.CountLiveMembers(Group("Pack Alpha", nowMs, alpha), null, liveNames) == 0,
    "missing watch entries count as zero");
Check(SteamFriendGroupLogic.CountLiveMembers(Group("Pack Alpha", nowMs, alpha), watchEntries, null) == 0,
    "missing live rosters count as zero");

Console.WriteLine(
    "Steam friend group verification passed (pinned id domain, name normalization, bounded roster and memberships, member editing, and live-count honesty).");
