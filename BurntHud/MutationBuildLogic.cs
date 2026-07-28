namespace Isley;

internal sealed record MutationBuildFocusDefinition(
    string Id,
    string Label,
    string ShortLabel,
    string Description,
    string[] Keywords);

internal sealed record MutationSynergyDefinition(
    string FirstMutationId,
    string SecondMutationId,
    string Label);

internal readonly record struct MutationBuildAnalysis(
    MutationBuildFocusDefinition Focus,
    int FitPercent,
    int SustainPercent,
    int FightPercent,
    int MovePercent,
    int RolePercent,
    string Heading,
    string Insight,
    string SynergyLabel,
    string RecommendationId,
    string RecommendationName,
    string RecommendationMeta,
    string RecommendationReason,
    int RecommendationSlot)
{
    internal bool HasRecommendation => !string.IsNullOrWhiteSpace(RecommendationId);
}

internal static class MutationBuildLogic
{
    private static readonly string[] SustainKeywords =
        ["survival", "health", "heal", "regen", "food", "hunger", "water", "thirst", "diet", "nutrients"];
    private static readonly string[] FightKeywords =
        ["combat", "offense", "damage", "bleed", "fracture", "defense", "tank", "pounce", "buck", "prey", "hunt"];
    private static readonly string[] MoveKeywords =
        ["travel", "stamina", "speed", "swim", "aquatic", "oxygen", "jump", "flight", "wade", "sprint"];
    private static readonly string[] UtilityKeywords =
        ["awareness", "stealth", "tracks", "sound", "night", "vision", "nest", "group", "weather", "reproduction"];

    internal static readonly MutationBuildFocusDefinition[] Focuses =
    [
        new("balanced", "BALANCED", "BAL", "even survival, combat, movement, and utility coverage", UtilityKeywords),
        new("survival", "SURVIVAL", "SURV", "sustain, recovery, food, water, bleed, and fracture coverage", SustainKeywords),
        new("combat", "COMBAT", "FIGHT", "damage pressure, resistance, bucking, and fracture coverage", FightKeywords),
        new("travel", "TRAVEL", "MOVE", "stamina, speed, jumping, flight, and terrain coverage", MoveKeywords),
        new("aquatic", "AQUATIC", "AQUA", "swimming, oxygen, water access, and underwater vision", ["aquatic", "swim", "underwater", "oxygen", "water", "wade", "salt"]),
        new("nesting", "NESTING", "NEST", "gestation, young, solo nesting, and reproductive utility", ["nest", "nesting", "egg", "parent", "reproduction", "female", "young", "gestation"]),
        new("stealth", "STEALTH", "STEALTH", "quiet movement, fading tracks, night vision, and awareness", ["stealth", "tracks", "footprint", "sound", "night", "vision", "awareness", "chat"]),
        new("group", "GROUP", "GROUP", "communication, leadership, awareness, and pack utility", ["group", "leader", "social", "communication", "chat", "awareness", "sound"])
    ];

    internal static readonly MutationSynergyDefinition[] Synergies =
    [
        new("hydrodynamic", "increased-inspiratory-capacity", "SWIM SPEED + DIVE TIME"),
        new("efficient-digestion", "sustained-hydration", "FOOD + WATER ENDURANCE"),
        new("cellular-regeneration", "epidermal-fibrosis", "RECOVERY + BLEED DEFENSE"),
        new("advanced-gestation", "prolific-reproduction", "FASTER CLUTCH + STRONGER YOUNG"),
        new("featherweight", "infrasound-communication", "QUIETER TRAILS + GROUP CHAT"),
        new("photosynthetic-regeneration", "photosynthetic-tissue", "DAY STAMINA + HEALTH"),
        new("hydro-regenerative", "reabsorption", "RAIN HEALTH + WATER RECOVERY"),
        new("multichambered-lungs", "reinforced-tendons", "STAMINA CAPACITY + LOWER COST"),
        new("wader", "reniculate-kidneys", "SHALLOWS + SALTWATER ACCESS")
    ];

    internal static int NormalizeFocusIndex(int index) =>
        Math.Clamp(index, 0, Focuses.Length - 1);

    internal static int CycleFocusIndex(int current, int delta)
    {
        var normalized = NormalizeFocusIndex(current);
        var direction = Math.Clamp(delta, -1, 1);
        return (normalized + direction + Focuses.Length) % Focuses.Length;
    }

    internal static bool IsDietCompatible(MutationCatalogEntry mutation, string? dietClass)
    {
        var normalizedDiet = (dietClass ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedDiet)
            || normalizedDiet.Contains("choose", StringComparison.Ordinal)
            || normalizedDiet.Contains("unknown", StringComparison.Ordinal))
        {
            return true;
        }

        var restrictions = mutation.Restrictions.ToLowerInvariant();
        if (restrictions.Contains("herbivore or omnivore", StringComparison.Ordinal))
        {
            return normalizedDiet is "herbivore" or "omnivore";
        }
        if (restrictions.Contains("carnivore", StringComparison.Ordinal) && normalizedDiet != "carnivore")
        {
            return false;
        }
        if (restrictions.Contains("herbivore", StringComparison.Ordinal) && normalizedDiet != "herbivore")
        {
            return false;
        }
        return true;
    }

    internal static MutationBuildAnalysis Analyze(
        int focusIndex,
        IEnumerable<MutationLoadoutItem>? rawLoadout,
        string? dietClass)
    {
        var focus = Focuses[NormalizeFocusIndex(focusIndex)];
        var loadout = MutationPlannerLogic.NormalizeLoadout(rawLoadout).ToArray();
        var entries = loadout
            .Select(item => MutationPlannerLogic.FindById(item.MutationId))
            .Where(entry => entry is not null)
            .Cast<MutationCatalogEntry>()
            .ToArray();

        var sustain = Coverage(entries, SustainKeywords);
        var fight = Coverage(entries, FightKeywords);
        var move = Coverage(entries, MoveKeywords);
        var role = Coverage(entries, focus.Keywords);
        var fit = string.Equals(focus.Id, "balanced", StringComparison.Ordinal)
            ? (int)Math.Round((sustain + fight + move + role) / 4d)
            : (int)Math.Round((sustain + fight + move + role * 2) / 5d);

        var loadedIds = loadout.Select(item => item.MutationId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var synergy = Synergies.FirstOrDefault(pair =>
            loadedIds.Contains(pair.FirstMutationId) && loadedIds.Contains(pair.SecondMutationId));
        var insight = synergy is not null
            ? $"PAIR · {synergy.Label}"
            : BuildGapInsight(focus, sustain, fight, move, role, entries.Length);
        var heading = entries.Length == 0
            ? $"BUILD START · {focus.Label}"
            : fit >= 75
                ? $"BUILD COHERENT · {focus.Label}"
                : fit >= 45
                    ? $"BUILD FORMING · {focus.Label}"
                    : $"BUILD GAP · {focus.Label}";

        var recommendation = MutationPlannerLogic.Catalog
            .Select((entry, index) => new
            {
                Entry = entry,
                Index = index,
                Slot = MutationPlannerLogic.NextFreeSlotForMutation(loadout, entry),
                Synergy = SynergyCompletedBy(entry.Id, loadedIds),
                Score = RecommendationScore(entry, focus, sustain, fight, move, role, loadedIds)
            })
            .Where(candidate => !loadedIds.Contains(candidate.Entry.Id)
                                && candidate.Slot > 0
                                && IsDietCompatible(candidate.Entry, dietClass))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Index)
            .FirstOrDefault();

        if (recommendation is null)
        {
            return new MutationBuildAnalysis(
                focus, fit, sustain, fight, move, role, heading, insight,
                synergy?.Label ?? string.Empty,
                string.Empty, "NO AVAILABLE GUIDE FIT", "LOADOUT FULL",
                "REMOVE OR CORRECT A SLOT TO CONTINUE", 0);
        }

        var recommendationReason = recommendation.Synergy is not null
            ? $"COMPLETES · {recommendation.Synergy.Label}"
            : RecommendationReason(recommendation.Entry, focus, sustain, fight, move, role);
        var restriction = string.IsNullOrWhiteSpace(recommendation.Entry.Restrictions)
            ? recommendation.Entry.Group.ToUpperInvariant()
            : recommendation.Entry.Restrictions.ToUpperInvariant();
        return new MutationBuildAnalysis(
            focus, fit, sustain, fight, move, role, heading, insight,
            synergy?.Label ?? string.Empty,
            recommendation.Entry.Id,
            recommendation.Entry.Name.ToUpperInvariant(),
            $"S{recommendation.Slot} · {restriction}",
            recommendationReason,
            recommendation.Slot);
    }

    internal static string CompactSummary(MutationBuildAnalysis analysis) =>
        $"BUILD {analysis.Focus.ShortLabel} {analysis.FitPercent}%";

    private static int Coverage(IEnumerable<MutationCatalogEntry> entries, string[] keywords) =>
        Math.Min(100, entries.Count(entry => Matches(entry, keywords)) * 34);

    private static bool Matches(MutationCatalogEntry entry, string[] keywords)
    {
        var corpus = $"{entry.Tags} {entry.Effect} {entry.Restrictions} {entry.Group}".ToLowerInvariant();
        return keywords.Any(keyword => corpus.Contains(keyword, StringComparison.Ordinal));
    }

    private static int MatchCount(MutationCatalogEntry entry, string[] keywords)
    {
        var corpus = $"{entry.Tags} {entry.Effect} {entry.Restrictions} {entry.Group}".ToLowerInvariant();
        return keywords.Count(keyword => corpus.Contains(keyword, StringComparison.Ordinal));
    }

    private static string BuildGapInsight(
        MutationBuildFocusDefinition focus,
        int sustain,
        int fight,
        int move,
        int role,
        int count)
    {
        if (count == 0) return "ADD ONE PICK TO START THE BUILD SIGNAL";
        if (!string.Equals(focus.Id, "balanced", StringComparison.Ordinal) && role == 0)
        {
            return $"GAP · NO {focus.Label} PICK YET";
        }

        var weakest = new[]
            {
                (Label: "SUSTAIN", Score: sustain),
                (Label: "FIGHT", Score: fight),
                (Label: "MOVE", Score: move),
                (Label: "ROLE", Score: role)
            }
            .OrderBy(item => item.Score)
            .First();
        return weakest.Score < 67
            ? $"GAP · {weakest.Label} COVERAGE {weakest.Score}%"
            : "COVERAGE · FOUR-WAY FOUNDATION READY";
    }

    private static MutationSynergyDefinition? SynergyCompletedBy(
        string candidateId,
        HashSet<string> loadedIds) =>
        Synergies.FirstOrDefault(pair =>
            (string.Equals(pair.FirstMutationId, candidateId, StringComparison.OrdinalIgnoreCase)
             && loadedIds.Contains(pair.SecondMutationId))
            || (string.Equals(pair.SecondMutationId, candidateId, StringComparison.OrdinalIgnoreCase)
                && loadedIds.Contains(pair.FirstMutationId)));

    private static int RecommendationScore(
        MutationCatalogEntry entry,
        MutationBuildFocusDefinition focus,
        int sustain,
        int fight,
        int move,
        int role,
        HashSet<string> loadedIds)
    {
        var score = MatchCount(entry, focus.Keywords) * 60;
        if (Matches(entry, SustainKeywords)) score += 100 - sustain;
        if (Matches(entry, FightKeywords)) score += 100 - fight;
        if (Matches(entry, MoveKeywords)) score += 100 - move;
        if (Matches(entry, focus.Keywords)) score += 120 - role;
        if (SynergyCompletedBy(entry.Id, loadedIds) is not null) score += 240;
        if (entry.Restrictions.Contains("Female only", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(focus.Id, "nesting", StringComparison.Ordinal))
        {
            score -= 120;
        }
        return score;
    }

    private static string RecommendationReason(
        MutationCatalogEntry entry,
        MutationBuildFocusDefinition focus,
        int sustain,
        int fight,
        int move,
        int role)
    {
        if (!string.Equals(focus.Id, "balanced", StringComparison.Ordinal)
            && role < 67
            && Matches(entry, focus.Keywords))
        {
            return $"STRENGTHENS · {focus.Label} ROLE";
        }
        if (sustain < 67 && Matches(entry, SustainKeywords)) return "FILLS · SUSTAIN GAP";
        if (fight < 67 && Matches(entry, FightKeywords)) return "FILLS · FIGHT GAP";
        if (move < 67 && Matches(entry, MoveKeywords)) return "FILLS · MOVE GAP";
        return "ADDS · EVEN COVERAGE";
    }
}
