using System.IO;
using System.Text.Json;

namespace Isley;

internal sealed class PlannerStateDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public PlannerGrowthState Growth { get; set; } = new();
    public PlannerNestState Nest { get; set; } = new();
    public PlannerMutationState Mutation { get; set; } = new();
    public PlannerSpawnState Spawn { get; set; } = new();
    public PlannerStatsState Stats { get; set; } = new();
    public PlannerRatePresetState RatePresets { get; set; } = new();
}

internal sealed class PlannerGrowthState
{
    public int Percent { get; set; } = 25;
    public int ServerMultiplierIndex { get; set; }
    public bool Paused { get; set; }
}

internal sealed class PlannerNestState
{
    public bool Active { get; set; }
    public int PhaseIndex { get; set; }
    public bool PartnerReady { get; set; }
    public bool SiteReady { get; set; }
    public bool DebrisReady { get; set; }
    public bool ReservesReady { get; set; }
    public int AccessIndex { get; set; }
    public int EggTarget { get; set; } = 2;
    public int EggsLaid { get; set; }
    public int EggsHatched { get; set; }
    public int YoungRaised { get; set; }
    public int TimerDurationIndex { get; set; } = 1;
    public bool AutoHatchGuidanceEnabled { get; set; } = true;
    public int TimerAlertPresetIndex { get; set; }
}

internal sealed class PlannerMutationItemState
{
    public int Slot { get; set; }
    public string MutationId { get; set; } = string.Empty;
    public int Status { get; set; }
}

internal sealed class PlannerMutationUnlockState
{
    public string ChallengeId { get; set; } = string.Empty;
    public int Value { get; set; }
}

internal sealed class PlannerMutationState
{
    public List<PlannerMutationItemState> Loadout { get; set; } = [];
    public int BuildFocusIndex { get; set; }
    public int UnlockSelectedIndex { get; set; }
    public List<PlannerMutationUnlockState> UnlockProgress { get; set; } = [];
}

internal sealed class PlannerSpawnState
{
    public bool CoverReady { get; set; }
    public bool ScentChecked { get; set; }
    public bool WaterFound { get; set; }
    public bool FoodFound { get; set; }
}

internal sealed class PlannerStatsState
{
    public int CaptureStreakCurrent { get; set; }
    public int CaptureStreakBest { get; set; }
}

internal sealed class PlannerRatePresetItemState
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int MultiplierIndex { get; set; }
}

internal sealed class PlannerRatePresetState
{
    public string SelectedPresetId { get; set; } = string.Empty;
    public List<PlannerRatePresetItemState> CustomPresets { get; set; } = [];
}

// One schema-versioned store for every planner (growth, nest, mutation, spawn) plus the
// planner-scoped stats and rate presets. It mirrors the MapperSettings SchemaVersion
// pattern: the version is written on every save, unknown newer versions are never
// read or overwritten (fail-closed), and everything is bounded and normalized on load.
internal static class PlannerStateStoreLogic
{
    internal const int MaximumDocumentBytes = 64 * 1024;
    internal const string StoreFileName = "planner-state.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    internal static string? ResolvePath(string? settingsFilePath)
    {
        if (string.IsNullOrWhiteSpace(settingsFilePath))
        {
            return null;
        }

        try
        {
            var directory = Path.GetDirectoryName(settingsFilePath);
            return string.IsNullOrWhiteSpace(directory)
                ? null
                : Path.Combine(directory, StoreFileName);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    internal static PlannerStateDocument Normalize(PlannerStateDocument? document)
    {
        var source = document ?? new PlannerStateDocument();
        var nest = NestPlannerLogic.Normalize(new NestPlannerSnapshot(
            source.Nest.Active,
            source.Nest.PhaseIndex,
            source.Nest.PartnerReady,
            source.Nest.SiteReady,
            source.Nest.DebrisReady,
            source.Nest.ReservesReady,
            source.Nest.AccessIndex,
            source.Nest.EggTarget,
            source.Nest.EggsLaid,
            source.Nest.EggsHatched,
            source.Nest.YoungRaised,
            source.Nest.TimerDurationIndex));
        var streak = LifeRunLogic.NormalizeCaptureStreak(new LifeRunCaptureStreak(
            source.Stats.CaptureStreakCurrent,
            source.Stats.CaptureStreakBest));
        var customPresets = ServerRatePresetLogic.NormalizeCustomPresets(
            (source.RatePresets.CustomPresets ?? []).Select(item => new ServerRatePreset(
                item?.Id ?? string.Empty,
                item?.Label ?? string.Empty,
                item?.MultiplierIndex ?? 0)));
        var selectedPresetId = ServerRatePresetLogic.SanitizeId(source.RatePresets.SelectedPresetId);
        if (ServerRatePresetLogic.Find(
                ServerRatePresetLogic.All(customPresets),
                selectedPresetId) is null)
        {
            selectedPresetId = string.Empty;
        }

        return new PlannerStateDocument
        {
            SchemaVersion = PlannerStateDocument.CurrentSchemaVersion,
            Growth = new PlannerGrowthState
            {
                Percent = Math.Clamp(source.Growth.Percent, 0, 100),
                ServerMultiplierIndex = ServerRatePresetLogic.NormalizeMultiplierIndex(
                    source.Growth.ServerMultiplierIndex),
                Paused = source.Growth.Paused
            },
            Nest = new PlannerNestState
            {
                Active = nest.Active,
                PhaseIndex = nest.PhaseIndex,
                PartnerReady = nest.PartnerReady,
                SiteReady = nest.SiteReady,
                DebrisReady = nest.DebrisReady,
                ReservesReady = nest.ReservesReady,
                AccessIndex = nest.AccessIndex,
                EggTarget = nest.EggTarget,
                EggsLaid = nest.EggsLaid,
                EggsHatched = nest.EggsHatched,
                YoungRaised = nest.YoungRaised,
                TimerDurationIndex = nest.TimerDurationIndex,
                AutoHatchGuidanceEnabled = source.Nest.AutoHatchGuidanceEnabled,
                TimerAlertPresetIndex = NestTimerAlertLogic.NormalizePresetIndex(
                    source.Nest.TimerAlertPresetIndex)
            },
            Mutation = new PlannerMutationState
            {
                Loadout = MutationPlannerLogic.NormalizeLoadout(
                    (source.Mutation.Loadout ?? []).Select(item => new MutationLoadoutItem(
                        item?.Slot ?? 0,
                        item?.MutationId ?? string.Empty,
                        item?.Status ?? 0)))
                    .Select(item => new PlannerMutationItemState
                    {
                        Slot = item.Slot,
                        MutationId = item.MutationId,
                        Status = item.Status
                    })
                    .ToList(),
                BuildFocusIndex = MutationBuildLogic.NormalizeFocusIndex(source.Mutation.BuildFocusIndex),
                UnlockSelectedIndex = MutationUnlockLogic.NormalizeSelectedIndex(
                    source.Mutation.UnlockSelectedIndex),
                UnlockProgress = MutationUnlockLogic.NormalizeProgress(
                    (source.Mutation.UnlockProgress ?? []).Select(item => new MutationUnlockProgress(
                        item?.ChallengeId ?? string.Empty,
                        item?.Value ?? 0)))
                    .Select(item => new PlannerMutationUnlockState
                    {
                        ChallengeId = item.ChallengeId,
                        Value = item.Value
                    })
                    .ToList()
            },
            Spawn = new PlannerSpawnState
            {
                CoverReady = source.Spawn.CoverReady,
                ScentChecked = source.Spawn.ScentChecked,
                WaterFound = source.Spawn.WaterFound,
                FoodFound = source.Spawn.FoodFound
            },
            Stats = new PlannerStatsState
            {
                CaptureStreakCurrent = streak.Current,
                CaptureStreakBest = streak.Best
            },
            RatePresets = new PlannerRatePresetState
            {
                SelectedPresetId = selectedPresetId,
                CustomPresets = customPresets
                    .Select(preset => new PlannerRatePresetItemState
                    {
                        Id = preset.Id,
                        Label = preset.Label,
                        MultiplierIndex = preset.MultiplierIndex
                    })
                    .ToList()
            }
        };
    }

    internal static string Serialize(PlannerStateDocument document) =>
        JsonSerializer.Serialize(Normalize(document), SerializerOptions);

    // Reads the store. A missing, oversized, malformed, or legacy-versioned file is
    // treated as absent so the caller migrates from the legacy per-planner keys. A newer
    // schema reports foreignSchema and is never read or overwritten (fail-closed).
    internal static bool TryRead(
        string? path,
        out PlannerStateDocument? document,
        out bool foreignSchema)
    {
        document = null;
        foreignSchema = false;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string text;
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumDocumentBytes)
            {
                return false;
            }

            text = File.ReadAllText(path);
        }
        catch
        {
            return false;
        }

        PlannerStateDocument? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PlannerStateDocument>(text);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return false;
        }

        if (parsed is null)
        {
            return false;
        }

        if (parsed.SchemaVersion > PlannerStateDocument.CurrentSchemaVersion)
        {
            foreignSchema = true;
            return false;
        }

        if (parsed.SchemaVersion < 1)
        {
            return false;
        }

        document = Normalize(parsed);
        return true;
    }

    internal static bool TryWrite(string? path, PlannerStateDocument document)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string serialized;
        try
        {
            serialized = Serialize(document);
        }
        catch
        {
            return false;
        }

        if (serialized.Length > MaximumDocumentBytes)
        {
            return false;
        }

        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporaryPath, serialized);
            File.Move(temporaryPath, path, overwrite: true);
            temporaryPath = null;
            if (!File.Exists(path)
                || !string.Equals(File.ReadAllText(path), serialized, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(temporaryPath))
            {
                try { File.Delete(temporaryPath); } catch { }
            }

            return false;
        }
    }
}
