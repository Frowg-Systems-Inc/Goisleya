using System.Globalization;
using System.Text.RegularExpressions;

namespace Isley;

internal sealed record UniversalCoordinatePoint(double X, double Y, double Z);

internal sealed record UniversalCoordinateMovement(
    double HorizontalDistance,
    double AltitudeDelta,
    double ElapsedSeconds,
    string AxisCourse);

internal sealed record UniversalHillEvidence(
    double GradePercent,
    double AngleDegrees,
    double RiseOrDrop,
    string Direction);

internal readonly record struct UniversalHeading(double Degrees, bool Updated);

internal readonly record struct UniversalTrackSample(
    UniversalCoordinatePoint Point,
    DateTimeOffset CapturedAt);

internal sealed record UniversalTrackEstimate(
    double HeadingDegrees,
    double SpeedWorldUnitsPerSecond,
    double DirectionAgreement,
    int SegmentCount,
    string ConfidenceLabel);

internal static class UniversalCoordinateLogic
{
    private const double MaximumHorizontalMagnitude = 1_000_000;
    private const double MaximumAltitudeMagnitude = 200_000;
    private const double MinimumHillRun = 5;

    internal static bool TryParseClipboard(string? value, out UniversalCoordinatePoint point)
    {
        point = new UniversalCoordinatePoint(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value) || value.Length > 180)
        {
            return false;
        }

        var sanitized = Regex.Replace(value, @"[\u0000-\u001F\u007F]+", " ").Trim();
        var prefixed = Regex.Match(
            sanitized,
            @"^(?:ASSET\s+LOCATION\s*[:\-]?\s*)?(?<coordinates>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!prefixed.Success)
        {
            return false;
        }

        var coordinateText = prefixed.Groups["coordinates"].Value.Trim();
        var parts = SplitCoordinateParts(coordinateText);
        if (parts.Length != 3
            || !TryParseCoordinateNumber(parts[0], out var x)
            || !TryParseCoordinateNumber(parts[1], out var y)
            || !TryParseCoordinateNumber(parts[2], out var z)
            || Math.Abs(x) > MaximumHorizontalMagnitude
            || Math.Abs(y) > MaximumHorizontalMagnitude
            || Math.Abs(z) > MaximumAltitudeMagnitude)
        {
            return false;
        }

        point = new UniversalCoordinatePoint(x, y, z);
        return true;
    }

    /// <summary>
    /// Parses a destination target from Asset Location / XYZ clipboard text or a plain X,Y pair.
    /// Altitude is accepted when present and discarded for map routing.
    /// </summary>
    internal static bool TryParseDestinationWorldPoint(string? value, out double worldX, out double worldY)
    {
        worldX = 0;
        worldY = 0;
        if (TryParseClipboard(value, out var point))
        {
            worldX = point.X;
            worldY = point.Y;
            return true;
        }

        if (string.IsNullOrWhiteSpace(value) || value.Length > 180)
        {
            return false;
        }

        var sanitized = Regex.Replace(value, @"[\u0000-\u001F\u007F]+", " ").Trim();
        if (Regex.IsMatch(sanitized, @"https?://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || Regex.IsMatch(sanitized, @"\b[xy]=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        var matches = Regex.Matches(sanitized, @"[-+]?(?:\d+(?:\.\d+)?|\.\d+)");
        if (matches.Count != 2
            || !double.TryParse(matches[0].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out worldX)
            || !double.TryParse(matches[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out worldY)
            || !double.IsFinite(worldX)
            || !double.IsFinite(worldY)
            || Math.Abs(worldX) > MaximumHorizontalMagnitude
            || Math.Abs(worldY) > MaximumHorizontalMagnitude)
        {
            worldX = 0;
            worldY = 0;
            return false;
        }

        return true;
    }

    internal static bool SamePoint(UniversalCoordinatePoint? left, UniversalCoordinatePoint? right) =>
        left is not null
        && right is not null
        && Math.Abs(left.X - right.X) < 0.001
        && Math.Abs(left.Y - right.Y) < 0.001
        && Math.Abs(left.Z - right.Z) < 0.001;

    internal static UniversalCoordinateMovement? DescribeMovement(
        UniversalCoordinatePoint? previous,
        UniversalCoordinatePoint? current,
        TimeSpan elapsed)
    {
        if (previous is null || current is null || elapsed <= TimeSpan.Zero)
        {
            return null;
        }

        var dx = current.X - previous.X;
        var dy = current.Y - previous.Y;
        var dz = current.Z - previous.Z;
        var horizontalDistance = Math.Sqrt(dx * dx + dy * dy);
        return new UniversalCoordinateMovement(
            horizontalDistance,
            dz,
            Math.Max(0.1, elapsed.TotalSeconds),
            FormatAxisCourse(dx, dy, horizontalDistance));
    }

    internal static UniversalHeading ResolveHeading(
        UniversalCoordinatePoint? previous,
        UniversalCoordinatePoint? current,
        double previousHeadingDegrees,
        bool previousHeadingAvailable)
    {
        if (previous is null || current is null)
        {
            return new UniversalHeading(previousHeadingAvailable ? NormalizeHeading(previousHeadingDegrees) : 0, false);
        }

        var dx = current.X - previous.X;
        var dy = current.Y - previous.Y;
        if (Math.Sqrt(dx * dx + dy * dy) < 0.01)
        {
            return new UniversalHeading(previousHeadingAvailable ? NormalizeHeading(previousHeadingDegrees) : 0, false);
        }

        return new UniversalHeading(
            NormalizeHeading(Math.Atan2(dy, dx) * 180 / Math.PI),
            true);
    }

    internal static UniversalTrackEstimate? EstimateTrack(
        IReadOnlyList<UniversalTrackSample> samples)
    {
        if (samples.Count < 2)
        {
            return null;
        }

        var segments = new List<(double UnitX, double UnitY, double Speed, double Weight)>();
        var firstIndex = Math.Max(1, samples.Count - 8);
        for (var index = firstIndex; index < samples.Count; index++)
        {
            var previous = samples[index - 1];
            var current = samples[index];
            var elapsedSeconds = (current.CapturedAt - previous.CapturedAt).TotalSeconds;
            var dx = current.Point.X - previous.Point.X;
            var dy = current.Point.Y - previous.Point.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (!double.IsFinite(elapsedSeconds)
                || elapsedSeconds is < 0.1 or > 60
                || !double.IsFinite(distance)
                || distance is < 0.05 or > 100_000)
            {
                continue;
            }

            var ageFromNewest = samples.Count - 1 - index;
            var recencyWeight = Math.Pow(0.62, ageFromNewest);
            var distanceWeight = Math.Clamp(distance, 1, 250);
            segments.Add((
                dx / distance,
                dy / distance,
                distance / elapsedSeconds,
                recencyWeight * distanceWeight));
        }

        if (segments.Count == 0)
        {
            return null;
        }

        var totalWeight = segments.Sum(segment => segment.Weight);
        var vectorX = segments.Sum(segment => segment.UnitX * segment.Weight) / totalWeight;
        var vectorY = segments.Sum(segment => segment.UnitY * segment.Weight) / totalWeight;
        var agreement = Math.Clamp(Math.Sqrt(vectorX * vectorX + vectorY * vectorY), 0, 1);
        if (agreement < 0.15)
        {
            return null;
        }

        var speed = segments.Sum(segment => segment.Speed * segment.Weight) / totalWeight;
        var confidence = segments.Count >= 3 && agreement >= 0.8
            ? "HIGH"
            : segments.Count >= 2 && agreement >= 0.55
                ? "MEDIUM"
                : "LOW";
        return new UniversalTrackEstimate(
            NormalizeHeading(Math.Atan2(vectorY, vectorX) * 180 / Math.PI),
            speed,
            agreement,
            segments.Count,
            confidence);
    }

    internal static UniversalHillEvidence? DescribeHill(UniversalCoordinateMovement? movement)
    {
        if (movement is null
            || !double.IsFinite(movement.HorizontalDistance)
            || !double.IsFinite(movement.AltitudeDelta)
            || movement.HorizontalDistance < MinimumHillRun)
        {
            return null;
        }

        var riseOrDrop = Math.Abs(movement.AltitudeDelta);
        var gradePercent = riseOrDrop / movement.HorizontalDistance * 100;
        var angleDegrees = Math.Atan2(riseOrDrop, movement.HorizontalDistance) * 180 / Math.PI;
        var direction = riseOrDrop < 0.5
            ? "LEVEL"
            : movement.AltitudeDelta > 0
                ? "CLIMB"
                : "DESCENT";
        return new UniversalHillEvidence(
            gradePercent,
            angleDegrees,
            riseOrDrop,
            direction);
    }

    private static string[] SplitCoordinateParts(string value)
    {
        var explicitParts = Regex.Split(value, @"\s*[;|]\s*")
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        if (explicitParts.Length == 3)
        {
            return explicitParts;
        }

        var spacedCommaParts = Regex.Split(value, @",\s+")
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        if (spacedCommaParts.Length == 3)
        {
            return spacedCommaParts;
        }

        return value.Count(character => character == ',') == 2
            ? value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : [];
    }

    private static bool TryParseCoordinateNumber(string value, out double result)
    {
        result = 0;
        var compact = Regex.Replace(value.Trim(), @"\s+", string.Empty);
        if (!Regex.IsMatch(compact, @"^[+-]?\d+(?:[.,]\d+)*$", RegexOptions.CultureInvariant))
        {
            return false;
        }

        var commaIndex = compact.LastIndexOf(',');
        var dotIndex = compact.LastIndexOf('.');
        string normalized;
        if (commaIndex >= 0 && dotIndex >= 0)
        {
            var decimalSeparator = commaIndex > dotIndex ? ',' : '.';
            var thousandsSeparator = decimalSeparator == ',' ? "." : ",";
            normalized = compact.Replace(thousandsSeparator, string.Empty, StringComparison.Ordinal);
            if (decimalSeparator == ',')
            {
                normalized = normalized.Replace(',', '.');
            }
        }
        else
        {
            var separator = commaIndex >= 0 ? ',' : dotIndex >= 0 ? '.' : '\0';
            if (separator == '\0')
            {
                normalized = compact;
            }
            else
            {
                var separatorCount = compact.Count(character => character == separator);
                if (separatorCount == 1)
                {
                    normalized = separator == ',' ? compact.Replace(',', '.') : compact;
                }
                else
                {
                    var groups = compact.Split(separator);
                    var looksLikeThousands = groups.Skip(1).All(group => group.Length == 3);
                    if (looksLikeThousands)
                    {
                        normalized = string.Concat(groups);
                    }
                    else
                    {
                        normalized = string.Concat(groups[..^1]) + "." + groups[^1];
                    }
                }
            }
        }

        return double.TryParse(
                   normalized,
                   NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                   CultureInfo.InvariantCulture,
                   out result)
               && double.IsFinite(result);
    }

    private static string FormatAxisCourse(double dx, double dy, double distance)
    {
        if (distance < 1)
        {
            return "STATIONARY";
        }

        var threshold = distance * 0.12;
        var axes = new List<string>(2);
        if (Math.Abs(dx) >= threshold)
        {
            axes.Add(dx >= 0 ? "+X" : "-X");
        }
        if (Math.Abs(dy) >= threshold)
        {
            axes.Add(dy >= 0 ? "+Y" : "-Y");
        }
        return axes.Count > 0 ? string.Join(" / ", axes) : "STATIONARY";
    }

    private static double NormalizeHeading(double degrees)
    {
        var normalized = degrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}
