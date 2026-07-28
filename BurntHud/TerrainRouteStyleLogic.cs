namespace Isley;

internal readonly record struct TerrainRouteStyleOption(
    string Id,
    string Label,
    string Description);

internal static class TerrainRouteStyleLogic
{
    internal const string BalancedId = "balanced";
    internal const string RoadFirstId = "road-first";
    internal const string ShortestId = "shortest";

    internal static readonly TerrainRouteStyleOption[] Options =
    [
        new(
            BalancedId,
            "BALANCED",
            "Balances mapped roads and trails while applying a modest cost to off-network connectors."),
        new(
            RoadFirstId,
            "ROAD-FIRST",
            "Prefers mapped roads, penalizes trails, and strongly limits off-network connectors."),
        new(
            ShortestId,
            "SHORTEST",
            "Minimizes valid course length without relaxing water, Danger-zone, or No-Go constraints.")
    ];

    internal static string Normalize(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return Options.Any(option => option.Id == normalized)
            ? normalized
            : BalancedId;
    }

    internal static TerrainRouteStyleOption Resolve(string? value)
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
