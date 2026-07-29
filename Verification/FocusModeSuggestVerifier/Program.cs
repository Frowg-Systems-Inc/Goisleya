using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void Suggests(string category, string tone, string expectedModeId, string message)
{
    var suggestion = FocusModeSuggestLogic.FromNextMove(category, tone, "balanced");
    Check(suggestion.Available, message);
    Check(suggestion.ModeId == expectedModeId, $"{message} (mode)");
    var definition = FocusModeLogic.Find(expectedModeId);
    Check(definition is not null, $"{message} (known mode)");
    Check(suggestion.Label == definition!.Label, $"{message} (label)");
    Check(suggestion.Reason.Contains("nothing changes automatically"),
        $"{message} (suggestion stays advisory)");
}

static void Silent(string category, string tone, string activeModeId, string message)
{
    var suggestion = FocusModeSuggestLogic.FromNextMove(category, tone, activeModeId);
    Check(!suggestion.Available, message);
    Check(suggestion.ModeId.Length == 0
          && suggestion.Label.Length == 0
          && suggestion.Reason.Length == 0, $"{message} (fully empty)");
}

Suggests("PACK", "Info", "pack", "pack category suggests pack focus");
Suggests("NEST", "Info", "nest", "nest category suggests nest focus");
Suggests("CONTACT", "Info", "combat", "contact category suggests combat focus");
Suggests("COMBAT", "Info", "combat", "combat category suggests combat focus");
Suggests("TRAVEL", "Info", "travel", "travel category suggests travel focus");
Suggests("ROUTE", "Info", "travel", "route category suggests travel focus");
Suggests("WAYPOINT", "Info", "travel", "waypoint category suggests travel focus");
Suggests("SURVIVAL", "Info", "survival", "survival category suggests survival focus");
Suggests("VITALS", "Info", "survival", "vitals category suggests survival focus");
Suggests("FOOD", "Info", "survival", "food category suggests survival focus");
Suggests("WATER", "Info", "survival", "water category suggests survival focus");
Suggests(" pack ", "Info", "pack", "category matching trims and ignores case");
Suggests("UNKNOWN", "Critical", "combat", "critical tone falls back to combat focus");
Suggests("UNKNOWN", "warning", "combat", "warning tone falls back to combat focus");

Silent("UNKNOWN", "Info", "balanced", "unknown category with calm tone stays silent");
Silent("PACK", "Info", "pack", "active mode is never re-suggested");
Silent("pack", "Info", "PACK", "active-mode compare ignores case");
Silent(null!, "Info", "balanced", "null category stays silent");

foreach (var definition in FocusModeLogic.Definitions)
{
    var suggestion = FocusModeSuggestLogic.FromNextMove(
        definition.Id, "Info", "balanced");
    Check(!suggestion.Available || FocusModeLogic.Find(suggestion.ModeId) is not null,
        $"{definition.Id} suggestions always resolve to a real focus mode");
}

Console.WriteLine(
    "Focus mode suggestion verification passed (category mapping, tone fallback, active-mode suppression, advisory copy, and resolution integrity).");
