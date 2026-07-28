using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Isley;

internal sealed record IsleyRelease(
    Version Version,
    string VersionText,
    DateTimeOffset PublishedAt,
    Uri DownloadUri,
    string Sha256,
    long Bytes,
    string Notes,
    bool Required);

internal static partial class IsleyReleaseLogic
{
    internal const int ManifestVersion = 1;
    internal const int MaxManifestBytes = 16 * 1024;
    internal const long MinimumArchiveBytes = 1 * 1024 * 1024;
    internal const long MaximumArchiveBytes = 100 * 1024 * 1024;
    internal const long MaximumExpandedBytes = 300 * 1024 * 1024;
    internal const int MaximumArchiveEntries = 2500;
    internal const string ReleaseEndpoint =
        "https://isley-download.gmith.chatgpt.site/Isley-release.json";
    internal const string StableDownloadUrl =
        "https://isley-download.gmith.chatgpt.site/Isley-Windows-x64.zip";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static IsleyRelease ParseManifest(string json, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(json)
            || Encoding.UTF8.GetByteCount(json) > MaxManifestBytes)
        {
            throw new InvalidDataException("The Isley release notice was empty or oversized.");
        }

        ReleaseManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ReleaseManifest>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Isley release notice was not valid JSON.", exception);
        }

        if (manifest is null
            || manifest.ManifestVersion != ManifestVersion
            || !string.Equals(manifest.Channel, "stable", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Isley release notice used an unsupported channel.");
        }

        var versionText = (manifest.Version ?? string.Empty).Trim();
        if (!ReleaseVersionPattern().IsMatch(versionText)
            || !Version.TryParse(versionText, out var version))
        {
            throw new InvalidDataException("The Isley release notice had an invalid version.");
        }

        if (!DateTimeOffset.TryParse(
                manifest.PublishedAt,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal
                | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var publishedAt)
            || publishedAt < new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero)
            || publishedAt > now.AddDays(1))
        {
            throw new InvalidDataException("The Isley release notice had an invalid publication time.");
        }

        if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var downloadUri)
            || !string.Equals(downloadUri.AbsoluteUri, StableDownloadUrl, StringComparison.Ordinal)
            || downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("The Isley release notice did not use the trusted download address.");
        }

        var hash = (manifest.Sha256 ?? string.Empty).Trim().ToUpperInvariant();
        if (!Sha256Pattern().IsMatch(hash))
        {
            throw new InvalidDataException("The Isley release notice had an invalid archive fingerprint.");
        }

        if (manifest.Bytes is < MinimumArchiveBytes or > MaximumArchiveBytes)
        {
            throw new InvalidDataException("The Isley release notice had an invalid archive size.");
        }

        var notes = CleanNotes(manifest.Notes);
        if (string.IsNullOrEmpty(notes))
        {
            notes = $"Isley {versionText} is ready.";
        }

        return new IsleyRelease(
            version,
            versionText,
            publishedAt,
            downloadUri,
            hash,
            manifest.Bytes,
            notes,
            manifest.Required);
    }

    internal static bool IsNewer(Version current, Version candidate) =>
        NormalizeVersion(candidate).CompareTo(NormalizeVersion(current)) > 0;

    internal static string DisplayVersion(Version version)
    {
        var normalized = NormalizeVersion(version);
        return $"{normalized.Major}.{normalized.Minor}.{normalized.Build}";
    }

    internal static string ResolveSafePackageEntry(string packageDirectory, string entryName)
    {
        if (string.IsNullOrWhiteSpace(packageDirectory)
            || string.IsNullOrWhiteSpace(entryName)
            || Path.IsPathRooted(entryName)
            || entryName.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(part => part == ".."))
        {
            throw new InvalidDataException("The Isley update contained an unsafe path.");
        }

        var root = Path.GetFullPath(packageDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(
            root,
            entryName.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The Isley update contained an unsafe path.");
        }
        return candidate;
    }

    private static Version NormalizeVersion(Version version) =>
        new(
            Math.Max(0, version.Major),
            Math.Max(0, version.Minor),
            Math.Max(0, version.Build));

    private static string CleanNotes(string? value)
    {
        var normalized = Regex.Replace(
            value ?? string.Empty,
            @"[\u0000-\u001F\u007F]+",
            " ");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return normalized.Length <= 220 ? normalized : normalized[..220];
    }

    private sealed class ReleaseManifest
    {
        [JsonPropertyName("manifestVersion")]
        public int ManifestVersion { get; set; }

        [JsonPropertyName("channel")]
        public string? Channel { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("publishedAt")]
        public string? PublishedAt { get; set; }

        [JsonPropertyName("downloadUrl")]
        public string? DownloadUrl { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }

        [JsonPropertyName("bytes")]
        public long Bytes { get; set; }

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("required")]
        public bool Required { get; set; }
    }

    [GeneratedRegex(@"^\d{1,4}\.\d{1,4}\.\d{1,6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseVersionPattern();

    [GeneratedRegex(@"^[A-F0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
