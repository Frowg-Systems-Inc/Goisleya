using System.Text.Json;
using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

Check(PlannerStateStoreLogic.MaximumDocumentBytes == 64 * 1024, "the planner store stays bounded at 64 KB");
Check(PlannerStateStoreLogic.StoreFileName == "planner-state.json", "the store file name is stable");
Check(PlannerStateDocument.CurrentSchemaVersion == 1, "the store schema version is stable");

Check(PlannerStateStoreLogic.ResolvePath(null) is null, "null settings paths resolve to null");
Check(PlannerStateStoreLogic.ResolvePath("   ") is null, "blank settings paths resolve to null");
var settingsPath = Path.Combine(Path.GetTempPath(), "isley-planner-store-test", "settings.json");
Check(PlannerStateStoreLogic.ResolvePath(settingsPath)
        == Path.Combine(Path.GetTempPath(), "isley-planner-store-test", "planner-state.json"),
    "the store lives beside the settings file");

var defaults = PlannerStateStoreLogic.Normalize(null);
Check(defaults.SchemaVersion == 1
      && defaults.Growth is { Percent: 25, ServerMultiplierIndex: 0, Paused: false }
      && defaults.Nest is { Active: false, EggTarget: 2, TimerDurationIndex: 1,
          AutoHatchGuidanceEnabled: true, TimerAlertPresetIndex: 0 }
      && defaults.Mutation.Loadout.Count == 0
      && defaults.Mutation.UnlockProgress.Count == 0
      && defaults.RatePresets.SelectedPresetId.Length == 0
      && defaults.RatePresets.CustomPresets.Count == 0,
    "a missing document normalizes to the honest defaults");

var hostile = new PlannerStateDocument
{
    SchemaVersion = 99,
    Growth = new PlannerGrowthState { Percent = 250, ServerMultiplierIndex = 99, Paused = true },
    Nest = new PlannerNestState
    {
        Active = false,
        PhaseIndex = 7,
        PartnerReady = true,
        EggTarget = 99,
        EggsLaid = 99,
        EggsHatched = 99,
        YoungRaised = 99,
        AccessIndex = 9,
        TimerDurationIndex = 99,
        TimerAlertPresetIndex = -3
    },
    Stats = new PlannerStatsState { CaptureStreakCurrent = -5, CaptureStreakBest = 3 },
    RatePresets = new PlannerRatePresetState
    {
        SelectedPresetId = "does-not-exist",
        CustomPresets =
        [
            new PlannerRatePresetItemState { Id = "custom-1", Label = "First", MultiplierIndex = 1 },
            new PlannerRatePresetItemState { Id = "official-9", Label = "NotCustom", MultiplierIndex = 3 }
        ]
    }
};
var tamed = PlannerStateStoreLogic.Normalize(hostile);
Check(tamed.SchemaVersion == PlannerStateDocument.CurrentSchemaVersion,
    "normalize always rewrites the schema version to current");
Check(tamed.Growth.Percent == 100, "growth percent clamps to 100");
Check(tamed.Growth.ServerMultiplierIndex == GrowthPlannerLogic.ServerMultipliers.Length - 1,
    "server multiplier index clamps to the known roster");
Check(tamed.Growth.Paused, "the paused flag survives normalization");
Check(tamed.Nest is { Active: false, PhaseIndex: 0, PartnerReady: false, EggsLaid: 0, EggsHatched: 0,
      YoungRaised: 0 },
    "an inactive nest cannot carry progress");
Check(tamed.Nest.EggTarget == NestPlannerLogic.MaxEggs, "egg targets clamp to the nest cap");
Check(tamed.Nest.AccessIndex == 1, "nest access clamps to the known range");
Check(tamed.Nest.TimerDurationIndex == NestPlannerLogic.TimerMinutes.Length - 1,
    "timer duration index clamps to the known roster");
Check(tamed.Nest.TimerAlertPresetIndex == 0, "timer alert preset index clamps to the known roster");
Check(tamed.Stats.CaptureStreakCurrent == 0 && tamed.Stats.CaptureStreakBest == 3,
    "capture streaks never go negative");
Check(tamed.RatePresets.SelectedPresetId.Length == 0,
    "a selected preset that does not resolve is cleared");
Check(tamed.RatePresets.CustomPresets.Count == 1
      && tamed.RatePresets.CustomPresets[0].Id == "custom-1",
    "custom presets normalize through the rate-preset contract");

var serialized = PlannerStateStoreLogic.Serialize(hostile);
Check(serialized.Length <= PlannerStateStoreLogic.MaximumDocumentBytes,
    "serialization stays inside the byte cap");
Check(serialized.Contains("\"SchemaVersion\": 1"),
    "serialization always writes the current schema version");

var storeDirectory = Path.Combine(
    Path.GetTempPath(),
    $"isley-planner-store-verifier-{Guid.NewGuid():N}");
try
{
    var storePath = Path.Combine(storeDirectory, PlannerStateStoreLogic.StoreFileName);
    Check(!PlannerStateStoreLogic.TryRead(storePath, out _, out _),
        "a missing store reads as absent");

    Directory.CreateDirectory(storeDirectory);
    var oversizedPath = storePath.Replace(".json", "-oversized.json");
    File.WriteAllText(oversizedPath, new string('x', 64 * 1024 + 1));
    Check(!PlannerStateStoreLogic.TryRead(oversizedPath, out _, out _),
        "an oversized store reads as absent");

    File.WriteAllText(storePath, "{ not json");
    Check(!PlannerStateStoreLogic.TryRead(storePath, out _, out _),
        "a malformed store reads as absent");

    File.WriteAllText(storePath, JsonSerializer.Serialize(new { SchemaVersion = 2 }));
    Check(!PlannerStateStoreLogic.TryRead(storePath, out _, out var foreignSchema) && foreignSchema,
        "a newer schema reports foreign and is never read");

    File.WriteAllText(storePath, JsonSerializer.Serialize(new { SchemaVersion = 0 }));
    Check(!PlannerStateStoreLogic.TryRead(storePath, out _, out foreignSchema) && !foreignSchema,
        "a legacy schema reads as absent without reporting foreign");

    Check(!PlannerStateStoreLogic.TryWrite(null, defaults), "null paths never write");
    Check(PlannerStateStoreLogic.TryWrite(storePath, hostile),
        "a valid document writes atomically");
    Check(PlannerStateStoreLogic.TryRead(storePath, out var roundTripped, out foreignSchema)
          && !foreignSchema
          && roundTripped is not null
          && roundTripped.Growth.Percent == 100
          && roundTripped.Growth.Paused
          && roundTripped.Nest.EggTarget == NestPlannerLogic.MaxEggs
          && roundTripped.RatePresets.CustomPresets.Count == 1,
        "a written store round-trips through normalization");
    Check(!Directory.EnumerateFiles(storeDirectory, "*.tmp").Any(),
        "atomic writes leave no temporary files behind");
}
finally
{
    try { Directory.Delete(storeDirectory, recursive: true); } catch { }
}

Console.WriteLine(
    "Planner state store verification passed (path resolution, normalization clamps, schema gating, byte caps, and atomic round trips).");
