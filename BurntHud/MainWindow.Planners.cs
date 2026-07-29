using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Net.WebSockets;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Isley.Telemetry;
using Microsoft.Web.WebView2.Core;

namespace Isley;

public partial class MainWindow
{
    private void ClearGrowthGateWatchSession()
    {
        _lastGrowthGateSample = null;
        _growthGatePending = null;
        _growthGateUiSignature = string.Empty;
        _growthPlannerUiSignature = string.Empty;
    }

    private void ClearLifeTransitionSession()
    {
        ClearGrowthGateWatchSession();
        _lastLiveDinoSample = null;
        _lifeTransitionPending = null;
        _lifeTransitionUiSignature = string.Empty;
        _lifeRunUiSignature = string.Empty;
        _nextMoveUiSignature = string.Empty;
    }

    private int CurrentServerGrowthMultiplierIndex()
    {
        if (CommunitySessionActive)
        {
            return CurrentCommunityServerProfile().GrowthMultiplierIndex;
        }

        return ServerSessionLogic.Find(_serverSessionProfileId).SuggestedGrowthMultiplierIndex;
    }

    private LiveSpeciesBridgeView CurrentLiveSpeciesBridge(DateTimeOffset? now = null)
    {
        var playerSnapshot = CurrentPlayerSnapshotEvaluation(now ?? DateTimeOffset.UtcNow);
        return LiveSpeciesBridgeLogic.Analyze(new LiveSpeciesBridgeSnapshot(
            LiveMapServicesActive && playerSnapshot.LiveFresh,
            _lifeRunActive,
            _dietSpeciesIndex,
            playerSnapshot.SpeciesId));
    }

    private int CurrentEffectiveSpeciesIndex(DateTimeOffset? now = null) =>
        CurrentLiveSpeciesBridge(now).EffectiveSpeciesIndex;

    private string CurrentEffectiveSpeciesId(DateTimeOffset? now = null)
    {
        var bridge = CurrentLiveSpeciesBridge(now);
        if (bridge.Available) return bridge.LiveSpeciesId;
        return _dietSpeciesIndex > 0 && _dietSpeciesIndex <= DietCoachLogic.Species.Length
            ? DietCoachLogic.Species[_dietSpeciesIndex - 1].Id
            : _guideSelectedSpeciesId;
    }

    private LiveGrowthBridgeView CurrentLiveGrowthBridge(DateTimeOffset? now = null)
    {
        var playerSnapshot = CurrentPlayerSnapshotEvaluation(now ?? DateTimeOffset.UtcNow);
        return LiveGrowthBridgeLogic.Analyze(new LiveGrowthBridgeSnapshot(
            LiveMapServicesActive && playerSnapshot.LiveFresh,
            _lifeRunActive,
            _lifeRunGrowthPercent,
            LifeRunPrimeConditionCount(),
            LifeRunPrimeRequiredConditionCount(),
            playerSnapshot.GrowthPercent,
            playerSnapshot.PrimeAvailable,
            playerSnapshot.PrimeCompleted,
            playerSnapshot.PrimeRequired,
            playerSnapshot.PrimeTotal));
    }

    private GrowthPlannerResult CurrentGrowthPlannerResult()
    {
        var diet = DietCoachLogic.Analyze(_dietSlot1, _dietSlot2, _dietSlot3, _dietTargetIndex);
        var bridge = CurrentLiveGrowthBridge();
        return GrowthPlannerLogic.Analyze(new GrowthPlannerSnapshot(
            CurrentEffectiveSpeciesIndex(),
            bridge.EffectiveGrowthPercent,
            _growthServerMultiplierIndex,
            diet.FilledCount,
            _growthPaused,
            bridge.PrimeCompleted,
            bridge.PrimeRequired));
    }

    private NestPlannerSnapshot CurrentNestPlannerSnapshot() => NestPlannerLogic.Normalize(new NestPlannerSnapshot(
        _nestPlannerActive,
        _nestPhaseIndex,
        _nestPartnerReady,
        _nestSiteReady,
        _nestDebrisReady,
        _nestReservesReady,
        _nestAccessIndex,
        _nestEggTarget,
        _nestEggsLaid,
        _nestEggsHatched,
        _nestYoungRaised,
        _nestTimerDurationIndex));

    private void ApplyNestPlannerSnapshot(NestPlannerSnapshot snapshot)
    {
        var normalized = NestPlannerLogic.Normalize(snapshot);
        _nestPlannerActive = normalized.Active;
        _nestPhaseIndex = normalized.PhaseIndex;
        _nestPartnerReady = normalized.PartnerReady;
        _nestSiteReady = normalized.SiteReady;
        _nestDebrisReady = normalized.DebrisReady;
        _nestReservesReady = normalized.ReservesReady;
        _nestAccessIndex = normalized.AccessIndex;
        _nestEggTarget = normalized.EggTarget;
        _nestEggsLaid = normalized.EggsLaid;
        _nestEggsHatched = normalized.EggsHatched;
        _nestYoungRaised = normalized.YoungRaised;
        _nestTimerDurationIndex = normalized.TimerDurationIndex;
    }

    private LifeRunSnapshot CurrentLifeRunSnapshot() => new(
        _lifeRunStageIndex,
        _lifeRunSanctuaryVisited,
        _lifeRunPerfectDiet,
        _lifeRunNestedIn,
        _lifeRunRaisedYoung,
        _lifeRunMigrationVisits,
        _lifeRunPatrolVisits,
        _lifeRunMassMigrationVisited,
        _lifeRunFertilityStatus,
        _lifeRunSpasmStatus,
        _lifeRunSpeciesClass);

    private int LifeRunTrackedMilestoneCount() =>
        LifeRunLogic.TrackedMilestoneCount(CurrentLifeRunSnapshot());

    private SpawnPlanView CurrentSpawnPlanView()
    {
        var vitals = CurrentCoreVitalsGuidance();
        var effectiveSpeciesIndex = CurrentEffectiveSpeciesIndex();
        return SpawnPlanLogic.Evaluate(new SpawnPlanSnapshot(
            _lifeRunActive,
            _streamerMode,
            LiveMapServicesActive,
            effectiveSpeciesIndex > 0,
            _spawnPlanCoverReady,
            _spawnPlanScentChecked,
            _spawnPlanWaterFound,
            _spawnPlanFoodFound,
            vitals.Water,
            vitals.WaterFresh,
            vitals.Food,
            vitals.FoodFresh));
    }

    private ZoneBriefView CurrentZoneBriefView()
    {
        var diet = DietCoachLogic.Analyze(_dietSlot1, _dietSlot2, _dietSlot3, _dietTargetIndex);
        var effectiveSpeciesIndex = CurrentEffectiveSpeciesIndex();
        return ZoneBriefLogic.Evaluate(new ZoneBriefSnapshot(
            _lifeRunActive,
            _streamerMode,
            LiveMapServicesActive,
            ZoneBriefLogic.NormalizeZone(_zoneBriefIndex),
            _lifeRunStageIndex,
            effectiveSpeciesIndex > 0,
            DietCoachLogic.SpeciesClassLabel(effectiveSpeciesIndex),
            diet.FilledCount));
    }

    private string GetLifeRunNextObjective()
    {
        var spawnPlan = CurrentSpawnPlanView();
        if (spawnPlan.IsVisible && !spawnPlan.IsComplete)
        {
            return spawnPlan.CurrentTask;
        }
        var zoneBrief = CurrentZoneBriefView();
        return zoneBrief.RequiresAttention
            ? zoneBrief.NextObjective
            : LifeRunLogic.NextObjective(CurrentLifeRunSnapshot());
    }

    private int LifeRunPrimeConditionCount() =>
        LifeRunLogic.PrimeConditionCount(CurrentLifeRunSnapshot());

    private int LifeRunPrimeRequiredConditionCount() =>
        LifeRunLogic.PrimeRequiredConditionCount(CurrentLifeRunSnapshot());

    private string GetLifeRunPrimeNextObjective() =>
        LifeRunLogic.PrimeNextObjective(CurrentLifeRunSnapshot());

    private ElderLineagePresentation CurrentElderLineagePresentation() =>
        ElderLineageLogic.Analyze(new ElderLineageSnapshot(
            _lifeRunActive,
            _lifeRunGrowthPercent,
            LifeRunPrimeConditionCount(),
            LifeRunPrimeRequiredConditionCount(),
            _elderEntombCount,
            _elderPrimeConfirmed,
            _elderConfirmed,
            _mutationLoadout.Count,
            _mutationLoadout.Count(item => item.Status == 2)));

    private static string FormatLifeRunElapsed(TimeSpan elapsed, bool compact) =>
        LifeRunLogic.FormatElapsed(elapsed, compact);

    private string BuildLifeRunSummary(bool compact)
    {
        if (!_lifeRunActive) return string.Empty;
        var elapsed = FormatLifeRunElapsed(DateTimeOffset.UtcNow - _lifeRunStartedAt, compact);
        var stage = compact
            ? _lifeRunStageShortLabels[_lifeRunStageIndex]
            : _lifeRunStageLabels[_lifeRunStageIndex];
        var primeCount = LifeRunPrimeConditionCount();
        var primeRequired = LifeRunPrimeRequiredConditionCount();
        var equippedMutations = MutationPlannerLogic.EquippedCount(_mutationLoadout);
        var diet = DietCoachLogic.Analyze(_dietSlot1, _dietSlot2, _dietSlot3, _dietTargetIndex);
        var dietTarget = DietCoachLogic.Targets[_dietTargetIndex];
        var effectiveSpeciesIndex = CurrentEffectiveSpeciesIndex();
        var dietState = diet.IsComplete ? diet.Key : $"{diet.FilledCount}/3";
        var dietNeed = diet.NeededNutrient == DietCoachLogic.Empty
            ? dietTarget.Label
            : $"NEED {DietCoachLogic.NutrientShortName(diet.NeededNutrient)}";
        var growth = CurrentGrowthPlannerResult();
        var compactGrowth = $" · {GrowthPlannerLogic.CompactSummary(growth)}";
        var fullGrowth = $" · growth {_lifeRunGrowthPercent}%, next {growth.Milestone.Percent}% " +
                         $"{growth.Milestone.Label.ToLowerInvariant()}, server {growth.ServerMultiplier:0.#}x, " +
                         $"diet {growth.DietMultiplier}x, estimate {growth.EtaLabel.ToLowerInvariant()}" +
                         (_growthPaused ? ", manually paused" : string.Empty);
        var compactMutations = _mutationLoadout.Count > 0
            ? $" · MUT {equippedMutations}/{_mutationLoadout.Count} EQUIPPED"
            : string.Empty;
        var mutationBuild = CurrentMutationBuildAnalysis();
        var compactMutationBuild = _mutationLoadout.Count > 0
            ? $" · {MutationBuildLogic.CompactSummary(mutationBuild)}"
            : string.Empty;
        var unlockChallenge = CurrentMutationUnlockChallenge();
        var unlockTimer = FindMutationUnlockTimer(unlockChallenge);
        var unlockValue = EffectiveMutationUnlockValue(
            unlockChallenge,
            unlockTimer,
            DateTimeOffset.UtcNow);
        var unlockProgressForSummary = MutationUnlockLogic.SetValue(
            _mutationUnlockProgress,
            unlockChallenge.Id,
            unlockValue);
        var hasUnlockProgress = unlockProgressForSummary.Count > 0 || unlockTimer is not null;
        var compactUnlocks = hasUnlockProgress
            ? $" · {MutationUnlockLogic.CompactSummary(unlockProgressForSummary, _mutationUnlockSelectedIndex)}"
            : string.Empty;
        var nest = CurrentNestPlannerSnapshot();
        var compactNest = nest.Active
            ? $" · {NestPlannerLogic.CompactSummary(nest)}"
            : string.Empty;
        var captureStreakLabel = LifeRunLogic.CaptureStreakLabel(_captureStreak);
        var compactCaptureStreak = captureStreakLabel.Length > 0
            ? $" · SYNC {captureStreakLabel}"
            : string.Empty;
        var fullCaptureStreak = captureStreakLabel.Length > 0
            ? $" · player sync capture {captureStreakLabel.ToLowerInvariant()} this life"
            : string.Empty;
        var spawnPlan = CurrentSpawnPlanView();
        var compactSpawnPlan = spawnPlan.IsVisible
            ? $" · {SpawnPlanLogic.CompactSummary(spawnPlan)}"
            : string.Empty;
        var fullSpawnPlan = spawnPlan.IsVisible
            ? $" · spawn plan {spawnPlan.Completed}/{spawnPlan.Total}, " +
              $"{(spawnPlan.IsComplete ? "complete" : $"next {spawnPlan.CurrentTask.ToLowerInvariant()}")}"
            : string.Empty;
        var zoneBrief = CurrentZoneBriefView();
        var zoneSummary = ZoneBriefLogic.CompactSummary(zoneBrief);
        var compactZone = !string.IsNullOrEmpty(zoneSummary)
            ? $" · {zoneSummary}"
            : string.Empty;
        var fullZone = zoneBrief.IsVisible
            ? $" · current zone {zoneBrief.ZoneLabel.ToLowerInvariant()}, {zoneBrief.Heading.ToLowerInvariant()}"
            : string.Empty;
        var fullNest = nest.Active
            ? $" · nest plan {NestPlannerLogic.Phase(nest).Label.ToLowerInvariant()}, " +
              $"readiness {NestPlannerLogic.ReadinessCount(nest)}/4, target {nest.EggTarget}, " +
              $"laid {nest.EggsLaid}, hatched {nest.EggsHatched}, raised {nest.YoungRaised}, " +
              $"access {NestPlannerLogic.AccessLabel(nest.AccessIndex).ToLowerInvariant()}, " +
              $"auto-hatch guidance {(_nestAutoHatchGuidanceEnabled ? "on" : "off")}"
            : " · no active nest plan";
        var fullMutations = _mutationLoadout.Count > 0
            ? " · mutation loadout " + string.Join(", ", _mutationLoadout.OrderBy(item => item.Slot).Select(item =>
            {
                var mutation = MutationPlannerLogic.FindById(item.MutationId);
                return $"S{item.Slot} {mutation?.Name ?? "Unknown"} [{MutationPlannerLogic.StatusLabel(item.Status).ToLowerInvariant()}]";
            }))
            : " · mutation loadout empty";
        var fullMutationBuild = _mutationLoadout.Count > 0
            ? $" · mutation build focus {mutationBuild.Focus.Label.ToLowerInvariant()}, " +
              $"fit {mutationBuild.FitPercent}%, recommendation {mutationBuild.RecommendationName.ToLowerInvariant()}"
            : " · mutation Build Lab waiting";
        var fullUnlocks = hasUnlockProgress
            ? $" · mutation unlocks {MutationUnlockLogic.CompletedCount(unlockProgressForSummary)}/{MutationUnlockLogic.Challenges.Length}; " +
              $"tracked {unlockChallenge.Label.ToLowerInvariant()} " +
              $"{MutationUnlockLogic.ProgressLabel(unlockChallenge, unlockValue).ToLowerInvariant()}"
            : " · mutation unlock tracker empty";
        var elder = CurrentElderLineagePresentation();
        var compactElder = _elderEntombCount > 0 || _lifeRunGrowthPercent >= 75 || _elderConfirmed
            ? $" · {ElderLineageLogic.CompactSummary(elder)}"
            : string.Empty;
        var fullElder = $" · Elder lineage {elder.Snapshot.EntombCount + 1}, " +
                        $"completed Entombs {elder.Snapshot.EntombCount}, " +
                        $"Prime check {(elder.Snapshot.PrimeConfirmed ? "verified" : "not verified")}, " +
                        $"inherited mutations {elder.Snapshot.InheritedMutationCount}/{elder.Snapshot.MutationCount}, " +
                        $"state {elder.Heading.ToLowerInvariant()}, next {elder.NextAction.ToLowerInvariant()}";
        var mark = compact ? "Y" : "yes";
        var empty = compact ? "N" : "no";
        return compact
            ? $"RUN {stage} {LifeRunTrackedMilestoneCount()}/6 · SANC {(_lifeRunSanctuaryVisited ? mark : empty)}" +
              $" · DIET {(_lifeRunPerfectDiet ? mark : empty)} · MIG {_lifeRunMigrationVisits}" +
              $" · PAT {_lifeRunPatrolVisits} · NUTR {dietState} {dietNeed}" +
              $" · PRIME {primeCount}/10 NEED {primeRequired}{compactSpawnPlan}{compactZone}{compactGrowth}{compactElder}{compactMutations}{compactMutationBuild}{compactUnlocks}{compactNest}{compactCaptureStreak} · {elapsed}"
            : $"Isley life run · stage {stage} · elapsed {elapsed}" +
              $" · Sanctuary {(_lifeRunSanctuaryVisited ? mark : empty)}" +
              $" · perfect diet {(_lifeRunPerfectDiet ? mark : empty)}" +
              $" · migration visits {_lifeRunMigrationVisits} · patrol visits {_lifeRunPatrolVisits}" +
              $" · nested in {(_lifeRunNestedIn ? mark : empty)}" +
              $" · raised young {(_lifeRunRaisedYoung ? mark : empty)}" +
              $" · mass migration {(_lifeRunMassMigrationVisited ? mark : empty)}" +
              $" · Prime plan {primeCount}/10, guide threshold {primeRequired}" +
              $" · infertility {PrimeManualStateLabel(_lifeRunFertilityStatus).ToLowerInvariant()}" +
              $" · muscle spasms {PrimeManualStateLabel(_lifeRunSpasmStatus).ToLowerInvariant()}" +
              $" · species class {PrimeSpeciesClassLabel(_lifeRunSpeciesClass).ToLowerInvariant()}" +
              $" · diet coach {DietCoachLogic.SpeciesLabel(effectiveSpeciesIndex).ToLowerInvariant()}, " +
              $"slots {dietState}, target {dietTarget.Label.ToLowerInvariant()}, {diet.Recommendation.ToLowerInvariant()}" +
              $" · suggested food {DietCoachLogic.FoodForNutrient(effectiveSpeciesIndex, diet.NeededNutrient)}" +
              fullSpawnPlan +
              fullZone +
              fullGrowth +
              fullElder +
              fullNest +
              fullMutations +
              fullMutationBuild +
              fullUnlocks +
              fullCaptureStreak +
              $" · manual estimate; diet catalog {DietCoachLogic.SpeciesSnapshot}; " +
              "verify current server rules and the fourth mutation slot in game; not automatic certification";
    }

    private void UpdateLifeRunHistory(bool force = false)
    {
        if (LifeRunHistoryStatusText is null || LifeRunHistoryListPanel is null)
        {
            return;
        }

        var signature = string.Join('|',
            _lifeRunActive,
            _streamerMode,
            _clearLifeRunHistoryConfirmationPending,
            string.Join(';', _lifeRunHistory.Select(entry =>
                $"{entry.Id}:{entry.EndedAtUnixMs}:{entry.Outcome}:{entry.DurationSeconds}:" +
                $"{entry.FinalGrowthPercent}:{entry.TrackedMilestones}:{entry.PrimeConditions}:" +
                $"{entry.BestCaptureStreak}")));
        if (!force && string.Equals(signature, _lifeRunHistoryUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _lifeRunHistoryUiSignature = signature;

        LifeRunHistoryContentPanel.Visibility = _streamerMode
            ? Visibility.Collapsed
            : Visibility.Visible;
        LifeRunHistoryJumpButton.Visibility = _streamerMode
            ? Visibility.Collapsed
            : Visibility.Visible;
        LifeRunHistoryArchiveActionsPanel.Visibility = _lifeRunActive && !_streamerMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_streamerMode)
        {
            LifeRunHistoryStatusText.Text = "Hidden in streamer mode";
            LifeRunHistoryStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            LifeRunHistoryListPanel.Children.Clear();
            return;
        }

        var summary = LifeRunHistoryLogic.Summarize(_lifeRunHistory);
        var journalBestStreak = _lifeRunHistory.Count == 0
            ? 0
            : _lifeRunHistory.Max(entry => Math.Max(0, entry.BestCaptureStreak));
        LifeRunHistoryJumpButton.Content = summary.Total == 1
            ? "Survival journal · 1 life"
            : $"Survival journal · {summary.Total} lives";
        LifeRunHistoryStatusText.Text = summary.Total == 0
            ? "No archived lives · private and local"
            : $"{summary.Total} LIVES · {summary.Survived} SURVIVED · {summary.Entombed} ENTOMBED · " +
              $"AVG {LifeRunHistoryLogic.FormatDuration(summary.AverageDurationSeconds)} · " +
              $"BEST {summary.BestGrowthPercent}%" +
              (journalBestStreak > 0 ? $" · SYNC BEST {journalBestStreak}" : string.Empty);
        LifeRunHistoryStatusText.Foreground = summary.Survived > 0 || summary.Entombed > 0
            ? new SolidColorBrush(Color.FromRgb(110, 231, 183))
            : (Brush)FindResource("SecondaryTextBrush");

        CopyLifeRunHistoryButton.IsEnabled = summary.Total > 0;
        ClearLifeRunHistoryButton.IsEnabled = summary.Total > 0;
        ClearLifeRunHistoryButton.Content = _clearLifeRunHistoryConfirmationPending
            ? "SURE"
            : "CLEAR";
        ClearLifeRunHistoryButton.ToolTip = _clearLifeRunHistoryConfirmationPending
            ? "Press again within three seconds to clear every archived life"
            : "Clear the private survival journal after confirmation";
        SetToggleButtonState(
            ClearLifeRunHistoryButton,
            _clearLifeRunHistoryConfirmationPending);

        LifeRunHistoryListPanel.Children.Clear();
        foreach (var (entry, index) in _lifeRunHistory
                     .Take(LifeRunHistoryLogic.VisibleEntries)
                     .Select((entry, index) => (entry, index)))
        {
            var outcome = LifeRunHistoryLogic.NormalizeOutcome(entry.Outcome);
            var outcomeBrush = outcome switch
            {
                LifeRunHistoryLogic.DeathOutcome => new SolidColorBrush(Color.FromRgb(248, 113, 113)),
                LifeRunHistoryLogic.SurvivedOutcome => new SolidColorBrush(Color.FromRgb(110, 231, 183)),
                LifeRunHistoryLogic.EntombedOutcome => new SolidColorBrush(Color.FromRgb(196, 181, 253)),
                _ => (SolidColorBrush)FindResource("AccentBrush")
            };
            var endedAt = DateTimeOffset.FromUnixTimeMilliseconds(entry.EndedAtUnixMs).ToLocalTime();
            var row = new Border
            {
                Padding = new Thickness(1, 5, 1, 5),
                BorderBrush = new SolidColorBrush(Color.FromArgb(55, 100, 116, 139)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                ToolTip = $"{entry.ServerName} · {endedAt:g} · " +
                          $"{entry.TrackedMilestones}/6 tracked · Prime {entry.PrimeConditions}/{entry.PrimeRequired}"
            };
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = outcomeBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Text = $"{LifeRunHistoryLogic.OutcomeLabel(outcome)} · " +
                       $"{entry.SpeciesName.ToUpperInvariant()} · {entry.FinalGrowthPercent}%"
            });
            content.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 2, 0, 0),
                FontSize = 7,
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Text = $"{LifeRunHistoryLogic.FormatDuration(entry.DurationSeconds)} · " +
                       $"RUN {entry.TrackedMilestones}/6 · {endedAt:MMM d}"
            });
            row.Child = content;
            row.Opacity = 0;
            row.BeginAnimation(
                OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(
                    0,
                    1,
                    TimeSpan.FromMilliseconds(120))
                {
                    BeginTime = TimeSpan.FromMilliseconds(index * 25)
                });
            LifeRunHistoryListPanel.Children.Add(row);
        }
    }

    private LifeRunHistoryEntry CreateCurrentLifeRunHistoryEntry(
        DateTimeOffset now,
        string outcome)
    {
        var elapsed = now - _lifeRunStartedAt;
        var durationSeconds = (int)Math.Clamp(
            elapsed.TotalSeconds,
            0,
            30d * 24 * 60 * 60);
        var speciesId = string.Empty;
        var speciesName = "Unknown / server mod";
        if (_dietSpeciesIndex > 0)
        {
            var species = DietCoachLogic.Species[_dietSpeciesIndex - 1];
            speciesId = species.Id;
            speciesName = species.Name;
        }
        var entry = LifeRunHistoryLogic.CreateEntry(
            now,
            speciesId,
            speciesName,
            outcome,
            durationSeconds,
            _lifeRunGrowthPercent,
            _lifeRunStageIndex,
            LifeRunTrackedMilestoneCount(),
            LifeRunPrimeConditionCount(),
            LifeRunPrimeRequiredConditionCount(),
            ServerSessionLogic.DisplayName(_serverSessionProfileId, _serverSessionName));
        entry.BestCaptureStreak = LifeRunLogic.NormalizeCaptureStreak(_captureStreak).Best;
        return entry;
    }

    private void PrependLifeRunHistory(LifeRunHistoryEntry entry, DateTimeOffset now)
    {
        var normalized = LifeRunHistoryLogic.NormalizeEntries(
            new[] { entry }.Concat(_lifeRunHistory),
            now);
        _lifeRunHistory.Clear();
        _lifeRunHistory.AddRange(normalized);
        _clearLifeRunHistoryConfirmationPending = false;
        _clearLifeRunHistoryConfirmationRevision++;
        _lifeRunHistoryUiSignature = string.Empty;
    }

    private async void LifeTransitionOutcomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive
            || _streamerMode
            || _lifeTransitionPending?.Detected != true
            || sender is not Button { Tag: string requestedOutcome })
        {
            return;
        }

        if (string.Equals(requestedOutcome, "keep", StringComparison.OrdinalIgnoreCase))
        {
            _lifeTransitionPending = null;
            _lifeTransitionUiSignature = string.Empty;
            _lifeRunUiSignature = string.Empty;
            _nextMoveUiSignature = string.Empty;
            AddTacticalEvent("LIFE", "New dinosaur signal dismissed", "Current saved Life Run kept unchanged");
            UpdateLifeRun(force: true);
            UpdateNextMove(force: true);
            UpdateTacticalBrief();
            await ShowHotkeyToastAsync("CURRENT LIFE RUN KEPT", true);
            return;
        }

        var requested = requestedOutcome.Trim().ToLowerInvariant();
        if (requested is not LifeRunHistoryLogic.DeathOutcome
            and not LifeRunHistoryLogic.SurvivedOutcome
            and not LifeRunHistoryLogic.EndedOutcome)
        {
            return;
        }
        var outcome = LifeRunHistoryLogic.NormalizeOutcome(requested);

        var speciesBridge = CurrentLiveSpeciesBridge();
        var growthBridge = CurrentLiveGrowthBridge();
        if (!speciesBridge.Available || !growthBridge.Available)
        {
            await ShowHotkeyToastAsync("FRESH LIVE DINOSAUR REQUIRED · SIGNAL KEPT", false);
            return;
        }

        var now = DateTimeOffset.Now;
        var archived = CreateCurrentLifeRunHistoryEntry(now, outcome);
        PrependLifeRunHistory(archived, now);
        ClearSurvivalIncident(logEvent: false);
        StartNewLifeRun(logEvent: false);
        ApplyLiveSpeciesToSavedRun(speciesBridge);
        _lifeRunGrowthPercent = growthBridge.LiveGrowthPercent;
        _lifeRunStageIndex = GrowthPlannerLogic.StageIndex(_lifeRunGrowthPercent);
        AddTacticalEvent(
            "LIFE",
            $"Life archived · {LifeRunHistoryLogic.OutcomeLabel(archived.Outcome)}",
            $"{archived.SpeciesName} {archived.FinalGrowthPercent}% · new {speciesBridge.LiveSpeciesName} {_lifeRunGrowthPercent}% run");
        CommitGrowthClockChange(
            "New Life Run started from live dinosaur",
            $"{speciesBridge.LiveSpeciesName} · saved growth {_lifeRunGrowthPercent}% · live Prime remains read-only");
        UpdateFieldGuide(force: true);
        UpdateFightCheck(force: true);
        await ShowHotkeyToastAsync(
            $"{LifeRunHistoryLogic.OutcomeLabel(archived.Outcome)} ARCHIVED · {speciesBridge.LiveSpeciesName.ToUpperInvariant()} RUN",
            archived.Outcome != LifeRunHistoryLogic.DeathOutcome);
    }

    private async void LifeRunArchiveOutcomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive
            || _streamerMode
            || sender is not Button { Tag: string outcome })
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var entry = CreateCurrentLifeRunHistoryEntry(now, outcome);
        PrependLifeRunHistory(entry, now);
        _lifeRunActive = false;
        ClearLifeTransitionSession();
        _newLifeRunConfirmationPending = false;
        _newLifeRunConfirmationRevision++;
        _lifeRunUiSignature = string.Empty;
        ClearSurvivalIncident(logEvent: false);
        AddTacticalEvent(
            "LIFE",
            $"Life archived · {LifeRunHistoryLogic.OutcomeLabel(entry.Outcome)}",
            $"{entry.SpeciesName} · {entry.FinalGrowthPercent}% · " +
            LifeRunHistoryLogic.FormatDuration(entry.DurationSeconds));
        UpdateLifeRun(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
        await ShowHotkeyToastAsync(
            $"LIFE ARCHIVED · {LifeRunHistoryLogic.OutcomeLabel(entry.Outcome)}",
            entry.Outcome != LifeRunHistoryLogic.DeathOutcome);
    }

    private void LifeRunHistoryJumpButton_Click(object sender, RoutedEventArgs e) =>
        JumpToMapToolsSection("life-journal");

    private async void CopyLifeRunHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || _lifeRunHistory.Count == 0)
        {
            return;
        }
        try
        {
            Clipboard.SetText(LifeRunHistoryLogic.BuildExport(
                _lifeRunHistory,
                DateTimeOffset.Now));
            await ShowHotkeyToastAsync("SURVIVAL JOURNAL COPIED", true);
        }
        catch
        {
            await ShowHotkeyToastAsync("COPY UNAVAILABLE", false);
        }
    }

    private async void ClearLifeRunHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || _lifeRunHistory.Count == 0)
        {
            return;
        }

        if (_clearLifeRunHistoryConfirmationPending)
        {
            var clearedCount = _lifeRunHistory.Count;
            _lifeRunHistory.Clear();
            _clearLifeRunHistoryConfirmationPending = false;
            _clearLifeRunHistoryConfirmationRevision++;
            _lifeRunHistoryUiSignature = string.Empty;
            AddTacticalEvent("LIFE", "Survival journal cleared", $"{clearedCount} archived lives removed");
            UpdateLifeRunHistory(force: true);
            SavePlannerState();
            await ShowHotkeyToastAsync("SURVIVAL JOURNAL CLEARED", true);
            return;
        }

        _clearLifeRunHistoryConfirmationPending = true;
        var revision = ++_clearLifeRunHistoryConfirmationRevision;
        _lifeRunHistoryUiSignature = string.Empty;
        UpdateLifeRunHistory(force: true);
        await ShowHotkeyToastAsync("PRESS CLEAR AGAIN", false);
        await Task.Delay(3000);
        if (!IsLoaded
            || !_clearLifeRunHistoryConfirmationPending
            || revision != _clearLifeRunHistoryConfirmationRevision)
        {
            return;
        }
        _clearLifeRunHistoryConfirmationPending = false;
        _lifeRunHistoryUiSignature = string.Empty;
        UpdateLifeRunHistory(force: true);
    }

    private static string PrimeManualStateLabel(int state) => state switch
    {
        1 => "CLEAR",
        2 => "FAILED",
        _ => "UNKNOWN"
    };

    private static string PrimeSpeciesClassLabel(int state) => state switch
    {
        1 => "SMALL-SPECIES CREDIT",
        2 => "OTHER SPECIES",
        _ => "UNKNOWN"
    };

    private static string PrimeSpeciesButtonLabel(int state) => state switch
    {
        1 => "SMALL",
        2 => "OTHER",
        _ => "UNKNOWN"
    };

    private void SetPrimeStateButton(Button button, int state)
    {
        SetToggleButtonState(button, state == 1);
        if (state == 2)
        {
            button.Foreground = (Brush)FindResource("WarningBrush");
            button.BorderBrush = (Brush)FindResource("WarningBrush");
        }
    }

    private void UpdateSpawnPlan(bool force = false)
    {
        if (SpawnPlanAnchor is null
            || SpawnPlanProgressText is null
            || SpawnPlanHeadingText is null
            || SpawnPlanDetailText is null
            || SpawnPlanCoverButton is null
            || SpawnPlanScentButton is null
            || SpawnPlanWaterButton is null
            || SpawnPlanFoodButton is null
            || SpawnPlanActionButton is null)
        {
            return;
        }

        var view = CurrentSpawnPlanView();
        var signature = string.Join('|',
            view.State,
            view.Completed,
            view.Heading,
            view.Detail,
            view.ActionLabel,
            view.ActionId,
            view.CurrentTask,
            _spawnPlanCoverReady,
            _spawnPlanScentChecked,
            _spawnPlanWaterFound,
            _spawnPlanFoodFound);
        if (!force && string.Equals(signature, _spawnPlanUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _spawnPlanUiSignature = signature;

        SpawnPlanAnchor.Visibility = view.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        if (!view.IsVisible)
        {
            return;
        }

        var completeBrush = new SolidColorBrush(Color.FromRgb(110, 231, 183));
        var urgent = view.Heading is "WATER FIRST" or "FOOD FIRST";
        var statusBrush = view.IsComplete
            ? completeBrush
            : urgent
                ? (Brush)FindResource("WarningBrush")
                : (Brush)FindResource("AccentBrush");
        SpawnPlanProgressText.Text = $"{view.Completed}/{view.Total}";
        SpawnPlanProgressText.Foreground = statusBrush;
        SpawnPlanHeadingText.Text = view.Heading;
        SpawnPlanHeadingText.Foreground = statusBrush;
        SpawnPlanDetailText.Text = view.Detail;
        SpawnPlanActionButton.Content = view.ActionLabel;
        SpawnPlanActionButton.Tag = view.ActionId;
        SpawnPlanActionButton.ToolTip = view.Detail;
        SetToggleButtonState(SpawnPlanActionButton, view.IsComplete);

        SpawnPlanCoverButton.Content = _spawnPlanCoverReady ? "COVER ✓" : "COVER";
        SpawnPlanScentButton.Content = _spawnPlanScentChecked ? "SCENT ✓" : "SCENT";
        SpawnPlanWaterButton.Content = _spawnPlanWaterFound ? "WATER ✓" : "WATER";
        SpawnPlanFoodButton.Content = _spawnPlanFoodFound ? "FOOD ✓" : "FOOD";
        SetToggleButtonState(SpawnPlanCoverButton, _spawnPlanCoverReady);
        SetToggleButtonState(SpawnPlanScentButton, _spawnPlanScentChecked);
        SetToggleButtonState(SpawnPlanWaterButton, _spawnPlanWaterFound);
        SetToggleButtonState(SpawnPlanFoodButton, _spawnPlanFoodFound);

        SpawnPlanHeadingText.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0.55,
                1,
                TimeSpan.FromMilliseconds(160)));
    }

    private void UpdateZoneBrief(bool force = false)
    {
        if (ZoneBriefAnchor is null
            || ZoneBriefStatusText is null
            || ZoneBriefHeadingText is null
            || ZoneBriefDetailText is null
            || ZoneBriefOutsideButton is null
            || ZoneBriefSanctuaryButton is null
            || ZoneBriefMigrationButton is null
            || ZoneBriefPatrolButton is null
            || ZoneBriefActionButton is null)
        {
            return;
        }

        var view = CurrentZoneBriefView();
        var signature = string.Join('|',
            view.IsVisible,
            view.Zone,
            view.Heading,
            view.Detail,
            view.ActionLabel,
            view.ActionId,
            view.NextObjective,
            view.Tone,
            view.RequiresAttention);
        if (!force && string.Equals(signature, _zoneBriefUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _zoneBriefUiSignature = signature;

        ZoneBriefAnchor.Visibility = view.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        if (!view.IsVisible)
        {
            return;
        }

        var activeBrush = view.Tone == ZoneBriefTone.Warning
            ? (Brush)FindResource("WarningBrush")
            : view.Zone == PlayerZone.Outside
                ? (Brush)FindResource("SecondaryTextBrush")
                : (Brush)FindResource("AccentBrush");
        ZoneBriefStatusText.Text = view.ZoneLabel;
        ZoneBriefStatusText.Foreground = activeBrush;
        ZoneBriefHeadingText.Text = view.Heading;
        ZoneBriefHeadingText.Foreground = activeBrush;
        ZoneBriefDetailText.Text = view.Detail;
        ZoneBriefActionButton.Content = view.ActionLabel;
        ZoneBriefActionButton.Tag = view.ActionId;
        ZoneBriefActionButton.ToolTip = view.Detail;

        SetToggleButtonState(ZoneBriefOutsideButton, view.Zone == PlayerZone.Outside);
        SetToggleButtonState(ZoneBriefSanctuaryButton, view.Zone == PlayerZone.Sanctuary);
        SetToggleButtonState(ZoneBriefMigrationButton, view.Zone == PlayerZone.Migration);
        SetToggleButtonState(ZoneBriefPatrolButton, view.Zone == PlayerZone.Patrol);
    }

    private void UpdateLifeRun(bool force = false)
    {
        EnsurePlannerStateStoreLoaded();
        var tickNow = DateTimeOffset.UtcNow;
        UpdateNestTimerAlerts(tickNow);
        UpdateCaptureStreak(tickNow);
        if (LifeRunHudBorder is null || LifeRunStatusText is null) return;
        UpdateLifeRunHistory(force);
        UpdateMutationUnlockTracker(force);
        UpdateSpawnPlan(force);
        UpdateZoneBrief(force);
        var spawnPlan = CurrentSpawnPlanView();
        var zoneBrief = CurrentZoneBriefView();
        var elapsed = _lifeRunActive
            ? DateTimeOffset.UtcNow - _lifeRunStartedAt
            : TimeSpan.Zero;
        var elapsedMinute = Math.Max(0, (int)elapsed.TotalMinutes);
        var liveSpecies = CurrentLiveSpeciesBridge();
        var signature = string.Join("|", new object[]
        {
            _lifeRunActive, _lifeRunStageIndex, _lifeRunHudVisible,
            _lifeRunSanctuaryVisited, _lifeRunPerfectDiet,
            _lifeRunNestedIn, _lifeRunRaisedYoung,
            _spawnPlanCoverReady, _spawnPlanScentChecked,
            _spawnPlanWaterFound, _spawnPlanFoodFound,
            spawnPlan.State, spawnPlan.CurrentTask,
            _zoneBriefIndex, zoneBrief.Heading, zoneBrief.RequiresAttention,
            _lifeRunMigrationVisits, _lifeRunPatrolVisits,
            _lifeRunMassMigrationVisited,
            _lifeRunFertilityStatus, _lifeRunSpasmStatus, _lifeRunSpeciesClass,
            _dietSpeciesIndex, _dietTargetIndex, _dietSlot1, _dietSlot2, _dietSlot3,
            liveSpecies.State, liveSpecies.LiveSpeciesId, liveSpecies.EffectiveSpeciesIndex,
            _lifeRunGrowthPercent, _growthServerMultiplierIndex, _growthPaused,
            _elderEntombCount, _elderPrimeConfirmed, _elderConfirmed, _recordEntombConfirmationPending,
            _nestPlannerActive, _nestPhaseIndex, _nestPartnerReady, _nestSiteReady,
            _nestDebrisReady, _nestReservesReady, _nestAccessIndex, _nestEggTarget,
            _nestEggsLaid, _nestEggsHatched, _nestYoungRaised, _nestTimerDurationIndex,
            _nestAutoHatchGuidanceEnabled,
            _clearNestConfirmationPending,
            string.Join(';', _mutationLoadout.Select(item => $"{item.Slot}:{item.MutationId}:{item.Status}")),
            _mutationUnlockSelectedIndex,
            string.Join(';', _mutationUnlockProgress.Select(item => $"{item.ChallengeId}:{item.Value}")),
            _lifeTransitionPending?.Key ?? string.Empty,
            _newLifeRunConfirmationPending, elapsedMinute,
            _captureStreak.Current, _captureStreak.Best,
            _streamerMode, _hudDetailModeIndex
        });
        if (!force && string.Equals(signature, _lifeRunUiSignature, StringComparison.Ordinal)) return;
        _lifeRunUiSignature = signature;

        LifeRunStartButton.Visibility = !_lifeRunActive && !_streamerMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        LifeRunActiveControls.Visibility = _lifeRunActive && !_streamerMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        LifeRunHudBorder.Visibility = _lifeRunActive
                                      && _lifeRunHudVisible
                                      && !_streamerMode
                                      && _hudDetailModeIndex < 2
                                      && !CurrentHudPriorityPresentation().HideAmbientHud
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateMutationPlanner(force);
        UpdateDietCoachControls();
        UpdateGrowthClockControls(force);
        UpdateNestPlannerControls(force);
        UpdateElderLineageControls(force);
        UpdateLifeTransitionPresentation();

        if (_streamerMode)
        {
            LifeRunStatusText.Text = "Hidden in streamer mode";
            LifeRunStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            return;
        }
        if (!_lifeRunActive)
        {
            LifeRunStatusText.Text = "No active life run · manual and local";
            LifeRunStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            return;
        }

        var tracked = LifeRunTrackedMilestoneCount();
        var stage = _lifeRunStageLabels[_lifeRunStageIndex];
        var compactStage = _lifeRunStageShortLabels[_lifeRunStageIndex];
        var next = GetLifeRunNextObjective();
        var primeCount = LifeRunPrimeConditionCount();
        var primeRequired = LifeRunPrimeRequiredConditionCount();
        var primeReady = primeCount >= primeRequired;
        var primeNext = GetLifeRunPrimeNextObjective();
        var elapsedLabel = FormatLifeRunElapsed(elapsed, compact: false);
        var effectiveSpeciesName = liveSpecies.EffectiveSpeciesIndex > 0
            ? DietCoachLogic.Species[liveSpecies.EffectiveSpeciesIndex - 1].Name
            : string.Empty;
        var speciesPrefix = string.IsNullOrEmpty(effectiveSpeciesName)
            ? string.Empty
            : $"{effectiveSpeciesName} · ";
        LifeRunStatusText.Text = $"{speciesPrefix}{stage} · {_lifeRunGrowthPercent}% · {elapsedLabel} · {tracked}/6 tracked · next {next.ToLowerInvariant()}" +
                                 (_lifeRunHudVisible
                                     ? _hudDetailModeIndex >= 2 ? " · HUD hidden by Clean view" : string.Empty
                                     : " · HUD hidden") +
                                 (LifeRunLogic.CaptureStreakLabel(_captureStreak) is { Length: > 0 } captureStreakLabel
                                     ? $" · capture {captureStreakLabel.ToLowerInvariant()}"
                                     : string.Empty);
        LifeRunStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
        LifeRunStageButton.Content = $"Stage · {stage}";
        LifeRunMigrationText.Text = $"MIGRATION · {_lifeRunMigrationVisits}/2";
        LifeRunPatrolText.Text = $"PATROL · {_lifeRunPatrolVisits}/4";
        LifeRunMigrationMinusButton.IsEnabled = _lifeRunMigrationVisits > 0;
        LifeRunMigrationPlusButton.IsEnabled = _lifeRunMigrationVisits < 99;
        LifeRunPatrolMinusButton.IsEnabled = _lifeRunPatrolVisits > 0;
        LifeRunPatrolPlusButton.IsEnabled = _lifeRunPatrolVisits < 99;

        LifeRunSanctuaryButton.Content = _lifeRunSanctuaryVisited ? "SANCTUARY ✓" : "SANCTUARY";
        LifeRunDietButton.Content = _lifeRunPerfectDiet ? "PERFECT DIET ✓" : "PERFECT DIET";
        LifeRunNestedButton.Content = _lifeRunNestedIn ? "NESTED IN ✓" : "NESTED IN";
        LifeRunRaisedButton.Content = _lifeRunRaisedYoung ? "RAISED YOUNG ✓" : "RAISED YOUNG";
        LifeRunMassMigrationButton.Content = _lifeRunMassMigrationVisited ? "MASS MIGRATION ✓" : "MASS MIGRATION";
        SetToggleButtonState(LifeRunSanctuaryButton, _lifeRunSanctuaryVisited);
        SetToggleButtonState(LifeRunDietButton, _lifeRunPerfectDiet);
        SetToggleButtonState(LifeRunNestedButton, _lifeRunNestedIn);
        SetToggleButtonState(LifeRunRaisedButton, _lifeRunRaisedYoung);
        SetToggleButtonState(LifeRunMassMigrationButton, _lifeRunMassMigrationVisited);
        LifeRunSpeciesClassButton.Content = $"SPECIES · {PrimeSpeciesButtonLabel(_lifeRunSpeciesClass)}";
        LifeRunFertilityButton.Content = $"INFERTILITY · {PrimeManualStateLabel(_lifeRunFertilityStatus)}";
        LifeRunSpasmButton.Content = $"SPASMS · {PrimeManualStateLabel(_lifeRunSpasmStatus)}";
        SetPrimeStateButton(LifeRunSpeciesClassButton, _lifeRunSpeciesClass);
        SetPrimeStateButton(LifeRunFertilityButton, _lifeRunFertilityStatus);
        SetPrimeStateButton(LifeRunSpasmButton, _lifeRunSpasmStatus);
        LifeRunPrimeScoreText.Text = _elderPrimeConfirmed
            ? $"{primeCount}/10 · PRIME VERIFIED"
            : primeReady
                ? $"{primeCount}/10 · PLAN READY"
                : $"{primeCount}/10 · NEED {primeRequired}";
        LifeRunPrimeNextText.Text = _elderPrimeConfirmed
            ? "NEXT · PROTECT THE LINEAGE TO 100%"
            : $"NEXT · {primeNext}";
        LifeRunPrimeNextText.Foreground = primeReady || _elderPrimeConfirmed
            ? new SolidColorBrush(Color.FromRgb(110, 231, 183))
            : new SolidColorBrush(Color.FromRgb(251, 191, 36));
        LifeRunPrimeProgressTransform.ScaleX = Math.Clamp(primeCount / (double)primeRequired, 0, 1);
        LifeRunHudButton.Content = _lifeRunHudVisible ? "HUD ON" : "HUD OFF";
        SetToggleButtonState(LifeRunHudButton, _lifeRunHudVisible);
        NewLifeRunButton.Content = _newLifeRunConfirmationPending ? "CONFIRM" : "NEW LIFE";
        NewLifeRunButton.ToolTip = _newLifeRunConfirmationPending
            ? "Select again within three seconds to clear this run and start a new life"
            : "Clear this run and start a new life after confirmation";

        LifeRunHudElapsedText.Text = FormatLifeRunElapsed(elapsed, compact: true);
        LifeRunHudStageText.Text = $"{compactStage} {_lifeRunGrowthPercent}% · RUN {tracked}/6";
        LifeRunHudMilestonesText.Text =
            $"SANC {(_lifeRunSanctuaryVisited ? "✓" : "—")} · " +
            $"DIET {(_lifeRunPerfectDiet ? "✓" : "—")} · " +
            $"MIG {_lifeRunMigrationVisits} · PAT {_lifeRunPatrolVisits}";
        var diet = DietCoachLogic.Analyze(_dietSlot1, _dietSlot2, _dietSlot3, _dietTargetIndex);
        var target = DietCoachLogic.Targets[_dietTargetIndex];
        var dietState = diet.IsComplete
            ? string.IsNullOrWhiteSpace(diet.Key) ? diet.Label : diet.Key
            : $"{diet.FilledCount}/3";
        var dietNext = diet.MatchesTarget
            ? target.Label
            : diet.NeededNutrient == DietCoachLogic.Empty
                ? "CHECK TARGET"
                : $"NEED {DietCoachLogic.NutrientShortName(diet.NeededNutrient)}";
        LifeRunHudDietText.Text = $"DIET · {dietState} · {dietNext}";
        LifeRunHudDietText.Visibility = _dietSpeciesIndex > 0 || diet.FilledCount > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        var elder = CurrentElderLineagePresentation();
        LifeRunHudPrimeText.Text = elder.Snapshot.PrimeConfirmed
            ? $"PRIME VERIFIED · {primeCount}/10"
            : primeReady
                ? $"PRIME PLAN · {primeCount}/10 · VERIFY"
                : $"PRIME PLAN · {primeCount}/10 · NEED {primeRequired}";
        var nest = CurrentNestPlannerSnapshot();
        var autoHatch = NestPlannerLogic.EvaluateAutoHatch(
            nest,
            _nestAutoHatchGuidanceEnabled);
        LifeRunHudNestText.Visibility = nest.Active ? Visibility.Visible : Visibility.Collapsed;
        LifeRunHudNestText.Text = nest.Active
            ? $"NEST / {NestPlannerLogic.Phase(nest).Label} / {nest.EggsHatched}/{nest.EggsLaid} HATCHED" +
              (autoHatch.RequiresAttention ? " / AUTO-HATCH CHECK" : string.Empty)
            : "NEST / PREPARE";
        var lineageGuidesNext = _lifeRunGrowthPercent >= 75 || _elderEntombCount > 0;
        var spawnPlanGuidesNext = spawnPlan.IsVisible && !spawnPlan.IsComplete;
        var zoneBriefGuidesNext = zoneBrief.RequiresAttention;
        LifeRunHudNextText.Text = $"NEXT · {(spawnPlanGuidesNext
            ? spawnPlan.CurrentTask
            : zoneBriefGuidesNext
                ? zoneBrief.NextObjective
                : lineageGuidesNext ? elder.NextAction : primeNext)}";
        LifeRunHudNextText.Foreground = !spawnPlanGuidesNext
                                       && !zoneBriefGuidesNext
                                       && (elder.State == ElderLineageState.EntombReady
                                           || elder.Snapshot.PrimeConfirmed)
            ? new SolidColorBrush(Color.FromRgb(110, 231, 183))
            : new SolidColorBrush(Color.FromRgb(251, 191, 36));
        var mutationBuild = CurrentMutationBuildAnalysis();
        LifeRunHudBorder.ToolTip =
            $"Private current-life tracker · {speciesPrefix}{stage} {_lifeRunGrowthPercent}% · {elapsedLabel} · {tracked}/6 notes marked" +
            $" · Prime plan {primeCount}/10, guide threshold {primeRequired}" +
            $" · {elder.LineageLabel.ToLowerInvariant()} · {elder.MutationLabel.ToLowerInvariant()}" +
            (_mutationLoadout.Count > 0
                ? $" · build {mutationBuild.Focus.Label.ToLowerInvariant()} {mutationBuild.FitPercent}%"
                : string.Empty) +
            $" · diet coach {dietState}, target {target.Label.ToLowerInvariant()}" +
            (spawnPlan.IsVisible
                ? $" · spawn plan {spawnPlan.Completed}/{spawnPlan.Total}, " +
                  $"{(spawnPlan.IsComplete ? "complete" : $"next {spawnPlan.CurrentTask.ToLowerInvariant()}")}"
                : string.Empty) +
            (zoneBrief.IsVisible
                ? $" · current zone {zoneBrief.ZoneLabel.ToLowerInvariant()}, {zoneBrief.Heading.ToLowerInvariant()}"
                : string.Empty) +
            $" · growth clock {CurrentGrowthPlannerResult().EtaLabel.ToLowerInvariant()} to next gate" +
            (nest.Active
                ? $" · nest {NestPlannerLogic.Phase(nest).Label.ToLowerInvariant()}, " +
                  $"{NestPlannerLogic.ReadinessCount(nest)}/4 ready, {nest.EggsHatched}/{nest.EggsLaid} hatched"
                : string.Empty) +
            $" · Prime check {(elder.Snapshot.PrimeConfirmed ? "verified in game" : "still required in game")}" +
            " · no game memory or automatic Prime claim";
    }

    private void UpdateLifeTransitionPresentation()
    {
        if (LifeTransitionPanel is null
            || LifeTransitionHeadingText is null
            || LifeTransitionDetailText is null)
        {
            return;
        }

        var transition = _lifeTransitionPending;
        var visible = _lifeRunActive
                      && !_streamerMode
                      && transition?.Detected == true;
        LifeTransitionPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible || transition is null)
        {
            _lifeTransitionUiSignature = string.Empty;
            return;
        }

        LifeTransitionHeadingText.Text = transition.Value.Heading;
        LifeTransitionDetailText.Text = transition.Value.Detail +
                                        " Choose the outcome; Isley will not decide for you.";
        if (string.Equals(
                _lifeTransitionUiSignature,
                transition.Value.Key,
                StringComparison.Ordinal))
        {
            return;
        }

        _lifeTransitionUiSignature = transition.Value.Key;
        LifeTransitionPanel.Opacity = 0;
        LifeTransitionPanel.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0,
                1,
                TimeSpan.FromMilliseconds(160)));
    }

    private void UpdateElderLineageControls(bool force = false)
    {
        if (ElderLineageStatusText is null
            || ElderLineageRunText is null
            || ElderPrimeConfirmedButton is null
            || RecordEntombButton is null
            || LifeRunHudLineageText is null)
        {
            return;
        }

        var primeConditionsReady = LifeRunPrimeConditionCount() >= LifeRunPrimeRequiredConditionCount();
        if ((_lifeRunGrowthPercent < 75 || !primeConditionsReady) && _elderPrimeConfirmed)
        {
            _elderPrimeConfirmed = false;
            _recordEntombConfirmationPending = false;
            _recordEntombConfirmationRevision++;
        }
        if (_lifeRunGrowthPercent < 100 && _elderConfirmed)
        {
            _elderConfirmed = false;
            _recordEntombConfirmationPending = false;
            _recordEntombConfirmationRevision++;
        }
        var presentation = CurrentElderLineagePresentation();
        var signature = string.Join('|',
            presentation.State,
            presentation.Heading,
            presentation.NextAction,
            presentation.LineageLabel,
            presentation.MutationLabel,
            presentation.Snapshot.EntombCount,
            presentation.Snapshot.PrimeConfirmed,
            presentation.Snapshot.ElderConfirmed,
            _recordEntombConfirmationPending,
            _streamerMode,
            _lifeRunHudVisible,
            _hudDetailModeIndex);
        if (!force && string.Equals(signature, _elderLineageUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _elderLineageUiSignature = signature;

        ElderLineageRunText.Text = presentation.LineageLabel;
        ElderLineageStatusText.Text = presentation.Heading;
        ElderLineageNextText.Text = $"NEXT · {presentation.NextAction}";
        ElderLineageMutationText.Text = presentation.MutationLabel;
        ElderLineageProgressTransform.ScaleX = presentation.Progress;

        var ready = presentation.State == ElderLineageState.EntombReady;
        var verification = presentation.State is ElderLineageState.PrimeVerification
            or ElderLineageState.ElderVerification;
        ElderLineageStatusText.Foreground = ready
            ? new SolidColorBrush(Color.FromRgb(110, 231, 183))
            : verification
                ? (Brush)FindResource("WarningBrush")
                : (Brush)FindResource("PrimaryTextBrush");
        ElderLineageNextText.Foreground = ready
            ? new SolidColorBrush(Color.FromRgb(110, 231, 183))
            : (Brush)FindResource("WarningBrush");

        var editable = _lifeRunActive && !_streamerMode;
        ElderLineageMinusButton.IsEnabled = editable && _elderEntombCount > 0;
        ElderLineagePlusButton.IsEnabled = editable
                                           && _elderEntombCount < ElderLineageLogic.MaximumEntombCount;
        ElderPrimeConfirmedButton.IsEnabled = editable && presentation.CanConfirmPrime;
        ElderPrimeConfirmedButton.Content = _elderPrimeConfirmed ? "PRIME ✓" : "PRIME CHECK";
        ElderPrimeConfirmedButton.ToolTip = !presentation.CanConfirmPrime
            ? "Reach 75% with the Prime plan ready before checking the fourth mutation slot in game"
            : _elderPrimeConfirmed
                ? "In-game fourth mutation slot confirmed · select to undo"
                : "Confirm only after the fourth mutation slot works in game";
        SetToggleButtonState(ElderPrimeConfirmedButton, _elderPrimeConfirmed);

        ElderConfirmedButton.IsEnabled = editable && presentation.CanConfirmElder;
        ElderConfirmedButton.Content = _elderConfirmed ? "ELDER ✓" : "ELDER CHECK";
        ElderConfirmedButton.ToolTip = !presentation.CanConfirmElder
            ? "Reach 100% growth before recording the in-game Elder and Entomb check"
            : _elderConfirmed
                ? "In-game Elder availability confirmed · select to undo"
                : "Confirm only after the game exposes Elder and Entomb at 100%";
        SetToggleButtonState(ElderConfirmedButton, _elderConfirmed);

        RecordEntombButton.IsEnabled = editable && presentation.CanRecordEntomb;
        RecordEntombButton.Content = _recordEntombConfirmationPending
            ? "CONFIRM ENTOMB"
            : "RECORD ENTOMB";
        RecordEntombButton.ToolTip = !presentation.CanRecordEntomb
            ? "Verify 100% Elder and Entomb availability in game first"
            : _recordEntombConfirmationPending
                ? "Select again within three seconds after the in-game Entomb has completed"
                : "Archive this run, carry equipped mutations, and begin the next same-species lineage after confirmation";
        SetToggleButtonState(RecordEntombButton, _recordEntombConfirmationPending);

        var showHudLineage = _lifeRunActive
                             && _lifeRunHudVisible
                             && !_streamerMode
                             && _hudDetailModeIndex < 2
                             && (_elderEntombCount > 0 || _lifeRunGrowthPercent >= 75 || _elderConfirmed);
        LifeRunHudLineageText.Visibility = showHudLineage
            ? Visibility.Visible
            : Visibility.Collapsed;
        var runNumber = presentation.Snapshot.EntombCount + 1;
        LifeRunHudLineageText.Text = presentation.State switch
        {
            ElderLineageState.EntombReady => $"LINEAGE {runNumber} · ENTOMB READY",
            ElderLineageState.ElderVerification => $"LINEAGE {runNumber} · ELDER CHECK",
            ElderLineageState.PrimeWindow => $"LINEAGE {runNumber} · PRIME WINDOW",
            ElderLineageState.PrimeVerification => $"LINEAGE {runNumber} · PRIME CHECK",
            ElderLineageState.FrailPath => $"LINEAGE {runNumber} · FRAIL PATH",
            ElderLineageState.Aging => $"LINEAGE {runNumber} · AGING TO ELDER",
            _ => $"LINEAGE {runNumber} · PRIME PREP"
        } + (presentation.Snapshot.InheritedMutationCount > 0
            ? $" · CARRY {presentation.Snapshot.InheritedMutationCount}"
            : string.Empty);
        LifeRunHudLineageText.ToolTip =
            $"{presentation.LineageLabel} · {presentation.MutationLabel} · {presentation.NextAction}";
    }

    private void ResetEntombConfirmation()
    {
        _recordEntombConfirmationPending = false;
        _recordEntombConfirmationRevision++;
        _elderLineageUiSignature = string.Empty;
    }

    private void ElderLineageCountButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive
            || _streamerMode
            || sender is not Button { Tag: string rawDelta }
            || !int.TryParse(rawDelta, out var delta)
            || Math.Abs(delta) != 1)
        {
            return;
        }
        var next = ElderLineageLogic.AdjustEntombCount(_elderEntombCount, delta);
        if (next == _elderEntombCount)
        {
            return;
        }
        _elderEntombCount = next;
        ResetEntombConfirmation();
        _lifeRunUiSignature = string.Empty;
        AddTacticalEvent("ELDER", "Lineage count corrected", $"Completed Entombs · {_elderEntombCount}");
        UpdateLifeRun(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
    }

    private void ElderPrimeConfirmedButton_Click(object sender, RoutedEventArgs e)
    {
        var presentation = CurrentElderLineagePresentation();
        if (!_lifeRunActive || _streamerMode || !presentation.CanConfirmPrime)
        {
            return;
        }
        _elderPrimeConfirmed = !_elderPrimeConfirmed;
        if (!_elderPrimeConfirmed)
        {
            _elderConfirmed = false;
        }
        ResetEntombConfirmation();
        _lifeRunUiSignature = string.Empty;
        AddTacticalEvent(
            "ELDER",
            _elderPrimeConfirmed ? "Prime verified in game" : "Prime verification cleared",
            _elderPrimeConfirmed ? "Fourth mutation slot available" : "Manual check reset");
        UpdateLifeRun(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
    }

    private void ElderConfirmedButton_Click(object sender, RoutedEventArgs e)
    {
        var presentation = CurrentElderLineagePresentation();
        if (!_lifeRunActive || _streamerMode || !presentation.CanConfirmElder)
        {
            return;
        }
        _elderConfirmed = !_elderConfirmed;
        ResetEntombConfirmation();
        _lifeRunUiSignature = string.Empty;
        AddTacticalEvent(
            "ELDER",
            _elderConfirmed ? "Elder verified in game" : "Elder verification cleared",
            _elderConfirmed ? "100% · Entomb available" : "Manual check reset");
        UpdateLifeRun(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
    }

    private async void RecordEntombButton_Click(object sender, RoutedEventArgs e)
    {
        var presentation = CurrentElderLineagePresentation();
        if (!_lifeRunActive || _streamerMode || !presentation.CanRecordEntomb)
        {
            return;
        }

        if (_recordEntombConfirmationPending)
        {
            var now = DateTimeOffset.Now;
            var archived = CreateCurrentLifeRunHistoryEntry(
                now,
                LifeRunHistoryLogic.EntombedOutcome);
            PrependLifeRunHistory(archived, now);
            ClearLifeTransitionSession();

            var nextEntombCount = ElderLineageLogic.AdjustEntombCount(_elderEntombCount, 1);
            var retainedSpeciesIndex = _dietSpeciesIndex;
            var retainedTargetIndex = _dietTargetIndex;
            var retainedSpeciesClass = _lifeRunSpeciesClass;
            var retainedHud = _lifeRunHudVisible;
            var retainedBuildFocusIndex = _mutationBuildFocusIndex;
            var carriedLoadout = MutationPlannerLogic.NormalizeLoadout(
                _mutationLoadout.Select(item => item with
                {
                    Status = ElderLineageLogic.CarryForwardMutationStatus(item.Status)
                })).ToList();
            var inheritedCount = carriedLoadout.Count(item => item.Status == 2);

            _lifeRunActive = true;
            _lifeRunStartedAt = DateTimeOffset.UtcNow;
            _lifeRunStageIndex = GrowthPlannerLogic.StageIndex(25);
            _lifeRunHudVisible = retainedHud;
            _lifeRunSanctuaryVisited = false;
            _lifeRunPerfectDiet = false;
            _lifeRunNestedIn = false;
            _lifeRunRaisedYoung = false;
            _spawnPlanCoverReady = false;
            _spawnPlanScentChecked = false;
            _spawnPlanWaterFound = false;
            _spawnPlanFoodFound = false;
            _spawnPlanUiSignature = string.Empty;
            _zoneBriefIndex = 0;
            _zoneBriefUiSignature = string.Empty;
            _lifeRunMigrationVisits = 0;
            _lifeRunPatrolVisits = 0;
            _lifeRunMassMigrationVisited = false;
            _lifeRunFertilityStatus = 0;
            _lifeRunSpasmStatus = 0;
            _lifeRunSpeciesClass = retainedSpeciesClass;
            _dietSpeciesIndex = retainedSpeciesIndex;
            _dietTargetIndex = retainedTargetIndex;
            _dietSlot1 = DietCoachLogic.Empty;
            _dietSlot2 = DietCoachLogic.Empty;
            _dietSlot3 = DietCoachLogic.Empty;
            _lifeRunGrowthPercent = 25;
            _growthPaused = false;
            _growthPlannerUiSignature = string.Empty;
            _elderEntombCount = nextEntombCount;
            _elderPrimeConfirmed = false;
            _elderConfirmed = false;
            ResetEntombConfirmation();

            ApplyNestPlannerSnapshot(NestPlannerLogic.Normalize(new NestPlannerSnapshot(
                false, 0, false, false, false, false, 0, 2, 0, 0, 0, 1)));
            _clearNestConfirmationPending = false;
            _clearNestConfirmationRevision++;
            _nestPlannerUiSignature = string.Empty;
            _mutationLoadout.Clear();
            _mutationLoadout.AddRange(carriedLoadout);
            _mutationSearchResults = [];
            _mutationSearchResultIndex = 0;
            _mutationBuildFocusIndex = retainedBuildFocusIndex;
            _mutationPlannerUiSignature = string.Empty;
            _mutationRemoveConfirmationSlot = 0;
            _mutationRemoveConfirmationRevision++;
            _mutationUnlockProgress.Clear();
            _mutationUnlockSelectedIndex = 0;
            _mutationUnlockUiSignature = string.Empty;
            _mutationUnlockResetConfirmationId = string.Empty;
            _mutationUnlockResetConfirmationRevision++;
            _survivalTimers.RemoveAll(timer => MutationUnlockLogic.Challenges.Any(challenge =>
                !string.IsNullOrWhiteSpace(challenge.TimerLabel)
                && string.Equals(timer.Label, challenge.TimerLabel, StringComparison.OrdinalIgnoreCase)));
            _survivalTimerUiSignature = string.Empty;
            _newLifeRunConfirmationPending = false;
            _newLifeRunConfirmationRevision++;
            _lifeRunUiSignature = string.Empty;
            ClearSurvivalIncident(logEvent: false);
            AddTacticalEvent(
                "ELDER",
                "Entomb recorded · next lineage started",
                $"Lineage {_elderEntombCount + 1} · {inheritedCount} inherited mutation{(inheritedCount == 1 ? string.Empty : "s")}");
            UpdateLifeRun(force: true);
            UpdateTacticalBrief();
            SavePlannerState();
            await ShowHotkeyToastAsync(
                $"LINEAGE {_elderEntombCount + 1} STARTED · {inheritedCount} CARRIED",
                true);
            return;
        }

        _recordEntombConfirmationPending = true;
        var revision = ++_recordEntombConfirmationRevision;
        _elderLineageUiSignature = string.Empty;
        UpdateElderLineageControls(force: true);
        await ShowHotkeyToastAsync("PRESS RECORD ENTOMB AGAIN", false);
        await Task.Delay(3000);
        if (!IsLoaded
            || !_recordEntombConfirmationPending
            || revision != _recordEntombConfirmationRevision)
        {
            return;
        }
        _recordEntombConfirmationPending = false;
        _elderLineageUiSignature = string.Empty;
        UpdateElderLineageControls(force: true);
    }

    private void UpdateDietCoachControls()
    {
        if (DietCoachComboText is null || DietSlotOneButton is null || DietLiveSpeciesButton is null) return;

        _dietSpeciesIndex = DietCoachLogic.NormalizeSpeciesIndex(_dietSpeciesIndex);
        _dietTargetIndex = DietCoachLogic.NormalizeTargetIndex(_dietTargetIndex);
        _dietSlot1 = DietCoachLogic.NormalizeNutrient(_dietSlot1);
        _dietSlot2 = DietCoachLogic.NormalizeNutrient(_dietSlot2);
        _dietSlot3 = DietCoachLogic.NormalizeNutrient(_dietSlot3);

        var result = DietCoachLogic.Analyze(_dietSlot1, _dietSlot2, _dietSlot3, _dietTargetIndex);
        var target = DietCoachLogic.Targets[_dietTargetIndex];
        var speciesBridge = CurrentLiveSpeciesBridge();
        var effectiveSpeciesIndex = speciesBridge.EffectiveSpeciesIndex;
        DietCoachComboText.Text = result.IsComplete
            ? result.Label
            : $"{result.FilledCount}/3 LOGGED";
        DietCoachGrowthText.Text = result.GrowthBonus > 0
            ? $"+{result.GrowthBonus}% GROWTH"
            : "LOG SLOTS";
        DietCoachSummaryText.Text = result.IsComplete
            ? $"{result.Summary} · {result.Effects}"
            : result.Summary;

        SetDietSlotButton(DietSlotOneButton, 1, _dietSlot1);
        SetDietSlotButton(DietSlotTwoButton, 2, _dietSlot2);
        SetDietSlotButton(DietSlotThreeButton, 3, _dietSlot3);

        if (speciesBridge.Available)
        {
            DietSpeciesText.Text = $"LIVE {speciesBridge.LiveSpeciesName.ToUpperInvariant()}";
            var savedLabel = _dietSpeciesIndex == 0
                ? "RUN UNSET"
                : speciesBridge.State == LiveSpeciesBridgeState.Matched
                    ? "LIVE MATCHED"
                    : $"RUN {DietCoachLogic.SpeciesLabel(_dietSpeciesIndex)}";
            DietSpeciesClassText.Text = $"{DietCoachLogic.SpeciesClassLabel(effectiveSpeciesIndex)} · {savedLabel}";
        }
        else
        {
            DietSpeciesText.Text = DietCoachLogic.SpeciesLabel(_dietSpeciesIndex);
            DietSpeciesClassText.Text = DietCoachLogic.SpeciesClassLabel(_dietSpeciesIndex);
        }
        DietLiveSpeciesButton.Visibility = speciesBridge.CanAdopt && _lifeRunActive && !_streamerMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        DietLiveSpeciesButton.Content = speciesBridge.ActionLabel;
        DietLiveSpeciesButton.ToolTip = speciesBridge.Detail;
        DietTargetText.Text = target.Label;
        DietTargetSlotsText.Text = string.Join(" + ", target.Nutrients.Select(DietCoachLogic.NutrientShortName));
        DietCoachRecommendationText.Text = result.Recommendation;
        DietCoachRecommendationText.Foreground = result.MatchesTarget
            ? new SolidColorBrush(Color.FromRgb(110, 231, 183))
            : new SolidColorBrush(Color.FromRgb(251, 191, 36));
        DietCoachFoodText.Text = DietCoachLogic.FoodForNutrient(effectiveSpeciesIndex, result.NeededNutrient);
        DietFoodLayerButton.IsEnabled = LiveMapServicesActive && _foodLayer is not null;
        DietFoodLayerButton.Content = !LiveMapServicesActive
            ? "LIVE MAP MODE ONLY"
            : _foodLayer is true ? "FOOD LAYER ON" : "SHOW FOOD";
        SetToggleButtonState(DietFoodLayerButton, _foodLayer is true);
        var resourceQuery = CurrentDietResourceQuery(result);
        var resourceAvailable = _gatewayResourceNetwork is not null
                                && ResourceFinderLogic.Select(
                                    _gatewayResourceNetwork.Points,
                                    resourceQuery,
                                    null,
                                    null) is not null;
        DietFindResourceButton.IsEnabled = _lifeRunActive
                                           && !_streamerMode
                                           && LiveMapServicesActive
                                           && resourceAvailable;
        DietFindResourceButton.ToolTip = resourceAvailable
            ? $"Find the nearest public {resourceQuery} site for this recommendation"
            : "No mapped public site matches the current nutrient recommendation";
        ResetDietSlotsButton.IsEnabled = result.FilledCount > 0;
    }

    private string CurrentDietResourceQuery(DietComboResult? analysis = null)
    {
        var result = analysis ?? DietCoachLogic.Analyze(
            _dietSlot1,
            _dietSlot2,
            _dietSlot3,
            _dietTargetIndex);
        var nutrient = result.NeededNutrient == DietCoachLogic.Empty
            ? DietCoachLogic.Targets[_dietTargetIndex].Nutrients[0]
            : result.NeededNutrient;
        return ResourceFinderLogic.SuggestedDietQuery(
            CurrentEffectiveSpeciesIndex(),
            nutrient,
            _gatewayResourceNetwork?.Points);
    }

    private void SetDietSlotButton(Button button, int slotNumber, int nutrient)
    {
        button.Content = $"S{slotNumber} {DietCoachLogic.NutrientShortName(nutrient)}";
        button.ToolTip = nutrient switch
        {
            DietCoachLogic.Protein => "Protein logged · select to change this slot to Carbs",
            DietCoachLogic.Carbs => "Carbs logged · select to change this slot to Lipids",
            DietCoachLogic.Lipids => "Lipids logged · select to clear this slot",
            _ => "Empty · select to log Protein"
        };
        SetToggleButtonState(button, nutrient != DietCoachLogic.Empty);
    }

    private void CommitDietCoachChange(string? eventDetail = null)
    {
        _newLifeRunConfirmationPending = false;
        _lifeRunUiSignature = string.Empty;
        if (!string.IsNullOrWhiteSpace(eventDetail))
        {
            AddTacticalEvent("DIET", "Diet coach updated", eventDetail);
        }
        UpdateLifeRun(force: true);
        UpdateNextMove(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
    }

    private void DietSlotButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || sender is not Button { Tag: string rawSlot }
            || !int.TryParse(rawSlot, out var slot)) return;

        var previous = DietCoachLogic.Analyze(_dietSlot1, _dietSlot2, _dietSlot3, _dietTargetIndex);
        switch (slot)
        {
            case 1:
                _dietSlot1 = (_dietSlot1 + 1) % 4;
                break;
            case 2:
                _dietSlot2 = (_dietSlot2 + 1) % 4;
                break;
            case 3:
                _dietSlot3 = (_dietSlot3 + 1) % 4;
                break;
            default:
                return;
        }

        var current = DietCoachLogic.Analyze(_dietSlot1, _dietSlot2, _dietSlot3, _dietTargetIndex);
        if (current.Key == "P+C+L") _lifeRunPerfectDiet = true;
        CommitDietCoachChange(!previous.IsComplete && current.IsComplete
            ? $"{current.Key} · {current.Label}"
            : null);
    }

    private void DietSpeciesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || sender is not Button { Tag: string rawDelta }
            || !int.TryParse(rawDelta, out var delta)) return;
        var optionCount = DietCoachLogic.Species.Length + 1;
        _dietSpeciesIndex = (_dietSpeciesIndex + delta + optionCount) % optionCount;
        CommitDietCoachChange();
    }

    private void ApplyLiveSpeciesToSavedRun(LiveSpeciesBridgeView bridge)
    {
        _dietSpeciesIndex = bridge.LiveSpeciesIndex;
        var speciesChanged = !string.Equals(
            _guideSelectedSpeciesId,
            bridge.LiveSpeciesId,
            StringComparison.OrdinalIgnoreCase);
        _guideSelectedSpeciesId = bridge.LiveSpeciesId;
        if (speciesChanged)
        {
            ApplyAimCalibrationForSelection(useDefaultsWhenMissing: true, force: false);
        }
        _guideUiSignature = string.Empty;
        _fightCheckUiSignature = string.Empty;
        _growthPlannerUiSignature = string.Empty;
        _lifeRunUiSignature = string.Empty;
    }

    private async void DietLiveSpeciesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode) return;
        var bridge = CurrentLiveSpeciesBridge();
        if (!bridge.Available || !bridge.CanAdopt)
        {
            await ShowHotkeyToastAsync(
                bridge.Available ? "LIFE RUN SPECIES ALREADY MATCHED" : "FRESH LIVE SPECIES REQUIRED",
                bridge.Available);
            return;
        }

        ApplyLiveSpeciesToSavedRun(bridge);
        AddTacticalEvent(
            "LIFE",
            "Life run species synchronized",
            $"Saved species · {bridge.LiveSpeciesName}");
        UpdateLifeRun(force: true);
        UpdateFieldGuide(force: true);
        UpdateNextMove(force: true);
        UpdateFightCheck(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
        await ShowHotkeyToastAsync($"RUN SPECIES · {bridge.LiveSpeciesName.ToUpperInvariant()}", true);
    }

    private void DietTargetButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || sender is not Button { Tag: string rawDelta }
            || !int.TryParse(rawDelta, out var delta)) return;
        var optionCount = DietCoachLogic.Targets.Length;
        _dietTargetIndex = (_dietTargetIndex + delta + optionCount) % optionCount;
        CommitDietCoachChange();
    }

    private void ResetDietSlotsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode) return;
        _dietSlot1 = DietCoachLogic.Empty;
        _dietSlot2 = DietCoachLogic.Empty;
        _dietSlot3 = DietCoachLogic.Empty;
        CommitDietCoachChange("Nutrient slots cleared");
    }

    private async void DietFoodLayerButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode) return;
        var enabled = _foodLayer is true || await ExecuteMapperCommandAsync(
            "window.__isley?.setOfficialLayer('food', true) ?? false");
        await ShowHotkeyToastAsync(
            enabled ? "FOOD LAYER ON" : "FOOD LAYER UNAVAILABLE",
            enabled);
        if (enabled)
        {
            AddTacticalEvent("DIET", "Food layer opened", "Live server map");
        }
    }

    private async void DietFindResourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || !LiveMapServicesActive)
        {
            return;
        }

        await OpenResourceFinderForQueryAsync(
            CurrentDietResourceQuery(),
            "NO PUBLIC SITE FOR THIS FOOD",
            "Diet Coach recommendation");
    }

    private static string FormatGrowthMultiplier(double value) => $"{value:0.#}X";

    private void UpdateGrowthClockControls(bool force = false)
    {
        if (GrowthClockSpeciesText is null
            || GrowthPercentButton is null
            || GrowthClockProgressTransform is null
            || GrowthLiveBridgePanel is null
            || GrowthLiveAdoptButton is null
            || LifeRunLiveStartText is null)
        {
            return;
        }

        _lifeRunGrowthPercent = Math.Clamp(_lifeRunGrowthPercent, 0, 100);
        _growthServerMultiplierIndex = Math.Clamp(
            _growthServerMultiplierIndex,
            0,
            GrowthPlannerLogic.ServerMultipliers.Length - 1);
        if (_lifeRunActive) _lifeRunStageIndex = GrowthPlannerLogic.StageIndex(_lifeRunGrowthPercent);
        var bridge = CurrentLiveGrowthBridge();
        var speciesBridge = CurrentLiveSpeciesBridge();
        var result = CurrentGrowthPlannerResult();
        var signature = string.Join('|', new object[]
        {
            _lifeRunActive, _streamerMode, _dietSpeciesIndex,
            _lifeRunGrowthPercent, _growthServerMultiplierIndex, _growthPaused,
            _serverSessionProfileId, _serverSessionName,
            bridge.State, bridge.Available, bridge.LiveGrowthPercent, bridge.DriftPercent,
            bridge.PrimeAvailable, bridge.PrimeCompleted, bridge.PrimeRequired,
            result.DietMultiplier, result.Snapshot.PrimeCount, result.Snapshot.PrimeRequired,
            result.EtaLabel,
            _growthGatePending?.Key ?? string.Empty
        });
        if (!force && string.Equals(signature, _growthPlannerUiSignature, StringComparison.Ordinal)) return;
        _growthPlannerUiSignature = signature;

        var available = _lifeRunActive && !_streamerMode;
        GrowthPercentButton.IsEnabled = available;
        GrowthServerMultiplierButton.IsEnabled = available;
        GrowthPauseButton.IsEnabled = available && _lifeRunGrowthPercent < 100;
        GrowthLiveBridgePanel.Visibility = _streamerMode ? Visibility.Collapsed : Visibility.Visible;
        var canStartFromLive = !_lifeRunActive
                               && !_streamerMode
                               && bridge.State == LiveGrowthBridgeState.ReadyToStart;
        LifeRunLiveStartText.Visibility = canStartFromLive ? Visibility.Visible : Visibility.Collapsed;
        var liveStartSpecies = speciesBridge.Available
            ? $"{speciesBridge.LiveSpeciesName.ToUpperInvariant()} · "
            : string.Empty;
        LifeRunLiveStartText.Text = bridge.PrimeAvailable
            ? $"LIVE {liveStartSpecies}{bridge.LiveGrowthPercent}% · PRIME {bridge.PrimeCompleted}/{bridge.PrimeRequired} · START USES LIVE DINO"
            : $"LIVE {liveStartSpecies}{bridge.LiveGrowthPercent}% · START USES LIVE DINO · PRIME MANUAL";
        LifeRunStartButton.Content = canStartFromLive
            ? $"START @ {bridge.LiveGrowthPercent}%"
            : "START LIFE RUN";
        LifeRunStartButton.ToolTip = canStartFromLive
            ? "Start the private local Life Run with the fresh recognized species and growth percentage; Prime remains read-only"
            : "Start a private manual tracker for the current dinosaur life";
        UpdateGrowthGateWatchPresentation();

        if (_streamerMode)
        {
            GrowthClockSpeciesText.Text = "HIDDEN IN STREAMER MODE";
            GrowthClockTargetText.Text = string.Empty;
            GrowthClockRateText.Text = "PRIVATE CURRENT-LIFE ESTIMATE";
            GrowthClockActionText.Text = "Growth Clock is hidden with the Life Run.";
            return;
        }

        var species = result.Species;
        GrowthClockSpeciesText.Text = species is { } selected
            ? $"{selected.Name.ToUpperInvariant()} / {(selected.Approximate ? "~" : string.Empty)}{selected.BaseHours:0.##}H BASE"
            : "CHOOSE SPECIES";
        GrowthClockSpeciesText.ToolTip = species is { } timing
            ? $"Community base-time snapshot {GrowthPlannerLogic.SnapshotDate}" +
              (timing.Approximate ? " · this species timing is approximate" : string.Empty)
            : "Choose the current dinosaur in Diet Coach";
        GrowthClockTargetText.Text = bridge.EffectiveGrowthPercent >= 100
            ? "100% / ELDER"
            : $"{bridge.EffectiveGrowthPercent}% > {result.Milestone.Percent}%";
        GrowthClockTargetText.ToolTip = result.Milestone.Label;
        GrowthClockProgressTransform.ScaleX = bridge.EffectiveGrowthPercent / 100d;
        GrowthPercentButton.Content = bridge.Available
            ? $"RUN {_lifeRunGrowthPercent}% / +1"
            : $"{_lifeRunGrowthPercent}% / +1";
        GrowthLiveStateText.Text = bridge.StateLabel;
        GrowthLiveValuesText.Text = bridge.ValueLabel;
        GrowthLiveDetailText.Text = bridge.Detail;
        GrowthLiveAdoptButton.Content = bridge.ActionLabel;
        GrowthLiveAdoptButton.IsEnabled = bridge.CanAdopt;
        GrowthLiveAdoptButton.ToolTip = bridge.CanAdopt
            ? bridge.State == LiveGrowthBridgeState.ReadyToStart
                ? "Start a private local Life Run at the current fresh live growth percentage; Prime remains read-only"
                : "Update only the saved Life Run growth percentage to the current fresh live value"
            : bridge.Available
                ? "The saved Life Run already matches the current fresh live growth percentage"
            : "A fresh online Live Map current-dino snapshot is required";
        var bridgeAccent = bridge.State == LiveGrowthBridgeState.Drifted
            ? (Brush)FindResource("WarningBrush")
            : bridge.Available
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("SecondaryTextBrush");
        GrowthLiveStateText.Foreground = bridgeAccent;
        GrowthLiveValuesText.Foreground = bridgeAccent;
        GrowthLiveAdoptButton.Foreground = bridgeAccent;
        GrowthLiveAdoptButton.BorderBrush = bridgeAccent;
        GrowthServerMultiplierButton.Content =
            $"SERVER {FormatGrowthMultiplier(result.ServerMultiplier)}";
        GrowthServerMultiplierButton.ToolTip = CurrentRatePreset() is { } activeRatePreset
            ? $"Cycle the server growth multiplier · preset {activeRatePreset.Label} applied " +
              "(Quick Commands: apply next or save custom presets)"
            : "Cycle the server growth multiplier · Quick Commands apply or save named rate presets";
        GrowthPauseButton.Content = _growthPaused ? "PAUSED" : "GROWING";
        SetToggleButtonState(GrowthPauseButton, _growthPaused);
        if (_growthPaused)
        {
            GrowthPauseButton.Foreground = (Brush)FindResource("WarningBrush");
            GrowthPauseButton.BorderBrush = (Brush)FindResource("WarningBrush");
        }

        var effectiveRate = result.DietMultiplier > 0
            ? result.ServerMultiplier * result.DietMultiplier
            : 0;
        GrowthClockRateText.Text = result.Species is null
            ? "NO SPECIES / CHOOSE IN DIET COACH"
            : _growthPaused
                ? "PAUSED / FOOD + WATER CHECK"
                : result.DietMultiplier == 0
                    ? $"SERVER {FormatGrowthMultiplier(result.ServerMultiplier)} / LOG NUTRIENTS FOR ETA"
                    : $"DIET {result.DietMultiplier}X / EFFECTIVE {FormatGrowthMultiplier(effectiveRate)} / ETA {result.EtaLabel}";
        GrowthClockRateText.ToolTip =
            "Ballpark next-gate estimate = base time × remaining percent ÷ server multiplier ÷ logged nutrient count";
        var disclosure = _serverSessionProfileId switch
        {
            ServerSessionLogic.LiveMapId =>
            "Manual estimate · choose the server's advertised rate · base times and lifecycle gates can change by patch or server",
            ServerSessionLogic.OfficialId =>
                "Manual estimate · the profile suggests the vanilla 1x baseline · verify the selected official server and current patch",
            _ =>
                $"Manual estimate · set the rate advertised by {ServerSessionLogic.DisplayName(_serverSessionProfileId, _serverSessionName)} · community rules can differ"
        };
        GrowthClockDisclosureText.Text = bridge.Available
            ? "Fresh live growth and Prime guide decisions only · Start/Sync is the only saved-run write · " + disclosure
            : disclosure;
        GrowthClockActionText.Text = result.Advice;
        GrowthClockActionText.Foreground = _growthPaused
            ? (Brush)FindResource("WarningBrush")
            : result.Snapshot.GrowthPercent >= 75
              || result.Snapshot.PrimeCount >= result.Snapshot.PrimeRequired
                ? new SolidColorBrush(Color.FromRgb(110, 231, 183))
                : new SolidColorBrush(Color.FromRgb(251, 191, 36));
    }

    private void UpdateGrowthGateWatchPresentation()
    {
        if (GrowthGateWatchPanel is null
            || GrowthGateWatchLabelText is null
            || GrowthGateWatchHeadingText is null
            || GrowthGateWatchDetailText is null
            || GrowthGateWatchActionButton is null)
        {
            return;
        }

        var gate = _growthGatePending;
        var visible = _lifeRunActive
                      && !_streamerMode
                      && gate?.Detected == true;
        GrowthGateWatchPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible || gate is null)
        {
            _growthGateUiSignature = string.Empty;
            return;
        }

        GrowthGateWatchLabelText.Text = $"GROWTH GATE · {gate.Value.GatePercent}%";
        GrowthGateWatchHeadingText.Text = gate.Value.Heading;
        GrowthGateWatchDetailText.Text = gate.Value.Detail;
        GrowthGateWatchActionButton.Content = gate.Value.ActionLabel;
        GrowthGateWatchActionButton.Tag = gate.Value.ActionId;
        GrowthGateWatchActionButton.ToolTip =
            $"{gate.Value.Detail} Open the relevant existing Isley workspace.";
        if (string.Equals(_growthGateUiSignature, gate.Value.Key, StringComparison.Ordinal))
        {
            return;
        }

        _growthGateUiSignature = gate.Value.Key;
        GrowthGateWatchPanel.Opacity = 0;
        GrowthGateWatchPanel.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0,
                1,
                TimeSpan.FromMilliseconds(160)));
    }

    private async void GrowthGateWatchActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode
            || _growthGatePending?.Detected != true
            || sender is not Button { Tag: string actionId }
            || actionId is not "mutation-planner" and not "prime-planner" and not "elder-lineage")
        {
            return;
        }

        await ExecuteCommandPaletteActionAsync(actionId);
    }

    private async void GrowthGateWatchAcknowledgeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || _growthGatePending?.Detected != true)
        {
            return;
        }

        var gatePercent = _growthGatePending.Value.GatePercent;
        _growthGatePending = null;
        _growthGateUiSignature = string.Empty;
        _growthPlannerUiSignature = string.Empty;
        _lifeRunUiSignature = string.Empty;
        _nextMoveUiSignature = string.Empty;
        AddTacticalEvent("GROWTH", "Growth gate acknowledged", $"{gatePercent}% · no saved state changed");
        UpdateLifeRun(force: true);
        UpdateNextMove(force: true);
        UpdateTacticalBrief();
        await ShowHotkeyToastAsync($"GROWTH GATE {gatePercent}% ACKNOWLEDGED", true);
    }

    private void CommitGrowthClockChange(string title, string detail, bool warning = false)
    {
        _lifeRunStageIndex = GrowthPlannerLogic.StageIndex(_lifeRunGrowthPercent);
        _newLifeRunConfirmationPending = false;
        _growthPlannerUiSignature = string.Empty;
        _lifeRunUiSignature = string.Empty;
        AddTacticalEvent("GROWTH", title, detail, warning);
        UpdateLifeRun(force: true);
        UpdateNextMove(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
    }

    private async Task AdoptLiveGrowthBridgeAsync(LiveGrowthBridgeView bridge)
    {
        var startedRun = !_lifeRunActive;
        if (startedRun)
        {
            StartNewLifeRun(logEvent: false);
        }

        _lifeRunGrowthPercent = bridge.LiveGrowthPercent;
        _lifeRunStageIndex = GrowthPlannerLogic.StageIndex(_lifeRunGrowthPercent);
        CommitGrowthClockChange(
            startedRun ? "Life run started from live growth" : "Life run synchronized",
            $"Saved growth {_lifeRunGrowthPercent}% · live Prime remains read-only");
        await ShowHotkeyToastAsync(
            startedRun
                ? $"LIFE RUN STARTED @ {_lifeRunGrowthPercent}%"
                : $"LIFE RUN SYNCED @ {_lifeRunGrowthPercent}%",
            true);
    }

    private async void GrowthLiveAdoptButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        var bridge = CurrentLiveGrowthBridge();
        if (!bridge.Available || !bridge.CanAdopt)
        {
            await ShowHotkeyToastAsync(
                bridge.Available ? "LIFE RUN ALREADY MATCHED" : "FRESH LIVE GROWTH REQUIRED",
                bridge.Available);
            return;
        }

        await AdoptLiveGrowthBridgeAsync(bridge);
    }

    private void GrowthPercentButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode
            || sender is not Button { Tag: string rawDelta }
            || !int.TryParse(rawDelta, out var delta)
            || delta is not (-5 or 1 or 5))
        {
            return;
        }

        var previous = _lifeRunGrowthPercent;
        _lifeRunGrowthPercent = Math.Clamp(previous + delta, 0, 100);
        if (_lifeRunGrowthPercent == previous) return;
        var crossed = delta > 0
            ? GrowthPlannerLogic.Milestones
                .Where(item => item.Percent > previous && item.Percent <= _lifeRunGrowthPercent)
                .LastOrDefault()
            : default;
        if (crossed.Percent > 0)
        {
            var primeCount = LifeRunPrimeConditionCount();
            var primeRequired = LifeRunPrimeRequiredConditionCount();
            var missedPrimeDeadline = crossed.Percent == 75 && primeCount < primeRequired;
            CommitGrowthClockChange(
                crossed.Percent == 75 ? "Prime gate reached" : "Growth gate reached",
                $"{crossed.Percent}% / {crossed.Label}" +
                (crossed.Percent == 75 ? $" / Prime {primeCount}/{primeRequired}" : string.Empty),
                missedPrimeDeadline);
            return;
        }

        CommitGrowthClockChange(
            "Growth reading updated",
            $"{_lifeRunGrowthPercent}% / {_lifeRunStageLabels[GrowthPlannerLogic.StageIndex(_lifeRunGrowthPercent)]}");
    }

    private void GrowthServerMultiplierButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode) return;
        _growthServerMultiplierIndex =
            (_growthServerMultiplierIndex + 1) % GrowthPlannerLogic.ServerMultipliers.Length;
        if (CommunitySessionActive)
        {
            SyncCurrentCommunityServerProfile(includeGrowthRate: true);
            UpdateServerSessionPresentation();
        }
        var multiplier = GrowthPlannerLogic.ServerMultipliers[_growthServerMultiplierIndex];
        CommitGrowthClockChange("Growth multiplier updated", $"Server / {multiplier:0.#}x");
    }

    private void GrowthPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || _lifeRunGrowthPercent >= 100) return;
        _growthPaused = !_growthPaused;
        CommitGrowthClockChange(
            _growthPaused ? "Growth clock paused" : "Growth clock resumed",
            _growthPaused ? "Manual food or water floor" : "Manual growth estimate active",
            _growthPaused);
    }

    private void UpdateNestPlannerControls(bool force = false)
    {
        if (NestPlannerStatusText is null
            || NestPlannerStartButton is null
            || NestPlannerActivePanel is null
            || LifeRunHudNestText is null)
        {
            return;
        }

        var nest = CurrentNestPlannerSnapshot();
        ApplyNestPlannerSnapshot(nest);
        var phase = NestPlannerLogic.Phase(nest);
        var autoHatch = NestPlannerLogic.EvaluateAutoHatch(
            nest,
            _nestAutoHatchGuidanceEnabled);
        var timerLabel = NestPlannerLogic.TimerLabel(nest);
        var matchingTimer = string.IsNullOrWhiteSpace(timerLabel)
            ? null
            : _survivalTimers.FirstOrDefault(timer =>
                string.Equals(timer.Label, timerLabel, StringComparison.OrdinalIgnoreCase));
        var signature = string.Join('|', new object[]
        {
            _lifeRunActive, _streamerMode, nest.Active, nest.PhaseIndex,
            nest.PartnerReady, nest.SiteReady, nest.DebrisReady, nest.ReservesReady,
            nest.AccessIndex, nest.EggTarget, nest.EggsLaid, nest.EggsHatched,
            nest.YoungRaised, nest.TimerDurationIndex, _clearNestConfirmationPending,
            _nestAutoHatchGuidanceEnabled, _nestTimerAlertPresetIndex, autoHatch.State, autoHatch.Heading,
            timerLabel, matchingTimer is not null, _survivalTimers.Count
        });
        if (!force && string.Equals(signature, _nestPlannerUiSignature, StringComparison.Ordinal)) return;
        _nestPlannerUiSignature = signature;

        var available = _lifeRunActive && !_streamerMode;
        NestPlannerStartButton.Visibility = available && !nest.Active
            ? Visibility.Visible
            : Visibility.Collapsed;
        NestPlannerActivePanel.Visibility = available && nest.Active
            ? Visibility.Visible
            : Visibility.Collapsed;
        LifeRunHudNestText.Visibility = available && nest.Active
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_streamerMode)
        {
            NestPlannerStatusText.Text = "HIDDEN";
            NestPlannerStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            return;
        }
        if (!_lifeRunActive)
        {
            NestPlannerStatusText.Text = "START LIFE RUN";
            NestPlannerStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            return;
        }
        if (!nest.Active)
        {
            NestPlannerStatusText.Text = "NO PLAN";
            NestPlannerStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            return;
        }

        var readyCount = NestPlannerLogic.ReadinessCount(nest);
        NestPlannerStatusText.Text = $"{readyCount}/4 READY";
        NestPlannerStatusText.Foreground = readyCount == 4
            ? new SolidColorBrush(Color.FromRgb(110, 231, 183))
            : (Brush)FindResource("SecondaryTextBrush");
        NestPlannerPhaseText.Text = $"{nest.PhaseIndex + 1}/{NestPlannerLogic.Phases.Length} / {phase.Label}";
        NestPlannerProgressText.Text = $"READY {readyCount}/4 / HATCH {nest.EggsHatched}/{nest.EggsLaid}";
        NestPlannerProgressTransform.ScaleX = (nest.PhaseIndex + 1d) / NestPlannerLogic.Phases.Length;
        NestPlannerPhaseActionText.Text =
            $"NEXT / {NestPlannerLogic.NextAction(nest, _nestAutoHatchGuidanceEnabled)}";
        NestPhaseBackButton.IsEnabled = nest.PhaseIndex > 0;
        NestPhaseNextButton.IsEnabled = nest.PhaseIndex < NestPlannerLogic.Phases.Length - 1;

        SetNestReadinessButton(NestPartnerButton, "PAIR / SOLO", nest.PartnerReady);
        SetNestReadinessButton(NestSiteButton, "SAFE SITE", nest.SiteReady);
        SetNestReadinessButton(NestDebrisButton, "DEBRIS", nest.DebrisReady);
        SetNestReadinessButton(NestReservesButton, "RESERVES", nest.ReservesReady);

        NestEggTargetButton.Content = $"TARGET {nest.EggTarget}";
        NestEggsLaidButton.Content = $"LAID {nest.EggsLaid}/{nest.EggTarget}";
        NestEggsHatchedButton.Content = $"HATCHED {nest.EggsHatched}/{nest.EggsLaid}";
        NestYoungRaisedButton.Content = $"RAISED {nest.YoungRaised}/{nest.EggsHatched}";
        NestEggsHatchedButton.IsEnabled = nest.EggsLaid > 0;
        NestYoungRaisedButton.IsEnabled = nest.EggsHatched > 0;
        SetToggleButtonState(NestEggsLaidButton, nest.EggsLaid > 0);
        SetToggleButtonState(NestEggsHatchedButton, nest.EggsHatched > 0);
        SetToggleButtonState(NestYoungRaisedButton, nest.YoungRaised > 0);

        NestAccessButton.Content = NestPlannerLogic.AccessLabel(nest.AccessIndex);
        SetToggleButtonState(NestAccessButton, nest.AccessIndex == 1);
        NestAutoHatchGuidanceButton.Content = _nestAutoHatchGuidanceEnabled
            ? "AUTO-HATCH ON"
            : "AUTO-HATCH OFF";
        NestAutoHatchGuidanceButton.ToolTip = _nestAutoHatchGuidanceEnabled
            ? "Hide public-branch auto-hatch guidance; the manual clutch ledger remains unchanged"
            : "Show public-branch auto-hatch guidance without guessing an automatic hatch duration";
        SetToggleButtonState(NestAutoHatchGuidanceButton, _nestAutoHatchGuidanceEnabled);
        NestAutoHatchGuidanceText.Text = autoHatch.Heading;
        NestAutoHatchGuidanceText.ToolTip = autoHatch.Detail;
        NestAutoHatchGuidanceText.Foreground = autoHatch.State switch
        {
            NestAutoHatchState.Pending => (Brush)FindResource("WarningBrush"),
            NestAutoHatchState.Synchronized => (Brush)FindResource("SuccessBrush"),
            _ => (Brush)FindResource("SecondaryTextBrush")
        };
        NestTimerDurationButton.Content = $"{NestPlannerLogic.TimerDurationMinutes(nest)} MIN";
        NestStartTimerButton.IsEnabled = !string.IsNullOrWhiteSpace(timerLabel)
                                         && (matchingTimer is not null || _survivalTimers.Count < 4);
        NestStartTimerButton.Content = string.IsNullOrWhiteSpace(timerLabel)
            ? "NO TIMER"
            : matchingTimer is not null
                ? "RESET TIMER"
                : "START TIMER";
        NestStartTimerButton.ToolTip = string.IsNullOrWhiteSpace(timerLabel)
            ? "A timer is available during Gestate and Incubate"
            : $"{(matchingTimer is null ? "Start" : "Reset")} a manual {timerLabel.ToLowerInvariant()} timer" +
              $" · countdown alerts {NestTimerAlertLogic.PresetLabel(_nestTimerAlertPresetIndex)}";
        ClearNestPlanButton.Content = _clearNestConfirmationPending ? "CONFIRM RESET" : "RESET CLUTCH";
        ClearNestPlanButton.ToolTip = _clearNestConfirmationPending
            ? "Select again within three seconds to clear this clutch ledger"
            : "Clear this clutch ledger after confirmation; Life Run milestones stay intact";
    }

    private void SetNestReadinessButton(Button button, string label, bool ready)
    {
        button.Content = ready ? $"{label} ✓" : label;
        SetToggleButtonState(button, ready);
    }

    private void CommitNestPlannerChange(string title, string detail)
    {
        ApplyNestPlannerSnapshot(CurrentNestPlannerSnapshot());
        _newLifeRunConfirmationPending = false;
        _clearNestConfirmationPending = false;
        _clearNestConfirmationRevision++;
        _nestPlannerUiSignature = string.Empty;
        _lifeRunUiSignature = string.Empty;
        AddTacticalEvent("NEST", title, detail);
        UpdateLifeRun(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
    }

    private void NestPlannerStartButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || _nestPlannerActive) return;
        ApplyNestPlannerSnapshot(new NestPlannerSnapshot(
            true, 0, false, false, false, false, 0,
            Math.Clamp(_nestEggTarget, 1, NestPlannerLogic.MaxEggs), 0, 0, 0,
            Math.Clamp(_nestTimerDurationIndex, 0, NestPlannerLogic.TimerMinutes.Length - 1)));
        CommitNestPlannerChange("Nest plan started", $"Prepare / target {_nestEggTarget}");
        MaybeShowPressureCoach(PressureCoachLogic.FirstNest(_pressureCoachFirstNestSeen, true), () =>
        {
            _pressureCoachFirstNestSeen = true;
        });
        _ = ShowHotkeyToastAsync("NEST PLAN STARTED", true);
    }

    private void NestPhaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || !_nestPlannerActive
            || sender is not Button { Tag: string rawDelta }
            || !int.TryParse(rawDelta, out var delta)
            || Math.Abs(delta) != 1)
        {
            return;
        }
        var nextIndex = Math.Clamp(_nestPhaseIndex + delta, 0, NestPlannerLogic.Phases.Length - 1);
        if (nextIndex == _nestPhaseIndex) return;
        _nestPhaseIndex = nextIndex;
        var phase = NestPlannerLogic.Phases[_nestPhaseIndex];
        CommitNestPlannerChange("Nest phase updated", phase.Label);
    }

    private void NestReadinessButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || !_nestPlannerActive
            || sender is not Button { Tag: string readiness }) return;
        string label;
        bool ready;
        switch (readiness)
        {
            case "partner":
                _nestPartnerReady = !_nestPartnerReady;
                label = "Partner";
                ready = _nestPartnerReady;
                break;
            case "site":
                _nestSiteReady = !_nestSiteReady;
                label = "Safe site";
                ready = _nestSiteReady;
                break;
            case "debris":
                _nestDebrisReady = !_nestDebrisReady;
                label = "Debris";
                ready = _nestDebrisReady;
                break;
            case "reserves":
                _nestReservesReady = !_nestReservesReady;
                label = "Reserves";
                ready = _nestReservesReady;
                break;
            default:
                return;
        }
        CommitNestPlannerChange("Nest readiness updated", $"{label} / {(ready ? "ready" : "not ready")}");
    }

    private void NestCounterButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || !_nestPlannerActive
            || sender is not Button { Tag: string counter }) return;
        string detail;
        switch (counter)
        {
            case "target":
                _nestEggTarget = _nestEggTarget >= NestPlannerLogic.MaxEggs ? 1 : _nestEggTarget + 1;
                detail = $"Target / {_nestEggTarget}";
                break;
            case "laid":
                _nestEggsLaid = _nestEggsLaid >= _nestEggTarget ? 0 : _nestEggsLaid + 1;
                if (_nestEggsLaid > 0) _nestPhaseIndex = Math.Max(_nestPhaseIndex, 4);
                detail = $"Laid / {_nestEggsLaid}/{_nestEggTarget}";
                break;
            case "hatched":
                if (_nestEggsLaid <= 0) return;
                _nestEggsHatched = _nestEggsHatched >= _nestEggsLaid ? 0 : _nestEggsHatched + 1;
                if (_nestEggsHatched > 0) _nestPhaseIndex = Math.Max(_nestPhaseIndex, 6);
                detail = $"Hatched / {_nestEggsHatched}/{_nestEggsLaid}";
                break;
            case "raised":
                if (_nestEggsHatched <= 0) return;
                _nestYoungRaised = _nestYoungRaised >= _nestEggsHatched ? 0 : _nestYoungRaised + 1;
                if (_nestYoungRaised > 0)
                {
                    _nestPhaseIndex = Math.Max(_nestPhaseIndex, 8);
                    _lifeRunRaisedYoung = true;
                }
                detail = $"Raised / {_nestYoungRaised}/{_nestEggsHatched}";
                break;
            default:
                return;
        }
        ApplyNestPlannerSnapshot(CurrentNestPlannerSnapshot());
        CommitNestPlannerChange("Clutch ledger updated", detail);
    }

    private void NestAccessButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || !_nestPlannerActive) return;
        _nestAccessIndex = (_nestAccessIndex + 1) % 2;
        CommitNestPlannerChange("Nest access updated", NestPlannerLogic.AccessLabel(_nestAccessIndex));
    }

    private void NestAutoHatchGuidanceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || !_nestPlannerActive) return;
        _nestAutoHatchGuidanceEnabled = !_nestAutoHatchGuidanceEnabled;
        CommitNestPlannerChange(
            "Auto-hatch guidance updated",
            _nestAutoHatchGuidanceEnabled
                ? "On · public-branch reminder"
                : "Off · manual clutch ledger unchanged");
    }

    private void NestTimerDurationButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || !_nestPlannerActive) return;
        _nestTimerDurationIndex = (_nestTimerDurationIndex + 1) % NestPlannerLogic.TimerMinutes.Length;
        CommitNestPlannerChange("Nest timer duration updated", $"{NestPlannerLogic.TimerDurationMinutes(CurrentNestPlannerSnapshot())}m");
    }

    private async void NestStartTimerButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || !_nestPlannerActive) return;
        var nest = CurrentNestPlannerSnapshot();
        var label = NestPlannerLogic.TimerLabel(nest);
        if (string.IsNullOrWhiteSpace(label)) return;
        var minutes = NestPlannerLogic.TimerDurationMinutes(nest);
        var timer = _survivalTimers.FirstOrDefault(candidate =>
            string.Equals(candidate.Label, label, StringComparison.OrdinalIgnoreCase));
        if (timer is not null)
        {
            timer.DurationSeconds = minutes * 60;
            timer.EndsAt = DateTimeOffset.UtcNow.AddMinutes(minutes);
            timer.PausedRemainingSeconds = 0;
            timer.IsPaused = false;
            timer.Completed = false;
            timer.CompletionNotified = false;
            _nestTimerAlertNotifiedMasks.Remove(timer.Id);
            _clearTimersConfirmationPending = false;
            _survivalTimerUiSignature = string.Empty;
            _nestPlannerUiSignature = string.Empty;
            AddTacticalEvent("TIMER", "Nest timer reset", $"{label} / {minutes}m");
            UpdateSurvivalTimers(force: true);
            UpdateNestPlannerControls(force: true);
            SavePlannerState();
            await ShowHotkeyToastAsync($"{label.ToUpperInvariant()} RESET / {minutes}M", true);
            return;
        }

        var started = StartSurvivalTimer(label, minutes);
        _nestPlannerUiSignature = string.Empty;
        UpdateNestPlannerControls(force: true);
        await ShowHotkeyToastAsync(
            started ? $"{label.ToUpperInvariant()} STARTED / {minutes}M" : "TIMER LIMIT / CLEAR ONE",
            started);
    }

    private async void ClearNestPlanButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || !_nestPlannerActive) return;
        if (_clearNestConfirmationPending)
        {
            var previous = CurrentNestPlannerSnapshot();
            ApplyNestPlannerSnapshot(new NestPlannerSnapshot(
                false, 0, false, false, false, false, 0, 2, 0, 0, 0, 1));
            _nestTimerAlertNotifiedMasks.Clear();
            _clearNestConfirmationPending = false;
            _clearNestConfirmationRevision++;
            _nestPlannerUiSignature = string.Empty;
            _lifeRunUiSignature = string.Empty;
            AddTacticalEvent("NEST", "Clutch ledger reset",
                $"{NestPlannerLogic.Phase(previous).Label} / {previous.EggsHatched} hatched / {previous.YoungRaised} raised");
            UpdateLifeRun(force: true);
            UpdateTacticalBrief();
            SavePlannerState();
            await ShowHotkeyToastAsync("NEST PLAN RESET", true);
            return;
        }

        _clearNestConfirmationPending = true;
        var revision = ++_clearNestConfirmationRevision;
        _nestPlannerUiSignature = string.Empty;
        UpdateNestPlannerControls(force: true);
        await Task.Delay(3000);
        if (!IsLoaded || !_clearNestConfirmationPending || revision != _clearNestConfirmationRevision) return;
        _clearNestConfirmationPending = false;
        _nestPlannerUiSignature = string.Empty;
        UpdateNestPlannerControls(force: true);
    }

    private void StartNewLifeRun(bool logEvent)
    {
        ClearLifeTransitionSession();
        ClearCoreVitals(logEvent: false, updateUi: false);
        ClearManualSighting(logEvent: false, updateUi: true, resetDraft: true, collapse: true);
        _survivalIncidentUiSignature = string.Empty;
        _lifeRunActive = true;
        _lifeRunStartedAt = DateTimeOffset.UtcNow;
        _lifeRunStageIndex = 1;
        _lifeRunHudVisible = true;
        _lifeRunSanctuaryVisited = false;
        _lifeRunPerfectDiet = false;
        _lifeRunNestedIn = false;
        _lifeRunRaisedYoung = false;
        _spawnPlanCoverReady = false;
        _spawnPlanScentChecked = false;
        _spawnPlanWaterFound = false;
        _spawnPlanFoodFound = false;
        _spawnPlanUiSignature = string.Empty;
        _zoneBriefIndex = 0;
        _zoneBriefUiSignature = string.Empty;
        _lifeRunMigrationVisits = 0;
        _lifeRunPatrolVisits = 0;
        _lifeRunMassMigrationVisited = false;
        _lifeRunFertilityStatus = 0;
        _lifeRunSpasmStatus = 0;
        _lifeRunSpeciesClass = 0;
        _dietSpeciesIndex = 0;
        _dietTargetIndex = 0;
        _dietSlot1 = DietCoachLogic.Empty;
        _dietSlot2 = DietCoachLogic.Empty;
        _dietSlot3 = DietCoachLogic.Empty;
        _lifeRunGrowthPercent = 25;
        var sessionGrowthMultiplierIndex = CurrentServerGrowthMultiplierIndex();
        if (sessionGrowthMultiplierIndex >= 0)
        {
            _growthServerMultiplierIndex = sessionGrowthMultiplierIndex;
        }
        _growthPaused = false;
        _growthPlannerUiSignature = string.Empty;
        _elderEntombCount = 0;
        _elderPrimeConfirmed = false;
        _elderConfirmed = false;
        ResetEntombConfirmation();
        ApplyNestPlannerSnapshot(NestPlannerLogic.Normalize(new NestPlannerSnapshot(
            false, 0, false, false, false, false, 0, 2, 0, 0, 0, 1)));
        _clearNestConfirmationPending = false;
        _clearNestConfirmationRevision++;
        _nestPlannerUiSignature = string.Empty;
        _mutationLoadout.Clear();
        _mutationSearchResults = [];
        _mutationSearchResultIndex = 0;
        _mutationBuildFocusIndex = 0;
        _mutationPlannerUiSignature = string.Empty;
        _mutationRemoveConfirmationSlot = 0;
        _mutationRemoveConfirmationRevision++;
        _mutationUnlockProgress.Clear();
        _mutationUnlockSelectedIndex = 0;
        _mutationUnlockUiSignature = string.Empty;
        _mutationUnlockResetConfirmationId = string.Empty;
        _mutationUnlockResetConfirmationRevision++;
        _survivalTimers.RemoveAll(timer => MutationUnlockLogic.Challenges.Any(challenge =>
            !string.IsNullOrWhiteSpace(challenge.TimerLabel)
            && string.Equals(timer.Label, challenge.TimerLabel, StringComparison.OrdinalIgnoreCase)));
        _survivalTimerUiSignature = string.Empty;
        _newLifeRunConfirmationPending = false;
        _newLifeRunConfirmationRevision++;
        _captureStreak = new LifeRunCaptureStreak(0, 0);
        _nestTimerAlertNotifiedMasks.Clear();
        _lifeRunUiSignature = string.Empty;
        if (logEvent) AddTacticalEvent("LIFE", "Life run started", "Manual local tracker · Juvenile");
        UpdateCoreVitals(force: true);
        UpdateSurvivalAssistant(force: true);
        UpdateLifeRun(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
    }

    private async void LifeRunStartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lifeRunActive || _streamerMode) return;
        var growthBridge = CurrentLiveGrowthBridge();
        if (growthBridge.State == LiveGrowthBridgeState.ReadyToStart)
        {
            var speciesBridge = CurrentLiveSpeciesBridge();
            StartNewLifeRun(logEvent: false);
            if (speciesBridge.Available)
            {
                ApplyLiveSpeciesToSavedRun(speciesBridge);
            }
            _lifeRunGrowthPercent = growthBridge.LiveGrowthPercent;
            _lifeRunStageIndex = GrowthPlannerLogic.StageIndex(_lifeRunGrowthPercent);
            CommitGrowthClockChange(
                "Life run started from live snapshot",
                $"Saved growth {_lifeRunGrowthPercent}%" +
                (speciesBridge.Available ? $" · {speciesBridge.LiveSpeciesName}" : string.Empty) +
                " · live Prime remains read-only");
            await ShowHotkeyToastAsync(
                speciesBridge.Available
                    ? $"{speciesBridge.LiveSpeciesName.ToUpperInvariant()} RUN · {_lifeRunGrowthPercent}%"
                    : $"LIFE RUN STARTED @ {_lifeRunGrowthPercent}%",
                true);
            return;
        }
        StartNewLifeRun(logEvent: true);
    }

    private void SpawnPlanTaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || sender is not Button { Tag: string task }) return;

        string label;
        bool completed;
        switch (task)
        {
            case "cover":
                _spawnPlanCoverReady = !_spawnPlanCoverReady;
                label = "Cover and exit";
                completed = _spawnPlanCoverReady;
                break;
            case "scent":
                _spawnPlanScentChecked = !_spawnPlanScentChecked;
                label = "Scent checked";
                completed = _spawnPlanScentChecked;
                break;
            case "water":
                _spawnPlanWaterFound = !_spawnPlanWaterFound;
                label = "Water found";
                completed = _spawnPlanWaterFound;
                break;
            case "food":
                _spawnPlanFoodFound = !_spawnPlanFoodFound;
                label = "Food found";
                completed = _spawnPlanFoodFound;
                break;
            default:
                return;
        }

        _newLifeRunConfirmationPending = false;
        _spawnPlanUiSignature = string.Empty;
        _lifeRunUiSignature = string.Empty;
        AddTacticalEvent(
            "LIFE",
            "Spawn plan updated",
            $"{label} · {(completed ? "confirmed" : "cleared")}");
        UpdateLifeRun(force: true);
        UpdateNextMove(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
    }

    private async void SpawnPlanActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive
            || _streamerMode
            || sender is not Button { Tag: string actionId }
            || string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        if (actionId == "current-first-hour-guide")
        {
            OpenExternalUri(OverlayLinks.FirstHourGuide);
            await ShowHotkeyToastAsync("FIRST-HOUR GUIDE OPEN", true);
            return;
        }

        if (actionId == "spawn-water-scent")
        {
            if (!LiveMapServicesActive)
            {
                OpenExternalUri(OverlayLinks.FirstHourGuide);
                await ShowHotkeyToastAsync("FIRST-HOUR GUIDE OPEN", true);
                return;
            }

            var targetChanged = _trackFinderScentTarget != ScentTargetKind.Water;
            var hadTrack = _soundBearingFirst is not null || _soundBearingSecond is not null;
            _trackFinderScentTarget = ScentTargetKind.Water;
            if (_trackFinderMode != TrackFinderMode.Scent)
            {
                await SetTrackFinderModeAsync(TrackFinderMode.Scent, showToast: false);
            }
            else if (targetChanged)
            {
                await ClearSoundFinderAsync(showToast: false, logEvent: hadTrack);
                _soundFinderUiSignature = string.Empty;
                UpdateSoundFinder(force: true);
            }
            OpenMapToolsAtSection("scent-finder");
            await ShowHotkeyToastAsync("WATER SCENT READY", true);
            return;
        }

        await ExecuteCommandPaletteActionAsync(actionId);
    }

    private void ZoneBriefZoneButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive
            || _streamerMode
            || sender is not Button { Tag: string zoneText }
            || !int.TryParse(zoneText, out var zoneValue))
        {
            return;
        }

        var zone = ZoneBriefLogic.NormalizeZone(zoneValue);
        if (_zoneBriefIndex == (int)zone)
        {
            return;
        }

        _zoneBriefIndex = (int)zone;
        _zoneBriefUiSignature = string.Empty;
        _lifeRunUiSignature = string.Empty;
        AddTacticalEvent(
            "LIFE",
            "Current zone updated",
            $"{ZoneBriefLogic.Label(zone)} · player-reported compass signal");
        UpdateLifeRun(force: true);
        UpdateNextMove(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
    }

    private async void ZoneBriefActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive
            || _streamerMode
            || sender is not Button { Tag: string actionId }
            || string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        if (actionId == "current-zones-guide")
        {
            OpenExternalUri(OverlayLinks.ZonesGuide);
            await ShowHotkeyToastAsync("CURRENT ZONES GUIDE OPEN", true);
            return;
        }

        await ExecuteCommandPaletteActionAsync(actionId);
    }

    private void LifeRunStageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode) return;
        _lifeRunStageIndex = (_lifeRunStageIndex + 1) % _lifeRunStageLabels.Length;
        _lifeRunGrowthPercent = GrowthPlannerLogic.StageAnchor(_lifeRunStageIndex);
        _growthPaused = false;
        _growthPlannerUiSignature = string.Empty;
        _newLifeRunConfirmationPending = false;
        _lifeRunUiSignature = string.Empty;
        AddTacticalEvent(
            "GROWTH",
            "Growth stage updated",
            $"{_lifeRunStageLabels[_lifeRunStageIndex]} / {_lifeRunGrowthPercent}%");
        UpdateLifeRun(force: true);
        UpdateNextMove(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
    }

    private void LifeRunMilestoneButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || sender is not Button { Tag: string milestone }) return;
        string label;
        bool completed;
        switch (milestone)
        {
            case "sanctuary":
                _lifeRunSanctuaryVisited = !_lifeRunSanctuaryVisited;
                label = "Sanctuary";
                completed = _lifeRunSanctuaryVisited;
                break;
            case "diet":
                _lifeRunPerfectDiet = !_lifeRunPerfectDiet;
                label = "Perfect diet";
                completed = _lifeRunPerfectDiet;
                break;
            case "nested":
                _lifeRunNestedIn = !_lifeRunNestedIn;
                label = "Nested in";
                completed = _lifeRunNestedIn;
                break;
            case "raised":
                _lifeRunRaisedYoung = !_lifeRunRaisedYoung;
                label = "Raised young";
                completed = _lifeRunRaisedYoung;
                break;
            case "mass-migration":
                _lifeRunMassMigrationVisited = !_lifeRunMassMigrationVisited;
                label = "Mass migration";
                completed = _lifeRunMassMigrationVisited;
                break;
            default:
                return;
        }
        _newLifeRunConfirmationPending = false;
        _lifeRunUiSignature = string.Empty;
        AddTacticalEvent("LIFE", completed ? "Milestone marked" : "Milestone unmarked", label);
        UpdateLifeRun(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
    }

    private void LifeRunPrimeStateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || sender is not Button { Tag: string stateTag }) return;
        string label;
        int state;
        switch (stateTag)
        {
            case "species":
                _lifeRunSpeciesClass = (_lifeRunSpeciesClass + 1) % 3;
                label = "Species class";
                state = _lifeRunSpeciesClass;
                break;
            case "fertility":
                _lifeRunFertilityStatus = (_lifeRunFertilityStatus + 1) % 3;
                label = "Infertility condition";
                state = _lifeRunFertilityStatus;
                break;
            case "spasm":
                _lifeRunSpasmStatus = (_lifeRunSpasmStatus + 1) % 3;
                label = "Muscle-spasms condition";
                state = _lifeRunSpasmStatus;
                break;
            default:
                return;
        }

        _newLifeRunConfirmationPending = false;
        _lifeRunUiSignature = string.Empty;
        var stateLabel = stateTag == "species"
            ? PrimeSpeciesClassLabel(state)
            : PrimeManualStateLabel(state);
        AddTacticalEvent("PRIME", "Manual condition updated", $"{label} · {stateLabel}");
        UpdateLifeRun(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
    }

    private MutationUnlockChallenge CurrentMutationUnlockChallenge()
    {
        _mutationUnlockSelectedIndex = MutationUnlockLogic.NormalizeSelectedIndex(_mutationUnlockSelectedIndex);
        return MutationUnlockLogic.Challenges[_mutationUnlockSelectedIndex];
    }

    private SurvivalTimer? FindMutationUnlockTimer(MutationUnlockChallenge challenge) =>
        string.IsNullOrWhiteSpace(challenge.TimerLabel)
            ? null
            : _survivalTimers.FirstOrDefault(timer =>
                string.Equals(timer.Label, challenge.TimerLabel, StringComparison.OrdinalIgnoreCase));

    private int StoredMutationUnlockValue(MutationUnlockChallenge challenge) =>
        MutationUnlockLogic.ValueFor(_mutationUnlockProgress, challenge.Id);

    private int EffectiveMutationUnlockValue(
        MutationUnlockChallenge challenge,
        SurvivalTimer? timer,
        DateTimeOffset now)
    {
        var elapsedSeconds = timer is null
            ? 0
            : Math.Max(0, timer.DurationSeconds - GetTimerRemainingSeconds(timer, now));
        return MutationUnlockLogic.EffectiveValue(
            challenge,
            StoredMutationUnlockValue(challenge),
            elapsedSeconds,
            timer?.Completed == true);
    }

    private void SetMutationUnlockValue(MutationUnlockChallenge challenge, int value)
    {
        var normalized = MutationUnlockLogic.SetValue(_mutationUnlockProgress, challenge.Id, value);
        _mutationUnlockProgress.Clear();
        _mutationUnlockProgress.AddRange(normalized);
        _mutationUnlockUiSignature = string.Empty;
        _mutationPlannerUiSignature = string.Empty;
        _lifeRunUiSignature = string.Empty;
    }

    private bool SyncCompletedMutationUnlockTimer(DateTimeOffset now)
    {
        var changed = false;
        foreach (var challenge in MutationUnlockLogic.Challenges.Where(item => item.Mode == MutationUnlockMode.Timer))
        {
            var timer = FindMutationUnlockTimer(challenge);
            if (timer?.Completed != true || StoredMutationUnlockValue(challenge) >= challenge.Target) continue;
            SetMutationUnlockValue(challenge, challenge.Target);
            AddTacticalEvent("MUTATION", "Unlock challenge complete", challenge.Label, warning: true);
            changed = true;
        }
        if (changed)
        {
            UpdateTacticalBrief();
            SavePlannerState();
        }
        return changed;
    }

    private void UpdateMutationUnlockTracker(bool force = false)
    {
        if (MutationUnlockNameText is null
            || MutationUnlockProgressTransform is null
            || MutationUnlockActionButton is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        SyncCompletedMutationUnlockTimer(now);
        var challenge = CurrentMutationUnlockChallenge();
        var timer = FindMutationUnlockTimer(challenge);
        var value = EffectiveMutationUnlockValue(challenge, timer, now);
        var complete = MutationUnlockLogic.IsComplete(challenge, value);
        var remainingSeconds = timer is null ? 0 : GetTimerRemainingSeconds(timer, now);
        var signature = string.Join('|', new object[]
        {
            _lifeRunActive,
            _streamerMode,
            _mutationUnlockSelectedIndex,
            challenge.Id,
            value,
            timer?.Id ?? string.Empty,
            timer?.IsPaused ?? false,
            timer?.Completed ?? false,
            Math.Ceiling(remainingSeconds),
            _mutationUnlockResetConfirmationId,
            string.Join(';', _mutationUnlockProgress.Select(item => $"{item.ChallengeId}:{item.Value}"))
        });
        if (!force && string.Equals(signature, _mutationUnlockUiSignature, StringComparison.Ordinal)) return;
        _mutationUnlockUiSignature = signature;

        var available = _lifeRunActive && !_streamerMode;
        MutationUnlockPreviousButton.IsEnabled = available;
        MutationUnlockNextButton.IsEnabled = available;
        MutationUnlockActionButton.IsEnabled = available && !complete;
        MutationUnlockMinusButton.IsEnabled = available
                                                && challenge.Mode != MutationUnlockMode.Timer
                                                && value > 0;
        MutationUnlockResetButton.IsEnabled = available && (value > 0 || timer is not null);

        MutationUnlockIndexText.Text = $"{_mutationUnlockSelectedIndex + 1} / {MutationUnlockLogic.Challenges.Length}";
        MutationUnlockNameText.Text = challenge.Label;
        MutationUnlockGoalText.Text = challenge.Goal;
        MutationUnlockProgressText.Text = MutationUnlockLogic.ProgressLabel(challenge, value);
        MutationUnlockProgressText.Foreground = complete
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("WarningBrush");
        MutationUnlockProgressTransform.ScaleX = challenge.Target > 0
            ? Math.Clamp(value / (double)challenge.Target, 0, 1)
            : 0;
        MutationUnlockActionText.Text = challenge.NextAction +
                                        (timer is { Completed: false }
                                            ? timer.IsPaused
                                                ? $" Timer paused with {FormatTimerRemaining(remainingSeconds)} remaining."
                                                : $" Timer running · {FormatTimerRemaining(remainingSeconds)} remaining."
                                            : complete ? " Challenge marked complete." : string.Empty);

        MutationUnlockMinusButton.Content = challenge.Mode switch
        {
            MutationUnlockMode.Toggle => "UNDO",
            MutationUnlockMode.Timer => "—",
            _ => $"-{challenge.Step:N0}"
        };
        MutationUnlockActionButton.Content = challenge.Mode switch
        {
            MutationUnlockMode.Toggle => complete ? "DONE" : "MARK DONE",
            MutationUnlockMode.Timer when complete => "DONE",
            MutationUnlockMode.Timer when timer is null => $"START {challenge.TimerMinutes}M",
            MutationUnlockMode.Timer when timer.IsPaused => "RESUME",
            MutationUnlockMode.Timer => "PAUSE",
            _ => complete ? "DONE" : $"ADD {challenge.Step:N0}"
        };
        MutationUnlockResetButton.Content = string.Equals(
            _mutationUnlockResetConfirmationId,
            challenge.Id,
            StringComparison.OrdinalIgnoreCase)
            ? "SURE?"
            : "RESET";
        MutationUnlockActionButton.ToolTip = challenge.Mode == MutationUnlockMode.Timer
            ? "Start, pause, or resume the condition timer; reset it if the in-game condition breaks"
            : challenge.NextAction;
        SetToggleButtonState(
            MutationUnlockActionButton,
            complete || timer is { Completed: false, IsPaused: false });
    }

    private void SelectMutationUnlockChallenge(int direction)
    {
        if (!_lifeRunActive || _streamerMode) return;
        _mutationUnlockSelectedIndex = (_mutationUnlockSelectedIndex + direction + MutationUnlockLogic.Challenges.Length)
                                       % MutationUnlockLogic.Challenges.Length;
        _mutationUnlockResetConfirmationId = string.Empty;
        _mutationUnlockResetConfirmationRevision++;
        _mutationUnlockUiSignature = string.Empty;
        UpdateMutationUnlockTracker(force: true);
        SavePlannerState();
    }

    private void MutationUnlockPreviousButton_Click(object sender, RoutedEventArgs e) =>
        SelectMutationUnlockChallenge(-1);

    private void MutationUnlockNextButton_Click(object sender, RoutedEventArgs e) =>
        SelectMutationUnlockChallenge(1);

    private void MutationUnlockMinusButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode) return;
        var challenge = CurrentMutationUnlockChallenge();
        if (challenge.Mode == MutationUnlockMode.Timer) return;
        var previous = StoredMutationUnlockValue(challenge);
        var next = MutationUnlockLogic.Adjust(challenge, previous, -1);
        if (next == previous) return;
        SetMutationUnlockValue(challenge, next);
        _mutationUnlockResetConfirmationId = string.Empty;
        _mutationUnlockResetConfirmationRevision++;
        AddTacticalEvent("MUTATION", "Unlock progress corrected", $"{challenge.Label} · {MutationUnlockLogic.ProgressLabel(challenge, next)}");
        UpdateLifeRun(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
    }

    private async void MutationUnlockActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode) return;
        var challenge = CurrentMutationUnlockChallenge();
        var now = DateTimeOffset.UtcNow;
        var timer = FindMutationUnlockTimer(challenge);
        var previous = EffectiveMutationUnlockValue(challenge, timer, now);
        if (MutationUnlockLogic.IsComplete(challenge, previous)) return;

        _mutationUnlockResetConfirmationId = string.Empty;
        _mutationUnlockResetConfirmationRevision++;
        if (challenge.Mode == MutationUnlockMode.Timer)
        {
            if (timer is null)
            {
                if (!StartSurvivalTimer(challenge.TimerLabel, challenge.TimerMinutes))
                {
                    await ShowHotkeyToastAsync("REMOVE A TIMER FIRST · FOUR ACTIVE", false);
                    return;
                }
            }
            else
            {
                ToggleSurvivalTimerState(timer);
            }
            _mutationUnlockUiSignature = string.Empty;
            UpdateMutationUnlockTracker(force: true);
            return;
        }

        var next = challenge.Mode == MutationUnlockMode.Toggle
            ? challenge.Target
            : MutationUnlockLogic.Adjust(challenge, previous, 1);
        SetMutationUnlockValue(challenge, next);
        var completed = MutationUnlockLogic.IsComplete(challenge, next);
        AddTacticalEvent(
            "MUTATION",
            completed ? "Unlock challenge complete" : "Unlock progress recorded",
            $"{challenge.Label} · {MutationUnlockLogic.ProgressLabel(challenge, next)}",
            warning: completed);
        UpdateLifeRun(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
    }

    private async void MutationUnlockResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode) return;
        var challenge = CurrentMutationUnlockChallenge();
        var timer = FindMutationUnlockTimer(challenge);
        if (StoredMutationUnlockValue(challenge) <= 0 && timer is null) return;
        if (string.Equals(_mutationUnlockResetConfirmationId, challenge.Id, StringComparison.OrdinalIgnoreCase))
        {
            if (timer is not null)
            {
                _survivalTimers.Remove(timer);
                _survivalTimerUiSignature = string.Empty;
                UpdateSurvivalTimers(force: true);
            }
            SetMutationUnlockValue(challenge, 0);
            _mutationUnlockResetConfirmationId = string.Empty;
            _mutationUnlockResetConfirmationRevision++;
            AddTacticalEvent("MUTATION", "Unlock challenge reset", challenge.Label);
            UpdateLifeRun(force: true);
            UpdateTacticalBrief();
            SavePlannerState();
            return;
        }

        _mutationUnlockResetConfirmationId = challenge.Id;
        var revision = ++_mutationUnlockResetConfirmationRevision;
        _mutationUnlockUiSignature = string.Empty;
        UpdateMutationUnlockTracker(force: true);
        await Task.Delay(3000);
        if (!IsLoaded || revision != _mutationUnlockResetConfirmationRevision) return;
        _mutationUnlockResetConfirmationId = string.Empty;
        _mutationUnlockUiSignature = string.Empty;
        UpdateMutationUnlockTracker(force: true);
    }

    private MutationCatalogEntry? CurrentMutationSearchResult() =>
        _mutationSearchResults.Count == 0
            ? null
            : _mutationSearchResults[Math.Clamp(_mutationSearchResultIndex, 0, _mutationSearchResults.Count - 1)];

    private MutationBuildAnalysis CurrentMutationBuildAnalysis() =>
        MutationBuildLogic.Analyze(
            _mutationBuildFocusIndex,
            _mutationLoadout,
            DietCoachLogic.SpeciesClassLabel(_dietSpeciesIndex));

    private void UpdateMutationBuildControls()
    {
        if (MutationBuildFocusText is null
            || MutationBuildCoverageText is null
            || MutationBuildRecommendationButton is null)
        {
            return;
        }

        var analysis = CurrentMutationBuildAnalysis();
        var available = _lifeRunActive && !_streamerMode;

        if (!available)
        {
            MutationBuildFocusText.Text = _streamerMode ? "FOCUS · HIDDEN" : "FOCUS · WAITING";
            MutationBuildFitText.Text = "FIT 0%";
            MutationBuildFitText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            MutationBuildCoverageTransform.ScaleX = 0;
            MutationBuildCoverageText.Text = "SUSTAIN 0 · FIGHT 0 · MOVE 0 · ROLE 0";
            MutationBuildInsightText.Text = _streamerMode
                ? "BUILD ANALYSIS HIDDEN IN STREAMER MODE"
                : "START LIFE RUN TO ANALYZE A BUILD";
            MutationBuildInsightText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            MutationBuildRecommendationText.Text = "GUIDE FIT · WAITING";
            MutationBuildRecommendationMetaText.Text = _streamerMode
                ? "PRIVATE LIFE-RUN DATA REDACTED"
                : "CHOOSE A SPECIES AND START A LIFE RUN";
            MutationBuildRecommendationReasonText.Text = "Restriction-aware guidance appears here.";
            MutationBuildPreviousButton.IsEnabled = false;
            MutationBuildNextButton.IsEnabled = false;
            MutationBuildRecommendationButton.IsEnabled = false;
            MutationBuildRecommendationButton.Content = "NO PICK";
            MutationBuildRecommendationButton.ToolTip = "Start a visible Life Run before requesting a recommendation";
            return;
        }

        MutationBuildFocusText.Text = $"FOCUS · {analysis.Focus.Label}";
        MutationBuildFitText.Text = $"FIT {analysis.FitPercent}%";
        MutationBuildFitText.Foreground = analysis.FitPercent >= 67
            ? (Brush)FindResource("AccentBrush")
            : analysis.FitPercent > 0
                ? (Brush)FindResource("PrimaryTextBrush")
                : (Brush)FindResource("SecondaryTextBrush");
        MutationBuildCoverageTransform.ScaleX = analysis.FitPercent / 100d;
        MutationBuildCoverageText.Text =
            $"SUSTAIN {analysis.SustainPercent} · FIGHT {analysis.FightPercent} · " +
            $"MOVE {analysis.MovePercent} · ROLE {analysis.RolePercent}";
        MutationBuildInsightText.Text = analysis.Insight;
        MutationBuildInsightText.Foreground = analysis.SynergyLabel.Length > 0
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("WarningBrush");

        MutationBuildRecommendationText.Text = analysis.HasRecommendation
            ? $"GUIDE FIT · {analysis.RecommendationName}"
            : "GUIDE FIT · WAITING";
        MutationBuildRecommendationMetaText.Text = analysis.HasRecommendation
            ? _dietSpeciesIndex > 0
                ? analysis.RecommendationMeta
                : $"SPECIES UNSET · {analysis.RecommendationMeta}"
            : "NO LEGAL GUIDE FIT";
        MutationBuildRecommendationReasonText.Text = analysis.HasRecommendation
            ? analysis.RecommendationReason
            : "Restriction-aware guidance appears here.";
        MutationBuildPreviousButton.IsEnabled = true;
        MutationBuildNextButton.IsEnabled = true;
        MutationBuildRecommendationButton.IsEnabled = analysis.HasRecommendation;
        MutationBuildRecommendationButton.Content = analysis.HasRecommendation
            ? $"FIND S{analysis.RecommendationSlot} PICK"
            : "NO PICK";
        var recommended = analysis.HasRecommendation
            ? MutationPlannerLogic.FindById(analysis.RecommendationId)
            : null;
        MutationBuildRecommendationButton.ToolTip = recommended is null
            ? "Add or correct the current Life Run before requesting a recommendation"
            : $"Open {recommended.Name} in mutation search · {recommended.Effect} · verify in game";
    }

    private void RefreshMutationSearch(bool resetIndex)
    {
        if (MutationSearchInputBox is null) return;
        _mutationSearchResults = MutationPlannerLogic.Search(MutationSearchInputBox.Text, 6);
        _mutationSearchResultIndex = resetIndex
            ? 0
            : _mutationSearchResults.Count == 0
                ? 0
                : Math.Clamp(_mutationSearchResultIndex, 0, _mutationSearchResults.Count - 1);
        _mutationPlannerUiSignature = string.Empty;
        UpdateMutationPlanner(force: true);
    }

    private void UpdateMutationPlanner(bool force = false)
    {
        if (MutationPlannerStatusText is null
            || MutationSearchInputBox is null
            || CopyMutationLoadoutButton is null
            || MutationLoadoutListPanel is null
            || MutationBuildRecommendationButton is null
            || LifeRunHudMutationText is null)
        {
            return;
        }

        var query = MutationSearchInputBox?.Text?.Trim() ?? string.Empty;
        var signature = string.Join('|', new[]
        {
            _lifeRunActive ? "active" : "inactive",
            _streamerMode ? "streamer" : "normal",
            _dietSpeciesIndex.ToString(),
            _mutationBuildFocusIndex.ToString(),
            query,
            _mutationSearchResultIndex.ToString(),
            string.Join(';', _mutationSearchResults.Select(entry => entry.Id)),
            string.Join(';', _mutationLoadout.Select(item => $"{item.Slot}:{item.MutationId}:{item.Status}")),
            string.Join(';', _mutationUnlockProgress.Select(item => $"{item.ChallengeId}:{item.Value}")),
            _mutationUnlockSelectedIndex.ToString(),
            _mutationRemoveConfirmationSlot.ToString()
        });
        if (!force && string.Equals(signature, _mutationPlannerUiSignature, StringComparison.Ordinal)) return;
        _mutationPlannerUiSignature = signature;

        var hasUnlockProgress = _mutationUnlockProgress.Count > 0
                                || MutationUnlockLogic.Challenges.Any(challenge =>
                                    FindMutationUnlockTimer(challenge) is not null);
        LifeRunHudMutationText.Visibility = _lifeRunActive
                                            && !_streamerMode
                                            && (_mutationLoadout.Count > 0 || hasUnlockProgress)
            ? Visibility.Visible
            : Visibility.Collapsed;
        MutationSearchInputBox!.IsEnabled = _lifeRunActive && !_streamerMode;
        CopyMutationLoadoutButton.IsEnabled = _lifeRunActive && !_streamerMode && _mutationLoadout.Count > 0;
        UpdateMutationBuildControls();

        if (!_lifeRunActive || _streamerMode)
        {
            MutationPlannerStatusText.Text = _streamerMode
                ? "Mutation loadout hidden in streamer mode"
                : "Start Life Run to plan this dinosaur's mutations";
            MutationSearchResultBorder.Visibility = Visibility.Collapsed;
            MutationLoadoutListPanel.Children.Clear();
            return;
        }

        var equipped = MutationPlannerLogic.EquippedCount(_mutationLoadout);
        var nextSlot = MutationPlannerLogic.NextFreeSlot(_mutationLoadout);
        MutationPlannerStatusText.Text = _mutationLoadout.Count == 0
            ? $"No mutations planned · search {MutationPlannerLogic.Catalog.Length} current guide entries"
            : nextSlot > 0
                ? $"{_mutationLoadout.Count} saved · {equipped} equipped · next S{nextSlot}"
                : $"{_mutationLoadout.Count}/{MutationPlannerLogic.MaxLoadoutSize} saved · loadout full";
        MutationPlannerStatusText.ToolTip =
            $"Manual current-life loadout · {MutationPlannerLogic.Catalog.Length} non-experimental guide entries" +
            $" · catalog snapshot {MutationPlannerLogic.CatalogDate}";
        var unlockCompleted = MutationUnlockLogic.CompletedCount(_mutationUnlockProgress);
        LifeRunHudMutationText.Text = _mutationLoadout.Count > 0
            ? $"MUT {equipped}/{_mutationLoadout.Count} · UNLOCK {unlockCompleted}/{MutationUnlockLogic.Challenges.Length}"
            : $"UNLOCKS · {unlockCompleted}/{MutationUnlockLogic.Challenges.Length} DONE";
        var mutationBuild = CurrentMutationBuildAnalysis();
        LifeRunHudMutationText.ToolTip =
            $"{MutationBuildLogic.CompactSummary(mutationBuild)} · {mutationBuild.Insight} · " +
            $"next guide fit {mutationBuild.RecommendationName.ToLowerInvariant()} · verify availability in game";

        var selected = CurrentMutationSearchResult();
        var showSearch = query.Length >= 2;
        MutationSearchResultBorder.Visibility = showSearch ? Visibility.Visible : Visibility.Collapsed;
        if (showSearch && selected is null)
        {
            MutationMatchNameText.Text = "NO MATCH";
            MutationMatchIndexText.Text = "0/0";
            MutationMatchMetaText.Text = "TRY A NAME, EFFECT, RESTRICTION, OR PLAY STYLE";
            MutationMatchEffectText.Text = "Examples: digestion, aquatic, nesting, combat, stamina, night.";
            MutationMatchUnlockText.Visibility = Visibility.Collapsed;
            AddMutationButton.IsEnabled = false;
            MutationPreviousMatchButton.IsEnabled = false;
            MutationNextMatchButton.IsEnabled = false;
        }
        else if (selected is not null)
        {
            var selectedIndex = Math.Clamp(_mutationSearchResultIndex, 0, _mutationSearchResults.Count - 1);
            MutationMatchNameText.Text = selected.Name;
            MutationMatchIndexText.Text = $"{selectedIndex + 1}/{_mutationSearchResults.Count}";
            MutationMatchMetaText.Text = string.IsNullOrWhiteSpace(selected.Restrictions)
                ? selected.Group.ToUpperInvariant()
                : $"{selected.Group.ToUpperInvariant()} · {selected.Restrictions.ToUpperInvariant()}";
            MutationMatchEffectText.Text = selected.Effect;
            MutationMatchUnlockText.Text = string.IsNullOrWhiteSpace(selected.Unlock)
                ? string.Empty
                : $"UNLOCK · {selected.Unlock}";
            MutationMatchUnlockText.Visibility = string.IsNullOrWhiteSpace(selected.Unlock)
                ? Visibility.Collapsed
                : Visibility.Visible;
            var alreadyPlanned = _mutationLoadout.Any(item =>
                string.Equals(item.MutationId, selected.Id, StringComparison.OrdinalIgnoreCase));
            var eligibleSlot = MutationPlannerLogic.NextFreeSlotForMutation(_mutationLoadout, selected);
            AddMutationButton.IsEnabled = !alreadyPlanned && eligibleSlot > 0;
            AddMutationButton.Content = alreadyPlanned ? "SAVED" : eligibleSlot > 0 ? $"ADD S{eligibleSlot}" : "NO SLOT";
            AddMutationButton.ToolTip = eligibleSlot > 0
                ? $"Add this mutation to legal slot S{eligibleSlot}"
                : $"No legal free slot · {MutationPlannerLogic.AllowedSlotLabel(selected)} required";
            MutationPreviousMatchButton.IsEnabled = _mutationSearchResults.Count > 1;
            MutationNextMatchButton.IsEnabled = _mutationSearchResults.Count > 1;
        }

        MutationLoadoutListPanel.Children.Clear();
        foreach (var item in _mutationLoadout.OrderBy(item => item.Slot))
        {
            var mutation = MutationPlannerLogic.FindById(item.MutationId);
            if (mutation is null) continue;

            var row = new Border
            {
                Margin = new Thickness(0, 0, 0, 3),
                Padding = new Thickness(7, 5, 5, 5),
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.FromArgb(0x70, 0x21, 0x2C, 0x38)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x7E, 0x89, 0x95)),
                BorderThickness = new Thickness(1)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

            var labelStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labelStack.Children.Add(new TextBlock
            {
                Text = mutation.Name,
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("PrimaryTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = $"{mutation.Name} · {mutation.Effect}"
            });
            labelStack.Children.Add(new TextBlock
            {
                Text = $"S{item.Slot} · {mutation.Group.ToUpperInvariant()}",
                FontSize = 7,
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(labelStack, 0);
            grid.Children.Add(labelStack);

            var statusButton = new Button
            {
                Style = (Style)FindResource("DrawerCompactButton"),
                Margin = new Thickness(3, 0, 0, 0),
                Tag = item.Slot,
                Content = item.Status switch { 1 => "ACTIVE", 2 => "CARRY", _ => "PLAN" },
                ToolTip = "Cycle Planned, Active, or Carried",
                FontSize = 7
            };
            statusButton.Click += MutationLoadoutStatusButton_Click;
            SetToggleButtonState(statusButton, item.Status == 1);
            if (item.Status == 2)
            {
                statusButton.Background = new SolidColorBrush(Color.FromRgb(167, 139, 250));
                statusButton.Foreground = new SolidColorBrush(Color.FromRgb(20, 12, 38));
                statusButton.BorderBrush = new SolidColorBrush(Color.FromRgb(196, 181, 253));
            }
            Grid.SetColumn(statusButton, 1);
            grid.Children.Add(statusButton);

            var removeButton = new Button
            {
                Style = (Style)FindResource("DrawerCompactButton"),
                Margin = new Thickness(3, 0, 0, 0),
                Tag = item.Slot,
                Content = _mutationRemoveConfirmationSlot == item.Slot ? "!" : "×",
                ToolTip = _mutationRemoveConfirmationSlot == item.Slot
                    ? "Select again within three seconds to remove this mutation"
                    : "Remove this planned mutation after confirmation",
                FontSize = 9
            };
            removeButton.Click += MutationLoadoutRemoveButton_Click;
            if (_mutationRemoveConfirmationSlot == item.Slot)
            {
                removeButton.Foreground = (Brush)FindResource("WarningBrush");
                removeButton.BorderBrush = (Brush)FindResource("WarningBrush");
            }
            Grid.SetColumn(removeButton, 2);
            grid.Children.Add(removeButton);

            row.Child = grid;
            MutationLoadoutListPanel.Children.Add(row);
        }
    }

    private void ResetMutationRemoveConfirmation()
    {
        _mutationRemoveConfirmationSlot = 0;
        _mutationRemoveConfirmationRevision++;
    }

    private bool AddSelectedMutation()
    {
        if (!_lifeRunActive || _streamerMode || CurrentMutationSearchResult() is not { } selected) return false;
        if (_mutationLoadout.Any(item => string.Equals(item.MutationId, selected.Id, StringComparison.OrdinalIgnoreCase)))
        {
            _ = ShowHotkeyToastAsync("MUTATION ALREADY SAVED", false);
            return false;
        }
        var slot = MutationPlannerLogic.NextFreeSlotForMutation(_mutationLoadout, selected);
        if (slot <= 0)
        {
            _ = ShowHotkeyToastAsync($"NO LEGAL SLOT · {MutationPlannerLogic.AllowedSlotLabel(selected)}", false);
            return false;
        }

        _mutationLoadout.Add(new MutationLoadoutItem(slot, selected.Id, 0));
        ResetMutationRemoveConfirmation();
        _mutationPlannerUiSignature = string.Empty;
        _lifeRunUiSignature = string.Empty;
        AddTacticalEvent("MUTATION", "Mutation planned", $"S{slot} · {selected.Name}");
        UpdateLifeRun(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
        _ = ShowHotkeyToastAsync($"MUTATION SAVED · S{slot}", true);
        return true;
    }

    private void MutationSearchInputBox_TextChanged(object sender, TextChangedEventArgs e) =>
        RefreshMutationSearch(resetIndex: true);

    private void MutationBuildFocusButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive
            || _streamerMode
            || sender is not Button { Tag: string rawDirection }
            || !int.TryParse(rawDirection, out var direction)
            || Math.Abs(direction) != 1)
        {
            return;
        }
        _mutationBuildFocusIndex = MutationBuildLogic.CycleFocusIndex(_mutationBuildFocusIndex, direction);
        _mutationPlannerUiSignature = string.Empty;
        _lifeRunUiSignature = string.Empty;
        AddTacticalEvent("MUTATION", "Build focus changed", MutationBuildLogic.Focuses[_mutationBuildFocusIndex].Label);
        UpdateLifeRun(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
    }

    private void MutationBuildRecommendationButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode) return;
        var analysis = CurrentMutationBuildAnalysis();
        var recommendation = analysis.HasRecommendation
            ? MutationPlannerLogic.FindById(analysis.RecommendationId)
            : null;
        if (recommendation is null) return;
        MutationSearchInputBox.Text = recommendation.Name;
        MutationSearchInputBox.Focus();
        MutationSearchInputBox.SelectAll();
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => MutationSearchInputBox.BringIntoView()));
    }

    private void MutationSearchInputBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.IsKeyboardFocusWithin) return;
        e.Handled = true;
        textBox.Focus();
        textBox.SelectAll();
    }

    private void MutationSearchInputBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox) textBox.SelectAll();
    }

    private void MutationSearchInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (_mutationSearchResults.Count == 0) return;
        if (e.Key == Key.Down)
        {
            _mutationSearchResultIndex = (_mutationSearchResultIndex + 1) % _mutationSearchResults.Count;
            _mutationPlannerUiSignature = string.Empty;
            UpdateMutationPlanner(force: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            _mutationSearchResultIndex = (_mutationSearchResultIndex - 1 + _mutationSearchResults.Count) % _mutationSearchResults.Count;
            _mutationPlannerUiSignature = string.Empty;
            UpdateMutationPlanner(force: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            AddSelectedMutation();
            e.Handled = true;
        }
    }

    private void MutationPreviousMatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mutationSearchResults.Count < 2) return;
        _mutationSearchResultIndex = (_mutationSearchResultIndex - 1 + _mutationSearchResults.Count) % _mutationSearchResults.Count;
        _mutationPlannerUiSignature = string.Empty;
        UpdateMutationPlanner(force: true);
    }

    private void MutationNextMatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mutationSearchResults.Count < 2) return;
        _mutationSearchResultIndex = (_mutationSearchResultIndex + 1) % _mutationSearchResults.Count;
        _mutationPlannerUiSignature = string.Empty;
        UpdateMutationPlanner(force: true);
    }

    private void AddMutationButton_Click(object sender, RoutedEventArgs e) => AddSelectedMutation();

    private void MutationLoadoutStatusButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || sender is not Button { Tag: int slot }) return;
        var index = _mutationLoadout.FindIndex(item => item.Slot == slot);
        if (index < 0) return;
        var current = _mutationLoadout[index];
        var updated = current with { Status = (current.Status + 1) % 3 };
        _mutationLoadout[index] = updated;
        ResetMutationRemoveConfirmation();
        _mutationPlannerUiSignature = string.Empty;
        _lifeRunUiSignature = string.Empty;
        var mutation = MutationPlannerLogic.FindById(updated.MutationId);
        AddTacticalEvent("MUTATION", "Mutation state updated",
            $"S{slot} · {mutation?.Name ?? "Unknown"} · {MutationPlannerLogic.StatusLabel(updated.Status)}");
        UpdateLifeRun(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
    }

    private async void MutationLoadoutRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || sender is not Button { Tag: int slot }) return;
        var index = _mutationLoadout.FindIndex(item => item.Slot == slot);
        if (index < 0) return;
        if (_mutationRemoveConfirmationSlot == slot)
        {
            var removed = _mutationLoadout[index];
            _mutationLoadout.RemoveAt(index);
            ResetMutationRemoveConfirmation();
            _mutationPlannerUiSignature = string.Empty;
            _lifeRunUiSignature = string.Empty;
            var mutation = MutationPlannerLogic.FindById(removed.MutationId);
            AddTacticalEvent("MUTATION", "Mutation removed", $"S{slot} · {mutation?.Name ?? "Unknown"}");
            UpdateLifeRun(force: true);
            UpdateTacticalBrief();
            SavePlannerState();
            await ShowHotkeyToastAsync("MUTATION REMOVED", true);
            return;
        }

        _mutationRemoveConfirmationSlot = slot;
        var revision = ++_mutationRemoveConfirmationRevision;
        _mutationPlannerUiSignature = string.Empty;
        UpdateMutationPlanner(force: true);
        await Task.Delay(3000);
        if (!IsLoaded || _mutationRemoveConfirmationSlot != slot || revision != _mutationRemoveConfirmationRevision) return;
        _mutationRemoveConfirmationSlot = 0;
        _mutationPlannerUiSignature = string.Empty;
        UpdateMutationPlanner(force: true);
    }

    private async void CopyMutationLoadoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || _mutationLoadout.Count == 0) return;
        var loadout = string.Join(" · ", _mutationLoadout.OrderBy(item => item.Slot).Select(item =>
        {
            var mutation = MutationPlannerLogic.FindById(item.MutationId);
            return $"S{item.Slot} {mutation?.Name ?? "Unknown"} [{MutationPlannerLogic.StatusLabel(item.Status).ToLowerInvariant()}]";
        }));
        try
        {
            var build = CurrentMutationBuildAnalysis();
            Clipboard.SetText(
                $"Isley mutation loadout · {loadout} · {MutationBuildLogic.CompactSummary(build)} · " +
                $"guide fit {build.RecommendationName.ToLowerInvariant()} · manual plan · catalog {MutationPlannerLogic.CatalogDate}");
            await ShowHotkeyToastAsync("MUTATION LOADOUT COPIED", true);
        }
        catch
        {
            await ShowHotkeyToastAsync("COPY UNAVAILABLE", false);
        }
    }

    private void LifeRunCounterButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode || sender is not Button { Tag: string counterTag }) return;
        var parts = counterTag.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var delta) || Math.Abs(delta) != 1) return;
        string label;
        int value;
        switch (parts[0])
        {
            case "migration":
                _lifeRunMigrationVisits = Math.Clamp(_lifeRunMigrationVisits + delta, 0, 99);
                label = "Migration visits";
                value = _lifeRunMigrationVisits;
                break;
            case "patrol":
                _lifeRunPatrolVisits = Math.Clamp(_lifeRunPatrolVisits + delta, 0, 99);
                label = "Patrol visits";
                value = _lifeRunPatrolVisits;
                break;
            default:
                return;
        }
        _newLifeRunConfirmationPending = false;
        _lifeRunUiSignature = string.Empty;
        AddTacticalEvent("LIFE", "Zone count updated", $"{label} · {value}");
        UpdateLifeRun(force: true);
        UpdateTacticalBrief();
        SavePlannerState();
    }

    private void LifeRunHudButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode) return;
        _lifeRunHudVisible = !_lifeRunHudVisible;
        _newLifeRunConfirmationPending = false;
        _lifeRunUiSignature = string.Empty;
        UpdateLifeRun(force: true);
        SavePlannerState();
    }

    private async void CopyLifeRunButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode) return;
        try
        {
            Clipboard.SetText(BuildLifeRunSummary(compact: false));
            await ShowHotkeyToastAsync("LIFE RUN COPIED", true);
        }
        catch
        {
            await ShowHotkeyToastAsync("COPY UNAVAILABLE", false);
        }
    }

    private async void NewLifeRunButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive || _streamerMode) return;
        if (_newLifeRunConfirmationPending)
        {
            ClearSurvivalIncident(logEvent: false);
            StartNewLifeRun(logEvent: true);
            await ShowHotkeyToastAsync("NEW LIFE STARTED", true);
            return;
        }

        _newLifeRunConfirmationPending = true;
        var revision = ++_newLifeRunConfirmationRevision;
        _lifeRunUiSignature = string.Empty;
        UpdateLifeRun(force: true);
        await Task.Delay(3000);
        if (!IsLoaded || !_newLifeRunConfirmationPending || revision != _newLifeRunConfirmationRevision) return;
        _newLifeRunConfirmationPending = false;
        _lifeRunUiSignature = string.Empty;
        UpdateLifeRun(force: true);
    }

    private void GuideSearchInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (GuideResultsPanel is not null)
        {
            UpdateFieldGuide(force: true);
        }
    }

    private void GuideSearchInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !string.IsNullOrEmpty(GuideSearchInputBox.Text))
        {
            e.Handled = true;
            GuideSearchInputBox.Clear();
            return;
        }

        if (e.Key == Key.Enter && _guideSearchResults.Count > 0)
        {
            e.Handled = true;
            var speciesChanged = !string.Equals(
                _guideSelectedSpeciesId,
                _guideSearchResults[0].Id,
                StringComparison.OrdinalIgnoreCase);
            _guideSelectedSpeciesId = _guideSearchResults[0].Id;
            if (speciesChanged)
            {
                ApplyAimCalibrationForSelection(useDefaultsWhenMissing: true, force: false);
            }
            _guideUiSignature = string.Empty;
            UpdateFieldGuide(force: true);
            SavePlannerState();
        }
    }

    private void GuideFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string filter })
        {
            return;
        }

        _guideFilterId = FieldGuideLogic.NormalizeDietFilter(filter);
        _guideUiSignature = string.Empty;
        UpdateFieldGuide(force: true);
    }

    private void GuideSpeciesButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string speciesId }
            || FieldGuideLogic.Find(speciesId) is null)
        {
            return;
        }

        var speciesChanged = !string.Equals(
            _guideSelectedSpeciesId,
            speciesId,
            StringComparison.OrdinalIgnoreCase);
        _guideSelectedSpeciesId = speciesId;
        if (speciesChanged)
        {
            ApplyAimCalibrationForSelection(useDefaultsWhenMissing: true, force: false);
        }
        _guideUiSignature = string.Empty;
        UpdateFieldGuide(force: true);
        SavePlannerState();
    }

    private void GuideFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_guideFavoriteSpeciesIds.RemoveAll(id => string.Equals(
                id, _guideSelectedSpeciesId, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            _guideFavoriteSpeciesIds.Insert(0, _guideSelectedSpeciesId);
            if (_guideFavoriteSpeciesIds.Count > 12)
            {
                _guideFavoriteSpeciesIds.RemoveRange(12, _guideFavoriteSpeciesIds.Count - 12);
            }
        }

        _guideUiSignature = string.Empty;
        UpdateFieldGuide(force: true);
        SavePlannerState();
    }

    private async void GuideDietCoachButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_lifeRunActive)
        {
            OpenMapToolsAtSection("life-run");
            await ShowHotkeyToastAsync("START A LIFE RUN TO SET SPECIES", false);
            return;
        }

        _dietSpeciesIndex = FieldGuideLogic.DietSpeciesIndex(_guideSelectedSpeciesId);
        _lifeRunUiSignature = string.Empty;
        UpdateLifeRun(force: true);
        SavePlannerState();
        OpenMapToolsAtSection("diet-coach");
        await ShowHotkeyToastAsync("DIET SPECIES UPDATED", true);
    }

    private async void GuideCombatMutationsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            await ShowHotkeyToastAsync("COMBAT PLAN HIDDEN IN STREAMER MODE", false);
            return;
        }
        if (!_lifeRunActive)
        {
            OpenMapToolsAtSection("life-run");
            await ShowHotkeyToastAsync("START A LIFE RUN TO PLAN MUTATIONS", false);
            return;
        }

        MutationSearchInputBox.Text = CombatGuideLogic.MutationSearchQuery(_guideSelectedSpeciesId);
        RefreshMutationSearch(resetIndex: true);
        OpenMapToolsAtSection("mutation-planner");
        await ShowHotkeyToastAsync("COMBAT MUTATIONS READY", true);
    }

    private async void GuideCombatTriageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            await ShowHotkeyToastAsync("DAMAGE TRIAGE HIDDEN IN STREAMER MODE", false);
            return;
        }
        OpenMapToolsAtSection("survival-assistant");
        await ShowHotkeyToastAsync("SELECT THE ACTIVE DAMAGE TYPE", true);
    }

    private void OpenGuideCombatBrief()
    {
        if (_clickThrough)
        {
            SetClickThrough(false);
        }
        SetToolsOpen(true);
        ShowToolsSection("guide");
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                var offset = GuideCombatBriefAnchor.TranslatePoint(new Point(0, 0), GuideToolsPanel).Y;
                ToolsScrollViewer.ScrollToVerticalOffset(Math.Max(0, offset - 4));
            }));
    }

    private void UpdateFieldGuide(bool force = false)
    {
        if (GuideResultsPanel is null || GuideControlsPanel is null)
        {
            return;
        }

        var query = Regex.Replace(GuideSearchInputBox?.Text ?? string.Empty, @"\s+", " ").Trim();
        var signature = string.Join('|', query, _guideFilterId, _guideSelectedSpeciesId,
            string.Join(',', _guideFavoriteSpeciesIds));
        if (!force && string.Equals(signature, _guideUiSignature, StringComparison.Ordinal))
        {
            return;
        }

        _guideSearchResults = FieldGuideLogic.Search(
            query, _guideFilterId, _guideFavoriteSpeciesIds, 6);
        if (_guideSearchResults.Count > 0
            && !_guideSearchResults.Any(entry => string.Equals(
                entry.Id, _guideSelectedSpeciesId, StringComparison.OrdinalIgnoreCase)))
        {
            _guideSelectedSpeciesId = _guideSearchResults[0].Id;
            ApplyAimCalibrationForSelection(useDefaultsWhenMissing: true, force: false);
        }

        _guideUiSignature = string.Join('|', query, _guideFilterId, _guideSelectedSpeciesId,
            string.Join(',', _guideFavoriteSpeciesIds));
        SetToggleButtonState(GuideAllFilterButton, _guideFilterId == "all");
        SetToggleButtonState(GuideCarnivoreFilterButton, _guideFilterId == "carnivore");
        SetToggleButtonState(GuideHerbivoreFilterButton, _guideFilterId == "herbivore");
        SetToggleButtonState(GuideOmnivoreFilterButton, _guideFilterId == "omnivore");

        GuideRosterStatusText.Text = _guideSearchResults.Count == 0
            ? "NO MATCHES / TRY A ROLE OR SPECIES"
            : $"{_guideSearchResults.Count} SHOWN / {FieldGuideLogic.Species.Length} SPECIES / {FieldGuideLogic.Snapshot}";
        GuideResultsPanel.Children.Clear();
        foreach (var entry in _guideSearchResults)
        {
            var favorite = _guideFavoriteSpeciesIds.Contains(entry.Id, StringComparer.OrdinalIgnoreCase);
            var selected = string.Equals(entry.Id, _guideSelectedSpeciesId, StringComparison.OrdinalIgnoreCase);
            var button = new Button
            {
                Tag = entry.Id,
                Content = $"{(favorite ? "FAV / " : string.Empty)}{entry.Name}  /  {entry.Role.ToUpperInvariant()}",
                Style = (Style)FindResource("DrawerButton"),
                ToolTip = $"{entry.DietClass} / {entry.Identity}",
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            button.Click += GuideSpeciesButton_Click;
            SetToggleButtonState(button, selected);
            GuideResultsPanel.Children.Add(button);
        }

        GuideProfileCard.Visibility = FieldGuideLogic.Find(_guideSelectedSpeciesId) is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (FieldGuideLogic.Find(_guideSelectedSpeciesId) is { } selectedGuideEntry)
        {
            GuideSpeciesNameText.Text = selectedGuideEntry.Name;
            GuideSpeciesMetaText.Text = $"{selectedGuideEntry.DietClass.ToUpperInvariant()} / {selectedGuideEntry.Role.ToUpperInvariant()}";
            GuideSpeciesIdentityText.Text = selectedGuideEntry.Identity;
            GuideSpeciesSurvivalText.Text = selectedGuideEntry.SurvivalTip;
            GuideSpeciesDangerText.Text = selectedGuideEntry.DangerTip;
            GuideSpeciesFoodText.Text = GuideFoodSummary(selectedGuideEntry.Id);
            if (CombatGuideLogic.Find(selectedGuideEntry.Id) is { } combatBrief)
            {
                GuideCombatBriefAnchor.Visibility = Visibility.Visible;
                GuideCombatDamageText.Text = combatBrief.DamageStyle;
                GuideCombatSignatureText.Text = combatBrief.Signature;
                GuideCombatPositionText.Text = combatBrief.Positioning;
                GuideCombatAbortText.Text = combatBrief.AbortCondition;
            }
            else
            {
                GuideCombatBriefAnchor.Visibility = Visibility.Collapsed;
            }
            var favorite = _guideFavoriteSpeciesIds.Contains(selectedGuideEntry.Id, StringComparer.OrdinalIgnoreCase);
            GuideFavoriteButton.Content = favorite ? "FAVED" : "FAV";
            GuideFavoriteButton.ToolTip = favorite
                ? "Remove this species from guide favorites"
                : "Keep this species at the top of guide results";
            SetToggleButtonState(GuideFavoriteButton, favorite);
            GuideDietCoachButton.Content = _lifeRunActive ? "USE IN DIET" : "START LIFE RUN";
            GuideCombatMutationsButton.Content = _lifeRunActive ? "COMBAT MUTATIONS" : "START LIFE RUN";
        }

        UpdateFightCheck(force: true);

        if (GuideControlsPanel.Children.Count == 0)
        {
            BuildFieldGuideControls();
        }
    }

    private static string GuideFoodSummary(string speciesId)
    {
        var diet = DietCoachLogic.Species.FirstOrDefault(entry =>
            string.Equals(entry.Id, speciesId, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(diet.Id))
        {
            return "No local diet snapshot is available.";
        }
        if (diet.MigrationDriven)
        {
            return "Migration zones set current plants. Use in-game scent and the live Food layer.";
        }

        static string FirstFoods(IEnumerable<string> foods) => string.Join(" / ", foods.Take(2));
        return $"P: {FirstFoods(diet.ProteinFoods)}\n" +
               $"C: {FirstFoods(diet.CarbFoods)}\n" +
               $"L: {FirstFoods(diet.LipidFoods)}";
    }

    private void BuildFieldGuideControls()
    {
        foreach (var control in FieldGuideLogic.EssentialControls)
        {
            var row = new Grid { Margin = new Thickness(1, 0, 1, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var keys = new TextBlock
            {
                Text = control.Keys,
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("AccentBrush")
            };
            var detail = new TextBlock
            {
                Text = $"{control.Action}\n{control.Note}",
                FontSize = 8,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("SecondaryTextBrush")
            };
            Grid.SetColumn(keys, 0);
            Grid.SetColumn(detail, 1);
            row.Children.Add(keys);
            row.Children.Add(detail);
            GuideControlsPanel.Children.Add(row);
        }
    }

    private void OpenFieldGuideButton_Click(object sender, RoutedEventArgs e) => OpenExternalUri(OverlayLinks.FieldGuide);

    // ── Wave 2 planner state: unified store, nest timer alerts, capture streaks, rate presets ──

    private bool _plannerStateStoreLoaded;
    private bool _plannerStateStoreForeignSchema;
    private string _plannerStateStoreSignature = string.Empty;
    private int _nestTimerAlertPresetIndex;
    private readonly Dictionary<string, int> _nestTimerAlertNotifiedMasks = new(StringComparer.Ordinal);
    private LifeRunCaptureStreak _captureStreak;
    private bool _captureStreakBaselineSet;
    private int _captureStreakLastCount;
    private uint _captureStreakLastClipboardSequence;
    private DateTimeOffset _captureStreakLastPersistAt;
    private string _selectedRatePresetId = string.Empty;
    private readonly List<ServerRatePreset> _customRatePresets = [];

    // Every planner-state write in this partial goes through here. The unified
    // schema-versioned planner-state store is the only writer of planner keys
    // (legacy dual-write retired after one shipped version; RestoreLifeRun
    // still reads old files for migration).
    private void SavePlannerState()
    {
        SaveSettings();
        PersistPlannerStateStore();
    }

    private PlannerStateDocument CapturePlannerStateDocument()
    {
        var nest = CurrentNestPlannerSnapshot();
        return PlannerStateStoreLogic.Normalize(new PlannerStateDocument
        {
            SchemaVersion = PlannerStateDocument.CurrentSchemaVersion,
            Growth = new PlannerGrowthState
            {
                Percent = _lifeRunGrowthPercent,
                ServerMultiplierIndex = _growthServerMultiplierIndex,
                Paused = _growthPaused
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
                AutoHatchGuidanceEnabled = _nestAutoHatchGuidanceEnabled,
                TimerAlertPresetIndex = _nestTimerAlertPresetIndex
            },
            Mutation = new PlannerMutationState
            {
                Loadout = _mutationLoadout.Select(item => new PlannerMutationItemState
                {
                    Slot = item.Slot,
                    MutationId = item.MutationId,
                    Status = item.Status
                }).ToList(),
                BuildFocusIndex = _mutationBuildFocusIndex,
                UnlockSelectedIndex = _mutationUnlockSelectedIndex,
                UnlockProgress = _mutationUnlockProgress.Select(item => new PlannerMutationUnlockState
                {
                    ChallengeId = item.ChallengeId,
                    Value = item.Value
                }).ToList()
            },
            Spawn = new PlannerSpawnState
            {
                CoverReady = _spawnPlanCoverReady,
                ScentChecked = _spawnPlanScentChecked,
                WaterFound = _spawnPlanWaterFound,
                FoodFound = _spawnPlanFoodFound
            },
            Stats = new PlannerStatsState
            {
                CaptureStreakCurrent = _captureStreak.Current,
                CaptureStreakBest = _captureStreak.Best
            },
            RatePresets = new PlannerRatePresetState
            {
                SelectedPresetId = _selectedRatePresetId,
                CustomPresets = _customRatePresets.Select(preset => new PlannerRatePresetItemState
                {
                    Id = preset.Id,
                    Label = preset.Label,
                    MultiplierIndex = preset.MultiplierIndex
                }).ToList()
            }
        });
    }

    private void ApplyPlannerStateDocument(PlannerStateDocument document)
    {
        var normalized = PlannerStateStoreLogic.Normalize(document);
        if (_lifeRunActive)
        {
            _lifeRunGrowthPercent = normalized.Growth.Percent;
            _growthServerMultiplierIndex = normalized.Growth.ServerMultiplierIndex;
            _growthPaused = normalized.Growth.Paused;
            _lifeRunStageIndex = GrowthPlannerLogic.StageIndex(_lifeRunGrowthPercent);
            ApplyNestPlannerSnapshot(new NestPlannerSnapshot(
                normalized.Nest.Active,
                normalized.Nest.PhaseIndex,
                normalized.Nest.PartnerReady,
                normalized.Nest.SiteReady,
                normalized.Nest.DebrisReady,
                normalized.Nest.ReservesReady,
                normalized.Nest.AccessIndex,
                normalized.Nest.EggTarget,
                normalized.Nest.EggsLaid,
                normalized.Nest.EggsHatched,
                normalized.Nest.YoungRaised,
                normalized.Nest.TimerDurationIndex));
            _nestAutoHatchGuidanceEnabled = normalized.Nest.AutoHatchGuidanceEnabled;
            _spawnPlanCoverReady = normalized.Spawn.CoverReady;
            _spawnPlanScentChecked = normalized.Spawn.ScentChecked;
            _spawnPlanWaterFound = normalized.Spawn.WaterFound;
            _spawnPlanFoodFound = normalized.Spawn.FoodFound;
            _mutationLoadout.Clear();
            _mutationLoadout.AddRange(MutationPlannerLogic.NormalizeLoadout(
                normalized.Mutation.Loadout.Select(item =>
                    new MutationLoadoutItem(item.Slot, item.MutationId, item.Status))));
            _mutationBuildFocusIndex = MutationBuildLogic.NormalizeFocusIndex(
                normalized.Mutation.BuildFocusIndex);
            _mutationUnlockProgress.Clear();
            _mutationUnlockProgress.AddRange(MutationUnlockLogic.NormalizeProgress(
                normalized.Mutation.UnlockProgress.Select(item =>
                    new MutationUnlockProgress(item.ChallengeId, item.Value))));
            _mutationUnlockSelectedIndex = MutationUnlockLogic.NormalizeSelectedIndex(
                normalized.Mutation.UnlockSelectedIndex);
        }

        _nestTimerAlertPresetIndex = NestTimerAlertLogic.NormalizePresetIndex(
            normalized.Nest.TimerAlertPresetIndex);
        _captureStreak = LifeRunLogic.NormalizeCaptureStreak(new LifeRunCaptureStreak(
            normalized.Stats.CaptureStreakCurrent,
            normalized.Stats.CaptureStreakBest));
        _customRatePresets.Clear();
        _customRatePresets.AddRange(ServerRatePresetLogic.NormalizeCustomPresets(
            normalized.RatePresets.CustomPresets.Select(item =>
                new ServerRatePreset(item.Id, item.Label, item.MultiplierIndex))));
        _selectedRatePresetId = ServerRatePresetLogic.Find(
            ServerRatePresetLogic.All(_customRatePresets),
            normalized.RatePresets.SelectedPresetId) is { } selectedPreset
            ? selectedPreset.Id
            : string.Empty;
        _growthPlannerUiSignature = string.Empty;
        _nestPlannerUiSignature = string.Empty;
        _spawnPlanUiSignature = string.Empty;
        _mutationPlannerUiSignature = string.Empty;
        _mutationUnlockUiSignature = string.Empty;
        _lifeRunUiSignature = string.Empty;
    }

    // One-time store load after LoadSettings restored the legacy per-planner keys into
    // memory. A valid store wins; a missing store migrates from those legacy values and
    // is written immediately; a newer schema is left untouched for the whole session.
    private void EnsurePlannerStateStoreLoaded()
    {
        if (_plannerStateStoreLoaded)
        {
            return;
        }

        _plannerStateStoreLoaded = true;
        var path = PlannerStateStoreLogic.ResolvePath(_activeSettingsPath);
        if (PlannerStateStoreLogic.TryRead(path, out var document, out var foreignSchema)
            && document is not null)
        {
            ApplyPlannerStateDocument(document);
            _plannerStateStoreSignature = PlannerStateStoreLogic.Serialize(document);
            return;
        }

        if (foreignSchema)
        {
            _plannerStateStoreForeignSchema = true;
            AddTacticalEvent(
                "LIFE",
                "Planner state store skipped",
                "Newer schema version · legacy planner keys still applied",
                warning: true);
            return;
        }

        PersistPlannerStateStore(force: true);
    }

    private void PersistPlannerStateStore(bool force = false)
    {
        if (!_plannerStateStoreLoaded || _plannerStateStoreForeignSchema)
        {
            return;
        }

        var document = CapturePlannerStateDocument();
        var signature = PlannerStateStoreLogic.Serialize(document);
        if (!force && string.Equals(signature, _plannerStateStoreSignature, StringComparison.Ordinal))
        {
            return;
        }

        if (PlannerStateStoreLogic.TryWrite(
                PlannerStateStoreLogic.ResolvePath(_activeSettingsPath),
                document))
        {
            _plannerStateStoreSignature = signature;
        }
    }

    // Threshold toasts for the nest timer, evaluated on the survival tick before any UI
    // early-out. Each threshold fires once per timer run; the generic completion
    // announcement still comes from the survival timer itself.
    private void UpdateNestTimerAlerts(DateTimeOffset now)
    {
        if (_nestTimerAlertNotifiedMasks.Count > 0)
        {
            var liveTimerIds = _survivalTimers.Select(timer => timer.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var staleId in _nestTimerAlertNotifiedMasks.Keys
                         .Where(id => !liveTimerIds.Contains(id))
                         .ToArray())
            {
                _nestTimerAlertNotifiedMasks.Remove(staleId);
            }
        }

        var thresholds = NestTimerAlertLogic.Thresholds(_nestTimerAlertPresetIndex);
        if (thresholds.Count == 0 || !_lifeRunActive || _streamerMode)
        {
            return;
        }

        var nest = CurrentNestPlannerSnapshot();
        if (!nest.Active)
        {
            return;
        }

        var timerLabel = NestPlannerLogic.TimerLabel(nest);
        if (string.IsNullOrWhiteSpace(timerLabel))
        {
            return;
        }

        var timer = _survivalTimers.FirstOrDefault(candidate =>
            string.Equals(candidate.Label, timerLabel, StringComparison.OrdinalIgnoreCase));
        if (timer is null || timer.IsPaused || timer.Completed)
        {
            return;
        }

        _nestTimerAlertNotifiedMasks.TryGetValue(timer.Id, out var notifiedMask);
        var hit = NestTimerAlertLogic.Evaluate(
            timer.DurationSeconds,
            (timer.EndsAt - now).TotalSeconds,
            thresholds,
            notifiedMask);
        if (hit is not { } alert)
        {
            return;
        }

        _nestTimerAlertNotifiedMasks[timer.Id] = notifiedMask | alert.MaskBit;
        AddTacticalEvent(
            "NEST",
            "Nest timer alert",
            $"{timerLabel} · {alert.ThresholdMinutes}m remaining",
            warning: true);
        _ = ShowHotkeyToastAsync(
            $"{timerLabel.ToUpperInvariant()} · {alert.ThresholdMinutes}M REMAINING",
            true);
    }

    // Player Sync capture streak: counts consecutive successful clipboard captures
    // (observed via the session capture counter, so both the clipboard poll and the
    // visible-text read count), resets on a failed capture while the game or overlay is
    // foreground, and survives restarts through the planner-state store.
    private void UpdateCaptureStreak(DateTimeOffset now)
    {
        var captureCount = _universalCoordinateCaptureCount;
        var clipboardSequence = NativeMethods.GetClipboardSequenceNumber();
        if (!_captureStreakBaselineSet)
        {
            _captureStreakBaselineSet = true;
            _captureStreakLastCount = captureCount;
            _captureStreakLastClipboardSequence = clipboardSequence;
            return;
        }

        var streakChanged = false;
        if (captureCount > _captureStreakLastCount)
        {
            _captureStreak = LifeRunLogic.RecordCaptureSuccess(
                _captureStreak,
                captureCount - _captureStreakLastCount);
            _captureStreakLastCount = captureCount;
            _captureStreakLastClipboardSequence = clipboardSequence;
            streakChanged = true;
        }
        else if (captureCount < _captureStreakLastCount)
        {
            // Session counter restarted (overlay relaunch); re-baseline without penalty.
            _captureStreakLastCount = captureCount;
            _captureStreakLastClipboardSequence = clipboardSequence;
        }
        else if (clipboardSequence != 0
                 && clipboardSequence != _captureStreakLastClipboardSequence
                 && _universalCoordinateCaptureEnabled
                 && !_streamerMode
                 && GetPlayFocusForeground() is PlayFocusForeground.Game or PlayFocusForeground.Mapper)
        {
            _captureStreakLastClipboardSequence = clipboardSequence;
            if (_captureStreak.Current > 0)
            {
                _captureStreak = LifeRunLogic.RecordCaptureFailure(_captureStreak);
                streakChanged = true;
            }
        }

        if (!streakChanged)
        {
            return;
        }

        _lifeRunUiSignature = string.Empty;
        if (now - _captureStreakLastPersistAt > TimeSpan.FromSeconds(5))
        {
            _captureStreakLastPersistAt = now;
            PersistPlannerStateStore();
        }
    }

    private ServerRatePreset? CurrentRatePreset() =>
        ServerRatePresetLogic.Find(
            ServerRatePresetLogic.All(_customRatePresets),
            _selectedRatePresetId) is { } preset
        && preset.MultiplierIndex == ServerRatePresetLogic.NormalizeMultiplierIndex(
            _growthServerMultiplierIndex)
            ? preset
            : null;

    private async Task CycleNestTimerAlertPresetAsync()
    {
        _nestTimerAlertPresetIndex =
            (_nestTimerAlertPresetIndex + 1) % NestTimerAlertLogic.ThresholdPresets.Length;
        _nestTimerAlertNotifiedMasks.Clear();
        _nestPlannerUiSignature = string.Empty;
        AddTacticalEvent(
            "NEST",
            "Nest timer alerts updated",
            $"Countdown alerts {NestTimerAlertLogic.PresetLabel(_nestTimerAlertPresetIndex)}");
        UpdateNestPlannerControls(force: true);
        SavePlannerState();
        await ShowHotkeyToastAsync(
            $"NEST ALERTS · {NestTimerAlertLogic.PresetLabel(_nestTimerAlertPresetIndex)}",
            true);
    }

    private async Task ApplyNextServerRatePresetAsync()
    {
        if (!_lifeRunActive || _streamerMode)
        {
            await ShowHotkeyToastAsync("START A LIFE RUN TO APPLY A RATE PRESET", false);
            return;
        }

        var presets = ServerRatePresetLogic.All(_customRatePresets);
        var next = ServerRatePresetLogic.Next(presets, _selectedRatePresetId, _growthServerMultiplierIndex);
        _selectedRatePresetId = next.Id;
        _growthServerMultiplierIndex = next.MultiplierIndex;
        if (CommunitySessionActive)
        {
            SyncCurrentCommunityServerProfile(includeGrowthRate: true);
            UpdateServerSessionPresentation();
        }

        CommitGrowthClockChange(
            "Server rate preset applied",
            $"{next.Label} / {GrowthPlannerLogic.ServerMultipliers[next.MultiplierIndex]:0.#}x");
        await ShowHotkeyToastAsync($"RATE PRESET · {next.Label}", true);
    }

    private async Task SaveCustomServerRatePresetAsync()
    {
        if (!_lifeRunActive || _streamerMode)
        {
            await ShowHotkeyToastAsync("START A LIFE RUN TO SAVE A RATE PRESET", false);
            return;
        }

        var result = ServerRatePresetLogic.TryCreateCustom(
            _growthServerMultiplierIndex,
            _customRatePresets,
            out var preset);
        if (result != ServerRatePresetSaveResult.Created)
        {
            await ShowHotkeyToastAsync(
                result == ServerRatePresetSaveResult.AlreadyTracked
                    ? "RATE ALREADY HAS A PRESET"
                    : $"CUSTOM PRESET LIMIT · {ServerRatePresetLogic.MaximumCustomPresets}",
                false);
            return;
        }

        _customRatePresets.Add(preset);
        _selectedRatePresetId = preset.Id;
        _growthPlannerUiSignature = string.Empty;
        AddTacticalEvent(
            "GROWTH",
            "Custom server rate preset saved",
            $"{preset.Label} · {_customRatePresets.Count}/{ServerRatePresetLogic.MaximumCustomPresets} custom");
        SavePlannerState();
        await ShowHotkeyToastAsync($"PRESET SAVED · {preset.Label}", true);
    }
}
