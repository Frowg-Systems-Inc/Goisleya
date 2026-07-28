using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Isley;

internal sealed class SteamFriendWatchEntry
{
    public string Id { get; set; } = string.Empty;
    public string ProfileUrl { get; set; } = string.Empty;
    public string SteamId64 { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public long AddedAtUnixMs { get; set; }
}

internal readonly record struct SteamFriendTarget(
    string CanonicalProfileUrl,
    string SteamId64,
    string VanityName)
{
    internal bool HasSteamId64 => !string.IsNullOrEmpty(SteamId64);

    internal string DisplayIdentity => HasSteamId64 ? SteamId64 : VanityName;
}

internal enum SteamFriendAutoFollowState
{
    Off,
    Hidden,
    ServerPaused,
    WaitingForFriend,
    RouteBusy,
    Following,
    Ready
}

internal readonly record struct SteamFriendAutoFollowDecision(
    SteamFriendAutoFollowState State,
    string LiveName)
{
    internal bool ShouldStart => State == SteamFriendAutoFollowState.Ready;
}

internal static class SteamFriendLogic
{
    internal const int MaximumEntries = 12;

    private static readonly Regex SteamId64Pattern = new(
        @"^7656119\d{10}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex VanityPattern = new(
        @"^[A-Za-z0-9_-]{2,64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static bool TryParseTarget(string? input, out SteamFriendTarget target)
    {
        target = default;
        var candidate = (input ?? string.Empty).Trim();
        if (candidate.Length == 0
            || candidate.Length > 180
            || candidate.Any(char.IsControl))
        {
            return false;
        }

        if (IsSteamId64(candidate))
        {
            target = new SteamFriendTarget(
                $"https://steamcommunity.com/profiles/{candidate}",
                candidate,
                string.Empty);
            return true;
        }

        if (candidate.StartsWith("steamcommunity.com/", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("www.steamcommunity.com/", StringComparison.OrdinalIgnoreCase))
        {
            candidate = $"https://{candidate}";
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || (!string.Equals(uri.Host, "steamcommunity.com", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Host, "www.steamcommunity.com", StringComparison.OrdinalIgnoreCase))
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 2)
        {
            return false;
        }

        if (string.Equals(segments[0], "profiles", StringComparison.OrdinalIgnoreCase)
            && IsSteamId64(segments[1]))
        {
            target = new SteamFriendTarget(
                $"https://steamcommunity.com/profiles/{segments[1]}",
                segments[1],
                string.Empty);
            return true;
        }

        if (string.Equals(segments[0], "id", StringComparison.OrdinalIgnoreCase)
            && VanityPattern.IsMatch(segments[1]))
        {
            target = new SteamFriendTarget(
                $"https://steamcommunity.com/id/{segments[1]}",
                string.Empty,
                segments[1]);
            return true;
        }

        return false;
    }

    internal static bool TryCreateEntry(
        string? profileInput,
        string? mapNameInput,
        DateTimeOffset now,
        out SteamFriendWatchEntry entry,
        out string error)
    {
        entry = new SteamFriendWatchEntry();
        error = string.Empty;
        if (!TryParseTarget(profileInput, out var target))
        {
            error = "Enter a SteamID64 or Steam Community profile URL";
            return false;
        }

        var mapName = NormalizeMapName(mapNameInput);
        if (mapName.Length == 0)
        {
            error = "Enter the exact name shown on the authorized live map";
            return false;
        }

        entry = new SteamFriendWatchEntry
        {
            Id = StableId(target.CanonicalProfileUrl, mapName),
            ProfileUrl = target.CanonicalProfileUrl,
            SteamId64 = target.SteamId64,
            MapName = mapName,
            AddedAtUnixMs = now.ToUnixTimeMilliseconds()
        };
        return true;
    }

    internal static List<SteamFriendWatchEntry> Upsert(
        IEnumerable<SteamFriendWatchEntry>? entries,
        SteamFriendWatchEntry candidate,
        DateTimeOffset now)
    {
        if (!TryCreateEntry(candidate.ProfileUrl, candidate.MapName, now, out var freshCandidate, out _))
        {
            return NormalizeEntries(entries, now);
        }

        var retained = NormalizeEntries(entries, now)
            .Where(entry => !string.Equals(
                                entry.ProfileUrl,
                                freshCandidate.ProfileUrl,
                                StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(
                                entry.MapName,
                                freshCandidate.MapName,
                                StringComparison.OrdinalIgnoreCase))
            .ToList();
        retained.Insert(0, freshCandidate);
        return NormalizeEntries(retained, now);
    }

    internal static List<SteamFriendWatchEntry> NormalizeEntries(
        IEnumerable<SteamFriendWatchEntry>? entries,
        DateTimeOffset now)
    {
        if (entries is null)
        {
            return [];
        }

        var minimumTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var maximumTime = now.AddDays(1).ToUnixTimeMilliseconds();
        var seenProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenMapNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<SteamFriendWatchEntry>();

        foreach (var source in entries
                     .Where(entry => entry is not null)
                     .OrderByDescending(entry => entry.AddedAtUnixMs))
        {
            if (!TryParseTarget(source.ProfileUrl, out var target))
            {
                continue;
            }

            var mapName = NormalizeMapName(source.MapName);
            if (mapName.Length == 0
                || !seenProfiles.Add(target.CanonicalProfileUrl)
                || !seenMapNames.Add(mapName))
            {
                continue;
            }

            var addedAt = source.AddedAtUnixMs is >= 0
                          && source.AddedAtUnixMs >= minimumTime
                          && source.AddedAtUnixMs <= maximumTime
                ? source.AddedAtUnixMs
                : now.ToUnixTimeMilliseconds();
            normalized.Add(new SteamFriendWatchEntry
            {
                Id = StableId(target.CanonicalProfileUrl, mapName),
                ProfileUrl = target.CanonicalProfileUrl,
                SteamId64 = target.SteamId64,
                MapName = mapName,
                AddedAtUnixMs = addedAt
            });
            if (normalized.Count >= MaximumEntries)
            {
                break;
            }
        }

        return normalized;
    }

    internal static string? FindLiveMatch(string? mapName, IEnumerable<string>? liveNames)
    {
        var normalizedName = NormalizeMapName(mapName);
        if (normalizedName.Length == 0 || liveNames is null)
        {
            return null;
        }

        return liveNames
            .Select(NormalizeMapName)
            .FirstOrDefault(name => string.Equals(name, normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    internal static SteamFriendAutoFollowDecision EvaluateAutoFollow(
        string? autoFollowWatchId,
        SteamFriendWatchEntry? watchedEntry,
        IEnumerable<string>? liveNames,
        bool streamerMode,
        bool liveMapServicesActive,
        bool routeBusy,
        string? activeFriendRouteName)
    {
        if (watchedEntry is null
            || string.IsNullOrWhiteSpace(autoFollowWatchId)
            || !string.Equals(autoFollowWatchId, watchedEntry.Id, StringComparison.Ordinal))
        {
            return new SteamFriendAutoFollowDecision(SteamFriendAutoFollowState.Off, string.Empty);
        }

        if (streamerMode)
        {
            return new SteamFriendAutoFollowDecision(SteamFriendAutoFollowState.Hidden, string.Empty);
        }

        if (!liveMapServicesActive)
        {
            return new SteamFriendAutoFollowDecision(SteamFriendAutoFollowState.ServerPaused, string.Empty);
        }

        var liveName = FindLiveMatch(watchedEntry.MapName, liveNames) ?? string.Empty;
        if (liveName.Length == 0)
        {
            return new SteamFriendAutoFollowDecision(
                SteamFriendAutoFollowState.WaitingForFriend,
                string.Empty);
        }

        if (string.Equals(activeFriendRouteName, liveName, StringComparison.OrdinalIgnoreCase))
        {
            return new SteamFriendAutoFollowDecision(SteamFriendAutoFollowState.Following, liveName);
        }

        if (routeBusy || !string.IsNullOrWhiteSpace(activeFriendRouteName))
        {
            return new SteamFriendAutoFollowDecision(SteamFriendAutoFollowState.RouteBusy, liveName);
        }

        return new SteamFriendAutoFollowDecision(SteamFriendAutoFollowState.Ready, liveName);
    }

    internal static string? BuildAddClientUri(SteamFriendWatchEntry? entry) =>
        entry is not null && TryParseTarget(entry.ProfileUrl, out var target) && target.HasSteamId64
            ? $"steam://friends/add/{target.SteamId64}"
            : null;

    internal static string? BuildProfileClientUri(SteamFriendWatchEntry? entry) =>
        entry is not null && TryParseTarget(entry.ProfileUrl, out var target)
            ? $"steam://openurl/{target.CanonicalProfileUrl}"
            : null;

    internal static string NormalizeMapName(string? value)
    {
        var sanitized = new string((value ?? string.Empty)
            .Where(character => !char.IsControl(character) || char.IsWhiteSpace(character))
            .Select(character => char.IsWhiteSpace(character) ? ' ' : character)
            .ToArray());
        sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
        return sanitized.Length <= 64 ? sanitized : sanitized[..64].TrimEnd();
    }

    private static bool IsSteamId64(string value) =>
        SteamId64Pattern.IsMatch(value)
        && ulong.TryParse(value, out _);

    private static string StableId(string profileUrl, string mapName)
    {
        var key = $"{profileUrl.ToLowerInvariant()}\n{mapName.ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
