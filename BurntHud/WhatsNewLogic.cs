using System.Text;
using System.Text.Json;

namespace Isley;

internal readonly record struct WhatsNewPresentation(
    string Title,
    string Body,
    string Version);

internal static class WhatsNewLogic
{
    public const int MaximumBodyCharacters = 4000;

    public static WhatsNewPresentation FromJson(string? json, string currentVersion)
    {
        var version = string.IsNullOrWhiteSpace(currentVersion) ? "1.3.0" : currentVersion.Trim();
        if (string.IsNullOrWhiteSpace(json))
        {
            return Fallback(version);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var title = root.TryGetProperty("title", out var titleValue)
                ? (titleValue.GetString() ?? string.Empty).Trim()
                : string.Empty;
            var body = root.TryGetProperty("body", out var bodyValue)
                ? (bodyValue.GetString() ?? string.Empty).Trim()
                : string.Empty;
            var releaseVersion = root.TryGetProperty("version", out var versionValue)
                ? (versionValue.GetString() ?? version).Trim()
                : version;
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(body))
            {
                return Fallback(version);
            }

            if (body.Length > MaximumBodyCharacters)
            {
                body = body[..MaximumBodyCharacters];
            }

            return new WhatsNewPresentation(title, body, releaseVersion);
        }
        catch (JsonException)
        {
            return Fallback(version);
        }
    }

    public static bool ShouldHighlight(string lastSeenVersion, string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(currentVersion)) return false;
        return !string.Equals(
            (lastSeenVersion ?? string.Empty).Trim(),
            currentVersion.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static WhatsNewPresentation Fallback(string version) =>
        new(
            $"Isley {version}",
            new StringBuilder()
                .AppendLine("• Live Map with Gateway basemap, zones, and resources")
                .AppendLine("• Optional Isley Live Network for participating servers")
                .AppendLine("• Private PTT proximity voice with explicit pack offers")
                .AppendLine("• Core Vitals, routes, survival coaches, and portable updates")
                .Append("Open Tools for Live Network, Voice, and Patch Watch.")
                .ToString(),
            version);
}
