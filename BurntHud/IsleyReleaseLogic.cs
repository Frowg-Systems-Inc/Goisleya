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
    bool Required,
    string Channel,
    IsleyDeltaOffer? Delta);

internal sealed record IsleyDeltaOffer(
    Version FromVersion,
    string FromVersionText,
    Uri DownloadUri,
    string Sha256,
    long Bytes);

internal sealed record IsleyDeltaPlan(
    Version FromVersion,
    Version ToVersion,
    IReadOnlyList<string> DeletedFiles);

internal static partial class IsleyReleaseLogic
{
    internal const int ManifestVersion = 1;
    internal const int MaxManifestBytes = 16 * 1024;
    internal const long MinimumArchiveBytes = 1 * 1024 * 1024;
    internal const long MaximumArchiveBytes = 100 * 1024 * 1024;
    internal const long MaximumExpandedBytes = 300 * 1024 * 1024;
    internal const int MaximumArchiveEntries = 2500;
    internal const long MinimumDeltaBytes = 256;
    internal const int MaxDeltaManifestBytes = 64 * 1024;
    internal const int MaximumDeltaDeleteEntries = 2000;
    internal const int MaximumDeltaPathLength = 512;
    internal const string StableChannel = "stable";
    internal const string BetaChannel = "beta";
    internal const string TrustedDownloadHost = "isley-download.gmith.chatgpt.site";
    internal const string ReleaseEndpoint =
        "https://isley-download.gmith.chatgpt.site/Isley-release.json";
    internal const string BetaReleaseEndpoint =
        "https://isley-download.gmith.chatgpt.site/Isley-release-beta.json";
    internal const string StableDownloadUrl =
        "https://isley-download.gmith.chatgpt.site/Isley-Windows-x64.zip";
    internal const string BetaDownloadUrl =
        "https://isley-download.gmith.chatgpt.site/Isley-Windows-x64-beta.zip";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static IsleyRelease ParseManifest(string json, DateTimeOffset now) =>
        ParseManifest(json, now, StableChannel);

    internal static IsleyRelease ParseManifest(string json, DateTimeOffset now, string channel)
    {
        if (channel is not (StableChannel or BetaChannel))
        {
            throw new InvalidDataException("The Isley release channel request was unsupported.");
        }
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
            || !string.Equals(manifest.Channel, channel, StringComparison.Ordinal))
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

        var pinnedDownloadUrl = channel == BetaChannel ? BetaDownloadUrl : StableDownloadUrl;
        if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var downloadUri)
            || !string.Equals(downloadUri.AbsoluteUri, pinnedDownloadUrl, StringComparison.Ordinal)
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

        var delta = ParseDeltaOffer(manifest.Delta, version);
        return new IsleyRelease(
            version,
            versionText,
            publishedAt,
            downloadUri,
            hash,
            manifest.Bytes,
            notes,
            manifest.Required,
            channel,
            delta);
    }

    internal static IsleyDeltaPlan ParseDeltaManifest(
        string json,
        Version expectedFromVersion,
        Version expectedToVersion)
    {
        if (string.IsNullOrWhiteSpace(json)
            || Encoding.UTF8.GetByteCount(json) > MaxDeltaManifestBytes)
        {
            throw new InvalidDataException("The Isley delta file list was empty or oversized.");
        }

        DeltaPackageManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<DeltaPackageManifest>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Isley delta file list was not valid JSON.", exception);
        }

        var fromText = (manifest?.FromVersion ?? string.Empty).Trim();
        var toText = (manifest?.ToVersion ?? string.Empty).Trim();
        if (manifest is null
            || manifest.Format != 1
            || !ReleaseVersionPattern().IsMatch(fromText)
            || !ReleaseVersionPattern().IsMatch(toText)
            || !Version.TryParse(fromText, out var fromVersion)
            || !Version.TryParse(toText, out var toVersion)
            || !IsSameVersion(fromVersion, expectedFromVersion)
            || !IsSameVersion(toVersion, expectedToVersion))
        {
            throw new InvalidDataException("The Isley delta file list did not match its release notice.");
        }

        var deleted = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.DeletedFiles ?? [])
        {
            if (deleted.Count >= MaximumDeltaDeleteEntries)
            {
                throw new InvalidDataException("The Isley delta file list exceeded its safety limit.");
            }
            var cleaned = ValidateDeltaRelativePath(entry);
            if (seen.Add(cleaned))
            {
                deleted.Add(cleaned);
            }
        }

        return new IsleyDeltaPlan(fromVersion, toVersion, deleted);
    }

    internal static string ValidateDeltaRelativePath(string? entry)
    {
        var cleaned = (entry ?? string.Empty).Trim().Replace('/', '\\');
        if (cleaned.Length == 0
            || cleaned.Length > MaximumDeltaPathLength
            || cleaned.IndexOfAny(new[] { '/', ':', '\0' }) >= 0
            || cleaned.Any(char.IsControl)
            || Path.IsPathRooted(cleaned))
        {
            throw new InvalidDataException("The Isley delta file list contained an unsafe path.");
        }

        var segments = cleaned.Split(
            new[] { '\\' },
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0
            || segments.Any(part => part == "..")
            || string.Equals(segments[0], "IsleyData", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The Isley delta file list contained an unsafe path.");
        }
        return string.Join('\\', segments);
    }

    internal static bool IsNewer(Version current, Version candidate) =>
        NormalizeVersion(candidate).CompareTo(NormalizeVersion(current)) > 0;

    internal static bool IsSameVersion(Version left, Version right) =>
        NormalizeVersion(left).CompareTo(NormalizeVersion(right)) == 0;

    internal static bool IsValidVersionText(string? versionText) =>
        !string.IsNullOrWhiteSpace(versionText)
        && ReleaseVersionPattern().IsMatch(versionText.Trim());

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

    private static IsleyDeltaOffer? ParseDeltaOffer(ReleaseDelta? deltaBlock, Version releaseVersion)
    {
        if (deltaBlock is null)
        {
            return null;
        }

        var fromText = (deltaBlock.FromVersion ?? string.Empty).Trim();
        if (!ReleaseVersionPattern().IsMatch(fromText)
            || !Version.TryParse(fromText, out var fromVersion)
            || NormalizeVersion(fromVersion).CompareTo(NormalizeVersion(releaseVersion)) >= 0)
        {
            throw new InvalidDataException("The Isley release notice had an invalid delta base version.");
        }

        if (!Uri.TryCreate(deltaBlock.Url, UriKind.Absolute, out var deltaUri)
            || deltaUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(deltaUri.Host, TrustedDownloadHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The Isley release notice did not use the trusted delta address.");
        }

        var deltaHash = (deltaBlock.Sha256 ?? string.Empty).Trim().ToUpperInvariant();
        if (!Sha256Pattern().IsMatch(deltaHash))
        {
            throw new InvalidDataException("The Isley release notice had an invalid delta fingerprint.");
        }

        if (deltaBlock.Bytes is < MinimumDeltaBytes or > MaximumArchiveBytes)
        {
            throw new InvalidDataException("The Isley release notice had an invalid delta size.");
        }

        return new IsleyDeltaOffer(
            fromVersion,
            fromText,
            deltaUri,
            deltaHash,
            deltaBlock.Bytes);
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

        [JsonPropertyName("delta")]
        public ReleaseDelta? Delta { get; set; }
    }

    private sealed class ReleaseDelta
    {
        [JsonPropertyName("fromVersion")]
        public string? FromVersion { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }

        [JsonPropertyName("bytes")]
        public long Bytes { get; set; }
    }

    private sealed class DeltaPackageManifest
    {
        [JsonPropertyName("format")]
        public int Format { get; set; }

        [JsonPropertyName("fromVersion")]
        public string? FromVersion { get; set; }

        [JsonPropertyName("toVersion")]
        public string? ToVersion { get; set; }

        [JsonPropertyName("deletedFiles")]
        public List<string>? DeletedFiles { get; set; }
    }

    [GeneratedRegex(@"^\d{1,4}\.\d{1,4}\.\d{1,6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseVersionPattern();

    [GeneratedRegex(@"^[A-F0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
