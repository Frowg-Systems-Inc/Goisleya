namespace Isley;

internal sealed record HudLayoutProfile(
    string Name,
    bool HudDockMirrored,
    bool Expanded,
    double Width,
    double Height,
    int HudDetailModeIndex,
    bool NavigationHudVisible,
    bool VitalsHudVisible,
    bool SurvivalHudVisible,
    bool AlertHudVisible,
    bool QuickKeysHudVisible,
    int QuickKeysModeIndex,
    long SavedAtUnixMs);

internal static class LayoutProfileLogic
{
    internal const int MaximumProfiles = 8;
    internal const int MaximumNameLength = 24;
    internal const int HudDetailModeCount = 3;
    internal const double MinimumSize = 240;
    internal const double MaximumSize = 3840;
    internal const double FallbackWidth = 472;
    internal const double FallbackHeight = 560;
    private const int TrackedHudSurfaceCount = 5;

    internal static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var cleaned = new string(name.Trim().Where(ch => !char.IsControl(ch)).ToArray());
        var collapsed = string.Join(
            ' ',
            cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= MaximumNameLength
            ? collapsed
            : collapsed[..MaximumNameLength].TrimEnd();
    }

    internal static string FallbackName(int index) => $"Layout {Math.Max(0, index) + 1}";

    internal static string UniqueName(
        string? requested,
        IEnumerable<string> existingNames,
        int fallbackIndex)
    {
        var taken = new HashSet<string>(
            existingNames ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        var baseName = NormalizeName(requested);
        if (baseName.Length == 0)
        {
            baseName = FallbackName(fallbackIndex);
        }
        if (!taken.Contains(baseName))
        {
            return baseName;
        }

        for (var suffix = 2; suffix <= 99; suffix++)
        {
            var tag = $" {suffix}";
            var candidate = baseName.Length + tag.Length <= MaximumNameLength
                ? baseName + tag
                : baseName[..(MaximumNameLength - tag.Length)].TrimEnd() + tag;
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return baseName;
    }

    internal static HudLayoutProfile Normalize(HudLayoutProfile? candidate, int fallbackIndex)
    {
        var source = candidate ?? new HudLayoutProfile(
            string.Empty,
            false,
            false,
            FallbackWidth,
            FallbackHeight,
            0,
            true,
            true,
            true,
            true,
            false,
            0,
            0);
        var name = NormalizeName(source.Name);
        return source with
        {
            Name = name.Length > 0 ? name : FallbackName(fallbackIndex),
            Width = ClampSize(source.Width, FallbackWidth),
            Height = ClampSize(source.Height, FallbackHeight),
            HudDetailModeIndex = Math.Clamp(source.HudDetailModeIndex, 0, HudDetailModeCount - 1),
            QuickKeysModeIndex = QuickKeysLogic.NormalizeModeIndex(source.QuickKeysModeIndex),
            SavedAtUnixMs = Math.Max(0, source.SavedAtUnixMs)
        };
    }

    internal static List<HudLayoutProfile> NormalizeProfiles(IEnumerable<HudLayoutProfile>? saved)
    {
        var result = new List<HudLayoutProfile>(MaximumProfiles);
        if (saved is null)
        {
            return result;
        }

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in saved)
        {
            if (result.Count >= MaximumProfiles)
            {
                break;
            }

            var normalized = Normalize(candidate, result.Count);
            if (!taken.Add(normalized.Name))
            {
                normalized = Normalize(
                    normalized with { Name = FallbackName(result.Count) },
                    result.Count);
                if (!taken.Add(normalized.Name))
                {
                    continue;
                }
            }
            result.Add(normalized);
        }

        return result;
    }

    internal static int VisibleSurfaceCount(HudLayoutProfile profile) =>
        new[]
        {
            profile.NavigationHudVisible,
            profile.VitalsHudVisible,
            profile.SurvivalHudVisible,
            profile.AlertHudVisible,
            profile.QuickKeysHudVisible
        }.Count(visible => visible);

    internal static string Summary(HudLayoutProfile profile) =>
        $"{(profile.HudDockMirrored ? "DOCK LEFT" : "DOCK RIGHT")} · " +
        $"{VisibleSurfaceCount(profile)}/{TrackedHudSurfaceCount} HUDS · " +
        $"{profile.Width:0}×{profile.Height:0}";

    private static double ClampSize(double value, double fallback) =>
        double.IsFinite(value)
            ? Math.Clamp(value, MinimumSize, MaximumSize)
            : fallback;
}
