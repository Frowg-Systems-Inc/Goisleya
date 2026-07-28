namespace Isley;

internal enum SpawnPlanState
{
    Hidden,
    Setup,
    Active,
    Complete
}

internal readonly record struct SpawnPlanSnapshot(
    bool LifeRunActive,
    bool StreamerMode,
    bool LiveMapAvailable,
    bool SpeciesSelected,
    bool CoverReady,
    bool ScentChecked,
    bool WaterFound,
    bool FoodFound,
    ReportedVitalState Water,
    bool WaterFresh,
    ReportedVitalState Food,
    bool FoodFresh);

internal readonly record struct SpawnPlanView(
    SpawnPlanState State,
    int Completed,
    int Total,
    string Heading,
    string Detail,
    string ActionLabel,
    string ActionId,
    string CurrentTask)
{
    internal bool IsVisible => State != SpawnPlanState.Hidden;
    internal bool IsComplete => State == SpawnPlanState.Complete;
}

internal static class SpawnPlanLogic
{
    internal const int TaskCount = 4;

    internal static SpawnPlanView Evaluate(SpawnPlanSnapshot raw)
    {
        if (!raw.LifeRunActive || raw.StreamerMode)
        {
            return View(SpawnPlanState.Hidden, 0, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }

        var completed = CompletedCount(raw);
        if (!raw.SpeciesSelected)
        {
            return View(
                SpawnPlanState.Setup,
                completed,
                "SET THE CURRENT SPECIES",
                "Pick the animal you actually spawned so food guidance and the rest of the Life Run use the right profile.",
                "PICK SPECIES",
                "field-guide",
                "SET SPECIES");
        }

        if (!raw.WaterFound && raw.WaterFresh && raw.Water is ReportedVitalState.Low or ReportedVitalState.Empty)
        {
            return View(
                SpawnPlanState.Active,
                completed,
                "WATER FIRST",
                "Thirst is reported low. Use scent from cover, verify the shoreline, drink, then mark Water found.",
                raw.LiveMapAvailable ? "WATER SCENT" : "FIRST-HOUR GUIDE",
                raw.LiveMapAvailable ? "spawn-water-scent" : "current-first-hour-guide",
                "FIND WATER");
        }

        if (!raw.FoodFound && raw.FoodFresh && raw.Food is ReportedVitalState.Low or ReportedVitalState.Empty)
        {
            return View(
                SpawnPlanState.Active,
                completed,
                "FOOD FIRST",
                "Hunger is reported low. Check the selected species' current foods and confirm the source in game.",
                "DIET FOODS",
                "diet-coach",
                "FIND FOOD");
        }

        if (!raw.CoverReady)
        {
            return View(
                SpawnPlanState.Active,
                completed,
                "CHOOSE COVER AND AN EXIT",
                "Leave the open spawn area, avoid steep drops and exposed shorelines, and keep terrain you can climb back out of.",
                raw.LiveMapAvailable ? "SAFE PINS" : "FIRST-HOUR GUIDE",
                raw.LiveMapAvailable ? "pins" : "current-first-hour-guide",
                "SECURE COVER");
        }

        if (!raw.ScentChecked)
        {
            return View(
                SpawnPlanState.Active,
                completed,
                "CHECK SCENT BEFORE MOVING",
                "Hold the in-game scent key from cover and identify the next water, food, or trail clue before committing.",
                raw.LiveMapAvailable ? "SCENT FINDER" : "FIRST-HOUR GUIDE",
                raw.LiveMapAvailable ? "scent-finder" : "current-first-hour-guide",
                "CHECK SCENT");
        }

        if (!raw.WaterFound)
        {
            return View(
                SpawnPlanState.Active,
                completed,
                "FIND WATER SAFELY",
                "Approach from cover with enough stamina to leave; verify the water and shoreline in game before drinking.",
                raw.LiveMapAvailable ? "WATER SCENT" : "FIRST-HOUR GUIDE",
                raw.LiveMapAvailable ? "spawn-water-scent" : "current-first-hour-guide",
                "FIND WATER");
        }

        if (!raw.FoodFound)
        {
            return View(
                SpawnPlanState.Active,
                completed,
                "FIND SPECIES FOOD",
                "Use the selected species' current diet list, then verify scent and the food itself in game.",
                "DIET FOODS",
                "diet-coach",
                "FIND FOOD");
        }

        return View(
            SpawnPlanState.Complete,
            completed,
            "SPAWN PLAN COMPLETE",
            "Cover, scent, water, and food are confirmed for this life. Keep reports current and check the route before exposed travel.",
            raw.LiveMapAvailable ? "TRIP CHECK" : "FIRST-HOUR GUIDE",
            raw.LiveMapAvailable ? "trip-check" : "current-first-hour-guide",
            "READY TO TRAVEL");
    }

    internal static int CompletedCount(SpawnPlanSnapshot raw) =>
        (raw.CoverReady ? 1 : 0)
        + (raw.ScentChecked ? 1 : 0)
        + (raw.WaterFound ? 1 : 0)
        + (raw.FoodFound ? 1 : 0);

    internal static string CompactSummary(SpawnPlanView view) =>
        view.IsVisible ? $"SPAWN {view.Completed}/{view.Total}" : string.Empty;

    private static SpawnPlanView View(
        SpawnPlanState state,
        int completed,
        string heading,
        string detail,
        string actionLabel,
        string actionId,
        string currentTask) =>
        new(
            state,
            Math.Clamp(completed, 0, TaskCount),
            TaskCount,
            heading,
            detail,
            actionLabel,
            actionId,
            currentTask);
}
