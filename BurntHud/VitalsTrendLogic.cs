namespace Isley;

internal enum VitalTrendDirection
{
    Learning,
    Stable,
    Falling,
    Rising,
    Mixed
}

internal readonly record struct VitalsTrendSample(
    DateTimeOffset CapturedAt,
    double HealthPercent,
    double FoodPercent,
    double WaterPercent,
    double GrowthPercent);

internal readonly record struct VitalMetricTrend(
    string Label,
    VitalTrendDirection Direction,
    int SampleCount,
    int SpanSeconds,
    double CurrentPercent,
    double RatePerMinute,
    int? MinutesToBoundary,
    int BoundaryPercent,
    string BoundaryLabel)
{
    internal bool Ready => Direction != VitalTrendDirection.Learning;
    internal bool Falling => Direction == VitalTrendDirection.Falling;
    internal bool Rising => Direction == VitalTrendDirection.Rising;
}

internal readonly record struct VitalsTrendAnalysis(
    bool Active,
    bool Fresh,
    int SampleCount,
    VitalMetricTrend Health,
    VitalMetricTrend Food,
    VitalMetricTrend Water,
    bool Warning,
    string WarningHeading,
    string WarningDetail,
    string CompactLabel);

internal static class VitalsTrendLogic
{
    internal const int MinimumSamples = 3;
    internal const int MinimumSpanSeconds = 50;
    internal const int MaximumSampleCount = 12;
    internal const int MaximumWindowMinutes = 15;
    internal const int FreshnessSeconds = PlayerSnapshotLogic.FreshnessSeconds;
    internal const double RefillResetPercent = 3;
    internal const double DirectionRatePercentPerMinute = 0.15;
    internal const double MinimumDirectionalChangePercent = 1;
    internal const double HealthDamageResetPercent = 1;
    internal const int MaximumHealthEtaMinutes = 120;
    internal const int EarlyWarningMinutes = 15;

    internal static VitalsTrendAnalysis Analyze(
        IReadOnlyList<VitalsTrendSample>? rawSamples,
        DateTimeOffset now)
    {
        var minimumTime = now.AddMinutes(-MaximumWindowMinutes);
        var maximumTime = now.AddSeconds(5);
        var samples = (rawSamples ?? [])
            .Where(IsValid)
            .Where(sample => sample.CapturedAt >= minimumTime && sample.CapturedAt <= maximumTime)
            .OrderBy(sample => sample.CapturedAt)
            .GroupBy(sample => sample.CapturedAt)
            .Select(group => group.Last())
            .TakeLast(MaximumSampleCount)
            .ToArray();
        if (samples.Length == 0)
        {
            return Empty("TREND · WAITING FOR LIVE SAMPLES");
        }

        var latestAge = Math.Max(0, (now - samples[^1].CapturedAt).TotalSeconds);
        if (latestAge >= FreshnessSeconds)
        {
            return Empty("TREND PAUSED · SNAPSHOT STALE", samples.Length);
        }

        var health = AnalyzeHealth(samples);
        var food = AnalyzeMetric("FOOD", samples, sample => sample.FoodPercent);
        var water = AnalyzeMetric("WATER", samples, sample => sample.WaterPercent);
        var warningMetric = new[] { water, food }
            .Where(metric => metric.Falling
                             && metric.MinutesToBoundary is > 0 and <= EarlyWarningMinutes)
            .OrderBy(metric => metric.MinutesToBoundary)
            .FirstOrDefault();
        var warning = !string.IsNullOrEmpty(warningMetric.Label);
        var compact = health.Direction == VitalTrendDirection.Learning
                      && food.Direction == VitalTrendDirection.Learning
                      && water.Direction == VitalTrendDirection.Learning
            ? $"TREND · LEARNING {Math.Min(samples.Length, MinimumSamples)}/{MinimumSamples}"
            : $"{CompactHealth(health)} · {CompactMetric(food)} · {CompactMetric(water)}";
        var warningHeading = warning
            ? $"{warningMetric.Label} {warningMetric.BoundaryLabel} IN ABOUT {warningMetric.MinutesToBoundary}M"
            : string.Empty;
        var warningDetail = warning
            ? $"{warningMetric.SampleCount} fresh provider samples show {warningMetric.Label.ToLowerInvariant()} " +
              $"falling steadily at {Math.Abs(warningMetric.RatePerMinute):0.#}%/min. " +
              "This short-window estimate resets after a refill and cannot predict future activity."
            : string.Empty;
        return new VitalsTrendAnalysis(
            true,
            true,
            samples.Length,
            health,
            food,
            water,
            warning,
            warningHeading,
            warningDetail,
            compact);
    }

    internal static string FooterGlyph(VitalMetricTrend metric) => metric.Direction switch
    {
        VitalTrendDirection.Falling => "↓",
        VitalTrendDirection.Rising => "↑",
        VitalTrendDirection.Stable => "→",
        VitalTrendDirection.Mixed => "↕",
        _ => string.Empty
    };

    internal static string HealthRecoveryDetail(VitalMetricTrend health)
    {
        if (health.Label != "HP" || !health.Ready)
        {
            return "Healing evidence needs three fresh live HP samples spanning at least 50 seconds.";
        }

        return health.Direction switch
        {
            VitalTrendDirection.Rising when health.MinutesToBoundary is not null =>
                $"{health.SampleCount} fresh live samples show HP rising at about " +
                $"{health.RatePerMinute:0.#}%/min; about {health.MinutesToBoundary}m to full " +
                "if this short-window rate continues. Damage resets the estimate.",
            VitalTrendDirection.Rising =>
                $"{health.SampleCount} fresh live samples show HP rising at about " +
                $"{health.RatePerMinute:0.#}%/min. No bounded full-health ETA is available.",
            VitalTrendDirection.Stable =>
                $"{health.SampleCount} fresh live samples show HP stable across this short window. " +
                "No healing ETA is available.",
            VitalTrendDirection.Mixed =>
                "Live HP changes are mixed, so Isley refuses to estimate recovery time.",
            VitalTrendDirection.Falling =>
                "Live HP is falling. The healing estimate is unavailable and will relearn after damage.",
            _ => "Healing evidence is still learning."
        };
    }

    private static VitalMetricTrend AnalyzeHealth(IReadOnlyList<VitalsTrendSample> samples)
    {
        var segmentStart = 0;
        for (var index = 1; index < samples.Count; index++)
        {
            if (samples[index - 1].HealthPercent - samples[index].HealthPercent
                >= HealthDamageResetPercent)
            {
                segmentStart = index;
            }
        }

        var segment = samples.Skip(segmentStart).ToArray();
        var current = segment[^1].HealthPercent;
        var spanSeconds = segment.Length <= 1
            ? 0
            : (int)Math.Max(
                0,
                Math.Floor((segment[^1].CapturedAt - segment[0].CapturedAt).TotalSeconds));
        if (segment.Length < MinimumSamples || spanSeconds < MinimumSpanSeconds)
        {
            return HealthMetric(
                VitalTrendDirection.Learning,
                segment.Length,
                spanSeconds,
                current,
                0,
                null);
        }

        var slopes = new List<double>();
        for (var index = 1; index < segment.Length; index++)
        {
            var elapsedSeconds =
                (segment[index].CapturedAt - segment[index - 1].CapturedAt).TotalSeconds;
            if (elapsedSeconds is < 10 or > 180)
            {
                continue;
            }

            var slope =
                (segment[index].HealthPercent - segment[index - 1].HealthPercent)
                / elapsedSeconds
                * 60;
            if (double.IsFinite(slope) && Math.Abs(slope) <= 100)
            {
                slopes.Add(slope);
            }
        }

        if (slopes.Count < MinimumSamples - 1)
        {
            return HealthMetric(
                VitalTrendDirection.Learning,
                segment.Length,
                spanSeconds,
                current,
                0,
                null);
        }

        slopes.Sort();
        var rate = Median(slopes);
        var totalChange = current - segment[0].HealthPercent;
        var fallingShare = slopes.Count(slope => slope <= 0) / (double)slopes.Count;
        var risingShare = slopes.Count(slope => slope >= 0) / (double)slopes.Count;
        VitalTrendDirection direction;
        if (rate >= DirectionRatePercentPerMinute
            && totalChange >= MinimumDirectionalChangePercent
            && risingShare >= 0.67)
        {
            direction = VitalTrendDirection.Rising;
        }
        else if (rate <= -DirectionRatePercentPerMinute
                 && totalChange <= -MinimumDirectionalChangePercent
                 && fallingShare >= 0.67)
        {
            direction = VitalTrendDirection.Falling;
        }
        else if (Math.Abs(totalChange) < MinimumDirectionalChangePercent
                 || Math.Abs(rate) < DirectionRatePercentPerMinute)
        {
            direction = VitalTrendDirection.Stable;
        }
        else
        {
            direction = VitalTrendDirection.Mixed;
        }

        int? minutesToFull = null;
        if (direction == VitalTrendDirection.Rising && current < 100)
        {
            var estimate = (100 - current) / rate;
            if (double.IsFinite(estimate) && estimate is > 0 and <= MaximumHealthEtaMinutes)
            {
                minutesToFull = Math.Max(1, (int)Math.Ceiling(estimate));
            }
        }

        return HealthMetric(
            direction,
            segment.Length,
            spanSeconds,
            current,
            rate,
            minutesToFull);
    }

    private static VitalMetricTrend HealthMetric(
        VitalTrendDirection direction,
        int sampleCount,
        int spanSeconds,
        double current,
        double rate,
        int? minutesToFull) => new(
        "HP",
        direction,
        sampleCount,
        spanSeconds,
        current,
        rate,
        minutesToFull,
        100,
        "FULL");

    private static VitalMetricTrend AnalyzeMetric(
        string label,
        IReadOnlyList<VitalsTrendSample> samples,
        Func<VitalsTrendSample, double> value)
    {
        var segmentStart = 0;
        for (var index = 1; index < samples.Count; index++)
        {
            if (value(samples[index]) - value(samples[index - 1]) >= RefillResetPercent)
            {
                segmentStart = index;
            }
        }

        var segment = samples.Skip(segmentStart).ToArray();
        var current = value(segment[^1]);
        var spanSeconds = segment.Length <= 1
            ? 0
            : (int)Math.Max(0, Math.Floor((segment[^1].CapturedAt - segment[0].CapturedAt).TotalSeconds));
        if (segment.Length < MinimumSamples || spanSeconds < MinimumSpanSeconds)
        {
            return Metric(label, VitalTrendDirection.Learning, segment.Length, spanSeconds, current, 0, null);
        }

        var slopes = new List<double>();
        for (var index = 1; index < segment.Length; index++)
        {
            var elapsedSeconds = (segment[index].CapturedAt - segment[index - 1].CapturedAt).TotalSeconds;
            if (elapsedSeconds is < 10 or > 180) continue;
            var slope = (value(segment[index]) - value(segment[index - 1])) / elapsedSeconds * 60;
            if (double.IsFinite(slope) && Math.Abs(slope) <= 100) slopes.Add(slope);
        }

        if (slopes.Count < MinimumSamples - 1)
        {
            return Metric(label, VitalTrendDirection.Learning, segment.Length, spanSeconds, current, 0, null);
        }

        slopes.Sort();
        var rate = Median(slopes);
        var totalChange = current - value(segment[0]);
        var fallingShare = slopes.Count(slope => slope <= 0) / (double)slopes.Count;
        var risingShare = slopes.Count(slope => slope >= 0) / (double)slopes.Count;
        VitalTrendDirection direction;
        if (rate <= -DirectionRatePercentPerMinute
            && totalChange <= -MinimumDirectionalChangePercent
            && fallingShare >= 0.67)
        {
            direction = VitalTrendDirection.Falling;
        }
        else if (rate >= DirectionRatePercentPerMinute
                 && totalChange >= MinimumDirectionalChangePercent
                 && risingShare >= 0.67)
        {
            direction = VitalTrendDirection.Rising;
        }
        else if (Math.Abs(totalChange) < MinimumDirectionalChangePercent
                 || Math.Abs(rate) < DirectionRatePercentPerMinute)
        {
            direction = VitalTrendDirection.Stable;
        }
        else
        {
            direction = VitalTrendDirection.Mixed;
        }

        int? minutesToBoundary = null;
        if (direction == VitalTrendDirection.Falling)
        {
            var boundary = Boundary(current);
            var estimate = (current - boundary.Percent) / Math.Abs(rate);
            if (double.IsFinite(estimate) && estimate is > 0 and <= 60)
            {
                minutesToBoundary = Math.Max(1, (int)Math.Ceiling(estimate));
            }
        }

        return Metric(label, direction, segment.Length, spanSeconds, current, rate, minutesToBoundary);
    }

    private static VitalMetricTrend Metric(
        string label,
        VitalTrendDirection direction,
        int sampleCount,
        int spanSeconds,
        double current,
        double rate,
        int? minutesToBoundary)
    {
        var boundary = Boundary(current);
        return new VitalMetricTrend(
            label,
            direction,
            sampleCount,
            spanSeconds,
            current,
            rate,
            minutesToBoundary,
            boundary.Percent,
            boundary.Label);
    }

    private static (int Percent, string Label) Boundary(double current) => current switch
    {
        > 35 => (35, "LOW"),
        > 10 => (10, "CRITICAL"),
        _ => (0, "EMPTY")
    };

    private static string CompactMetric(VitalMetricTrend metric) => metric.Direction switch
    {
        VitalTrendDirection.Falling when metric.MinutesToBoundary is not null =>
            $"{metric.Label} ↓ {metric.MinutesToBoundary}M TO {metric.BoundaryLabel}",
        VitalTrendDirection.Falling => $"{metric.Label} ↓",
        VitalTrendDirection.Rising => $"{metric.Label} ↑",
        VitalTrendDirection.Stable => $"{metric.Label} →",
        VitalTrendDirection.Mixed => $"{metric.Label} ↕",
        _ => $"{metric.Label} … {Math.Min(metric.SampleCount, MinimumSamples)}/{MinimumSamples}"
    };

    private static string CompactHealth(VitalMetricTrend health) => health.Direction switch
    {
        VitalTrendDirection.Rising when health.MinutesToBoundary is not null =>
            $"HP ↑ {health.MinutesToBoundary}M TO FULL",
        VitalTrendDirection.Rising => "HP ↑",
        VitalTrendDirection.Falling => "HP ↓",
        VitalTrendDirection.Stable when health.CurrentPercent >= 100 => "HP FULL",
        VitalTrendDirection.Stable => "HP →",
        VitalTrendDirection.Mixed => "HP ↕",
        _ => $"HP … {Math.Min(health.SampleCount, MinimumSamples)}/{MinimumSamples}"
    };

    private static double Median(IReadOnlyList<double> sorted)
    {
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static bool IsValid(VitalsTrendSample sample) =>
        sample.CapturedAt != default
        && IsPercent(sample.HealthPercent)
        && IsPercent(sample.FoodPercent)
        && IsPercent(sample.WaterPercent)
        && IsPercent(sample.GrowthPercent);

    private static bool IsPercent(double value) => double.IsFinite(value) && value is >= 0 and <= 100;

    private static VitalsTrendAnalysis Empty(string compact, int sampleCount = 0) => new(
        false,
        false,
        sampleCount,
        default,
        default,
        default,
        false,
        string.Empty,
        string.Empty,
        compact);
}
