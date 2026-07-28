namespace Isley;

public sealed record NoGoPoint(double X, double Y);

public sealed record NoGoArea(
    string Id,
    string Label,
    IReadOnlyList<NoGoPoint> Points,
    long CreatedAtUnixMs);

public sealed record NoGoAreaValidationResult(
    bool IsValid,
    string Error,
    NoGoArea? Area);

/// <summary>
/// Deterministic validation and intersection rules for manually traced map obstacles.
/// Coordinates use Isley's normalized 0..1000 map space and never read game memory.
/// </summary>
public static class NoGoAreaLogic
{
    public const int MaximumAreaCount = 8;
    public const int MinimumVertexCount = 3;
    public const int MaximumVertexCount = 12;
    public const double MinimumArea = 4;
    private const double Epsilon = 0.000001;

    public static NoGoAreaValidationResult Validate(
        string? id,
        string? label,
        IEnumerable<NoGoPoint>? points,
        long createdAtUnixMs = 0)
    {
        var cleanPoints = (points ?? [])
            .Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y))
            .ToList();

        while (cleanPoints.Count > 1 && SamePoint(cleanPoints[0], cleanPoints[^1]))
        {
            cleanPoints.RemoveAt(cleanPoints.Count - 1);
        }

        for (var index = cleanPoints.Count - 1; index > 0; index--)
        {
            if (SamePoint(cleanPoints[index - 1], cleanPoints[index]))
            {
                cleanPoints.RemoveAt(index);
            }
        }

        if (cleanPoints.Count is < MinimumVertexCount or > MaximumVertexCount)
        {
            return Invalid($"Use {MinimumVertexCount}-{MaximumVertexCount} distinct boundary points");
        }

        if (cleanPoints.Any(point => point.X is < 0 or > 1000 || point.Y is < 0 or > 1000))
        {
            return Invalid("Every boundary point must be inside the map");
        }

        if (HasSelfIntersection(cleanPoints))
        {
            return Invalid("Boundary lines cannot cross each other");
        }

        if (PolygonArea(cleanPoints) < MinimumArea)
        {
            return Invalid("Trace a larger area before finishing");
        }

        var cleanLabel = SanitizeLabel(label);
        var cleanId = SanitizeId(id);
        return new NoGoAreaValidationResult(
            true,
            string.Empty,
            new NoGoArea(
                cleanId.Length > 0 ? cleanId : $"area-{Math.Max(0, createdAtUnixMs)}",
                cleanLabel.Length > 0 ? cleanLabel : "No-go area",
                cleanPoints,
                Math.Max(0, createdAtUnixMs)));
    }

    public static double PolygonArea(IReadOnlyList<NoGoPoint>? points)
    {
        if (points is null || points.Count < MinimumVertexCount)
        {
            return 0;
        }

        double doubledArea = 0;
        for (var index = 0; index < points.Count; index++)
        {
            var next = (index + 1) % points.Count;
            doubledArea += points[index].X * points[next].Y - points[next].X * points[index].Y;
        }

        return Math.Abs(doubledArea) / 2;
    }

    public static bool ContainsPoint(
        IReadOnlyList<NoGoPoint>? polygon,
        NoGoPoint point,
        double padding = 0)
    {
        if (polygon is null || polygon.Count < MinimumVertexCount
            || !double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            return false;
        }

        var safePadding = Math.Max(0, double.IsFinite(padding) ? padding : 0);
        for (var index = 0; index < polygon.Count; index++)
        {
            var next = (index + 1) % polygon.Count;
            if (DistancePointToSegment(point, polygon[index], polygon[next]) <= safePadding + Epsilon)
            {
                return true;
            }
        }

        var inside = false;
        for (int current = 0, previous = polygon.Count - 1;
             current < polygon.Count;
             previous = current++)
        {
            var a = polygon[current];
            var b = polygon[previous];
            var crosses = (a.Y > point.Y) != (b.Y > point.Y)
                          && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X;
            if (crosses)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    public static bool SegmentIntersectsPolygon(
        NoGoPoint start,
        NoGoPoint end,
        IReadOnlyList<NoGoPoint>? polygon,
        double padding = 0)
    {
        if (polygon is null || polygon.Count < MinimumVertexCount)
        {
            return false;
        }

        var safePadding = Math.Max(0, double.IsFinite(padding) ? padding : 0);
        if (ContainsPoint(polygon, start, safePadding) || ContainsPoint(polygon, end, safePadding))
        {
            return true;
        }

        for (var index = 0; index < polygon.Count; index++)
        {
            var next = (index + 1) % polygon.Count;
            if (SegmentsIntersect(start, end, polygon[index], polygon[next])
                || DistanceSegmentToSegment(start, end, polygon[index], polygon[next]) <= safePadding + Epsilon)
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasSelfIntersection(IReadOnlyList<NoGoPoint>? points)
    {
        if (points is null || points.Count < MinimumVertexCount)
        {
            return false;
        }

        for (var first = 0; first < points.Count; first++)
        {
            var firstNext = (first + 1) % points.Count;
            for (var second = first + 1; second < points.Count; second++)
            {
                var secondNext = (second + 1) % points.Count;
                if (first == second || firstNext == second || secondNext == first)
                {
                    continue;
                }

                if (SegmentsIntersect(points[first], points[firstNext], points[second], points[secondNext]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static double DistanceSegmentToSegment(
        NoGoPoint a,
        NoGoPoint b,
        NoGoPoint c,
        NoGoPoint d)
    {
        if (SegmentsIntersect(a, b, c, d))
        {
            return 0;
        }

        return new[]
        {
            DistancePointToSegment(a, c, d),
            DistancePointToSegment(b, c, d),
            DistancePointToSegment(c, a, b),
            DistancePointToSegment(d, a, b)
        }.Min();
    }

    private static double DistancePointToSegment(NoGoPoint point, NoGoPoint start, NoGoPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= Epsilon)
        {
            return Math.Sqrt(
                Math.Pow(point.X - start.X, 2) + Math.Pow(point.Y - start.Y, 2));
        }

        var amount = Math.Clamp(
            ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared,
            0,
            1);
        return Math.Sqrt(
            Math.Pow(point.X - (start.X + amount * dx), 2)
            + Math.Pow(point.Y - (start.Y + amount * dy), 2));
    }

    private static bool SegmentsIntersect(NoGoPoint a, NoGoPoint b, NoGoPoint c, NoGoPoint d)
    {
        var o1 = Orientation(a, b, c);
        var o2 = Orientation(a, b, d);
        var o3 = Orientation(c, d, a);
        var o4 = Orientation(c, d, b);
        if (o1 != o2 && o3 != o4)
        {
            return true;
        }

        return o1 == 0 && OnSegment(a, c, b)
               || o2 == 0 && OnSegment(a, d, b)
               || o3 == 0 && OnSegment(c, a, d)
               || o4 == 0 && OnSegment(c, b, d);
    }

    private static int Orientation(NoGoPoint a, NoGoPoint b, NoGoPoint c)
    {
        var cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        return Math.Abs(cross) <= Epsilon ? 0 : cross > 0 ? 1 : -1;
    }

    private static bool OnSegment(NoGoPoint start, NoGoPoint point, NoGoPoint end) =>
        point.X <= Math.Max(start.X, end.X) + Epsilon
        && point.X + Epsilon >= Math.Min(start.X, end.X)
        && point.Y <= Math.Max(start.Y, end.Y) + Epsilon
        && point.Y + Epsilon >= Math.Min(start.Y, end.Y);

    private static bool SamePoint(NoGoPoint a, NoGoPoint b) =>
        Math.Abs(a.X - b.X) <= Epsilon && Math.Abs(a.Y - b.Y) <= Epsilon;

    private static string SanitizeLabel(string? value)
    {
        var clean = string.Join(
            ' ',
            (value ?? string.Empty).Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries)).Trim();
        return clean[..Math.Min(40, clean.Length)];
    }

    private static string SanitizeId(string? value)
    {
        var clean = new string((value ?? string.Empty)
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            .Take(80)
            .ToArray());
        return clean;
    }

    private static NoGoAreaValidationResult Invalid(string error) => new(false, error, null);
}
