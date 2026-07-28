namespace Isley;

internal enum FightCheckState
{
    Hidden,
    Hold,
    Verify,
    Manual,
    Waiting,
    Caution,
    Watch
}

internal readonly record struct FightCheckSnapshot(
    bool StreamerMode,
    bool LiveContactFeedAvailable,
    bool PositionFresh,
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
    int EncounterCount,
    double? EncounterDistance,
    string EncounterCardinal,
    string EncounterMotion,
    int EncounterMotionSampleCount,
    bool PackSpreadAlert,
    int PackFriendCount,
    double? PackSpread,
    string AbortCondition,
    bool ManualSightingActive = false,
    int ManualSightingUrgency = 0,
    string ManualSightingHeading = "",
    string ManualSightingDetail = "");

internal readonly record struct FightCheckView(
    FightCheckState State,
    string Badge,
    string Heading,
    string Detail,
    string ActionLabel,
    string ActionId,
    int Severity)
{
    internal bool IsVisible => State != FightCheckState.Hidden;
}

internal static class FightCheckLogic
{
    internal static FightCheckView Evaluate(FightCheckSnapshot raw)
    {
        if (raw.StreamerMode)
        {
            return View(FightCheckState.Hidden, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0);
        }

        var abort = Clean(raw.AbortCondition, "Break contact when the matchup stops favoring your current position.");
        var survival = Clean(raw.SurvivalLabel, "The active recovery condition");

        if (raw.SurvivalUrgency > 0)
        {
            return View(
                FightCheckState.Hold,
                "RECOVERY",
                "NO COMMIT · RECOVER FIRST",
                $"{survival} is still reported. Finish or explicitly clear recovery before considering contact. Abort cue: {abort}",
                "RECOVERY",
                "survival-assistant",
                3);
        }

        if (raw.HealthFresh && raw.Health is ReportedHealthState.Critical or ReportedHealthState.Hurt)
        {
            return View(
                FightCheckState.Hold,
                "HP",
                raw.Health == ReportedHealthState.Critical
                    ? "NO COMMIT · HP CRITICAL"
                    : "NO COMMIT · HP HURT",
                $"The fresh manual EKG report favors cover and disengagement, not another exchange. Abort cue: {abort}",
                "VITALS",
                "core-vitals",
                raw.Health == ReportedHealthState.Critical ? 3 : 2);
        }

        var empty = EmptyVital(raw);
        if (!string.IsNullOrEmpty(empty))
        {
            return View(
                FightCheckState.Hold,
                "EMPTY",
                $"NO COMMIT · {empty} EMPTY",
                $"Recover the reported {empty.ToLowerInvariant()} band before chasing, crossing, or trading. Abort cue: {abort}",
                "VITALS",
                "core-vitals",
                3);
        }

        if (raw.StaminaFresh && raw.Stamina == ReportedVitalState.Low)
        {
            return View(
                FightCheckState.Hold,
                "STAMINA",
                "HOLD · REGEN STAMINA",
                $"Low reported stamina reduces the room to attack, sprint, or disengage. Abort cue: {abort}",
                "VITALS",
                "core-vitals",
                2);
        }

        var coverage = VitalCoverage(raw);
        if (coverage < 4)
        {
            return View(
                FightCheckState.Verify,
                $"VITALS {coverage}/4",
                "VERIFY BEFORE CONTACT",
                $"Refresh the missing manual bands; Isley will not call a combat posture from stale or partial vitals. Abort cue: {abort}",
                "REPORT VITALS",
                "core-vitals",
                1);
        }

        var manualSightingApplies = raw.ManualSightingActive
                                    && (!raw.LiveContactFeedAvailable || raw.EncounterCount <= 0);
        if (manualSightingApplies)
        {
            var urgency = Math.Clamp(raw.ManualSightingUrgency, 1, 3);
            var heading = Clean(raw.ManualSightingHeading, urgency >= 3
                ? "CREATE SPACE"
                : urgency == 2 ? "HOLD AN EXIT" : "MONITOR THE CONTACT");
            var detail = Clean(
                raw.ManualSightingDetail,
                "A current player-reported sighting needs an in-game position check.");
            return urgency switch
            {
                >= 3 => View(
                    FightCheckState.Hold,
                    "REPORTED CLOSE",
                    $"NO CHASE · {heading}",
                    $"{detail} Abort cue: {abort}",
                    "UPDATE SIGHTING",
                    "sighting-check",
                    3),
                2 => View(
                    FightCheckState.Caution,
                    "REPORTED NEAR",
                    $"CAUTION · {heading}",
                    $"{detail} Abort cue: {abort}",
                    "UPDATE SIGHTING",
                    "sighting-check",
                    2),
                _ => View(
                    FightCheckState.Watch,
                    "REPORTED FAR",
                    heading,
                    $"{detail} Abort cue: {abort}",
                    "UPDATE SIGHTING",
                    "sighting-check",
                    1)
            };
        }

        if (!raw.LiveContactFeedAvailable)
        {
            return View(
                FightCheckState.Manual,
                "MANUAL",
                "CONTACTS UNAVAILABLE",
                $"This server session exposes no authorized live player feed. Vitals are fresh; verify opponents, terrain, and escape room in game. Abort cue: {abort}",
                "CURRENT GUIDE",
                "current-combat-guide",
                1);
        }

        if (!raw.PositionFresh)
        {
            return View(
                FightCheckState.Waiting,
                "POSITION",
                "WAITING · PLAYER MARKER",
                $"The live feed needs a fresh authorized self marker before it can measure contact range. Abort cue: {abort}",
                "RECENTER",
                "recenter",
                1);
        }

        var spread = NormalizeDistance(raw.PackSpread);
        if (raw.PackFriendCount >= 2 && (raw.PackSpreadAlert || spread is > 50))
        {
            return View(
                FightCheckState.Caution,
                "PACK SPLIT",
                "CAUTION · REGROUP FIRST",
                $"{raw.PackFriendCount} authorized friends span {(spread is null ? "an unknown distance" : $"{spread:0.0} MU")}. Restore mutual support before committing. Abort cue: {abort}",
                "PACK",
                "players",
                2);
        }

        var count = Math.Max(0, raw.EncounterCount);
        if (count == 0)
        {
            return View(
                FightCheckState.Watch,
                "NO LIVE CONTACT",
                "MONITOR · KEEP AN EXIT",
                $"No authorized non-friend marker is visible; that is not proof the area is clear. Abort cue: {abort}",
                "COMBAT FOCUS",
                "focus-combat",
                0);
        }

        var distance = NormalizeDistance(raw.EncounterDistance);
        if (distance is null)
        {
            return View(
                FightCheckState.Waiting,
                $"{count} LIVE",
                "VERIFY · CONTACT RANGE",
                $"Authorized contact is present, but range is not yet available. Keep visual confirmation and an exit. Abort cue: {abort}",
                "CONTACTS",
                "players",
                2);
        }

        var motionReady = raw.EncounterMotionSampleCount >= 3;
        var closing = motionReady && string.Equals(raw.EncounterMotion, "closing", StringComparison.OrdinalIgnoreCase);
        var cardinal = Clean(raw.EncounterCardinal, "nearby").ToUpperInvariant();
        var contact = $"{count} authorized contact{(count == 1 ? string.Empty : "s")} · nearest {distance:0.0} MU {cardinal}";
        if (distance <= 10 || distance <= 25 && closing)
        {
            return View(
                FightCheckState.Hold,
                closing ? "CLOSING" : "VERY CLOSE",
                "NO CHASE · EXIT READY",
                $"{contact}{(closing ? " and closing" : string.Empty)}. Preserve stamina and use the mapped escape heading only if you need it. Abort cue: {abort}",
                "PLAN ESCAPE",
                "escape-route",
                3);
        }

        if (distance <= 50 || closing)
        {
            return View(
                FightCheckState.Caution,
                closing ? "CLOSING" : "NEAR",
                "POSITION · DO NOT OVEREXTEND",
                $"{contact}{(closing ? " and closing" : string.Empty)}. Keep terrain, stamina, and the species abort cue visible: {abort}",
                "CONTACTS",
                "players",
                2);
        }

        return View(
            FightCheckState.Watch,
            "TRACKED",
            "POSITION · KEEP AN EXIT",
            $"{contact}. The current read supports awareness, not a predicted outcome. Abort cue: {abort}",
            "CONTACTS",
            "players",
            0);
    }

    internal static int VitalCoverage(FightCheckSnapshot raw) =>
        (raw.HealthFresh ? 1 : 0)
        + (raw.FoodFresh ? 1 : 0)
        + (raw.WaterFresh ? 1 : 0)
        + (raw.StaminaFresh ? 1 : 0);

    private static string EmptyVital(FightCheckSnapshot raw)
    {
        if (raw.StaminaFresh && raw.Stamina == ReportedVitalState.Empty) return "STAMINA";
        if (raw.WaterFresh && raw.Water == ReportedVitalState.Empty) return "WATER";
        return raw.FoodFresh && raw.Food == ReportedVitalState.Empty ? "FOOD" : string.Empty;
    }

    private static double? NormalizeDistance(double? value) =>
        value is not null && double.IsFinite(value.Value) && value.Value >= 0
            ? value.Value
            : null;

    private static string Clean(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static FightCheckView View(
        FightCheckState state,
        string badge,
        string heading,
        string detail,
        string actionLabel,
        string actionId,
        int severity) =>
        new(state, badge, heading, detail, actionLabel, actionId, Math.Clamp(severity, 0, 3));
}
