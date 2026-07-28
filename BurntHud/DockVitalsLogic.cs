namespace Isley;

internal sealed record DockVitalsPresentation(
    bool Visible,
    string SourceLabel,
    string ValuesLabel,
    string Tooltip,
    int Severity,
    bool Fresh)
{
    internal static DockVitalsPresentation Hidden { get; } = new(
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        0,
        false);
}

internal static class DockVitalsLogic
{
    internal static DockVitalsPresentation Resolve(
        bool requestedVisible,
        bool streamerMode,
        bool liveMapServicesActive,
        PlayerSnapshotEvaluation snapshot,
        CoreVitalsGuidance guidance,
        VitalMetricTrend? healthTrend = null,
        VisibleHudSensorSample? visibleHudSample = null)
    {
        if (!requestedVisible || streamerMode)
        {
            return DockVitalsPresentation.Hidden;
        }

        var severity = guidance.Critical ? 2 : guidance.Warning ? 1 : 0;
        if (liveMapServicesActive && snapshot.LiveFresh)
        {
            var healthGlyph = healthTrend is { } trend
                ? VitalsTrendLogic.FooterGlyph(trend)
                : string.Empty;
            var healingDetail = healthTrend is { Rising: true } rising
                ? $" {VitalsTrendLogic.HealthRecoveryDetail(rising)}"
                : string.Empty;
            return new DockVitalsPresentation(
                true,
                $"LIVE {PlayerSnapshotLogic.FormatAge(snapshot.AgeSeconds)}",
                $"HP{snapshot.HealthPercent}{healthGlyph}  F{snapshot.FoodPercent}  W{snapshot.WaterPercent}  " +
                $"ST{CoreVitalsLogic.ShortLabel(guidance.Stamina)}",
                "Live signed-in Live Map HP, food, and water. Stamina remains the current manual band." +
                healingDetail +
                " Select to inspect or report Core Vitals.",
                severity,
                true);
        }

        if (visibleHudSample is { } visible)
        {
            return new DockVitalsPresentation(
                true,
                "HUD ESTIMATE",
                $"~HP{visible.HealthPercent}  ~F{visible.FoodPercent}  " +
                $"~W{visible.WaterPercent}  ~ST{visible.StaminaPercent}",
                "Broad estimates sampled only from the visible The Isle HUD. " +
                "No game memory, packets, input, or screenshots are stored. Select to inspect.",
                severity,
                true);
        }

        if (guidance.HasFreshReport)
        {
            var source = snapshot.Stale
                ? "MANUAL / LIVE STALE"
                : snapshot.LastKnown
                    ? "MANUAL / LAST DINO"
                    : "MANUAL CURRENT";
            return new DockVitalsPresentation(
                true,
                source,
                ManualValues(guidance),
                $"Current player-reported bands. {guidance.Freshness}. Select to inspect or update Core Vitals.",
                severity,
                true);
        }

        var waitingSource = snapshot.Stale
            ? "STALE / REPORT"
            : snapshot.LastKnown
                ? "LAST DINO / REPORT"
                : liveMapServicesActive
                    ? "WAITING / REPORT"
                    : "MANUAL / REPORT";
        return new DockVitalsPresentation(
            true,
            waitingSource,
            "HP?  F?  W?  ST?",
            snapshot.LastKnown
                ? "Last-dinosaur values are reference-only. Select to report the current in-game vital bands."
                : snapshot.Stale
                    ? "The live snapshot expired and is excluded from decisions. Select to report current in-game vital bands."
                    : "No fresh vital report is available. Select to inspect or report the in-game bands.",
            0,
            false);
    }

    private static string ManualValues(CoreVitalsGuidance guidance) =>
        $"HP{SurvivalAssistantLogic.HealthLabel(guidance.Health)}  " +
        $"F{CoreVitalsLogic.ShortLabel(guidance.Food)}  " +
        $"W{CoreVitalsLogic.ShortLabel(guidance.Water)}  " +
        $"ST{CoreVitalsLogic.ShortLabel(guidance.Stamina)}";
}
