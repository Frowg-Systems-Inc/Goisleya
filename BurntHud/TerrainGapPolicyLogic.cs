namespace Isley;

internal readonly record struct TerrainGapPolicyOption(
    string Id,
    string Label,
    int MaximumConnectorDistance,
    string Description);

internal static class TerrainGapPolicyLogic
{
    internal const string StrictId = "strict";
    internal const string BalancedId = "balanced";
    internal const string FlexibleId = "flexible";

    internal static readonly TerrainGapPolicyOption[] Options =
    [
        new(
            StrictId,
            "STRICT",
            45,
            "Refuses endpoint gaps over 45 MU so the course stays close to mapped roads and trails."),
        new(
            BalancedId,
            "BALANCED",
            80,
            "Allows endpoint gaps up to 80 MU while refusing broad shortcuts across unknown terrain."),
        new(
            FlexibleId,
            "FLEXIBLE",
            125,
            "Allows endpoint gaps up to 125 MU when the destination is far from a mapped path.")
    ];

    internal static string Normalize(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return Options.Any(option => option.Id == normalized)
            ? normalized
            : BalancedId;
    }

    internal static TerrainGapPolicyOption Resolve(string? value)
    {
        var normalized = Normalize(value);
        return Options.First(option => option.Id == normalized);
    }

    internal static string Next(string? value)
    {
        var normalized = Normalize(value);
        var index = Array.FindIndex(Options, option => option.Id == normalized);
        return Options[(index + 1) % Options.Length].Id;
    }
}
