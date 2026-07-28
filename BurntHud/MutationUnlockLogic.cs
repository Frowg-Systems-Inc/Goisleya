namespace Isley;

internal enum MutationUnlockMode
{
    Counter,
    Timer,
    Toggle
}

internal sealed record MutationUnlockChallenge(
    string Id,
    string MutationId,
    string Label,
    MutationUnlockMode Mode,
    int Target,
    int Step,
    string Unit,
    int TimerMinutes,
    string TimerLabel,
    string Goal,
    string NextAction);

internal readonly record struct MutationUnlockProgress(string ChallengeId, int Value);

internal static class MutationUnlockLogic
{
    internal const string SnapshotDate = "2026-05-28";

    internal static readonly MutationUnlockChallenge[] Challenges =
    [
        new(
            "night-hunter",
            "augmented-tapetum",
            "AUGMENTED TAPETUM",
            MutationUnlockMode.Counter,
            5,
            1,
            "NIGHT KILLS",
            0,
            string.Empty,
            "Kill five players at night.",
            "Record only a confirmed nighttime player kill."),
        new(
            "nutrient-streak",
            "enhanced-digestion",
            "ENHANCED DIGESTION",
            MutationUnlockMode.Timer,
            60,
            5,
            "MIN NUTRIENTS",
            60,
            "NUTRIENT UNLOCK STREAK",
            "Maintain nutrients for 60 minutes.",
            "Start while nutrients are present; reset if the condition breaks."),
        new(
            "hunger-streak",
            "heightened-ghrelin",
            "HEIGHTENED GHRELIN",
            MutationUnlockMode.Timer,
            30,
            5,
            "MIN ABOVE 80%",
            30,
            "HUNGER 80 UNLOCK STREAK",
            "Keep current hunger above 80% for 30 minutes.",
            "Start above 80%; reset immediately if hunger drops below it."),
        new(
            "stamina-drain",
            "multichambered-lungs",
            "MULTICHAMBERED LUNGS",
            MutationUnlockMode.Counter,
            4500,
            250,
            "STAMINA DRAINED",
            0,
            string.Empty,
            "Drain 4,500 stamina by sprinting or fast-swimming.",
            "Add a conservative 250 only after observing the drain."),
        new(
            "fracture-bones",
            "osteophagic",
            "OSTEOPHAGIC",
            MutationUnlockMode.Toggle,
            1,
            1,
            "CONDITION",
            0,
            string.Empty,
            "Eat bones while you have a broken bone.",
            "Mark done only after the fractured-bone action is completed."),
        new(
            "jump-training",
            "reinforced-tendons",
            "REINFORCED TENDONS",
            MutationUnlockMode.Counter,
            50,
            5,
            "JUMPS",
            0,
            string.Empty,
            "Jump 50 times.",
            "Add five after a counted set; use minus to correct over-counting."),
        new(
            "saltwater-conditioning",
            "reniculate-kidneys",
            "RENICULATE KIDNEYS",
            MutationUnlockMode.Counter,
            1250,
            50,
            "THIRST LOST",
            0,
            string.Empty,
            "Lose 1,250 thirst by drinking saltwater.",
            "Add 50 only after the in-game saltwater drain is observed.")
    ];

    internal static MutationUnlockChallenge? Find(string? id) =>
        Challenges.FirstOrDefault(challenge =>
            string.Equals(challenge.Id, id, StringComparison.OrdinalIgnoreCase));

    internal static int NormalizeSelectedIndex(int index) =>
        Challenges.Length == 0 ? 0 : Math.Clamp(index, 0, Challenges.Length - 1);

    internal static IReadOnlyList<MutationUnlockProgress> NormalizeProgress(
        IEnumerable<MutationUnlockProgress>? progress)
    {
        if (progress is null) return [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<MutationUnlockProgress>();
        foreach (var item in progress)
        {
            var challenge = Find(item.ChallengeId);
            if (challenge is null || !seen.Add(challenge.Id)) continue;
            var value = Math.Clamp(item.Value, 0, challenge.Target);
            if (value > 0) normalized.Add(new MutationUnlockProgress(challenge.Id, value));
        }
        return normalized;
    }

    internal static int ValueFor(IEnumerable<MutationUnlockProgress> progress, string challengeId) =>
        progress.FirstOrDefault(item =>
            string.Equals(item.ChallengeId, challengeId, StringComparison.OrdinalIgnoreCase)).Value;

    internal static IReadOnlyList<MutationUnlockProgress> SetValue(
        IEnumerable<MutationUnlockProgress> progress,
        string challengeId,
        int value)
    {
        var challenge = Find(challengeId);
        if (challenge is null) return NormalizeProgress(progress);
        var next = NormalizeProgress(progress)
            .Where(item => !string.Equals(item.ChallengeId, challenge.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        value = Math.Clamp(value, 0, challenge.Target);
        if (value > 0) next.Add(new MutationUnlockProgress(challenge.Id, value));
        return NormalizeProgress(next);
    }

    internal static int Adjust(MutationUnlockChallenge challenge, int currentValue, int direction)
    {
        var step = direction < 0 ? -challenge.Step : challenge.Step;
        return Math.Clamp(currentValue + step, 0, challenge.Target);
    }

    internal static int EffectiveValue(
        MutationUnlockChallenge challenge,
        int storedValue,
        double timerElapsedSeconds,
        bool timerCompleted)
    {
        storedValue = Math.Clamp(storedValue, 0, challenge.Target);
        if (challenge.Mode != MutationUnlockMode.Timer) return storedValue;
        if (timerCompleted) return challenge.Target;
        var timerMinutes = Math.Max(0, (int)Math.Floor(timerElapsedSeconds / 60d));
        return Math.Clamp(Math.Max(storedValue, timerMinutes), 0, challenge.Target);
    }

    internal static bool IsComplete(MutationUnlockChallenge challenge, int value) =>
        value >= challenge.Target;

    internal static int CompletedCount(IEnumerable<MutationUnlockProgress> progress)
    {
        var normalized = NormalizeProgress(progress);
        return Challenges.Count(challenge => ValueFor(normalized, challenge.Id) >= challenge.Target);
    }

    internal static string ProgressLabel(MutationUnlockChallenge challenge, int value)
    {
        value = Math.Clamp(value, 0, challenge.Target);
        if (challenge.Mode == MutationUnlockMode.Toggle)
        {
            return value >= challenge.Target ? "CONDITION DONE" : "CONDITION NOT DONE";
        }
        return $"{value:N0} / {challenge.Target:N0} {challenge.Unit}";
    }

    internal static string CompactSummary(
        IEnumerable<MutationUnlockProgress> progress,
        int selectedIndex)
    {
        var normalized = NormalizeProgress(progress);
        var challenge = Challenges[NormalizeSelectedIndex(selectedIndex)];
        var value = ValueFor(normalized, challenge.Id);
        return $"UNLOCKS {CompletedCount(normalized)}/{Challenges.Length} · {challenge.Label} {value:N0}/{challenge.Target:N0}";
    }
}
