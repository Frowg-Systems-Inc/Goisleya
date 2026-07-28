using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Isley;

internal sealed record OfficialPatchSnapshot(
    string Version,
    string Title,
    string AnnouncementId,
    string NotesUrl,
    DateTimeOffset PublishedAt,
    DateTimeOffset RetrievedAt);

internal static class OfficialPatchWatchClient
{
    internal const string NewsEndpoint =
        "https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=376210&count=20&maxlength=1200&format=json";
    internal const string NewsSourcePage = "https://steamcommunity.com/app/376210/announcements/";
    internal const int MaxPayloadBytes = 512 * 1024;

    private static readonly Regex PatchTitlePattern = new(
        @"\bPatch\s+(?<version>\d{1,3}\.\d{1,3}\.\d{1,6})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex AnnouncementIdPattern = new(
        @"^\d{6,24}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HttpClient Client = CreateClient();

    internal static async Task<OfficialPatchSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, NewsEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxPayloadBytes)
        {
            throw new InvalidDataException("The official patch response exceeded the size limit.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var block = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(block.AsMemory(0, block.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > MaxPayloadBytes)
            {
                throw new InvalidDataException("The official patch response exceeded the size limit.");
            }
            buffer.Write(block, 0, read);
        }

        var json = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
        return Parse(json, DateTimeOffset.Now);
    }

    internal static OfficialPatchSnapshot Parse(string json, DateTimeOffset retrievedAt)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxPayloadBytes)
        {
            throw new InvalidDataException("The official patch response was empty or oversized.");
        }

        var envelope = JsonSerializer.Deserialize<NewsEnvelope>(json)
                       ?? throw new InvalidDataException("The official patch response was empty.");
        if (envelope.AppNews?.AppId != 376210)
        {
            throw new InvalidDataException("The official patch response was for a different game.");
        }

        var news = envelope.AppNews.NewsItems
                   ?? throw new InvalidDataException("The official patch response had no news list.");
        if (news.Count is < 1 or > 50)
        {
            throw new InvalidDataException("The official patch response had an invalid item count.");
        }

        var candidates = new List<OfficialPatchSnapshot>();
        foreach (var item in news)
        {
            var title = CleanTitle(item.Title);
            var match = PatchTitlePattern.Match(title);
            if (!match.Success
                || item.AppId != 376210
                || !string.Equals(
                    item.FeedName,
                    "steam_community_announcements",
                    StringComparison.Ordinal)
                || item.Tags is null
                || !item.Tags.Contains("patchnotes", StringComparer.OrdinalIgnoreCase)
                || !PatchWatchLogic.TryParseVersion(match.Groups["version"].Value, out _)
                || !AnnouncementIdPattern.IsMatch(item.Gid ?? string.Empty)
                || item.Date <= 0)
            {
                continue;
            }

            DateTimeOffset publishedAt;
            try
            {
                publishedAt = DateTimeOffset.FromUnixTimeSeconds(item.Date).ToLocalTime();
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }

            if (publishedAt < new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)
                || publishedAt > retrievedAt.AddDays(1))
            {
                continue;
            }

            var announcementId = item.Gid!;
            candidates.Add(new OfficialPatchSnapshot(
                match.Groups["version"].Value,
                title,
                announcementId,
                $"https://steamcommunity.com/ogg/376210/announcements/detail/{announcementId}",
                publishedAt,
                retrievedAt));
        }

        return candidates
                   .OrderByDescending(candidate => candidate.PublishedAt)
                   .ThenByDescending(candidate => candidate.Version, StringComparer.Ordinal)
                   .FirstOrDefault()
               ?? throw new InvalidDataException("No valid public-branch patch announcement was found.");
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Isley/1.0");
        return client;
    }

    private static string CleanTitle(string? value)
    {
        var withoutControls = Regex.Replace(value ?? string.Empty, @"[\u0000-\u001F\u007F]+", " ");
        var normalized = Regex.Replace(withoutControls, @"\s+", " ").Trim();
        return normalized.Length <= 140 ? normalized : normalized[..140];
    }

    private sealed class NewsEnvelope
    {
        [JsonPropertyName("appnews")]
        public AppNews? AppNews { get; set; }
    }

    private sealed class AppNews
    {
        [JsonPropertyName("appid")]
        public int AppId { get; set; }

        [JsonPropertyName("newsitems")]
        public List<NewsItem>? NewsItems { get; set; }
    }

    private sealed class NewsItem
    {
        [JsonPropertyName("gid")]
        public string? Gid { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("date")]
        public long Date { get; set; }

        [JsonPropertyName("feedname")]
        public string? FeedName { get; set; }

        [JsonPropertyName("appid")]
        public int AppId { get; set; }

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }
    }
}

internal enum PatchWatchState
{
    Checking,
    Current,
    ReviewNeeded,
    ServerAhead,
    BaselineAhead,
    Unavailable
}

internal readonly record struct PatchWatchGuidance(
    PatchWatchState State,
    string Heading,
    string VersionLine,
    string FreshnessLine,
    string Detail,
    bool HasNotes,
    string ReviewVersion)
{
    internal bool NeedsReview => State is PatchWatchState.ReviewNeeded or PatchWatchState.ServerAhead;
}

internal readonly record struct PatchImpactGuidance(
    bool Visible,
    string Heading,
    string Detail,
    string ScopeLine,
    string CopyText);

internal static class PatchWatchLogic
{
    internal static bool TryParseVersion(string? value, out (int Major, int Minor, int Build) version)
    {
        version = default;
        var parts = (value ?? string.Empty).Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var build)
            || major is < 0 or > 999999
            || minor is < 0 or > 999999
            || build is < 0 or > 999999)
        {
            return false;
        }

        version = (major, minor, build);
        return true;
    }

    internal static int CompareVersions(string left, string right)
    {
        if (!TryParseVersion(left, out var a) || !TryParseVersion(right, out var b))
        {
            throw new ArgumentException("Patch versions must contain exactly three numeric components.");
        }
        var major = a.Major.CompareTo(b.Major);
        if (major != 0) return major;
        var minor = a.Minor.CompareTo(b.Minor);
        return minor != 0 ? minor : a.Build.CompareTo(b.Build);
    }

    internal static PatchWatchGuidance Evaluate(
        OfficialPatchSnapshot? latest,
        bool refreshing,
        bool lastRefreshFailed,
        DateTimeOffset now,
        string? observedServerVersion = null)
    {
        var hasServerVersion = TryExtractVersion(observedServerVersion, out var serverVersion);
        var serverAheadOfBaseline = hasServerVersion
                                    && CompareVersions(serverVersion, IsleContentBaseline.PublicBranch) > 0;
        if (latest is null)
        {
            if (serverAheadOfBaseline)
            {
                return new PatchWatchGuidance(
                    PatchWatchState.ServerAhead,
                    "SERVER BUILD AHEAD",
                    $"SERVER {serverVersion} · ISLEY {IsleContentBaseline.PublicBranch}",
                    refreshing
                        ? "Official Steam news · checking public patch"
                        : "Official patch unavailable · server version is public listing metadata",
                    "This server reports a newer build than Isley's reviewed guidance. Verify server rules and official notes before trusting update-sensitive advice.",
                    false,
                    serverVersion);
            }
            return refreshing
                ? new PatchWatchGuidance(
                    PatchWatchState.Checking,
                    "CHECKING OFFICIAL PATCH",
                    $"ISLEY GUIDES · {IsleContentBaseline.PublicBranch}",
                    "Official Steam news · waiting for first check",
                    "Looking for the newest public-branch patch announcement.",
                    false,
                    string.Empty)
                : new PatchWatchGuidance(
                    PatchWatchState.Unavailable,
                    "PATCH CHECK UNAVAILABLE",
                    $"ISLEY GUIDES · {IsleContentBaseline.PublicBranch}",
                    "No official snapshot · refresh or open announcements",
                    "Guide content remains usable, but current-patch alignment is unverified.",
                    false,
                    string.Empty);
        }

        var comparison = CompareVersions(latest.Version, IsleContentBaseline.PublicBranch);
        var serverAheadOfOfficial = hasServerVersion
                                    && CompareVersions(serverVersion, latest.Version) > 0;
        var state = serverAheadOfBaseline && serverAheadOfOfficial
            ? PatchWatchState.ServerAhead
            : comparison > 0
            ? PatchWatchState.ReviewNeeded
            : comparison == 0
                ? PatchWatchState.Current
                : PatchWatchState.BaselineAhead;
        var heading = state switch
        {
            PatchWatchState.ServerAhead => "SERVER BUILD AHEAD",
            PatchWatchState.ReviewNeeded => $"REVIEW PATCH {latest.Version}",
            PatchWatchState.Current => "GUIDES MATCH PUBLIC",
            _ => "ISLEY BASELINE AHEAD"
        };
        if (lastRefreshFailed)
        {
            heading = $"LAST GOOD · {heading}";
        }

        var detail = state switch
        {
            PatchWatchState.ServerAhead =>
                "This server reports a newer build than both Steam's newest patch announcement and Isley's reviewed guidance. Verify server rules and official notes before trusting update-sensitive advice.",
            PatchWatchState.ReviewNeeded =>
                "A newer public build is live. Check official notes before trusting update-sensitive controls, mutations, timers, or route assumptions.",
            PatchWatchState.Current =>
                "Isley's embedded update-sensitive guidance matches the newest official public patch announcement.",
            _ =>
                "Isley's reviewed baseline is newer than Steam's newest returned patch announcement; verify the feed before changing guides."
        };
        if (lastRefreshFailed)
        {
            detail += " The latest refresh failed, so this is the last good official snapshot.";
        }

        var publishedDate = latest.PublishedAt.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        var checkAge = now - latest.RetrievedAt;
        var freshness = lastRefreshFailed
            ? $"Published {publishedDate} · last good checked {FormatAge(checkAge)} ago"
            : $"Published {publishedDate} · checked {FormatAge(checkAge)} ago";
        var versionLine = hasServerVersion
            ? $"SERVER {serverVersion} · PUBLIC {latest.Version} · ISLEY {IsleContentBaseline.PublicBranch}"
            : $"PUBLIC {latest.Version} · ISLEY {IsleContentBaseline.PublicBranch}";
        var reviewVersion = state switch
        {
            PatchWatchState.ServerAhead => serverVersion,
            PatchWatchState.ReviewNeeded => latest.Version,
            _ => string.Empty
        };
        return new PatchWatchGuidance(
            state,
            heading,
            versionLine,
            freshness,
            detail,
            true,
            reviewVersion);
    }

    internal static PatchImpactGuidance BuildImpact(
        PatchWatchGuidance guidance,
        string? officialNotesUrl = null)
    {
        if (!guidance.NeedsReview)
        {
            return new PatchImpactGuidance(
                false,
                "VERSION GUARD · ALIGNED",
                string.Empty,
                string.Empty,
                string.Empty);
        }

        var serverAhead = guidance.State == PatchWatchState.ServerAhead;
        var heading = serverAhead
            ? "VERSION GUARD · SERVER BUILD"
            : "VERSION GUARD · PUBLIC PATCH";
        var detail = serverAhead
            ? "The observed server build is newer than both the official public notes and Isley's reviewed baseline. Verify server rules and in-game behavior first."
            : "The official public patch is newer than Isley's reviewed baseline. Review its notes before relying on update-sensitive guidance.";
        const string scopeLine =
            "VERIFY · COMBAT · MUTATIONS · GROWTH / LIFE · RECOVERY · TERRAIN / ROUTES";

        var copy = new StringBuilder()
            .AppendLine("ISLEY VERSION IMPACT CHECK")
            .AppendLine(guidance.VersionLine)
            .AppendLine(serverAhead
                ? "Status: observed server build is ahead of the documented public patch and Isley guide baseline."
                : "Status: documented public patch is ahead of the Isley guide baseline.")
            .AppendLine("Verify before relying on update-sensitive guidance:")
            .AppendLine("- combat, species abilities, and aim calibration")
            .AppendLine("- mutation availability, effects, and unlock tasks")
            .AppendLine("- growth, Prime/Elder, nesting, healing, and sickness timing")
            .AppendLine("- terrain, routes, water, and resource assumptions")
            .Append("- server-specific rules, rates, and multipliers");

        if (IsTrustedOfficialNotesUrl(officialNotesUrl))
        {
            copy.AppendLine()
                .Append("Official notes: ")
                .Append(officialNotesUrl);
        }

        return new PatchImpactGuidance(
            true,
            heading,
            detail,
            scopeLine,
            copy.ToString());
    }

    private static bool IsTrustedOfficialNotesUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, "steamcommunity.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith(
            "/ogg/376210/announcements/detail/",
            StringComparison.Ordinal);

    internal static bool TryExtractVersion(string? value, out string version)
    {
        version = string.Empty;
        var match = Regex.Match(
            value ?? string.Empty,
            @"(?<!\d)(?<version>\d{1,3}\.\d{1,3}\.\d{1,6})(?!\d)",
            RegexOptions.CultureInvariant);
        if (!match.Success || !TryParseVersion(match.Groups["version"].Value, out _))
        {
            return false;
        }
        version = match.Groups["version"].Value;
        return true;
    }

    internal static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero || age.TotalMinutes < 1) return "just now";
        if (age.TotalHours < 1) return $"{Math.Max(1, (int)age.TotalMinutes)}m";
        if (age.TotalDays < 1) return $"{Math.Max(1, (int)age.TotalHours)}h";
        return $"{Math.Max(1, (int)age.TotalDays)}d";
    }
}
