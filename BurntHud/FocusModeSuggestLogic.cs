namespace Isley;

internal readonly record struct FocusModeSuggestion(
    bool Available,
    string ModeId,
    string Label,
    string Reason);

internal static class FocusModeSuggestLogic
{
    public static FocusModeSuggestion FromNextMove(
        string category,
        string tone,
        string activeFocusModeId)
    {
        var modeId = (category ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "PACK" => "pack",
            "NEST" => "nest",
            "CONTACT" => "combat",
            "COMBAT" => "combat",
            "TRAVEL" or "ROUTE" or "WAYPOINT" => "travel",
            "SURVIVAL" or "VITALS" or "FOOD" or "WATER" => "survival",
            _ when string.Equals(tone, "Critical", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tone, "Warning", StringComparison.OrdinalIgnoreCase)
                => "combat",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(modeId)
            || string.Equals(modeId, activeFocusModeId, StringComparison.OrdinalIgnoreCase))
        {
            return new FocusModeSuggestion(false, string.Empty, string.Empty, string.Empty);
        }

        var definition = FocusModeLogic.Find(modeId);
        if (definition is null)
        {
            return new FocusModeSuggestion(false, string.Empty, string.Empty, string.Empty);
        }

        return new FocusModeSuggestion(
            true,
            definition.Id,
            definition.Label,
            $"Next Move suggests {definition.Label} focus — tap to apply, nothing changes automatically.");
    }
}
