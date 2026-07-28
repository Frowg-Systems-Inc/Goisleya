namespace Isley;

public sealed record FocusModeDefinition(
    string Id,
    string Label,
    string Description,
    bool PlayerLabelsVisible,
    bool FriendOnly,
    bool HeadingUp,
    int RangeRingModeIndex,
    bool MapGridVisible,
    int LandmarkLabelDensityIndex,
    bool BreadcrumbTrailVisible,
    bool FriendRadarVisible,
    bool NearestPlaceVisible,
    int TrailDurationIndex,
    int ArrivalAlertIndex,
    int DangerAlertIndex,
    int MarkerStyleIndex,
    int HudDetailModeIndex,
    bool EncounterHudVisible,
    int EncounterAlertIndex,
    int EncounterMemoryIndex,
    string LayerProfile);

/// <summary>
/// Deterministic, reversible map presentation profiles. These profiles only
/// coordinate Isley display settings and bundled map layer controls.
/// </summary>
public static class FocusModeLogic
{
    public static IReadOnlyList<string> LayerKeys { get; } =
    [
        "locations", "sanctuaries", "migration", "patrol",
        "food", "heatmap", "selfTrail", "friendTrails"
    ];

    public static IReadOnlyList<FocusModeDefinition> Definitions { get; } =
    [
        new(
            "balanced", "Balanced", "essential context - calm north-up map",
            true, false, false, 0, false, 0, true, true, true,
            2, 2, 2, 0, 0, true, 2, 2, "navigation"),
        new(
            "travel", "Travel", "heading-up - grid and route awareness",
            false, false, true, 2, true, 1, true, true, true,
            2, 2, 2, 0, 1, true, 2, 1, "navigation"),
        new(
            "survival", "Survival", "food, zones, hazards, and longer trails",
            true, false, false, 3, true, 0, true, true, true,
            3, 3, 2, 0, 0, true, 2, 2, "survival"),
        new(
            "pack", "Pack", "friends-only - long trails and pack context",
            true, true, true, 2, false, 1, true, true, false,
            4, 2, 2, 1, 1, false, 0, 0, "pack"),
        new(
            "combat", "Combat", "high-contrast contacts - fast threat read",
            true, false, true, 1, false, 0, false, true, false,
            1, 1, 2, 1, 1, true, 2, 1, "combat"),
        new(
            "nest", "Nest", "wide perimeter - food, zones, and friend trails",
            true, false, false, 3, true, 1, true, true, true,
            2, 2, 3, 0, 0, true, 3, 2, "nest")
    ];

    public static FocusModeDefinition? Find(string? id) => Definitions.FirstOrDefault(
        definition => string.Equals(definition.Id, id, StringComparison.Ordinal));

    public static IReadOnlyDictionary<string, bool?> LayerState(string? profile) => profile switch
    {
        "navigation" => State(true, true, true, true, false, false, false, false),
        "survival" => State(true, true, true, true, true, true, false, false),
        "pack" => State(true, false, true, true, false, false, false, true),
        "combat" => State(false, false, false, false, false, true, false, true),
        "nest" => State(true, false, true, true, true, true, false, true),
        _ => new Dictionary<string, bool?>()
    };

    private static Dictionary<string, bool?> State(
        bool locations,
        bool sanctuaries,
        bool migration,
        bool patrol,
        bool food,
        bool heatmap,
        bool selfTrail,
        bool friendTrails) => new()
    {
        ["locations"] = locations,
        ["sanctuaries"] = sanctuaries,
        ["migration"] = migration,
        ["patrol"] = patrol,
        ["food"] = food,
        ["heatmap"] = heatmap,
        ["selfTrail"] = selfTrail,
        ["friendTrails"] = friendTrails
    };
}
