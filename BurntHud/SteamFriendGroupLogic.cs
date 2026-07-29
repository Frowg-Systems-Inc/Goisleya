using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Isley;

internal sealed class SteamFriendGroupEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> MemberWatchIds { get; set; } = [];
    public long UpdatedAtUnixMs { get; set; }
}

// Named squads over the existing Steam friend watch entries. Members are
// referenced by the opaque 16-hex watch entry id (never by display name) and
// group-level presence reuses the same authorized-live-name matching as the
// watchlist itself. Bounded: at most 16 groups and 64 total memberships.
internal static class SteamFriendGroupLogic
{
    internal const int MaximumGroups = 16;
    internal const int MaximumTotalMembers = 64;
    internal const int MaximumNameLength = 24;
    internal const int GroupIdLength = 16;

    internal static string NormalizeGroupName(string? value)
    {
        var sanitized = new string((value ?? string.Empty)
            .Where(character => !char.IsControl(character) || char.IsWhiteSpace(character))
            .Select(character => char.IsWhiteSpace(character) ? ' ' : character)
            .ToArray());
        sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
        return sanitized.Length <= MaximumNameLength
            ? sanitized
            : sanitized[..MaximumNameLength].TrimEnd();
    }

    internal static string StableGroupId(string normalizedName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"isley-friend-group-v1\n{normalizedName.ToLowerInvariant()}"));
        return Convert.ToHexString(hash)[..GroupIdLength].ToLowerInvariant();
    }

    internal static bool IsValidGroupId(string? value) =>
        value is { Length: GroupIdLength }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool TryCreateGroup(
        string? nameInput,
        IEnumerable<SteamFriendGroupEntry>? existing,
        DateTimeOffset now,
        out SteamFriendGroupEntry group,
        out string error)
    {
        group = new SteamFriendGroupEntry();
        error = string.Empty;
        var name = NormalizeGroupName(nameInput);
        if (name.Length == 0)
        {
            error = "Enter a group name first";
            return false;
        }

        var retained = existing?.Where(entry => entry is not null).ToList() ?? [];
        if (retained.Count >= MaximumGroups)
        {
            error = $"Group limit reached ({MaximumGroups})";
            return false;
        }

        if (retained.Any(entry => string.Equals(
                NormalizeGroupName(entry.Name),
                name,
                StringComparison.OrdinalIgnoreCase)))
        {
            error = "A group with that name already exists";
            return false;
        }

        group = new SteamFriendGroupEntry
        {
            Id = StableGroupId(name),
            Name = name,
            UpdatedAtUnixMs = now.ToUnixTimeMilliseconds()
        };
        return true;
    }

    internal static List<SteamFriendGroupEntry> NormalizeGroups(
        IEnumerable<SteamFriendGroupEntry>? groups,
        IEnumerable<string>? validWatchIds,
        DateTimeOffset now)
    {
        if (groups is null)
        {
            return [];
        }

        var minimumTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var maximumTime = now.AddDays(1).ToUnixTimeMilliseconds();
        var validIds = validWatchIds is null
            ? null
            : new HashSet<string>(validWatchIds.Where(IsValidGroupId), StringComparer.Ordinal);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<SteamFriendGroupEntry>();
        var totalMembers = 0;
        foreach (var source in groups
                     .Where(entry => entry is not null)
                     .OrderByDescending(entry => entry.UpdatedAtUnixMs))
        {
            var name = NormalizeGroupName(source.Name);
            if (name.Length == 0 || !seenNames.Add(name))
            {
                continue;
            }

            var members = (source.MemberWatchIds ?? [])
                .Where(id => IsValidGroupId(id) && (validIds is null || validIds.Contains(id)))
                .Distinct(StringComparer.Ordinal)
                .Take(Math.Max(0, MaximumTotalMembers - totalMembers))
                .ToList();
            totalMembers += members.Count;
            normalized.Add(new SteamFriendGroupEntry
            {
                Id = StableGroupId(name),
                Name = name,
                MemberWatchIds = members,
                UpdatedAtUnixMs = source.UpdatedAtUnixMs >= minimumTime
                                  && source.UpdatedAtUnixMs <= maximumTime
                    ? source.UpdatedAtUnixMs
                    : now.ToUnixTimeMilliseconds()
            });
            if (normalized.Count >= MaximumGroups || totalMembers >= MaximumTotalMembers)
            {
                break;
            }
        }

        return normalized;
    }

    internal static bool TryAddMember(
        List<SteamFriendGroupEntry> groups,
        string groupId,
        string? watchId,
        DateTimeOffset now,
        out string error)
    {
        error = string.Empty;
        var group = groups.FirstOrDefault(entry =>
            string.Equals(entry.Id, groupId, StringComparison.Ordinal));
        if (group is null)
        {
            error = "Choose a group first";
            return false;
        }

        if (!IsValidGroupId(watchId))
        {
            error = "Choose a watched Steam friend first";
            return false;
        }

        if (group.MemberWatchIds.Any(id => string.Equals(id, watchId, StringComparison.Ordinal)))
        {
            error = "That friend is already in this group";
            return false;
        }

        if (groups.Sum(entry => entry.MemberWatchIds.Count) >= MaximumTotalMembers)
        {
            error = $"Group member limit reached ({MaximumTotalMembers})";
            return false;
        }

        group.MemberWatchIds.Add(watchId!);
        group.UpdatedAtUnixMs = now.ToUnixTimeMilliseconds();
        return true;
    }

    internal static int CountLiveMembers(
        SteamFriendGroupEntry group,
        IEnumerable<SteamFriendWatchEntry>? watchEntries,
        IEnumerable<string>? liveNames)
    {
        if (watchEntries is null || liveNames is null)
        {
            return 0;
        }

        var liveCount = 0;
        foreach (var memberId in group.MemberWatchIds)
        {
            var entry = watchEntries.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, memberId, StringComparison.Ordinal));
            if (entry is not null
                && SteamFriendLogic.FindLiveMatch(entry.MapName, liveNames) is not null)
            {
                liveCount++;
            }
        }

        return liveCount;
    }
}
