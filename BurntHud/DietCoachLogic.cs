namespace Isley;

internal readonly record struct DietTargetEntry(
    string Id,
    string Label,
    string Purpose,
    int[] Nutrients);

internal readonly record struct DietSpeciesEntry(
    string Id,
    string Name,
    string DietClass,
    string[] ProteinFoods,
    string[] CarbFoods,
    string[] LipidFoods,
    bool MigrationDriven = false);

internal readonly record struct DietComboResult(
    string Key,
    string Label,
    string Summary,
    string Effects,
    int FilledCount,
    int GrowthBonus,
    bool IsComplete,
    bool MatchesTarget,
    int NeededNutrient,
    int ReplaceNutrient,
    string Recommendation);

internal static class DietCoachLogic
{
    internal const int Empty = 0;
    internal const int Protein = 1;
    internal const int Carbs = 2;
    internal const int Lipids = 3;
    internal const string SpeciesSnapshot = "2026-07-03";
    internal const string CombinationSnapshot = "2026-03-02";

    internal static readonly DietTargetEntry[] Targets =
    [
        new("balanced", "BALANCED", "Maximum growth and efficient travel", [Protein, Carbs, Lipids]),
        new("endurance", "ENDURANCE", "Growth, stamina, travel, scent, and night vision", [Carbs, Carbs, Lipids]),
        new("recovery", "RECOVERY", "Growth, health recovery, fracture recovery, and travel", [Protein, Protein, Carbs]),
        new("awareness", "AWARENESS", "Growth, health recovery, scent, night vision, and bleed resistance", [Protein, Lipids, Lipids]),
        new("nesting", "NESTING", "Growth, health recovery, awareness, and egg incubation", [Protein, Protein, Lipids])
    ];

    internal static readonly DietSpeciesEntry[] Species =
    [
        new("allosaurus", "Allosaurus", "Carnivore",
            ["Stegosaurus", "Tenontosaurus", "Boar"],
            ["Diabloceratops", "Triceratops", "Deer"],
            ["Maiasaura", "Dryosaurus", "Goat"]),
        new("carnotaurus", "Carnotaurus", "Carnivore",
            ["Pachycephalosaurus", "Tenontosaurus", "Herrerasaurus", "Boar"],
            ["Omniraptor", "Diabloceratops", "Troodon", "Deer"],
            ["Dryosaurus", "Gallimimus", "Dilophosaurus"]),
        new("ceratosaurus", "Ceratosaurus", "Carnivore",
            ["Tenontosaurus", "Pachycephalosaurus", "Ceratosaurus"],
            ["Carnotaurus", "Deinosuchus", "Omniraptor", "Diabloceratops", "Deer"],
            ["Dilophosaurus", "Stegosaurus", "Beipiaosaurus", "Goat"]),
        new("deinosuchus", "Deinosuchus", "Carnivore",
            ["Tenontosaurus", "Pachycephalosaurus", "Ceratosaurus"],
            ["Carnotaurus", "Omniraptor", "Diabloceratops", "Deinosuchus", "Troodon", "Bullfrog"],
            ["Elite Fish", "Gallimimus", "Stegosaurus", "Beipiaosaurus", "Maiasaura"]),
        new("dilophosaurus", "Dilophosaurus", "Carnivore",
            ["Boar", "Tenontosaurus", "Herrerasaurus", "Ceratosaurus"],
            ["Diabloceratops", "Carnotaurus", "Hypsilophodon", "Deer", "Chicken"],
            ["Gallimimus", "Maiasaura", "Goat", "Sea Turtle", "Dryosaurus"]),
        new("herrerasaurus", "Herrerasaurus", "Carnivore",
            ["Crab", "Schooling Fish", "Tenontosaurus", "Pachycephalosaurus", "Boar"],
            ["Bullfrog", "Omniraptor", "Hypsilophodon", "Chicken"],
            ["Dryosaurus", "Pteranodon", "Beipiaosaurus", "Goat", "Sea Turtle", "Gallimimus"]),
        new("omniraptor", "Omniraptor", "Carnivore",
            ["Boar", "Herrerasaurus", "Pachycephalosaurus", "Ceratosaurus"],
            ["Carnotaurus", "Diabloceratops", "Troodon", "Deer", "Rabbit"],
            ["Dryosaurus", "Psittacosaurus", "Gallimimus", "Stegosaurus"]),
        new("pteranodon", "Pteranodon", "Carnivore",
            ["Schooling Fish", "Crab"],
            ["Chicken", "Hypsilophodon", "Bullfrog", "Rabbit", "Troodon"],
            ["Sea Turtle", "Psittacosaurus", "Beipiaosaurus", "Pteranodon"]),
        new("troodon", "Troodon", "Carnivore",
            ["Boar", "Pachycephalosaurus", "Tenontosaurus", "Herrerasaurus"],
            ["Chicken", "Rabbit", "Compsognathus", "Hypsilophodon", "Omniraptor"],
            ["Goat", "Psittacosaurus", "Dryosaurus", "Pteranodon"]),
        new("tyrannosaurus", "Tyrannosaurus", "Carnivore",
            ["Stegosaurus", "Tenontosaurus", "Pachycephalosaurus", "Boar", "Crab"],
            ["Diabloceratops", "Triceratops", "Hypsilophodon", "Deer", "Chicken", "Rabbit"],
            ["Maiasaura", "Gallimimus", "Dryosaurus", "Beipiaosaurus", "Goat", "Psittacosaurus", "Sea Turtle"]),

        new("diabloceratops", "Diabloceratops", "Herbivore", [], [], [], true),
        new("dryosaurus", "Dryosaurus", "Herbivore", [], [], [], true),
        new("hypsilophodon", "Hypsilophodon", "Herbivore", [], [], [], true),
        new("kentrosaurus", "Kentrosaurus", "Herbivore", [], [], [], true),
        new("maiasaura", "Maiasaura", "Herbivore", [], [], [], true),
        new("pachycephalosaurus", "Pachycephalosaurus", "Herbivore", [], [], [], true),
        new("stegosaurus", "Stegosaurus", "Herbivore", [], [], [], true),
        new("tenontosaurus", "Tenontosaurus", "Herbivore", [], [], [], true),
        new("triceratops", "Triceratops", "Herbivore", [], [], [], true),

        new("beipiaosaurus", "Beipiaosaurus", "Omnivore",
            ["Radish Flower", "Fireweed"],
            ["Crab", "Schooling Fish", "Mountain Ash"],
            ["Radish Root", "Bullfrog (38%+ growth)"]),
        new("gallimimus", "Gallimimus", "Omnivore",
            ["Radish Flower", "Fireweed"],
            ["Crab", "Mountain Ash"],
            ["Radish Root", "Bullfrog"])
    ];

    internal static int NormalizeNutrient(int nutrient) => Math.Clamp(nutrient, Empty, Lipids);

    internal static int NormalizeSpeciesIndex(int index) => Math.Clamp(index, 0, Species.Length);

    internal static int NormalizeTargetIndex(int index) => Math.Clamp(index, 0, Targets.Length - 1);

    internal static string NutrientName(int nutrient) => NormalizeNutrient(nutrient) switch
    {
        Protein => "PROTEIN",
        Carbs => "CARBS",
        Lipids => "LIPIDS",
        _ => "EMPTY"
    };

    internal static string NutrientShortName(int nutrient) => NormalizeNutrient(nutrient) switch
    {
        Protein => "P",
        Carbs => "C",
        Lipids => "L",
        _ => "-"
    };

    internal static string SlotKey(params int[] slots) => string.Join(
        "+",
        slots.Select(NormalizeNutrient)
            .Where(value => value != Empty)
            .OrderBy(value => value)
            .Select(NutrientShortName));

    internal static DietComboResult Analyze(int slot1, int slot2, int slot3, int targetIndex)
    {
        var slots = new[] { NormalizeNutrient(slot1), NormalizeNutrient(slot2), NormalizeNutrient(slot3) };
        var filled = slots.Count(value => value != Empty);
        var key = SlotKey(slots);
        var combo = CompleteCombo(key);
        var target = Targets[NormalizeTargetIndex(targetIndex)];
        var currentCounts = NutrientCounts(slots);
        var targetCounts = NutrientCounts(target.Nutrients);
        var needed = FirstDifference(targetCounts, currentCounts, missing: true);
        var replace = FirstDifference(targetCounts, currentCounts, missing: false);
        var matchesTarget = filled == 3 && needed == Empty && replace == Empty;

        string recommendation;
        if (matchesTarget)
        {
            recommendation = $"{target.Label} target complete.";
        }
        else if (filled == 0)
        {
            needed = target.Nutrients[0];
            recommendation = $"Start with {NutrientName(needed)} for the {target.Label} target.";
        }
        else if (filled < 3 && needed != Empty)
        {
            recommendation = $"Next: add {NutrientName(needed)} for {target.Label}.";
        }
        else if (needed != Empty && replace != Empty)
        {
            recommendation = $"For {target.Label}, replace {NutrientName(replace)} with {NutrientName(needed)}.";
        }
        else
        {
            recommendation = $"Compare your slots with the {target.Label} target.";
        }

        if (filled < 3)
        {
            return new DietComboResult(
                key,
                $"{filled}/3 LOGGED",
                "Log the nutrient icons shown in game.",
                target.Purpose,
                filled,
                0,
                false,
                false,
                needed,
                replace,
                recommendation);
        }

        return combo with
        {
            MatchesTarget = matchesTarget,
            NeededNutrient = needed,
            ReplaceNutrient = replace,
            Recommendation = recommendation
        };
    }

    internal static string FoodForNutrient(int speciesIndex, int nutrient)
    {
        var normalizedIndex = NormalizeSpeciesIndex(speciesIndex);
        var normalizedNutrient = NormalizeNutrient(nutrient);
        if (normalizedIndex == 0)
        {
            return "Choose a species for current food suggestions.";
        }
        if (normalizedNutrient == Empty)
        {
            return "Your selected target is already matched.";
        }

        var species = Species[normalizedIndex - 1];
        if (species.MigrationDriven)
        {
            return "Migration zones set the current plant diet. Use scent in game and the live Food layer.";
        }

        var foods = normalizedNutrient switch
        {
            Protein => species.ProteinFoods,
            Carbs => species.CarbFoods,
            Lipids => species.LipidFoods,
            _ => []
        };
        return foods.Length == 0
            ? "No static food list is available; verify with scent and the current server rules."
            : string.Join(" / ", foods);
    }

    internal static string SpeciesLabel(int speciesIndex)
    {
        var normalizedIndex = NormalizeSpeciesIndex(speciesIndex);
        return normalizedIndex == 0 ? "UNKNOWN / SERVER MOD" : Species[normalizedIndex - 1].Name.ToUpperInvariant();
    }

    internal static string SpeciesClassLabel(int speciesIndex)
    {
        var normalizedIndex = NormalizeSpeciesIndex(speciesIndex);
        return normalizedIndex == 0 ? "Choose a current playable" : Species[normalizedIndex - 1].DietClass;
    }

    private static int[] NutrientCounts(IEnumerable<int> nutrients)
    {
        var counts = new int[4];
        foreach (var nutrient in nutrients.Select(NormalizeNutrient).Where(value => value != Empty))
        {
            counts[nutrient]++;
        }
        return counts;
    }

    private static int FirstDifference(int[] target, int[] current, bool missing)
    {
        for (var nutrient = Protein; nutrient <= Lipids; nutrient++)
        {
            if (missing && target[nutrient] > current[nutrient]) return nutrient;
            if (!missing && current[nutrient] > target[nutrient]) return nutrient;
        }
        return Empty;
    }

    private static DietComboResult CompleteCombo(string key) => key switch
    {
        "P+C+L" => Complete(key, "PERFECT BALANCE", "+100% growth", "Lower sprint and swim cost", 100),
        "C+C+C" => Complete(key, "TRAVEL FOCUS", "+30% growth", "Lower sprint and swim cost", 30),
        "C+C+L" => Complete(key, "ENDURANCE", "+50% growth", "Scent, night vision, stamina, sprint, and swim benefits", 50),
        "P+C+C" => Complete(key, "MOBILITY RECOVERY", "+50% growth", "Health recovery, fracture resistance, sprint, and swim benefits", 50),
        "L+L+L" => Complete(key, "AWARENESS", "+30% growth", "Stronger scent and night vision", 30),
        "C+L+L" => Complete(key, "SCOUT", "+50% growth", "Travel, scent, night vision, and blood recovery benefits", 50),
        "P+L+L" => Complete(key, "SURVIVOR", "+50% growth", "Health recovery, scent, night vision, and bleed resistance", 50),
        "P+P+P" => Complete(key, "VITALITY", "+30% growth", "Stronger health and locked-health recovery", 30),
        "P+P+C" => Complete(key, "RECOVERY", "+50% growth", "Health, fracture recovery, sprint, and swim benefits", 50),
        "P+P+L" => Complete(key, "NESTING", "+50% growth", "Health recovery, awareness, and egg-incubation benefits", 50),
        _ => Complete(key, "FULL DIET", "Three slots logged", "Verify current effects in game", 0)
    };

    private static DietComboResult Complete(
        string key,
        string label,
        string summary,
        string effects,
        int growthBonus) => new(
            key,
            label,
            summary,
            effects,
            3,
            growthBonus,
            true,
            false,
            Empty,
            Empty,
            string.Empty);
}
