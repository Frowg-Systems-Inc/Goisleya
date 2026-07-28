using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var expectedIds = new HashSet<string>(
    ["balanced", "travel", "survival", "pack", "combat", "nest"],
    StringComparer.Ordinal);
var definitions = FocusModeLogic.Definitions;
Check(definitions.Count == expectedIds.Count, "exact six-mode catalog");
Check(definitions.Select(mode => mode.Id).ToHashSet(StringComparer.Ordinal).SetEquals(expectedIds),
    "mode identifiers complete and unique");

foreach (var mode in definitions)
{
    Check(mode.RangeRingModeIndex is >= 0 and <= 3, $"{mode.Id} range-ring index");
    Check(mode.LandmarkLabelDensityIndex is >= 0 and <= 2, $"{mode.Id} label-density index");
    Check(mode.TrailDurationIndex is >= 0 and <= 4, $"{mode.Id} trail index");
    Check(mode.ArrivalAlertIndex is >= 0 and <= 3, $"{mode.Id} arrival-alert index");
    Check(mode.DangerAlertIndex is >= 0 and <= 4, $"{mode.Id} danger-alert index");
    Check(mode.MarkerStyleIndex is >= 0 and <= 2, $"{mode.Id} marker-style index");
    Check(mode.HudDetailModeIndex is >= 0 and <= 2, $"{mode.Id} HUD-detail index");
    Check(mode.EncounterAlertIndex is >= 0 and <= 3, $"{mode.Id} encounter-alert index");
    Check(mode.EncounterMemoryIndex is >= 0 and <= 3, $"{mode.Id} encounter-memory index");

    var layers = FocusModeLogic.LayerState(mode.LayerProfile);
    Check(layers.Count == FocusModeLogic.LayerKeys.Count, $"{mode.Id} complete layer profile");
    Check(layers.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(FocusModeLogic.LayerKeys),
        $"{mode.Id} known layer keys only");
    Check(layers.Values.All(value => value is not null), $"{mode.Id} deterministic layer values");
}

var combat = FocusModeLogic.Find("combat")!;
Check(combat.MarkerStyleIndex == 1 && combat.HudDetailModeIndex == 1,
    "combat uses high-contrast essential presentation");
Check(combat.HeadingUp && combat.RangeRingModeIndex == 1 && !combat.BreadcrumbTrailVisible,
    "combat uses heading-up near-range low-clutter navigation");
Check(combat.EncounterHudVisible && combat.EncounterAlertIndex == 2 && combat.EncounterMemoryIndex == 1,
    "combat uses 25 MU alert and two-minute memory");
var combatLayers = FocusModeLogic.LayerState(combat.LayerProfile);
Check(combatLayers["heatmap"] is true && combatLayers["friendTrails"] is true,
    "combat retains authorized contact context");
Check(combatLayers.Where(pair => pair.Key is not "heatmap" and not "friendTrails")
    .All(pair => pair.Value is false), "combat removes ambient layer clutter");

var nest = FocusModeLogic.Find("nest")!;
Check(!nest.HeadingUp && nest.RangeRingModeIndex == 3 && nest.MapGridVisible,
    "nest uses stable wide perimeter view");
Check(nest.EncounterHudVisible && nest.EncounterAlertIndex == 3 && nest.DangerAlertIndex == 3,
    "nest uses 50 MU contact and danger awareness");
var nestLayers = FocusModeLogic.LayerState(nest.LayerProfile);
Check(nestLayers["locations"] is true && nestLayers["migration"] is true
      && nestLayers["patrol"] is true && nestLayers["food"] is true
      && nestLayers["heatmap"] is true && nestLayers["friendTrails"] is true,
    "nest provides site, resource, zone, and friend context");
Check(nestLayers["sanctuaries"] is false && nestLayers["selfTrail"] is false,
    "nest layer profile remains restrained");

Check(FocusModeLogic.Find("Combat") is null, "mode ids remain exact and predictable");
Check(FocusModeLogic.LayerState("unknown").Count == 0, "unknown profile fails closed");

Console.WriteLine("Focus modes: PASS (six profiles, bounded settings, restrained layers, combat awareness, and nest perimeter)");
