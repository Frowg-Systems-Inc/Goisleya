namespace Isley;

internal enum PlayerZone
{
    Outside,
    Sanctuary,
    Migration,
    Patrol
}

internal enum ZoneBriefTone
{
    Neutral,
    Active,
    Warning
}

internal readonly record struct ZoneBriefSnapshot(
    bool LifeRunActive,
    bool StreamerMode,
    bool LiveMapAvailable,
    PlayerZone Zone,
    int StageIndex,
    bool SpeciesSelected,
    string DietClass,
    int DietFilledCount);

internal readonly record struct ZoneBriefView(
    bool IsVisible,
    PlayerZone Zone,
    string ZoneLabel,
    string Heading,
    string Detail,
    string ActionLabel,
    string ActionId,
    string NextObjective,
    ZoneBriefTone Tone,
    bool RequiresAttention);

internal static class ZoneBriefLogic
{
    internal static PlayerZone NormalizeZone(int value) =>
        Enum.IsDefined(typeof(PlayerZone), value) ? (PlayerZone)value : PlayerZone.Outside;

    internal static string Label(PlayerZone zone) => zone switch
    {
        PlayerZone.Sanctuary => "SANCTUARY",
        PlayerZone.Migration => "MIGRATION",
        PlayerZone.Patrol => "PATROL",
        _ => "OUTSIDE"
    };

    internal static ZoneBriefView Evaluate(ZoneBriefSnapshot raw)
    {
        var zone = NormalizeZone((int)raw.Zone);
        if (!raw.LifeRunActive || raw.StreamerMode)
        {
            return Hidden(zone);
        }

        var guideAction = raw.LiveMapAvailable ? "layers" : "current-zones-guide";
        var guideLabel = raw.LiveMapAvailable ? "ZONE LAYERS" : "CURRENT GUIDE";
        var stage = Math.Clamp(raw.StageIndex, 0, 4);
        var dietClass = NormalizeDietClass(raw.DietClass);
        var filled = Math.Clamp(raw.DietFilledCount, 0, 3);

        if (zone == PlayerZone.Outside)
        {
            return View(
                zone,
                "REPORT THE COMPASS ZONE",
                "Select the Sanctuary, Migration, or Patrol icon currently shown in game. Isley cannot read that personal HUD signal.",
                guideLabel,
                guideAction,
                string.Empty,
                ZoneBriefTone.Neutral,
                false);
        }

        if (!raw.SpeciesSelected)
        {
            return View(
                zone,
                "SET SPECIES BEFORE COMMITTING",
                "Zone food and risk depend on the animal you spawned. Choose it before treating this zone as useful.",
                "PICK SPECIES",
                "field-guide",
                "SET SPECIES FOR ZONE",
                ZoneBriefTone.Warning,
                true);
        }

        return zone switch
        {
            PlayerZone.Sanctuary => Sanctuary(stage, dietClass, filled, guideLabel, guideAction),
            PlayerZone.Migration => Migration(dietClass, filled, guideLabel, guideAction),
            PlayerZone.Patrol => Patrol(dietClass, guideLabel, guideAction),
            _ => Hidden(zone)
        };
    }

    internal static string CompactSummary(ZoneBriefView view) =>
        view.IsVisible && view.Zone != PlayerZone.Outside
            ? $"ZONE {view.ZoneLabel}"
            : string.Empty;

    private static ZoneBriefView Sanctuary(
        int stage,
        string dietClass,
        int filled,
        string guideLabel,
        string guideAction)
    {
        if (stage >= 2)
        {
            return View(
                PlayerZone.Sanctuary,
                "LEAVE BEFORE BEES COMMIT",
                "Sanctuaries are juvenile space. At Subadult or later, move to a clear exit and verify the bee pressure in game.",
                guideLabel,
                guideAction,
                "LEAVE SANCTUARY",
                ZoneBriefTone.Warning,
                true);
        }

        if (dietClass == "Carnivore")
        {
            return View(
                PlayerZone.Sanctuary,
                "USE COVER; DO NOT ASSUME PREY",
                "Dense juvenile cover can hide movement, but it does not guarantee a meal. Keep an exit and verify every contact in game.",
                "FIGHT CHECK",
                "fight-check",
                "KEEP A SANCTUARY EXIT",
                ZoneBriefTone.Active,
                false);
        }

        if (filled < 3)
        {
            return View(
                PlayerZone.Sanctuary,
                "BUILD THE THREE-NUTRIENT COMBO",
                "Juvenile Sanctuary mushrooms can fill all three nutrient types. Confirm the food and your bars in game, then leave before Subadult pressure.",
                "DIET COACH",
                "diet-coach",
                "BUILD SANCTUARY DIET",
                ZoneBriefTone.Active,
                false);
        }

        return View(
            PlayerZone.Sanctuary,
            "DIET SET; PLAN THE EXIT",
            "Protect the logged three-nutrient combo and identify a clear route out before the Sanctuary stops serving this growth stage.",
            guideLabel,
            guideAction,
            "PLAN SANCTUARY EXIT",
            ZoneBriefTone.Active,
            false);
    }

    private static ZoneBriefView Migration(
        string dietClass,
        int filled,
        string guideLabel,
        string guideAction)
    {
        if (dietClass == "Carnivore")
        {
            return View(
                PlayerZone.Migration,
                "TRAIL AT RANGE; PREY IS NOT GUARANTEED",
                "Migration concentrates likely movement, not a confirmed target. Keep stamina and an exit, then use authorized contacts only as evidence.",
                "FIGHT CHECK",
                "fight-check",
                "ASSESS MIGRATION CONTACTS",
                ZoneBriefTone.Active,
                false);
        }

        if (filled < 3)
        {
            return View(
                PlayerZone.Migration,
                "EAT YOUR SPECIES FOODS",
                "An active Migration boosts the yield of preferred foods; it does not auto-fill every nutrient. Verify each food and bar in game.",
                "DIET COACH",
                "diet-coach",
                "FILL MIGRATION DIET",
                ZoneBriefTone.Active,
                false);
        }

        return View(
            PlayerZone.Migration,
            "MOVE WHEN THE ZONE MOVES",
            "Your logged diet is full. Protect it, watch the in-game signal, and do not linger after the active Migration shifts.",
            guideLabel,
            guideAction,
            "FOLLOW ACTIVE MIGRATION",
            ZoneBriefTone.Active,
            false);
    }

    private static ZoneBriefView Patrol(
        string dietClass,
        string guideLabel,
        string guideAction)
    {
        var detail = dietClass == "Carnivore"
            ? "Patrol is personal or group-leader scoped. Recent activity can activate a carnivore Patrol, but it is a clue—not a guaranteed player."
            : "Patrol is personal or group-leader scoped. Follow the in-game target for you or your leader and verify food before committing.";
        return View(
            PlayerZone.Patrol,
            "FOLLOW YOUR ASSIGNED PATROL",
            detail,
            guideLabel,
            guideAction,
            "FOLLOW ASSIGNED PATROL",
            ZoneBriefTone.Active,
            false);
    }

    private static string NormalizeDietClass(string? value) => value?.Trim() switch
    {
        "Carnivore" => "Carnivore",
        "Herbivore" => "Herbivore",
        "Omnivore" => "Omnivore",
        _ => "Unknown"
    };

    private static ZoneBriefView View(
        PlayerZone zone,
        string heading,
        string detail,
        string actionLabel,
        string actionId,
        string nextObjective,
        ZoneBriefTone tone,
        bool requiresAttention) =>
        new(
            true,
            zone,
            Label(zone),
            heading,
            detail,
            actionLabel,
            actionId,
            nextObjective,
            tone,
            requiresAttention);

    private static ZoneBriefView Hidden(PlayerZone zone) =>
        new(false, zone, Label(zone), string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, ZoneBriefTone.Neutral, false);
}
