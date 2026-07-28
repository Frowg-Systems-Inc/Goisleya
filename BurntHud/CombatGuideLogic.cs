namespace Isley;

internal readonly record struct CombatBriefEntry(
    string Id,
    string DamageStyle,
    string Signature,
    string Positioning,
    string AbortCondition,
    string[] Keywords);

internal static class CombatGuideLogic
{
    internal const string SnapshotDate = "2026-07-09";
    internal const string PublicBranch = IsleContentBaseline.PublicBranch;

    internal static readonly CombatBriefEntry[] Briefs =
    [
        Entry("allosaurus", "BLEED / RAW · HEAVY POUNCER",
            "Commit from cover, use the grapple to control the size matchup, then convert the opening into bleed pressure.",
            "Work a flank with a clear reset lane; recover stamina before a second commitment.",
            "Break off after a missed grapple, group focus, or any approach that leaves no exit.",
            "grapple pounce pull bleed heavyweight flank ambush"),
        Entry("carnotaurus", "RAW / KNOCKDOWN · CHARGER",
            "Build a straight charge lane, land the knockdown, and use the short opening instead of circling endlessly.",
            "Keep open ground behind the target and enough space to accelerate and leave.",
            "Abort in tight terrain, after a missed charge, or before stamina removes your speed advantage.",
            "charge ram knockdown straight lane burst sprint"),
        Entry("ceratosaurus", "BACTERIA / RAW · BRAWLER",
            "Use repeated bite pressure and carcass control to make longer engagements increasingly expensive.",
            "Hold close terrain around food without giving a larger opponent a clean face trade.",
            "Disengage when the opponent can out-damage the bacteria plan before it has time to matter.",
            "bacteria bite carcass pressure brawler attrition"),
        Entry("deinosuchus", "RAW / GRAB · AQUATIC AMBUSHER",
            "Stay concealed and turn a shoreline drinking window into one decisive lunge or grab.",
            "Fight from deep-water cover with a submerged retreat already selected.",
            "Release the plan when forced onto land or when stamina and oxygen cannot support the return trip.",
            "water lunge grab shoreline ambush oxygen aquatic"),
        Entry("dilophosaurus", "VENOM / BLEED · NIGHT SKIRMISHER",
            "Use darkness, venom pressure, and repeated disengages; force the target to chase false openings.",
            "Stay at the edge of sight and rotate around cover instead of accepting a direct trade.",
            "Leave in daylight, against a heavier clean engager, or when the reset route is exposed.",
            "night venom bleed hallucination skirmish darkness"),
        Entry("herrerasaurus", "RAW / BLEED · VERTICAL POUNCER",
            "Climb, verify the landing, and turn a drop-pounce into one clean damage window.",
            "Use trees, walls, and height so the target must expose itself to reach you.",
            "Abort after a missed drop or whenever open ground removes the vertical escape.",
            "tree climb latch drop pounce vertical ambush"),
        Entry("omniraptor", "BLEED / RAW · PACK POUNCER",
            "Split latch slots across the pack, apply bleed, and rotate before bucking drains the attacker.",
            "Approach from different angles and keep one packmate free to punish the defender.",
            "Dismount before stamina empties; do not convert a failed team pounce into a solo face trade.",
            "pack pounce latch slot buck bleed rotate"),
        Entry("pteranodon", "PECK / ESCAPE · AERIAL SCOUT",
            "Use G for the current spearfishing bind and reserve attacks for fish or genuinely small exposed targets.",
            "Land into wind with a visible takeoff lane and enough stamina to leave immediately.",
            "Take off before a grounded feeding or drinking window becomes a committed fight.",
            "flight aerial spearfish g peck takeoff stamina"),
        Entry("troodon", "VENOM · PACK SKIRMISHER",
            "Stagger venom contacts across the pack, build stages, then commit together only at the agreed window.",
            "Stay outside the target's turn arc and keep calls short enough to preserve timing.",
            "Break contact after a mistimed latch, synchronized bite reset, or loss of pack spacing.",
            "venom stage pack pounce stagger calls timing"),
        Entry("tyrannosaurus", "FRACTURE / RAW · HEAVY FINISHER",
            "Use cover and the Space ambush burst to land Hold-LMB Crush; use RMB swings to control smaller threats.",
            "Face the target, protect the flanks, and make the first heavy hit decide the engagement.",
            "Never turn the ambush into a long pursuit; reset when the first fracture or pin does not land.",
            "rex crush fracture hold lmb rmb muzzle swing space ambush"),
        Entry("diabloceratops", "RAW / STAGGER · HORN BRAWLER",
            "Keep the horns forward, use controlled charges, and punish anything entering the herd's front arc.",
            "Anchor beside allies with enough room to turn as a unit instead of chasing alone.",
            "Back out when surrounded or when terrain lets attackers stay behind the horns.",
            "horn charge stagger herd front arc brawler"),
        Entry("dryosaurus", "EVADE · ESCAPE SPECIALIST",
            "Win through direction changes and prepared cover transitions, not damage races.",
            "Keep two escape branches visible and force pursuers to guess at every obstacle.",
            "Leave before stamina, a fracture, or a straight corridor removes the next turn.",
            "evade dodge escape cover turn small survivor"),
        Entry("hypsilophodon", "BLIND / DISRUPT · ESCAPE SKIRMISHER",
            "Blind a committed pursuer, use wall-latch or terrain changes, and create distance instead of chasing damage.",
            "Fight around clutter and height where larger bodies cannot hold a clean line.",
            "Escape as soon as the blind window opens; open ground makes a second exchange unsafe.",
            "blind spit acid wall latch pounce disrupt escape"),
        Entry("kentrosaurus", "RAW / REFLECT · SPIKED DEFENDER",
            "Hold RMB to charge Power Swing, combine RMB+LMB to release it, and use Ctrl for Defensive Stance.",
            "Present the tail and shoulder spikes; let reflection punish attacks into the defended arc.",
            "Do not chase out of stance or expose the unprotected front while the group is repositioning.",
            "kentro power swing rmb lmb ctrl defensive stance reflect spikes"),
        Entry("maiasaura", "RAW / MOBILITY · HERD BRAWLER",
            "Use body presence and group movement to punish small attackers without abandoning herd speed.",
            "Fight on the herd edge with an ally covering the opposite side and a travel lane behind you.",
            "Leave when isolated, stamina-gated, or pulled away from the herd's overlapping protection.",
            "maia herd mobility body pressure brawler group"),
        Entry("pachycephalosaurus", "FRACTURE / STAGGER · RAM SKIRMISHER",
            "Create a clean lane, land the headbutt or ram, and exploit the fracture without overcommitting.",
            "Use firm footing and short angles that let you miss safely and turn out.",
            "Abort after a missed ram, poor footing, or any angle that gives a heavyweight a free trade.",
            "pachy headbutt ram fracture stagger lane footing"),
        Entry("stegosaurus", "RAW / BLEED · REAR-ARC DEFENDER",
            "Make the thagomizer zone the only path to you and punish a committed approach with the tail.",
            "Back toward terrain or allies so predators cannot split the front from the tail arc.",
            "Reposition instead of pursuing when the target refuses the defensive zone.",
            "stego tail thagomizer rear arc bleed defense"),
        Entry("tenontosaurus", "RAW / STAGGER · VERSATILE DEFENDER",
            "Match the tool to the angle: bite or claw in front, rear kick and tail slam behind.",
            "Trot for the tighter turn, keep stamina for the counter, and deny a clean rear latch.",
            "Disengage when stamina loss turns four useful attacks into one predictable panic option.",
            "teno bite claw rear kick tail slam stagger turn"),
        Entry("triceratops", "RAW / STAGGER · FRONT-ARC TANK",
            "Use horn pressure and the stomp window to make direct approaches prohibitively expensive.",
            "Keep the threat in front and let herd mates close the rear access.",
            "Reset when attackers split your facing or stamina attrition prevents another turn.",
            "trike horn stomp tank front arc herd stagger"),
        Entry("beipiaosaurus", "RAW / ESCAPE · WATER SKIRMISHER",
            "Use the claws against small threats, then convert water agility into the disengage.",
            "Stay within one burst of a known river exit while keeping Deinosuchus sightlines in mind.",
            "Leave land combat against larger animals and abandon water that is controlled by an aquatic apex.",
            "beipi claws water swim breach escape aquatic"),
        Entry("gallimimus", "STAGGER / ESCAPE · FLOCK SCOUT",
            "Use the dash kick only on a safe pass and use the mobilization call to move the flock, not start a brawl.",
            "Keep speed, stamina, and a wide turn lane; report danger before it reaches the herd.",
            "Flee before a fracture or ambush removes the speed advantage that defines the matchup.",
            "galli dash kick mobilization flock scout speed stagger")
    ];

    internal static CombatBriefEntry? Find(string? id) =>
        Briefs.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase)) is { Id.Length: > 0 } found
            ? found
            : null;

    internal static string SearchText(string? id)
    {
        if (Find(id) is not { } entry) return string.Empty;
        return string.Join(' ',
            entry.DamageStyle,
            entry.Signature,
            entry.Positioning,
            entry.AbortCondition,
            string.Join(' ', entry.Keywords));
    }

    internal static string MutationSearchQuery(string? id)
    {
        if (Find(id) is not { } entry) return "combat";
        if (entry.DamageStyle.Contains("FRACTURE", StringComparison.Ordinal)) return "fracture";
        if (entry.DamageStyle.Contains("BLEED", StringComparison.Ordinal)) return "bleed";
        if (entry.Keywords.Contains("aquatic", StringComparer.OrdinalIgnoreCase)) return "aquatic";
        if (entry.Keywords.Contains("night", StringComparer.OrdinalIgnoreCase)) return "night";
        if (entry.Keywords.Contains("stamina", StringComparer.OrdinalIgnoreCase)
            || entry.Keywords.Contains("speed", StringComparer.OrdinalIgnoreCase)) return "stamina";
        return "combat";
    }

    private static CombatBriefEntry Entry(
        string id,
        string damageStyle,
        string signature,
        string positioning,
        string abortCondition,
        string keywords) =>
        new(
            id,
            damageStyle,
            signature,
            positioning,
            abortCondition,
            keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
