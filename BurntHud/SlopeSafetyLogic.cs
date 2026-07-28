namespace Isley;

internal sealed record SlopeSafetyPresentation(
    string State,
    string Heading,
    string Detail,
    string Guidance,
    int Severity,
    bool HasMeasurement,
    bool CanSaveAvoidance,
    string SaveLabel,
    string SaveTooltip);

internal static class SlopeSafetyLogic
{
    internal const double ElevatedGradePercent = 15;
    internal const double HighGradePercent = 35;

    internal static SlopeSafetyPresentation Present(
        bool captureEnabled,
        UniversalCoordinateMovement? movement,
        bool mapReady,
        bool obstacleLimitReached)
    {
        if (!captureEnabled)
        {
            return new SlopeSafetyPresentation(
                "OFF",
                "PLAYER SYNC OFF",
                "Isley is not listening for Asset Location copies.",
                "Turn Player Sync on, focus The Isle, press Tab, then click Asset Location. Copy again after moving for direction and Terrain Probe slope.",
                0,
                false,
                false,
                "SAVE ROUTE AVOIDANCE",
                "Measure a slope before saving a route avoidance");
        }

        var hill = UniversalCoordinateLogic.DescribeHill(movement);
        if (hill is null || movement is null)
        {
            return new SlopeSafetyPresentation(
                "WAITING",
                "COPY TWO POINTS",
                movement is null
                    ? "In The Isle: Tab → Asset Location. Move, then copy again."
                    : "Move at least 5 world units, then copy Asset Location again.",
                "Each accepted copy refreshes your map icon. Two different points also unlock Terrain Probe slope checks.",
                0,
                false,
                false,
                "SAVE ROUTE AVOIDANCE",
                "Measure a slope before saving a route avoidance");
        }

        var detail =
            $"{hill.GradePercent:0.0}% grade · {hill.AngleDegrees:0.0}° · " +
            $"{movement.HorizontalDistance:0} WU run · Z {movement.AltitudeDelta:+0;-0;0}";
        if (hill.Direction == "LEVEL")
        {
            return new SlopeSafetyPresentation(
                "LEVEL",
                "MEASURED LEVEL",
                detail,
                "This short segment measured level; surrounding terrain can still differ.",
                0,
                true,
                false,
                "LEVEL · NOTHING TO AVOID",
                "Only a measured climb or descent can become a route avoidance");
        }

        var severity = hill.GradePercent >= HighGradePercent
            ? 3
            : hill.GradePercent >= ElevatedGradePercent
                ? 2
                : 1;
        var direction = hill.Direction == "DESCENT" ? "DESCENT" : "CLIMB";
        var heading = severity switch
        {
            3 => $"HIGH {direction}",
            2 => $"ELEVATED {direction}",
            _ => $"MEASURED {direction}"
        };
        var guidance = hill.Direction == "DESCENT"
            ? severity >= 2
                ? "Treat this as fall-risk until verified; slow before the grade and turn across or retreat if sliding begins."
                : "Hill sliding is enabled in the public branch; verify grip before committing downhill."
            : severity >= 2
                ? "Expect meaningful slowdown; quadrupeds currently have an uphill advantage, but Isley cannot infer traction or passability."
                : "Expect some uphill slowdown and verify the surface in game.";
        var canSave = mapReady && !obstacleLimitReached;
        var saveLabel = obstacleLimitReached
            ? "NO-GO LIMIT REACHED"
            : mapReady
                ? "SAVE ROUTE AVOIDANCE"
                : "MAP CALIBRATION NEEDED";
        var saveTooltip = obstacleLimitReached
            ? "Remove a saved No-Go area before adding this measured slope"
            : mapReady
                ? "Convert these two measured endpoints into a reversible local No-Go corridor and replan an active terrain course"
            : "A calibrated map from Live Map mode is required to convert world coordinates into a route obstacle";

        return new SlopeSafetyPresentation(
            severity >= 3 ? "HIGH" : severity == 2 ? "ELEVATED" : "MEASURED",
            heading,
            detail,
            guidance,
            severity,
            true,
            canSave,
            saveLabel,
            saveTooltip);
    }
}
