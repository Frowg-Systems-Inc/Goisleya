namespace Isley;

internal enum ReportedVitalState
{
    Unknown,
    Stable,
    Low,
    Empty
}

internal readonly record struct CoreVitalsSnapshot(
    ReportedHealthState Health,
    DateTimeOffset HealthReportedAt,
    ReportedVitalState Food,
    DateTimeOffset FoodReportedAt,
    ReportedVitalState Water,
    DateTimeOffset WaterReportedAt,
    ReportedVitalState Stamina,
    DateTimeOffset StaminaReportedAt,
    DateTimeOffset Now);

internal readonly record struct CoreVitalsGuidance(
    ReportedHealthState Health,
    ReportedVitalState Food,
    ReportedVitalState Water,
    ReportedVitalState Stamina,
    bool HealthFresh,
    bool FoodFresh,
    bool WaterFresh,
    bool StaminaFresh,
    int HealthAgeSeconds,
    int FoodAgeSeconds,
    int WaterAgeSeconds,
    int StaminaAgeSeconds,
    int Urgency,
    string Heading,
    string Action,
    string Detail,
    string RoutePinType,
    string RouteLabel,
    string Freshness,
    string CompactLabel,
    string BriefLabel)
{
    internal bool HasFreshReport => HealthFresh || FoodFresh || WaterFresh || StaminaFresh;
    internal bool Warning => Urgency > 0;
    internal bool Critical => Urgency >= 3;
}

internal static class CoreVitalsLogic
{
    internal const int FreshnessSeconds = 300;

    internal static ReportedVitalState Normalize(ReportedVitalState state) =>
        Enum.IsDefined(state) ? state : ReportedVitalState.Unknown;

    internal static ReportedVitalState Next(ReportedVitalState state) =>
        Normalize(state) switch
        {
            ReportedVitalState.Unknown => ReportedVitalState.Stable,
            ReportedVitalState.Stable => ReportedVitalState.Low,
            ReportedVitalState.Low => ReportedVitalState.Empty,
            _ => ReportedVitalState.Unknown
        };

    internal static string Label(ReportedVitalState state) =>
        Normalize(state) switch
        {
            ReportedVitalState.Stable => "OK",
            ReportedVitalState.Low => "LOW",
            ReportedVitalState.Empty => "EMPTY",
            _ => "?"
        };

    internal static string ShortLabel(ReportedVitalState state) =>
        Normalize(state) switch
        {
            ReportedVitalState.Stable => "OK",
            ReportedVitalState.Low => "LOW",
            ReportedVitalState.Empty => "0",
            _ => "?"
        };

    internal static CoreVitalsGuidance Evaluate(CoreVitalsSnapshot raw)
    {
        var health = SurvivalAssistantLogic.NormalizeHealthState(raw.Health);
        var food = Normalize(raw.Food);
        var water = Normalize(raw.Water);
        var stamina = Normalize(raw.Stamina);

        var healthAge = AgeSeconds(raw.HealthReportedAt, raw.Now);
        var foodAge = AgeSeconds(raw.FoodReportedAt, raw.Now);
        var waterAge = AgeSeconds(raw.WaterReportedAt, raw.Now);
        var staminaAge = AgeSeconds(raw.StaminaReportedAt, raw.Now);
        var healthFresh = health != ReportedHealthState.Unknown && IsFresh(healthAge);
        var foodFresh = food != ReportedVitalState.Unknown && IsFresh(foodAge);
        var waterFresh = water != ReportedVitalState.Unknown && IsFresh(waterAge);
        var staminaFresh = stamina != ReportedVitalState.Unknown && IsFresh(staminaAge);

        var currentHealth = healthFresh ? health : ReportedHealthState.Unknown;
        var currentFood = foodFresh ? food : ReportedVitalState.Unknown;
        var currentWater = waterFresh ? water : ReportedVitalState.Unknown;
        var currentStamina = staminaFresh ? stamina : ReportedVitalState.Unknown;

        var urgency = 0;
        var heading = "VITALS REPORTED";
        var action = "KEEP THE SNAPSHOT CURRENT";
        var detail = "Manual bands are current. Refresh any value as the in-game HUD changes.";
        var routePinType = string.Empty;
        var routeLabel = "NO ROUTE NEEDED";

        if (!healthFresh && !foodFresh && !waterFresh && !staminaFresh)
        {
            heading = "NO FRESH VITALS";
            action = "REPORT WHAT THE HUD SHOWS";
            detail = "The authorized map cannot read health, food, water, or stamina. Manual reports expire after five minutes.";
            routeLabel = "REPORT A LOW BAND FIRST";
        }
        else if (currentHealth == ReportedHealthState.Critical)
        {
            urgency = 3;
            heading = "CRITICAL HP REPORTED";
            action = "DISENGAGE NOW";
            detail = "Break contact, reach cover, and follow the in-game EKG. Report a survival issue if a named condition is active.";
            routePinType = "safe";
            routeLabel = "ROUTE TO SAVED SAFE PIN";
        }
        else if (currentWater == ReportedVitalState.Empty)
        {
            urgency = 3;
            heading = "WATER EMPTY";
            action = "FIND WATER NOW";
            detail = "Stop sprinting, conserve stamina, and approach exposed water carefully.";
            routePinType = "water";
            routeLabel = "ROUTE TO SAVED WATER PIN";
        }
        else if (currentFood == ReportedVitalState.Empty)
        {
            urgency = 3;
            heading = "FOOD EMPTY";
            action = "FIND FOOD NOW";
            detail = "Avoid a costly fight and use a species-appropriate food source or saved marker.";
            routePinType = "food";
            routeLabel = "FIND FOOD";
        }
        else if (currentStamina == ReportedVitalState.Empty)
        {
            urgency = 2;
            heading = "STAMINA EMPTY";
            action = "STOP AND RECOVER";
            detail = "Do not begin a crossing, chase, or exposed climb until the in-game stamina bar recovers.";
            routePinType = "safe";
            routeLabel = "ROUTE TO SAVED SAFE PIN";
        }
        else if (currentHealth == ReportedHealthState.Hurt)
        {
            urgency = 2;
            heading = "LOW HP REPORTED";
            action = "HOLD COVER";
            detail = "Avoid re-engaging until the in-game EKG improves; report the exact condition if one is visible.";
            routePinType = "safe";
            routeLabel = "ROUTE TO SAVED SAFE PIN";
        }
        else if (currentWater == ReportedVitalState.Low)
        {
            urgency = 2;
            heading = "WATER LOW";
            action = "PLAN WATER BEFORE TRAVEL";
            detail = "Route before the bar empties and keep enough stamina for the exposed approach.";
            routePinType = "water";
            routeLabel = "ROUTE TO SAVED WATER PIN";
        }
        else if (currentFood == ReportedVitalState.Low)
        {
            urgency = 2;
            heading = "FOOD LOW";
            action = "PLAN FOOD BEFORE COMBAT";
            detail = "Use the Food layer or a saved Food marker before the bar becomes critical.";
            routePinType = "food";
            routeLabel = "FIND FOOD";
        }
        else if (currentStamina == ReportedVitalState.Low)
        {
            urgency = 1;
            heading = "STAMINA LOW";
            action = "CONSERVE STAMINA";
            detail = "Shorten the next movement and avoid committing beyond a safe recovery point.";
            routePinType = "safe";
            routeLabel = "ROUTE TO SAVED SAFE PIN";
        }

        var compact = CompactLabel(currentHealth, currentFood, currentWater, currentStamina);
        var brief = urgency > 0
            ? $"VITALS {SurvivalAssistantLogic.HealthLabel(currentHealth)} / F {ShortLabel(currentFood)} / W {ShortLabel(currentWater)} / ST {ShortLabel(currentStamina)}"
            : string.Empty;
        return new CoreVitalsGuidance(
            currentHealth,
            currentFood,
            currentWater,
            currentStamina,
            healthFresh,
            foodFresh,
            waterFresh,
            staminaFresh,
            healthAge,
            foodAge,
            waterAge,
            staminaAge,
            urgency,
            heading,
            action,
            detail,
            routePinType,
            routeLabel,
            BuildFreshness(healthFresh, healthAge, foodFresh, foodAge, waterFresh, waterAge, staminaFresh, staminaAge),
            compact,
            brief);
    }

    internal static string CompactLabel(
        ReportedHealthState health,
        ReportedVitalState food,
        ReportedVitalState water,
        ReportedVitalState stamina) =>
        $"HP {SurvivalAssistantLogic.HealthLabel(health)} · F {ShortLabel(food)} · W {ShortLabel(water)} · ST {ShortLabel(stamina)}";

    internal static string FormatAge(int seconds)
    {
        var safe = Math.Max(0, seconds);
        return safe < 60 ? $"{safe}s" : $"{safe / 60}m";
    }

    private static int AgeSeconds(DateTimeOffset reportedAt, DateTimeOffset now)
    {
        if (reportedAt == default) return int.MaxValue;
        return (int)Math.Clamp(Math.Floor((now - reportedAt).TotalSeconds), 0, int.MaxValue);
    }

    private static bool IsFresh(int ageSeconds) => ageSeconds < FreshnessSeconds;

    private static string BuildFreshness(
        bool healthFresh,
        int healthAge,
        bool foodFresh,
        int foodAge,
        bool waterFresh,
        int waterAge,
        bool staminaFresh,
        int staminaAge)
    {
        var values = new List<string>();
        if (healthFresh) values.Add($"HP {FormatAge(healthAge)}");
        if (foodFresh) values.Add($"food {FormatAge(foodAge)}");
        if (waterFresh) values.Add($"water {FormatAge(waterAge)}");
        if (staminaFresh) values.Add($"stamina {FormatAge(staminaAge)}");
        return values.Count == 0
            ? "No fresh report - manual / session-only"
            : string.Join(" · ", values) + " · expires at 5m";
    }
}
