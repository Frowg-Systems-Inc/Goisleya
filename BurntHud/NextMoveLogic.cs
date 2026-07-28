namespace Isley;

internal enum NextMoveTone
{
    Neutral,
    Active,
    Warning,
    Critical
}

internal readonly record struct NextMoveSnapshot(
    bool StreamerMode,
    string SurvivalLabel,
    string SurvivalPriority,
    int SurvivalUrgency,
    double? EncounterDistance,
    string EncounterCardinal,
    string EncounterMotion,
    bool PackSpreadAlertActive,
    int PackFriendCount,
    double? PackSpread,
    bool WaypointActive,
    double? WaypointDistance,
    string WaypointTrend,
    int SoonestTimerSeconds,
    bool GrowthPaused,
    int GrowthPercent,
    bool PrimeConditionsReady,
    bool PrimeConfirmed,
    bool ElderConfirmed,
    bool NestActive,
    string NestPhase,
    string NestNextAction,
    bool LifeRunActive,
    string LifeRunNextObjective,
    bool LiveMapServicesActive,
    bool SelfAvailable,
    bool FieldConditionsWarning = false,
    string FieldConditionsHeading = "",
    string FieldConditionsDetail = "",
    int CoreVitalsUrgency = 0,
    string CoreVitalsHeading = "",
    string CoreVitalsDetail = "",
    bool ResourceTrendWarning = false,
    string ResourceTrendHeading = "",
    string ResourceTrendDetail = "",
    bool SpeciesMismatch = false,
    string LiveSpeciesName = "",
    bool LifeTransitionPending = false,
    string LifeTransitionHeading = "",
    string LifeTransitionDetail = "",
    bool GrowthGatePending = false,
    string GrowthGateHeading = "",
    string GrowthGateDetail = "",
    string GrowthGateActionId = "",
    string GrowthGateActionLabel = "",
    bool ApproachBriefActive = false,
    int ApproachBriefUrgency = 0,
    string ApproachBriefHeading = "",
    string ApproachBriefDetail = "",
    string ApproachBriefActionId = "",
    string ApproachBriefActionLabel = "",
    bool RestartWatchActive = false,
    int RestartWatchRemainingSeconds = 0,
    string RestartWatchHeading = "",
    string RestartWatchDetail = "",
    string RestartWatchActionId = "",
    string RestartWatchActionLabel = "",
    bool WaterCrossingActive = false,
    int WaterCrossingSeverity = 0,
    string WaterCrossingHeading = "",
    string WaterCrossingDetail = "",
    string WaterCrossingActionId = "",
    string WaterCrossingActionLabel = "",
    bool ShorelineCheckActive = false,
    int ShorelineCheckSeverity = 0,
    string ShorelineCheckHeading = "",
    string ShorelineCheckDetail = "",
    string ShorelineCheckActionId = "",
    string ShorelineCheckActionLabel = "",
    bool ManualSightingActive = false,
    int ManualSightingUrgency = 0,
    string ManualSightingHeading = "",
    string ManualSightingDetail = "");

internal readonly record struct NextMoveRecommendation(
    string Category,
    string Heading,
    string Detail,
    string ActionId,
    string ActionLabel,
    int Priority,
    NextMoveTone Tone)
{
    internal bool HasAction => !string.IsNullOrWhiteSpace(ActionId);
}

internal static class NextMoveLogic
{
    internal static NextMoveRecommendation Evaluate(NextMoveSnapshot raw)
    {
        var survivalLabel = Clean(raw.SurvivalLabel, "SURVIVAL ISSUE");
        var survivalPriority = Clean(raw.SurvivalPriority, "OPEN SURVIVAL RESPONSE");
        var encounterDistance = SafeDistance(raw.EncounterDistance);
        var packSpread = SafeDistance(raw.PackSpread);
        var waypointDistance = SafeDistance(raw.WaypointDistance);
        var encounterMotion = CleanToken(raw.EncounterMotion);
        var waypointTrend = CleanToken(raw.WaypointTrend);

        if (raw.StreamerMode)
        {
            return new NextMoveRecommendation(
                "HIDDEN",
                "NEXT MOVE HIDDEN",
                "Private live and Life Run context is redacted in Streamer Mode.",
                string.Empty,
                "HIDDEN",
                0,
                NextMoveTone.Neutral);
        }

        if (raw.SurvivalUrgency >= 3)
        {
            return Pick(
                "SURVIVAL",
                survivalPriority,
                $"{survivalLabel} is the highest-priority reported condition.",
                "survival-assistant",
                "OPEN SURVIVAL",
                1000,
                NextMoveTone.Critical);
        }

        if (raw.CoreVitalsUrgency >= 3)
        {
            return Pick(
                "VITALS",
                Clean(raw.CoreVitalsHeading, "CHECK CORE VITALS"),
                CleanSentence(raw.CoreVitalsDetail,
                    "A fresh vital report needs immediate attention."),
                "core-vitals",
                "OPEN VITALS",
                975,
                NextMoveTone.Critical);
        }

        var closeContact = encounterDistance is <= 10;
        var closingContact = encounterDistance is <= 25
                             && string.Equals(encounterMotion, "closing", StringComparison.Ordinal);
        if (raw.LiveMapServicesActive && (closeContact || closingContact))
        {
            var motion = closingContact ? " and closing" : string.Empty;
            var direction = CleanCardinal(raw.EncounterCardinal);
            var directionText = string.IsNullOrEmpty(direction) ? string.Empty : $" {direction}";
            var contactPicture = $"Authorized contact {encounterDistance:0.0} MU{directionText}{motion}";
            if (!raw.SelfAvailable)
            {
                return Pick(
                    "CONTACT",
                    "CREATE DISTANCE",
                    $"{contactPicture}; your live position is still calibrating.",
                    "players",
                    "OPEN CONTACTS",
                    950,
                    NextMoveTone.Critical);
            }

            return Pick(
                "CONTACT",
                "CREATE DISTANCE",
                $"{contactPicture}; plan a clear route away.",
                "escape-route",
                "PLAN ESCAPE",
                950,
                NextMoveTone.Critical);
        }

        if (raw.ManualSightingActive && raw.ManualSightingUrgency >= 3)
        {
            return Pick(
                "SIGHTING",
                Clean(raw.ManualSightingHeading, "CREATE SPACE"),
                CleanSentence(raw.ManualSightingDetail,
                    "A close player-reported sighting is current; preserve stamina and keep an exit."),
                "sighting-check",
                "UPDATE SIGHTING",
                940,
                NextMoveTone.Critical);
        }

        if (raw.RestartWatchActive && raw.RestartWatchRemainingSeconds <= 60)
        {
            return Pick(
                "RESTART",
                Clean(raw.RestartWatchHeading, "SAFE LOGOUT NOW"),
                CleanSentence(raw.RestartWatchDetail,
                    "The player-reported restart warning is in its final minute; verify it in game."),
                CleanToken(raw.RestartWatchActionId),
                Clean(raw.RestartWatchActionLabel, "START LOGOUT"),
                925,
                NextMoveTone.Critical);
        }

        if (raw.SurvivalUrgency > 0)
        {
            return Pick(
                "SURVIVAL",
                survivalPriority,
                $"{survivalLabel} is still active; keep the recovery steps visible.",
                "survival-assistant",
                "OPEN SURVIVAL",
                900,
                NextMoveTone.Warning);
        }

        if (raw.RestartWatchActive && raw.RestartWatchRemainingSeconds <= 120)
        {
            return Pick(
                "RESTART",
                Clean(raw.RestartWatchHeading, "PREPARE SAFE LOGOUT"),
                CleanSentence(raw.RestartWatchDetail,
                    "The player-reported restart warning is under two minutes; verify it in game."),
                CleanToken(raw.RestartWatchActionId),
                Clean(raw.RestartWatchActionLabel, "START LOGOUT"),
                890,
                NextMoveTone.Warning);
        }

        if (raw.CoreVitalsUrgency > 0)
        {
            return Pick(
                "VITALS",
                Clean(raw.CoreVitalsHeading, "CHECK CORE VITALS"),
                CleanSentence(raw.CoreVitalsDetail,
                    "A fresh vital report needs attention."),
                "core-vitals",
                "OPEN VITALS",
                875,
                NextMoveTone.Warning);
        }

        if (raw.ResourceTrendWarning)
        {
            return Pick(
                "RESOURCES",
                Clean(raw.ResourceTrendHeading, "RESOURCE TREND WARNING"),
                CleanSentence(raw.ResourceTrendDetail,
                    "Fresh resource samples are steadily approaching the low threshold."),
                "core-vitals",
                "OPEN VITALS",
                860,
                NextMoveTone.Warning);
        }

        if (raw.ShorelineCheckActive && raw.ShorelineCheckSeverity >= 2)
        {
            return Pick(
                "SHORELINE",
                Clean(raw.ShorelineCheckHeading, "CHECK THE WATERLINE"),
                CleanSentence(raw.ShorelineCheckDetail,
                    "The active drinking check has a current blocker or warning."),
                CleanToken(raw.ShorelineCheckActionId),
                Clean(raw.ShorelineCheckActionLabel, "OPEN CHECK"),
                859,
                raw.ShorelineCheckSeverity >= 3 ? NextMoveTone.Critical : NextMoveTone.Warning);
        }

        if (raw.WaterCrossingActive && raw.WaterCrossingSeverity >= 2)
        {
            return Pick(
                "CROSSING",
                Clean(raw.WaterCrossingHeading, "CHECK THE WATER CROSSING"),
                CleanSentence(raw.WaterCrossingDetail,
                    "The active bank-to-bank check has a current blocker or warning."),
                CleanToken(raw.WaterCrossingActionId),
                Clean(raw.WaterCrossingActionLabel, "OPEN CHECK"),
                858,
                raw.WaterCrossingSeverity >= 3 ? NextMoveTone.Critical : NextMoveTone.Warning);
        }

        if (raw.RestartWatchActive && raw.RestartWatchRemainingSeconds <= 300)
        {
            return Pick(
                "RESTART",
                Clean(raw.RestartWatchHeading, "FINISH AND FIND COVER"),
                CleanSentence(raw.RestartWatchDetail,
                    "The player-reported restart warning is under five minutes; verify it in game."),
                CleanToken(raw.RestartWatchActionId),
                Clean(raw.RestartWatchActionLabel, "OPEN LOGOUT"),
                855,
                NextMoveTone.Warning);
        }

        if (raw.PackSpreadAlertActive)
        {
            var friendCount = Math.Max(0, raw.PackFriendCount);
            var spread = packSpread is null ? "beyond the selected boundary" : $"across {packSpread:0.0} MU";
            var packSubject = friendCount > 0
                ? $"{friendCount} authorized friend{(friendCount == 1 ? string.Empty : "s")}"
                : "The authorized pack";
            return Pick(
                "PACK",
                "REGROUP THE PACK",
                $"{packSubject} {(friendCount <= 1 ? "is" : "are")} spread {spread}.",
                "players",
                "OPEN PACK",
                850,
                NextMoveTone.Warning);
        }

        if (raw.ManualSightingActive && raw.ManualSightingUrgency >= 2)
        {
            return Pick(
                "SIGHTING",
                Clean(raw.ManualSightingHeading, "HOLD AN EXIT"),
                CleanSentence(raw.ManualSightingDetail,
                    "A near player-reported sighting is current; keep terrain and an exit in view."),
                "sighting-check",
                "UPDATE SIGHTING",
                845,
                NextMoveTone.Warning);
        }

        if (raw.WaypointActive && string.Equals(waypointTrend, "away", StringComparison.Ordinal))
        {
            var distance = waypointDistance is null ? string.Empty : $" · {waypointDistance:0.0} MU";
            return Pick(
                "ROUTE",
                "CORRECT COURSE",
                $"The active destination is getting farther away{distance}.",
                "routes",
                "OPEN ROUTE",
                800,
                NextMoveTone.Warning);
        }

        if (raw.SoonestTimerSeconds is >= 0 and <= 60)
        {
            return Pick(
                "TIMER",
                "TIMER DUE SOON",
                $"The next active timer reaches zero in {FormatDuration(raw.SoonestTimerSeconds)}.",
                "timers",
                "OPEN TIMERS",
                750,
                NextMoveTone.Warning);
        }

        if (raw.RestartWatchActive)
        {
            return Pick(
                "RESTART",
                Clean(raw.RestartWatchHeading, "RESTART REPORTED"),
                CleanSentence(raw.RestartWatchDetail,
                    "A player-reported restart warning is active; watch for a newer in-game warning."),
                CleanToken(raw.RestartWatchActionId),
                Clean(raw.RestartWatchActionLabel, "OPEN WATCH"),
                745,
                NextMoveTone.Active);
        }

        if (raw.LifeRunActive && raw.LifeTransitionPending)
        {
            return Pick(
                "LIFE",
                Clean(raw.LifeTransitionHeading, "CHECK NEW DINOSAUR"),
                CleanSentence(raw.LifeTransitionDetail,
                    "The fresh live feed changed; choose what happened before changing this Life Run."),
                "life-run",
                "REVIEW LIFE",
                740,
                NextMoveTone.Warning);
        }

        if (raw.LifeRunActive && raw.GrowthGatePending)
        {
            return Pick(
                "GROWTH",
                Clean(raw.GrowthGateHeading, "GROWTH GATE REACHED"),
                CleanSentence(raw.GrowthGateDetail,
                    "A live lifecycle gate was crossed; verify it in game before changing saved state."),
                CleanToken(raw.GrowthGateActionId),
                Clean(raw.GrowthGateActionLabel, "OPEN GROWTH"),
                735,
                NextMoveTone.Warning);
        }

        if (raw.ApproachBriefActive && raw.ApproachBriefUrgency >= 2)
        {
            return Pick(
                "APPROACH",
                Clean(raw.ApproachBriefHeading, "CHECK THE APPROACH"),
                CleanSentence(raw.ApproachBriefDetail,
                    "The active destination needs a cautious in-game approach check."),
                CleanToken(raw.ApproachBriefActionId),
                Clean(raw.ApproachBriefActionLabel, "OPEN ROUTE"),
                730,
                NextMoveTone.Warning);
        }

        if (raw.ShorelineCheckActive)
        {
            return Pick(
                "SHORELINE",
                Clean(raw.ShorelineCheckHeading, "CHECK THE WATERLINE"),
                CleanSentence(raw.ShorelineCheckDetail,
                    "The 75-second shoreline snapshot is active; verify the bank in game."),
                CleanToken(raw.ShorelineCheckActionId),
                Clean(raw.ShorelineCheckActionLabel, "OPEN CHECK"),
                729,
                raw.ShorelineCheckSeverity > 0 ? NextMoveTone.Warning : NextMoveTone.Active);
        }

        if (raw.WaterCrossingActive)
        {
            return Pick(
                "CROSSING",
                Clean(raw.WaterCrossingHeading, "COMPLETE THE WATER CHECK"),
                CleanSentence(raw.WaterCrossingDetail,
                    "Mark both banks and verify the remaining waterline evidence in game."),
                CleanToken(raw.WaterCrossingActionId),
                Clean(raw.WaterCrossingActionLabel, "OPEN CHECK"),
                728,
                raw.WaterCrossingSeverity > 0 ? NextMoveTone.Warning : NextMoveTone.Active);
        }

        if (raw.FieldConditionsWarning)
        {
            return Pick(
                "FIELD",
                Clean(raw.FieldConditionsHeading, "CHECK FIELD CONDITIONS"),
                CleanSentence(raw.FieldConditionsDetail,
                    "A fresh player-reported field condition needs attention."),
                "field-conditions",
                "OPEN CONDITIONS",
                725,
                NextMoveTone.Warning);
        }

        if (raw.LifeRunActive && raw.SpeciesMismatch)
        {
            var speciesName = Clean(raw.LiveSpeciesName, "CURRENT DINOSAUR").ToUpperInvariant();
            return Pick(
                "PROFILE",
                "SYNC LIVE SPECIES",
                $"The fresh current dinosaur is {speciesName}; saved Life Run species guidance differs.",
                "diet-coach",
                "SYNC SPECIES",
                710,
                NextMoveTone.Warning);
        }

        if (raw.LifeRunActive && raw.GrowthPaused)
        {
            return Pick(
                "GROWTH",
                "RESTORE GROWTH",
                "The manual Growth Clock is paused; restore food and water before resuming it.",
                "growth-clock",
                "OPEN GROWTH",
                700,
                NextMoveTone.Warning);
        }

        if (raw.ApproachBriefActive)
        {
            return Pick(
                "APPROACH",
                Clean(raw.ApproachBriefHeading, "CHECK THE APPROACH"),
                CleanSentence(raw.ApproachBriefDetail,
                    "The active destination is inside its destination-specific approach radius."),
                CleanToken(raw.ApproachBriefActionId),
                Clean(raw.ApproachBriefActionLabel, "OPEN ROUTE"),
                690,
                NextMoveTone.Active);
        }

        if (raw.LifeRunActive
            && Math.Clamp(raw.GrowthPercent, 0, 100) >= 100
            && raw.PrimeConfirmed
            && !raw.ElderConfirmed)
        {
            return Pick(
                "ELDER",
                "VERIFY ELDER",
                "Growth reached 100%; confirm Elder and Entomb availability in game.",
                "elder-lineage",
                "OPEN ELDER",
                675,
                NextMoveTone.Warning);
        }

        if (raw.LifeRunActive
            && Math.Clamp(raw.GrowthPercent, 0, 100) >= 75
            && raw.PrimeConditionsReady
            && !raw.PrimeConfirmed)
        {
            return Pick(
                "PRIME",
                "VERIFY PRIME",
                "The plan and growth gate are ready; verify the fourth mutation slot in game.",
                "prime-planner",
                "OPEN PRIME",
                650,
                NextMoveTone.Warning);
        }

        if (raw.LifeRunActive && raw.NestActive)
        {
            var phase = Clean(raw.NestPhase, "NEST");
            var nextAction = CleanSentence(raw.NestNextAction, "Continue the current nest phase.");
            return Pick(
                "NEST",
                $"NEST · {phase}",
                nextAction,
                "nest-planner",
                "OPEN NEST",
                600,
                NextMoveTone.Active);
        }

        if (raw.WaypointActive)
        {
            if (waypointDistance is <= 20)
            {
                return Pick(
                    "ROUTE",
                    "ARRIVAL SOON",
                    $"The active destination is {waypointDistance:0.0} MU away; prepare to stop or advance.",
                    "routes",
                    "OPEN ROUTE",
                    550,
                    NextMoveTone.Active);
            }

            var distance = waypointDistance is null ? "Distance is waiting for your live marker." : $"{waypointDistance:0.0} MU remain.";
            return Pick(
                "ROUTE",
                "STAY ON ROUTE",
                distance,
                "routes",
                "OPEN ROUTE",
                500,
                NextMoveTone.Active);
        }

        if (raw.LifeRunActive)
        {
            var nextObjective = Clean(raw.LifeRunNextObjective, "REVIEW THE CURRENT LIFE");
            return Pick(
                "LIFE",
                nextObjective == "ALL TRACKED" ? "PROTECT THE LINEAGE" : nextObjective,
                nextObjective == "ALL TRACKED"
                    ? "The base milestones are logged; review Prime, Elder, mutations, or nesting next."
                    : "This is the next unlogged manual Life Run objective.",
                "life-run",
                "OPEN LIFE RUN",
                400,
                NextMoveTone.Active);
        }

        if (raw.LiveMapServicesActive && !raw.SelfAvailable)
        {
            return Pick(
                "RECOVERY",
                "PLAYER POSITION WAITING",
                "Follow will resume automatically; recovery tools retain only authorized last-position context.",
                "recovery",
                "OPEN RECOVERY",
                300,
                NextMoveTone.Neutral);
        }

        if (!raw.LifeRunActive)
        {
            return Pick(
                "LIFE",
                "START A LIFE RUN",
                "Create a private manual run to connect growth, diet, nesting, mutations, and Elder progress.",
                "life-run",
                "OPEN LIFE RUN",
                200,
                NextMoveTone.Neutral);
        }

        return Pick(
            "NAV",
            "STAY ORIENTED",
            "No urgent condition is active; keep follow, heading, and nearby-place context ready.",
            "navigation",
            "OPEN NAVIGATION",
            100,
            NextMoveTone.Neutral);
    }

    internal static string CompactSummary(NextMoveRecommendation recommendation) =>
        $"NEXT {Clean(recommendation.Category, "READY")}";

    private static NextMoveRecommendation Pick(
        string category,
        string heading,
        string detail,
        string actionId,
        string actionLabel,
        int priority,
        NextMoveTone tone) =>
        new(
            Clean(category, "READY"),
            Clean(heading, "REVIEW ISLEY"),
            CleanSentence(detail, "Review the current overlay state."),
            CleanToken(actionId),
            Clean(actionLabel, "OPEN"),
            Math.Max(0, priority),
            tone);

    private static double? SafeDistance(double? value) =>
        value is not null && double.IsFinite(value.Value) && value.Value >= 0
            ? value.Value
            : null;

    private static string CleanCardinal(string? value)
    {
        var normalized = CleanToken(value).ToUpperInvariant();
        return normalized is "N" or "NE" or "E" or "SE" or "S" or "SW" or "W" or "NW"
            ? normalized
            : string.Empty;
    }

    private static string CleanToken(string? value) =>
        new((value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(40)
            .ToArray());

    private static string Clean(string? value, string fallback)
    {
        var cleaned = string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim();
        return string.IsNullOrWhiteSpace(cleaned)
            ? fallback
            : cleaned.Length <= 72 ? cleaned : $"{cleaned[..71]}…";
    }

    private static string CleanSentence(string? value, string fallback)
    {
        var cleaned = Clean(value, fallback);
        return cleaned.Length <= 140 ? cleaned : $"{cleaned[..139]}…";
    }

    private static string FormatDuration(int seconds)
    {
        var safe = Math.Max(0, seconds);
        return safe < 60 ? $"{safe}s" : $"{safe / 60}:{safe % 60:00}";
    }
}
