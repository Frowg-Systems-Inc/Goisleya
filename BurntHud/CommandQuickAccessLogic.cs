namespace Isley;

internal readonly record struct CommandFavoriteToggleResult(
    IReadOnlyList<string> Items,
    bool Changed,
    bool Added,
    bool LimitReached);

internal static class CommandQuickAccessLogic
{
    internal const int MaximumFavorites = 8;
    internal const int MaximumRecents = 8;

    internal static IReadOnlyList<string> NormalizeFavorites(
        IEnumerable<string>? requested,
        IEnumerable<string> validActionIds) =>
        Normalize(requested, validActionIds, MaximumFavorites);

    internal static IReadOnlyList<string> NormalizeRecents(
        IEnumerable<string>? requested,
        IEnumerable<string> validActionIds) =>
        Normalize(requested, validActionIds, MaximumRecents);

    internal static CommandFavoriteToggleResult ToggleFavorite(
        IEnumerable<string>? current,
        string? actionId,
        IEnumerable<string> validActionIds)
    {
        var valid = BuildValidSet(validActionIds);
        var items = Normalize(current, valid, MaximumFavorites).ToList();
        var normalizedActionId = NormalizeActionId(actionId);
        if (normalizedActionId.Length == 0 || !valid.Contains(normalizedActionId))
        {
            return new(items, Changed: false, Added: false, LimitReached: false);
        }

        var existingIndex = items.FindIndex(
            item => string.Equals(item, normalizedActionId, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            items.RemoveAt(existingIndex);
            return new(items, Changed: true, Added: false, LimitReached: false);
        }

        if (items.Count >= MaximumFavorites)
        {
            return new(items, Changed: false, Added: false, LimitReached: true);
        }

        items.Insert(0, normalizedActionId);
        return new(items, Changed: true, Added: true, LimitReached: false);
    }

    internal static IReadOnlyList<string> RecordRecent(
        IEnumerable<string>? current,
        string? actionId,
        IEnumerable<string> validActionIds)
    {
        var valid = BuildValidSet(validActionIds);
        var items = Normalize(current, valid, MaximumRecents).ToList();
        var normalizedActionId = NormalizeActionId(actionId);
        if (normalizedActionId.Length == 0 || !valid.Contains(normalizedActionId))
        {
            return items;
        }

        items.RemoveAll(
            item => string.Equals(item, normalizedActionId, StringComparison.OrdinalIgnoreCase));
        items.Insert(0, normalizedActionId);
        if (items.Count > MaximumRecents)
        {
            items.RemoveRange(MaximumRecents, items.Count - MaximumRecents);
        }

        return items;
    }

    internal static IReadOnlyList<string> BuildDefaultOrder(
        IEnumerable<string> allActionIds,
        IEnumerable<string>? favorites,
        IEnumerable<string>? recents,
        int maximumResults)
    {
        if (maximumResults <= 0)
        {
            return [];
        }

        var catalog = BuildCatalog(allActionIds);
        var valid = catalog.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>(Math.Min(maximumResults, catalog.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddUnique(Normalize(favorites, valid, MaximumFavorites));
        AddUnique(Normalize(recents, valid, MaximumRecents));
        AddUnique(catalog);
        return ordered;

        void AddUnique(IEnumerable<string> candidates)
        {
            foreach (var candidate in candidates)
            {
                if (ordered.Count >= maximumResults)
                {
                    return;
                }

                if (seen.Add(candidate))
                {
                    ordered.Add(candidate);
                }
            }
        }
    }

    private static IReadOnlyList<string> Normalize(
        IEnumerable<string>? requested,
        IEnumerable<string> validActionIds,
        int maximum)
    {
        var valid = validActionIds as HashSet<string> ?? BuildValidSet(validActionIds);
        var normalized = new List<string>(maximum);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawActionId in requested ?? [])
        {
            var actionId = NormalizeActionId(rawActionId);
            if (actionId.Length == 0 || !valid.Contains(actionId) || !seen.Add(actionId))
            {
                continue;
            }

            normalized.Add(actionId);
            if (normalized.Count >= maximum)
            {
                break;
            }
        }

        return normalized;
    }

    private static HashSet<string> BuildValidSet(IEnumerable<string> validActionIds) =>
        BuildCatalog(validActionIds).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static List<string> BuildCatalog(IEnumerable<string> actionIds)
    {
        var catalog = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawActionId in actionIds ?? [])
        {
            var actionId = NormalizeActionId(rawActionId);
            if (actionId.Length > 0 && seen.Add(actionId))
            {
                catalog.Add(actionId);
            }
        }

        return catalog;
    }

    private static string NormalizeActionId(string? actionId) =>
        (actionId ?? string.Empty).Trim();
}
