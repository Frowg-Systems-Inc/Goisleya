using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Isley;

internal sealed record TerrainRoadPoint(double X, double Y);

internal sealed record TerrainRoadPath(
    string Label,
    string Type,
    IReadOnlyList<TerrainRoadPoint> Points);

internal sealed record TerrainWaterMask(
    string AssetUrl,
    string Version,
    DateTimeOffset RetrievedAt,
    byte[] WebpBytes);

internal sealed record TerrainRoadNetwork(
    string AssetUrl,
    string Version,
    DateTimeOffset RetrievedAt,
    IReadOnlyList<TerrainRoadPath> Paths,
    TerrainWaterMask? WaterMask = null,
    TerrainCommunityHazardFeed? CommunityHazards = null,
    GatewayMapDataset? GatewayMap = null)
{
    internal int PointCount => Paths.Sum(path => path.Points.Count);

    internal string ToMapperJson() => JsonSerializer.Serialize(new
    {
        sourceUrl = AssetUrl,
        sourceVersion = Version,
        loadedAt = RetrievedAt.ToUnixTimeMilliseconds(),
        waterMask = WaterMask is null ? null : new
        {
            sourceUrl = WaterMask.AssetUrl,
            sourceVersion = WaterMask.Version,
            loadedAt = WaterMask.RetrievedAt.ToUnixTimeMilliseconds(),
            mediaType = "image/webp",
            dataBase64 = Convert.ToBase64String(WaterMask.WebpBytes)
        },
        communityHazards = CommunityHazards is null ? null : new
        {
            sourceUrl = CommunityHazards.AssetUrl,
            mapId = CommunityHazards.MapId,
            sourceVersion = CommunityHazards.Version,
            loadedAt = CommunityHazards.RetrievedAt.ToUnixTimeMilliseconds(),
            radius = TerrainCommunityHazardFeedClient.RouteAvoidanceRadius,
            points = CommunityHazards.Hazards.Select(hazard => new
            {
                x = hazard.X,
                y = hazard.Y
            })
        },
        gatewayMap = GatewayMap is null ? null : new
        {
            sourceUrl = GatewayMap.AssetUrl,
            sourceVersion = GatewayMap.Version,
            loadedAt = GatewayMap.RetrievedAt.ToUnixTimeMilliseconds(),
            coordinateSpace = "gateway-current-gamefiles",
            calibration = new
            {
                minimumWorldX = GatewayMapOverlayClient.MinimumWorldX,
                maximumWorldX = GatewayMapOverlayClient.MaximumWorldX,
                minimumWorldY = GatewayMapOverlayClient.MinimumWorldY,
                maximumWorldY = GatewayMapOverlayClient.MaximumWorldY,
                swapAxes = true
            },
            basemap = new
            {
                previewUrl = GatewayMap.Basemap.PreviewUrl,
                previewVersion = GatewayMap.Basemap.PreviewVersion,
                tileUrlTemplate = GatewayMap.Basemap.TileUrlTemplate,
                referenceDate = GatewayMap.Basemap.ReferenceDate,
                sourceWidth = GatewayMap.Basemap.SourceWidth,
                sourceHeight = GatewayMap.Basemap.SourceHeight,
                tileColumns = GatewayMap.Basemap.TileColumns,
                tileRows = GatewayMap.Basemap.TileRows,
                tileSize = GatewayMap.Basemap.TileSize,
                attributionUrl = TerrainRoadNetworkClient.AttributionPage,
                attribution = "MyIsleMap · Gateway game-file map · game imagery © Afterthought"
            },
            zones = new
            {
                sanctuaries = SerializeZones(GatewayMap.Sanctuaries),
                patrolCandidates = SerializeZones(GatewayMap.PatrolCandidates),
                migrationCandidates = SerializeZones(GatewayMap.MigrationCandidates)
            },
            resources = new
            {
                animals = SerializeResources(GatewayMap.Animals),
                herbs = SerializeResources(GatewayMap.Herbs),
                earth = SerializeResources(GatewayMap.Earth)
            },
            waterLabels = GatewayMap.WaterLabels.Select(label => new
            {
                label = label.Label,
                worldX = label.WorldX,
                worldY = label.WorldY,
                x = label.X,
                y = label.Y
            })
        },
        paths = Paths.Select(path => new
        {
            label = path.Label,
            type = path.Type,
            points = path.Points.Select(point => new { x = point.X, y = point.Y })
        })
    });

    private static IEnumerable<object> SerializeZones(IReadOnlyList<GatewayMapZone> zones) =>
        zones.Select(zone => (object)new
        {
            type = zone.Type,
            label = zone.Label,
            gameLabel = zone.GameLabel,
            cx = zone.CenterX,
            cy = zone.CenterY,
            r = zone.Radius,
            points = zone.Points.Select(point => new { x = point.X, y = point.Y })
        });

    private static IEnumerable<object> SerializeResources(
        IReadOnlyList<GatewayMapResource> resources) =>
        resources.Select(resource => (object)new
        {
            bucket = resource.Bucket,
            group = resource.Group,
            key = resource.Key,
            name = resource.Name,
            x = resource.X,
            y = resource.Y,
            updated = resource.Updated
        });
}

internal static partial class TerrainRoadNetworkClient
{
    internal const string IndexPage = "https://myislemap.com/";
    internal const string AttributionPage = "https://myislemap.com/";
    private const int MaxIndexBytes = 256_000;
    private const int MaxAssetBytes = 512_000;
    private const int MaxWaterAssetBytes = 256_000;
    private const int MaxOverlayAssetBytes = 512_000;
    private const int MaxWaterLabelAssetBytes = 64_000;
    private const int MaxPaths = 200;
    private const int MaxPointsPerPath = 500;
    private const int MaxTotalPoints = 20_000;

    private static readonly HttpClient Client = CreateClient();

    internal static async Task<TerrainRoadNetwork> FetchAsync(CancellationToken cancellationToken)
    {
        var index = await FetchTextAsync(new Uri(IndexPage), MaxIndexBytes, cancellationToken);
        var assetUri = ResolveRoadAssetUri(index);
        var waterAssetUri = ResolveWaterAssetUri(index);
        var overlayAssetUri = GatewayMapOverlayClient.ResolveOverlayAssetUri(index);
        var waterLabelAssetUri = GatewayMapOverlayClient.ResolveWaterLabelAssetUri(index);
        var basemap = GatewayMapOverlayClient.ResolveBasemapAsset(index);
        var assetTask = FetchTextAsync(assetUri, MaxAssetBytes, cancellationToken);
        var waterTask = FetchBytesAsync(
            waterAssetUri,
            MaxWaterAssetBytes,
            "image/webp",
            cancellationToken);
        var overlayTask = FetchTextAsync(
            overlayAssetUri, MaxOverlayAssetBytes, cancellationToken);
        var waterLabelTask = FetchTextAsync(
            waterLabelAssetUri, MaxWaterLabelAssetBytes, cancellationToken);
        var hazardTask = FetchCommunityHazardsBestEffortAsync(cancellationToken);
        await Task.WhenAll(
            assetTask, waterTask, overlayTask, waterLabelTask, hazardTask);
        var asset = await assetTask;
        var waterBytes = await waterTask;
        var overlayAsset = await overlayTask;
        var waterLabelAsset = await waterLabelTask;
        var communityHazards = await hazardTask;
        ValidateWebp(waterBytes);
        var retrievedAt = DateTimeOffset.Now;
        var network = ParseJavaScriptAsset(asset, assetUri, retrievedAt);
        var waterLabels = GatewayMapOverlayClient.ParseWaterLabelAsset(
            waterLabelAsset, waterLabelAssetUri);
        var gatewayMap = GatewayMapOverlayClient.ParseOverlayAsset(
            overlayAsset,
            overlayAssetUri,
            retrievedAt,
            basemap,
            waterLabels);
        return network with
        {
            WaterMask = new TerrainWaterMask(
                waterAssetUri.AbsoluteUri,
                ReadVersion(waterAssetUri),
                retrievedAt,
                waterBytes),
            CommunityHazards = communityHazards,
            GatewayMap = gatewayMap
        };
    }

    private static async Task<TerrainCommunityHazardFeed?> FetchCommunityHazardsBestEffortAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await TerrainCommunityHazardFeedClient.FetchAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    internal static Uri ResolveRoadAssetUri(string indexHtml)
    {
        var match = RoadScriptRegex().Match(indexHtml ?? string.Empty);
        if (!match.Success)
        {
            throw new InvalidDataException("The current road/trail asset was not advertised by the map source.");
        }

        var relative = WebUtility.HtmlDecode(match.Groups["src"].Value);
        if (!Uri.TryCreate(new Uri(IndexPage), relative, out var assetUri)
            || !string.Equals(assetUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(assetUri.Host, "myislemap.com", StringComparison.OrdinalIgnoreCase)
            || !assetUri.AbsolutePath.EndsWith("/map-roads.js", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The road/trail asset URL was not trusted.");
        }
        return assetUri;
    }

    internal static Uri ResolveWaterAssetUri(string indexHtml)
    {
        var match = WaterImageRegex().Match(indexHtml ?? string.Empty);
        if (!match.Success)
        {
            throw new InvalidDataException("The current drinkable-water mask was not advertised by the map source.");
        }

        var relative = WebUtility.HtmlDecode(match.Groups["src"].Value);
        if (!Uri.TryCreate(new Uri(IndexPage), relative, out var assetUri)
            || !string.Equals(assetUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(assetUri.Host, "myislemap.com", StringComparison.OrdinalIgnoreCase)
            || !assetUri.AbsolutePath.EndsWith("/assets/water-map.webp", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The drinkable-water mask URL was not trusted.");
        }
        return assetUri;
    }

    internal static TerrainRoadNetwork ParseJavaScriptAsset(
        string source,
        Uri assetUri,
        DateTimeOffset retrievedAt)
    {
        if (!string.Equals(assetUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(assetUri.Host, "myislemap.com", StringComparison.OrdinalIgnoreCase)
            || !assetUri.AbsolutePath.EndsWith("/map-roads.js", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The road/trail asset URL was not trusted.");
        }

        var safeSource = source ?? string.Empty;
        var markerIndex = safeSource.IndexOf("MAP_ROADS", StringComparison.Ordinal);
        var arrayStart = markerIndex < 0 ? -1 : safeSource.IndexOf('[', markerIndex);
        var arrayEnd = safeSource.LastIndexOf("];", StringComparison.Ordinal);
        if (markerIndex < 0 || arrayStart < 0 || arrayEnd <= arrayStart)
        {
            throw new InvalidDataException("The road/trail asset did not contain the expected data array.");
        }

        var json = safeSource[arrayStart..(arrayEnd + 1)];
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 16
        });
        if (document.RootElement.ValueKind != JsonValueKind.Array
            || document.RootElement.GetArrayLength() is < 1 or > MaxPaths)
        {
            throw new InvalidDataException("The road/trail dataset had an invalid path count.");
        }

        var paths = new List<TerrainRoadPath>();
        var totalPoints = 0;
        foreach (var pathElement in document.RootElement.EnumerateArray())
        {
            if (pathElement.ValueKind != JsonValueKind.Object
                || !pathElement.TryGetProperty("label", out var labelElement)
                || labelElement.ValueKind != JsonValueKind.String
                || !pathElement.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String
                || !pathElement.TryGetProperty("points", out var pointsElement)
                || pointsElement.ValueKind != JsonValueKind.Array
                || pointsElement.GetArrayLength() is < 2 or > MaxPointsPerPath)
            {
                throw new InvalidDataException("The road/trail dataset contained an invalid path.");
            }

            var label = CleanLabel(labelElement.GetString());
            var type = CleanPathType(typeElement.GetString());
            var points = new List<TerrainRoadPoint>();
            foreach (var pointElement in pointsElement.EnumerateArray())
            {
                if (pointElement.ValueKind != JsonValueKind.Object
                    || !pointElement.TryGetProperty("x", out var xElement)
                    || xElement.ValueKind != JsonValueKind.Number
                    || !pointElement.TryGetProperty("y", out var yElement)
                    || yElement.ValueKind != JsonValueKind.Number)
                {
                    throw new InvalidDataException("The road/trail dataset contained an invalid point.");
                }

                var x = xElement.GetDouble();
                var y = yElement.GetDouble();
                if (!double.IsFinite(x) || !double.IsFinite(y)
                    || x < GatewayMapOverlayClient.MinimumWorldX
                    || x > GatewayMapOverlayClient.MaximumWorldX
                    || y < GatewayMapOverlayClient.MinimumWorldY
                    || y > GatewayMapOverlayClient.MaximumWorldY)
                {
                    throw new InvalidDataException("The road/trail dataset contained an out-of-range point.");
                }
                points.Add(new TerrainRoadPoint(x, y));
            }

            totalPoints += points.Count;
            if (totalPoints > MaxTotalPoints)
            {
                throw new InvalidDataException("The road/trail dataset exceeded the safe point limit.");
            }
            paths.Add(new TerrainRoadPath(label, type, points));
        }

        var version = ReadVersion(assetUri);
        return new TerrainRoadNetwork(assetUri.AbsoluteUri, version, retrievedAt, paths);
    }

    private static async Task<byte[]> FetchBytesAsync(
        Uri uri,
        int maximumBytes,
        string expectedMediaType,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(expectedMediaType));
        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var returnedMediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.IsNullOrWhiteSpace(returnedMediaType)
            && !string.Equals(returnedMediaType, expectedMediaType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The terrain mask returned an unexpected media type.");
        }
        if (response.Content.Headers.ContentLength is > 0
            && response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException("The terrain mask exceeded the safe download limit.");
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
                throw new InvalidDataException("The terrain mask exceeded the safe download limit.");
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    internal static void ValidateWebp(byte[] bytes)
    {
        if (bytes.Length < 16
            || bytes[0] != (byte)'R' || bytes[1] != (byte)'I'
            || bytes[2] != (byte)'F' || bytes[3] != (byte)'F'
            || bytes[8] != (byte)'W' || bytes[9] != (byte)'E'
            || bytes[10] != (byte)'B' || bytes[11] != (byte)'P')
        {
            throw new InvalidDataException("The terrain mask was not a valid WebP asset.");
        }
    }

    private static async Task<string> FetchTextAsync(
        Uri uri,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/javascript"));
        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 0
            && response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException("The terrain source exceeded the safe download limit.");
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
                throw new InvalidDataException("The terrain source exceeded the safe download limit.");
            }
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        using var reader = new StreamReader(buffer, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Isley/1.0 terrain-course");
        return client;
    }

    private static string CleanLabel(string? value)
    {
        var label = string.Join(" ", (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (label.Length is < 1 or > 80 || label.Any(char.IsControl))
        {
            throw new InvalidDataException("The road/trail dataset contained an invalid label.");
        }
        return label;
    }

    private static string CleanPathType(string? value)
    {
        var type = (value ?? string.Empty).Trim().ToLowerInvariant();
        return type is "road" or "trail"
            ? type
            : throw new InvalidDataException("The road/trail dataset contained an invalid path type.");
    }

    private static string CleanVersion(string? value)
    {
        var version = Regex.Replace(value ?? string.Empty, "[^A-Za-z0-9._-]", string.Empty);
        return string.IsNullOrWhiteSpace(version) ? "live" : version[..Math.Min(24, version.Length)];
    }

    private static string ReadVersion(Uri assetUri)
    {
        var versionMatch = Regex.Match(
            assetUri.Query,
            "(?:^|[?&])v=(?<version>[^&]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return CleanVersion(versionMatch.Success
            ? Uri.UnescapeDataString(versionMatch.Groups["version"].Value)
            : string.Empty);
    }

    [GeneratedRegex("<script\\b[^>]*\\bsrc=[\\\"'](?<src>[^\\\"']*map-roads\\.js(?:\\?[^\\\"']*)?)[\\\"'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RoadScriptRegex();

    [GeneratedRegex("<img\\b(?=[^>]*\\bid=[\\\"']waterMapImage[\\\"'])[^>]*\\bdata-src=[\\\"'](?<src>[^\\\"']*water-map\\.webp(?:\\?[^\\\"']*)?)[\\\"'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WaterImageRegex();
}
