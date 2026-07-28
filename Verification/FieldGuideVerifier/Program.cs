using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Check(FieldGuideLogic.Species.Length == DietCoachLogic.Species.Length, "roster count mismatch");
Check(FieldGuideLogic.Species.Select(entry => entry.Id).SequenceEqual(
    DietCoachLogic.Species.Select(entry => entry.Id)), "roster identity mismatch");
Check(CombatGuideLogic.Briefs.Length == 21, "combat brief count");
Check(CombatGuideLogic.Briefs.Select(entry => entry.Id).SequenceEqual(
    FieldGuideLogic.Species.Select(entry => entry.Id)), "combat roster identity mismatch");
Check(CombatGuideLogic.Briefs.All(entry =>
    !string.IsNullOrWhiteSpace(entry.DamageStyle)
    && !string.IsNullOrWhiteSpace(entry.Signature)
    && !string.IsNullOrWhiteSpace(entry.Positioning)
    && !string.IsNullOrWhiteSpace(entry.AbortCondition)), "incomplete combat brief");
Check(CombatGuideLogic.Find("baryonyx") is null && CombatGuideLogic.Find("austroraptor") is null,
    "Hordetest or upcoming animals must not enter the public roster");
Check(FieldGuideLogic.Species.All(entry =>
    !string.IsNullOrWhiteSpace(entry.Role)
    && !string.IsNullOrWhiteSpace(entry.Identity)
    && !string.IsNullOrWhiteSpace(entry.SurvivalTip)
    && !string.IsNullOrWhiteSpace(entry.DangerTip)), "incomplete profile");
Check(FieldGuideLogic.EssentialControls.Length == 11, "control reference count");
Check(FieldGuideLogic.Search("tree", "all", [], 6).Single().Id == "herrerasaurus", "tree search");
Check(FieldGuideLogic.Search("aerial scout", "all", [], 6).Single().Id == "pteranodon", "role search");
Check(FieldGuideLogic.Search("venom", "carnivore", [], 6).Select(entry => entry.Id)
    .SequenceEqual(["dilophosaurus", "troodon"]), "venom search");
Check(FieldGuideLogic.Search("defensive stance reflect", "herbivore", [], 6).Single().Id == "kentrosaurus",
    "current Kentrosaurus combat search");
Check(FieldGuideLogic.Search("spearfish", "carnivore", [], 6).Single().Id == "pteranodon",
    "current Pteranodon combat search");
Check(FieldGuideLogic.Search("crush fracture", "carnivore", [], 6).First().Id == "tyrannosaurus",
    "Tyrannosaurus combat search");
Check(CombatGuideLogic.MutationSearchQuery("tyrannosaurus") == "fracture", "fracture mutation bridge");
Check(CombatGuideLogic.MutationSearchQuery("beipiaosaurus") == "aquatic", "aquatic mutation bridge");
Check(CombatGuideLogic.MutationSearchQuery("invalid") == "combat", "fallback mutation bridge");
Check(FieldGuideLogic.Search(string.Empty, "omnivore", [], 6).Count == 2, "diet filter");
Check(FieldGuideLogic.Search(string.Empty, "all", ["troodon"], 2).First().Id == "troodon", "favorite rank");
Check(FieldGuideLogic.NormalizeFavorites(["troodon", "invalid", "troodon"]).SequenceEqual(["troodon"]),
    "favorite normalization");
Check(FieldGuideLogic.DietSpeciesIndex("gallimimus") == DietCoachLogic.Species.Length, "diet index bridge");
Check(FieldGuideLogic.DietSpeciesIndex("invalid") == 0, "invalid diet index");

Console.WriteLine("Field guide verification passed (21 public profiles, combat briefs, search, filters, favorites, controls, and diet bridge).");
