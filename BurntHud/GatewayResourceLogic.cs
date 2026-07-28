using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Isley;

public sealed record GatewayResourcePoint(
    string Bucket,
    string Key,
    string Name,
    string Group,
    double X,
    double Y,
    DateOnly? Updated,
    int? RespawnSeconds)
{
    public string Id => string.Create(
        CultureInfo.InvariantCulture,
        $"{Bucket}:{Key}:{X:0.0}:{Y:0.0}");
}

public sealed record GatewayResourceNetwork(
    string AssetUrl,
    string Version,
    DateTimeOffset RetrievedAt,
    IReadOnlyList<GatewayResourcePoint> Points)
{
    public int PointCount => Points.Count;

    public DateOnly? LatestSiteDate => Points
        .Where(point => point.Updated is not null)
        .Select(point => point.Updated)
        .Max();
}

public sealed record ResourceFinderSelection(
    string Query,
    GatewayResourcePoint Site,
    int MatchCount,
    int SelectedIndex,
    double? Distance,
    double? Bearing,
    string Cardinal)
{
    public bool HasLiveDistance => Distance is not null && Bearing is not null;
}

public static class ResourceFinderLogic
{
    private const double MapSize = 1000;

    private static readonly IReadOnlyDictionary<string, string> ExactAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["salt"] = "saltrock",
            ["saltlick"] = "saltrock",
            ["saltrock"] = "saltrock",
            ["mud"] = "mudwallow",
            ["wallow"] = "mudwallow",
            ["mudwallow"] = "mudwallow",
            ["stone"] = "gastro",
            ["stones"] = "gastro",
            ["gastrolith"] = "gastro",
            ["gastroliths"] = "gastro",
            ["schoolingfish"] = "fish",
            ["elitefish"] = "fish",
            ["bullfrog"] = "frog",
            ["seaturtle"] = "turtle",
            ["mountainash"] = "ash",
            ["radishflower"] = "radish",
            ["radishroot"] = "radish",
            ["potatovine"] = "potatovine"
        };

    private static readonly IReadOnlyDictionary<string, string> BucketAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ai"] = "animals",
            ["animal"] = "animals",
            ["animals"] = "animals",
            ["meat"] = "animals",
            ["prey"] = "animals",
            ["plant"] = "herbs",
            ["plants"] = "herbs",
            ["herb"] = "herbs",
            ["herbs"] = "herbs",
            ["fruit"] = "herbs",
            ["food"] = "herbs",
            ["earth"] = "earth",
            ["utility"] = "earth"
        };

    public static string NormalizeQuery(string? query)
    {
        var words = string.Join(' ', (query ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var clean = new string(words
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is ' ' or '-')
            .Take(32)
            .ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "salt" : clean.Trim();
    }

    public static ResourceFinderSelection? Select(
        IEnumerable<GatewayResourcePoint>? points,
        string? query,
        double? selfX,
        double? selfY,
        int selectedIndex = 0)
    {
        var normalizedQuery = NormalizeQuery(query);
        var queryKey = Compact(normalizedQuery);
        var hasSelf = IsMapPoint(selfX, selfY);
        var candidates = (points ?? [])
            .Select(point => new
            {
                Point = point,
                Score = Score(point, queryKey),
                Distance = hasSelf
                    ? Distance(selfX!.Value, selfY!.Value, point.X, point.Y)
                    : double.PositiveInfinity
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Point.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Point.X)
            .ThenBy(candidate => candidate.Point.Y)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var index = ((selectedIndex % candidates.Count) + candidates.Count) % candidates.Count;
        var chosen = candidates[index];
        double? distance = null;
        double? bearing = null;
        var cardinal = string.Empty;
        if (hasSelf)
        {
            distance = chosen.Distance;
            bearing = Bearing(selfX!.Value, selfY!.Value, chosen.Point.X, chosen.Point.Y);
            cardinal = Cardinal(bearing.Value);
        }

        return new ResourceFinderSelection(
            normalizedQuery,
            chosen.Point,
            candidates.Count,
            index,
            distance,
            bearing,
            cardinal);
    }

    public static string SuggestedDietQuery(
        int speciesIndex,
        int nutrient,
        IEnumerable<GatewayResourcePoint>? points)
    {
        var normalizedIndex = DietCoachLogic.NormalizeSpeciesIndex(speciesIndex);
        var normalizedNutrient = DietCoachLogic.NormalizeNutrient(nutrient);
        if (normalizedIndex <= 0 || normalizedNutrient == DietCoachLogic.Empty)
        {
            return "food";
        }

        var species = DietCoachLogic.Species[normalizedIndex - 1];
        if (species.MigrationDriven)
        {
            return "plant";
        }

        var foods = normalizedNutrient switch
        {
            DietCoachLogic.Protein => species.ProteinFoods,
            DietCoachLogic.Carbs => species.CarbFoods,
            DietCoachLogic.Lipids => species.LipidFoods,
            _ => []
        };
        foreach (var food in foods)
        {
            var query = SuggestedFoodQuery(food);
            if (Select(points, query, null, null) is not null)
            {
                return query;
            }
        }
        return species.DietClass.Equals("Carnivore", StringComparison.OrdinalIgnoreCase)
            ? "prey"
            : "food";
    }

    public static string SuggestedFoodQuery(string? food)
    {
        var compact = Compact(food);
        return ExactAliases.TryGetValue(compact, out var alias)
            ? alias
            : compact switch
            {
                "boar" or "chicken" or "deer" or "dryosaurus" or "gallimimus" or
                    "goat" or "rabbit" or "crab" => compact,
                _ => compact.Length > 0 ? compact : "food"
            };
    }

    public static string ApproachKind(GatewayResourcePoint? point)
    {
        if (point is null) return "resource";
        var key = Compact(point.Key);
        if (key == "saltrock") return "salt";
        if (key == "mudwallow") return "mud";
        if (key == "gastro") return "gastrolith";
        return Compact(point.Bucket) is "animals" or "herbs"
            ? "food"
            : "resource";
    }

    private static int Score(GatewayResourcePoint point, string queryKey)
    {
        var key = Compact(point.Key);
        var name = Compact(point.Name);
        var group = Compact(point.Group);
        var bucket = Compact(point.Bucket);
        if (ExactAliases.TryGetValue(queryKey, out var exactKey))
        {
            return key == exactKey ? 500 : 0;
        }
        if (BucketAliases.TryGetValue(queryKey, out var exactBucket))
        {
            return bucket == exactBucket ? 420 : 0;
        }
        if (key == queryKey || name == queryKey) return 400;
        if (key.StartsWith(queryKey, StringComparison.Ordinal)
            || name.StartsWith(queryKey, StringComparison.Ordinal)) return 300;
        if (key.Contains(queryKey, StringComparison.Ordinal)
            || name.Contains(queryKey, StringComparison.Ordinal)) return 220;
        if (group.Contains(queryKey, StringComparison.Ordinal)) return 120;
        return 0;
    }

    private static string Compact(string? value) => new((value ?? string.Empty)
        .ToLowerInvariant()
        .Where(char.IsAsciiLetterOrDigit)
        .Take(48)
        .ToArray());

    private static bool IsMapPoint(double? x, double? y) =>
        x is not null && y is not null
        && double.IsFinite(x.Value) && double.IsFinite(y.Value)
        && x.Value is >= 0 and <= MapSize && y.Value is >= 0 and <= MapSize;

    private static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));

    private static double Bearing(double x1, double y1, double x2, double y2) =>
        (Math.Atan2(x2 - x1, -(y2 - y1)) * 180 / Math.PI + 360) % 360;

    private static string Cardinal(double bearing) => (((int)Math.Round(bearing / 45) + 8) % 8) switch
    {
        0 => "N",
        1 => "NE",
        2 => "E",
        3 => "SE",
        4 => "S",
        5 => "SW",
        6 => "W",
        _ => "NW"
    };
}

public static partial class GatewayResourceClient
{
    public const string IndexPage = "https://myislemap.com/";
    private const int MaxIndexBytes = 256_000;
    private const int MaxAssetBytes = 768_000;
    private const int MaxPointCount = 5_000;
    private const double SourceMapHeight = 1003;

    private static readonly HttpClient Client = CreateClient();

    public static async Task<GatewayResourceNetwork> FetchAsync(CancellationToken cancellationToken)
    {
        var index = await FetchTextAsync(new Uri(IndexPage), MaxIndexBytes, cancellationToken);
        var assetUri = ResolveAssetUri(index);
        var asset = await FetchTextAsync(assetUri, MaxAssetBytes, cancellationToken);
        return ParseJavaScriptAsset(asset, assetUri, DateTimeOffset.UtcNow);
    }

    public static Uri ResolveAssetUri(string indexHtml)
    {
        var match = DataScriptRegex().Match(indexHtml ?? string.Empty);
        if (!match.Success)
        {
            throw new InvalidDataException("The current resource asset was not advertised by the map source.");
        }

        var relative = WebUtility.HtmlDecode(match.Groups["src"].Value);
        if (!Uri.TryCreate(new Uri(IndexPage), relative, out var assetUri)
            || !IsTrustedAsset(assetUri))
        {
            throw new InvalidDataException("The resource asset URL was not trusted.");
        }
        return assetUri;
    }

    public static GatewayResourceNetwork ParseJavaScriptAsset(
        string source,
        Uri assetUri,
        DateTimeOffset retrievedAt)
    {
        if (!IsTrustedAsset(assetUri))
        {
            throw new InvalidDataException("The resource asset URL was not trusted.");
        }

        var safeSource = source ?? string.Empty;
        if (!safeSource.Contains("const MAP_OVERLAYS", StringComparison.Ordinal)
            || !safeSource.Contains("window.MAP_OVERLAYS = MAP_OVERLAYS", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The resource asset did not contain the expected data envelope.");
        }

        var points = new List<GatewayResourcePoint>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match objectMatch in ObjectRegex().Matches(safeSource))
        {
            var body = objectMatch.Groups["body"].Value;
            if (!TryReadString(body, "bucket", out var bucket))
            {
                continue;
            }

            bucket = CleanToken(bucket);
            if (bucket is not ("animals" or "herbs" or "earth")
                || !TryReadString(body, "key", out var key)
                || !TryReadString(body, "name", out var name)
                || !TryReadString(body, "group", out var group)
                || !TryReadNumber(body, "x", out var x)
                || !TryReadNumber(body, "y", out var sourceY))
            {
                throw new InvalidDataException("The resource asset contained an invalid site.");
            }

            key = CleanToken(key);
            name = CleanLabel(name, 48);
            group = CleanLabel(group, 56);
            if (key.Length == 0 || name.Length == 0 || group.Length == 0
                || !double.IsFinite(x) || !double.IsFinite(sourceY)
                || x is < 0 or > 1000 || sourceY is < 0 or > SourceMapHeight)
            {
                throw new InvalidDataException("The resource asset contained an out-of-range site.");
            }

            DateOnly? updated = null;
            if (TryReadString(body, "updated", out var rawUpdated))
            {
                if (!DateOnly.TryParseExact(
                        rawUpdated,
                        "yyyy/MM/dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var parsedUpdated))
                {
                    throw new InvalidDataException("The resource asset contained an invalid update date.");
                }
                updated = parsedUpdated;
            }

            int? respawnSeconds = null;
            if (TryReadNumber(body, "respawnS", out var rawRespawn))
            {
                if (rawRespawn is < 0 or > 86_400 || rawRespawn != Math.Truncate(rawRespawn))
                {
                    throw new InvalidDataException("The resource asset contained an invalid respawn value.");
                }
                respawnSeconds = (int)rawRespawn;
            }

            var normalizedY = sourceY / SourceMapHeight * 1000;
            var point = new GatewayResourcePoint(
                bucket,
                key,
                name,
                group,
                x,
                normalizedY,
                updated,
                respawnSeconds);
            if (seen.Add(point.Id))
            {
                points.Add(point);
                if (points.Count > MaxPointCount)
                {
                    throw new InvalidDataException("The resource asset exceeded the safe site limit.");
                }
            }
        }

        if (points.Count == 0)
        {
            throw new InvalidDataException("The resource asset contained no usable sites.");
        }

        var versionMatch = Regex.Match(
            assetUri.Query,
            "(?:^|[?&])v=(?<version>[^&]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var version = CleanVersion(versionMatch.Success
            ? Uri.UnescapeDataString(versionMatch.Groups["version"].Value)
            : string.Empty);
        return new GatewayResourceNetwork(
            assetUri.AbsoluteUri,
            version,
            retrievedAt,
            points);
    }

    private static bool IsTrustedAsset(Uri assetUri) =>
        string.Equals(assetUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && string.Equals(assetUri.Host, "myislemap.com", StringComparison.OrdinalIgnoreCase)
        && assetUri.AbsolutePath.EndsWith("/map-data.js", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadString(string body, string field, out string value)
    {
        var match = Regex.Match(
            body,
            $"\\b{Regex.Escape(field)}\\s*:\\s*\\\"(?<value>(?:\\\\.|[^\\\"\\\\])*)\\\"",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            value = string.Empty;
            return false;
        }
        try
        {
            value = JsonSerializer.Deserialize<string>($"\"{match.Groups["value"].Value}\"")
                    ?? string.Empty;
            return true;
        }
        catch (JsonException)
        {
            value = string.Empty;
            return false;
        }
    }

    private static bool TryReadNumber(string body, string field, out double value)
    {
        value = 0;
        var match = Regex.Match(
            body,
            $@"\b{Regex.Escape(field)}\s*:\s*(?<value>-?(?:\d+(?:\.\d+)?|\.\d+))(?=\s*[,}}])",
            RegexOptions.CultureInvariant);
        return match.Success && double.TryParse(
            match.Groups["value"].Value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static string CleanToken(string? value) => new((value ?? string.Empty)
        .Trim()
        .ToLowerInvariant()
        .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
        .Take(32)
        .ToArray());

    private static string CleanLabel(string? value, int maximumLength)
    {
        var label = string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (label.Any(char.IsControl)) return string.Empty;
        return label[..Math.Min(maximumLength, label.Length)];
    }

    private static string CleanVersion(string? value)
    {
        var version = Regex.Replace(value ?? string.Empty, "[^A-Za-z0-9._-]", string.Empty);
        return string.IsNullOrWhiteSpace(version) ? "live" : version[..Math.Min(24, version.Length)];
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
            throw new InvalidDataException("The resource source exceeded the safe download limit.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read <= 0) break;
            if (buffer.Length + read > maximumBytes)
            {
                throw new InvalidDataException("The resource source exceeded the safe download limit.");
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Isley/1.0 resource-finder");
        return client;
    }

    [GeneratedRegex("<script\\b[^>]*\\bsrc=[\\\"'](?<src>[^\\\"']*map-data\\.js(?:\\?[^\\\"']*)?)[\\\"'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DataScriptRegex();

    [GeneratedRegex("\\{(?<body>[^{}]{1,1800})\\}", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ObjectRegex();
}
