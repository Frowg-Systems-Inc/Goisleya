namespace Isley;

internal sealed record MutationCatalogEntry(
    string Id,
    string Name,
    string Group,
    string Restrictions,
    string Effect,
    string Unlock,
    string Tags);

internal readonly record struct MutationLoadoutItem(int Slot, string MutationId, int Status);

internal static class MutationPlannerLogic
{
    internal const int MaxLoadoutSize = 16;
    internal const string CatalogDate = "2026-07-03";

    internal static readonly MutationCatalogEntry[] Catalog =
    [
        new("accelerated-prey-drive", "Accelerated Prey Drive", "Lifecycle", "Carnivore · 10%", "More damage to animals below 35% health.", "", "combat offense hunt prey carnivore"),
        new("advanced-gestation", "Advanced Gestation", "Lifecycle", "Female only · 50%", "Faster egg gestation, incubation, and cooldown.", "", "nest nesting eggs parent reproduction female"),
        new("barometric-sensitivity", "Barometric Sensitivity", "Lifecycle", "Herbivore", "Indication before storms or droughts.", "", "weather storm drought awareness herbivore"),
        new("cannibalistic", "Cannibalistic", "Lifecycle", "Carnivore · slots 2 and 4", "Adds your own species as preferred prey for nutrients.", "", "diet food nutrients carnivore cannibal"),
        new("cellular-regeneration", "Cellular Regeneration", "Lifecycle", "15%", "Recover health faster.", "", "health heal regen survival defense"),
        new("congenital-hypoalgesia", "Congenital Hypoalgesia", "Lifecycle", "15%", "Reduce incoming damage against larger species.", "", "combat defense damage tank larger"),
        new("efficient-digestion", "Efficient Digestion", "Lifecycle", "20%", "Food drains more slowly.", "", "diet food hunger survival growth"),
        new("enlarged-meniscus", "Enlarged Meniscus", "Lifecycle", "", "Fall damage consumes stamina before health.", "", "fall travel defense stamina cliff"),
        new("epidermal-fibrosis", "Epidermal Fibrosis", "Lifecycle", "15%", "Increase bleed resistance.", "", "bleed defense combat survival"),
        new("featherweight", "Featherweight", "Lifecycle", "50%", "Footprints fade much faster.", "", "stealth tracks footprint escape"),
        new("hematophagy", "Hematophagy", "Lifecycle", "Carnivore · 15%", "Restore thirst while eating corpses.", "", "thirst water corpse carnivore diet"),
        new("hemomania", "Hemomania", "Lifecycle", "Carnivore · 5%", "Deal more damage to a bleeding target.", "", "combat offense bleed carnivore"),
        new("hydrodynamic", "Hydrodynamic", "Lifecycle", "15%", "Increase swimming speed.", "", "aquatic swim speed travel water"),
        new("hydro-regenerative", "Hydro-regenerative", "Lifecycle", "25%", "Recover health faster during rain.", "", "health heal rain weather regen"),
        new("hypervigilance", "Hypervigilance", "Lifecycle", "Herbivore · 50%", "Wider camera while feeding and louder enemy footsteps.", "", "awareness sound feeding herbivore safety"),
        new("increased-inspiratory-capacity", "Increased Inspiratory Capacity", "Lifecycle", "15%", "Increase oxygen capacity.", "", "oxygen aquatic dive stamina survival"),
        new("infrasound-communication", "Infrasound Communication", "Lifecycle", "50%", "Make less noise while using chat.", "", "stealth sound chat communication group"),
        new("nocturnal", "Nocturnal", "Lifecycle", "5%", "Faster health and locked-health recovery at night.", "", "night health heal regen survival"),
        new("osteosclerosis", "Osteosclerosis", "Lifecycle", "20%", "Resist or reduce fracture damage.", "", "fracture bone defense combat survival"),
        new("photosynthetic-regeneration", "Photosynthetic Regeneration", "Lifecycle", "Herbivore · 10%", "Regenerate stamina faster during the day.", "", "day stamina regen herbivore travel"),
        new("photosynthetic-tissue", "Photosynthetic Tissue", "Lifecycle", "5%", "Faster health and locked-health recovery during the day.", "", "day health heal regen survival"),
        new("reabsorption", "Reabsorption", "Lifecycle", "", "Recover water during rain or in drinkable water.", "", "water thirst rain swim survival"),
        new("sequential-hermaphroditism", "Sequential Hermaphroditism", "Lifecycle", "Not inherited", "Change sex.", "", "sex nesting reproduction lineage"),
        new("social-behavior", "Social Behavior", "Lifecycle", "Herbivore or omnivore · group leader", "Increase group size.", "", "pack herd group leader social"),
        new("submerged-optical-retention", "Submerged Optical Retention", "Lifecycle", "5%", "Increase underwater vision range.", "", "aquatic underwater vision swim"),
        new("sustained-hydration", "Sustained Hydration", "Lifecycle", "20%", "Water drains more slowly.", "", "water thirst survival travel"),
        new("truculency", "Truculency", "Lifecycle", "Herbivore · 5%", "Bucking is more likely to dismount attackers.", "", "combat defense pounce buck herbivore"),
        new("wader", "Wader", "Lifecycle", "25%", "Move more effectively through shallow water.", "", "water wade travel speed aquatic"),
        new("xerocole-adaptation", "Xerocole Adaptation", "Lifecycle", "Herbivore · 15%", "Gain water while eating plants.", "", "water thirst plants herbivore diet"),
        new("tactile-endurance", "Tactile Endurance", "Slot 2 exclusive", "Herbivore", "Convert incoming damage to stamina.", "", "combat defense stamina herbivore tank"),
        new("gastronomic-regeneration", "Gastronomic Regeneration", "Slot 2 exclusive", "", "Eating restores health.", "", "food health heal diet regen"),
        new("hypermetabolic-inanition", "Hypermetabolic Inanition", "Slot 2 exclusive", "Carnivore", "Deal more damage as hunger falls.", "", "combat offense hunger carnivore risk"),
        new("augmented-tapetum", "Augmented Tapetum", "Unlockable", "Carnivore · slot 2", "Increase night vision.", "Kill 5 players at night.", "night vision carnivore unlock combat"),
        new("enhanced-digestion", "Enhanced Digestion", "Unlockable", "Slots 2 and 3", "Decrease nutrition decay.", "Maintain nutrients for 60 minutes.", "diet nutrients growth unlock food"),
        new("heightened-ghrelin", "Heightened Ghrelin", "Unlockable", "Slot 2", "Increase overeating capacity.", "Keep hunger above 80% for 30 minutes.", "hunger food diet unlock capacity"),
        new("multichambered-lungs", "Multichambered Lungs", "Unlockable", "Slots 2 and 3", "Increase the stamina-regeneration threshold.", "Drain 4,500 stamina by sprinting or fast-swimming.", "stamina sprint swim travel unlock endurance"),
        new("osteophagic", "Osteophagic", "Unlockable", "Carnivore", "Eat bones to regenerate fractures faster.", "Eat bones while a bone is broken.", "bone fracture heal carnivore unlock diet"),
        new("parthenogenesis", "Parthenogenesis", "Unlockable", "Female only · slot 2 · not inherited", "Nest without a mate.", "Available on slot 2.", "nest nesting solo reproduction female unlock"),
        new("prolific-reproduction", "Prolific Reproduction", "Unlockable", "Female only · slot 2", "Young grow faster and need less food with stronger regeneration.", "Available on slot 2.", "nest young parent growth reproduction female"),
        new("reinforced-tendons", "Reinforced Tendons", "Unlockable", "", "Jumping costs less stamina; lowers Pteranodon takeoff cost.", "Jump 50 times.", "jump stamina flight pteranodon travel unlock"),
        new("reniculate-kidneys", "Reniculate Kidneys", "Unlockable", "Slots 2 and 3", "Drink saltwater.", "Lose 1,250 thirst by drinking saltwater.", "water thirst salt ocean aquatic unlock")
    ];

    internal static MutationCatalogEntry? FindById(string? id) =>
        Catalog.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));

    internal static IReadOnlyList<MutationCatalogEntry> Search(string? query, int limit = 6)
    {
        var normalized = (query ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length < 2 || limit < 1) return [];
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Catalog
            .Select((entry, index) => new { Entry = entry, Index = index, Score = SearchScore(entry, normalized, tokens) })
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Index)
            .Take(limit)
            .Select(match => match.Entry)
            .ToArray();
    }

    private static int SearchScore(MutationCatalogEntry entry, string query, string[] tokens)
    {
        var name = entry.Name.ToLowerInvariant();
        var tags = entry.Tags.ToLowerInvariant();
        var restrictions = entry.Restrictions.ToLowerInvariant();
        var effect = entry.Effect.ToLowerInvariant();
        var unlock = entry.Unlock.ToLowerInvariant();
        var group = entry.Group.ToLowerInvariant();
        var score = 0;
        if (name == query) score += 1200;
        if (name.StartsWith(query, StringComparison.Ordinal)) score += 700;
        if (name.Contains(query, StringComparison.Ordinal)) score += 450;
        if (tags.Contains(query, StringComparison.Ordinal)) score += 260;
        if (restrictions.Contains(query, StringComparison.Ordinal)) score += 150;
        if (effect.Contains(query, StringComparison.Ordinal)) score += 120;
        if (unlock.Contains(query, StringComparison.Ordinal)) score += 120;
        if (group.Contains(query, StringComparison.Ordinal)) score += 100;
        foreach (var token in tokens)
        {
            if (name.Split(' ').Any(word => word.StartsWith(token, StringComparison.Ordinal))) score += 90;
            if (tags.Split(' ').Any(word => word.StartsWith(token, StringComparison.Ordinal))) score += 45;
            if (restrictions.Contains(token, StringComparison.Ordinal)) score += 35;
            if (effect.Contains(token, StringComparison.Ordinal)) score += 25;
            if (unlock.Contains(token, StringComparison.Ordinal)) score += 25;
        }
        return score;
    }

    internal static IReadOnlyList<MutationLoadoutItem> NormalizeLoadout(IEnumerable<MutationLoadoutItem>? items)
    {
        if (items is null) return [];
        var slots = new HashSet<int>();
        var mutations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<MutationLoadoutItem>();
        foreach (var item in items.OrderBy(item => item.Slot))
        {
            var mutation = FindById(item.MutationId);
            if (item.Slot is < 1 or > MaxLoadoutSize
                || mutation is null
                || !AllowedSlots(mutation).Contains(item.Slot)
                || !slots.Add(item.Slot)
                || !mutations.Add(item.MutationId))
            {
                continue;
            }
            normalized.Add(item with { Status = Math.Clamp(item.Status, 0, 2) });
        }
        return normalized;
    }

    internal static IReadOnlyList<int> AllowedSlots(MutationCatalogEntry mutation)
    {
        if (string.Equals(mutation.Group, "Slot 2 exclusive", StringComparison.OrdinalIgnoreCase))
        {
            return [2];
        }

        var restrictions = mutation.Restrictions.ToLowerInvariant();
        if (restrictions.Contains("slots 2 and 4", StringComparison.Ordinal)) return [2, 4];
        if (restrictions.Contains("slots 2 and 3", StringComparison.Ordinal)) return [2, 3];
        if (restrictions.Contains("slot 2", StringComparison.Ordinal)) return [2];
        return Enumerable.Range(1, MaxLoadoutSize).ToArray();
    }

    internal static int NextFreeSlotForMutation(
        IEnumerable<MutationLoadoutItem> items,
        MutationCatalogEntry mutation)
    {
        var occupied = items.Select(item => item.Slot).ToHashSet();
        return AllowedSlots(mutation).FirstOrDefault(slot => !occupied.Contains(slot));
    }

    internal static string AllowedSlotLabel(MutationCatalogEntry mutation)
    {
        var slots = AllowedSlots(mutation);
        return slots.Count == MaxLoadoutSize
            ? "ANY SLOT"
            : string.Join('/', slots.Select(slot => $"S{slot}"));
    }

    internal static int NextFreeSlot(IEnumerable<MutationLoadoutItem> items)
    {
        var occupied = items.Select(item => item.Slot).ToHashSet();
        for (var slot = 1; slot <= MaxLoadoutSize; slot++)
        {
            if (!occupied.Contains(slot)) return slot;
        }
        return 0;
    }

    internal static int EquippedCount(IEnumerable<MutationLoadoutItem> items) =>
        items.Count(item => item.Status is 1 or 2);

    internal static string StatusLabel(int status) => status switch
    {
        1 => "ACTIVE",
        2 => "CARRIED",
        _ => "PLANNED"
    };
}
