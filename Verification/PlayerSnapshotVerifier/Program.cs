using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var now = DateTimeOffset.UnixEpoch.AddHours(12);

Check(PlayerSnapshotLogic.LiveRefreshMilliseconds(liteMode: false) == 2_000
      && PlayerSnapshotLogic.LiveRefreshMilliseconds(liteMode: true) == 5_000
      && PlayerSnapshotLogic.LastKnownRefreshMilliseconds == 60_000
      && PlayerSnapshotLogic.ErrorRetryMilliseconds == 5_000
      && PlayerSnapshotLogic.MaximumErrorRetryMilliseconds == 60_000
      && PlayerSnapshotLogic.FreshnessSeconds == 15,
    "live update cadence policy failed");

var live = PlayerSnapshotLogic.Evaluate(new PlayerSnapshotRaw(
    PlayerSnapshotSourceState.Live,
    "carnotaurus",
    35,
    88, 100,
    32, 50,
    720, 1000,
    2, 5, 10,
    now.AddSeconds(-12)), now);
Check(live.HasValidData
      && live.LiveFresh
      && live.AgeSeconds == 12
      && live.SpeciesAvailable
      && live.SpeciesId == "carnotaurus"
      && live.GrowthPercent == 35
      && live.HealthPercent == 88
      && live.FoodPercent == 64
      && live.WaterPercent == 72
      && live.HealthState == ReportedHealthState.Stable
      && live.FoodState == ReportedVitalState.Stable
      && live.PrimeAvailable
      && live.PrimeCompleted == 2
      && live.PrimeRequired == 5,
    "fresh live snapshot failed");
Check(PlayerSnapshotLogic.CompactLabel(live, ReportedVitalState.Low)
      == "HP 88 · F 64 · W 72 · ST LOW",
    "live compact strip failed");

var thresholds = PlayerSnapshotLogic.Evaluate(new PlayerSnapshotRaw(
    PlayerSnapshotSourceState.Live,
    null,
    100,
    25, 100,
    10, 100,
    35, 100,
    null, null, null,
    now), now);
Check(thresholds.HealthState == ReportedHealthState.Critical
      && thresholds.FoodState == ReportedVitalState.Empty
      && thresholds.WaterState == ReportedVitalState.Low
      && !thresholds.PrimeAvailable,
    "critical thresholds failed");

var lastDino = PlayerSnapshotLogic.Evaluate(new PlayerSnapshotRaw(
    PlayerSnapshotSourceState.LastKnown,
    "deinosuchus",
    42,
    0, 148,
    1, 49,
    807, 1000,
    2, 5, 10,
    now), now);
Check(lastDino.HasValidData
      && lastDino.LastKnown
      && !lastDino.LiveFresh
      && lastDino.SpeciesId == "deinosuchus"
      && lastDino.HealthState == ReportedHealthState.Unknown
      && lastDino.FoodState == ReportedVitalState.Unknown
      && PlayerSnapshotLogic.CompactLabel(lastDino, ReportedVitalState.Stable) == string.Empty,
    "offline last-dino isolation failed");

var stale = PlayerSnapshotLogic.Evaluate(new PlayerSnapshotRaw(
    PlayerSnapshotSourceState.Live,
    "kentrosaurus",
    80,
    100, 100,
    100, 100,
    100, 100,
    5, 5, 10,
    now.AddSeconds(-PlayerSnapshotLogic.FreshnessSeconds)), now);
Check(stale.Stale
      && !stale.LiveFresh
      && stale.HealthState == ReportedHealthState.Unknown,
    "stale live snapshot failed closed");

var invalidSpecies = PlayerSnapshotLogic.Evaluate(new PlayerSnapshotRaw(
    PlayerSnapshotSourceState.Live,
    "carnotaurus<script>",
    50,
    100, 100,
    50, 100,
    50, 100,
    2, 5, 10,
    now), now);
Check(invalidSpecies.HasValidData
      && invalidSpecies.LiveFresh
      && !invalidSpecies.SpeciesAvailable
      && invalidSpecies.SpeciesId == string.Empty,
    "invalid species identifier was not isolated");

var invalid = PlayerSnapshotLogic.Evaluate(new PlayerSnapshotRaw(
    PlayerSnapshotSourceState.Live,
    "carnotaurus<script>",
    101,
    120, 100,
    1, 0,
    -1, 100,
    11, 5, 10,
    now), now);
Check(!invalid.HasValidData && !invalid.LiveFresh,
    "invalid numeric snapshot was accepted");

Console.WriteLine("Player snapshot: PASS (2s/5s cadence, validation, freshness, species identifier bounds, thresholds, exact strip, Prime, and offline isolation)");
