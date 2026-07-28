using Isley;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

ApproachBriefView Brief(
    string kind,
    double distance,
    string species = "carnotaurus",
    bool streamer = false,
    bool active = true,
    bool movingAway = false) =>
    ApproachBriefLogic.Evaluate(new ApproachBriefSnapshot(
        streamer,
        active,
        distance,
        kind,
        species,
        movingAway));

Require(!Brief("water", 20, streamer: true).Visible,
    "Streamer Mode must hide destination context");
Require(!Brief("water", 20, active: false).Visible,
    "An inactive route must not produce an approach brief");
Require(!Brief("unknown", 5).Visible && !Brief("water", 46).Visible,
    "Unknown destinations and out-of-range water routes must stay quiet");
Require(!ApproachBriefLogic.Evaluate(new ApproachBriefSnapshot(
        false, true, double.NaN, "water", "carnotaurus", false)).Visible,
    "Invalid distances must fail closed");

var danger = Brief("danger", 59);
Require(danger.Visible
        && danger.Urgency == 2
        && danger.Tone == ApproachBriefTone.Warning
        && danger.HudLine.Contains("THREAT MAY REMAIN", StringComparison.Ordinal)
        && danger.Detail.Contains("verify the area in game", StringComparison.OrdinalIgnoreCase),
    "Danger approach guidance failed");
var deathFinal = Brief("death", 14);
Require(deathFinal.Key == "death:final"
        && deathFinal.HudLine.Contains("STOP SHORT", StringComparison.Ordinal),
    "Death-marker final approach failed");

var landWater = Brief("water", 40, "carnotaurus");
Require(landWater.Heading == "WATER APPROACH"
        && landWater.HudLine.Contains("LISTEN", StringComparison.Ordinal)
        && landWater.Detail.Contains("both shores", StringComparison.OrdinalIgnoreCase),
    "Terrestrial water approach failed");
Require(Brief("water", 10, "carnotaurus").HudLine.Contains("STOP SHORT", StringComparison.Ordinal),
    "Final terrestrial water check failed");
Require(Brief("water", 20, "pteranodon").Heading == "WATER LANDING CHECK",
    "Pteranodon water approach must become a landing check");
Require(Brief("water", 20, "deinosuchus").Heading == "WATERLINE APPROACH"
        && Brief("water", 20, "beipiaosaurus").Heading == "WATERLINE APPROACH",
    "Aquatic and semi-aquatic water guidance failed");

var carnivoreFood = Brief("food", 9, "dilophosaurus");
Require(carnivoreFood.HudLine.Contains("CARCASS", StringComparison.Ordinal)
        && carnivoreFood.Detail.Contains("contested", StringComparison.OrdinalIgnoreCase),
    "Carnivore food approach failed");
Require(Brief("food", 9, "tenontosaurus").Detail.Contains("plant", StringComparison.OrdinalIgnoreCase),
    "Herbivore food approach failed");
Require(Brief("food", 20, "pteranodon").Heading == "FOOD LANDING CHECK",
    "Pteranodon food landing guidance failed");

Require(Brief("nest", 40).Heading == "NEST APPROACH",
    "Nest approach failed");
Require(Brief("safe", 20).Detail.Contains("not a live safety guarantee", StringComparison.OrdinalIgnoreCase),
    "Safe pins must not certify safety");
Require(new[] { "rally", "friend", "pack" }.All(kind =>
        Brief(kind, 20).Heading == "RALLY APPROACH"),
    "Rally and authorized pack targets failed");
Require(Brief("salt", 30).Detail.Contains("nutrient", StringComparison.OrdinalIgnoreCase),
    "Salt approach must retain the nutrient tradeoff");
Require(Brief("mud", 30).HudLine.Contains("WATERLINE", StringComparison.Ordinal),
    "Mud approach failed");
Require(Brief("gastrolith", 30).Heading == "GASTROLITH APPROACH",
    "Gastrolith approach failed");
Require(Brief("resource", 30).Detail.Contains("Availability is not live", StringComparison.Ordinal),
    "Generic static resource boundary failed");
Require(Brief("estimate", 40).Detail.Contains("did not detect", StringComparison.OrdinalIgnoreCase),
    "Track Finder estimates must not become detection claims");
Require(Brief("escape", 20).Urgency == 2
        && Brief("escape", 20).Detail.Contains("not treat the endpoint as safe", StringComparison.OrdinalIgnoreCase),
    "Escape endpoint warning failed");
Require(Brief("recovery", 20).Detail.Contains("historical context", StringComparison.OrdinalIgnoreCase),
    "Recovery anchors must remain historical");
Require(Brief("water", 20, movingAway: true).Detail.Contains("correct course first", StringComparison.OrdinalIgnoreCase),
    "Moving-away evidence must be preserved");

var allVisibleKinds = ApproachBriefLogic.DestinationKinds
    .Select(kind => Brief(kind, 5))
    .ToArray();
Require(allVisibleKinds.All(view => view.Visible
        && view.ActionId == "routes"
        && view.ActionLabel == "OPEN ROUTE"
        && view.Detail.Contains("in game", StringComparison.OrdinalIgnoreCase)),
    "Every allowlisted destination needs one explicit route action and in-game boundary");
Require(ApproachBriefLogic.NormalizeKind(" WATER ") == "water"
        && ApproachBriefLogic.NormalizeKind("water<script>") == string.Empty,
    "Destination-kind normalization failed");

Console.WriteLine("Approach Brief verification passed: destination radii, final-stage commands, species-aware water and food checks, warnings, privacy, and explicit in-game authority");
