using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace Isley;

internal sealed record TerrainCommunityHazard(double X, double Y);

internal sealed record TerrainCommunityHazardFeed(
    string AssetUrl,
    string MapId,
    string Version,
    DateTimeOffset RetrievedAt,
    IReadOnlyList<TerrainCommunityHazard> Hazards);

internal static partial class TerrainCommunityHazardFeedClient
{
    internal const string CatalogPage = "https://vulnona.com/game/map/dat.txt";
    internal const string AttributionPage = "https://vulnona.com/game/map/";
    internal const double RouteAvoidanceRadius = 12;
    internal const int MaximumHazards = 64;

    private const int MaxCatalogBytes = 256_000;
    private const int MaxFeedBytes = 128_000;
    private static readonly HttpClient Client = CreateClient();

    internal static async Task<TerrainCommunityHazardFeed> FetchAsync(
        CancellationToken cancellationToken)
    {
        var catalog = await FetchTextAsync(
            new Uri(CatalogPage), MaxCatalogBytes, cancellationToken);
        var map = ResolveCurrentGatewayMap(catalog);
        var assetUri = ResolvePhotoAssetUri(map.MapId);
        var feed = await FetchTextAsync(assetUri, MaxFeedBytes, cancellationToken);
        return ParsePhotoFeed(feed, assetUri, map.MapId, map.Version, DateTimeOffset.Now);
    }

    internal static (string MapId, string Version) ResolveCurrentGatewayMap(string catalog)
    {
        foreach (var rawLine in (catalog ?? string.Empty)
                     .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var columns = rawLine.Split('\t');
            if (columns.Length < 6
                || !string.Equals(columns[0].Trim(), "map", StringComparison.Ordinal)
                || !string.Equals(columns[2].Trim(), "TI", StringComparison.Ordinal))
            {
                continue;
            }

            var mapId = columns[3].Trim();
            var display = string.Join(" ", columns.Skip(5).Select(value => value.Trim()));
            if (!GatewayMapIdRegex().IsMatch(mapId)
                || display.Contains("OUTDATED", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var versionMatch = GatewayVersionRegex().Match(display);
            var version = versionMatch.Success
                ? versionMatch.Groups["version"].Value
                : mapId["Gateway_v".Length..];
            return (mapId, CleanVersion(version));
        }

        throw new InvalidDataException(
            "The current Gateway map was not advertised by the terrain source.");
    }

    internal static Uri ResolvePhotoAssetUri(string mapId)
    {
        var safeMapId = (mapId ?? string.Empty).Trim();
        if (!GatewayMapIdRegex().IsMatch(safeMapId))
        {
            throw new InvalidDataException("The terrain danger feed map identifier was invalid.");
        }

        var assetUri = new Uri(
            $"https://vulnona.com/game/map/map/{safeMapId}/photo.txt",
            UriKind.Absolute);
        if (!string.Equals(assetUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(assetUri.Host, "vulnona.com", StringComparison.OrdinalIgnoreCase)
            || !assetUri.AbsolutePath.EndsWith(
                $"/map/{safeMapId}/photo.txt", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The terrain danger feed URL was not trusted.");
        }
        return assetUri;
    }

    internal static TerrainCommunityHazardFeed ParsePhotoFeed(
        string source,
        Uri assetUri,
        string mapId,
        string version,
        DateTimeOffset retrievedAt)
    {
        var expectedUri = ResolvePhotoAssetUri(mapId);
        if (assetUri != expectedUri)
        {
            throw new InvalidDataException("The terrain danger feed URL was not trusted.");
        }

        var hazards = new List<TerrainCommunityHazard>();
        foreach (var rawLine in (source ?? string.Empty)
                     .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var columns = rawLine.Split('\t');
            if (columns.Length < 2)
            {
                continue;
            }

            var coordinateParts = columns[0].Split(',');
            if (coordinateParts.Length != 2
                || !double.TryParse(
                    coordinateParts[0],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var mapX)
                || !double.TryParse(
                    coordinateParts[1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var mapY)
                || !long.TryParse(
                    columns[1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var timestamp)
                || timestamp >= 0)
            {
                continue;
            }

            if (!double.IsFinite(mapX) || !double.IsFinite(mapY)
                || Math.Abs(mapX) > 500 || Math.Abs(mapY) > 500)
            {
                throw new InvalidDataException(
                    "The terrain danger feed contained an out-of-range coordinate.");
            }

            if (hazards.Count >= MaximumHazards)
            {
                throw new InvalidDataException(
                    "The terrain danger feed exceeded the safe hazard limit.");
            }

            // Vulnona's public Gateway map coordinates are expressed in kilometres
            // relative to the same game-world origin used by the live map calibration.
            // Contributor names, image keys, and comments are intentionally discarded.
            hazards.Add(new TerrainCommunityHazard(mapX * 1000, mapY * 1000));
        }

        return new TerrainCommunityHazardFeed(
            assetUri.AbsoluteUri,
            mapId,
            CleanVersion(version),
            retrievedAt,
            hazards);
    }

    private static async Task<string> FetchTextAsync(
        Uri uri,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 0
            && response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException("The terrain danger source exceeded the safe limit.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read <= 0)
            {
                break;
            }
            if (buffer.Length + read > maximumBytes)
            {
                throw new InvalidDataException("The terrain danger source exceeded the safe limit.");
            }
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        using var reader = new StreamReader(buffer, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Isley/1.0 terrain-danger");
        return client;
    }

    private static string CleanVersion(string? value)
    {
        var version = Regex.Replace(value ?? string.Empty, "[^A-Za-z0-9._-]", string.Empty);
        return string.IsNullOrWhiteSpace(version)
            ? "live"
            : version[..Math.Min(24, version.Length)];
    }

    [GeneratedRegex("^Gateway_v[0-9]+(?:\\.[0-9]+)*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex GatewayMapIdRegex();

    [GeneratedRegex("Gateway\\s+v(?<version>[0-9]+(?:\\.[0-9]+)*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GatewayVersionRegex();
}
