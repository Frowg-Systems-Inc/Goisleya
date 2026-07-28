using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Isley;

internal static class PortableConfigLogic
{
    public const string Schema = "isley-portable-config";
    public const int SchemaVersion = 1;
    public const int MaximumCharacters = 120_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Export(object allowlistedSettings) =>
        JsonSerializer.Serialize(new
        {
            schema = Schema,
            schemaVersion = SchemaVersion,
            exportedAt = DateTimeOffset.UtcNow,
            note = "No Steam tokens, TURN credentials, or relay secrets are included.",
            settings = allowlistedSettings
        }, JsonOptions);

    public static bool TryParse(string? text, out JsonElement settings, out string error)
    {
        settings = default;
        error = "INVALID PORTABLE CONFIG";
        if (string.IsNullOrWhiteSpace(text)
            || text.Length > MaximumCharacters
            || text.Any(ch => char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t'))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schema", out var schema)
                || schema.GetString() != Schema
                || !root.TryGetProperty("schemaVersion", out var version)
                || version.GetInt32() != SchemaVersion
                || !root.TryGetProperty("settings", out var settingsElement)
                || settingsElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // Clone into a standalone element for the caller.
            settings = settingsElement.Clone();
            error = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string PreviewSummary(JsonElement settings)
    {
        var parts = new List<string>();
        if (settings.TryGetProperty("ActiveFocusModeId", out var focus)
            && focus.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(focus.GetString()))
        {
            parts.Add($"focus {focus.GetString()}");
        }
        if (settings.TryGetProperty("HotkeyBindings", out var hotkeys)
            && hotkeys.ValueKind == JsonValueKind.Array)
        {
            parts.Add($"{hotkeys.GetArrayLength()} hotkeys");
        }
        if (settings.TryGetProperty("CommunityServerProfiles", out var profiles)
            && profiles.ValueKind == JsonValueKind.Array)
        {
            parts.Add($"{profiles.GetArrayLength()} community profiles");
        }
        if (settings.TryGetProperty("HudDetailModeIndex", out _))
        {
            parts.Add("HUD prefs");
        }
        return parts.Count == 0
            ? "Portable prefs ready to import"
            : "Includes " + string.Join(" · ", parts);
    }
}
