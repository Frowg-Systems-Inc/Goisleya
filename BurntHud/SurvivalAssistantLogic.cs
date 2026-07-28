namespace Isley;

internal readonly record struct SurvivalIncidentDefinition(
    string Id,
    string Label,
    string ShortLabel,
    int Urgency,
    string Priority,
    string[] Steps,
    int ExpectedSeconds,
    string Note);

internal enum RecoveryRemedyKind
{
    SavedPin,
    ResourceFinder,
    FoodLayer,
    DietCoach
}

internal readonly record struct RecoveryRemedy(
    RecoveryRemedyKind Kind,
    string Target,
    string ActionLabel,
    string Tooltip);

internal readonly record struct SurvivalHudPresentation(
    bool IsCollapsed,
    bool ShowDetails,
    string ToggleLabel,
    string ToggleTooltip);

internal readonly record struct VomitRecoveryClock(
    DateTimeOffset StartedAt,
    int AdditionalSeconds,
    bool Restarted);

internal enum SurvivalQuickActionKind
{
    StartVomit,
    ReportAdditionalVomit,
    OpenActiveIncident
}

internal readonly record struct SurvivalQuickAction(
    SurvivalQuickActionKind Kind,
    string Label,
    string Tooltip,
    string AutomationName);

internal readonly record struct SurvivalIncidentPresentation(
    string Priority,
    string[] Steps,
    string HudSteps,
    bool StopEatingWarningActive,
    bool RequiresGameCheck);

internal enum ReportedHealthState
{
    Unknown,
    Stable,
    Hurt,
    Critical
}

internal static class SurvivalAssistantLogic
{
    internal const string MechanicsSnapshot = "2026-07-23";
    internal const int VomitStackSeconds = 300;
    internal const int MaximumAdditionalVomitSeconds = 3600;

    internal static readonly SurvivalIncidentDefinition[] Incidents =
    [
        new("bleeding", "Bleeding", "BLEED", 3, "STOP SPRINTING",
            ["Break sightline and reach cover.", "Stand still, then rest / lie down.", "Keep stamina, food, and water stable; wallow if safely available."],
            0, "Severity varies. Movement makes blood loss worse."),
        new("fracture", "Fracture", "FRACTURE", 2, "HIDE AND REST",
            ["Avoid falls, fights, and forced movement.", "Rest / lie down in cover.", "Maintain a strong diet while locked health recovers."],
            600, "Community estimate: roughly 5-10 minutes; patches can change this."),
        new("wounded", "Wounded / low health", "WOUNDED", 2, "DISENGAGE",
            ["Break contact and reach concealment.", "Rest until the Status Report improves.", "Do not re-engage while damage output may be reduced."],
            0, "The in-game EKG is the reliable health signal; red is critical."),
        new("venom", "Venom", "VENOM", 3, "BREAK CONTACT",
            ["Disengage immediately.", "Conserve stamina and avoid a second attack.", "Stay in cover and outlast the staged damage."],
            60, "Guide reports about 45 seconds; the one-minute timer adds safety margin."),
        new("blindness", "Blindness / spit", "BLIND", 2, "CLEAR THE EFFECT",
            ["Hold your bound Clear / Buck control.", "Stay near cover while vision returns.", "Do not chase into unknown terrain."],
            0, "Controls are rebindable; use the Clear / Buck action shown in your settings."),
        new("bacteria", "Bacterial sickness", "BACTERIA", 2, "FIND SALT",
            ["Use a salt lick if one is safely available.", "Avoid more Ceratosaurus bites.", "Rest in cover while the effect decays."],
            0, "Salt may clear the effect; natural decay timing can vary."),
        new("vomit", "Vomit sickness", "SICK", 2, "STOP EATING · GET COVER",
            ["Stop eating or swallowing food; more can trigger another vomit.", "Walk or trot to nearby cover, then avoid sprinting and combat.", "Use a salt lick if safe; it cures the effect but drains the active nutrient. Tap +5 after every extra vomit."],
            300, "Working estimate: five minutes. Each additional vomit can extend recovery; salt can cure the effect faster but drains the active nutrient. The in-game warning remains authoritative."),
        new("food-poisoning", "Rotten-food poisoning", "POISON", 2, "STOP EATING ROTTEN FOOD",
            ["Stop eating rotten meat or bones immediately.", "Conserve water and stay concealed; more food can cost health.", "Wait for the warning to clear before rebuilding nutrients."],
            420, "Community estimate: roughly 6-7 minutes. Salt may not clear this variant."),
        new("long-sickness", "Cannibal / mushroom sickness", "SICKNESS", 2, "WAIT IT OUT",
            ["Hide and avoid unnecessary movement.", "Conserve food, water, and stamina.", "Avoid combat until the effect clears."],
            1200, "Community guides report 15-20 minutes and no known direct cure."),
        new("dehydrated", "Dehydrated", "THIRST", 3, "FIND WATER",
            ["Stop sprinting and conserve stamina.", "Route to a saved clean water marker.", "Approach exposed water carefully."],
            0, "Saved routes are personal and stay on this PC."),
        new("starving", "Starving", "HUNGER", 3, "FIND FOOD",
            ["Enable the live Food layer.", "Route to a saved food marker if available.", "Avoid a costly fight unless necessary."],
            0, "Food availability still depends on species, migration, and current server rules.")
    ];

    private static readonly IReadOnlyDictionary<string, string[]> CompactHudSteps =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["bleeding"] =
                ["Break sight and reach cover.", "Stand still, then lie down.", "Keep vitals steady; wallow if safe."],
            ["fracture"] =
                ["Avoid falls, fights, and forced moves.", "Lie down in cover.", "Keep a strong diet while health unlocks."],
            ["wounded"] =
                ["Break contact and hide.", "Rest until the Status Report improves.", "Do not re-engage while weakened."],
            ["venom"] =
                ["Disengage now.", "Save stamina; avoid a second hit.", "Hide and wait out the staged damage."],
            ["blindness"] =
                ["Hold your Clear / Buck control.", "Stay by cover while vision returns.", "Do not chase into unknown terrain."],
            ["bacteria"] =
                ["Use salt if safe.", "Avoid more Ceratosaurus bites.", "Rest in cover while the effect decays."],
            ["vomit"] =
                ["Stop eating; more can trigger vomit.", "Walk to cover; do not sprint or fight.", "Salt lick cures but drains nutrient; tap +5 after vomit."],
            ["food-poisoning"] =
                ["Stop eating rotten meat or bones.", "Hide and conserve water.", "Wait for the warning to clear."],
            ["long-sickness"] =
                ["Hide and limit movement.", "Conserve food, water, and stamina.", "Avoid combat until the effect clears."],
            ["dehydrated"] =
                ["Stop sprinting; save stamina.", "Route to clean water.", "Check for threats before drinking."],
            ["starving"] =
                ["Enable the Food layer.", "Route to saved food if available.", "Avoid an expensive fight."]
        };

    private static readonly IReadOnlyDictionary<string, string[]> ExpiredStopEatingSteps =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["vomit"] =
                ["Check the vomit-sickness warning shown by the game.", "If it cleared, mark the in-game warning cleared.", "If it remains, add five minutes and avoid food until it clears."],
            ["food-poisoning"] =
                ["Check the poisoning warning shown by the game.", "If it cleared, mark the in-game warning cleared.", "If it remains, restart the estimate and avoid rotten food."]
        };

    private static readonly IReadOnlyDictionary<string, string[]> ExpiredStopEatingHudSteps =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["vomit"] =
                ["Check the game's sickness warning.", "Clear Isley if the warning is gone.", "If still sick, tap +5 and avoid food."],
            ["food-poisoning"] =
                ["Check the game's poisoning warning.", "Clear Isley if the warning is gone.", "If still sick, restart; avoid rotten food."]
        };

    internal static string NormalizeIncidentId(string? id) =>
        Incidents.Any(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ? id!.ToLowerInvariant()
            : string.Empty;

    internal static SurvivalIncidentDefinition? Find(string? id)
    {
        var normalized = NormalizeIncidentId(id);
        return string.IsNullOrEmpty(normalized)
            ? null
            : Incidents.First(item => item.Id == normalized);
    }

    internal static int RemainingSeconds(
        SurvivalIncidentDefinition incident,
        DateTimeOffset startedAt,
        DateTimeOffset now,
        int additionalSeconds = 0)
    {
        if (incident.ExpectedSeconds <= 0) return 0;
        var elapsed = Math.Max(0, (int)Math.Floor((now - startedAt).TotalSeconds));
        var total = TotalExpectedSeconds(incident, additionalSeconds);
        return Math.Clamp(total - elapsed, 0, total);
    }

    internal static int NormalizeAdditionalSeconds(
        SurvivalIncidentDefinition incident,
        int additionalSeconds) =>
        incident.Id == "vomit"
            ? Math.Clamp(additionalSeconds, 0, MaximumAdditionalVomitSeconds)
            : 0;

    internal static int TotalExpectedSeconds(
        SurvivalIncidentDefinition incident,
        int additionalSeconds = 0) =>
        incident.ExpectedSeconds <= 0
            ? 0
            : incident.ExpectedSeconds + NormalizeAdditionalSeconds(incident, additionalSeconds);

    internal static int AddVomitStack(int additionalSeconds) =>
        Math.Min(
            MaximumAdditionalVomitSeconds,
            Math.Clamp(additionalSeconds, 0, MaximumAdditionalVomitSeconds) + VomitStackSeconds);

    internal static VomitRecoveryClock ReportAdditionalVomit(
        DateTimeOffset startedAt,
        int additionalSeconds,
        DateTimeOffset now)
    {
        var vomit = Find("vomit")!.Value;
        if (RemainingSeconds(vomit, startedAt, now, additionalSeconds) <= 0)
        {
            return new VomitRecoveryClock(now, 0, Restarted: true);
        }

        return new VomitRecoveryClock(
            startedAt,
            AddVomitStack(additionalSeconds),
            Restarted: false);
    }

    internal static double RemainingRatio(
        SurvivalIncidentDefinition incident,
        DateTimeOffset startedAt,
        DateTimeOffset now,
        int additionalSeconds = 0)
    {
        var total = TotalExpectedSeconds(incident, additionalSeconds);
        return total <= 0
            ? 0
            : Math.Clamp(RemainingSeconds(incident, startedAt, now, additionalSeconds) / (double)total, 0, 1);
    }

    internal static bool IsFinalMinute(int remainingSeconds) =>
        remainingSeconds is > 0 and <= 60;

    internal static bool IsStopEatingIncident(SurvivalIncidentDefinition incident) =>
        incident.Id is "vomit" or "food-poisoning";

    internal static SurvivalIncidentPresentation Presentation(
        SurvivalIncidentDefinition incident,
        DateTimeOffset startedAt,
        DateTimeOffset now,
        int additionalSeconds = 0)
    {
        var activeStopEatingWarning = IsStopEatingIncident(incident)
                                      && RemainingSeconds(
                                          incident,
                                          startedAt,
                                          now,
                                          additionalSeconds) > 0;
        if (IsStopEatingIncident(incident)
            && !activeStopEatingWarning
            && ExpiredStopEatingSteps.TryGetValue(incident.Id, out var expiredSteps)
            && ExpiredStopEatingHudSteps.TryGetValue(incident.Id, out var expiredHudSteps))
        {
            return new SurvivalIncidentPresentation(
                "CHECK IN-GAME WARNING",
                expiredSteps,
                FormatHudSteps(expiredHudSteps),
                StopEatingWarningActive: false,
                RequiresGameCheck: true);
        }

        var hudSteps = CompactHudSteps.TryGetValue(incident.Id, out var compact)
            ? compact
            : incident.Steps;
        return new SurvivalIncidentPresentation(
            incident.Priority,
            incident.Steps,
            FormatHudSteps(hudSteps),
            activeStopEatingWarning,
            RequiresGameCheck: false);
    }

    internal static bool ShouldRestoreIncident(
        SurvivalIncidentDefinition incident,
        DateTimeOffset startedAt,
        DateTimeOffset now,
        int additionalSeconds = 0) =>
        !IsStopEatingIncident(incident)
        || RemainingSeconds(incident, startedAt, now, additionalSeconds) > 0;

    internal static string FormatRemaining(int seconds)
    {
        var safeSeconds = Math.Max(0, seconds);
        return safeSeconds >= 3600
            ? $"{safeSeconds / 3600}:{safeSeconds % 3600 / 60:00}:{safeSeconds % 60:00}"
            : $"{safeSeconds / 60}:{safeSeconds % 60:00}";
    }

    internal static string CompactSummary(
        SurvivalIncidentDefinition incident,
        DateTimeOffset startedAt,
        DateTimeOffset now,
        int additionalSeconds = 0)
    {
        var remaining = RemainingSeconds(incident, startedAt, now, additionalSeconds);
        var presentation = Presentation(incident, startedAt, now, additionalSeconds);
        var timer = incident.ExpectedSeconds <= 0
            ? string.Empty
            : remaining > 0
                ? $" · {FormatRemaining(remaining)} EST"
                : presentation.RequiresGameCheck
                    ? " · CHECK GAME"
                    : " · 0:00 EST";
        return $"STATUS {incident.ShortLabel} · {presentation.Priority}{timer}";
    }

    internal static string FooterLabel(
        SurvivalIncidentDefinition? incident,
        DateTimeOffset startedAt,
        DateTimeOffset now,
        int additionalSeconds = 0)
    {
        if (incident is not { } active) return "VOMIT WARNING? START 5M";
        if (active.ExpectedSeconds <= 0) return active.ShortLabel;
        var remaining = RemainingSeconds(active, startedAt, now, additionalSeconds);
        return remaining > 0
            ? $"{active.ShortLabel} {FormatRemaining(remaining)}"
            : $"{active.ShortLabel} CHECK";
    }

    internal static SurvivalQuickAction QuickAction(
        SurvivalIncidentDefinition? incident,
        DateTimeOffset startedAt,
        DateTimeOffset now,
        int additionalSeconds = 0)
    {
        if (incident is not { } active)
        {
            return new SurvivalQuickAction(
                SurvivalQuickActionKind.StartVomit,
                "VOMIT WARNING? START 5M",
                "Use only while the game shows Vomit sickness; start the five-minute working estimate and pin recovery guidance",
                "Report the in-game Vomit sickness warning and start five-minute recovery guidance");
        }

        if (active.Id == "vomit")
        {
            var remaining = RemainingSeconds(active, startedAt, now, additionalSeconds);
            return remaining > 0
                ? new SurvivalQuickAction(
                    SurvivalQuickActionKind.ReportAdditionalVomit,
                    $"SICK {FormatRemaining(remaining)} · +5M",
                    "Report another vomit and add five minutes to the active recovery estimate",
                    "Report another vomit and add five minutes")
                : new SurvivalQuickAction(
                    SurvivalQuickActionKind.ReportAdditionalVomit,
                    "WARNING STILL ON? · +5M",
                    "Use only if the game's Vomit sickness warning remains; restart the five-minute recovery estimate",
                    "Confirm the in-game Vomit sickness warning remains and restart five-minute recovery guidance");
        }

        return new SurvivalQuickAction(
            SurvivalQuickActionKind.OpenActiveIncident,
            FooterLabel(active, startedAt, now, additionalSeconds),
            $"Open the active {active.Label} recovery instructions",
            $"Open {active.Label} recovery instructions");
    }

    internal static string HudSteps(SurvivalIncidentDefinition incident) =>
        Presentation(
            incident,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch).HudSteps;

    internal static string HudSteps(
        SurvivalIncidentDefinition incident,
        DateTimeOffset startedAt,
        DateTimeOffset now,
        int additionalSeconds = 0) =>
        Presentation(incident, startedAt, now, additionalSeconds).HudSteps;

    private static string FormatHudSteps(IEnumerable<string> steps) =>
        string.Join("\n", steps.Select((step, index) => $"{index + 1}  {step}"));

    internal static SurvivalHudPresentation HudPresentation(
        string? incidentId,
        bool requestedCollapsed)
    {
        var isCollapsed = Find(incidentId) is not null && requestedCollapsed;
        return new SurvivalHudPresentation(
            isCollapsed,
            !isCollapsed,
            isCollapsed ? "MORE" : "LESS",
            isCollapsed
                ? "Show all recovery instructions on the map"
                : "Keep only the urgent recovery action on the map");
    }

    internal static RecoveryRemedy ResolveRecoveryRemedy(
        string? incidentId,
        bool liveMapServicesAvailable,
        bool lifeRunActive)
    {
        var normalized = NormalizeIncidentId(incidentId);
        if (normalized is "vomit" or "bacteria" && liveMapServicesAvailable)
        {
            return new RecoveryRemedy(
                RecoveryRemedyKind.ResourceFinder,
                "salt",
                "FIND SALT LICK",
                "Open current public Gateway Salt Lick sites. Static map source; verify the site in game.");
        }

        if (normalized == "starving")
        {
            if (liveMapServicesAvailable && lifeRunActive)
            {
                return new RecoveryRemedy(
                    RecoveryRemedyKind.ResourceFinder,
                    "diet",
                    "FIND FOOD SITE",
                    "Use your selected species and nutrient need to open the best matching public food site.");
            }

            if (liveMapServicesAvailable)
            {
                return new RecoveryRemedy(
                    RecoveryRemedyKind.FoodLayer,
                    "food",
                    "SHOW FOOD LAYER",
                    "Enable the authenticated server Food layer; start a Life Run for species-aware site guidance.");
            }

            return new RecoveryRemedy(
                RecoveryRemedyKind.DietCoach,
                "diet-coach",
                "OPEN DIET COACH",
                "Open the manual species and nutrient coach for this server session.");
        }

        var pinType = normalized == "dehydrated" ? "water" : "safe";
        return new RecoveryRemedy(
            RecoveryRemedyKind.SavedPin,
            pinType,
            $"ROUTE TO SAVED {pinType.ToUpperInvariant()} PIN",
            $"Route to the nearest personal {pinType} marker saved on this PC.");
    }

    internal static ReportedHealthState NormalizeHealthState(ReportedHealthState state) =>
        Enum.IsDefined(state) ? state : ReportedHealthState.Unknown;

    internal static ReportedHealthState NextHealthState(ReportedHealthState state) =>
        NormalizeHealthState(state) switch
        {
            ReportedHealthState.Unknown => ReportedHealthState.Stable,
            ReportedHealthState.Stable => ReportedHealthState.Hurt,
            ReportedHealthState.Hurt => ReportedHealthState.Critical,
            _ => ReportedHealthState.Unknown
        };

    internal static string HealthLabel(ReportedHealthState state) =>
        NormalizeHealthState(state) switch
        {
            ReportedHealthState.Stable => "OK",
            ReportedHealthState.Hurt => "HURT",
            ReportedHealthState.Critical => "CRIT",
            _ => "?"
        };

    internal static string StatusBeaconLabel(
        ReportedHealthState health,
        SurvivalIncidentDefinition? incident,
        DateTimeOffset startedAt,
        DateTimeOffset now,
        int additionalSeconds = 0)
    {
        var healthLabel = $"HP {HealthLabel(health)}";
        if (incident is not { } active) return $"{healthLabel} · REPORT";
        var remaining = RemainingSeconds(active, startedAt, now, additionalSeconds);
        var timer = active.ExpectedSeconds <= 0
            ? string.Empty
            : remaining > 0
                ? $" {FormatRemaining(remaining)}"
                : " CHECK";
        return $"{healthLabel} · {active.ShortLabel}{timer}";
    }
}
