namespace Isley;

internal readonly record struct FieldGuideSpeciesEntry(
    string Id,
    string Name,
    string DietClass,
    string Role,
    string Identity,
    string SurvivalTip,
    string DangerTip,
    string[] Keywords);

internal readonly record struct FieldGuideControlEntry(
    string Keys,
    string Action,
    string Note);

internal static class FieldGuideLogic
{
    internal const string Snapshot = "2026-07-03";

    internal static readonly FieldGuideSpeciesEntry[] Species =
    [
        Entry("allosaurus", "Versatile bruiser", "Bleed pressure and committed ambushes", "Heal, reset, and choose isolated targets instead of chasing forever", "Respect groups and turn fights; a failed commitment is expensive", "bleed ambush grapple latch mid tier"),
        Entry("carnotaurus", "Sprint charger", "Explosive straight-line pursuit and knockdown pressure", "Keep an escape lane and use speed in short decisive bursts", "Tight terrain and sharp turns take away your strongest advantage", "charge ram sprint speed pursuit knockdown"),
        Entry("ceratosaurus", "Carrion brawler", "Carcass control, bacteria pressure, and close-range bullying", "Fight around food and let sustained pressure do the work", "Do not donate free hits while forcing a face-to-face trade", "bacteria scavenger carcass iron stomach brawler"),
        Entry("deinosuchus", "Aquatic ambusher", "River control and sudden shoreline grabs", "Stay concealed, conserve stamina, and move between safe water systems", "Open land removes your initiative and makes every trip costly", "water aquatic river lunge grab crocodile ambush"),
        Entry("dilophosaurus", "Nocturnal hunter", "Darkness, venom pressure, and repeated disengages", "Use night vision and patience; force the target to make mistakes", "Daylight and direct trades favor heavier animals", "night nocturnal venom hallucination vision stalk"),
        Entry("herrerasaurus", "Vertical ambusher", "Tree mobility and drop attacks from unexpected angles", "Plan the climb and the landing before revealing yourself", "Open ground leaves little margin when the first attack misses", "tree climb pounce drop vertical ambush small"),
        Entry("omniraptor", "Pack pouncer", "Coordinated latches, bleed, and target isolation", "Rotate attackers and preserve stamina instead of stacking one bad pounce", "Solo trades and predictable approaches erase the pack advantage", "raptor pack pounce grapple latch bleed omni"),
        Entry("pteranodon", "Aerial scout", "Fast reconnaissance, fishing, and terrain-safe travel", "Land with a departure route and keep enough stamina to take off", "Grounded feeding and drinking windows are your real danger", "fly flight aerial scout fish coast fragile"),
        Entry("troodon", "Venom pack hunter", "Small-target harassment and staged venom pounces", "Coordinate calls and reset cleanly between pounces", "One mistimed latch near a larger animal can end the life", "venom night pounce pack tiny calls"),
        Entry("tyrannosaurus", "Apex finisher", "Ambush, fracture pressure, and punishing committed bites", "Let cover and patience close the distance for you", "Long pursuits expose stamina limits and broadcast your route", "rex apex crush fracture ambush heavy"),
        Entry("diabloceratops", "Mobile horn fighter", "Herd protection, sparring pressure, and controlled charges", "Keep threats in front and preserve room for the herd to move", "Being surrounded removes the value of your forward defense", "diablo ceratopsian horn charge herd spar"),
        Entry("dryosaurus", "Evasive forager", "Small profile, fast direction changes, and escape routing", "Know the next patch of cover before leaving the current one", "Straight lines make a small survivor easy to read", "small evade dodge forager escape agile"),
        Entry("hypsilophodon", "Tiny disruptor", "Evasion and blinding pressure against careless pursuers", "Use clutter and height changes; survival matters more than damage", "Open terrain and long exposure remove your tricks", "hypsi blind spit glide climb small disrupt"),
        Entry("kentrosaurus", "Spiked defender", "Close-range deterrence and punishing flank access", "Make opponents enter your defensive arc on your terms", "Chasing turns a defensive body plan into an exposed one", "kentro spikes tail defense shoulder"),
        Entry("maiasaura", "Herd runner", "Group mobility, body presence, and sustained travel", "Stay with the herd and make every rest stop defensible", "Isolation gives ambushers the clean fight they want", "maia herd runner hadrosaur group speed"),
        Entry("pachycephalosaurus", "Fracture skirmisher", "Precise rams, limb pressure, and terrain disruption", "Create a clean lane before committing to a charge", "Missed rams and poor footing hand the tempo away", "pachy ram fracture headbutt skirmish"),
        Entry("stegosaurus", "Tail-zone defender", "Area denial and devastating rear-arc punishment", "Control spacing and force threats to enter the tail zone", "Blind pursuit and boxed terrain can expose the front and flanks", "stego tail thagomizer defense heavy"),
        Entry("tenontosaurus", "Versatile herd fighter", "Kick, claw, bite, and tail options across many ranges", "Change tools with the angle instead of repeating one attack", "Stamina loss and tunnel vision turn versatility into panic", "teno kick claw tail stun versatile herd"),
        Entry("triceratops", "Ceratopsian tank", "Forward pressure, herd anchoring, and heavy punishment", "Face the threat and let allies use the space you create", "Rear access and stamina attrition are the openings predators need", "trike triceratops horn tank herd spar heavy"),
        Entry("beipiaosaurus", "Semi-aquatic forager", "Water escape routes, mixed food, and agile slashing defense", "Chain shallow-water cover with nearby food instead of crossing open ground", "Large aquatic threats can turn the escape route into a trap", "beipi water swim semi aquatic omnivore claw"),
        Entry("gallimimus", "High-speed scout", "Long-range travel, flock awareness, and rapid disengagement", "Keep moving, report danger early, and never spend all your stamina", "Ambushes and fractures remove the speed that keeps you alive", "galli speed scout flock kick stamina omnivore")
    ];

    internal static readonly FieldGuideControlEntry[] EssentialControls =
    [
        new("WASD / SHIFT", "Move / sprint", "Use Z for the slower, tighter-turning gait"),
        new("Q HOLD", "Scent", "Longer holds reveal more survival information"),
        new("Q TAP", "Compass", "Check direction without opening another surface"),
        new("LMB / RMB", "Primary attacks", "Alt modifiers unlock species-specific alternatives"),
        new("E", "Use", "Eat, drink, and interact with world objects"),
        new("G TAP / HOLD", "Chunk / carry", "Tear food or move a carried object"),
        new("H / HOLD H", "Rest / safe logout", "Hold until the logout completes before leaving"),
        new("1 / 2 / 3 / 4", "Calls", "Broadcast, friendly, threat, and help"),
        new("X", "Night vision", "Use only when darkness actually limits sight"),
        new("N / B", "Court / build nest", "Requirements and species behavior can differ"),
        new("F", "Vocalize", "Contextual call also used while typing")
    ];

    internal static FieldGuideSpeciesEntry? Find(string? id) =>
        Species.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase)) is { Id.Length: > 0 } found
            ? found
            : null;

    internal static string NormalizeDietFilter(string? filter) =>
        filter?.Trim().ToLowerInvariant() switch
        {
            "carnivore" => "carnivore",
            "herbivore" => "herbivore",
            "omnivore" => "omnivore",
            _ => "all"
        };

    internal static IReadOnlyList<FieldGuideSpeciesEntry> Search(
        string? query,
        string? dietFilter,
        IEnumerable<string>? favoriteIds,
        int limit = 6)
    {
        var normalizedQuery = string.Join(' ', (query ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var filter = NormalizeDietFilter(dietFilter);
        var favorites = NormalizeFavorites(favoriteIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return Species
            .Where(entry => filter == "all"
                            || string.Equals(entry.DietClass, filter, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new { Entry = entry, Score = Score(entry, normalizedQuery, tokens, favorites) })
            .Where(candidate => candidate.Score >= 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, Species.Length))
            .Select(candidate => candidate.Entry)
            .ToList();
    }

    internal static IReadOnlyList<string> NormalizeFavorites(IEnumerable<string>? ids) =>
        (ids ?? [])
            .Where(id => Find(id) is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

    internal static int DietSpeciesIndex(string? id)
    {
        var index = Array.FindIndex(
            DietCoachLogic.Species,
            species => string.Equals(species.Id, id, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 0 : index + 1;
    }

    private static FieldGuideSpeciesEntry Entry(
        string id,
        string role,
        string identity,
        string survivalTip,
        string dangerTip,
        string keywords)
    {
        var diet = DietCoachLogic.Species.First(species => species.Id == id);
        return new FieldGuideSpeciesEntry(
            id,
            diet.Name,
            diet.DietClass,
            role,
            identity,
            survivalTip,
            dangerTip,
            keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static int Score(
        FieldGuideSpeciesEntry entry,
        string query,
        IReadOnlyList<string> tokens,
        IReadOnlySet<string> favorites)
    {
        var favoriteScore = favorites.Contains(entry.Id) ? 120 : 0;
        if (string.IsNullOrEmpty(query))
        {
            return favoriteScore;
        }

        var name = entry.Name.ToLowerInvariant();
        var searchable = string.Join(' ', new[]
        {
            name,
            entry.DietClass,
            entry.Role,
            entry.Identity,
            entry.SurvivalTip,
            entry.DangerTip,
            CombatGuideLogic.SearchText(entry.Id),
            string.Join(' ', entry.Keywords)
        }).ToLowerInvariant();
        if (tokens.Any(token => !searchable.Contains(token, StringComparison.Ordinal)))
        {
            return -1;
        }

        var score = 30 + favoriteScore;
        if (name == query) score += 500;
        else if (name.StartsWith(query, StringComparison.Ordinal)) score += 260;
        else if (name.Contains(query, StringComparison.Ordinal)) score += 160;
        score += tokens.Count(token => name.Split(' ').Any(word => word.StartsWith(token, StringComparison.Ordinal))) * 35;
        return score;
    }
}
