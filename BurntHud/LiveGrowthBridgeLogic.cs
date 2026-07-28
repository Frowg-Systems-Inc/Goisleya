namespace Isley;

internal enum LiveGrowthBridgeState
{
    Unavailable,
    ReadyToStart,
    Drifted,
    Matched
}

internal readonly record struct LiveGrowthBridgeSnapshot(
    bool LiveFresh,
    bool LifeRunActive,
    int ManualGrowthPercent,
    int ManualPrimeCompleted,
    int ManualPrimeRequired,
    int LiveGrowthPercent,
    bool LivePrimeAvailable,
    int LivePrimeCompleted,
    int LivePrimeRequired,
    int LivePrimeTotal);

internal readonly record struct LiveGrowthBridgeView(
    LiveGrowthBridgeState State,
    bool Available,
    bool UsesLiveGrowth,
    bool CanAdopt,
    int ManualGrowthPercent,
    int LiveGrowthPercent,
    int EffectiveGrowthPercent,
    int EffectiveStageIndex,
    int DriftPercent,
    bool PrimeAvailable,
    int PrimeCompleted,
    int PrimeRequired,
    int PrimeTotal,
    bool PrimeReady,
    string StateLabel,
    string ValueLabel,
    string Detail,
    string ActionLabel);

internal static class LiveGrowthBridgeLogic
{
    internal static LiveGrowthBridgeView Analyze(LiveGrowthBridgeSnapshot snapshot)
    {
        var manualGrowth = Math.Clamp(snapshot.ManualGrowthPercent, 0, 100);
        var manualPrimeCompleted = Math.Clamp(snapshot.ManualPrimeCompleted, 0, 10);
        var manualPrimeRequired = Math.Clamp(snapshot.ManualPrimeRequired, 1, 10);
        var liveGrowth = Math.Clamp(snapshot.LiveGrowthPercent, 0, 100);
        var livePrimeAvailable = snapshot.LivePrimeAvailable
                                 && snapshot.LivePrimeCompleted is >= 0 and <= 10
                                 && snapshot.LivePrimeRequired is >= 1 and <= 10
                                 && snapshot.LivePrimeTotal is >= 1 and <= 10
                                 && snapshot.LivePrimeCompleted <= snapshot.LivePrimeTotal
                                 && snapshot.LivePrimeRequired <= snapshot.LivePrimeTotal;

        if (!snapshot.LiveFresh)
        {
            return new LiveGrowthBridgeView(
                LiveGrowthBridgeState.Unavailable,
                false,
                false,
                false,
                manualGrowth,
                liveGrowth,
                manualGrowth,
                GrowthPlannerLogic.StageIndex(manualGrowth),
                0,
                false,
                manualPrimeCompleted,
                manualPrimeRequired,
                10,
                manualPrimeCompleted >= manualPrimeRequired,
                "MANUAL SOURCE",
                $"SAVED {manualGrowth}% · PRIME {manualPrimeCompleted}/{manualPrimeRequired}",
                "Live Growth is waiting; manual controls remain authoritative.",
                "LIVE WAITING");
        }

        var primeCompleted = livePrimeAvailable ? snapshot.LivePrimeCompleted : manualPrimeCompleted;
        var primeRequired = livePrimeAvailable ? snapshot.LivePrimeRequired : manualPrimeRequired;
        var primeTotal = livePrimeAvailable ? snapshot.LivePrimeTotal : 10;
        var drift = liveGrowth - manualGrowth;
        var state = !snapshot.LifeRunActive
            ? LiveGrowthBridgeState.ReadyToStart
            : drift == 0
                ? LiveGrowthBridgeState.Matched
                : LiveGrowthBridgeState.Drifted;
        var stateLabel = state switch
        {
            LiveGrowthBridgeState.ReadyToStart => "LIVE SOURCE · NO SAVED RUN",
            LiveGrowthBridgeState.Matched => "LIVE SOURCE · RUN MATCHED",
            _ => drift > 0
                ? $"LIVE SOURCE · RUN {drift}% BEHIND"
                : $"LIVE SOURCE · RUN {Math.Abs(drift)}% AHEAD"
        };
        var primeLabel = livePrimeAvailable
            ? $" · PRIME {primeCompleted}/{primeRequired}"
            : " · PRIME MANUAL";
        var detail = state switch
        {
            LiveGrowthBridgeState.ReadyToStart =>
                "Use the live percentage to begin a local Life Run; Prime stays read-only until verified in game.",
            LiveGrowthBridgeState.Matched =>
                "Saved growth matches the live dinosaur; Prime remains a read-only planning signal.",
            _ =>
                "Live values guide Isley now; sync only when you want to update the saved Life Run."
        };
        var actionLabel = state switch
        {
            LiveGrowthBridgeState.ReadyToStart => $"START @ {liveGrowth}%",
            LiveGrowthBridgeState.Drifted => $"SYNC @ {liveGrowth}%",
            _ => "MATCHED"
        };

        return new LiveGrowthBridgeView(
            state,
            true,
            true,
            state is LiveGrowthBridgeState.ReadyToStart or LiveGrowthBridgeState.Drifted,
            manualGrowth,
            liveGrowth,
            liveGrowth,
            GrowthPlannerLogic.StageIndex(liveGrowth),
            drift,
            livePrimeAvailable,
            primeCompleted,
            primeRequired,
            primeTotal,
            primeCompleted >= primeRequired,
            stateLabel,
            $"LIVE {liveGrowth}%{primeLabel}",
            detail,
            actionLabel);
    }
}
