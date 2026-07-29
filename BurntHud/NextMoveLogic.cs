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
    /// <summary>
    /// The single highest-priority recommendation — identical to the historical
    /// first-match cascade, now expressed as the top of the deterministic
    /// stacked-guidance ranking (<see cref="EvaluateStacked"/>).
    /// </summary>
    internal static NextMoveRecommendation Evaluate(NextMoveSnapshot raw) =>
        EvaluateStacked(raw).Top;

    /// <summary>
    /// The stacked view: every active condition-driven recommendation ranked by
    /// the declared priority ladder, bounded by <paramref name="maxShown"/>,
    /// with an honest "+N more" overflow for whatever the slot cannot show.
    /// </summary>
    internal static StackedGuidanceView EvaluateStacked(NextMoveSnapshot raw, int maxShown = 1) =>
        StackedGuidanceLogic.Rank(CollectCandidates(raw), maxShown);

    /// <summary>
    /// Collects every competing recommendation for the current snapshot. The
    /// branch order and priorities below are the historical cascade ladder
    /// (safety &gt; vitals-critical &gt; timers &gt; planners &gt; informational),
    /// so the ranked top is always the same recommendation the cascade returned.
    /// The ambient tail (route following, Life Run objective, position waiting,
    /// start-a-run, stay-oriented) is default content, not competition: it only
    /// produces a candidate when no condition-driven branch fired, exactly one,
    /// in the original cascade order.
    /// </summary>
    internal static IReadOnlyList<NextMoveRecommendation> CollectCandidates(NextMoveSnapshot raw)
    {
        var survivalLabel = Clean(raw.SurvivalLabel, "SURVIVAL ISSUE");
        var survivalPriority = Clean(raw.SurvivalPriority, "OPEN SURVIVAL RESPONSE");
        var encounterDistance = SafeDistance(raw.EncounterDistance);
        var packSpread = SafeDistance(raw.PackSpread);
        var waypointDistance = SafeDistance(raw.WaypointDistance);
        var encounterMotion = CleanToken(raw.EncounterMotion);
        var waypointTrend = CleanToken(raw.WaypointTrend);
        var candidates = new List<NextMoveRecommendation>();

        if (raw.StreamerMode)
        {
            candidates.Add(new NextMoveRecommendation(
                "HIDDEN",
                "NEXT MOVE HIDDEN",
                "Private live and Life Run context is redacted in Streamer Mode.",
                string.Empty,
                "HIDDEN",
                0,
                NextMoveTone.Neutral));
            return candidates;
        }

        if (raw.SurvivalUrgency >= 3)
        {
            candidates.Add(Pick(
                "SURVIVAL",
                survivalPriority,
                $"{survivalLabel} is the highest-priority reported condition.",
                "survival-assistant",
                "OPEN SURVIVAL",
                1000,
                NextMoveTone.Critical));
        }

        if (raw.CoreVitalsUrgency >= 3)
        {
            candidates.Add(Pick(
                "VITALS",
                Clean(raw.CoreVitalsHeading, "CHECK CORE VITALS"),
                CleanSentence(raw.CoreVitalsDetail,
                    "A fresh vital report needs immediate attention."),
                "core-vitals",
                "OPEN VITALS",
                975,
                NextMoveTone.Critical));
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
                candidates.Add(Pick(
                    "CONTACT",
                    "CREATE DISTANCE",
                    $"{contactPicture}; your live position is still calibrating.",
                    "players",
                    "OPEN CONTACTS",
                    950,
                    NextMoveTone.Critical));
            }
            else
            {
                candidates.Add(Pick(
                    "CONTACT",
                    "CREATE DISTANCE",
                    $"{contactPicture}; plan a clear route away.",
                    "escape-route",
                    "PLAN ESCAPE",
                    950,
                    NextMoveTone.Critical));
            }
        }

        if (raw.ManualSightingActive && raw.ManualSightingUrgency >= 3)
        {
            candidates.Add(Pick(
                "SIGHTING",
                Clean(raw.ManualSightingHeading, "CREATE SPACE"),
                CleanSentence(raw.ManualSightingDetail,
                    "A close player-reported sighting is current; preserve stamina and keep an exit."),
                "sighting-check",
                "UPDATE SIGHTING",
                940,
                NextMoveTone.Critical));
        }

        if (raw.RestartWatchActive && raw.RestartWatchRemainingSeconds <= 60)
        {
            candidates.Add(Pick(
                "RESTART",
                Clean(raw.RestartWatchHeading, "SAFE LOGOUT NOW"),
                CleanSentence(raw.RestartWatchDetail,
                    "The player-reported restart warning is in its final minute; verify it in game."),
                CleanToken(raw.RestartWatchActionId),
                Clean(raw.RestartWatchActionLabel, "START LOGOUT"),
                925,
                NextMoveTone.Critical));
        }

        if (raw.SurvivalUrgency > 0)
        {
            candidates.Add(Pick(
                "SURVIVAL",
                survivalPriority,
                $"{survivalLabel} is still active; keep the recovery steps visible.",
                "survival-assistant",
                "OPEN SURVIVAL",
                900,
                NextMoveTone.Warning));
        }

        if (raw.RestartWatchActive && raw.RestartWatchRemainingSeconds <= 120)
        {
            candidates.Add(Pick(
                "RESTART",
                Clean(raw.RestartWatchHeading, "PREPARE SAFE LOGOUT"),
                CleanSentence(raw.RestartWatchDetail,
                    "The player-reported restart warning is under two minutes; verify it in game."),
                CleanToken(raw.RestartWatchActionId),
                Clean(raw.RestartWatchActionLabel, "START LOGOUT"),
                890,
                NextMoveTone.Warning));
        }

        if (raw.CoreVitalsUrgency > 0)
        {
            candidates.Add(Pick(
                "VITALS",
                Clean(raw.CoreVitalsHeading, "CHECK CORE VITALS"),
                CleanSentence(raw.CoreVitalsDetail,
                    "A fresh vital report needs attention."),
                "core-vitals",
                "OPEN VITALS",
                875,
                NextMoveTone.Warning));
        }

        if (raw.ResourceTrendWarning)
        {
            candidates.Add(Pick(
                "RESOURCES",
                Clean(raw.ResourceTrendHeading, "RESOURCE TREND WARNING"),
                CleanSentence(raw.ResourceTrendDetail,
                    "Fresh resource samples are steadily approaching the low threshold."),
                "core-vitals",
                "OPEN VITALS",
                860,
                NextMoveTone.Warning));
        }

        if (raw.ShorelineCheckActive && raw.ShorelineCheckSeverity >= 2)
        {
            candidates.Add(Pick(
                "SHORELINE",
                Clean(raw.ShorelineCheckHeading, "CHECK THE WATERLINE"),
                CleanSentence(raw.ShorelineCheckDetail,
                    "The active drinking check has a current blocker or warning."),
                CleanToken(raw.ShorelineCheckActionId),
                Clean(raw.ShorelineCheckActionLabel, "OPEN CHECK"),
                859,
                raw.ShorelineCheckSeverity >= 3 ? NextMoveTone.Critical : NextMoveTone.Warning));
        }

        if (raw.WaterCrossingActive && raw.WaterCrossingSeverity >= 2)
        {
            candidates.Add(Pick(
                "CROSSING",
                Clean(raw.WaterCrossingHeading, "CHECK THE WATER CROSSING"),
                CleanSentence(raw.WaterCrossingDetail,
                    "The active bank-to-bank check has a current blocker or warning."),
                CleanToken(raw.WaterCrossingActionId),
                Clean(raw.WaterCrossingActionLabel, "OPEN CHECK"),
                858,
                raw.WaterCrossingSeverity >= 3 ? NextMoveTone.Critical : NextMoveTone.Warning));
        }

        if (raw.RestartWatchActive && raw.RestartWatchRemainingSeconds <= 300)
        {
            candidates.Add(Pick(
                "RESTART",
                Clean(raw.RestartWatchHeading, "FINISH AND FIND COVER"),
                CleanSentence(raw.RestartWatchDetail,
                    "The player-reported restart warning is under five minutes; verify it in game."),
                CleanToken(raw.RestartWatchActionId),
                Clean(raw.RestartWatchActionLabel, "OPEN LOGOUT"),
                855,
                NextMoveTone.Warning));
        }

        if (raw.PackSpreadAlertActive)
        {
            var friendCount = Math.Max(0, raw.PackFriendCount);
            var spread = packSpread is null ? "beyond the selected boundary" : $"across {packSpread:0.0} MU";
            var packSubject = friendCount > 0
                ? $"{friendCount} authorized friend{(friendCount == 1 ? string.Empty : "s")}"
                : "The authorized pack";
            candidates.Add(Pick(
                "PACK",
                "REGROUP THE PACK",
                $"{packSubject} {(friendCount <= 1 ? "is" : "are")} spread {spread}.",
                "players",
                "OPEN PACK",
                850,
                NextMoveTone.Warning));
        }

        if (raw.ManualSightingActive && raw.ManualSightingUrgency >= 2)
        {
            candidates.Add(Pick(
                "SIGHTING",
                Clean(raw.ManualSightingHeading, "HOLD AN EXIT"),
                CleanSentence(raw.ManualSightingDetail,
                    "A near player-reported sighting is current; keep terrain and an exit in view."),
                "sighting-check",
                "UPDATE SIGHTING",
                845,
                NextMoveTone.Warning));
        }

        if (raw.WaypointActive && string.Equals(waypointTrend, "away", StringComparison.Ordinal))
        {
            var distance = waypointDistance is null ? string.Empty : $" · {waypointDistance:0.0} MU";
            candidates.Add(Pick(
                "ROUTE",
                "CORRECT COURSE",
                $"The active destination is getting farther away{distance}.",
                "routes",
                "OPEN ROUTE",
                800,
                NextMoveTone.Warning));
        }

        if (raw.SoonestTimerSeconds is >= 0 and <= 60)
        {
            candidates.Add(Pick(
                "TIMER",
                "TIMER DUE SOON",
                $"The next active timer reaches zero in {FormatDuration(raw.SoonestTimerSeconds)}.",
                "timers",
                "OPEN TIMERS",
                750,
                NextMoveTone.Warning));
        }

        if (raw.RestartWatchActive)
        {
            candidates.Add(Pick(
                "RESTART",
                Clean(raw.RestartWatchHeading, "RESTART REPORTED"),
                CleanSentence(raw.RestartWatchDetail,
                    "A player-reported restart warning is active; watch for a newer in-game warning."),
                CleanToken(raw.RestartWatchActionId),
                Clean(raw.RestartWatchActionLabel, "OPEN WATCH"),
                745,
                NextMoveTone.Active));
        }

        if (raw.LifeRunActive && raw.LifeTransitionPending)
        {
            candidates.Add(Pick(
                "LIFE",
                Clean(raw.LifeTransitionHeading, "CHECK NEW DINOSAUR"),
                CleanSentence(raw.LifeTransitionDetail,
                    "The fresh live feed changed; choose what happened before changing this Life Run."),
                "life-run",
                "REVIEW LIFE",
                740,
                NextMoveTone.Warning));
        }

        if (raw.LifeRunActive && raw.GrowthGatePending)
        {
            candidates.Add(Pick(
                "GROWTH",
                Clean(raw.GrowthGateHeading, "GROWTH GATE REACHED"),
                CleanSentence(raw.GrowthGateDetail,
                    "A live lifecycle gate was crossed; verify it in game before changing saved state."),
                CleanToken(raw.GrowthGateActionId),
                Clean(raw.GrowthGateActionLabel, "OPEN GROWTH"),
                735,
                NextMoveTone.Warning));
        }

        if (raw.ApproachBriefActive && raw.ApproachBriefUrgency >= 2)
        {
            candidates.Add(Pick(
                "APPROACH",
                Clean(raw.ApproachBriefHeading, "CHECK THE APPROACH"),
                CleanSentence(raw.ApproachBriefDetail,
                    "The active destination needs a cautious in-game approach check."),
                CleanToken(raw.ApproachBriefActionId),
                Clean(raw.ApproachBriefActionLabel, "OPEN ROUTE"),
                730,
                NextMoveTone.Warning));
        }

        if (raw.ShorelineCheckActive)
        {
            candidates.Add(Pick(
                "SHORELINE",
                Clean(raw.ShorelineCheckHeading, "CHECK THE WATERLINE"),
                CleanSentence(raw.ShorelineCheckDetail,
                    "The 75-second shoreline snapshot is active; verify the bank in game."),
                CleanToken(raw.ShorelineCheckActionId),
                Clean(raw.ShorelineCheckActionLabel, "OPEN CHECK"),
                729,
                raw.ShorelineCheckSeverity > 0 ? NextMoveTone.Warning : NextMoveTone.Active));
        }

        if (raw.WaterCrossingActive)
        {
            candidates.Add(Pick(
                "CROSSING",
                Clean(raw.WaterCrossingHeading, "COMPLETE THE WATER CHECK"),
                CleanSentence(raw.WaterCrossingDetail,
                    "Mark both banks and verify the remaining waterline evidence in game."),
                CleanToken(raw.WaterCrossingActionId),
                Clean(raw.WaterCrossingActionLabel, "OPEN CHECK"),
                728,
                raw.WaterCrossingSeverity > 0 ? NextMoveTone.Warning : NextMoveTone.Active));
        }

        if (raw.FieldConditionsWarning)
        {
            candidates.Add(Pick(
                "FIELD",
                Clean(raw.FieldConditionsHeading, "CHECK FIELD CONDITIONS"),
                CleanSentence(raw.FieldConditionsDetail,
                    "A fresh player-reported field condition needs attention."),
                "field-conditions",
                "OPEN CONDITIONS",
                725,
                NextMoveTone.Warning));
        }

        if (raw.LifeRunActive && raw.SpeciesMismatch)
        {
            var speciesName = Clean(raw.LiveSpeciesName, "CURRENT DINOSAUR").ToUpperInvariant();
            candidates.Add(Pick(
                "PROFILE",
                "SYNC LIVE SPECIES",
                $"The fresh current dinosaur is {speciesName}; saved Life Run species guidance differs.",
                "diet-coach",
                "SYNC SPECIES",
                710,
                NextMoveTone.Warning));
        }

        if (raw.LifeRunActive && raw.GrowthPaused)
        {
            candidates.Add(Pick(
                "GROWTH",
                "RESTORE GROWTH",
                "The manual Growth Clock is paused; restore food and water before resuming it.",
                "growth-clock",
                "OPEN GROWTH",
                700,
                NextMoveTone.Warning));
        }

        if (raw.ApproachBriefActive)
        {
            candidates.Add(Pick(
                "APPROACH",
                Clean(raw.ApproachBriefHeading, "CHECK THE APPROACH"),
                CleanSentence(raw.ApproachBriefDetail,
                    "The active destination is inside its destination-specific approach radius."),
                CleanToken(raw.ApproachBriefActionId),
                Clean(raw.ApproachBriefActionLabel, "OPEN ROUTE"),
                690,
                NextMoveTone.Active));
        }

        if (raw.LifeRunActive
            && Math.Clamp(raw.GrowthPercent, 0, 100) >= 100
            && raw.PrimeConfirmed
            && !raw.ElderConfirmed)
        {
            candidates.Add(Pick(
                "ELDER",
                "VERIFY ELDER",
                "Growth reached 100%; confirm Elder and Entomb availability in game.",
                "elder-lineage",
                "OPEN ELDER",
                675,
                NextMoveTone.Warning));
        }

        if (raw.LifeRunActive
            && Math.Clamp(raw.GrowthPercent, 0, 100) >= 75
            && raw.PrimeConditionsReady
            && !raw.PrimeConfirmed)
        {
            candidates.Add(Pick(
                "PRIME",
                "VERIFY PRIME",
                "The plan and growth gate are ready; verify the fourth mutation slot in game.",
                "prime-planner",
                "OPEN PRIME",
                650,
                NextMoveTone.Warning));
        }

        if (raw.LifeRunActive && raw.NestActive)
        {
            var phase = Clean(raw.NestPhase, "NEST");
            var nextAction = CleanSentence(raw.NestNextAction, "Continue the current nest phase.");
            candidates.Add(Pick(
                "NEST",
                $"NEST · {phase}",
                nextAction,
                "nest-planner",
                "OPEN NEST",
                600,
                NextMoveTone.Active));
        }

        if (raw.WaypointActive && waypointDistance is <= 20)
        {
            candidates.Add(Pick(
                "ROUTE",
                "ARRIVAL SOON",
                $"The active destination is {waypointDistance:0.0} MU away; prepare to stop or advance.",
                "routes",
                "OPEN ROUTE",
                550,
                NextMoveTone.Active));
        }

        if (candidates.Count > 0)
        {
            return candidates;
        }

        // Ambient fallback — default content, never counted as competition.
        // First match wins, in the original cascade order.
        if (raw.WaypointActive)
        {
            var distance = waypointDistance is null ? "Distance is waiting for your live marker." : $"{waypointDistance:0.0} MU remain.";
            candidates.Add(Pick(
                "ROUTE",
                "STAY ON ROUTE",
                distance,
                "routes",
                "OPEN ROUTE",
                500,
                NextMoveTone.Active));
        }
        else if (raw.LifeRunActive)
        {
            var nextObjective = Clean(raw.LifeRunNextObjective, "REVIEW THE CURRENT LIFE");
            candidates.Add(Pick(
                "LIFE",
                nextObjective == "ALL TRACKED" ? "PROTECT THE LINEAGE" : nextObjective,
                nextObjective == "ALL TRACKED"
                    ? "The base milestones are logged; review Prime, Elder, mutations, or nesting next."
                    : "This is the next unlogged manual Life Run objective.",
                "life-run",
                "OPEN LIFE RUN",
                400,
                NextMoveTone.Active));
        }
        else if (raw.LiveMapServicesActive && !raw.SelfAvailable)
        {
            candidates.Add(Pick(
                "RECOVERY",
                "PLAYER POSITION WAITING",
                "Follow will resume automatically; recovery tools retain only authorized last-position context.",
                "recovery",
                "OPEN RECOVERY",
                300,
                NextMoveTone.Neutral));
        }
        else if (!raw.LifeRunActive)
        {
            candidates.Add(Pick(
                "LIFE",
                "START A LIFE RUN",
                "Create a private manual run to connect growth, diet, nesting, mutations, and Elder progress.",
                "life-run",
                "OPEN LIFE RUN",
                200,
                NextMoveTone.Neutral));
        }
        else
        {
            candidates.Add(Pick(
                "NAV",
                "STAY ORIENTED",
                "No urgent condition is active; keep follow, heading, and nearby-place context ready.",
                "navigation",
                "OPEN NAVIGATION",
                100,
                NextMoveTone.Neutral));
        }

        return candidates;
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
