namespace Isley;

internal enum WaterCrossingState
{
    Hidden,
    Off,
    Measure,
    Hold,
    Caution,
    Verify,
    Ready
}

internal readonly record struct WaterCrossingSnapshot(
    bool StreamerMode,
    bool LiveMapAvailable,
    bool Active,
    bool MeasurementArmed,
    bool MeasurementHasStart,
    double? Distance,
    string SpeciesId,
    bool SpeciesKnown,
    int SurvivalUrgency,
    string SurvivalLabel,
    ReportedHealthState Health,
    bool HealthFresh,
    ReportedVitalState Stamina,
    bool StaminaFresh,
    FieldWeather Weather,
    bool WeatherFresh,
    double? EncounterDistance,
    string EncounterMotion,
    bool DangerActive,
    int MarkedBoundaryCount,
    bool InsideMarkedBoundary);

internal readonly record struct WaterCrossingView(
    WaterCrossingState State,
    string Key,
    string Heading,
    string Detail,
    string ActionLabel,
    string ActionId,
    string HudLabel,
    int Severity)
{
    internal bool IsVisible => State != WaterCrossingState.Hidden;
    internal bool HasMeasurement => State is WaterCrossingState.Hold
        or WaterCrossingState.Caution
        or WaterCrossingState.Verify
        or WaterCrossingState.Ready;
}

internal static class WaterCrossingLogic
{
    // These bands describe map exposure only. They are deliberately not swim-range claims.
    internal const double ShortExposureMaximumMu = 20;
    internal const double MediumExposureMaximumMu = 50;

    internal static WaterCrossingView Evaluate(WaterCrossingSnapshot raw)
    {
        if (raw.StreamerMode)
        {
            return View(WaterCrossingState.Hidden, "hidden", string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, 0);
        }

        if (!raw.Active)
        {
            return View(
                WaterCrossingState.Off,
                "off",
                "OFF · MARK BOTH BANKS",
                "Use the ruler as an entry-to-exit line. Isley checks exposure, fresh reports, authorized contacts, weather, and marked boundaries—not depth, current, oxygen, or hidden animals.",
                "START CHECK",
                "measure-crossing",
                "OFF",
                0);
        }

        if (!raw.LiveMapAvailable)
        {
            return View(
                WaterCrossingState.Measure,
                "map-waiting",
                "WAITING · LIVE MAP",
            "Water Crossing Check needs the calibrated Live Map ruler. Manual vitals and field reports remain available on other server profiles.",
                "OPEN MAP",
                "navigation",
                "MAP WAITING",
                1);
        }

        var distance = NormalizeDistance(raw.Distance);
        if (distance is null)
        {
            var hasStart = raw.MeasurementHasStart;
            return View(
                WaterCrossingState.Measure,
                hasStart ? "measure-exit" : "measure-entry",
                hasStart ? "MARK THE EXIT BANK" : "MARK THE ENTRY BANK",
                hasStart
                    ? "Select the intended exit point. The ruler line is treated as the exposed bank-to-bank span."
                    : "Select the intended entry point, then the exit bank. Choose the actual crossing—not a distant destination.",
                raw.MeasurementArmed ? "CANCEL" : "MEASURE BANKS",
                raw.MeasurementArmed ? "clear-crossing-check" : "measure-crossing",
                hasStart ? "SELECT EXIT" : "SELECT ENTRY",
                1);
        }

        var span = ExposureLabel(distance.Value);
        var distanceLabel = $"{distance:0.0} MU · {span} MAP EXPOSURE";
        var survivalLabel = CleanLabel(raw.SurvivalLabel, "active recovery issue");

        if (raw.SurvivalUrgency > 0)
        {
            return View(
                WaterCrossingState.Hold,
                "hold-recovery",
                "HOLD · RECOVERY ACTIVE",
                $"{distanceLabel}. {survivalLabel} is still reported; recover before entering exposed water.",
                "RECOVERY",
                "survival-assistant",
                "HOLD · RECOVERY",
                3);
        }

        if (raw.HealthFresh && raw.Health is ReportedHealthState.Critical or ReportedHealthState.Hurt)
        {
            return View(
                WaterCrossingState.Hold,
                raw.Health == ReportedHealthState.Critical ? "hold-hp-critical" : "hold-hp-hurt",
                raw.Health == ReportedHealthState.Critical ? "HOLD · HP CRITICAL" : "HOLD · HP HURT",
                $"{distanceLabel}. The fresh health report favors cover and recovery before a committed crossing.",
                "VITALS",
                "core-vitals",
                raw.Health == ReportedHealthState.Critical ? "HOLD · HP CRIT" : "HOLD · HP HURT",
                3);
        }

        if (raw.StaminaFresh && raw.Stamina is ReportedVitalState.Empty or ReportedVitalState.Low)
        {
            return View(
                WaterCrossingState.Hold,
                raw.Stamina == ReportedVitalState.Empty ? "hold-stamina-empty" : "hold-stamina-low",
                raw.Stamina == ReportedVitalState.Empty ? "HOLD · STAMINA EMPTY" : "HOLD · STAMINA LOW",
                $"{distanceLabel}. Restore the in-game stamina bar before entering; Isley does not predict species swim range.",
                "VITALS",
                "core-vitals",
                raw.Stamina == ReportedVitalState.Empty ? "HOLD · ST 0" : "HOLD · ST LOW",
                raw.Stamina == ReportedVitalState.Empty ? 3 : 2);
        }

        var encounterDistance = NormalizeDistance(raw.EncounterDistance);
        var closing = string.Equals(raw.EncounterMotion, "closing", StringComparison.OrdinalIgnoreCase);
        if (encounterDistance is <= 10 || (encounterDistance is <= 25 && closing))
        {
            return View(
                WaterCrossingState.Hold,
                "hold-contact",
                "HOLD · CONTACT AT THE BANK",
                $"{distanceLabel}. An authorized non-friend is {encounterDistance:0.0} MU away{(closing ? " and closing" : string.Empty)}; reassess before losing mobility in water.",
                "CONTACTS",
                "players",
                "HOLD · CONTACT",
                3);
        }

        if (raw.InsideMarkedBoundary)
        {
            return View(
                WaterCrossingState.Hold,
                "hold-inside-boundary",
                "HOLD · INSIDE MARKED AREA",
                $"{distanceLabel}. The entry point is inside a saved Danger or traced No-Go boundary; exit and verify the mark first.",
                "NO-GO AREAS",
                "no-go-areas",
                "HOLD · MARKED AREA",
                3);
        }

        var boundaryCount = Math.Clamp(raw.MarkedBoundaryCount, 0, 20);
        if (boundaryCount > 0)
        {
            return View(
                WaterCrossingState.Caution,
                $"boundary-{boundaryCount}",
                boundaryCount == 1 ? "CAUTION · MARKED CROSSING" : $"CAUTION · {boundaryCount} MARKED CROSSINGS",
                $"{distanceLabel}. The bank-to-bank line intersects {boundaryCount} saved warning {(boundaryCount == 1 ? "boundary" : "boundaries")}; inspect those marks before entering.",
                "NO-GO AREAS",
                "no-go-areas",
                "CAUTION · MARKED",
                2);
        }

        if (raw.DangerActive)
        {
            return View(
                WaterCrossingState.Caution,
                "danger-nearby",
                "CAUTION · DANGER NEARBY",
                $"{distanceLabel}. A configured local Danger warning is active; check it before committing to the waterline.",
                "DANGER",
                "alert-zones",
                "CAUTION · DANGER",
                2);
        }

        if (raw.WeatherFresh && raw.Weather is FieldWeather.Storm or FieldWeather.Fog)
        {
            var weather = FieldConditionsLogic.WeatherLabel(raw.Weather);
            return View(
                WaterCrossingState.Caution,
                $"weather-{weather.ToLowerInvariant()}",
                $"CAUTION · {weather}",
                $"{distanceLabel}. Player-reported {weather.ToLowerInvariant()} reduces sight or sound; shorten exposure and keep the entry bank as a retreat.",
                "FIELD",
                "field-conditions",
                $"CAUTION · {weather}",
                2);
        }

        if (!raw.HealthFresh || !raw.StaminaFresh)
        {
            var missing = !raw.HealthFresh && !raw.StaminaFresh
                ? "HP + STAMINA"
                : !raw.HealthFresh ? "HP" : "STAMINA";
            return View(
                WaterCrossingState.Verify,
                $"verify-{missing.ToLowerInvariant().Replace(' ', '-')}",
                $"VERIFY · {missing}",
                $"{distanceLabel}. Refresh the missing in-game band before treating this crossing check as current.",
                "VITALS",
                "core-vitals",
                $"VERIFY · {missing}",
                1);
        }

        if (!raw.SpeciesKnown)
        {
            return View(
                WaterCrossingState.Verify,
                "verify-species",
                "VERIFY · CURRENT SPECIES",
                $"{distanceLabel}. Select or sync the current dinosaur so Isley can distinguish terrestrial, aerial, and water-adapted guidance.",
                "SELECT SPECIES",
                "diet-coach",
                "VERIFY · SPECIES",
                1);
        }

        if (!raw.WeatherFresh)
        {
            return View(
                WaterCrossingState.Verify,
                "verify-field",
                "VERIFY · FIELD CONDITIONS",
                $"{distanceLabel}. Weather is unreported; check visibility and sound before committing.",
                "FIELD",
                "field-conditions",
                "VERIFY · FIELD",
                1);
        }

        var species = NormalizeSpecies(raw.SpeciesId);
        if (species == "pteranodon")
        {
            return View(
                WaterCrossingState.Ready,
                "ready-aerial",
                "READY · AERIAL LEG",
                $"{distanceLabel}. This is a flight span, not a swim estimate; circle the exit, preserve takeoff stamina, and verify the landing in game.",
                "DONE",
                "clear-crossing-check",
                "READY · AERIAL",
                0);
        }

        if (species is "deinosuchus" or "beipiaosaurus")
        {
            return View(
                WaterCrossingState.Ready,
                species == "deinosuchus" ? "ready-aquatic" : "ready-semi-aquatic",
                species == "deinosuchus" ? "READY · AQUATIC CHECK" : "READY · SEMI-AQUATIC CHECK",
                $"{distanceLabel}. Reports are current; verify depth, oxygen where relevant, both banks, larger aquatic threats, and a usable exit in game.",
                "DONE",
                "clear-crossing-check",
                species == "deinosuchus" ? "READY · AQUATIC" : "READY · SEMI-AQUATIC",
                0);
        }

        if (distance > MediumExposureMaximumMu)
        {
            return View(
                WaterCrossingState.Caution,
                "caution-long-span",
                "CAUTION · LONG WATER EXPOSURE",
                $"{distanceLabel}. This is not a swim-range prediction. Look for a shorter span, verify depth and both banks, and keep enough stamina to retreat.",
                "RESET BANKS",
                "measure-crossing",
                "CAUTION · LONG SPAN",
                2);
        }

        if (distance > ShortExposureMaximumMu)
        {
            return View(
                WaterCrossingState.Caution,
                "caution-medium-span",
                "CAUTION · EXPOSED WATER",
                $"{distanceLabel}. Reports are current, but depth, current, oxygen, hidden animals, and species swim range remain unknown; verify both banks in game.",
                "RESET BANKS",
                "measure-crossing",
                "CAUTION · EXPOSED",
                2);
        }

        return View(
            WaterCrossingState.Ready,
            "ready-short-span",
            "READY · SHORT MAP SPAN",
            $"{distanceLabel}. Current reports show no known blocker. Stop, listen, verify depth and both banks, and treat the in-game waterline as authoritative.",
            "DONE",
            "clear-crossing-check",
            "READY · VERIFY BANKS",
            0);
    }

    internal static string ExposureLabel(double distance) => NormalizeDistance(distance) switch
    {
        <= ShortExposureMaximumMu => "SHORT",
        <= MediumExposureMaximumMu => "MEDIUM",
        _ => "LONG"
    };

    private static double? NormalizeDistance(double? value) =>
        value is not null && double.IsFinite(value.Value) && value.Value >= 0
            ? value.Value
            : null;

    private static string NormalizeSpecies(string? value) => new((value ?? string.Empty)
        .Trim()
        .ToLowerInvariant()
        .Where(char.IsAsciiLetter)
        .Take(32)
        .ToArray());

    private static string CleanLabel(string? value, string fallback)
    {
        var clean = string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(clean)
            ? fallback
            : clean.Length <= 48 ? clean : clean[..48];
    }

    private static WaterCrossingView View(
        WaterCrossingState state,
        string key,
        string heading,
        string detail,
        string actionLabel,
        string actionId,
        string hudLabel,
        int severity) => new(
        state,
        key,
        heading,
        detail,
        actionLabel,
        actionId,
        hudLabel,
        Math.Clamp(severity, 0, 3));
}
