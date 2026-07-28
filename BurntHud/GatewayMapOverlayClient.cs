using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Isley;

internal sealed record GatewayMapPoint(double X, double Y);

internal sealed record GatewayMapZone(
    string Type,
    string Label,
    string GameLabel,
    double? CenterX,
    double? CenterY,
    double? Radius,
    IReadOnlyList<GatewayMapPoint> Points);

internal sealed record GatewayMapResource(
    string Bucket,
    string Group,
    string Key,
    string Name,
    double X,
    double Y,
    string Updated);

internal sealed record GatewayMapWaterLabel(
    string Label,
    double WorldX,
    double WorldY,
    double X,
    double Y);

internal sealed record GatewayMapAsset(
    string PreviewUrl,
    string PreviewVersion,
    string TileUrlTemplate,
    string ReferenceDate,
    int SourceWidth,
    int SourceHeight,
    int TileColumns,
    int TileRows,
    int TileSize);

internal sealed record GatewayMapDataset(
    string AssetUrl,
    string Version,
    DateTimeOffset RetrievedAt,
    GatewayMapAsset Basemap,
    IReadOnlyList<GatewayMapZone> Sanctuaries,
    IReadOnlyList<GatewayMapZone> PatrolCandidates,
    IReadOnlyList<GatewayMapZone> MigrationCandidates,
    IReadOnlyList<GatewayMapResource> Animals,
    IReadOnlyList<GatewayMapResource> Herbs,
    IReadOnlyList<GatewayMapResource> Earth,
    IReadOnlyList<GatewayMapWaterLabel> WaterLabels)
{
    internal int ZoneCount =>
        Sanctuaries.Count + PatrolCandidates.Count + MigrationCandidates.Count;

    internal int ResourceCount => Animals.Count + Herbs.Count + Earth.Count;
}

internal static partial class GatewayMapOverlayClient
{
    internal const double MinimumWorldX = -607_000;
    internal const double MaximumWorldX = 509_000;
    internal const double MinimumWorldY = -505_000;
    internal const double MaximumWorldY = 607_000;
    internal const double SourceMapWidth = 1000;
    internal const double SourceMapHeight = 1003;

    private const int MaximumZonesPerLayer = 100;
    private const int MaximumZonePoints = 64;
    private const int MaximumResourcesPerBucket = 1000;
    private const int MaximumWaterLabels = 100;

    internal static Uri ResolveOverlayAssetUri(string indexHtml)
    {
        return ResolveTrustedScript(indexHtml, OverlayScriptRegex(), "/map-data.js",
            "The current Gateway overlay dataset was not advertised by the map source.");
    }

    internal static Uri ResolveWaterLabelAssetUri(string indexHtml)
    {
        return ResolveTrustedScript(indexHtml, WaterLabelScriptRegex(), "/map-water.js",
            "The current Gateway water-label dataset was not advertised by the map source.");
    }

    internal static GatewayMapAsset ResolveBasemapAsset(string indexHtml)
    {
        var match = PreviewImageRegex().Match(indexHtml ?? string.Empty);
        if (!match.Success)
        {
            throw new InvalidDataException(
                "The current Gateway preview basemap was not advertised by the map source.");
        }

        var relative = WebUtility.HtmlDecode(match.Groups["src"].Value);
        if (!Uri.TryCreate(new Uri(TerrainRoadNetworkClient.IndexPage), relative, out var previewUri)
            || !IsTrustedMyIsleMapUri(previewUri, "/assets/gateway-preview.webp"))
        {
            throw new InvalidDataException("The current Gateway preview basemap URL was not trusted.");
        }

        var version = ReadVersion(previewUri);
        var referenceDate = ReadReferenceDate(version);
        var tileTemplate =
            $"https://myislemap.com/assets/gateway-tiles/gateway-{{col}}-{{row}}.jpg?v={Uri.EscapeDataString(version)}";
        return new GatewayMapAsset(
            previewUri.AbsoluteUri,
            version,
            tileTemplate,
            referenceDate,
            7800,
            7817,
            8,
            8,
            1024);
    }

    internal static GatewayMapDataset ParseOverlayAsset(
        string source,
        Uri assetUri,
        DateTimeOffset retrievedAt,
        GatewayMapAsset basemap,
        IReadOnlyList<GatewayMapWaterLabel>? waterLabels = null)
    {
        if (!IsTrustedMyIsleMapUri(assetUri, "/map-data.js"))
        {
            throw new InvalidDataException("The Gateway overlay asset URL was not trusted.");
        }

        var objectLiteral = ExtractBalancedObject(source, "MAP_OVERLAYS");
        var strictJson = NormalizeObjectLiteralToJson(objectLiteral);
        using var document = JsonDocument.Parse(strictJson, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The Gateway overlay dataset root was invalid.");
        }

        var sanctuaries = ReadZones(root, "sanctuary");
        var patrol = ReadZones(root, "patrol");
        var migration = ReadZones(root, "migration");
        var animals = ReadResources(root, "animals");
        var herbs = ReadResources(root, "herbs");
        var earth = ReadResources(root, "earth");

        if (sanctuaries.Count is < 1 or > MaximumZonesPerLayer
            || patrol.Count is < 1 or > MaximumZonesPerLayer
            || migration.Count is < 1 or > MaximumZonesPerLayer
            || animals.Count is < 1 or > MaximumResourcesPerBucket
            || herbs.Count is < 1 or > MaximumResourcesPerBucket
            || earth.Count is < 1 or > MaximumResourcesPerBucket)
        {
            throw new InvalidDataException("The Gateway overlay dataset had invalid layer counts.");
        }

        return new GatewayMapDataset(
            assetUri.AbsoluteUri,
            ReadVersion(assetUri),
            retrievedAt,
            basemap,
            sanctuaries,
            patrol,
            migration,
            animals,
            herbs,
            earth,
            waterLabels ?? []);
    }

    internal static IReadOnlyList<GatewayMapWaterLabel> ParseWaterLabelAsset(
        string source,
        Uri assetUri)
    {
        if (!IsTrustedMyIsleMapUri(assetUri, "/map-water.js"))
        {
            throw new InvalidDataException("The Gateway water-label asset URL was not trusted.");
        }

        var arrayLiteral = ExtractBalancedArray(source, "MAP_WATER_LABELS");
        using var document = JsonDocument.Parse(arrayLiteral, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 12
        });
        if (document.RootElement.ValueKind != JsonValueKind.Array
            || document.RootElement.GetArrayLength() is < 1 or > MaximumWaterLabels)
        {
            throw new InvalidDataException("The Gateway water-label dataset had an invalid count.");
        }

        var labels = new List<GatewayMapWaterLabel>();
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("x", out var xElement)
                || xElement.ValueKind != JsonValueKind.Number
                || !entry.TryGetProperty("y", out var yElement)
                || yElement.ValueKind != JsonValueKind.Number
                || !entry.TryGetProperty("label", out var labelElement)
                || labelElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("The Gateway water-label dataset contained an invalid item.");
            }

            var worldX = xElement.GetDouble();
            var worldY = yElement.GetDouble();
            var mapPoint = WorldToMap(worldX, worldY);
            if (!double.IsFinite(worldX) || !double.IsFinite(worldY)
                || !IsMapPointInRange(mapPoint))
            {
                throw new InvalidDataException(
                    "The Gateway water-label dataset contained an out-of-range point.");
            }

            labels.Add(new GatewayMapWaterLabel(
                CleanMarkupLabel(labelElement.GetString()),
                worldX,
                worldY,
                mapPoint.X,
                mapPoint.Y));
        }
        return labels;
    }

    internal static GatewayMapPoint WorldToMap(double worldX, double worldY)
    {
        return new GatewayMapPoint(
            (worldY - MinimumWorldY) / (MaximumWorldY - MinimumWorldY) * 1000,
            (worldX - MinimumWorldX) / (MaximumWorldX - MinimumWorldX) * 1000);
    }

    private static IReadOnlyList<GatewayMapZone> ReadZones(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var layer)
            || layer.ValueKind != JsonValueKind.Object
            || !layer.TryGetProperty("zones", out var zones)
            || zones.ValueKind != JsonValueKind.Array
            || zones.GetArrayLength() is < 1 or > MaximumZonesPerLayer)
        {
            throw new InvalidDataException($"The Gateway {propertyName} layer was invalid.");
        }

        var result = new List<GatewayMapZone>();
        foreach (var zone in zones.EnumerateArray())
        {
            if (zone.ValueKind != JsonValueKind.Object
                || !zone.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String
                || !zone.TryGetProperty("label", out var labelElement)
                || labelElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"The Gateway {propertyName} layer contained an invalid zone.");
            }

            var type = (typeElement.GetString() ?? string.Empty).Trim().ToLowerInvariant();
            var label = CleanLabel(labelElement.GetString(), 64);
            var gameLabel = zone.TryGetProperty("gameLabel", out var gameLabelElement)
                            && gameLabelElement.ValueKind == JsonValueKind.String
                ? CleanIdentifier(gameLabelElement.GetString(), 96)
                : string.Empty;

            if (type == "circle")
            {
                var cx = ReadFiniteNumber(zone, "cx");
                var cy = NormalizeSourceY(ReadFiniteNumber(zone, "cy"));
                var radius = ReadFiniteNumber(zone, "r") * 1000 / SourceMapHeight;
                if (!IsMapPointInRange(new GatewayMapPoint(cx, cy))
                    || radius is <= 0 or > 250)
                {
                    throw new InvalidDataException(
                        $"The Gateway {propertyName} layer contained an invalid circle.");
                }
                result.Add(new GatewayMapZone(
                    type, label, gameLabel, cx, cy, radius, []));
                continue;
            }

            if (type != "polygon"
                || !zone.TryGetProperty("points", out var pointsElement)
                || pointsElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException(
                    $"The Gateway {propertyName} layer contained an unsupported zone.");
            }

            var points = ParseZonePoints(pointsElement.GetString());
            result.Add(new GatewayMapZone(
                type, label, gameLabel, null, null, null, points));
        }
        return result;
    }

    private static IReadOnlyList<GatewayMapResource> ReadResources(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var resources)
            || resources.ValueKind != JsonValueKind.Array
            || resources.GetArrayLength() is < 1 or > MaximumResourcesPerBucket)
        {
            throw new InvalidDataException($"The Gateway {propertyName} resource layer was invalid.");
        }

        var result = new List<GatewayMapResource>();
        foreach (var resource in resources.EnumerateArray())
        {
            if (resource.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"The Gateway {propertyName} resource layer contained an invalid item.");
            }

            var x = ReadFiniteNumber(resource, "x");
            var y = NormalizeSourceY(ReadFiniteNumber(resource, "y"));
            if (!IsMapPointInRange(new GatewayMapPoint(x, y)))
            {
                throw new InvalidDataException(
                    $"The Gateway {propertyName} resource layer contained an out-of-range point.");
            }

            result.Add(new GatewayMapResource(
                CleanIdentifier(ReadRequiredString(resource, "bucket"), 24),
                CleanLabel(ReadRequiredString(resource, "group"), 64),
                CleanIdentifier(ReadRequiredString(resource, "key"), 40),
                CleanLabel(ReadRequiredString(resource, "name"), 48),
                x,
                y,
                resource.TryGetProperty("updated", out var updated)
                && updated.ValueKind == JsonValueKind.String
                    ? CleanDate(updated.GetString())
                    : string.Empty));
        }
        return result;
    }

    private static IReadOnlyList<GatewayMapPoint> ParseZonePoints(string? value)
    {
        var tokens = (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is < 3 or > MaximumZonePoints)
        {
            throw new InvalidDataException("The Gateway overlay dataset contained an invalid polygon.");
        }

        var points = new List<GatewayMapPoint>();
        foreach (var token in tokens)
        {
            var coordinates = token.Split(',', StringSplitOptions.TrimEntries);
            if (coordinates.Length != 2
                || !double.TryParse(coordinates[0], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var x)
                || !double.TryParse(coordinates[1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var sourceY))
            {
                throw new InvalidDataException(
                    "The Gateway overlay dataset contained an invalid polygon coordinate.");
            }

            var point = new GatewayMapPoint(x, NormalizeSourceY(sourceY));
            if (!IsMapPointInRange(point))
            {
                throw new InvalidDataException(
                    "The Gateway overlay dataset contained an out-of-range polygon coordinate.");
            }
            points.Add(point);
        }
        return points;
    }

    private static string ExtractBalancedObject(string source, string marker)
        => ExtractBalanced(source, marker, '{', '}');

    private static string ExtractBalancedArray(string source, string marker)
        => ExtractBalanced(source.TrimStart('\uFEFF', '\u00EF', '\u00BB', '\u00BF'),
            marker, '[', ']');

    private static string ExtractBalanced(
        string? source,
        string marker,
        char opening,
        char closing)
    {
        var value = source ?? string.Empty;
        var markerIndex = value.IndexOf(marker, StringComparison.Ordinal);
        var start = markerIndex < 0 ? -1 : value.IndexOf(opening, markerIndex);
        if (start < 0)
        {
            throw new InvalidDataException("The expected Gateway data container was missing.");
        }

        var depth = 0;
        var inString = false;
        var escape = false;
        var lineComment = false;
        var blockComment = false;
        for (var index = start; index < value.Length; index++)
        {
            var current = value[index];
            var next = index + 1 < value.Length ? value[index + 1] : '\0';
            if (lineComment)
            {
                if (current is '\r' or '\n') lineComment = false;
                continue;
            }
            if (blockComment)
            {
                if (current == '*' && next == '/')
                {
                    blockComment = false;
                    index++;
                }
                continue;
            }
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                }
                else if (current == '\\')
                {
                    escape = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }
                continue;
            }
            if (current == '/' && next == '/')
            {
                lineComment = true;
                index++;
                continue;
            }
            if (current == '/' && next == '*')
            {
                blockComment = true;
                index++;
                continue;
            }
            if (current == '"')
            {
                inString = true;
                continue;
            }
            if (current == opening) depth++;
            if (current != closing) continue;
            depth--;
            if (depth == 0) return value[start..(index + 1)];
        }

        throw new InvalidDataException("The Gateway data container was not balanced.");
    }

    private static string NormalizeObjectLiteralToJson(string source)
    {
        var output = new StringBuilder(source.Length + 2048);
        var inString = false;
        var escape = false;
        var lineComment = false;
        var blockComment = false;

        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';
            if (lineComment)
            {
                if (current is '\r' or '\n')
                {
                    lineComment = false;
                    output.Append(current);
                }
                continue;
            }
            if (blockComment)
            {
                if (current == '*' && next == '/')
                {
                    blockComment = false;
                    index++;
                }
                continue;
            }
            if (inString)
            {
                output.Append(current);
                if (escape)
                {
                    escape = false;
                }
                else if (current == '\\')
                {
                    escape = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }
                continue;
            }
            if (current == '/' && next == '/')
            {
                lineComment = true;
                index++;
                continue;
            }
            if (current == '/' && next == '*')
            {
                blockComment = true;
                index++;
                continue;
            }
            if (current == '"')
            {
                inString = true;
                output.Append(current);
                continue;
            }
            if (char.IsLetter(current) || current == '_')
            {
                var start = index;
                while (index + 1 < source.Length
                       && (char.IsLetterOrDigit(source[index + 1])
                           || source[index + 1] is '_' or '-'))
                {
                    index++;
                }
                var identifier = source[start..(index + 1)];
                var previous = PreviousNonWhitespace(output);
                var following = NextNonWhitespace(source, index + 1);
                if (following == ':' && previous is '{' or ',')
                {
                    output.Append('"').Append(identifier).Append('"');
                }
                else if (identifier is "true" or "false" or "null")
                {
                    output.Append(identifier);
                }
                else
                {
                    throw new InvalidDataException(
                        "The Gateway overlay dataset contained executable or unsupported syntax.");
                }
                continue;
            }
            if (!char.IsWhiteSpace(current)
                && !"{}[],:.-+0123456789eE".Contains(current))
            {
                throw new InvalidDataException(
                    "The Gateway overlay dataset contained executable or unsupported syntax.");
            }
            output.Append(current);
        }
        return output.ToString();
    }

    private static char PreviousNonWhitespace(StringBuilder value)
    {
        for (var index = value.Length - 1; index >= 0; index--)
        {
            if (!char.IsWhiteSpace(value[index])) return value[index];
        }
        return '\0';
    }

    private static char NextNonWhitespace(string value, int start)
    {
        for (var index = start; index < value.Length; index++)
        {
            if (!char.IsWhiteSpace(value[index])) return value[index];
        }
        return '\0';
    }

    private static Uri ResolveTrustedScript(
        string indexHtml,
        Regex regex,
        string expectedPath,
        string missingMessage)
    {
        var match = regex.Match(indexHtml ?? string.Empty);
        if (!match.Success) throw new InvalidDataException(missingMessage);
        var relative = WebUtility.HtmlDecode(match.Groups["src"].Value);
        if (!Uri.TryCreate(new Uri(TerrainRoadNetworkClient.IndexPage), relative, out var assetUri)
            || !IsTrustedMyIsleMapUri(assetUri, expectedPath))
        {
            throw new InvalidDataException("The Gateway overlay asset URL was not trusted.");
        }
        return assetUri;
    }

    private static bool IsTrustedMyIsleMapUri(Uri uri, string expectedPath)
    {
        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
               && string.Equals(uri.Host, "myislemap.com", StringComparison.OrdinalIgnoreCase)
               && uri.AbsolutePath.EndsWith(expectedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static double ReadFiniteNumber(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidDataException(
                $"The Gateway overlay dataset was missing numeric {propertyName}.");
        }
        var number = property.GetDouble();
        return double.IsFinite(number)
            ? number
            : throw new InvalidDataException(
                $"The Gateway overlay dataset contained invalid numeric {propertyName}.");
    }

    private static string ReadRequiredString(JsonElement value, string propertyName)
    {
        return value.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : throw new InvalidDataException(
                $"The Gateway overlay dataset was missing text {propertyName}.");
    }

    private static double NormalizeSourceY(double sourceY) => sourceY * 1000 / SourceMapHeight;

    private static bool IsMapPointInRange(GatewayMapPoint point) =>
        double.IsFinite(point.X) && double.IsFinite(point.Y)
        && point.X is >= 0 and <= 1000
        && point.Y is >= 0 and <= 1000;

    private static string CleanLabel(string? value, int maximumLength)
    {
        var label = string.Join(" ", (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (label.Length is < 1 || label.Length > maximumLength || label.Any(char.IsControl))
        {
            throw new InvalidDataException("The Gateway overlay dataset contained an invalid label.");
        }
        return label;
    }

    private static string CleanMarkupLabel(string? value)
    {
        var withoutTags = HtmlTagRegex().Replace(value ?? string.Empty, " ");
        return CleanLabel(WebUtility.HtmlDecode(withoutTags), 64);
    }

    private static string CleanIdentifier(string? value, int maximumLength)
    {
        var identifier = (value ?? string.Empty).Trim();
        if (identifier.Length is < 1 || identifier.Length > maximumLength
            || identifier.Any(character => !(char.IsLetterOrDigit(character)
                                              || character is '_' or '-' or '(' or ')' or ' ')))
        {
            throw new InvalidDataException(
                "The Gateway overlay dataset contained an invalid identifier.");
        }
        return identifier;
    }

    private static string CleanDate(string? value)
    {
        var date = (value ?? string.Empty).Trim();
        return Regex.IsMatch(date, "^\\d{4}/\\d{2}/\\d{2}$",
            RegexOptions.CultureInvariant)
            ? date
            : string.Empty;
    }

    private static string ReadVersion(Uri uri)
    {
        var match = VersionRegex().Match(uri.Query);
        var value = match.Success
            ? Uri.UnescapeDataString(match.Groups["version"].Value)
            : "live";
        var version = Regex.Replace(value, "[^A-Za-z0-9._-]", string.Empty);
        return string.IsNullOrWhiteSpace(version)
            ? "live"
            : version[..Math.Min(version.Length, 32)];
    }

    private static string ReadReferenceDate(string version)
    {
        var match = Regex.Match(version, "(?<date>20\\d{6})",
            RegexOptions.CultureInvariant);
        if (!match.Success
            || !DateOnly.TryParseExact(match.Groups["date"].Value, "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return string.Empty;
        }
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    [GeneratedRegex("<script\\b[^>]*\\bsrc=[\\\"'](?<src>[^\\\"']*map-data\\.js(?:\\?[^\\\"']*)?)[\\\"'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OverlayScriptRegex();

    [GeneratedRegex("<script\\b[^>]*\\bsrc=[\\\"'](?<src>[^\\\"']*map-water\\.js(?:\\?[^\\\"']*)?)[\\\"'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WaterLabelScriptRegex();

    [GeneratedRegex("<img\\b(?=[^>]*\\bid=[\\\"']realMapImage[\\\"'])[^>]*\\bsrc=[\\\"'](?<src>[^\\\"']*gateway-preview\\.webp(?:\\?[^\\\"']*)?)[\\\"'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PreviewImageRegex();

    [GeneratedRegex("(?:^|[?&])v=(?<version>[^&]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    [GeneratedRegex("<[^>]{1,80}>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();
}
