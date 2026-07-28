namespace Isley;

internal enum TrackFinderMode
{
    Sound,
    Scent
}

internal enum ScentTargetKind
{
    Water,
    Food,
    Trail,
    Carcass
}

internal static class TrackFinderModeLogic
{
    internal static TrackFinderMode Next(TrackFinderMode mode) =>
        mode == TrackFinderMode.Sound ? TrackFinderMode.Scent : TrackFinderMode.Sound;

    internal static ScentTargetKind Next(ScentTargetKind target) => target switch
    {
        ScentTargetKind.Water => ScentTargetKind.Food,
        ScentTargetKind.Food => ScentTargetKind.Trail,
        ScentTargetKind.Trail => ScentTargetKind.Carcass,
        _ => ScentTargetKind.Water
    };

    internal static string ModeId(TrackFinderMode mode) =>
        mode == TrackFinderMode.Scent ? "scent" : "sound";

    internal static string TargetId(ScentTargetKind target) => target switch
    {
        ScentTargetKind.Food => "food",
        ScentTargetKind.Trail => "trail",
        ScentTargetKind.Carcass => "carcass",
        _ => "water"
    };

    internal static string TargetLabel(ScentTargetKind target) => target switch
    {
        ScentTargetKind.Food => "FOOD",
        ScentTargetKind.Trail => "TRAIL",
        ScentTargetKind.Carcass => "CARCASS",
        _ => "WATER"
    };

    internal static string CueLabel(TrackFinderMode mode, ScentTargetKind target) =>
        mode == TrackFinderMode.Sound
            ? "sound cue"
            : $"{TargetLabel(target).ToLowerInvariant()} scent clue";

    internal static string VerificationPhrase(TrackFinderMode mode) =>
        mode == TrackFinderMode.Sound ? "verify by sound" : "verify with scent in game";
}

internal enum SoundFinderStatus
{
    WaitingFirst,
    WaitingSecond,
    FirstExpired,
    TooClose,
    Parallel,
    Diverging,
    TooDistant,
    Ready
}

internal sealed record SoundBearingReading(
    double X,
    double Y,
    double BearingDegrees,
    DateTimeOffset CapturedAt);

internal sealed record SoundFinderAnalysis(
    SoundFinderStatus Status,
    double? EstimateX,
    double? EstimateY,
    double BaselineDistance,
    double? DistanceFromFirst,
    double? DistanceFromSecond,
    double IntersectionAngleDegrees,
    double UncertaintyRadius,
    string Confidence)
{
    internal bool HasEstimate =>
        Status == SoundFinderStatus.Ready
        && EstimateX is not null
        && EstimateY is not null;
}

internal static class SoundFinderLogic
{
    internal const double MinimumBaseline = 5;
    internal const double MinimumIntersectionAngle = 12;
    internal const double MaximumEstimateDistance = 1200;
    internal static readonly TimeSpan MaximumReadingAge = TimeSpan.FromSeconds(120);

    internal static SoundBearingReading Normalize(SoundBearingReading reading) => new(
        Math.Clamp(double.IsFinite(reading.X) ? reading.X : 0, 0, 1000),
        Math.Clamp(double.IsFinite(reading.Y) ? reading.Y : 0, 0, 1000),
        NormalizeBearing(reading.BearingDegrees),
        reading.CapturedAt);

    internal static SoundFinderAnalysis Analyze(
        SoundBearingReading? first,
        SoundBearingReading? second,
        DateTimeOffset now)
    {
        if (first is null)
        {
            return Empty(SoundFinderStatus.WaitingFirst);
        }

        var a = Normalize(first);
        if (now - a.CapturedAt > MaximumReadingAge)
        {
            return Empty(SoundFinderStatus.FirstExpired);
        }
        if (second is null)
        {
            return Empty(SoundFinderStatus.WaitingSecond);
        }

        var b = Normalize(second);
        var baselineDeltaX = b.X - a.X;
        var baselineDeltaY = b.Y - a.Y;
        var baseline = Math.Sqrt(baselineDeltaX * baselineDeltaX + baselineDeltaY * baselineDeltaY);
        if (baseline < MinimumBaseline)
        {
            return Empty(SoundFinderStatus.TooClose, baseline);
        }

        var directionA = Direction(a.BearingDegrees);
        var directionB = Direction(b.BearingDegrees);
        var cross = Cross(directionA.X, directionA.Y, directionB.X, directionB.Y);
        var dot = directionA.X * directionB.X + directionA.Y * directionB.Y;
        var acuteAngle = Math.Acos(Math.Clamp(Math.Abs(dot), 0, 1)) * 180 / Math.PI;
        if (acuteAngle < MinimumIntersectionAngle || Math.Abs(cross) < 0.000001)
        {
            return Empty(SoundFinderStatus.Parallel, baseline, acuteAngle);
        }

        var deltaX = b.X - a.X;
        var deltaY = b.Y - a.Y;
        var distanceA = Cross(deltaX, deltaY, directionB.X, directionB.Y) / cross;
        var distanceB = Cross(deltaX, deltaY, directionA.X, directionA.Y) / cross;
        if (distanceA <= 0.5 || distanceB <= 0.5)
        {
            return Empty(SoundFinderStatus.Diverging, baseline, acuteAngle);
        }
        if (distanceA > MaximumEstimateDistance || distanceB > MaximumEstimateDistance)
        {
            return Empty(
                SoundFinderStatus.TooDistant,
                baseline,
                acuteAngle,
                distanceA,
                distanceB);
        }

        var estimateX = a.X + directionA.X * distanceA;
        var estimateY = a.Y + directionA.Y * distanceA;
        if (!double.IsFinite(estimateX)
            || !double.IsFinite(estimateY)
            || estimateX is < -0.001 or > 1000.001
            || estimateY is < -0.001 or > 1000.001)
        {
            return Empty(
                SoundFinderStatus.TooDistant,
                baseline,
                acuteAngle,
                distanceA,
                distanceB);
        }
        estimateX = Math.Clamp(estimateX, 0, 1000);
        estimateY = Math.Clamp(estimateY, 0, 1000);

        var intervalSeconds = Math.Abs((b.CapturedAt - a.CapturedAt).TotalSeconds);
        var geometryPenalty = Math.Max(distanceA, distanceB)
                              * Math.Tan(7 * Math.PI / 180)
                              / Math.Max(0.25, Math.Sin(acuteAngle * Math.PI / 180));
        var uncertainty = Math.Clamp(geometryPenalty + intervalSeconds * 0.2, 8, 120);
        var confidence = uncertainty <= 20 && baseline >= 15 && intervalSeconds <= 45
            ? "HIGH"
            : uncertainty <= 45 && intervalSeconds <= 90
                ? "MEDIUM"
                : "ROUGH";
        return new SoundFinderAnalysis(
            SoundFinderStatus.Ready,
            estimateX,
            estimateY,
            baseline,
            distanceA,
            distanceB,
            acuteAngle,
            uncertainty,
            confidence);
    }

    internal static double NormalizeBearing(double bearing)
    {
        if (!double.IsFinite(bearing)) return 0;
        var normalized = bearing % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static (double X, double Y) Direction(double bearing)
    {
        var radians = NormalizeBearing(bearing) * Math.PI / 180;
        return (Math.Sin(radians), -Math.Cos(radians));
    }

    private static double Cross(double ax, double ay, double bx, double by) =>
        ax * by - ay * bx;

    private static SoundFinderAnalysis Empty(
        SoundFinderStatus status,
        double baseline = 0,
        double angle = 0,
        double? distanceA = null,
        double? distanceB = null) => new(
        status,
        null,
        null,
        baseline,
        distanceA,
        distanceB,
        angle,
        0,
        string.Empty);
}
