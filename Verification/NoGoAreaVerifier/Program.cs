using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var rectangle = new[]
{
    new NoGoPoint(100, 100),
    new NoGoPoint(180, 100),
    new NoGoPoint(180, 150),
    new NoGoPoint(100, 150),
    new NoGoPoint(100, 100)
};
var validated = NoGoAreaLogic.Validate("ridge-1", "  North   ridge  ", rectangle, 1234);
Check(validated is { IsValid: true, Area: not null }, "valid rectangle accepted");
var validArea = validated.Area!;
Check(validArea.Label == "North ridge" && validArea.Points.Count == 4,
    "label and closing point normalized");
Check(Math.Abs(NoGoAreaLogic.PolygonArea(validArea.Points) - 4000) < 0.001,
    "shoelace area");
Check(NoGoAreaLogic.ContainsPoint(validArea.Points, new NoGoPoint(120, 120)),
    "interior point");
Check(NoGoAreaLogic.ContainsPoint(validArea.Points, new NoGoPoint(100, 130)),
    "boundary point");
Check(!NoGoAreaLogic.ContainsPoint(validArea.Points, new NoGoPoint(200, 130)),
    "outside point");
Check(NoGoAreaLogic.SegmentIntersectsPolygon(
        new NoGoPoint(80, 125), new NoGoPoint(200, 125), validArea.Points, 3),
    "crossing route blocked");
Check(NoGoAreaLogic.SegmentIntersectsPolygon(
        new NoGoPoint(80, 95), new NoGoPoint(200, 95), validArea.Points, 6),
    "route safety padding");
Check(!NoGoAreaLogic.SegmentIntersectsPolygon(
        new NoGoPoint(80, 80), new NoGoPoint(200, 80), validArea.Points, 3),
    "distant route allowed");

var concave = new[]
{
    new NoGoPoint(10, 10), new NoGoPoint(30, 10), new NoGoPoint(30, 30),
    new NoGoPoint(20, 20), new NoGoPoint(10, 30)
};
Check(NoGoAreaLogic.Validate("concave", "Cliff band", concave).IsValid,
    "concave boundary accepted");
Check(NoGoAreaLogic.ContainsPoint(concave, new NoGoPoint(15, 20)), "concave interior");
Check(!NoGoAreaLogic.ContainsPoint(concave, new NoGoPoint(25, 25.5)), "concave cutout");

var crossed = new[]
{
    new NoGoPoint(10, 10), new NoGoPoint(30, 30),
    new NoGoPoint(10, 30), new NoGoPoint(30, 10)
};
Check(!NoGoAreaLogic.Validate("crossed", "Bad", crossed).IsValid,
    "self-intersection rejected");
Check(!NoGoAreaLogic.Validate("tiny", "Tiny", new[]
{
    new NoGoPoint(1, 1), new NoGoPoint(2, 1), new NoGoPoint(1, 2)
}).IsValid, "tiny area rejected");
Check(!NoGoAreaLogic.Validate("outside", "Outside", new[]
{
    new NoGoPoint(-1, 1), new NoGoPoint(20, 1), new NoGoPoint(20, 20)
}).IsValid, "out-of-bounds area rejected");
Check(!NoGoAreaLogic.Validate("few", "Few", new[]
{
    new NoGoPoint(1, 1), new NoGoPoint(20, 20)
}).IsValid, "too few vertices rejected");

Console.WriteLine("No-go areas: PASS (normalization, bounds, concavity, crossings, point containment, and padded route blocking)");
