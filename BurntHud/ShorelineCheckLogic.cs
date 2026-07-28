namespace Isley;

internal enum ShorelineCheckState
{
    Hidden,
    Off,
    Urgent,
    Hold,
    Caution,
    Verify,
    Window
}

internal readonly record struct ShorelineCheckSnapshot(
    bool StreamerMode,
    bool Active,
    DateTimeOffset StartedAt,
    DateTimeOffset Now,
    bool LiveContactFeedAvailable,
    bool PositionFresh,
    int SurvivalUrgency,
    string SurvivalId,
    string SurvivalLabel,
    ReportedHealthState Health,
    bool HealthFresh,
    ReportedVitalState Water,
    bool WaterFresh,
    ReportedVitalState Stamina,
    bool StaminaFresh,
    int EncounterCount,
    double? EncounterDistance,
    string EncounterCardinal,
    string EncounterMotion,
    int EncounterMotionSampleCount,
    bool DangerWarning,
    bool InsideAlertZone,
    FieldWeather Weather,
    bool WeatherFresh,
    string SpeciesId,
    bool SpeciesKnown);

internal readonly record struct ShorelineCheckView(
    ShorelineCheckState State,
    string Badge,
    string Heading,
    string Detail,
    string ActionLabel,
    string ActionId,
    int Severity,
    int RemainingSeconds)
{
    internal bool IsVisible => State != ShorelineCheckState.Hidden;
    internal bool IsCurrent => State is not ShorelineCheckState.Hidden and not ShorelineCheckState.Off;
}

internal static class ShorelineCheckLogic
{
    internal const int ActiveSeconds = 75;

    internal static int RemainingSeconds(DateTimeOffset startedAt, DateTimeOffset now)
    {
        var elapsed = Math.Max(0, (int)Math.Floor((now - startedAt).TotalSeconds));
        return Math.Clamp(ActiveSeconds - elapsed, 0, ActiveSeconds);
    }

    internal static ShorelineCheckView Evaluate(ShorelineCheckSnapshot raw)
    {
        if (raw.StreamerMode)
        {
            return View(ShorelineCheckState.Hidden, string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty, 0, 0);
        }

        if (!raw.Active)
        {
            return View(
                ShorelineCheckState.Off,
                "MANUAL",
                "CHECK THE WATERLINE",
                "Run this when you are at a shoreline. Isley uses reported vitals and authorized context; it does not detect hidden animals or prove the water is safe.",
                string.Empty,
                string.Empty,
                0,
                0);
        }

        var remaining = RemainingSeconds(raw.StartedAt, raw.Now);
        if (remaining <= 0)
        {
            return View(
                ShorelineCheckState.Off,
                "EXPIRED",
                "CHECK EXPIRED · SCAN AGAIN",
                "The 75-second shoreline snapshot is old. Run it again before using the result.",
                "RUN AGAIN",
                "shoreline-check",
                1,
                0);
        }

        var speciesCue = SpeciesCue(raw.SpeciesId, raw.SpeciesKnown);
        var contactDistance = NormalizeDistance(raw.EncounterDistance);
        var contactCount = Math.Max(0, raw.EncounterCount);
        var contactDirection = Clean(raw.EncounterCardinal, "nearby").ToUpperInvariant();
        var motionReady = raw.EncounterMotionSampleCount >= 3;
        var closing = motionReady
                      && string.Equals(raw.EncounterMotion, "closing", StringComparison.OrdinalIgnoreCase);

        if (raw.WaterFresh && raw.Water == ReportedVitalState.Empty)
        {
            var uncertainty = !raw.LiveContactFeedAvailable
                ? " Live contacts are unavailable on this session."
                : !raw.PositionFresh
                    ? " Your authorized position is not fresh enough for a contact-range check."
                    : contactDistance is not null && contactCount > 0
                        ? $" Nearest authorized contact: {contactDistance:0.0} MU {contactDirection}{(closing ? ", closing" : string.Empty)}."
                        : " No authorized contact is reported, which is not proof the bank is clear.";
            return View(
                ShorelineCheckState.Urgent,
                "WATER EMPTY",
                "DRINK NOW · MINIMIZE EXPOSURE",
                $"Hydration is the immediate reported need. Use the nearest verified water, spend as little time exposed as possible, and keep an exit.{uncertainty} {speciesCue}",
                "OPEN VITALS",
                "core-vitals",
                3,
                remaining);
        }

        var survivalId = CleanToken(raw.SurvivalId);
        if (raw.SurvivalUrgency > 0 && survivalId != "dehydrated")
        {
            return View(
                ShorelineCheckState.Hold,
                "RECOVERY",
                "HOLD · RECOVER OFF THE BANK",
                $"{Clean(raw.SurvivalLabel, "The active recovery condition")} is still reported. Keep the condition guidance visible and avoid adding shoreline exposure unless the in-game water state becomes urgent. {speciesCue}",
                "OPEN RECOVERY",
                "survival-assistant",
                3,
                remaining);
        }

        if (raw.HealthFresh && raw.Health is ReportedHealthState.Critical or ReportedHealthState.Hurt)
        {
            return View(
                ShorelineCheckState.Hold,
                "HP",
                raw.Health == ReportedHealthState.Critical
                    ? "HOLD · HP CRITICAL"
                    : "HOLD · HP HURT",
                $"The fresh health report favors cover before a stationary drink. Recheck water in game and expose only if hydration becomes the higher immediate risk. {speciesCue}",
                "OPEN VITALS",
                "core-vitals",
                raw.Health == ReportedHealthState.Critical ? 3 : 2,
                remaining);
        }

        if (raw.StaminaFresh && raw.Stamina is ReportedVitalState.Empty or ReportedVitalState.Low)
        {
            return View(
                ShorelineCheckState.Hold,
                "STAMINA",
                raw.Stamina == ReportedVitalState.Empty
                    ? "HOLD · STAMINA EMPTY"
                    : "HOLD · REGEN STAMINA",
                $"Recover enough in-game stamina to leave the bank immediately after drinking. {speciesCue}",
                "OPEN VITALS",
                "core-vitals",
                raw.Stamina == ReportedVitalState.Empty ? 3 : 2,
                remaining);
        }

        if (!raw.LiveContactFeedAvailable)
        {
            return View(
                ShorelineCheckState.Verify,
                "MANUAL CONTACTS",
                "VERIFY · SCAN BOTH BANKS",
                $"This server session exposes no authorized live contact feed. Listen, scent, check both directions and keep an exit; Isley cannot rule out hidden animals. {speciesCue}",
                "OPEN FIELD GUIDE",
                "field-guide",
                1,
                remaining);
        }

        if (!raw.PositionFresh)
        {
            return View(
                ShorelineCheckState.Verify,
                "POSITION",
                "VERIFY · PLAYER MARKER WAITING",
                $"The authorized self marker is not fresh enough for shoreline range or boundary context. Keep scanning in game. {speciesCue}",
                "RECENTER",
                "recenter",
                1,
                remaining);
        }

        if (raw.InsideAlertZone || raw.DangerWarning)
        {
            return View(
                ShorelineCheckState.Hold,
                raw.InsideAlertZone ? "INSIDE WARNING" : "DANGER NEAR",
                "BACK OFF · REASSESS THE BANK",
                $"{(raw.InsideAlertZone ? "You are inside a saved warning boundary." : "A saved Danger or alert-zone warning is active near this waterline.")} Create room before committing to a stationary drink. {speciesCue}",
                "PLAN ESCAPE",
                "escape-route",
                3,
                remaining);
        }

        if (contactCount > 0 && contactDistance is null)
        {
            return View(
                ShorelineCheckState.Verify,
                "CONTACT RANGE",
                "VERIFY · LIVE CONTACT RANGE",
                $"An authorized non-friend is visible, but range is not ready. Keep the shoreline uncommitted until the position settles or you verify the contact in game. {speciesCue}",
                "OPEN CONTACTS",
                "players",
                2,
                remaining);
        }

        if (contactCount > 0 && contactDistance is not null
            && (contactDistance <= 12 || contactDistance <= 30 && closing))
        {
            return View(
                ShorelineCheckState.Hold,
                closing ? "CONTACT CLOSING" : "CONTACT CLOSE",
                "BACK OFF · KEEP THE EXIT",
                $"{contactCount} authorized contact{(contactCount == 1 ? string.Empty : "s")}; nearest {contactDistance:0.0} MU {contactDirection}{(closing ? " and closing" : string.Empty)}. Do not become stationary at the bank. {speciesCue}",
                "PLAN ESCAPE",
                "escape-route",
                3,
                remaining);
        }

        if (contactCount > 0 && contactDistance is <= 50)
        {
            return View(
                ShorelineCheckState.Caution,
                "CONTACT NEAR",
                "CAUTION · SHORT DRINK ONLY",
                $"Nearest authorized contact is {contactDistance:0.0} MU {contactDirection}. Keep the drink brief, retain visual confirmation and leave before the contact closes. {speciesCue}",
                "OPEN CONTACTS",
                "players",
                2,
                remaining);
        }

        if (raw.WaterFresh && raw.Water == ReportedVitalState.Low)
        {
            return View(
                ShorelineCheckState.Caution,
                "WATER LOW",
                "DRINK · KEEP THE EXIT OPEN",
                $"The fresh water report favors topping off before travel. Limit shoreline time and leave with stamina in reserve. {speciesCue}",
                "OPEN VITALS",
                "core-vitals",
                2,
                remaining);
        }

        if (raw.WeatherFresh && raw.Weather is FieldWeather.Storm or FieldWeather.Fog)
        {
            return View(
                ShorelineCheckState.Caution,
                raw.Weather == FieldWeather.Storm ? "STORM" : "FOG",
                "CAUTION · VISIBILITY REDUCED",
                $"The fresh player report is {raw.Weather.ToString().ToUpperInvariant()}. Shorten exposure and trust in-game sight, sound and scent over this snapshot. {speciesCue}",
                "OPEN CONDITIONS",
                "field-conditions",
                2,
                remaining);
        }

        if (!raw.WaterFresh)
        {
            return View(
                ShorelineCheckState.Verify,
                "WATER UNKNOWN",
                "VERIFY · REPORT WATER BAND",
                $"Isley does not have a fresh water report, so it cannot weigh hydration urgency against exposure. {speciesCue}",
                "REPORT WATER",
                "core-vitals",
                1,
                remaining);
        }

        return View(
            ShorelineCheckState.Window,
            contactCount > 0 ? "CONTACT TRACKED" : "NO LIVE CONTACT",
            "NO REPORTED BLOCKER · VERIFY IN GAME",
            $"No current Isley input blocks a brief drink. That is not a safety guarantee: hidden animals, water quality, depth and cover are not detected. {speciesCue}",
            "END CHECK",
            "shoreline-check-clear",
            0,
            remaining);
    }

    internal static string BriefLabel(ShorelineCheckView view) =>
        view.IsCurrent
            ? $"SHORELINE {view.State.ToString().ToUpperInvariant()}"
            : string.Empty;

    internal static string SpeciesCue(string? speciesId, bool speciesKnown)
    {
        if (!speciesKnown)
        {
            return "Stop short, listen and scent, scan both banks, and keep a clear exit.";
        }

        var species = CleanToken(speciesId);
        if (species.Contains("pteranodon", StringComparison.Ordinal))
        {
            return "Circle first; land with immediate takeoff room and enough stamina to leave.";
        }

        if (species.Contains("deinosuchus", StringComparison.Ordinal)
            || species.Contains("beipiaosaurus", StringComparison.Ordinal))
        {
            return "Check both banks, depth and a clear exit; do not assume another aquatic is absent.";
        }

        return "Stop short, listen and scent, scan both banks, then drink from an angle with a clear exit.";
    }

    private static double? NormalizeDistance(double? value) =>
        value is not null && double.IsFinite(value.Value) && value.Value >= 0
            ? value.Value
            : null;

    private static string Clean(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string CleanToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Trim().ToLowerInvariant()
                .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
                .Take(80)
                .ToArray());

    private static ShorelineCheckView View(
        ShorelineCheckState state,
        string badge,
        string heading,
        string detail,
        string actionLabel,
        string actionId,
        int severity,
        int remainingSeconds) =>
        new(
            state,
            badge,
            heading,
            detail,
            actionLabel,
            actionId,
            Math.Clamp(severity, 0, 3),
            Math.Clamp(remainingSeconds, 0, ActiveSeconds));
}
