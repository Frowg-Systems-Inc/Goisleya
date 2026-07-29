namespace Isley;

internal readonly record struct ServerRatePreset(
    string Id,
    string Label,
    int MultiplierIndex);

internal enum ServerRatePresetSaveResult
{
    Created,
    AlreadyTracked,
    LimitReached
}

internal static class ServerRatePresetLogic
{
    internal const string Snapshot = "2026-07-28";
    internal const int MaximumCustomPresets = 4;
    internal const int MaximumLabelLength = 24;
    internal const string CustomIdPrefix = "custom-";

    // Two built-in named presets cover the common community-server rates; everything
    // else is saved by the player as a bounded custom preset.
    internal static readonly ServerRatePreset[] BuiltInPresets =
    [
        new("official-1x", "OFFICIAL 1X", 0),
        new("boosted-2x", "BOOSTED 2X", 2)
    ];

    internal static int NormalizeMultiplierIndex(int index) =>
        Math.Clamp(index, 0, GrowthPlannerLogic.ServerMultipliers.Length - 1);

    internal static string SanitizeId(string? value)
    {
        var sanitized = new string((value ?? string.Empty)
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            .Select(char.ToLowerInvariant)
            .ToArray())
            .Trim('-', '_');
        return sanitized.Length <= 32 ? sanitized : sanitized[..32];
    }

    internal static string SanitizeLabel(string? value, string fallback)
    {
        var sanitized = new string((value ?? string.Empty)
            .Where(character => !char.IsControl(character))
            .ToArray());
        sanitized = string.Join(' ', sanitized
            .Replace('|', '/')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return fallback;
        }

        return sanitized.Length <= MaximumLabelLength ? sanitized : sanitized[..MaximumLabelLength].TrimEnd();
    }

    internal static string CustomLabel(int multiplierIndex) =>
        $"CUSTOM {GrowthPlannerLogic.ServerMultipliers[NormalizeMultiplierIndex(multiplierIndex)]:0.#}X";

    internal static ServerRatePreset NormalizePreset(ServerRatePreset preset) => new(
        SanitizeId(preset.Id),
        SanitizeLabel(preset.Label, "CUSTOM RATE"),
        NormalizeMultiplierIndex(preset.MultiplierIndex));

    internal static List<ServerRatePreset> NormalizeCustomPresets(IEnumerable<ServerRatePreset>? presets)
    {
        var normalized = new List<ServerRatePreset>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in presets ?? [])
        {
            var preset = NormalizePreset(source);
            if (string.IsNullOrEmpty(preset.Id)
                || !preset.Id.StartsWith(CustomIdPrefix, StringComparison.Ordinal)
                || !usedIds.Add(preset.Id))
            {
                continue;
            }

            normalized.Add(preset);
            if (normalized.Count >= MaximumCustomPresets)
            {
                break;
            }
        }

        return normalized;
    }

    internal static IReadOnlyList<ServerRatePreset> All(IEnumerable<ServerRatePreset>? customPresets) =>
        BuiltInPresets.Concat(NormalizeCustomPresets(customPresets)).ToArray();

    internal static ServerRatePreset? Find(IEnumerable<ServerRatePreset> presets, string? id)
    {
        var normalizedId = SanitizeId(id);
        if (string.IsNullOrEmpty(normalizedId))
        {
            return null;
        }

        foreach (var preset in presets)
        {
            if (string.Equals(preset.Id, normalizedId, StringComparison.Ordinal))
            {
                return preset;
            }
        }

        return null;
    }

    // Picks the preset after the current one; without a selection, picks the preset that
    // matches the active multiplier (so the first apply feels like a no-op rename), then
    // falls back to the first preset.
    internal static ServerRatePreset Next(
        IReadOnlyList<ServerRatePreset> presets,
        string? currentId,
        int currentMultiplierIndex)
    {
        if (presets.Count == 0)
        {
            return BuiltInPresets[0];
        }

        var current = Find(presets, currentId);
        if (current is { } selected)
        {
            var index = IndexOf(presets, selected.Id);
            return presets[(index + 1) % presets.Count];
        }

        var normalizedIndex = NormalizeMultiplierIndex(currentMultiplierIndex);
        foreach (var preset in presets)
        {
            if (preset.MultiplierIndex == normalizedIndex)
            {
                return preset;
            }
        }

        return presets[0];
    }

    internal static ServerRatePresetSaveResult TryCreateCustom(
        int multiplierIndex,
        IReadOnlyList<ServerRatePreset> customPresets,
        out ServerRatePreset preset)
    {
        var normalizedIndex = NormalizeMultiplierIndex(multiplierIndex);
        if (BuiltInPresets.Any(builtIn => builtIn.MultiplierIndex == normalizedIndex)
            || customPresets.Any(custom => custom.MultiplierIndex == normalizedIndex))
        {
            preset = default;
            return ServerRatePresetSaveResult.AlreadyTracked;
        }

        if (customPresets.Count >= MaximumCustomPresets)
        {
            preset = default;
            return ServerRatePresetSaveResult.LimitReached;
        }

        preset = new ServerRatePreset(
            $"{CustomIdPrefix}{normalizedIndex}",
            CustomLabel(normalizedIndex),
            normalizedIndex);
        return ServerRatePresetSaveResult.Created;
    }

    private static int IndexOf(IReadOnlyList<ServerRatePreset> presets, string id)
    {
        for (var index = 0; index < presets.Count; index++)
        {
            if (string.Equals(presets[index].Id, id, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return 0;
    }
}
