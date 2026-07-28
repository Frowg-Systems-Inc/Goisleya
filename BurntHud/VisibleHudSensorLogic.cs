namespace Isley;

internal readonly record struct VisibleHudSensorSample(
    DateTimeOffset CapturedAt,
    int HealthPercent,
    int FoodPercent,
    int WaterPercent,
    int StaminaPercent,
    double Confidence,
    bool DamageVisualDetected);

internal readonly record struct VisibleHudCalibration(
    double Scale,
    double OffsetX,
    double OffsetY,
    double Score,
    DateTimeOffset CapturedAt)
{
    internal static VisibleHudCalibration Default { get; } = new(1, 0, 0, 0, default);
}

internal static class VisibleHudSensorLogic
{
    internal const int FreshnessSeconds = 3;

    internal static bool IsFresh(VisibleHudSensorSample sample, DateTimeOffset now)
    {
        var age = (now - sample.CapturedAt).TotalSeconds;
        return age >= 0 && age < FreshnessSeconds;
    }

    internal static VisibleHudCalibration NormalizeCalibration(VisibleHudCalibration calibration) =>
        new(
            Math.Clamp(
                double.IsFinite(calibration.Scale) ? calibration.Scale : 1,
                0.75,
                1.30),
            Math.Clamp(
                double.IsFinite(calibration.OffsetX) ? calibration.OffsetX : 0,
                -0.05,
                0.05),
            Math.Clamp(
                double.IsFinite(calibration.OffsetY) ? calibration.OffsetY : 0,
                -0.05,
                0.05),
            Math.Clamp(
                double.IsFinite(calibration.Score) ? calibration.Score : 0,
                0,
                1),
            calibration.CapturedAt);

    internal static (double Left, double Top, double Right, double Bottom) TransformRegion(
        double left,
        double top,
        double right,
        double bottom,
        VisibleHudCalibration calibration)
    {
        var normalized = NormalizeCalibration(calibration);
        static double Transform(double value, double scale, double offset) =>
            Math.Clamp(1 - (1 - value) * scale + offset, 0, 1);
        return (
            Transform(left, normalized.Scale, normalized.OffsetX),
            Transform(top, normalized.Scale, normalized.OffsetY),
            Transform(right, normalized.Scale, normalized.OffsetX),
            Transform(bottom, normalized.Scale, normalized.OffsetY));
    }

    internal static int EstimateFillPercent(double sampledDensity, double expectedFullDensity)
    {
        if (!double.IsFinite(sampledDensity)
            || !double.IsFinite(expectedFullDensity)
            || expectedFullDensity <= 0)
        {
            return 0;
        }

        return (int)Math.Clamp(
            Math.Round(sampledDensity / expectedFullDensity * 100),
            0,
            100);
    }

    internal static int EstimateHealthPercent(double redEdgeRatio) =>
        Math.Clamp(redEdgeRatio, 0, 1) switch
        {
            < 0.005 => 100,
            < 0.035 => 85,
            < 0.10 => 60,
            < 0.18 => 40,
            _ => 25
        };

    internal static ReportedHealthState HealthState(int percent) =>
        Math.Clamp(percent, 0, 100) switch
        {
            <= 30 => ReportedHealthState.Critical,
            <= 70 => ReportedHealthState.Hurt,
            _ => ReportedHealthState.Stable
        };

    internal static ReportedVitalState VitalState(int percent) =>
        Math.Clamp(percent, 0, 100) switch
        {
            <= 10 => ReportedVitalState.Empty,
            <= 35 => ReportedVitalState.Low,
            _ => ReportedVitalState.Stable
        };

    internal static VisibleHudSensorSample Median(
        IEnumerable<VisibleHudSensorSample> samples,
        DateTimeOffset capturedAt)
    {
        var current = samples.ToArray();
        if (current.Length == 0)
        {
            return default;
        }

        static int Middle(IEnumerable<int> values)
        {
            var ordered = values.Order().ToArray();
            return ordered[ordered.Length / 2];
        }

        return new VisibleHudSensorSample(
            capturedAt,
            Middle(current.Select(sample => sample.HealthPercent)),
            Middle(current.Select(sample => sample.FoodPercent)),
            Middle(current.Select(sample => sample.WaterPercent)),
            Middle(current.Select(sample => sample.StaminaPercent)),
            current.Average(sample => sample.Confidence),
            current.Count(sample => sample.DamageVisualDetected) > current.Length / 2);
    }
}
