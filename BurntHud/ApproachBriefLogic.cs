namespace Isley;

internal enum ApproachBriefTone
{
    Neutral,
    Active,
    Warning
}

internal readonly record struct ApproachBriefSnapshot(
    bool StreamerMode,
    bool WaypointActive,
    double? Distance,
    string DestinationKind,
    string SpeciesId,
    bool MovingAway);

internal readonly record struct ApproachBriefView(
    bool Visible,
    string Key,
    string Kind,
    string Heading,
    string HudLine,
    string Detail,
    string ActionId,
    string ActionLabel,
    int Urgency,
    ApproachBriefTone Tone)
{
    internal static ApproachBriefView Hidden => new(
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        0,
        ApproachBriefTone.Neutral);
}

internal static class ApproachBriefLogic
{
    internal static readonly string[] DestinationKinds =
    [
        "safe", "nest", "food", "danger", "water", "rally", "death",
        "friend", "pack", "salt", "mud", "gastrolith", "resource",
        "estimate", "escape", "recovery"
    ];

    private static readonly HashSet<string> Carnivores =
    [
        "allosaurus", "carnotaurus", "ceratosaurus", "deinosuchus",
        "dilophosaurus", "herrerasaurus", "omniraptor", "pteranodon",
        "troodon", "tyrannosaurus"
    ];

    internal static string NormalizeKind(string? value)
    {
        var normalized = new string((value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Where(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            .Take(24)
            .ToArray());
        return DestinationKinds.Contains(normalized, StringComparer.Ordinal)
            ? normalized
            : string.Empty;
    }

    internal static ApproachBriefView Evaluate(ApproachBriefSnapshot raw)
    {
        if (raw.StreamerMode || !raw.WaypointActive
            || raw.Distance is null || !double.IsFinite(raw.Distance.Value)
            || raw.Distance.Value < 0)
        {
            return ApproachBriefView.Hidden;
        }

        var kind = NormalizeKind(raw.DestinationKind);
        if (string.IsNullOrEmpty(kind)) return ApproachBriefView.Hidden;

        var distance = raw.Distance.Value;
        var threshold = ApproachRadius(kind);
        if (distance > threshold) return ApproachBriefView.Hidden;

        var final = distance <= FinalRadius(kind);
        var range = $"{distance:0} MU";
        var species = NormalizeSpecies(raw.SpeciesId);
        var movingAwaySuffix = raw.MovingAway
            ? " The destination is currently opening; correct course first."
            : string.Empty;

        return kind switch
        {
            "danger" or "death" => Build(
                kind,
                final,
                "THREAT APPROACH",
                final ? "STOP SHORT · VERIFY THE AREA" : "THREAT MAY REMAIN · USE COVER",
                $"{range} to a personal {KindLabel(kind)} marker. Slow down, approach from cover, and verify the area in game; the original threat may remain.{movingAwaySuffix}",
                2,
                ApproachBriefTone.Warning),
            "water" => WaterBrief(kind, final, range, species, movingAwaySuffix),
            "nest" => Build(
                kind,
                final,
                "NEST APPROACH",
                final ? "ARRIVE QUIET · VERIFY THE SITE" : "AVOID OPEN GROUND · SCAN FIRST",
                $"{range} to the saved nest. Use concealment, check the surrounding approach and reserves, then verify the nest site in game.{movingAwaySuffix}",
                1,
                ApproachBriefTone.Active),
            "food" => FoodBrief(kind, final, range, species, movingAwaySuffix),
            "safe" => Build(
                kind,
                final,
                "SAFE PIN CHECK",
                final ? "LISTEN · VERIFY COVER · THEN STOP" : "SCAN COVER · KEEP AN EXIT",
                $"{range} to a personal Safe marker. Recheck sightlines, sound, and an exit in game before resting or logging out; a saved pin is not a live safety guarantee.{movingAwaySuffix}",
                1,
                ApproachBriefTone.Active),
            "rally" or "friend" or "pack" => Build(
                kind,
                final,
                "RALLY APPROACH",
                final ? "IDENTIFY FRIENDS · CLOSE QUIETLY" : "CHECK THE GROUP · AVOID OPEN CALLS",
                $"{range} to the authorized rally target. Confirm friend markers and identify the group before exposing your position in game.{movingAwaySuffix}",
                1,
                ApproachBriefTone.Active),
            "salt" => Build(
                kind,
                final,
                "SALT LICK APPROACH",
                final ? "VERIFY SITE · USE ONLY IF NEEDED" : "SCAN THE SITE · EXPECT NUTRIENT DRAIN",
                $"{range} to a public Salt Lick site. Check for players and verify the lick in game; salt can trade or drain an active nutrient.{movingAwaySuffix}",
                1,
                ApproachBriefTone.Active),
            "mud" => Build(
                kind,
                final,
                "MUD WALLOW APPROACH",
                final ? "VERIFY THE WALLOW · KEEP AN EXIT" : "SCAN WATERLINE · APPROACH WITH STAMINA",
                $"{range} to a public Mud Wallow site. Scan the waterline and cover, then verify the wallow is present and usable in game.{movingAwaySuffix}",
                1,
                ApproachBriefTone.Active),
            "gastrolith" => Build(
                kind,
                final,
                "GASTROLITH APPROACH",
                final ? "VERIFY THE STONE · CHECK COVER" : "SCAN THE SITE · DO NOT TUNNEL",
                $"{range} to a public Gastrolith site. Verify the stone and nearby threat picture in game before committing.{movingAwaySuffix}",
                1,
                ApproachBriefTone.Active),
            "resource" => Build(
                kind,
                final,
                "RESOURCE APPROACH",
                final ? "VERIFY THE SITE · CHECK COVER" : "STATIC SITE · SCAN BEFORE COMMITTING",
                $"{range} to a public static resource point. Availability is not live; verify the resource, scent, and surroundings in game.{movingAwaySuffix}",
                1,
                ApproachBriefTone.Active),
            "estimate" => Build(
                kind,
                final,
                "ESTIMATE APPROACH",
                final ? "STOP · RECHECK THE CUE" : "SEARCH THE AREA · DO NOT ASSUME",
                $"{range} to the two-bearing estimate. Reacquire the sound or scent in game; Isley did not detect or identify the source.{movingAwaySuffix}",
                1,
                ApproachBriefTone.Active),
            "escape" => Build(
                kind,
                final,
                "ESCAPE LEG END",
                final ? "BREAK SIGHTLINE · REASSESS NOW" : "KEEP MOVING · USE COVER",
                $"{range} remain on the bounded escape heading. Do not treat the endpoint as safe; break sightline and reassess contacts and terrain in game.{movingAwaySuffix}",
                2,
                ApproachBriefTone.Warning),
            "recovery" => Build(
                kind,
                final,
                "RECOVERY ANCHOR",
                final ? "REACQUIRE CONTEXT · VERIFY THE AREA" : "OLD POSITION · APPROACH CAUTIOUSLY",
                $"{range} to a locally remembered position. It is historical context, not a live player or safety signal; verify the area in game.{movingAwaySuffix}",
                1,
                ApproachBriefTone.Active),
            _ => ApproachBriefView.Hidden
        };
    }

    private static ApproachBriefView WaterBrief(
        string kind,
        bool final,
        string range,
        string species,
        string movingAwaySuffix)
    {
        if (species == "pteranodon")
        {
            return Build(
                kind,
                final,
                "WATER LANDING CHECK",
                final ? "CIRCLE ONCE · KEEP TAKEOFF ROOM" : "CHECK BANKS · CHOOSE A CLEAR LANDING",
                $"{range} to the water marker. Verify a clear landing and takeoff line in game, scan both banks, and keep enough stamina to leave.{movingAwaySuffix}",
                1,
                ApproachBriefTone.Active);
        }

        if (species is "deinosuchus" or "beipiaosaurus")
        {
            return Build(
                kind,
                final,
                "WATERLINE APPROACH",
                final ? "CHECK BOTH BANKS · KEEP AN EXIT" : "SCAN SHORE · VERIFY DEPTH",
                $"{range} to the water marker. Check both banks, depth, and the route back out before entering; verify the waterline in game.{movingAwaySuffix}",
                1,
                ApproachBriefTone.Active);
        }

        return Build(
            kind,
            final,
            "WATER APPROACH",
            final ? "STOP SHORT · LISTEN · VERIFY NOW" : "WALK · LISTEN · KEEP AN EXIT",
            $"{range} to the water marker. Stop short of the bank, scent and listen, scan both shores, and verify the waterline in game while keeping stamina for one exit.{movingAwaySuffix}",
            1,
            ApproachBriefTone.Active);
    }

    private static ApproachBriefView FoodBrief(
        string kind,
        bool final,
        string range,
        string species,
        string movingAwaySuffix)
    {
        if (species == "pteranodon")
        {
            return Build(
                kind,
                final,
                "FOOD LANDING CHECK",
                final ? "CIRCLE · VERIFY FOOD · LAND CLEAR" : "SCAN THE SITE · KEEP TAKEOFF ROOM",
                $"{range} to the food target. Circle the site, verify food with scent in game, and keep a clear landing and takeoff line.{movingAwaySuffix}",
                1,
                ApproachBriefTone.Active);
        }

        var carnivore = Carnivores.Contains(species);
        return Build(
            kind,
            final,
            "FOOD APPROACH",
            final
                ? carnivore ? "SCENT · CHECK CARCASS · DO NOT TUNNEL" : "SCENT · SCAN COVER · VERIFY FOOD"
                : carnivore ? "CHECK CONTEST · KEEP AN EXIT" : "SCAN COVER · APPROACH WITH STAMINA",
            carnivore
                ? $"{range} to the food target. Verify scent in game, check whether a carcass or prey site is contested, and keep an exit before feeding.{movingAwaySuffix}"
                : $"{range} to the food target. Verify the current plant with scent in game, scan cover, and keep an exit before feeding.{movingAwaySuffix}",
            1,
            ApproachBriefTone.Active);
    }

    private static ApproachBriefView Build(
        string kind,
        bool final,
        string heading,
        string hudLine,
        string detail,
        int urgency,
        ApproachBriefTone tone) => new(
            true,
            $"{kind}:{(final ? "final" : "approach")}",
            kind,
            heading,
            hudLine,
            detail,
            "routes",
            "OPEN ROUTE",
            Math.Clamp(urgency, 0, 2),
            tone);

    private static double ApproachRadius(string kind) => kind switch
    {
        "danger" or "death" => 60,
        "estimate" => 50,
        "water" or "nest" => 45,
        "food" or "salt" or "mud" or "gastrolith" or "resource" => 35,
        _ => 25
    };

    private static double FinalRadius(string kind) => kind switch
    {
        "danger" or "death" => 15,
        "water" or "nest" or "estimate" => 12,
        _ => 10
    };

    private static string KindLabel(string kind) => kind == "death" ? "Death" : "Danger";

    private static string NormalizeSpecies(string? value) => new((value ?? string.Empty)
        .Trim()
        .ToLowerInvariant()
        .Where(char.IsAsciiLetter)
        .Take(32)
        .ToArray());
}
