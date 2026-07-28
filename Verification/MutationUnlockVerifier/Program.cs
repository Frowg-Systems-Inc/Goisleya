using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var challenges = MutationUnlockLogic.Challenges;
Check(challenges.Length == 7, "challenge count");
Check(challenges.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == challenges.Length,
    "unique challenge ids");
Check(challenges.Select(item => item.MutationId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == challenges.Length,
    "unique mutation ids");
Check(challenges.All(item => MutationPlannerLogic.FindById(item.MutationId) is not null),
    "every challenge maps to the mutation catalog");
Check(challenges.Count(item => item.Mode == MutationUnlockMode.Timer) == 2, "two timed challenges");
Check(challenges.Count(item => item.Mode == MutationUnlockMode.Counter) == 4, "four counter challenges");
Check(challenges.Count(item => item.Mode == MutationUnlockMode.Toggle) == 1, "one toggle challenge");
Check(challenges.Where(item => item.Mode == MutationUnlockMode.Timer)
    .Select(item => item.TimerLabel).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
    "unique timer labels");
Check(challenges.All(item => item.Target > 0 && item.Step > 0 && item.Step <= item.Target),
    "bounded goals and steps");

var night = MutationUnlockLogic.Find("night-hunter")!;
Check(night.Target == 5 && night.Step == 1, "night kill goal");
Check(MutationUnlockLogic.Adjust(night, 0, 1) == 1, "increment");
Check(MutationUnlockLogic.Adjust(night, 5, 1) == 5, "upper clamp");
Check(MutationUnlockLogic.Adjust(night, 0, -1) == 0, "lower clamp");

var nutrient = MutationUnlockLogic.Find("nutrient-streak")!;
Check(nutrient.TimerMinutes == 60 && nutrient.Target == 60, "nutrient timer goal");
Check(MutationUnlockLogic.EffectiveValue(nutrient, 0, 59 * 60 + 59, false) == 59,
    "timer floors incomplete minutes");
Check(MutationUnlockLogic.EffectiveValue(nutrient, 0, 60 * 60, true) == 60,
    "timer completion");

var normalized = MutationUnlockLogic.NormalizeProgress([
    new MutationUnlockProgress("night-hunter", 2),
    new MutationUnlockProgress("NIGHT-HUNTER", 5),
    new MutationUnlockProgress("jump-training", 999),
    new MutationUnlockProgress("invalid", 3),
    new MutationUnlockProgress("stamina-drain", -1)
]);
Check(normalized.Count == 2, "invalid, duplicate, and zero progress removed");
Check(MutationUnlockLogic.ValueFor(normalized, "night-hunter") == 2, "first duplicate wins");
Check(MutationUnlockLogic.ValueFor(normalized, "jump-training") == 50, "progress clamps to target");

var completed = MutationUnlockLogic.SetValue(normalized, "night-hunter", 5);
Check(MutationUnlockLogic.CompletedCount(completed) == 2, "completed count");
var cleared = MutationUnlockLogic.SetValue(completed, "night-hunter", 0);
Check(MutationUnlockLogic.ValueFor(cleared, "night-hunter") == 0, "zero clears progress row");
Check(MutationUnlockLogic.ProgressLabel(night, 3) == "3 / 5 NIGHT KILLS", "counter label");
Check(MutationUnlockLogic.ProgressLabel(MutationUnlockLogic.Find("fracture-bones")!, 1) == "CONDITION DONE",
    "toggle label");
Check(MutationUnlockLogic.NormalizeSelectedIndex(-5) == 0, "low selected index");
Check(MutationUnlockLogic.NormalizeSelectedIndex(99) == challenges.Length - 1, "high selected index");
Check(MutationUnlockLogic.CompactSummary(completed, 0).StartsWith("UNLOCKS 2/7 · AUGMENTED TAPETUM 5/5"),
    "compact summary");

Console.WriteLine("Mutation unlock verification passed (7 challenges, counters, timers, normalization, and summaries).");
