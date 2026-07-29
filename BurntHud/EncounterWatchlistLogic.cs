namespace Isley;

internal readonly record struct EncounterWatchEntry(
    string Name,
    int? DistanceMu,
    string Cardinal,
    long AddedAtUnixMs);

// Session-only encounter watchlist for authorized live-map players, populated
// from the map shell's right-click context action. Names are normalized the
// same way as Steam friend watch map names; the list is deduplicated
// case-insensitively and bounded at 32 entries (oldest dropped first).
internal static class EncounterWatchlistLogic
{
    internal const int MaximumWatchedPlayers = 32;

    private static readonly string[] Cardinals = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    internal static string NormalizeName(string? value) =>
        SteamFriendLogic.NormalizeMapName(value);

    internal static string NormalizeCardinal(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return Cardinals.Contains(normalized) ? normalized : string.Empty;
    }

    internal static int? NormalizeDistanceMu(double? value)
    {
        if (!value.HasValue || !double.IsFinite(value.Value) || value.Value < 0)
        {
            return null;
        }

        return (int)Math.Clamp(Math.Round(value.Value / 5d) * 5d, 0, 1_000_000);
    }

    // Re-watching an already-listed player refreshes that entry's snapshot and
    // moves it to the front instead of creating a duplicate.
    internal static List<EncounterWatchEntry> Upsert(
        IEnumerable<EncounterWatchEntry>? entries,
        string? name,
        int? distanceMu,
        string? cardinal,
        DateTimeOffset now)
    {
        var normalizedName = NormalizeName(name);
        var retained = (entries ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .Where(entry => !string.Equals(entry.Name, normalizedName, StringComparison.OrdinalIgnoreCase))
            .Take(MaximumWatchedPlayers)
            .ToList();
        if (normalizedName.Length > 0)
        {
            retained.Insert(0, new EncounterWatchEntry(
                normalizedName,
                distanceMu,
                NormalizeCardinal(cardinal),
                now.ToUnixTimeMilliseconds()));
        }

        return retained.Count <= MaximumWatchedPlayers
            ? retained
            : retained.Take(MaximumWatchedPlayers).ToList();
    }
}
