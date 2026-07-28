using Isley;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Require(DietCoachLogic.Species.Length == 21, "Current playable catalog count failed");
Require(DietCoachLogic.Species.Select(entry => entry.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 21,
    "Species catalog id uniqueness failed");
Require(DietCoachLogic.Species.Count(entry => entry.DietClass == "Carnivore") == 10,
    "Carnivore catalog count failed");
Require(DietCoachLogic.Species.Count(entry => entry.DietClass == "Herbivore") == 9,
    "Herbivore catalog count failed");
Require(DietCoachLogic.Species.Count(entry => entry.DietClass == "Omnivore") == 2,
    "Omnivore catalog count failed");
Require(DietCoachLogic.Targets.Length == 5, "Diet target count failed");

Require(DietCoachLogic.NormalizeNutrient(-7) == DietCoachLogic.Empty
        && DietCoachLogic.NormalizeNutrient(99) == DietCoachLogic.Lipids,
    "Nutrient normalization failed");
Require(DietCoachLogic.NormalizeSpeciesIndex(-1) == 0
        && DietCoachLogic.NormalizeSpeciesIndex(999) == 21,
    "Species normalization failed");

var expectedCombos = new Dictionary<string, (int[] Slots, string Label, int Growth)>
{
    ["P+C+L"] = ([1, 2, 3], "PERFECT BALANCE", 100),
    ["C+C+C"] = ([2, 2, 2], "TRAVEL FOCUS", 30),
    ["C+C+L"] = ([2, 2, 3], "ENDURANCE", 50),
    ["P+C+C"] = ([1, 2, 2], "MOBILITY RECOVERY", 50),
    ["L+L+L"] = ([3, 3, 3], "AWARENESS", 30),
    ["C+L+L"] = ([2, 3, 3], "SCOUT", 50),
    ["P+L+L"] = ([1, 3, 3], "SURVIVOR", 50),
    ["P+P+P"] = ([1, 1, 1], "VITALITY", 30),
    ["P+P+C"] = ([1, 1, 2], "RECOVERY", 50),
    ["P+P+L"] = ([1, 1, 3], "NESTING", 50)
};
foreach (var (key, expected) in expectedCombos)
{
    var result = DietCoachLogic.Analyze(expected.Slots[0], expected.Slots[1], expected.Slots[2], 0);
    Require(result.Key == key && result.Label == expected.Label && result.GrowthBonus == expected.Growth,
        $"Combination mapping failed for {key}");
}

var empty = DietCoachLogic.Analyze(0, 0, 0, 0);
Require(empty.FilledCount == 0 && empty.NeededNutrient == DietCoachLogic.Protein,
    "Empty balanced guidance failed");
var twoSlots = DietCoachLogic.Analyze(1, 2, 0, 0);
Require(twoSlots.FilledCount == 2 && twoSlots.NeededNutrient == DietCoachLogic.Lipids,
    "Partial balanced guidance failed");
var balanced = DietCoachLogic.Analyze(3, 1, 2, 0);
Require(balanced.MatchesTarget && balanced.NeededNutrient == DietCoachLogic.Empty,
    "Order-independent balanced match failed");
var replacement = DietCoachLogic.Analyze(1, 1, 2, 0);
Require(replacement.NeededNutrient == DietCoachLogic.Lipids
        && replacement.ReplaceNutrient == DietCoachLogic.Protein,
    "Target replacement guidance failed");

var alloIndex = Array.FindIndex(DietCoachLogic.Species, entry => entry.Id == "allosaurus") + 1;
Require(DietCoachLogic.FoodForNutrient(alloIndex, DietCoachLogic.Protein).Contains("Stegosaurus"),
    "Carnivore food lookup failed");
var beipiIndex = Array.FindIndex(DietCoachLogic.Species, entry => entry.Id == "beipiaosaurus") + 1;
Require(DietCoachLogic.FoodForNutrient(beipiIndex, DietCoachLogic.Lipids).Contains("Bullfrog"),
    "Omnivore food lookup failed");
var tenoIndex = Array.FindIndex(DietCoachLogic.Species, entry => entry.Id == "tenontosaurus") + 1;
Require(DietCoachLogic.FoodForNutrient(tenoIndex, DietCoachLogic.Carbs).Contains("Migration zones"),
    "Migration-driven herbivore guidance failed");
Require(DietCoachLogic.FoodForNutrient(0, DietCoachLogic.Protein).StartsWith("Choose a species"),
    "Unknown species guard failed");

Console.WriteLine("Diet coach: PASS (21 playables, 10 combinations, targets, guidance, and food lookup)");
