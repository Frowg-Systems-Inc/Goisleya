namespace Isley;

internal readonly record struct TerrainRouteConfidence(
    string Level,
    string Label,
    double MappedPercent,
    double UnknownDistance,
    double LongestUnknownDistance,
    int UnknownSegmentCount,
    string Detail,
    string Guidance);

internal static class TerrainRouteConfidenceLogic
{
    internal const string High = "high";
    internal const string Moderate = "moderate";
    internal const string Low = "low";
    internal const string Unavailable = "unavailable";

    internal static TerrainRouteConfidence Evaluate(
        double roadDistance,
        double trailDistance,
        double unknownDistance,
        double longestUnknownDistance,
        int unknownSegmentCount,
        bool waterSafetyApplied,
        double learnedDistance = 0)
    {
        var road = CleanDistance(roadDistance);
        var trail = CleanDistance(trailDistance);
        var learned = CleanDistance(learnedDistance);
        var unknown = CleanDistance(unknownDistance);
        var longestUnknown = Math.Min(unknown, CleanDistance(longestUnknownDistance));
        var unknownSegments = Math.Clamp(unknownSegmentCount, 0, 100);
        var total = road + trail + learned + unknown;
        if (total <= 0.001)
        {
            return new TerrainRouteConfidence(
                Unavailable,
                "NO COURSE EVIDENCE",
                0,
                0,
                0,
                0,
                "Plot a road/trail course to inspect its mapped coverage.",
                "No route evidence is available yet.");
        }

        var mappedPercent = Math.Clamp((road + trail + learned) / total * 100, 0, 100);
        var unknownWaterCovered = unknown <= 0.5 || waterSafetyApplied;
        var level = mappedPercent >= 90
                    && longestUnknown <= 18
                    && unknownSegments <= 2
                    && unknownWaterCovered
            ? High
            : mappedPercent >= 70
              && longestUnknown <= 45
              && unknownSegments <= 5
                ? Moderate
                : Low;
        var label = level switch
        {
            High => "HIGH ROUTE EVIDENCE",
            Moderate => "MODERATE ROUTE EVIDENCE",
            _ => "LOW ROUTE EVIDENCE"
        };
        var learnedDetail = learned > 0.5 ? $" · {learned:0} MU player-traveled" : string.Empty;
        var detail = $"{mappedPercent:0}% mapped · {road:0} MU road · {trail:0} MU trail" +
                     learnedDetail + " · " +
                     $"{unknown:0} MU unknown across {unknownSegments} gap" +
                     (unknownSegments == 1 ? string.Empty : "s");
        var guidance = level switch
        {
            High => "Mostly mapped travel. Elevation and live passability still require an in-game check.",
            Moderate => "Verify the amber connector gaps before committing; report a blocked passage to replan.",
            _ => "This course depends on substantial unknown terrain. Prefer Road-first or add local obstacles."
        };
        if (!unknownWaterCovered)
        {
            guidance += " Water safety is not covering the unknown connectors.";
        }

        return new TerrainRouteConfidence(
            level,
            label,
            mappedPercent,
            unknown,
            longestUnknown,
            unknownSegments,
            detail,
            guidance);
    }

    private static double CleanDistance(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 100_000) : 0;
}
