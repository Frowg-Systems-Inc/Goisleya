namespace Isley;

internal enum TripReadinessState
{
    Hidden,
    Plan,
    Waiting,
    Hold,
    Caution,
    Verify,
    Ready
}

internal readonly record struct TripReadinessSnapshot(
    bool StreamerMode,
    bool LiveMapAvailable,
    bool HasDestination,
    bool PositionFresh,
    double? RemainingDistance,
    int SurvivalUrgency,
    string SurvivalLabel,
    ReportedHealthState Health,
    bool HealthFresh,
    ReportedVitalState Food,
    bool FoodFresh,
    ReportedVitalState Water,
    bool WaterFresh,
    ReportedVitalState Stamina,
    bool StaminaFresh,
    FieldWeather Weather,
    bool WeatherFresh,
    FieldLight Light,
    bool LightFresh,
    double? EncounterDistance,
    string EncounterMotion,
    bool DangerActive,
    bool InsideAlertZone,
    int RouteObstacleCount,
    bool InsideRouteObstacle,
    bool TerrainCourseReady,
    bool MovingAway,
    bool ResourceTrendWarning = false,
    string ResourceTrendHeading = "",
    string ResourceTrendDetail = "");

internal readonly record struct TripReadinessView(
    TripReadinessState State,
    string Heading,
    string Detail,
    string ActionLabel,
    string ActionId,
    int Severity)
{
    internal bool IsVisible => State != TripReadinessState.Hidden;
}

internal static class TripReadinessLogic
{
    internal static TripReadinessView Evaluate(TripReadinessSnapshot raw)
    {
        if (raw.StreamerMode || !raw.LiveMapAvailable)
        {
            return View(TripReadinessState.Hidden, string.Empty, string.Empty, string.Empty, string.Empty, 0);
        }

        if (!raw.HasDestination)
        {
            return View(
                TripReadinessState.Plan,
                "NO TRIP SET",
                "Choose a destination; Trip Check will combine current route, current vitals, resource trends, field reports, contacts, marked crossings, and local warnings.",
                "SET ROUTE",
                "navigation",
                0);
        }

        var distance = NormalizeDistance(raw.RemainingDistance);
        var distanceLabel = distance is null ? "Active trip" : $"{distance:0.0} MU remain";
        var survivalLabel = CleanLabel(raw.SurvivalLabel, "active recovery issue");
        var routeObstacleCount = Math.Clamp(raw.RouteObstacleCount, 0, 20);

        if (raw.SurvivalUrgency > 0)
        {
            return View(
                TripReadinessState.Hold,
                "HOLD · RECOVERY ACTIVE",
                $"{survivalLabel} is still reported. Resolve or explicitly clear it before committing to the trip.",
                "RECOVERY",
                "survival-assistant",
                3);
        }

        if (raw.HealthFresh && raw.Health is ReportedHealthState.Critical or ReportedHealthState.Hurt)
        {
            return View(
                TripReadinessState.Hold,
                raw.Health == ReportedHealthState.Critical ? "HOLD · HP CRITICAL" : "HOLD · HP HURT",
                $"{distanceLabel}. The fresh health report needs cover before travel.",
                "VITALS",
                "core-vitals",
                raw.Health == ReportedHealthState.Critical ? 3 : 2);
        }

        var emptyVital = EmptyVital(raw);
        if (!string.IsNullOrEmpty(emptyVital))
        {
            return View(
                TripReadinessState.Hold,
                $"HOLD · {emptyVital} EMPTY",
                $"{distanceLabel}. Refill or recover the reported band before exposed movement.",
                "VITALS",
                "core-vitals",
                3);
        }

        var encounterDistance = NormalizeDistance(raw.EncounterDistance);
        var encounterClosing = string.Equals(raw.EncounterMotion, "closing", StringComparison.OrdinalIgnoreCase);
        if (encounterDistance is <= 10 || (encounterDistance is <= 25 && encounterClosing))
        {
            var motion = encounterClosing ? " and closing" : string.Empty;
            return View(
                TripReadinessState.Hold,
                "HOLD · CONTACT RISK",
                $"Authorized non-friend {encounterDistance:0.0} MU away{motion}. Reassess or disengage before following the route.",
                "CONTACTS",
                "players",
                3);
        }

        if (!raw.PositionFresh || distance is null)
        {
            return View(
                TripReadinessState.Waiting,
                "WAITING · PLAYER POSITION",
                "The destination is active, but Trip Check needs a fresh authorized self marker for current distance and local risk context.",
                "RECENTER",
                "recenter",
                1);
        }

        if (raw.InsideAlertZone)
        {
            return View(
                TripReadinessState.Hold,
                "HOLD · ALERT ZONE",
                $"{distanceLabel}. You are inside a local warning boundary; verify the marked threat before departure.",
                "DANGER",
                "alert-zones",
                3);
        }

        if (raw.InsideRouteObstacle)
        {
            return View(
                TripReadinessState.Hold,
                "HOLD · INSIDE MARKED AREA",
                $"{distanceLabel}. You are inside a saved Danger or traced No-Go boundary; exit and verify the marked area before continuing.",
                "NO-GO AREAS",
                "no-go-areas",
                3);
        }

        if (raw.DangerActive)
        {
            return View(
                TripReadinessState.Caution,
                "CAUTION · DANGER NEARBY",
                $"{distanceLabel}. A configured local Danger warning is active; check the marker before committing.",
                "DANGER",
                "alert-zones",
                2);
        }

        if (routeObstacleCount > 0)
        {
            var boundaryLabel = routeObstacleCount == 1
                ? "1 marked boundary"
                : $"{routeObstacleCount} marked boundaries";
            return View(
                TripReadinessState.Caution,
                routeObstacleCount == 1
                    ? "CAUTION · MARKED CROSSING"
                    : $"CAUTION · {routeObstacleCount} MARKED CROSSINGS",
                $"{distanceLabel}. The current direct leg crosses {boundaryLabel}; verify the marks or use the road/trail planner around them.",
                raw.TerrainCourseReady ? "PLOT COURSE" : "ROUTE CHECK",
                raw.TerrainCourseReady ? "terrain-course" : "no-go-areas",
                2);
        }

        if (raw.MovingAway)
        {
            return View(
                TripReadinessState.Caution,
                "RECHECK · MOVING AWAY",
                $"{distanceLabel}. Accepted movement samples show the route opening rather than closing.",
                "RECENTER",
                "recenter",
                2);
        }

        if (raw.WeatherFresh && raw.Weather is FieldWeather.Storm or FieldWeather.Fog)
        {
            var weather = FieldConditionsLogic.WeatherLabel(raw.Weather);
            return View(
                TripReadinessState.Caution,
                $"CAUTION · {weather}",
                $"{distanceLabel}. Visibility or sound is player-reported as degraded; shorten exposed legs and keep a retreat.",
                "FIELD",
                "field-conditions",
                2);
        }

        var lowVital = LowVital(raw);
        if (!string.IsNullOrEmpty(lowVital))
        {
            return View(
                TripReadinessState.Caution,
                $"CAUTION · {lowVital} LOW",
                $"{distanceLabel}. The fresh report favors a shorter leg or recovery first.",
                "VITALS",
                "core-vitals",
                2);
        }

        if (raw.ResourceTrendWarning)
        {
            return View(
                TripReadinessState.Caution,
                CleanLabel(raw.ResourceTrendHeading, "CAUTION · RESOURCE TREND"),
                $"{distanceLabel}. {CleanLabel(raw.ResourceTrendDetail, "A fresh resource trend is approaching the low threshold.")}",
                "VITALS",
                "core-vitals",
                2);
        }

        var coverage = VitalCoverage(raw);
        if (coverage < 4)
        {
            return View(
                TripReadinessState.Verify,
                $"VERIFY · VITALS {coverage}/4",
                $"{distanceLabel}. Refresh the missing bands before treating the trip check as current.",
                "VITALS",
                "core-vitals",
                1);
        }

        var fieldNote = raw.WeatherFresh || raw.LightFresh
            ? "Fresh field context has no severe reported warning."
            : "Weather and light are unreported; verify them in game.";
        return View(
            TripReadinessState.Ready,
            "GO · CURRENT CHECK CLEAR",
            $"{distanceLabel}. All four vital bands are fresh and stable; no current authorized contact or local warning is active. {fieldNote} Verify terrain in game.",
            "MAP",
            "close-tools",
            0);
    }

    internal static int VitalCoverage(TripReadinessSnapshot raw) =>
        (raw.HealthFresh ? 1 : 0)
        + (raw.FoodFresh ? 1 : 0)
        + (raw.WaterFresh ? 1 : 0)
        + (raw.StaminaFresh ? 1 : 0);

    private static string EmptyVital(TripReadinessSnapshot raw)
    {
        if (raw.WaterFresh && raw.Water == ReportedVitalState.Empty) return "WATER";
        if (raw.FoodFresh && raw.Food == ReportedVitalState.Empty) return "FOOD";
        return raw.StaminaFresh && raw.Stamina == ReportedVitalState.Empty ? "STAMINA" : string.Empty;
    }

    private static string LowVital(TripReadinessSnapshot raw)
    {
        if (raw.StaminaFresh && raw.Stamina == ReportedVitalState.Low) return "STAMINA";
        if (raw.WaterFresh && raw.Water == ReportedVitalState.Low) return "WATER";
        return raw.FoodFresh && raw.Food == ReportedVitalState.Low ? "FOOD" : string.Empty;
    }

    private static double? NormalizeDistance(double? value) =>
        value is not null && double.IsFinite(value.Value) && value.Value >= 0
            ? value.Value
            : null;

    private static string CleanLabel(string? value, string fallback)
    {
        var clean = string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(clean)
            ? fallback
            : clean.Length <= 48 ? clean : clean[..48];
    }

    private static TripReadinessView View(
        TripReadinessState state,
        string heading,
        string detail,
        string actionLabel,
        string actionId,
        int severity) =>
        new(state, heading, detail, actionLabel, actionId, Math.Clamp(severity, 0, 3));
}
