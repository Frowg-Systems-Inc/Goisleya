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
    private NextMoveSnapshot CurrentNextMoveSnapshot()
    {
        var incident = SurvivalAssistantLogic.Find(_survivalIncidentId);
        var now = DateTimeOffset.UtcNow;
        var fieldConditions = CurrentFieldConditionsGuidance(now);
        var coreVitals = CurrentCoreVitalsGuidance(now);
        var vitalsTrend = CurrentVitalsTrendAnalysis(now);
        var liveGrowth = CurrentLiveGrowthBridge(now);
        var liveSpecies = CurrentLiveSpeciesBridge(now);
        var approachBrief = CurrentApproachBrief();
        var restartWatch = CurrentServerRestartWatchView(now);
        var waterCrossing = CurrentWaterCrossingView();
        var shorelineCheck = CurrentShorelineCheckView(now);
        var manualSighting = CurrentManualSightingView(now);
        var soonestTimerSeconds = _survivalTimers
            .Where(timer => !timer.Completed && !timer.IsPaused)
            .Select(timer => Math.Max(0, (int)Math.Ceiling(GetTimerRemainingSeconds(timer, now))))
            .DefaultIfEmpty(-1)
            .Min();
        var nest = CurrentNestPlannerSnapshot();
        return new NextMoveSnapshot(
            _streamerMode,
            incident?.Label ?? string.Empty,
            !string.IsNullOrEmpty(_recoveryMonitorPriorityOverride)
                ? _recoveryMonitorPriorityOverride
                : incident?.Priority ?? string.Empty,
            incident?.Urgency ?? 0,
            _nearestEncounterDistance,
            _nearestEncounterCardinal,
            _nearestEncounterMotion,
            _packSpreadAlertActive,
            _packFriendCount,
            _packSpread,
            _waypointActive,
            _currentWaypointDistance,
            _waypointTrend,
            soonestTimerSeconds,
            _growthPaused,
            liveGrowth.EffectiveGrowthPercent,
            liveGrowth.PrimeReady,
            _elderPrimeConfirmed,
            _elderConfirmed,
            nest.Active,
            NestPlannerLogic.Phase(nest).Label,
            NestPlannerLogic.NextAction(nest, _nestAutoHatchGuidanceEnabled),
            _lifeRunActive,
            GetLifeRunNextObjective(),
            LiveMapServicesActive,
            _markerAvailable && _currentSelfX is not null && _currentSelfY is not null,
            fieldConditions.Warning && fieldConditions.HasFreshReport,
            fieldConditions.Heading,
            fieldConditions.Detail,
            coreVitals.Urgency,
            coreVitals.Heading,
            coreVitals.Detail,
            vitalsTrend.Warning,
            vitalsTrend.WarningHeading,
            vitalsTrend.WarningDetail,
            liveSpecies.State == LiveSpeciesBridgeState.Drifted,
            liveSpecies.LiveSpeciesName,
            _lifeTransitionPending?.Detected == true,
            _lifeTransitionPending?.Heading ?? string.Empty,
            _lifeTransitionPending?.Detail ?? string.Empty,
            _growthGatePending?.Detected == true,
            _growthGatePending?.Heading ?? string.Empty,
            _growthGatePending?.Detail ?? string.Empty,
            _growthGatePending?.ActionId ?? string.Empty,
            _growthGatePending?.ActionLabel ?? string.Empty,
            approachBrief.Visible,
            approachBrief.Urgency,
            approachBrief.Heading,
            approachBrief.Detail,
            approachBrief.ActionId,
            approachBrief.ActionLabel,
            restartWatch.Visible,
            restartWatch.RemainingSeconds,
            restartWatch.Heading,
            restartWatch.Detail,
            restartWatch.ActionId,
            restartWatch.ActionLabel,
            _waterCrossingCheckActive,
            waterCrossing.Severity,
            waterCrossing.Heading,
            waterCrossing.Detail,
            waterCrossing.ActionId,
            waterCrossing.ActionLabel,
            shorelineCheck.IsCurrent,
            shorelineCheck.Severity,
            shorelineCheck.Heading,
            shorelineCheck.Detail,
            shorelineCheck.ActionId,
            shorelineCheck.ActionLabel,
            ManualSightingApplies(manualSighting),
            manualSighting.Urgency,
            manualSighting.Heading,
            manualSighting.Detail);
    }

    private void UpdateNextMove(bool force = false)
    {
        if (NextMoveHeadingText is null
            || NextMoveDetailText is null
            || NextMoveCategoryText is null
            || NextMoveActionButton is null)
        {
            return;
        }

        var stack = NextMoveLogic.EvaluateStacked(CurrentNextMoveSnapshot());
        var recommendation = stack.Top;
        var signature = string.Join('|',
            recommendation.Category,
            recommendation.Heading,
            recommendation.Detail,
            recommendation.ActionId,
            recommendation.ActionLabel,
            recommendation.Priority,
            recommendation.Tone,
            stack.TotalActive);
        if (!force && string.Equals(signature, _nextMoveUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _nextMoveUiSignature = signature;

        var accent = recommendation.Tone switch
        {
            NextMoveTone.Critical => new SolidColorBrush(Color.FromRgb(255, 163, 108)),
            NextMoveTone.Warning => (Brush)FindResource("WarningBrush"),
            NextMoveTone.Active => (Brush)FindResource("AccentBrush"),
            _ => (Brush)FindResource("PrimaryTextBrush")
        };
        // Wave-8: stacked guidance — the slot shows the top of the deterministic
        // priority ranking; simultaneous runners-up surface as an honest "+N".
        NextMoveCategoryText.Text = stack.HasOverflow
            ? $"{recommendation.Category} {stack.OverflowSuffix}"
            : recommendation.Category;
        NextMoveCategoryText.ToolTip = stack.HasOverflow
            ? stack.OverflowTooltip
            : null;
        NextMoveCategoryText.Foreground = accent;
        NextMoveHeadingText.Text = recommendation.Heading;
        NextMoveHeadingText.Foreground = accent;
        NextMoveDetailText.Text = recommendation.Detail;
        NextMoveActionButton.Content = recommendation.ActionLabel;
        NextMoveActionButton.Tag = recommendation.ActionId;
        NextMoveActionButton.IsEnabled = recommendation.HasAction;
        NextMoveActionButton.ToolTip = recommendation.HasAction
            ? recommendation.ActionId == "escape-route"
                ? $"{recommendation.Detail} Create the vetted immediate heading now."
                : $"{recommendation.Detail} Open the exact Isley tool for this recommendation."
            : "Next Move is redacted in Streamer Mode";

        var focusSuggest = FocusModeSuggestLogic.FromNextMove(
            recommendation.Category,
            recommendation.Tone.ToString(),
            _activeFocusModeId);
        _pendingFocusModeSuggestId = focusSuggest.Available ? focusSuggest.ModeId : string.Empty;
        if (NextMoveFocusSuggestButton is not null)
        {
            NextMoveFocusSuggestButton.Visibility = focusSuggest.Available
                ? Visibility.Visible
                : Visibility.Collapsed;
            NextMoveFocusSuggestButton.Content = focusSuggest.Available
                ? $"TRY {focusSuggest.Label.ToUpperInvariant()}"
                : "TRY FOCUS";
            NextMoveFocusSuggestButton.ToolTip = focusSuggest.Available
                ? focusSuggest.Reason
                : "Apply the suggested Focus Mode manually — never auto-applied";
        }
    }

    private async void NextMoveFocusSuggestButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pendingFocusModeSuggestId))
        {
            return;
        }

        await ApplyFocusModeAsync(_pendingFocusModeSuggestId);
        UpdateNextMove(force: true);
    }

    private async void NextMoveActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string actionId }
            || string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }
        if (string.Equals(actionId, "escape-route", StringComparison.Ordinal))
        {
            SetToolsOpen(false);
        }
        await ExecuteCommandPaletteActionAsync(actionId);
    }

    private TripReadinessView CurrentTripReadinessView()
    {
        var incident = SurvivalAssistantLogic.Find(_survivalIncidentId);
        var vitals = CurrentCoreVitalsGuidance();
        var vitalsTrend = CurrentVitalsTrendAnalysis();
        var field = CurrentFieldConditionsGuidance();
        var routeActive = _waypointActive || _routePlanActive;
        var remainingDistance = _routePlanActive
            ? _routeRemainingDistance
            : _currentWaypointDistance;
        return TripReadinessLogic.Evaluate(new TripReadinessSnapshot(
            _streamerMode,
            LiveMapServicesActive,
            routeActive,
            _markerAvailable
            && !_staleAlertActive
            && _currentSelfMapX is not null
            && _currentSelfMapY is not null,
            remainingDistance,
            incident?.Urgency ?? 0,
            incident?.Label ?? string.Empty,
            vitals.Health,
            vitals.HealthFresh,
            vitals.Food,
            vitals.FoodFresh,
            vitals.Water,
            vitals.WaterFresh,
            vitals.Stamina,
            vitals.StaminaFresh,
            field.Weather,
            field.WeatherFresh,
            field.Light,
            field.LightFresh,
            _nearestEncounterDistance,
            _nearestEncounterMotion,
            !string.IsNullOrEmpty(_dangerAlertKey),
            _insideAlertZone,
            _tripRouteObstacleCount,
            _tripRouteInsideObstacle,
            _terrainNetworkReady,
            string.Equals(_waypointTrend, "away", StringComparison.Ordinal),
            vitalsTrend.Warning,
            vitalsTrend.WarningHeading,
            vitalsTrend.WarningDetail));
    }

    private void UpdateTripReadiness(bool force = false)
    {
        if (TripReadinessPanel is null
            || TripReadinessHeadingText is null
            || TripReadinessDetailText is null
            || TripReadinessActionButton is null)
        {
            return;
        }

        var view = CurrentTripReadinessView();
        var signature = string.Join('|',
            view.State,
            view.Heading,
            view.Detail,
            view.ActionLabel,
            view.ActionId,
            view.Severity);
        if (!force && string.Equals(signature, _tripReadinessUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _tripReadinessUiSignature = signature;

        TripReadinessPanel.Visibility = view.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        if (!view.IsVisible)
        {
            return;
        }

        var accent = view.State switch
        {
            TripReadinessState.Hold => new SolidColorBrush(Color.FromRgb(255, 112, 112)),
            TripReadinessState.Caution => (Brush)FindResource("WarningBrush"),
            TripReadinessState.Verify => (Brush)FindResource("AccentBrush"),
            TripReadinessState.Ready => new SolidColorBrush(Color.FromRgb(110, 231, 183)),
            _ => (Brush)FindResource("SecondaryTextBrush")
        };
        TripReadinessHeadingText.Text = view.Heading;
        TripReadinessHeadingText.Foreground = accent;
        TripReadinessDetailText.Text = view.Detail;
        TripReadinessActionButton.Content = view.ActionLabel;
        TripReadinessActionButton.Tag = view.ActionId;
        TripReadinessActionButton.IsEnabled = !string.IsNullOrEmpty(view.ActionId);
        TripReadinessActionButton.ToolTip = view.Detail;
        SetToggleButtonState(
            TripReadinessActionButton,
            view.State is TripReadinessState.Hold or TripReadinessState.Caution);

        var reveal = new System.Windows.Media.Animation.DoubleAnimation(
            0.35,
            1,
            TimeSpan.FromMilliseconds(160));
        TripReadinessHeadingText.BeginAnimation(OpacityProperty, reveal);
        TripReadinessDetailText.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0.45,
                1,
                TimeSpan.FromMilliseconds(180)));
    }

    private async void TripReadinessActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string actionId }
            || string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        if (string.Equals(actionId, "close-tools", StringComparison.Ordinal))
        {
            SetToolsOpen(false);
            await ShowHotkeyToastAsync("TRIP CHECK CLEAR · VERIFY TERRAIN IN GAME", true);
            return;
        }

        await ExecuteCommandPaletteActionAsync(actionId);
    }

    private FightCheckView CurrentFightCheckView()
    {
        var incident = SurvivalAssistantLogic.Find(_survivalIncidentId);
        var vitals = CurrentCoreVitalsGuidance();
        var manualSighting = CurrentManualSightingView();
        var abortCondition = CombatGuideLogic.Find(CurrentEffectiveSpeciesId())?.AbortCondition ?? string.Empty;
        return FightCheckLogic.Evaluate(new FightCheckSnapshot(
            _streamerMode,
            LiveMapServicesActive,
            _markerAvailable
            && !_staleAlertActive
            && _currentSelfX is not null
            && _currentSelfY is not null
            && _currentMarkerFreshnessAgeMs <= 6000,
            incident?.Urgency ?? 0,
            incident?.Label ?? string.Empty,
            vitals.Health,
            vitals.HealthFresh,
            vitals.Food,
            vitals.FoodFresh,
            vitals.Water,
            vitals.WaterFresh,
            vitals.Stamina,
            vitals.StaminaFresh,
            _encounterPlayerCount,
            _nearestEncounterDistance,
            _nearestEncounterCardinal,
            _nearestEncounterMotion,
            _nearestEncounterMotionSampleCount,
            _packSpreadAlertActive,
            _packFriendCount,
            _packSpread,
            abortCondition,
            ManualSightingApplies(manualSighting),
            manualSighting.Urgency,
            manualSighting.Heading,
            manualSighting.Detail));
    }

    private void UpdateFightCheck(bool force = false)
    {
        if (FightCheckAnchor is null
            || FightCheckBadgeText is null
            || FightCheckHeadingText is null
            || FightCheckDetailText is null
            || FightCheckActionButton is null)
        {
            return;
        }

        var view = CurrentFightCheckView();
        var effectiveSpeciesId = CurrentEffectiveSpeciesId();
        var signature = string.Join('|',
            effectiveSpeciesId,
            view.State,
            view.Badge,
            view.Heading,
            view.Detail,
            view.ActionLabel,
            view.ActionId,
            view.Severity);
        if (!force && string.Equals(signature, _fightCheckUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _fightCheckUiSignature = signature;

        FightCheckAnchor.Visibility = view.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        if (!view.IsVisible)
        {
            return;
        }

        var accent = view.State switch
        {
            FightCheckState.Hold => new SolidColorBrush(Color.FromRgb(255, 112, 112)),
            FightCheckState.Caution => new SolidColorBrush(Color.FromRgb(255, 163, 108)),
            FightCheckState.Verify => (Brush)FindResource("AccentBrush"),
            FightCheckState.Manual => (Brush)FindResource("AccentBrush"),
            FightCheckState.Watch => new SolidColorBrush(Color.FromRgb(110, 231, 183)),
            _ => (Brush)FindResource("SecondaryTextBrush")
        };
        FightCheckBadgeText.Text = view.Badge;
        FightCheckBadgeText.Foreground = accent;
        FightCheckHeadingText.Text = view.Heading;
        FightCheckHeadingText.Foreground = accent;
        FightCheckDetailText.Text = view.Detail;
        FightCheckActionButton.Content = view.ActionLabel;
        FightCheckActionButton.Tag = view.ActionId;
        FightCheckActionButton.IsEnabled = !string.IsNullOrEmpty(view.ActionId);
        FightCheckActionButton.ToolTip = view.Detail;
        SetToggleButtonState(
            FightCheckActionButton,
            view.State is FightCheckState.Hold or FightCheckState.Caution);

        FightCheckHeadingText.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0.4,
                1,
                TimeSpan.FromMilliseconds(160)));
    }

    private async void FightCheckActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string actionId }
            || string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        if (string.Equals(actionId, "current-combat-guide", StringComparison.Ordinal))
        {
            OpenExternalUri(OverlayLinks.CombatGuide);
            await ShowHotkeyToastAsync("CURRENT COMBAT GUIDE OPENED", true);
            return;
        }

        await ExecuteCommandPaletteActionAsync(actionId);
    }

    private ManualSightingView CurrentManualSightingView(DateTimeOffset? now = null) =>
        ManualSightingLogic.Evaluate(
            new ManualSightingSnapshot(
                _manualSightingReportedDirection,
                _manualSightingReportedRange,
                _manualSightingReportedAt),
            now ?? DateTimeOffset.UtcNow,
            _streamerMode);

    private bool ManualSightingApplies(ManualSightingView view) =>
        view.IsCurrent && (!LiveMapServicesActive || _encounterPlayerCount <= 0);

    private void UpdateManualSighting(bool force = false)
    {
        if (ManualSightingToggleButton is null
            || ManualSightingPanel is null
            || ManualSightingStatusText is null
            || ManualSightingTimerText is null
            || ManualSightingDetailText is null
            || ManualSightingReportButton is null
            || ManualSightingClearButton is null
            || UniversalSessionSightingButton is null)
        {
            return;
        }

        var view = CurrentManualSightingView();
        var guidanceApplies = ManualSightingApplies(view);
        var briefChanged = view.State != _manualSightingPreviousState
                           || view.RemainingSeconds != _manualSightingPreviousRemainingSeconds;
        _manualSightingPreviousState = view.State;
        _manualSightingPreviousRemainingSeconds = view.RemainingSeconds;
        if (briefChanged)
        {
            _nextMoveUiSignature = string.Empty;
            _fightCheckUiSignature = string.Empty;
            UpdateTacticalBrief();
        }

        var signature = string.Join('|',
            view.State,
            view.Badge,
            view.Heading,
            view.Detail,
            view.RemainingSeconds,
            view.Urgency,
            guidanceApplies,
            _manualSightingDraftDirection,
            _manualSightingDraftRange,
            _manualSightingExpanded,
            _streamerMode);
        if (!force && string.Equals(signature, _manualSightingUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _manualSightingUiSignature = signature;

        ManualSightingPanel.Visibility = _manualSightingExpanded && !_streamerMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        ManualSightingToggleButton.IsEnabled = !_streamerMode;
        ManualSightingToggleButton.Content = view.State switch
        {
            ManualSightingState.Current =>
                $"Sighting · {view.Badge} · {view.RemainingSeconds}s",
            ManualSightingState.Expired => "Sighting check · Expired",
            ManualSightingState.Hidden => "Sighting check · Hidden",
            _ => "Sighting check · Ready"
        };
        ManualSightingToggleButton.ToolTip = view.State switch
        {
            ManualSightingState.Current when guidanceApplies =>
                $"{view.Detail} This report is currently feeding Fight Check and Next Move.",
            ManualSightingState.Current =>
                $"{view.Detail} Authorized live contact data remains authoritative while available.",
            ManualSightingState.Expired => view.Detail,
            _ => "Report one temporary relative sighting from your own observation"
        };
        SetToggleButtonState(
            ManualSightingToggleButton,
            !_streamerMode && (_manualSightingExpanded || view.IsCurrent));

        var accent = view.Urgency switch
        {
            >= 3 => new SolidColorBrush(Color.FromRgb(255, 112, 112)),
            2 => (Brush)FindResource("WarningBrush"),
            1 => (Brush)FindResource("AccentBrush"),
            _ => (Brush)FindResource("SecondaryTextBrush")
        };
        ManualSightingStatusText.Text = view.State switch
        {
            ManualSightingState.Current => $"SIGHTING · {view.Badge}",
            ManualSightingState.Expired => "SIGHTING EXPIRED",
            ManualSightingState.Hidden => "HIDDEN IN STREAMER MODE",
            _ => "READY TO REPORT"
        };
        ManualSightingStatusText.Foreground = accent;
        ManualSightingTimerText.Text = view.State switch
        {
            ManualSightingState.Current => $"{view.RemainingSeconds}S",
            ManualSightingState.Expired => "EXPIRED",
            _ => $"{ManualSightingLogic.FreshnessSeconds}S"
        };
        ManualSightingTimerText.Foreground = accent;
        ManualSightingDetailText.Text = view.Detail;
        ManualSightingReportButton.Content = view.IsCurrent ? "UPDATE SIGHTING" : "REPORT SIGHTING";
        ManualSightingReportButton.IsEnabled = !_streamerMode
                                               && _manualSightingDraftDirection != ManualSightingDirection.None
                                               && _manualSightingDraftRange != ManualSightingRange.None;
        ManualSightingClearButton.IsEnabled = !_streamerMode && view.CanClear;

        SetToggleButtonState(
            ManualSightingAheadButton,
            _manualSightingDraftDirection == ManualSightingDirection.Ahead);
        SetToggleButtonState(
            ManualSightingRightButton,
            _manualSightingDraftDirection == ManualSightingDirection.Right);
        SetToggleButtonState(
            ManualSightingBehindButton,
            _manualSightingDraftDirection == ManualSightingDirection.Behind);
        SetToggleButtonState(
            ManualSightingLeftButton,
            _manualSightingDraftDirection == ManualSightingDirection.Left);
        SetToggleButtonState(
            ManualSightingCloseButton,
            _manualSightingDraftRange == ManualSightingRange.Close);
        SetToggleButtonState(
            ManualSightingNearButton,
            _manualSightingDraftRange == ManualSightingRange.Near);
        SetToggleButtonState(
            ManualSightingFarButton,
            _manualSightingDraftRange == ManualSightingRange.Far);

        UniversalSessionSightingButton.IsEnabled = !_streamerMode;
        UniversalSessionSightingButton.Content = view.IsCurrent
            ? $"SIGHT {view.RemainingSeconds}S"
            : view.State == ManualSightingState.Expired ? "SIGHT!" : "SIGHT";
        UniversalSessionSightingButton.ToolTip = view.IsCurrent
            ? $"{view.Badge} · {view.RemainingSeconds} seconds remain · update or clear the report"
            : "Report a session-only relative sighting without claiming live detection";
        SetToggleButtonState(UniversalSessionSightingButton, view.IsCurrent && !_streamerMode);
    }

    private void OpenManualSighting()
    {
        _manualSightingExpanded = true;
        _manualSightingUiSignature = string.Empty;
        UpdateManualSighting(force: true);
        OpenMapToolsAtSection("sighting-check");
    }

    private void ManualSightingToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }
        _manualSightingExpanded = !_manualSightingExpanded;
        _manualSightingUiSignature = string.Empty;
        UpdateManualSighting(force: true);
    }

    private void ManualSightingDirectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || sender is not Button { Tag: string direction })
        {
            return;
        }
        var parsed = ManualSightingLogic.ParseDirection(direction);
        if (parsed == ManualSightingDirection.None)
        {
            return;
        }
        _manualSightingDraftDirection = parsed;
        _manualSightingUiSignature = string.Empty;
        UpdateManualSighting(force: true);
    }

    private void ManualSightingRangeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || sender is not Button { Tag: string range })
        {
            return;
        }
        var parsed = ManualSightingLogic.ParseRange(range);
        if (parsed == ManualSightingRange.None)
        {
            return;
        }
        _manualSightingDraftRange = parsed;
        _manualSightingUiSignature = string.Empty;
        UpdateManualSighting(force: true);
    }

    private async void ManualSightingReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode
            || _manualSightingDraftDirection == ManualSightingDirection.None
            || _manualSightingDraftRange == ManualSightingRange.None)
        {
            return;
        }

        _manualSightingReportedDirection = _manualSightingDraftDirection;
        _manualSightingReportedRange = _manualSightingDraftRange;
        _manualSightingReportedAt = DateTimeOffset.UtcNow;
        _manualSightingUiSignature = string.Empty;
        AddTacticalEvent(
            "SIGHTING",
            "Manual sighting reported",
            $"{ManualSightingLogic.RangeLabel(_manualSightingReportedRange)} " +
            $"{ManualSightingLogic.DirectionLabel(_manualSightingReportedDirection)} · " +
            $"{ManualSightingLogic.FreshnessSeconds}s · no identity");
        UpdateManualSighting(force: true);
        UpdateNextMove(force: true);
        UpdateFightCheck(force: true);
        await ShowHotkeyToastAsync(
            $"SIGHTING · {ManualSightingLogic.RangeLabel(_manualSightingReportedRange)} " +
            $"{ManualSightingLogic.DirectionLabel(_manualSightingReportedDirection)} · " +
            $"{ManualSightingLogic.FreshnessSeconds}S",
            true);
    }

    private void ClearManualSighting(
        bool logEvent,
        bool updateUi,
        bool resetDraft = false,
        bool collapse = false)
    {
        var hadReport = _manualSightingReportedAt is not null;
        _manualSightingReportedDirection = ManualSightingDirection.None;
        _manualSightingReportedRange = ManualSightingRange.None;
        _manualSightingReportedAt = null;
        if (resetDraft)
        {
            _manualSightingDraftDirection = ManualSightingDirection.Ahead;
            _manualSightingDraftRange = ManualSightingRange.Near;
        }
        if (collapse)
        {
            _manualSightingExpanded = false;
        }
        _manualSightingUiSignature = string.Empty;
        _nextMoveUiSignature = string.Empty;
        _fightCheckUiSignature = string.Empty;
        if (logEvent && hadReport)
        {
            AddTacticalEvent("SIGHTING", "Manual sighting cleared", "Session-only report removed");
        }
        if (!updateUi)
        {
            return;
        }
        UpdateManualSighting(force: true);
        UpdateNextMove(force: true);
        UpdateFightCheck(force: true);
        UpdateTacticalBrief();
    }

    private async void ManualSightingClearButton_Click(object sender, RoutedEventArgs e)
    {
        var hadReport = _manualSightingReportedAt is not null;
        ClearManualSighting(logEvent: true, updateUi: true);
        await ShowHotkeyToastAsync(
            hadReport ? "SIGHTING CLEARED" : "NO SIGHTING TO CLEAR",
            true);
    }

    private string SafeLogoutBriefLabel()
    {
        var view = CurrentSafeLogoutGuardView();
        return view.State switch
        {
            SafeLogoutGuardState.CountingMonitored =>
                $"LOGOUT MONITORED {SafeLogoutLogic.FormatRemaining(view.RemainingSeconds)}",
            SafeLogoutGuardState.CountingManual =>
                $"LOGOUT MANUAL {SafeLogoutLogic.FormatRemaining(view.RemainingSeconds)}",
            SafeLogoutGuardState.Interrupted => "LOGOUT INTERRUPTED",
            SafeLogoutGuardState.MonitorLost => "LOGOUT MONITOR LOST",
            SafeLogoutGuardState.Complete => "LOGOUT VERIFY IN GAME",
            _ => string.Empty
        };
    }

    private string RecoveryMonitorBriefLabel() => _recoveryMonitorState switch
    {
        RecoveryMovementState.Moving => "RECOVERY MOVING · REST NOW",
        RecoveryMovementState.Settling => $"REST SETTLING {_recoveryMonitorRestSeconds}/{RecoveryMonitorLogic.SettlingSeconds}S",
        RecoveryMovementState.Resting => $"REST {RecoveryMonitorLogic.FormatElapsed(_recoveryMonitorRestSeconds)}",
        RecoveryMovementState.Manual => "REST MANUAL CHECK",
        RecoveryMovementState.Waiting => "REST MONITOR WAITING",
        _ => string.Empty
    };

    private string ManualSightingBriefLabel()
    {
        var view = CurrentManualSightingView();
        return ManualSightingApplies(view) ? view.BriefLabel : string.Empty;
    }

    private string BuildUniversalTacticalBriefSummary()
    {
        var segments = new List<string>
        {
            $"SESSION {ServerSessionLogic.BriefLabel(_serverSessionProfileId, _serverSessionName)}"
        };
        if (CommunitySessionActive && _lastCommunityServerStatus is { } communityStatus)
        {
            segments.Add(communityStatus.Online
                ? $"SERVER PUBLIC {communityStatus.Players}/{communityStatus.Capacity}" +
                  (!string.IsNullOrEmpty(_communityServerStatusError) ? " STALE" : string.Empty)
                : "SERVER PUBLIC OFFLINE");
        }
        if (_lifeRunActive)
        {
            segments.Add($"RUN {_lifeRunStageShortLabels[_lifeRunStageIndex]} " +
                         $"{LifeRunTrackedMilestoneCount()}/6 · PRIME {LifeRunPrimeConditionCount()}/10 " +
                         $"NEED {LifeRunPrimeRequiredConditionCount()}" +
                         (_elderEntombCount > 0 || _lifeRunGrowthPercent >= 75
                             ? $" · LINEAGE {_elderEntombCount + 1} ENT {_elderEntombCount}" +
                               $" PRIME-CHECK {(_elderPrimeConfirmed ? "Y" : "N")}"
                             : string.Empty) +
                         (_mutationLoadout.Count > 0
                             ? $" · MUT {MutationPlannerLogic.EquippedCount(_mutationLoadout)}/{_mutationLoadout.Count}" +
                               $" · {MutationBuildLogic.CompactSummary(CurrentMutationBuildAnalysis())}"
                             : string.Empty));
            var zoneSummary = ZoneBriefLogic.CompactSummary(CurrentZoneBriefView());
            if (!string.IsNullOrEmpty(zoneSummary)) segments.Add(zoneSummary);
        }

        if (SurvivalAssistantLogic.Find(_survivalIncidentId) is { } activeIncident)
        {
            segments.Add(SurvivalAssistantLogic.CompactSummary(
                activeIncident, _survivalIncidentStartedAt, DateTimeOffset.UtcNow));
        }
        var recoveryMonitor = RecoveryMonitorBriefLabel();
        if (!string.IsNullOrEmpty(recoveryMonitor)) segments.Add(recoveryMonitor);
        var coreVitals = CurrentCoreVitalsGuidance();
        if (!string.IsNullOrEmpty(coreVitals.BriefLabel))
        {
            segments.Add(coreVitals.BriefLabel);
        }
        var manualSighting = ManualSightingBriefLabel();
        if (!string.IsNullOrEmpty(manualSighting)) segments.Add(manualSighting);
        var shoreline = ShorelineCheckBriefLabel();
        if (!string.IsNullOrEmpty(shoreline)) segments.Add(shoreline);
        var restart = ServerRestartBriefLabel();
        if (!string.IsNullOrEmpty(restart)) segments.Add(restart);
        var logout = SafeLogoutBriefLabel();
        if (!string.IsNullOrEmpty(logout))
        {
            segments.Add(logout);
        }
        if (_survivalTimers.Count > 0)
        {
            segments.Add($"TIMERS {_survivalTimers.Count}");
        }
        if (!_lifeRunActive
            && string.IsNullOrWhiteSpace(_survivalIncidentId)
            && !coreVitals.Warning
            && string.IsNullOrEmpty(manualSighting)
            && _survivalTimers.Count == 0)
        {
            segments.Add("UNIVERSAL TOOLS READY");
        }
        return string.Join(" · ", segments);
    }

    private string BuildUniversalTacticalBriefText()
    {
        var parts = new List<string>
        {
            "Isley brief",
            $"SESSION {ServerSessionLogic.BriefLabel(_serverSessionProfileId, _serverSessionName)}",
            "UNIVERSAL TOOLS"
        };
        if (CommunitySessionActive && _lastCommunityServerStatus is { } communityStatus)
        {
            parts.Add(communityStatus.Online
                ? $"SERVER PUBLIC {communityStatus.Players}/{communityStatus.Capacity}" +
                  (!string.IsNullOrEmpty(_communityServerStatusError) ? " · STALE" : string.Empty)
                : "SERVER PUBLIC OFFLINE");
        }
        if (_lifeRunActive)
        {
            parts.Add(BuildLifeRunSummary(compact: true));
        }
        if (SurvivalAssistantLogic.Find(_survivalIncidentId) is { } activeIncident)
        {
            parts.Add(SurvivalAssistantLogic.CompactSummary(
                activeIncident, _survivalIncidentStartedAt, DateTimeOffset.UtcNow));
        }
        var recoveryMonitor = RecoveryMonitorBriefLabel();
        if (!string.IsNullOrEmpty(recoveryMonitor)) parts.Add(recoveryMonitor);
        var coreVitals = CurrentCoreVitalsGuidance();
        if (!string.IsNullOrEmpty(coreVitals.BriefLabel))
        {
            parts.Add(coreVitals.BriefLabel);
        }
        var manualSighting = ManualSightingBriefLabel();
        if (!string.IsNullOrEmpty(manualSighting)) parts.Add(manualSighting);
        var shoreline = ShorelineCheckBriefLabel();
        if (!string.IsNullOrEmpty(shoreline)) parts.Add(shoreline);
        var restart = ServerRestartBriefLabel();
        if (!string.IsNullOrEmpty(restart)) parts.Add(restart);
        var logout = SafeLogoutBriefLabel();
        if (!string.IsNullOrEmpty(logout))
        {
            parts.Add(logout);
        }
        if (_survivalTimers.Count > 0)
        {
            var paused = _survivalTimers.Count(timer => timer.IsPaused);
            parts.Add($"TIMERS {_survivalTimers.Count}" + (paused > 0 ? $" · {paused} PAUSED" : string.Empty));
        }
        parts.Add("LIVE MAP DATA OMITTED");
        return string.Join(" | ", parts);
    }

    private string BuildTacticalBriefSummary()
    {
        if (_streamerMode)
        {
            return "Hidden in streamer mode";
        }

        if (!LiveMapServicesActive)
        {
            return BuildUniversalTacticalBriefSummary();
        }

        var segments = new List<string>();
        var selfAvailable = _markerAvailable && _currentSelfX is not null && _currentSelfY is not null;
        var headingConfidence = HeadingConfidenceLogic.Evaluate(
            selfAvailable,
            _currentSelfBearing,
            _currentMarkerFreshnessAgeMs,
            _staleAlertActive);
        segments.Add(selfAvailable
            ? $"YOU {(string.IsNullOrWhiteSpace(_currentGridReference) ? "LIVE" : _currentGridReference)} " +
              $"{ToCardinal(headingConfidence.HeldDegrees)}{headingConfidence.CompactSuffix}"
            : "YOU WAITING");
        if (_lifeRunActive)
        {
            segments.Add($"RUN {_lifeRunStageShortLabels[_lifeRunStageIndex]} " +
                         $"{LifeRunTrackedMilestoneCount()}/6 · PRIME {LifeRunPrimeConditionCount()}/10 " +
                         $"NEED {LifeRunPrimeRequiredConditionCount()}" +
                         (_elderEntombCount > 0 || _lifeRunGrowthPercent >= 75
                             ? $" · LINEAGE {_elderEntombCount + 1} ENT {_elderEntombCount}" +
                               $" PRIME-CHECK {(_elderPrimeConfirmed ? "Y" : "N")}"
                             : string.Empty) +
                         (_mutationLoadout.Count > 0
                             ? $" · MUT {MutationPlannerLogic.EquippedCount(_mutationLoadout)}/{_mutationLoadout.Count}" +
                               $" · {MutationBuildLogic.CompactSummary(CurrentMutationBuildAnalysis())}"
                             : string.Empty));
            var zoneSummary = ZoneBriefLogic.CompactSummary(CurrentZoneBriefView());
            if (!string.IsNullOrEmpty(zoneSummary)) segments.Add(zoneSummary);
        }

        if (SurvivalAssistantLogic.Find(_survivalIncidentId) is { } activeIncident)
        {
            segments.Add(SurvivalAssistantLogic.CompactSummary(
                activeIncident, _survivalIncidentStartedAt, DateTimeOffset.UtcNow));
        }
        var recoveryMonitor = RecoveryMonitorBriefLabel();
        if (!string.IsNullOrEmpty(recoveryMonitor)) segments.Add(recoveryMonitor);

        var coreVitals = CurrentCoreVitalsGuidance();
        if (!string.IsNullOrEmpty(coreVitals.BriefLabel))
        {
            segments.Add(coreVitals.BriefLabel);
        }
        var manualSighting = ManualSightingBriefLabel();
        if (!string.IsNullOrEmpty(manualSighting))
        {
            segments.Add(manualSighting);
        }

        var shoreline = ShorelineCheckBriefLabel();
        if (!string.IsNullOrEmpty(shoreline))
        {
            segments.Add(shoreline);
        }

        var fieldConditions = CurrentFieldConditionsGuidance();
        if (!string.IsNullOrEmpty(fieldConditions.BriefLabel))
        {
            segments.Add(fieldConditions.BriefLabel);
        }

        var restart = ServerRestartBriefLabel();
        if (!string.IsNullOrEmpty(restart)) segments.Add(restart);

        var logout = SafeLogoutBriefLabel();
        if (!string.IsNullOrEmpty(logout))
        {
            segments.Add(logout);
        }

        if (_waypointActive && _currentWaypointDistance is not null)
        {
            segments.Add($"ROUTE {_currentWaypointDistance:0} MU " +
                         (_currentWaypointBearing is null ? string.Empty : ToCardinal(_currentWaypointBearing.Value)));
        }

        var crossing = WaterCrossingBriefLabel();
        if (!string.IsNullOrEmpty(crossing))
        {
            segments.Add(crossing);
        }

        if (_insideAlertZone)
        {
            segments.Add("INSIDE ALERT ZONE");
        }
        else
        {
            var dangerThreshold = _dangerAlertDistances[_dangerAlertIndex];
            if (dangerThreshold > 0
                && _nearestDangerDistance is not null
                && _nearestDangerDistance <= dangerThreshold)
            {
                segments.Add($"DANGER {_nearestDangerDistance:0} MU {_nearestDangerCardinal}");
            }
        }

        if (_packFriendCount > 0)
        {
            var cohesion = (_packSpread ?? 0) <= 25 ? "TIGHT" : (_packSpread ?? 0) <= 50 ? "LOOSE" : "SCATTERED";
            var motion = _packSpreadMotionSampleCount >= 3
                         && _packSpreadRate is not null
                         && _packSpreadMotion is "spreading" or "regrouping" or "steady"
                ? _packSpreadMotion switch
                {
                    "spreading" => " · SPREADING",
                    "regrouping" => " · REGROUPING",
                    _ => " · HOLDING"
                }
                : string.Empty;
            var course = _packCourseSampleCount >= 3
                         && _packCourseSpeed is not null
                         && _packCourseState is "moving" or "stationary"
                ? _packCourseState == "moving"
                    ? $" · COURSE {_packCourseCardinal}"
                    : " · PACK STILL"
                : string.Empty;
            segments.Add($"PACK {_packFriendCount} {cohesion}{motion}{course}");
        }

        segments.Add(_encounterPlayerCount > 0
            ? _nearestEncounterDistance is not null
                ? $"CONTACT {_encounterPlayerCount} · {_nearestEncounterDistance:0} MU {_nearestEncounterCardinal}"
                : $"CONTACT {_encounterPlayerCount} · POSITION WAITING"
            : _rememberedEncounterCount > 0
                ? $"RECENT CONTACT {_rememberedEncounterCount}"
                : "AREA CLEAR");
        return string.Join(" · ", segments).Trim();
    }

    private string BuildTacticalBriefText()
    {
        if (_streamerMode)
        {
            return string.Empty;
        }

        if (!LiveMapServicesActive)
        {
            return BuildUniversalTacticalBriefText();
        }

        var parts = new List<string>
        {
            "Isley brief",
            !_tacticalMapReadyLogged ? "FEED CONNECTING" : _staleAlertActive ? "FEED STALE" : "FEED ACTIVE"
        };
        var selfAvailable = _markerAvailable && _currentSelfX is not null && _currentSelfY is not null;
        if (selfAvailable)
        {
            var grid = string.IsNullOrWhiteSpace(_currentGridReference)
                ? "grid unavailable"
                : $"grid {_currentGridReference}";
            var movement = _currentSelfSpeed >= 0.15
                ? $"{_currentSelfSpeed:0.0} MU/min"
                : "still";
            var headingConfidence = HeadingConfidenceLogic.Evaluate(
                selfAvailable,
                _currentSelfBearing,
                _currentMarkerFreshnessAgeMs,
                _staleAlertActive);
            parts.Add($"YOU {grid} · heading {ToCardinal(headingConfidence.HeldDegrees)} " +
                      $"{headingConfidence.HeldDegrees:000}°{headingConfidence.Suffix} · {movement}");
        }
        else
        {
            parts.Add("YOU WAITING");
        }
        if (_lifeRunActive)
        {
            parts.Add(BuildLifeRunSummary(compact: true));
        }
        if (SurvivalAssistantLogic.Find(_survivalIncidentId) is { } activeIncident)
        {
            parts.Add(SurvivalAssistantLogic.CompactSummary(
                activeIncident, _survivalIncidentStartedAt, DateTimeOffset.UtcNow));
        }
        var recoveryMonitor = RecoveryMonitorBriefLabel();
        if (!string.IsNullOrEmpty(recoveryMonitor)) parts.Add(recoveryMonitor);

        var coreVitals = CurrentCoreVitalsGuidance();
        if (!string.IsNullOrEmpty(coreVitals.BriefLabel))
        {
            parts.Add(coreVitals.BriefLabel);
        }
        var manualSighting = ManualSightingBriefLabel();
        if (!string.IsNullOrEmpty(manualSighting))
        {
            parts.Add(manualSighting);
        }

        var shoreline = ShorelineCheckBriefLabel();
        if (!string.IsNullOrEmpty(shoreline))
        {
            parts.Add(shoreline);
        }

        var fieldConditions = CurrentFieldConditionsGuidance();
        if (!string.IsNullOrEmpty(fieldConditions.BriefLabel))
        {
            parts.Add(fieldConditions.BriefLabel);
        }

        var restart = ServerRestartBriefLabel();
        if (!string.IsNullOrEmpty(restart)) parts.Add(restart);

        var logout = SafeLogoutBriefLabel();
        if (!string.IsNullOrEmpty(logout))
        {
            parts.Add(logout);
        }

        if (_waypointActive)
        {
            var route = new List<string> { "ROUTE ACTIVE" };
            if (_currentWaypointDistance is not null)
            {
                route.Add($"{_currentWaypointDistance:0.0} MU");
            }
            if (_currentWaypointBearing is not null)
            {
                route.Add(ToCardinal(_currentWaypointBearing.Value));
            }
            if (_routePlanActive && _routeStopCount > 0)
            {
                route.Add($"leg {Math.Clamp(_routeCurrentIndex + 1, 1, _routeStopCount)}/{_routeStopCount}");
            }
            var eta = FormatBriefEta(_navigationEtaMinutes);
            if (!string.IsNullOrEmpty(eta))
            {
                route.Add($"ETA {eta}");
            }
            parts.Add(string.Join(" · ", route));
        }

        var crossing = WaterCrossingBriefLabel();
        if (!string.IsNullOrEmpty(crossing))
        {
            parts.Add(crossing);
        }

        if (_insideAlertZone)
        {
            parts.Add("SAFETY INSIDE SAVED ALERT ZONE");
        }
        else
        {
            var dangerThreshold = _dangerAlertDistances[_dangerAlertIndex];
            if (dangerThreshold > 0
                && _nearestDangerDistance is not null
                && _nearestDangerDistance <= dangerThreshold)
            {
                parts.Add($"DANGER PIN {_nearestDangerDistance:0.0} MU {_nearestDangerCardinal}");
            }
        }

        if (_packFriendCount > 0)
        {
            var pack = new List<string> { $"PACK {_packFriendCount}" };
            if (_packSpread is not null)
            {
                var cohesion = _packSpread <= 25 ? "tight" : _packSpread <= 50 ? "loose" : "scattered";
                pack.Add($"{cohesion} · {_packSpread:0.0} MU spread");
            }
            if (_packSpreadRate is not null && _packSpreadMotionSampleCount >= 3)
            {
                pack.Add(_packSpreadMotion switch
                {
                    "spreading" => $"spreading {Math.Abs(_packSpreadRate.Value):0.0} MU/min",
                    "regrouping" => $"regrouping {Math.Abs(_packSpreadRate.Value):0.0} MU/min",
                    "steady" => "holding formation",
                    _ => string.Empty
                });
                pack.RemoveAll(string.IsNullOrEmpty);
            }
            if (_packCenterDistance is not null)
            {
                pack.Add($"center {_packCenterDistance:0.0} MU {_packCenterCardinal}");
            }
            if (_packCourseSpeed is not null && _packCourseSampleCount >= 3)
            {
                pack.Add(_packCourseState switch
                {
                    "moving" => $"pack moving {_packCourseCardinal} {_packCourseSpeed.Value:0.0} MU/min",
                    "stationary" => "pack stationary",
                    _ => string.Empty
                });
                pack.RemoveAll(string.IsNullOrEmpty);
            }
            parts.Add(string.Join(" · ", pack));
        }

        if (_encounterPlayerCount > 0)
        {
            var contact = new List<string> { $"CONTACT {_encounterPlayerCount}" };
            if (_nearestEncounterDistance is not null)
            {
                contact.Add($"nearest {_nearestEncounterDistance:0.0} MU {_nearestEncounterCardinal}");
            }
            if (_nearestEncounterRelativeSpeed is not null && _nearestEncounterMotionSampleCount >= 3)
            {
                contact.Add(_nearestEncounterMotion switch
                {
                    "closing" => $"closing {Math.Abs(_nearestEncounterRelativeSpeed.Value):0.0} MU/min",
                    "opening" => $"opening {Math.Abs(_nearestEncounterRelativeSpeed.Value):0.0} MU/min",
                    _ => "holding distance"
                });
                var contactEta = FormatEncounterIntercept(_nearestEncounterInterceptSeconds);
                if (!string.IsNullOrEmpty(contactEta))
                {
                    contact.Add($"contact {contactEta.ToLowerInvariant()} if unchanged");
                }
            }
            parts.Add(string.Join(" · ", contact));
        }
        else if (_rememberedEncounterCount > 0)
        {
            parts.Add($"RECENT CONTACT {_rememberedEncounterCount} · newest " +
                      $"{FormatElapsedAge(_rememberedEncounterNewestAgeMs ?? 0)} ago · last known only");
        }
        else
        {
            parts.Add("CONTACT CLEAR");
        }

        parts.Add(_lastServerStatus switch
        {
            { Online: true } status => $"SERVER {status.Players}/{status.Capacity}",
            { Online: false } => "SERVER OFFLINE",
            _ => "SERVER UNKNOWN"
        });
        return string.Join(" | ", parts);
    }

    private void UpdateTacticalBrief()
    {
        if (TacticalBriefStatusText is null || CopyTacticalBriefButton is null)
        {
            return;
        }

        TacticalBriefStatusText.Text = BuildTacticalBriefSummary();
        TacticalBriefStatusText.Foreground = _streamerMode
            ? (Brush)FindResource("SecondaryTextBrush")
            : LiveMapServicesActive && (!_tacticalMapReadyLogged || _staleAlertActive || _insideAlertZone)
                ? (Brush)FindResource("WarningBrush")
                : (Brush)FindResource("SecondaryTextBrush");
        CopyTacticalBriefButton.IsEnabled = !_streamerMode;
        CopyTacticalBriefButton.Content = _streamerMode ? "Brief hidden" : "Copy tactical brief";
        CopyTacticalBriefButton.ToolTip = _streamerMode
            ? "Tactical brief is disabled in streamer mode"
            : LiveMapServicesActive
                ? "Copy an identity-free live callout with current-life notes, grid, route, safety, pack, contact, feed, and server state"
                : "Copy an identity-free manual-session brief without Live Map positions, contacts, or server population";
    }

    private void AddTacticalEvent(
        string category,
        string title,
        string detail,
        bool warning = false)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AddTacticalEvent(category, title, detail, warning));
            return;
        }

        category = string.IsNullOrWhiteSpace(category) ? "SYSTEM" : category.Trim().ToUpperInvariant();
        title = string.IsNullOrWhiteSpace(title) ? "Update" : title.Trim();
        detail = string.IsNullOrWhiteSpace(detail) ? "No additional detail" : detail.Trim();
        var now = DateTimeOffset.Now;
        if (_tacticalEvents.FirstOrDefault() is { } newest
            && string.Equals(newest.Category, category, StringComparison.Ordinal)
            && string.Equals(newest.Title, title, StringComparison.Ordinal)
            && string.Equals(newest.Detail, detail, StringComparison.Ordinal)
            && now - newest.OccurredAt < TimeSpan.FromSeconds(3))
        {
            return;
        }

        _tacticalEvents.Insert(0, new TacticalEventEntry(
            ++_nextTacticalEventId,
            now,
            category,
            title,
            detail,
            warning));
        if (_tacticalEvents.Count > 24)
        {
            _tacticalEvents.RemoveRange(24, _tacticalEvents.Count - 24);
        }

        _clearTacticalLogConfirmationPending = false;
        UpdateTacticalLog(animateNewest: IsLoaded);
    }

    private Brush TacticalEventBrush(TacticalEventEntry entry)
    {
        if (entry.Warning)
        {
            return (Brush)FindResource("WarningBrush");
        }

        return entry.Category switch
        {
            "PACK" => new SolidColorBrush(Color.FromRgb(52, 211, 153)),
            "PLAYER" => new SolidColorBrush(Color.FromRgb(251, 191, 36)),
            "TIMER" => new SolidColorBrush(Color.FromRgb(167, 139, 250)),
            "LOGOUT" => new SolidColorBrush(Color.FromRgb(110, 231, 183)),
            "RECOVERY" => new SolidColorBrush(Color.FromRgb(96, 165, 250)),
            "ROUTE" => (Brush)FindResource("AccentBrush"),
            _ => (Brush)FindResource("SecondaryTextBrush")
        };
    }

    private UIElement BuildTacticalEventRow(TacticalEventEntry entry, bool animate)
    {
        var accent = TacticalEventBrush(entry);
        var content = new StackPanel();
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var category = new TextBlock
        {
            Text = entry.Category,
            FontSize = 7,
            FontWeight = FontWeights.Bold,
            Foreground = accent,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var title = new TextBlock
        {
            Text = entry.Title,
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(title, 1);
        var time = new TextBlock
        {
            Text = entry.OccurredAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            FontSize = 7,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            Margin = new Thickness(6, 0, 0, 0)
        };
        Grid.SetColumn(time, 2);
        header.Children.Add(category);
        header.Children.Add(title);
        header.Children.Add(time);
        content.Children.Add(header);
        content.Children.Add(new TextBlock
        {
            Text = entry.Detail,
            FontSize = 7.5,
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        var row = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x3D, 0x64, 0x74, 0x8B)),
            BorderThickness = new Thickness(2, 0, 0, 1),
            Padding = new Thickness(7, 5, 2, 6),
            Margin = new Thickness(0, 0, 0, 4),
            Child = content,
            ToolTip = $"{entry.Category} · {entry.OccurredAt:HH:mm:ss} · {entry.Title} · {entry.Detail}"
        };
        row.BorderBrush = accent;
        if (animate)
        {
            row.Opacity = 1;
            var translate = new TranslateTransform();
            row.RenderTransform = translate;
            row.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            });
            translate.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 4,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            });
        }
        return row;
    }

    private void UpdateTacticalLog(bool animateNewest = false)
    {
        if (TacticalLogListPanel is null
            || TacticalLogStatusText is null
            || CopyTacticalLogButton is null
            || ClearTacticalLogButton is null)
        {
            return;
        }

        EnsureTacticalLogExportButton();
        TacticalLogListPanel.Children.Clear();
        if (_streamerMode)
        {
            TacticalLogListPanel.Visibility = Visibility.Collapsed;
            TacticalLogStatusText.Text = "Timeline hidden in streamer mode";
            CopyTacticalLogButton.IsEnabled = false;
            ClearTacticalLogButton.IsEnabled = false;
            if (_tacticalLogExportButton is not null)
            {
                _tacticalLogExportButton.IsEnabled = false;
            }
            return;
        }

        TacticalLogListPanel.Visibility = Visibility.Visible;
        CopyTacticalLogButton.IsEnabled = _tacticalEvents.Count > 0;
        ClearTacticalLogButton.IsEnabled = _tacticalEvents.Count > 0;
        if (_tacticalLogExportButton is not null)
        {
            _tacticalLogExportButton.IsEnabled = _tacticalEvents.Count > 0;
        }
        ClearTacticalLogButton.Content = _clearTacticalLogConfirmationPending
            ? "CONFIRM CLEAR"
            : "CLEAR LOG";
        TacticalLogStatusText.Foreground = _clearTacticalLogConfirmationPending
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("SecondaryTextBrush");

        if (_tacticalEvents.Count == 0)
        {
            TacticalLogStatusText.Text = "No events yet · session-only";
            return;
        }

        TacticalLogStatusText.Text = _clearTacticalLogConfirmationPending
            ? "Select Clear Log again within 3 seconds"
            : _tacticalEvents.Count > 8
                ? $"{_tacticalEvents.Count} events · showing latest 8 · session-only"
                : $"{_tacticalEvents.Count} event{(_tacticalEvents.Count == 1 ? string.Empty : "s")} · session-only";
        var visibleEvents = _tacticalEvents.Take(8).ToList();
        for (var index = 0; index < visibleEvents.Count; index++)
        {
            TacticalLogListPanel.Children.Add(BuildTacticalEventRow(
                visibleEvents[index],
                animateNewest && index == 0));
        }
    }

    private string BuildTacticalLogText()
    {
        var lines = new List<string>
        {
            $"ISLEY · TACTICAL LOG · {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}",
            "Session-only local timeline"
        };
        lines.AddRange(_tacticalEvents.AsEnumerable().Reverse().Select(entry =>
            $"{entry.OccurredAt:HH:mm:ss} · {entry.Category} · {entry.Title} · {entry.Detail}"));
        return string.Join(Environment.NewLine, lines);
    }

    private void ResetWaterCrossingCheck(bool logEvent)
    {
        var wasActive = _waterCrossingCheckActive;
        _waterCrossingCheckActive = false;
        _waterCrossingUiSignature = string.Empty;
        _waterCrossingLoggedDecisionKey = string.Empty;
        if (wasActive && logEvent)
        {
            AddTacticalEvent("CROSSING", "Water crossing check cleared", "Session-only check ended");
        }
    }

    private WaterCrossingView CurrentWaterCrossingView()
    {
        var incident = SurvivalAssistantLogic.Find(_survivalIncidentId);
        var vitals = CurrentCoreVitalsGuidance();
        var field = CurrentFieldConditionsGuidance();
        var liveSpecies = CurrentLiveSpeciesBridge();
        var speciesKnown = liveSpecies.Available
                           || _dietSpeciesIndex > 0 && _dietSpeciesIndex <= DietCoachLogic.Species.Length;
        return WaterCrossingLogic.Evaluate(new WaterCrossingSnapshot(
            _streamerMode,
            LiveMapServicesActive,
            _waterCrossingCheckActive,
            _measurementArmed,
            _measurementHasStart,
            _measurementActive ? _measurementDistance : null,
            speciesKnown ? CurrentEffectiveSpeciesId() : string.Empty,
            speciesKnown,
            incident?.Urgency ?? 0,
            incident?.Label ?? string.Empty,
            vitals.Health,
            vitals.HealthFresh,
            vitals.Stamina,
            vitals.StaminaFresh,
            field.Weather,
            field.WeatherFresh,
            _nearestEncounterDistance,
            _nearestEncounterMotion,
            !string.IsNullOrEmpty(_dangerAlertKey),
            _measurementMarkedBoundaryCount,
            _measurementInsideMarkedBoundary));
    }

    private Brush WaterCrossingAccent(WaterCrossingState state) => state switch
    {
        WaterCrossingState.Hold => new SolidColorBrush(Color.FromRgb(255, 112, 112)),
        WaterCrossingState.Caution => (Brush)FindResource("WarningBrush"),
        WaterCrossingState.Verify or WaterCrossingState.Measure => (Brush)FindResource("AccentBrush"),
        WaterCrossingState.Ready => new SolidColorBrush(Color.FromRgb(110, 231, 183)),
        _ => (Brush)FindResource("SecondaryTextBrush")
    };

    private void UpdateWaterCrossingCheck(bool force = false)
    {
        if (WaterCrossingSectionAnchor is null
            || WaterCrossingToggleButton is null
            || WaterCrossingResultPanel is null
            || WaterCrossingHeadingText is null
            || WaterCrossingDetailText is null
            || WaterCrossingActionButton is null
            || MeasurementHeadingText is null)
        {
            return;
        }

        var view = CurrentWaterCrossingView();
        var signature = string.Join('|',
            view.State,
            view.Key,
            view.Heading,
            view.Detail,
            view.ActionLabel,
            view.ActionId,
            view.HudLabel,
            view.Severity,
            _measurementArmed,
            _measurementHasStart,
            _measurementActive,
            _measurementDistance);
        if (!force && string.Equals(signature, _waterCrossingUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _waterCrossingUiSignature = signature;

        var visible = view.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        WaterCrossingSectionAnchor.Visibility = visible;
        WaterCrossingToggleButton.Visibility = visible;
        WaterCrossingResultPanel.Visibility = visible;
        if (!view.IsVisible)
        {
            return;
        }

        var accent = WaterCrossingAccent(view.State);

        WaterCrossingToggleButton.Content = _waterCrossingCheckActive
            ? "Crossing check on · clear"
            : "Start crossing check";
        WaterCrossingToggleButton.ToolTip = _waterCrossingCheckActive
            ? "Turn off Water Crossing Check; a completed ruler measurement remains available"
            : "Arm the map ruler and mark the intended entry and exit banks";
        SetToggleButtonState(WaterCrossingToggleButton, _waterCrossingCheckActive);
        WaterCrossingHeadingText.Text = view.Heading;
        WaterCrossingHeadingText.Foreground = accent;
        WaterCrossingDetailText.Text = view.Detail;
        WaterCrossingActionButton.Content = view.ActionLabel;
        WaterCrossingActionButton.Tag = view.ActionId;
        WaterCrossingActionButton.IsEnabled = !string.IsNullOrEmpty(view.ActionId);
        WaterCrossingActionButton.ToolTip = view.Detail;
        SetToggleButtonState(
            WaterCrossingActionButton,
            view.State is WaterCrossingState.Hold or WaterCrossingState.Caution);

        if (_waterCrossingCheckActive && (_measurementArmed || _measurementActive))
        {
            MeasurementHeadingText.Text = "WATER CROSSING";
            MeasurementHeadingText.Foreground = accent;
            MeasurementPanel.BorderBrush = accent;
            if (_measurementActive)
            {
                MeasurementDetailText.Text = $"{view.HudLabel} · VERIFY IN GAME";
                MeasurementDetailText.Foreground = accent;
                MeasurementDetailText.ToolTip = view.Detail;
            }
        }

        if (_waterCrossingCheckActive && _measurementActive && _measurementDistance is not null)
        {
            var decisionKey = $"{Math.Round(_measurementDistance.Value, 1):0.0}:{view.Key}";
            if (!string.Equals(decisionKey, _waterCrossingLoggedDecisionKey, StringComparison.Ordinal))
            {
                _waterCrossingLoggedDecisionKey = decisionKey;
                AddTacticalEvent(
                    "CROSSING",
                    view.Heading,
                    $"{_measurementDistance:0.0} MU · {WaterCrossingLogic.ExposureLabel(_measurementDistance.Value)} map exposure",
                    warning: view.Severity >= 2);
            }
        }

        UpdateTacticalBrief();
        UpdateNextMove(force: true);

        WaterCrossingHeadingText.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0.35,
                1,
                TimeSpan.FromMilliseconds(160)));
    }

    private string WaterCrossingBriefLabel()
    {
        if (_streamerMode || !_waterCrossingCheckActive)
        {
            return string.Empty;
        }

        var view = CurrentWaterCrossingView();
        return _measurementActive && _measurementDistance is not null
            ? $"CROSSING {view.State.ToString().ToUpperInvariant()} {_measurementDistance:0} MU"
            : "CROSSING BANKS NEEDED";
    }

    private ShorelineCheckView CurrentShorelineCheckView(DateTimeOffset? now = null)
    {
        var current = now ?? DateTimeOffset.UtcNow;
        var incident = SurvivalAssistantLogic.Find(_survivalIncidentId);
        var vitals = CurrentCoreVitalsGuidance(current);
        var field = CurrentFieldConditionsGuidance(current);
        var liveSpecies = CurrentLiveSpeciesBridge(current);
        var speciesKnown = liveSpecies.Available
                           || _dietSpeciesIndex > 0 && _dietSpeciesIndex <= DietCoachLogic.Species.Length;
        return ShorelineCheckLogic.Evaluate(new ShorelineCheckSnapshot(
            _streamerMode,
            _shorelineCheckActive,
            _shorelineCheckStartedAt,
            current,
            LiveMapServicesActive,
            _markerAvailable
            && !_staleAlertActive
            && _currentSelfX is not null
            && _currentSelfY is not null
            && _currentMarkerFreshnessAgeMs <= 6000,
            incident?.Urgency ?? 0,
            incident?.Id ?? string.Empty,
            incident?.Label ?? string.Empty,
            vitals.Health,
            vitals.HealthFresh,
            vitals.Water,
            vitals.WaterFresh,
            vitals.Stamina,
            vitals.StaminaFresh,
            _encounterPlayerCount,
            _nearestEncounterDistance,
            _nearestEncounterCardinal,
            _nearestEncounterMotion,
            _nearestEncounterMotionSampleCount,
            !string.IsNullOrEmpty(_dangerAlertKey),
            _insideAlertZone,
            field.Weather,
            field.WeatherFresh,
            speciesKnown ? CurrentEffectiveSpeciesId(current) : string.Empty,
            speciesKnown));
    }

    private Brush ShorelineCheckAccent(ShorelineCheckState state) => state switch
    {
        ShorelineCheckState.Urgent or ShorelineCheckState.Hold =>
            new SolidColorBrush(Color.FromRgb(255, 112, 112)),
        ShorelineCheckState.Caution => (Brush)FindResource("WarningBrush"),
        ShorelineCheckState.Verify => (Brush)FindResource("AccentBrush"),
        ShorelineCheckState.Window => new SolidColorBrush(Color.FromRgb(110, 231, 183)),
        _ => (Brush)FindResource("SecondaryTextBrush")
    };

    private void UpdateShorelineCheck(bool force = false)
    {
        if (ShorelineCheckSectionAnchor is null
            || ShorelineCheckToggleButton is null
            || ShorelineCheckResultPanel is null
            || ShorelineCheckBadgeText is null
            || ShorelineCheckTimerText is null
            || ShorelineCheckHeadingText is null
            || ShorelineCheckDetailText is null
            || ShorelineCheckActionButton is null)
        {
            return;
        }

        var view = CurrentShorelineCheckView();
        var signature = string.Join('|',
            _shorelineCheckActive,
            view.State,
            view.Badge,
            view.Heading,
            view.Detail,
            view.ActionLabel,
            view.ActionId,
            view.Severity,
            view.RemainingSeconds);
        if (!force && string.Equals(signature, _shorelineCheckUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _shorelineCheckUiSignature = signature;

        var visible = view.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        ShorelineCheckSectionAnchor.Visibility = visible;
        ShorelineCheckToggleButton.Visibility = visible;
        if (!view.IsVisible)
        {
            ShorelineCheckResultPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var expired = _shorelineCheckActive && view.State == ShorelineCheckState.Off;
        ShorelineCheckToggleButton.Content = !_shorelineCheckActive || expired
            ? expired ? "CHECK AGAIN" : "CHECK THIS SHORELINE"
            : "CHECK ACTIVE · CLEAR";
        ShorelineCheckToggleButton.ToolTip = !_shorelineCheckActive || expired
            ? "Run a fresh 75-second drinking check from the current Isley evidence"
            : "End this session-only shoreline check";
        SetToggleButtonState(ShorelineCheckToggleButton, _shorelineCheckActive && !expired);

        ShorelineCheckResultPanel.Visibility = _shorelineCheckActive
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!_shorelineCheckActive)
        {
            return;
        }

        var accent = ShorelineCheckAccent(view.State);
        ShorelineCheckResultPanel.BorderBrush = accent;
        ShorelineCheckBadgeText.Text = view.Badge;
        ShorelineCheckBadgeText.Foreground = accent;
        ShorelineCheckTimerText.Text = view.RemainingSeconds > 0
            ? $"{view.RemainingSeconds / 60}:{view.RemainingSeconds % 60:00}"
            : "EXPIRED";
        ShorelineCheckTimerText.Foreground = accent;
        ShorelineCheckHeadingText.Text = view.Heading;
        ShorelineCheckHeadingText.Foreground = accent;
        ShorelineCheckDetailText.Text = view.Detail;
        ShorelineCheckActionButton.Content = view.ActionLabel;
        ShorelineCheckActionButton.Tag = view.ActionId;
        ShorelineCheckActionButton.IsEnabled = !string.IsNullOrEmpty(view.ActionId);
        ShorelineCheckActionButton.ToolTip = view.Detail;
        SetToggleButtonState(
            ShorelineCheckActionButton,
            view.State is ShorelineCheckState.Urgent or ShorelineCheckState.Hold or ShorelineCheckState.Caution);

        if (expired && !_shorelineCheckExpirationLogged)
        {
            _shorelineCheckExpirationLogged = true;
            AddTacticalEvent(
                "SHORELINE",
                "Shoreline check expired",
                "Run a fresh manual scan before relying on the old decision");
        }

        if (view.IsCurrent)
        {
            var decisionKey = $"{view.State}:{view.Heading}:{view.ActionId}";
            if (!string.Equals(decisionKey, _shorelineCheckLoggedDecisionKey, StringComparison.Ordinal))
            {
                _shorelineCheckLoggedDecisionKey = decisionKey;
                AddTacticalEvent(
                    "SHORELINE",
                    view.Heading,
                    $"{view.Badge} · {view.RemainingSeconds}s snapshot",
                    warning: view.Severity >= 2);
            }
        }

        UpdateTacticalBrief();
        UpdateNextMove(force: true);

        ShorelineCheckHeadingText.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0.4,
                1,
                TimeSpan.FromMilliseconds(160)));
    }

    private string ShorelineCheckBriefLabel()
    {
        if (_streamerMode || !_shorelineCheckActive)
        {
            return string.Empty;
        }

        return ShorelineCheckLogic.BriefLabel(CurrentShorelineCheckView());
    }

    private void ResetShorelineCheck(bool logEvent)
    {
        var wasActive = _shorelineCheckActive;
        _shorelineCheckActive = false;
        _shorelineCheckStartedAt = default;
        _shorelineCheckUiSignature = string.Empty;
        _shorelineCheckLoggedDecisionKey = string.Empty;
        _shorelineCheckExpirationLogged = false;
        if (wasActive && logEvent)
        {
            AddTacticalEvent("SHORELINE", "Shoreline check cleared", "Session-only scan ended");
        }
    }

    private async Task StartShorelineCheckAsync(bool openSection)
    {
        if (_streamerMode)
        {
            await ShowHotkeyToastAsync("SHORELINE CHECK HIDDEN IN STREAMER MODE", false);
            return;
        }

        _shorelineCheckActive = true;
        _shorelineCheckStartedAt = DateTimeOffset.UtcNow;
        _shorelineCheckUiSignature = string.Empty;
        _shorelineCheckLoggedDecisionKey = string.Empty;
        _shorelineCheckExpirationLogged = false;
        AddTacticalEvent(
            "SHORELINE",
            "Shoreline check started",
            "75-second manual snapshot · verify hidden threats and water in game");
        if (openSection)
        {
            OpenMapToolsAtSection("shoreline-check");
        }
        UpdateShorelineCheck(force: true);
        await ShowHotkeyToastAsync("SHORELINE CHECK · 1:15 LIVE", true);
    }

    private async void ShorelineCheckToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var current = CurrentShorelineCheckView();
        if (_shorelineCheckActive && current.IsCurrent)
        {
            ResetShorelineCheck(logEvent: true);
            UpdateShorelineCheck(force: true);
            UpdateTacticalBrief();
            UpdateNextMove(force: true);
            await ShowHotkeyToastAsync("SHORELINE CHECK CLEAR", true);
            return;
        }

        await StartShorelineCheckAsync(openSection: false);
    }

    private async void ShorelineCheckActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string actionId }
            || string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        if (string.Equals(actionId, "shoreline-check", StringComparison.Ordinal))
        {
            await StartShorelineCheckAsync(openSection: false);
            return;
        }

        if (string.Equals(actionId, "shoreline-check-clear", StringComparison.Ordinal))
        {
            ResetShorelineCheck(logEvent: true);
            UpdateShorelineCheck(force: true);
            UpdateTacticalBrief();
            UpdateNextMove(force: true);
            await ShowHotkeyToastAsync("SHORELINE CHECK COMPLETE · VERIFY IN GAME", true);
            return;
        }

        await ExecuteCommandPaletteActionAsync(actionId);
    }

    private void UpdateRecoveryControls()
    {
        RecoveryStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
        var alertDistance = _arrivalAlertDistances[_arrivalAlertIndex];
        ArrivalAlertButton.Content = alertDistance <= 0
            ? "Arrival alert off"
            : $"Arrival alert {alertDistance:0} MU";
        ArrivalAlertButton.ToolTip = alertDistance <= 0
            ? "Arrival alert is off; select to cycle distance"
            : $"One visual and sound alert per route within {alertDistance:0} map units";
        SetToggleButtonState(ArrivalAlertButton, alertDistance > 0);

        LastPositionMemoryButton.Content = _rememberLastPosition
            ? "Last-position memory on"
            : "Last-position memory off";
        LastPositionMemoryButton.ToolTip = _rememberLastPosition
            ? "The latest authorized live position is stored locally for recovery"
            : "No last live position is retained between mapper launches";
        SetToggleButtonState(LastPositionMemoryButton, _rememberLastPosition);

        var breadcrumbRouteActive = string.Equals(_routePlanSource, "breadcrumb", StringComparison.Ordinal)
                                    && (_routePlanActive || _routePlanComplete);
        BreadcrumbReturnButton.IsEnabled = !_streamerMode
                                           && (breadcrumbRouteActive || _breadcrumbReturnAvailable);
        BreadcrumbReturnButton.Content = breadcrumbRouteActive
            ? _routePlanComplete
                ? "Backtrack complete · clear"
                : $"Backtracking · {Math.Clamp(_routeCurrentIndex + 1, 1, Math.Max(1, _routeStopCount))}/{_routeStopCount}"
            : _breadcrumbReturnAvailable
                ? $"Retrace session path · {_breadcrumbDistance:0} MU"
                : "Retrace session path";
        BreadcrumbReturnButton.ToolTip = breadcrumbRouteActive
            ? "Clear the current breadcrumb return route"
            : _breadcrumbReturnAvailable
                ? $"Build a reverse route from {_breadcrumbPointCount} session-only path samples"
                : "Move farther to record enough authorized path for a safe reverse route";
        SetToggleButtonState(BreadcrumbReturnButton, breadcrumbRouteActive);

        var sessionStartRouteActive = _waypointActive
                                      && string.Equals(_waypointLabel, "Session start", StringComparison.Ordinal);
        RouteSessionStartButton.IsEnabled = !_streamerMode && (_sessionStartAvailable || sessionStartRouteActive);
        RouteSessionStartButton.Content = sessionStartRouteActive
            ? "Stop session-start route"
            : _sessionStartDistance is not null
                ? $"Return to session start · {_sessionStartDistance:0.0} MU {_sessionStartCardinal}"
                : "Return to session start";
        RouteSessionStartButton.ToolTip = _sessionStartBearing is not null
            ? $"Route to this session's first authorized position · {_sessionStartCardinal} {_sessionStartBearing:000}°"
            : "Route to this session's first authorized position";
        SetToggleButtonState(RouteSessionStartButton, sessionStartRouteActive);

        var lastPositionRouteActive = _waypointActive
                                      && string.Equals(_waypointLabel, "Last live position", StringComparison.Ordinal);
        RouteLastPositionButton.IsEnabled = !_streamerMode
                                            && (lastPositionRouteActive
                                                || (_rememberLastPosition
                                                    && _lastPositionAvailable
                                                    && !_markerAvailable));
        RouteLastPositionButton.Content = lastPositionRouteActive
            ? "Stop last-position route"
            : _markerAvailable && _rememberLastPosition
                ? "Last live position recording"
                : "Route to last live position";
        RouteLastPositionButton.ToolTip = _lastPositionAvailable
            ? $"Last authorized position recorded {FormatElapsedAge(_lastPositionAgeMs)} ago"
            : "No authorized position has been recorded on this PC";
        SetToggleButtonState(RouteLastPositionButton, lastPositionRouteActive);

        DeathMarkerButton.IsEnabled = !_streamerMode;
        DeathMarkerButton.Content = _markerAvailable
            ? "Mark current position as death"
            : _lastPositionAvailable && _rememberLastPosition
                ? "Mark last live position as death"
                : "Mark latest position as death";
        var deathActionRecent = !string.IsNullOrWhiteSpace(_deathMarkerActionStatus)
                                && DateTimeOffset.UtcNow - _deathMarkerActionAt < TimeSpan.FromSeconds(6);
        DeathMarkerButton.ToolTip = deathActionRecent
            ? $"{_deathMarkerActionStatus} · attempt {_deathMarkerAttemptCount}"
            : _markerAvailable
                ? "Save a Death marker at your current authorized position, replacing the previous Death marker"
                : _lastPositionAvailable && _rememberLastPosition
                    ? $"Save a Death marker at the authorized position recorded {FormatElapsedAge(_lastPositionAgeMs)} ago"
                    : "A current or locally remembered authorized position is required";
        if (_streamerMode)
        {
            RecoveryStatusText.Text = "Recovery locations hidden in streamer mode";
        }
        else if (deathActionRecent)
        {
            RecoveryStatusText.Text = _deathMarkerActionStatus;
            RecoveryStatusText.Foreground = _lastDeathMarkerAttemptSucceeded is true
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("WarningBrush");
        }
        else if (breadcrumbRouteActive)
        {
            RecoveryStatusText.Text = _routePlanComplete
                ? "Breadcrumb return complete · the recorded session path remains local to this run"
                : $"Retracing {_breadcrumbDistance:0} MU across {_routeStopCount} simplified stops";
        }
        else if (_markerAvailable && _breadcrumbReturnAvailable)
        {
            RecoveryStatusText.Text = $"Breadcrumb return ready · {_breadcrumbDistance:0} MU recorded for this run";
        }
        else if (!_rememberLastPosition)
        {
            RecoveryStatusText.Text = "Persistent last-position memory is off · session start remains local to this run";
        }
        else if (_markerAvailable)
        {
            RecoveryStatusText.Text = "Recording your latest authorized position locally";
        }
        else if (_lastPositionAvailable)
        {
            RecoveryStatusText.Text = $"Player offline · last live position saved {FormatElapsedAge(_lastPositionAgeMs)} ago";
        }
        else
        {
            RecoveryStatusText.Text = "Waiting for an authorized live position";
        }
    }

    private static bool ShouldOfferRecoveryPrompt(
        bool markerWasAvailable,
        bool markerAvailable,
        bool lastPositionAvailable,
        bool promptDismissed,
        bool streamerMode) =>
        markerWasAvailable
        && !markerAvailable
        && lastPositionAvailable
        && !promptDismissed
        && !streamerMode;

    private void UpdateRecoveryPrompt(bool markerWasAvailable, bool markerAvailable)
    {
        if (RecoveryPromptBorder is null)
        {
            return;
        }

        if (markerAvailable)
        {
            var recovered = _markerLostAt is not null;
            _recoveryPromptRevision++;
            _recoveryPromptPending = false;
            _recoveryPromptDismissed = false;
            _markerLostAt = null;
            HideRecoveryPrompt();
            if (recovered)
            {
                AddTacticalEvent("RECOVERY", "Player marker recovered", "Authorized live position resumed");
            }
            return;
        }

        if (markerWasAvailable)
        {
            _recoveryPromptPending = true;
            _recoveryPromptDismissed = false;
            _markerLostAt = DateTimeOffset.UtcNow;
            AddTacticalEvent(
                "RECOVERY",
                "Player marker lost",
                "Authorized live position stopped updating",
                warning: true);
            var revision = ++_recoveryPromptRevision;
            HideRecoveryPrompt();
            _ = ConfirmRecoveryPromptAsync(revision);
            return;
        }

        if (_streamerMode || _recoveryPromptDismissed || !_rememberLastPosition)
        {
            HideRecoveryPrompt();
            return;
        }

        if (_markerLostAt is not null
            && DateTimeOffset.UtcNow - _markerLostAt.Value > TimeSpan.FromSeconds(30))
        {
            _recoveryPromptPending = false;
        }

        if (_recoveryPromptVisible)
        {
            RecoveryPromptDetailText.Text = GetRecoveryPromptDetail();
            return;
        }

    }

    private async Task ConfirmRecoveryPromptAsync(int revision)
    {
        await Task.Delay(1400);
        if (!IsLoaded
            || revision != _recoveryPromptRevision
            || !_recoveryPromptPending
            || !_rememberLastPosition
            || !ShouldOfferRecoveryPrompt(
                markerWasAvailable: true,
                markerAvailable: _markerAvailable,
                lastPositionAvailable: _lastPositionAvailable,
                promptDismissed: _recoveryPromptDismissed,
                streamerMode: _streamerMode))
        {
            return;
        }

        ShowRecoveryPrompt();
    }

    private void ShowRecoveryPrompt()
    {
        _recoveryPromptVisible = true;
        RecoveryPromptDetailText.Text = GetRecoveryPromptDetail();
        RecoveryPromptSaveButton.Content = "MARK DEATH";
        if (!HudSurfaceLogic.Show(_alertHudVisible, _streamerMode))
        {
            RecoveryPromptBorder.Visibility = Visibility.Collapsed;
            return;
        }
        RecoveryPromptBorder.BeginAnimation(OpacityProperty, null);
        RecoveryPromptTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        RecoveryPromptBorder.Visibility = Visibility.Visible;
        RecoveryPromptBorder.Opacity = 0;
        RecoveryPromptTranslate.Y = 10;
        HelpTipBorder.Visibility = Visibility.Collapsed;
        RecoveryPromptBorder.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0,
                1,
                TimeSpan.FromMilliseconds(170)));
        RecoveryPromptTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                10,
                0,
                TimeSpan.FromMilliseconds(190)));
    }

    private void HideRecoveryPrompt()
    {
        _recoveryPromptVisible = false;
        RecoveryPromptBorder.BeginAnimation(OpacityProperty, null);
        RecoveryPromptTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        RecoveryPromptBorder.Opacity = 1;
        RecoveryPromptTranslate.Y = 0;
        RecoveryPromptBorder.Visibility = Visibility.Collapsed;
    }

    private async void RecoveryPromptSaveButton_Click(object sender, RoutedEventArgs e)
    {
        RecoveryPromptSaveButton.Content = "SAVING...";
        var saved = await DropDeathMarkerAsync();
        if (saved)
        {
            _recoveryPromptPending = false;
            _recoveryPromptDismissed = true;
            HideRecoveryPrompt();
            return;
        }

        RecoveryPromptSaveButton.Content = "TRY AGAIN";
        RecoveryPromptDetailText.Text = "The authorized recovery point is unavailable. No marker was created.";
    }

    private async void RecoveryPromptRouteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || !LiveMapServicesActive || !_followControllerInstalled)
        {
            await ShowHotkeyToastAsync("LIVE MAP MODE REQUIRED FOR RECOVERY COURSE", false);
            return;
        }

        RecoveryPromptRouteButton.Content = "ROUTING...";
        var routed = await ExecuteMapperCommandAsync(
            "window.__isley?.routeToNearestPinType('death') ?? false");
        RecoveryPromptRouteButton.Content = "ROUTE TO BODY";
        if (!routed)
        {
            await ShowHotkeyToastAsync("NO DEATH MARKER TO ROUTE", false);
            return;
        }

        AddTacticalEvent(
            "RECOVERY",
            "Route to body started",
            "Road/trail course to nearest Death marker");
        HideRecoveryPrompt();
        await ShowHotkeyToastAsync("RECOVERY COURSE ACTIVE", true);
    }

    private void RecoveryPromptDismissButton_Click(object sender, RoutedEventArgs e)
    {
        _recoveryPromptRevision++;
        _recoveryPromptPending = false;
        _recoveryPromptDismissed = true;
        HideRecoveryPrompt();
    }

    private string GetRecoveryPromptDetail()
    {
        var ageText = _lastPositionAgeMs < 1000
            ? "just now"
            : $"{FormatElapsedAge(_lastPositionAgeMs)} ago";
        return $"Last authorized point {ageText} · save only if this was a death.";
    }

    private RecoveryMonitorView CurrentRecoveryMonitorView(string incidentId, DateTimeOffset now)
    {
        if (!string.Equals(_recoveryMonitorIncidentId, incidentId, StringComparison.Ordinal))
        {
            ResetRecoveryMonitorState();
            _recoveryMonitorIncidentId = incidentId;
        }

        var previousState = _recoveryMonitorState;
        var view = RecoveryMonitorLogic.Evaluate(new RecoveryMonitorSnapshot(
            incidentId,
            _streamerMode,
            LiveMapServicesActive,
            _markerAvailable
            && !_staleAlertActive
            && _currentSelfX is not null
            && _currentSelfY is not null
            && _currentMarkerFreshnessAgeMs <= 6000,
            _currentSelfSpeed,
            _recoveryMonitorStillSince,
            now));
        _recoveryMonitorStillSince = view.StillSince;
        _recoveryMonitorState = view.State;
        _recoveryMonitorRestSeconds = view.RestSeconds;
        _recoveryMonitorPriorityOverride = view.PriorityOverride;

        if (view.State == RecoveryMovementState.Resting
            && view.RestSeconds >= RecoveryMonitorLogic.QualifiedRestSeconds)
        {
            _recoveryMonitorRestQualified = true;
        }
        else if (view.State == RecoveryMovementState.Moving && _recoveryMonitorRestQualified)
        {
            _recoveryMonitorRestQualified = false;
            AddTacticalEvent(
                "RECOVERY",
                "Movement resumed during recovery",
                $"{SurvivalAssistantLogic.Find(incidentId)?.Label ?? "Recovery"} · rest streak interrupted",
                warning: true);
            _ = ShowHotkeyToastAsync("RECOVERY · MOVEMENT RESUMED", false);
        }
        else if (view.State is RecoveryMovementState.Hidden
                 or RecoveryMovementState.Manual
                 or RecoveryMovementState.Waiting)
        {
            _recoveryMonitorRestQualified = false;
        }

        if (previousState != view.State)
        {
            UpdateTacticalBrief();
        }
        return view;
    }

    private void ResetRecoveryMonitorState()
    {
        _recoveryMonitorIncidentId = string.Empty;
        _recoveryMonitorStillSince = null;
        _recoveryMonitorState = RecoveryMovementState.Hidden;
        _recoveryMonitorRestSeconds = 0;
        _recoveryMonitorPriorityOverride = string.Empty;
        _recoveryMonitorRestQualified = false;
    }

    private void UpdateSurvivalAssistant(bool force = false)
    {
        if (SurvivalAssistantStatusText is null
            || SurvivalIncidentHudBorder is null
            || SurvivalQuickButton is null
            || SurvivalAssistantResolvedButton is null)
        {
            return;
        }

        var incident = SurvivalAssistantLogic.Find(_survivalIncidentId);
        var now = DateTimeOffset.UtcNow;
        var vomitHotkeyBinding = CurrentHotkeyBinding(HotkeyBindingLogic.VomitRecoveryId);
        var vomitHotkeyLabel = vomitHotkeyBinding.Enabled
            ? HotkeyBindingLogic.Format(vomitHotkeyBinding)
            : string.Empty;
        var recoveryMonitor = CurrentRecoveryMonitorView(incident?.Id ?? string.Empty, now);
        var recoveryVitalsTrend = CurrentVitalsTrendAnalysis(now);
        var recoveryHasHealthEvidence =
            recoveryMonitor.State == RecoveryMovementState.Resting
            && recoveryVitalsTrend.Health.Ready;
        var recoveryMonitorLabel = recoveryMonitor.Label +
            (recoveryHasHealthEvidence && recoveryVitalsTrend.Health.Rising
                ? " · HP ↑"
                : string.Empty);
        var recoveryMonitorDetail = recoveryMonitor.Detail +
            (recoveryHasHealthEvidence
                ? $" {VitalsTrendLogic.HealthRecoveryDetail(recoveryVitalsTrend.Health)}"
                : string.Empty);
        var remedy = SurvivalAssistantLogic.ResolveRecoveryRemedy(
            incident?.Id,
            LiveMapServicesActive,
            _lifeRunActive);
        var responsivePresentation = CurrentResponsiveOverlayPresentation();
        var remaining = incident is { } active
            ? SurvivalAssistantLogic.RemainingSeconds(
                active,
                _survivalIncidentStartedAt,
                now,
                _survivalIncidentAdditionalSeconds)
            : 0;
        var quickAction = SurvivalAssistantLogic.QuickAction(
            incident,
            _survivalIncidentStartedAt,
            now,
            _survivalIncidentAdditionalSeconds);
        var signature = $"{_survivalIncidentId}:{_survivalIncidentPickerOpen}:{remaining}:" +
                        $"{_survivalIncidentAdditionalSeconds}:{_survivalIncidentHudCollapsed}:" +
                         $"{_streamerMode}:{vomitHotkeyLabel}:{recoveryMonitor.State}:" +
                         $"{recoveryMonitor.RestSeconds}:{recoveryMonitorLabel}:" +
                         $"{recoveryVitalsTrend.Health.Direction}:{recoveryVitalsTrend.Health.RatePerMinute:0.###}:" +
                         $"{recoveryVitalsTrend.Health.MinutesToBoundary}:" +
                        $"{remedy.Kind}:{remedy.Target}:{remedy.ActionLabel}:" +
                        $"quick={quickAction.Kind}:{quickAction.Label}:" +
                        $"micro={responsivePresentation.IsMicroLayout}";
        if (!force && string.Equals(signature, _survivalIncidentUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _survivalIncidentUiSignature = signature;

        if (_streamerMode)
        {
            SurvivalAssistantStatusText.Text = "Hidden in streamer mode";
            SurvivalVomitStartButton.Visibility = Visibility.Collapsed;
            SurvivalAssistantReportButton.Visibility = Visibility.Collapsed;
            SurvivalAssistantPickerPanel.Visibility = Visibility.Collapsed;
            SurvivalAssistantActivePanel.Visibility = Visibility.Collapsed;
            SurvivalIncidentHudBorder.Visibility = Visibility.Collapsed;
            SurvivalQuickButton.Visibility = Visibility.Collapsed;
            SurvivalIncidentHudVomitAgainButton.Visibility = Visibility.Collapsed;
            SurvivalRecoveryMonitorHudText.Visibility = Visibility.Collapsed;
            SurvivalRecoveryMonitorText.Visibility = Visibility.Collapsed;
            SurvivalRecoveryMonitorDetailText.Visibility = Visibility.Collapsed;
            UpdateSurvivalFinalMinutePulse(false);
            return;
        }

        var hasIncident = incident is not null;
        var hudPresentation = SurvivalAssistantLogic.HudPresentation(
            _survivalIncidentId,
            _survivalIncidentHudCollapsed);
        _survivalIncidentHudCollapsed = hudPresentation.IsCollapsed;
        SurvivalQuickButton.Visibility = Visibility.Visible;
        SurvivalVomitStartButton.Visibility = !hasIncident && !_survivalIncidentPickerOpen
            ? Visibility.Visible
            : Visibility.Collapsed;
        SurvivalAssistantReportButton.Visibility = !hasIncident && !_survivalIncidentPickerOpen
            ? Visibility.Visible
            : Visibility.Collapsed;
        SurvivalAssistantPickerPanel.Visibility = _survivalIncidentPickerOpen
            ? Visibility.Visible
            : Visibility.Collapsed;
        SurvivalAssistantActivePanel.Visibility = hasIncident && !_survivalIncidentPickerOpen
            ? Visibility.Visible
            : Visibility.Collapsed;
        SurvivalIncidentHudBorder.Visibility = hasIncident ? Visibility.Visible : Visibility.Collapsed;
        SurvivalIncidentHudDetailsPanel.Visibility = responsivePresentation.ShowSurvivalDetails
            ? Visibility.Visible
            : Visibility.Collapsed;
        SurvivalIncidentHudDetailButton.Content = responsivePresentation.SurvivalDetailAction;
        SurvivalIncidentHudDetailButton.ToolTip = responsivePresentation.SurvivalDetailTooltip;
        SurvivalIncidentHudBorder.Padding = !responsivePresentation.ShowSurvivalDetails
            ? new Thickness(9, 5, 9, 5)
            : new Thickness(9, 7, 9, 7);

        if (incident is not { } selected)
        {
            SurvivalQuickButton.Content = quickAction.Label;
            SurvivalQuickButton.ToolTip = quickAction.Tooltip +
                (string.IsNullOrEmpty(vomitHotkeyLabel) ? string.Empty : $", or press {vomitHotkeyLabel}");
            System.Windows.Automation.AutomationProperties.SetName(
                SurvivalQuickButton,
                quickAction.AutomationName);
            SurvivalQuickButton.Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 194));
            SurvivalVomitAgainButton.Visibility = Visibility.Collapsed;
            SurvivalIncidentHudVomitAgainButton.Visibility = Visibility.Collapsed;
            SurvivalRecoveryMonitorHudText.Visibility = Visibility.Collapsed;
            SurvivalRecoveryMonitorText.Visibility = Visibility.Collapsed;
            SurvivalRecoveryMonitorDetailText.Visibility = Visibility.Collapsed;
            SurvivalIncidentHudProgressTransform.ScaleX = 0;
            UpdateSurvivalFinalMinutePulse(false);
            SurvivalAssistantStatusText.Text = _survivalIncidentPickerOpen
                ? "Select the condition shown in game. One problem at a time keeps this fast."
                : "No active problem - ready for quick triage.";
            SurvivalAssistantStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            return;
        }

        var timerText = selected.ExpectedSeconds <= 0
            ? string.Empty
            : remaining > 0
                ? $"EST {SurvivalAssistantLogic.FormatRemaining(remaining)}"
                : "EST COMPLETE";
        var hudTimerText = responsivePresentation.IsMicroLayout
            ? selected.ExpectedSeconds <= 0
                ? string.Empty
                : remaining > 0
                    ? SurvivalAssistantLogic.FormatRemaining(remaining)
                    : "CHECK"
            : timerText;
        var criticalBrush = new SolidColorBrush(selected.Urgency >= 3
            ? Color.FromRgb(255, 112, 112)
            : Color.FromRgb(255, 163, 108));
        var stackedVomitCount = selected.Id == "vomit"
            ? _survivalIncidentAdditionalSeconds / SurvivalAssistantLogic.VomitStackSeconds
            : 0;
        var incidentPresentation = SurvivalAssistantLogic.Presentation(
            selected,
            _survivalIncidentStartedAt,
            now,
            _survivalIncidentAdditionalSeconds);
        var effectivePriority = string.IsNullOrEmpty(recoveryMonitor.PriorityOverride)
            ? incidentPresentation.Priority
            : recoveryMonitor.PriorityOverride;
        var compactSummary = SurvivalAssistantLogic.CompactSummary(
            selected,
            _survivalIncidentStartedAt,
            now,
            _survivalIncidentAdditionalSeconds);
        if (!string.Equals(effectivePriority, selected.Priority, StringComparison.Ordinal))
        {
            compactSummary = compactSummary.Replace(
                selected.Priority,
                effectivePriority,
                StringComparison.Ordinal);
        }
        SurvivalQuickButton.Content = quickAction.Label;
        SurvivalQuickButton.ToolTip = quickAction.Tooltip +
            (string.IsNullOrEmpty(vomitHotkeyLabel)
                ? string.Empty
                : $"; press {vomitHotkeyLabel} to open the active instructions");
        System.Windows.Automation.AutomationProperties.SetName(
            SurvivalQuickButton,
            quickAction.AutomationName);
        SurvivalQuickButton.Foreground = criticalBrush;
        SurvivalAssistantStatusText.Text = _survivalIncidentPickerOpen
            ? $"{selected.Label} remains active until you choose a replacement."
            : compactSummary;
        var incidentBrush = incidentPresentation.RequiresGameCheck
            ? (Brush)FindResource("AccentBrush")
            : criticalBrush;
        var incidentStateLabel = incidentPresentation.RequiresGameCheck
            ? "CHECK"
            : selected.Urgency >= 3
                ? "CRITICAL"
                : "HIGH";
        SurvivalAssistantStatusText.Foreground = incidentBrush;
        SurvivalIncidentLabelText.Text = $"{incidentStateLabel} - {selected.Label.ToUpperInvariant()}";
        SurvivalIncidentLabelText.Foreground = incidentBrush;
        SurvivalIncidentPriorityText.Text = effectivePriority;
        SurvivalIncidentStepsText.Text = string.Join(
            "\n",
            incidentPresentation.Steps.Select((step, index) => $"{index + 1}. {step}"));
        SurvivalIncidentTimerText.Text = timerText;
        SurvivalIncidentNoteText.Text = incidentPresentation.RequiresGameCheck
            ? $"{selected.Note} STOP EATING is no longer shown until you reconfirm the warning visible in game."
            : selected.Note;
        SurvivalRecoveryButton.Content = remedy.ActionLabel;
        SurvivalRecoveryButton.ToolTip = remedy.Tooltip;
        SurvivalRestartTimerButton.Visibility = selected.ExpectedSeconds > 0 && selected.Id != "vomit"
            ? Visibility.Visible
            : Visibility.Collapsed;
        SurvivalVomitAgainButton.Visibility = selected.Id == "vomit"
            ? Visibility.Visible
            : Visibility.Collapsed;
        SurvivalIncidentHudVomitAgainButton.Visibility = selected.Id == "vomit"
            ? Visibility.Visible
            : Visibility.Collapsed;
        SurvivalVomitAgainButton.Content = incidentPresentation.RequiresGameCheck
            ? "IN-GAME WARNING STILL ON · +5:00"
            : "VOMITED AGAIN · +5:00";
        SurvivalVomitAgainButton.ToolTip = incidentPresentation.RequiresGameCheck
            ? "Use only if the game's Vomit sickness warning is still visible"
            : "Report another vomit and add five minutes to the recovery estimate";
        SurvivalIncidentHudVomitAgainButton.ToolTip = SurvivalVomitAgainButton.ToolTip;
        System.Windows.Automation.AutomationProperties.SetName(
            SurvivalIncidentHudVomitAgainButton,
            incidentPresentation.RequiresGameCheck
                ? "Confirm the in-game Vomit sickness warning remains and add five minutes"
                : "Report another vomit and add five minutes");
        SurvivalAssistantResolvedButton.Content = incidentPresentation.RequiresGameCheck
            ? "IN-GAME WARNING CLEARED"
            : "RESOLVED";
        SurvivalAssistantResolvedButton.ToolTip = incidentPresentation.RequiresGameCheck
            ? "Use only after the related warning has disappeared in the game"
            : "Clear this reported condition and hide its HUD";

        SurvivalIncidentHudLabelText.Text = responsivePresentation.IsMicroLayout
            ? selected.ShortLabel.ToUpperInvariant()
            : $"{(incidentPresentation.RequiresGameCheck ? "CHECK" : selected.Urgency >= 3 ? "CRITICAL" : "SURVIVAL")} - {selected.ShortLabel}";
        SurvivalIncidentHudLabelText.Foreground = incidentBrush;
        SurvivalIncidentHudPriorityText.Text = effectivePriority;
        SurvivalIncidentHudActionText.Text = incidentPresentation.HudSteps.ToUpperInvariant();
        SurvivalIncidentHudTimerText.Text = hudTimerText;
        SurvivalIncidentHudBorder.BorderBrush = incidentBrush;
        SurvivalIncidentHudProgressFill.Background = incidentBrush;
        SurvivalIncidentHudProgressTransform.ScaleX = SurvivalAssistantLogic.RemainingRatio(
            selected,
            _survivalIncidentStartedAt,
            now,
            _survivalIncidentAdditionalSeconds);
        SurvivalIncidentHudEstimateText.Text = incidentPresentation.RequiresGameCheck
            ? "ESTIMATE COMPLETE · CHECK THE GAME"
            : stackedVomitCount > 0
            ? $"BASE 5:00 · +{stackedVomitCount} VOMIT · VERIFY IN GAME"
            : "ESTIMATE · VERIFY THE IN-GAME WARNING";
        var recoveryMonitorVisibility = recoveryMonitor.IsVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        var recoveryMonitorAccent = recoveryMonitor.State switch
        {
            RecoveryMovementState.Moving => criticalBrush,
            RecoveryMovementState.Resting => new SolidColorBrush(Color.FromRgb(110, 231, 183)),
            RecoveryMovementState.Settling => (Brush)FindResource("AccentBrush"),
            _ => (Brush)FindResource("SecondaryTextBrush")
        };
        SurvivalRecoveryMonitorHudText.Visibility = recoveryMonitorVisibility;
        SurvivalRecoveryMonitorHudText.Text = recoveryMonitorLabel;
        SurvivalRecoveryMonitorHudText.Foreground = recoveryMonitorAccent;
        SurvivalRecoveryMonitorHudText.ToolTip = recoveryMonitorDetail;
        SurvivalRecoveryMonitorText.Visibility = recoveryMonitorVisibility;
        SurvivalRecoveryMonitorText.Text = recoveryMonitorLabel;
        SurvivalRecoveryMonitorText.Foreground = recoveryMonitorAccent;
        SurvivalRecoveryMonitorDetailText.Visibility = recoveryMonitorVisibility;
        SurvivalRecoveryMonitorDetailText.Text = recoveryMonitorDetail;
        UpdateSurvivalFinalMinutePulse(SurvivalAssistantLogic.IsFinalMinute(remaining));

        if (selected.ExpectedSeconds > 0
            && remaining == 0
            && !_survivalIncidentEstimateCompletionAnnounced)
        {
            _survivalIncidentEstimateCompletionAnnounced = true;
            AddTacticalEvent(
                "SURVIVAL",
                $"{selected.Label} estimate complete",
                "Verify the in-game warning before marking resolved");
            if (_timerSoundEnabled) SystemSounds.Exclamation.Play();
            _ = ShowHotkeyToastAsync(
                $"{selected.ShortLabel} ESTIMATE COMPLETE · CHECK IN GAME",
                true);
        }
    }

    private void UpdateSurvivalFinalMinutePulse(bool shouldPulse)
    {
        shouldPulse = shouldPulse && !_liteModeEnabled;
        if (_survivalIncidentFinalMinutePulsing == shouldPulse
            || SurvivalIncidentHudBorder is null)
        {
            return;
        }

        _survivalIncidentFinalMinutePulsing = shouldPulse;
        if (!shouldPulse)
        {
            SurvivalIncidentHudBorder.BeginAnimation(OpacityProperty, null);
            SurvivalIncidentHudBorder.Opacity = 1;
            return;
        }

        SurvivalIncidentHudBorder.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0.72,
                1,
                TimeSpan.FromMilliseconds(620))
            {
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            });
    }

    private FieldConditionsGuidance CurrentFieldConditionsGuidance(DateTimeOffset? now = null)
    {
        var speciesId = _dietSpeciesIndex > 0 && _dietSpeciesIndex <= DietCoachLogic.Species.Length
            ? DietCoachLogic.Species[_dietSpeciesIndex - 1].Id
            : _guideSelectedSpeciesId;
        var activeMutations = _mutationLoadout
            .Where(item => item.Status is 1 or 2)
            .Select(item => item.MutationId)
            .ToArray();
        return FieldConditionsLogic.Evaluate(new FieldConditionsSnapshot(
            _fieldWeather,
            _fieldWeatherReportedAt,
            _fieldLight,
            _fieldLightReportedAt,
            now ?? DateTimeOffset.UtcNow,
            activeMutations,
            speciesId));
    }

    private void UpdateFieldConditions(bool force = false)
    {
        if (FieldConditionsStatusText is null
            || FieldWeatherButton is null
            || FieldLightButton is null
            || FieldConditionsHudBorder is null)
        {
            return;
        }

        var guidance = CurrentFieldConditionsGuidance();
        var signature = string.Join('|',
            _streamerMode,
            _fieldWeather,
            _fieldLight,
            guidance.WeatherFresh,
            guidance.LightFresh,
            guidance.WeatherAgeSeconds,
            guidance.LightAgeSeconds,
            guidance.Heading,
            guidance.Action,
            guidance.MutationWindow,
            LiveMapServicesActive);
        if (!force && string.Equals(signature, _fieldConditionsUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _fieldConditionsUiSignature = signature;

        if (_streamerMode)
        {
            FieldConditionsStatusText.Text = "Hidden in streamer mode";
            FieldWeatherButton.Visibility = Visibility.Collapsed;
            FieldLightButton.Visibility = Visibility.Collapsed;
            FieldConditionsActionText.Visibility = Visibility.Collapsed;
            FieldConditionsDetailText.Visibility = Visibility.Collapsed;
            FieldConditionsMutationText.Visibility = Visibility.Collapsed;
            FieldConditionsFreshnessText.Visibility = Visibility.Collapsed;
            FieldConditionsMatchMapButton.Visibility = Visibility.Collapsed;
            FieldConditionsClearButton.Visibility = Visibility.Collapsed;
            FieldConditionsHudBorder.Visibility = Visibility.Collapsed;
            return;
        }

        FieldWeatherButton.Visibility = Visibility.Visible;
        FieldLightButton.Visibility = Visibility.Visible;
        FieldConditionsActionText.Visibility = Visibility.Visible;
        FieldConditionsDetailText.Visibility = Visibility.Visible;
        FieldConditionsFreshnessText.Visibility = Visibility.Visible;
        FieldConditionsMatchMapButton.Visibility = Visibility.Visible;
        FieldConditionsClearButton.Visibility = Visibility.Visible;

        var warningBrush = guidance.Warning
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("AccentBrush");
        FieldConditionsStatusText.Text = guidance.Heading;
        FieldConditionsStatusText.Foreground = warningBrush;
        FieldConditionsActionText.Text = guidance.Action;
        FieldConditionsActionText.Foreground = warningBrush;
        FieldConditionsDetailText.Text = guidance.Detail;
        FieldConditionsFreshnessText.Text = guidance.Freshness;
        FieldConditionsMutationText.Text = guidance.MutationWindow;
        FieldConditionsMutationText.Visibility = string.IsNullOrEmpty(guidance.MutationWindow)
            ? Visibility.Collapsed
            : Visibility.Visible;

        FieldWeatherButton.Content = $"WEATHER - {FieldConditionsLogic.WeatherLabel(_fieldWeather)}";
        FieldLightButton.Content = $"LIGHT - {FieldConditionsLogic.LightLabel(_fieldLight)}";
        FieldWeatherButton.ToolTip = guidance.WeatherFresh
            ? $"Reported {FieldConditionsLogic.FormatAge(guidance.WeatherAgeSeconds)} ago; click for the next condition"
            : "Cycle Unknown, Clear, Rain, Storm, and Fog; each selection is timestamped";
        FieldLightButton.ToolTip = guidance.LightFresh
            ? $"Reported {FieldConditionsLogic.FormatAge(guidance.LightAgeSeconds)} ago; click for the next light phase"
            : "Cycle Unknown, Day, Dusk, Night, and Dawn; each selection is timestamped";
        SetToggleButtonState(FieldWeatherButton, guidance.WeatherFresh);
        SetToggleButtonState(FieldLightButton, guidance.LightFresh);

        FieldConditionsClearButton.IsEnabled = _fieldWeather != FieldWeather.Unknown
                                               || _fieldLight != FieldLight.Unknown;
        FieldConditionsMatchMapButton.IsEnabled = LiveMapServicesActive && guidance.LightFresh;
        FieldConditionsMatchMapButton.Content = guidance.LightFresh
            ? $"MAP - {FieldConditionsLogic.LightLabel(guidance.Light)}"
            : "MATCH MAP";
        FieldConditionsMatchMapButton.ToolTip = !LiveMapServicesActive
            ? "Map-light matching is available in Live Map mode"
            : guidance.LightFresh
                ? "Apply the matching local Day, Dim, or Night map comfort treatment"
                : "Report the current light phase first";

        var shouldShowHud = guidance.ShowHud
                            && (guidance.Warning
                                || (_hudDetailModeIndex < 2
                                    && !CurrentHudPriorityPresentation().HideAmbientHud));
        var wasHudVisible = FieldConditionsHudBorder.Visibility == Visibility.Visible;
        FieldConditionsHudBorder.Visibility = shouldShowHud ? Visibility.Visible : Visibility.Collapsed;
        if (shouldShowHud)
        {
            var ages = new List<int>();
            if (guidance.WeatherFresh) ages.Add(guidance.WeatherAgeSeconds);
            if (guidance.LightFresh) ages.Add(guidance.LightAgeSeconds);
            var oldestAge = ages.Count == 0 ? 0 : ages.Max();
            FieldConditionsHudHeadingText.Text = guidance.Heading;
            FieldConditionsHudHeadingText.Foreground = warningBrush;
            FieldConditionsHudAgeText.Text = FieldConditionsLogic.FormatAge(oldestAge).ToUpperInvariant();
            FieldConditionsHudActionText.Text = guidance.Action;
            FieldConditionsHudActionText.Foreground = warningBrush;
            FieldConditionsHudMutationText.Text = guidance.MutationWindow;
            FieldConditionsHudMutationText.Visibility = string.IsNullOrEmpty(guidance.MutationWindow)
                ? Visibility.Collapsed
                : Visibility.Visible;
            FieldConditionsHudBorder.BorderBrush = warningBrush;
            if (!wasHudVisible)
            {
                FieldConditionsHudBorder.Opacity = 0;
                FieldConditionsHudBorder.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(160),
                        EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                        {
                            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                        }
                    });
            }
        }

        var decisionSignature = string.Join('|',
            guidance.HasFreshReport,
            guidance.Warning,
            guidance.ShowHud,
            guidance.Heading,
            guidance.Action,
            guidance.MutationWindow);
        if (!string.Equals(decisionSignature, _fieldConditionsDecisionSignature, StringComparison.Ordinal))
        {
            _fieldConditionsDecisionSignature = decisionSignature;
            _nextMoveUiSignature = string.Empty;
            UpdateNextMove(force: true);
            UpdateTripReadiness(force: true);
            UpdateTacticalBrief();
        }
    }

    private void FieldWeatherButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode) return;
        _fieldWeather = FieldConditionsLogic.NextWeather(_fieldWeather);
        _fieldWeatherReportedAt = _fieldWeather == FieldWeather.Unknown
            ? default
            : DateTimeOffset.UtcNow;
        _fieldConditionsUiSignature = string.Empty;
        AddTacticalEvent(
            "FIELD",
            _fieldWeather == FieldWeather.Unknown ? "Weather report cleared" : "Weather reported",
            _fieldWeather == FieldWeather.Unknown
                ? "Session-only weather state removed"
                : FieldConditionsLogic.WeatherLabel(_fieldWeather),
            warning: _fieldWeather is FieldWeather.Storm or FieldWeather.Fog);
        UpdateFieldConditions(force: true);
    }

    private void FieldLightButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode) return;
        _fieldLight = FieldConditionsLogic.NextLight(_fieldLight);
        _fieldLightReportedAt = _fieldLight == FieldLight.Unknown
            ? default
            : DateTimeOffset.UtcNow;
        _fieldConditionsUiSignature = string.Empty;
        AddTacticalEvent(
            "FIELD",
            _fieldLight == FieldLight.Unknown ? "Light report cleared" : "Light phase reported",
            _fieldLight == FieldLight.Unknown
                ? "Session-only light state removed"
                : FieldConditionsLogic.LightLabel(_fieldLight));
        UpdateFieldConditions(force: true);
    }

    private async void FieldConditionsMatchMapButton_Click(object sender, RoutedEventArgs e)
    {
        var guidance = CurrentFieldConditionsGuidance();
        if (_streamerMode || !LiveMapServicesActive || !guidance.LightFresh) return;
        _mapLightModeIndex = guidance.Light switch
        {
            FieldLight.Night => 2,
            FieldLight.Dusk or FieldLight.Dawn => 1,
            _ => 0
        };
        UpdateMapLightMode(animate: true);
        SaveSettings();
        await ShowHotkeyToastAsync(
            $"MAP LIGHT - {_mapLightModeLabels[_mapLightModeIndex].ToUpperInvariant()}",
            true);
    }

    private void FieldConditionsClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode) return;
        ClearFieldConditions(logEvent: true, updateUi: true);
    }

    private void ClearFieldConditions(bool logEvent, bool updateUi)
    {
        var hadReport = _fieldWeather != FieldWeather.Unknown || _fieldLight != FieldLight.Unknown;
        _fieldWeather = FieldWeather.Unknown;
        _fieldWeatherReportedAt = default;
        _fieldLight = FieldLight.Unknown;
        _fieldLightReportedAt = default;
        _fieldConditionsUiSignature = string.Empty;
        _fieldConditionsDecisionSignature = string.Empty;
        if (logEvent && hadReport)
        {
            AddTacticalEvent("FIELD", "Field reports cleared", "No weather or light report active");
        }
        if (updateUi)
        {
            UpdateFieldConditions(force: true);
        }
    }

    private void SurvivalAssistantReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode) return;
        _survivalIncidentPickerOpen = true;
        _survivalIncidentUiSignature = string.Empty;
        UpdateSurvivalAssistant(force: true);
    }

    private void SurvivalAssistantChangeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || string.IsNullOrEmpty(_survivalIncidentId)) return;
        _survivalIncidentPickerOpen = true;
        _survivalIncidentUiSignature = string.Empty;
        UpdateSurvivalAssistant(force: true);
    }

    private void StatusBeaconButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode) return;
        OpenMapToolsAtSection("core-vitals");
    }

    private bool ActivateSurvivalIncident(string requestedId, bool logEvent)
    {
        if (_streamerMode || SurvivalAssistantLogic.Find(requestedId) is not { } incident) return false;
        ResetRecoveryMonitorState();
        _survivalIncidentId = incident.Id;
        _survivalIncidentStartedAt = DateTimeOffset.UtcNow;
        _survivalIncidentAdditionalSeconds = 0;
        _survivalIncidentEstimateCompletionAnnounced = incident.ExpectedSeconds <= 0;
        _survivalIncidentPickerOpen = false;
        _survivalIncidentHudCollapsed = false;
        _survivalIncidentUiSignature = string.Empty;
        if (logEvent)
        {
            AddTacticalEvent("SURVIVAL", $"{incident.Label} reported", incident.Priority,
                warning: incident.Urgency >= 3);
        }
        UpdateSurvivalAssistant(force: true);
        RefreshSmartHudPresentation(force: true);
        UpdateTacticalBrief();
        SaveSettings();
        return true;
    }

    private void SurvivalIncidentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || sender is not Button { Tag: string requestedId }) return;
        ActivateSurvivalIncident(requestedId, logEvent: true);
    }

    private void SurvivalAssistantResolvedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || string.IsNullOrEmpty(_survivalIncidentId)) return;
        ClearSurvivalIncident(logEvent: true);
    }

    private void ClearSurvivalIncident(bool logEvent)
    {
        var previous = SurvivalAssistantLogic.Find(_survivalIncidentId);
        if (previous is not null && logEvent)
        {
            AddTacticalEvent("SURVIVAL", $"{previous.Value.Label} resolved", "Manual status cleared");
        }
        ResetRecoveryMonitorState();
        _survivalIncidentId = string.Empty;
        _survivalIncidentStartedAt = DateTimeOffset.UtcNow;
        _survivalIncidentAdditionalSeconds = 0;
        _survivalIncidentEstimateCompletionAnnounced = true;
        _survivalIncidentPickerOpen = false;
        _survivalIncidentHudCollapsed = false;
        _survivalIncidentUiSignature = string.Empty;
        UpdateSurvivalAssistant(force: true);
        RefreshSmartHudPresentation(force: true);
        UpdateTacticalBrief();
        SaveSettings();
    }

    private void SurvivalRestartTimerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || SurvivalAssistantLogic.Find(_survivalIncidentId) is not { ExpectedSeconds: > 0 } incident)
        {
            return;
        }
        _survivalIncidentStartedAt = DateTimeOffset.UtcNow;
        _survivalIncidentAdditionalSeconds = 0;
        _survivalIncidentEstimateCompletionAnnounced = false;
        _survivalIncidentHudCollapsed = false;
        _survivalIncidentUiSignature = string.Empty;
        AddTacticalEvent("SURVIVAL", "Recovery estimate restarted", incident.Label);
        UpdateSurvivalAssistant(force: true);
        UpdateTacticalBrief();
        SaveSettings();
    }

    private async Task TriggerVomitRecoveryAsync(bool openPanelWhenStarted)
    {
        if (_streamerMode)
        {
            await ShowHotkeyToastAsync("RECOVERY STATUS HIDDEN IN STREAMER MODE", false);
            return;
        }

        if (SurvivalAssistantLogic.Find(_survivalIncidentId) is { } current)
        {
            _survivalIncidentHudCollapsed = false;
            _survivalIncidentUiSignature = string.Empty;
            UpdateSurvivalAssistant(force: true);
            SaveSettings();
            OpenMapToolsAtSection("survival-assistant");
            await ShowHotkeyToastAsync($"{current.ShortLabel} RECOVERY INSTRUCTIONS OPEN", true);
            return;
        }

        if (ActivateSurvivalIncident("vomit", logEvent: true))
        {
            if (openPanelWhenStarted)
            {
                OpenMapToolsAtSection("survival-assistant");
            }
            await ShowHotkeyToastAsync("VOMIT RECOVERY · 5:00 STARTED", true);
        }
    }

    private async void SurvivalQuickButton_Click(object sender, RoutedEventArgs e)
    {
        var quickAction = SurvivalAssistantLogic.QuickAction(
            SurvivalAssistantLogic.Find(_survivalIncidentId),
            _survivalIncidentStartedAt,
            DateTimeOffset.UtcNow,
            _survivalIncidentAdditionalSeconds);
        if (quickAction.Kind == SurvivalQuickActionKind.ReportAdditionalVomit)
        {
            await ReportAdditionalVomitAsync();
            return;
        }

        await TriggerVomitRecoveryAsync(openPanelWhenStarted: false);
    }

    private async void SurvivalVomitAgainButton_Click(object sender, RoutedEventArgs e) =>
        await ReportAdditionalVomitAsync();

    private async Task ReportAdditionalVomitAsync()
    {
        if (_streamerMode || _survivalIncidentId != "vomit") return;
        var previousAdditionalSeconds = _survivalIncidentAdditionalSeconds;
        var incident = SurvivalAssistantLogic.Find("vomit")!.Value;
        var now = DateTimeOffset.UtcNow;
        var recoveryClock = SurvivalAssistantLogic.ReportAdditionalVomit(
            _survivalIncidentStartedAt,
            _survivalIncidentAdditionalSeconds,
            now);
        _survivalIncidentStartedAt = recoveryClock.StartedAt;
        _survivalIncidentAdditionalSeconds = recoveryClock.AdditionalSeconds;
        if (!recoveryClock.Restarted
            && _survivalIncidentAdditionalSeconds == previousAdditionalSeconds)
        {
            await ShowHotkeyToastAsync("SICKNESS TIMER STACK LIMIT REACHED", false);
            return;
        }

        _survivalIncidentEstimateCompletionAnnounced = false;
        _survivalIncidentHudCollapsed = false;
        _survivalIncidentUiSignature = string.Empty;
        var remaining = SurvivalAssistantLogic.RemainingSeconds(
            incident,
            _survivalIncidentStartedAt,
            now,
            _survivalIncidentAdditionalSeconds);
            AddTacticalEvent(
                "SURVIVAL",
                recoveryClock.Restarted ? "In-game sickness warning reconfirmed" : "Additional vomit reported",
                recoveryClock.Restarted
                    ? "Fresh five-minute estimate started from the player's current game warning"
                    : $"Five minutes added · {SurvivalAssistantLogic.FormatRemaining(remaining)} estimated remaining");
        UpdateSurvivalAssistant(force: true);
        UpdateTacticalBrief();
        SaveSettings();
        await ShowHotkeyToastAsync(
            recoveryClock.Restarted
                ? "SICKNESS WARNING · 5:00 RESTARTED"
                : $"SICKNESS TIMER +5:00 · {SurvivalAssistantLogic.FormatRemaining(remaining)} LEFT",
            true);
    }

    private async void SurvivalRecoveryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || SurvivalAssistantLogic.Find(_survivalIncidentId) is not { } incident) return;
        var remedy = SurvivalAssistantLogic.ResolveRecoveryRemedy(
            incident.Id,
            LiveMapServicesActive,
            _lifeRunActive);

        if (remedy.Kind == RecoveryRemedyKind.ResourceFinder)
        {
            var query = remedy.Target == "diet"
                ? CurrentDietResourceQuery()
                : remedy.Target;
            await OpenResourceFinderForQueryAsync(
                query,
                remedy.Target == "salt" ? "NO CURRENT SALT LICK SITE" : "NO PUBLIC SITE FOR THIS FOOD",
                $"{incident.Label} · {query}");
            return;
        }

        if (remedy.Kind == RecoveryRemedyKind.FoodLayer)
        {
            var layerEnabled = _foodLayer is true || await ExecuteMapperCommandAsync(
                "window.__isley?.setOfficialLayer('food', true) ?? false");
            await ShowHotkeyToastAsync(
                layerEnabled ? "FOOD LAYER ON · START A LIFE RUN FOR SPECIES GUIDANCE" : "FOOD LAYER UNAVAILABLE",
                layerEnabled);
            if (layerEnabled)
            {
                AddTacticalEvent("SURVIVAL", "Food layer opened", "Start a Life Run for species-aware sites");
            }
            return;
        }

        if (remedy.Kind == RecoveryRemedyKind.DietCoach)
        {
            OpenMapToolsAtSection("diet-coach");
            await ShowHotkeyToastAsync("SELECT SPECIES · LOG NUTRIENTS · FIND FOOD", true);
            return;
        }

        var routed = await ExecuteMapperCommandAsync(
            $"window.__isley?.routeToNearestPinType('{remedy.Target}') ?? false");
        if (routed)
        {
            AddTacticalEvent("SURVIVAL", "Recovery route started", $"Nearest saved {remedy.Target} marker");
            await ShowHotkeyToastAsync($"ROUTING TO SAVED {remedy.Target.ToUpperInvariant()} PIN", true);
            return;
        }

        await ShowHotkeyToastAsync(
            LiveMapServicesActive
                ? $"SAVE A {remedy.Target.ToUpperInvariant()} PIN FIRST"
            : "SAVED-PIN ROUTING REQUIRES LIVE MAP MODE",
            false);
    }

    private ServerRestartWatchView CurrentServerRestartWatchView(DateTimeOffset? now = null) =>
        ServerRestartWatchLogic.Evaluate(new ServerRestartWatchSnapshot(
            _serverRestartWatchActive,
            _serverRestartWatchStartedAt,
            _serverRestartWatchDurationSeconds,
            now ?? DateTimeOffset.UtcNow));

    private async void RestartWatchReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode
            || sender is not Button { Tag: string durationTag }
            || !int.TryParse(durationTag, out var durationSeconds))
        {
            return;
        }

        await StartServerRestartWatchAsync(durationSeconds);
    }

    private async Task StartServerRestartWatchAsync(int durationSeconds)
    {
        if (_streamerMode)
        {
            return;
        }

        _serverRestartWatchDurationSeconds = ServerRestartWatchLogic.NormalizeDuration(durationSeconds);
        _serverRestartWatchStartedAt = DateTimeOffset.UtcNow;
        _serverRestartWatchActive = true;
        var view = CurrentServerRestartWatchView();
        _serverRestartWatchNoticeLevel = view.NoticeLevel;
        _serverRestartWatchUiSignature = string.Empty;
        AddTacticalEvent(
            "RESTART",
            $"{ServerRestartWatchLogic.WarningLabel(_serverRestartWatchDurationSeconds)} warning reported",
            "Player-reported in-game warning · session-only estimate");
        UpdateServerRestartWatch(force: true);
        UpdateNextMove(force: true);
        UpdateTacticalBrief();
        await ShowHotkeyToastAsync(
            $"RESTART WATCH · {ServerRestartWatchLogic.FormatRemaining(_serverRestartWatchDurationSeconds)} REPORTED",
            true);
    }

    private async void RestartWatchCancelButton_Click(object sender, RoutedEventArgs e)
    {
        var wasActive = _serverRestartWatchActive;
        CancelServerRestartWatch(logEvent: true, updateUi: true);
        if (wasActive)
        {
            await ShowHotkeyToastAsync("RESTART WATCH CANCELED", true);
        }
    }

    private async void RestartWatchSafeLogoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_serverRestartWatchActive || _streamerMode)
        {
            return;
        }

        var view = CurrentServerRestartWatchView();
        if (view.RemainingSeconds > 120)
        {
            OpenMapToolsAtSection("safe-logout");
            await ShowHotkeyToastAsync("SAFE LOGOUT READY · START WHEN THE WARNING SHORTENS", true);
            return;
        }

        await StartSafeLogoutGuardAsync();
        OpenMapToolsAtSection("safe-logout");
    }

    private async void RestartWatchOpenButton_Click(object sender, RoutedEventArgs e)
    {
        OpenMapToolsAtSection("restart-watch");
        await ShowHotkeyToastAsync(
            _serverRestartWatchActive ? "RESTART WATCH OPEN" : "REPORT THE CURRENT IN-GAME WARNING",
            true);
    }

    private void CancelServerRestartWatch(bool logEvent, bool updateUi)
    {
        var wasActive = _serverRestartWatchActive;
        _serverRestartWatchActive = false;
        _serverRestartWatchStartedAt = DateTimeOffset.UtcNow;
        _serverRestartWatchNoticeLevel = 0;
        _serverRestartWatchUiSignature = string.Empty;
        if (wasActive && logEvent)
        {
            AddTacticalEvent("RESTART", "Restart watch canceled", "Player-reported estimate dismissed");
        }

        if (!updateUi)
        {
            return;
        }

        UpdateServerRestartWatch(force: true);
        UpdateNextMove(force: true);
        UpdateTacticalBrief();
    }

    private void UpdateServerRestartWatch(bool force = false)
    {
        if (ServerRestartWatchHudBorder is null
            || ServerRestartWatchHeadingText is null
            || ServerRestartUniversalPanel is null
            || UniversalSessionRestartButton is null)
        {
            return;
        }

        var view = CurrentServerRestartWatchView();
        if (_serverRestartWatchActive && view.NoticeLevel > _serverRestartWatchNoticeLevel)
        {
            _serverRestartWatchNoticeLevel = view.NoticeLevel;
            var (title, detail) = view.NoticeLevel switch
            {
                1 => ("Five-minute restart warning", "Finish the current action and choose cover"),
                2 => ("Two-minute restart warning", "Stop traveling or fighting · prepare Safe Logout"),
                3 => ("Final-minute restart warning", "Use the in-game safe-log flow now"),
                _ => ("Reported restart window elapsed", "Verify the in-game server state")
            };
            AddTacticalEvent("RESTART", title, detail, warning: true);
            if (_timerSoundEnabled && view.NoticeLevel >= 3)
            {
                SystemSounds.Exclamation.Play();
            }
            _ = ShowHotkeyToastAsync(
                view.Phase == ServerRestartWatchPhase.Verify
                    ? "RESTART WINDOW ELAPSED · VERIFY IN GAME"
                    : $"SERVER RESTART · {view.Countdown} · {view.Heading}",
                view.NoticeLevel < 3);
        }

        var signature = string.Join('|',
            _serverRestartWatchActive,
            view.Phase,
            view.RemainingSeconds,
            view.Heading,
            view.Countdown,
            _streamerMode,
            LiveMapServicesActive);
        if (!force && string.Equals(signature, _serverRestartWatchUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _serverRestartWatchUiSignature = signature;

        var accent = view.Phase switch
        {
            ServerRestartWatchPhase.Verify or ServerRestartWatchPhase.FinalMinute =>
                new SolidColorBrush(Color.FromRgb(255, 163, 108)),
            ServerRestartWatchPhase.FinalTwo or ServerRestartWatchPhase.FinalFive =>
                (Brush)FindResource("WarningBrush"),
            ServerRestartWatchPhase.Planning => (Brush)FindResource("AccentBrush"),
            _ => (Brush)FindResource("SecondaryTextBrush")
        };
        var displayDetail = view.Visible
            ? $"{view.Detail} Player-reported estimate."
            : view.Detail;

        ServerRestartWatchHeadingText.Text = view.Heading;
        ServerRestartWatchHeadingText.Foreground = accent;
        ServerRestartWatchCountdownText.Text = view.Countdown;
        ServerRestartWatchCountdownText.Foreground = accent;
        ServerRestartWatchDetailText.Text = displayDetail;
        ServerRestartWatchProgressFill.Background = accent;
        ServerRestartWatchProgressTransform.ScaleX = view.RemainingFraction;
        ServerRestartWatchLogoutButton.IsEnabled = view.Visible && !_streamerMode;
        ServerRestartWatchLogoutButton.Content = view.RemainingSeconds <= 120 ? "START LOGOUT" : "OPEN LOGOUT";
        ServerRestartWatchLogoutButton.ToolTip = view.RemainingSeconds <= 120
            ? "Start the Safe Logout Guard and move to its controls"
            : "Open the Safe Logout controls without starting their short countdown yet";
        ServerRestartWatchCancelButton.IsEnabled = view.Visible && !_streamerMode;
        UniversalSessionRestartButton.IsEnabled = !_streamerMode;

        ServerRestartWatchHudBorder.Visibility = view.Visible
                                                  && LiveMapServicesActive
                                                  && !_streamerMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        ServerRestartWatchHudBorder.BorderBrush = accent;
        ServerRestartWatchHudHeadingText.Text = view.Heading;
        ServerRestartWatchHudHeadingText.Foreground = accent;
        ServerRestartWatchHudCountdownText.Text = view.Countdown;
        ServerRestartWatchHudCountdownText.Foreground = accent;
        ServerRestartWatchHudDetailText.Text = displayDetail;
        ServerRestartWatchHudProgressFill.Background = accent;
        ServerRestartWatchHudProgressTransform.ScaleX = view.RemainingFraction;

        ServerRestartUniversalPanel.Visibility = view.Visible
                                                 && !LiveMapServicesActive
                                                 && !_streamerMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        ServerRestartUniversalPanel.BorderBrush = accent;
        ServerRestartUniversalStatusText.Text = view.Countdown;
        ServerRestartUniversalStatusText.Foreground = accent;
        ServerRestartUniversalDetailText.Text = $"{view.Heading} · player-reported estimate.";

        if (view.Pulse && !_liteModeEnabled && !_serverRestartWatchPulsing)
        {
            _serverRestartWatchPulsing = true;
            ServerRestartWatchHudBorder.BeginAnimation(
                OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0.68, 1, TimeSpan.FromMilliseconds(420))
                {
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                });
        }
        else if ((!view.Pulse || _liteModeEnabled) && _serverRestartWatchPulsing)
        {
            _serverRestartWatchPulsing = false;
            ServerRestartWatchHudBorder.BeginAnimation(OpacityProperty, null);
            ServerRestartWatchHudBorder.Opacity = 1;
        }

        UpdateNextMove();
        UpdateTacticalBrief();
    }

    private SafeLogoutGuardView CurrentSafeLogoutGuardView(DateTimeOffset? now = null) =>
        SafeLogoutLogic.Evaluate(new SafeLogoutGuardSnapshot(
            _safeLogoutGuardState,
            _safeLogoutGuardStartedAt,
            SafeLogoutLogic.DurationOptions[_safeLogoutDurationIndex],
            _markerAvailable
            && !_staleAlertActive
            && _currentSelfX is not null
            && _currentSelfY is not null,
            _currentSelfSpeed,
            now ?? DateTimeOffset.UtcNow));

    private async void SafeLogoutStartButton_Click(object sender, RoutedEventArgs e) =>
        await StartSafeLogoutGuardAsync();

    private async Task StartSafeLogoutGuardAsync()
    {
        var liveMonitor = LiveMapServicesActive
                          && _markerAvailable
                          && !_staleAlertActive
                          && _currentSelfX is not null
                          && _currentSelfY is not null;
        _safeLogoutGuardState = liveMonitor
            ? SafeLogoutGuardState.CountingMonitored
            : SafeLogoutGuardState.CountingManual;
        _safeLogoutGuardStartedAt = DateTimeOffset.UtcNow;
        _safeLogoutUiSignature = string.Empty;
        var seconds = SafeLogoutLogic.DurationOptions[_safeLogoutDurationIndex];
        AddTacticalEvent(
            "LOGOUT",
            "Safe Logout Guard started",
            liveMonitor
                ? $"{seconds}s · authorized movement monitor active"
                : $"{seconds}s · manual countdown · no live movement monitor");
        UpdateSafeLogoutGuard(force: true);
        await ShowHotkeyToastAsync(
            liveMonitor
                ? $"LOGOUT GUARD · MONITORING {seconds}S"
                : $"LOGOUT GUARD · MANUAL {seconds}S",
            true);
    }

    private void SafeLogoutDurationButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentSafeLogoutGuardView().IsCounting)
        {
            return;
        }

        _safeLogoutDurationIndex =
            (_safeLogoutDurationIndex + 1) % SafeLogoutLogic.DurationOptions.Length;
        _safeLogoutGuardState = SafeLogoutGuardState.Ready;
        _safeLogoutUiSignature = string.Empty;
        UpdateSafeLogoutGuard(force: true);
    }

    private async void SafeLogoutCancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_safeLogoutGuardState == SafeLogoutGuardState.Ready)
        {
            return;
        }

        var wasCounting = CurrentSafeLogoutGuardView().IsCounting;
        _safeLogoutGuardState = SafeLogoutGuardState.Ready;
        _safeLogoutGuardStartedAt = DateTimeOffset.UtcNow;
        _safeLogoutUiSignature = string.Empty;
        if (wasCounting)
        {
            AddTacticalEvent("LOGOUT", "Safe Logout Guard canceled", "Manual session-only guard stopped");
        }
        UpdateSafeLogoutGuard(force: true);
        await ShowHotkeyToastAsync(wasCounting ? "LOGOUT GUARD CANCELED" : "LOGOUT GUARD READY", true);
    }

    private void UpdateSafeLogoutGuard(bool force = false)
    {
        if (SafeLogoutHudBorder is null
            || SafeLogoutStatusText is null
            || SafeLogoutUniversalButton is null)
        {
            return;
        }

        var previousState = _safeLogoutGuardState;
        var view = CurrentSafeLogoutGuardView();
        _safeLogoutGuardState = view.State;
        if (previousState != view.State)
        {
            switch (view.State)
            {
                case SafeLogoutGuardState.Interrupted:
                    AddTacticalEvent(
                        "LOGOUT",
                        "Safe Logout Guard interrupted",
                        "Authorized movement detected · restart after resting again",
                        warning: true);
                    _ = ShowHotkeyToastAsync("LOGOUT GUARD · MOVEMENT DETECTED", false);
                    break;
                case SafeLogoutGuardState.MonitorLost:
                    AddTacticalEvent(
                        "LOGOUT",
                        "Safe Logout monitor lost",
                        "Authorized self-marker or fresh feed unavailable · restart required",
                        warning: true);
                    _ = ShowHotkeyToastAsync("LOGOUT GUARD · MONITOR LOST", false);
                    break;
                case SafeLogoutGuardState.Complete:
                    AddTacticalEvent(
                        "LOGOUT",
                        "Safe Logout countdown complete",
                        "Verify logout in the game or server UI",
                        warning: true);
                    _ = ShowHotkeyToastAsync("COUNTDOWN COMPLETE · VERIFY IN GAME", true);
                    break;
            }
        }

        var duration = SafeLogoutLogic.DurationOptions[_safeLogoutDurationIndex];
        var signature = string.Join('|',
            view.State,
            view.RemainingSeconds,
            duration,
            LiveMapServicesActive,
            _streamerMode);
        if (!force && string.Equals(signature, _safeLogoutUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _safeLogoutUiSignature = signature;

        var accent = view.IsWarning
            ? (Brush)FindResource("WarningBrush")
            : view.State == SafeLogoutGuardState.Complete
                ? new SolidColorBrush(Color.FromRgb(103, 232, 249))
                : new SolidColorBrush(Color.FromRgb(110, 231, 183));
        var detail = view.Detail;
        SafeLogoutStatusText.Text = view.Label;
        SafeLogoutStatusText.Foreground = accent;
        SafeLogoutModeText.Text = view.IsCounting
            ? view.State == SafeLogoutGuardState.CountingMonitored ? "LIVE MONITOR" : "MANUAL"
            : $"{duration}S";
        SafeLogoutModeText.Foreground = accent;
        SafeLogoutDetailText.Text = detail;
        SafeLogoutDrawerProgressTransform.ScaleX = view.Progress;
        SafeLogoutStartButton.Content = view.State == SafeLogoutGuardState.Ready ? "START" : "RESTART";
        SafeLogoutDurationButton.Content = $"{duration} SEC";
        SafeLogoutDurationButton.IsEnabled = !view.IsCounting;
        SafeLogoutCancelButton.IsEnabled = view.State != SafeLogoutGuardState.Ready;
        SafeLogoutCancelButton.Content = view.IsCounting ? "CANCEL" : "DISMISS";

        SafeLogoutHudBorder.Visibility = view.State != SafeLogoutGuardState.Ready
                                         && LiveMapServicesActive
                                         && !_streamerMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        SafeLogoutHudBorder.BorderBrush = accent;
        SafeLogoutHudStatusText.Text = view.Label;
        SafeLogoutHudStatusText.Foreground = accent;
        SafeLogoutHudDetailText.Text = detail;
        SafeLogoutHudProgressFill.Background = accent;
        SafeLogoutHudProgressTransform.ScaleX = view.Progress;

        SafeLogoutUniversalPanel.Visibility = view.State != SafeLogoutGuardState.Ready
                                               && !LiveMapServicesActive
                                               && !_streamerMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        SafeLogoutUniversalPanel.BorderBrush = accent;
        SafeLogoutUniversalStatusText.Text = view.Label;
        SafeLogoutUniversalStatusText.Foreground = accent;
        SafeLogoutUniversalDetailText.Text = detail;
        SafeLogoutUniversalButton.Content = view.IsCounting
            ? SafeLogoutLogic.FormatRemaining(view.RemainingSeconds)
            : view.State == SafeLogoutGuardState.Ready ? "LOGOUT" : "RESTART";
        SafeLogoutUniversalButton.ToolTip = view.IsCounting
            ? detail
            : "Start or restart the session-only Safe Logout Guard";

        UpdateTacticalBrief();
    }

    private bool StartSurvivalTimer(string requestedLabel, int minutes)
    {
        if (_survivalTimers.Count >= 4 || minutes is < 1 or > 360)
        {
            return false;
        }

        var durationSeconds = minutes * 60;
        var label = NormalizeTimerLabel(requestedLabel);
        if (string.IsNullOrWhiteSpace(label))
        {
            label = $"Timer {_survivalTimers.Count + 1}";
        }

        var timer = new SurvivalTimer
        {
            Id = Guid.NewGuid().ToString("N"),
            Label = label,
            DurationSeconds = durationSeconds,
            EndsAt = DateTimeOffset.UtcNow.AddSeconds(durationSeconds)
        };
        _survivalTimers.Add(timer);
        AppendTimerJournalEvent(TimerJournalLogic.StartEvent, timer);
        AddTacticalEvent("TIMER", "Timer started", $"{label} · {minutes}m");
        _clearTimersConfirmationPending = false;
        _survivalTimerUiSignature = string.Empty;
        UpdateSurvivalTimers(force: true);
        SaveSettings();
        return true;
    }

    private void UpdateSurvivalTimers(bool force = false)
    {
        if (SurvivalTimerHudPanel is null || SurvivalTimerListPanel is null)
        {
            return;
        }

        ReconcileTimerJournalAfterRestore();
        var now = DateTimeOffset.UtcNow;
        var timerCompleted = false;
        foreach (var timer in _survivalTimers)
        {
            if (timer.Completed || timer.IsPaused || timer.EndsAt > now)
            {
                continue;
            }

            timer.Completed = true;
            timer.PausedRemainingSeconds = 0;
            timerCompleted = true;
            AppendTimerJournalEvent(TimerJournalLogic.ElapseEvent, timer);
            AddTacticalEvent("TIMER", "Timer complete", timer.Label, warning: true);
            if (!timer.CompletionNotified)
            {
                timer.CompletionNotified = true;
                _ = AnnounceTimerCompletionAsync(timer.Label);
            }
        }

        var signature = string.Join('|', new[]
        {
            _timerSoundEnabled ? "sound" : "silent",
            _clearTimersConfirmationPending ? "confirm" : "ready",
            string.Join(';', _survivalTimers.Select(timer =>
                $"{timer.Id}:{timer.Label}:{timer.DurationSeconds}:" +
                $"{Math.Ceiling(GetTimerRemainingSeconds(timer, now))}:" +
                $"{timer.IsPaused}:{timer.Completed}"))
        });
        if (!force && string.Equals(signature, _survivalTimerUiSignature, StringComparison.Ordinal))
        {
            if (timerCompleted) SaveSettings();
            return;
        }

        _survivalTimerUiSignature = signature;
        SurvivalTimerHudPanel.Children.Clear();
        SurvivalTimerListPanel.Children.Clear();

        var hasCapacity = _survivalTimers.Count < 4;
        Timer5Button.IsEnabled = hasCapacity;
        Timer15Button.IsEnabled = hasCapacity;
        Timer30Button.IsEnabled = hasCapacity;
        Timer60Button.IsEnabled = hasCapacity;
        StartCustomTimerButton.IsEnabled = hasCapacity;
        ClearAllTimersButton.IsEnabled = _survivalTimers.Count > 0;
        ClearAllTimersButton.Content = _clearTimersConfirmationPending
            ? "CONFIRM CLEAR"
            : "CLEAR ALL";
        TimerSoundButton.Content = _timerSoundEnabled ? "SOUND ON" : "SOUND OFF";
        SetToggleButtonState(TimerSoundButton, _timerSoundEnabled);

        SurvivalTimerHudPanel.Visibility = _survivalTimers.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_survivalTimers.Count == 0)
        {
            SurvivalTimerStatusText.Text = "No timers · start up to four";
            SurvivalTimerStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            if (timerCompleted) SaveSettings();
            return;
        }

        var pausedCount = _survivalTimers.Count(timer => timer.IsPaused && !timer.Completed);
        var completedCount = _survivalTimers.Count(timer => timer.Completed);
        SurvivalTimerStatusText.Text = _clearTimersConfirmationPending
            ? "Select Clear All again within 3 seconds"
            : $"{_survivalTimers.Count}/4 timer{(_survivalTimers.Count == 1 ? string.Empty : "s")}" +
              (pausedCount > 0 ? $" · {pausedCount} paused" : string.Empty) +
              (completedCount > 0 ? $" · {completedCount} done" : string.Empty);
        SurvivalTimerStatusText.Foreground = _clearTimersConfirmationPending
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("SecondaryTextBrush");

        foreach (var timer in _survivalTimers)
        {
            var remaining = GetTimerRemainingSeconds(timer, now);
            var displayTime = FormatTimerRemaining(remaining);
            var progress = timer.DurationSeconds > 0
                ? Math.Clamp(remaining / timer.DurationSeconds, 0, 1)
                : 0;
            var accent = TimerAccentBrush(timer, remaining);
            SurvivalTimerHudPanel.Children.Add(BuildTimerHudRow(timer, displayTime, progress, accent));
            SurvivalTimerListPanel.Children.Add(BuildTimerControlRow(timer, displayTime, accent));
        }

        if (timerCompleted) SaveSettings();
    }

    private UIElement BuildTimerHudRow(
        SurvivalTimer timer,
        string displayTime,
        double progress,
        Brush accent)
    {
        var label = timer.Label.Length <= 18 ? timer.Label : $"{timer.Label[..17]}…";
        var state = timer.Completed ? "DONE" : timer.IsPaused ? "PAUSED" : displayTime;
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = label.ToUpperInvariant(),
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var timeText = new TextBlock
        {
            Text = state,
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = accent
        };
        Grid.SetColumn(timeText, 1);
        header.Children.Add(timeText);

        var progressTrack = new Grid
        {
            Width = 156,
            Height = 2,
            Margin = new Thickness(0, 5, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromArgb(0x44, 0x64, 0x74, 0x8B)),
            ClipToBounds = true
        };
        progressTrack.Children.Add(new Border
        {
            Width = 156 * progress,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = accent,
            CornerRadius = new CornerRadius(1)
        });

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(progressTrack);
        return new Border
        {
            Width = 178,
            Margin = new Thickness(0, 0, 0, 5),
            Padding = new Thickness(8, 6, 8, 6),
            Background = (Brush)FindResource("MapChromeBrush"),
            BorderBrush = accent,
            BorderThickness = new Thickness(3, 1, 1, 1),
            CornerRadius = new CornerRadius(8),
            Child = content
        };
    }

    private UIElement BuildTimerControlRow(SurvivalTimer timer, string displayTime, Brush accent)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(31) });
        var label = timer.Label.Length <= 15 ? timer.Label : $"{timer.Label[..14]}…";
        var state = timer.Completed ? "DONE · RESTART" : timer.IsPaused ? "PAUSED · RESUME" : $"{displayTime} · PAUSE";
        var action = new Button
        {
            Style = (Style)FindResource("DrawerButton"),
            Height = 30,
            Margin = new Thickness(0, 0, 4, 4),
            FontSize = 8,
            BorderThickness = new Thickness(3, 1, 1, 1),
            BorderBrush = accent,
            Content = $"{label} · {state}",
            Tag = timer.Id,
            ToolTip = timer.Completed
                ? $"Restart {timer.Label} for {FormatTimerDuration(timer.DurationSeconds)}"
                : timer.IsPaused
                    ? $"Resume {timer.Label}"
                    : $"Pause {timer.Label}"
        };
        action.Click += SurvivalTimerButton_Click;
        row.Children.Add(action);

        var remove = new Button
        {
            Style = (Style)FindResource("DrawerCompactButton"),
            Width = 27,
            Height = 30,
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(0),
            Content = "X",
            Tag = timer.Id,
            ToolTip = $"Remove {timer.Label}"
        };
        remove.Click += RemoveSurvivalTimerButton_Click;
        Grid.SetColumn(remove, 1);
        row.Children.Add(remove);
        return row;
    }

    private Brush TimerAccentBrush(SurvivalTimer timer, double remainingSeconds)
    {
        if (timer.Completed) return new SolidColorBrush(Color.FromRgb(255, 104, 71));
        if (timer.IsPaused) return (Brush)FindResource("SecondaryTextBrush");
        return remainingSeconds <= 60
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("AccentBrush");
    }

    private async Task AnnounceTimerCompletionAsync(string label)
    {
        if (_timerSoundEnabled)
        {
            SystemSounds.Exclamation.Play();
        }

        if (IsLoaded && SurvivalTimerHudPanel is not null)
        {
            SurvivalTimerHudPanel.BeginAnimation(
                OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(
                    0.55,
                    1,
                    TimeSpan.FromMilliseconds(240))
                {
                    AutoReverse = true,
                    RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(3)
                });
        }
        await ShowHotkeyToastAsync($"{NormalizeTimerLabel(label).ToUpperInvariant()} · TIMER DONE", true);
    }

    private static double GetTimerRemainingSeconds(SurvivalTimer timer, DateTimeOffset now)
    {
        if (timer.Completed) return 0;
        return timer.IsPaused
            ? Math.Max(0, timer.PausedRemainingSeconds)
            : Math.Max(0, (timer.EndsAt - now).TotalSeconds);
    }

    private static string FormatTimerRemaining(double totalSeconds)
    {
        var seconds = Math.Max(0, (int)Math.Ceiling(totalSeconds));
        var time = TimeSpan.FromSeconds(seconds);
        return seconds >= 3600
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }

    private static string FormatTimerDuration(int durationSeconds)
    {
        var minutes = Math.Max(1, durationSeconds / 60);
        return minutes >= 60 && minutes % 60 == 0
            ? $"{minutes / 60}h"
            : $"{minutes}m";
    }

    private static string NormalizeTimerLabel(string value)
    {
        var withoutControls = Regex.Replace(value ?? string.Empty, @"[\u0000-\u001F\u007F]+", " ");
        var normalized = Regex.Replace(withoutControls, @"\s+", " ").Trim();
        return normalized.Length <= 28 ? normalized : normalized[..28];
    }

    private void TimerInputBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private void TimerInputBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.IsKeyboardFocusWithin)
        {
            return;
        }

        e.Handled = true;
        textBox.Focus();
        textBox.SelectAll();
    }

    private void TimerMinutesInputBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");

    private void TimerMinutesInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        StartCustomSurvivalTimer();
    }

    private void TimerPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string minutesText }
            || !int.TryParse(minutesText, out var minutes))
        {
            return;
        }

        if (!StartSurvivalTimer(TimerLabelInputBox.Text, minutes))
        {
            SurvivalTimerStatusText.Text = "Four timers are already active · remove one first";
            SurvivalTimerStatusText.Foreground = (Brush)FindResource("WarningBrush");
        }
    }

    private void StartCustomTimerButton_Click(object sender, RoutedEventArgs e) =>
        StartCustomSurvivalTimer();

    private void StartCustomSurvivalTimer()
    {
        if (!int.TryParse(TimerMinutesInputBox.Text.Trim(), out var minutes)
            || minutes is < 1 or > 360)
        {
            SurvivalTimerStatusText.Text = "Enter custom minutes from 1 to 360";
            SurvivalTimerStatusText.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        if (!StartSurvivalTimer(TimerLabelInputBox.Text, minutes))
        {
            SurvivalTimerStatusText.Text = "Four timers are already active · remove one first";
            SurvivalTimerStatusText.Foreground = (Brush)FindResource("WarningBrush");
        }
    }

    private void SurvivalTimerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string timerId }) return;
        var timer = _survivalTimers.FirstOrDefault(candidate => candidate.Id == timerId);
        if (timer is null) return;

        ToggleSurvivalTimerState(timer);
    }

    private void ToggleSurvivalTimerState(SurvivalTimer timer)
    {
        if (!_survivalTimers.Contains(timer)) return;

        var now = DateTimeOffset.UtcNow;
        if (timer.Completed)
        {
            timer.Completed = false;
            timer.IsPaused = false;
            timer.PausedRemainingSeconds = 0;
            timer.CompletionNotified = false;
            timer.EndsAt = now.AddSeconds(timer.DurationSeconds);
            AppendTimerJournalEvent(TimerJournalLogic.StartEvent, timer);
        }
        else if (timer.IsPaused)
        {
            timer.IsPaused = false;
            timer.EndsAt = now.AddSeconds(Math.Max(1, timer.PausedRemainingSeconds));
            timer.PausedRemainingSeconds = 0;
        }
        else
        {
            timer.PausedRemainingSeconds = GetTimerRemainingSeconds(timer, now);
            timer.IsPaused = true;
        }

        _clearTimersConfirmationPending = false;
        _survivalTimerUiSignature = string.Empty;
        UpdateSurvivalTimers(force: true);
        SaveSettings();
    }

    private void RemoveSurvivalTimerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string timerId }) return;
        var timer = _survivalTimers.FirstOrDefault(candidate => candidate.Id == timerId);
        if (timer is null || !_survivalTimers.Remove(timer)) return;
        AppendTimerJournalEvent(TimerJournalLogic.CancelEvent, timer);
        _clearTimersConfirmationPending = false;
        _survivalTimerUiSignature = string.Empty;
        UpdateSurvivalTimers(force: true);
        SaveSettings();
    }

    private void TimerSoundButton_Click(object sender, RoutedEventArgs e)
    {
        _timerSoundEnabled = !_timerSoundEnabled;
        _survivalTimerUiSignature = string.Empty;
        UpdateSurvivalTimers(force: true);
        SaveSettings();
    }

    private async void ClearAllTimersButton_Click(object sender, RoutedEventArgs e)
    {
        if (_survivalTimers.Count == 0) return;
        if (_clearTimersConfirmationPending)
        {
            foreach (var timer in _survivalTimers)
            {
                AppendTimerJournalEvent(TimerJournalLogic.CancelEvent, timer);
            }
            _survivalTimers.Clear();
            _clearTimersConfirmationPending = false;
            _survivalTimerUiSignature = string.Empty;
            UpdateSurvivalTimers(force: true);
            SaveSettings();
            return;
        }

        _clearTimersConfirmationPending = true;
        _survivalTimerUiSignature = string.Empty;
        UpdateSurvivalTimers(force: true);
        await Task.Delay(3000);
        if (!IsLoaded || !_clearTimersConfirmationPending) return;
        _clearTimersConfirmationPending = false;
        _survivalTimerUiSignature = string.Empty;
        UpdateSurvivalTimers(force: true);
    }

    private async void WaterCrossingToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        if (_waterCrossingCheckActive)
        {
            var clearArmedMeasurement = _measurementArmed && !_measurementActive;
            ResetWaterCrossingCheck(logEvent: true);
            if (clearArmedMeasurement)
            {
                await ClearMeasurementAsync();
            }
            else
            {
                UpdateMeasurementStatus();
                UpdateWaterCrossingCheck(force: true);
            }
            return;
        }

        await StartWaterCrossingMeasurementAsync(resetBanks: !_measurementActive);
    }

    private async Task StartWaterCrossingMeasurementAsync(bool resetBanks)
    {
        if (_streamerMode || !LiveMapServicesActive)
        {
            return;
        }

        var wasActive = _waterCrossingCheckActive;
        _waterCrossingCheckActive = true;
        _waterCrossingLoggedDecisionKey = string.Empty;
        _waterCrossingUiSignature = string.Empty;
        if (!wasActive)
        {
            AddTacticalEvent("CROSSING", "Water crossing check armed", "Mark the entry and exit banks");
        }

        if (resetBanks || !_measurementActive)
        {
            await ArmFreshMeasurementAsync();
        }
        else
        {
            UpdateWaterCrossingCheck(force: true);
        }
        UpdateTacticalBrief();
        UpdateNextMove(force: true);
    }

    private async void WaterCrossingActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string actionId }
            || string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        await ExecuteCommandPaletteActionAsync(actionId);
    }

    private void OpenFightCheck()
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
                UpdateFightCheck(force: true);
                var offset = FightCheckAnchor.TranslatePoint(new Point(0, 0), GuideToolsPanel).Y;
                ToolsScrollViewer.ScrollToVerticalOffset(Math.Max(0, offset - 4));
            }));
    }

    private async Task CopyTacticalBriefAsync()
    {
        if (_streamerMode)
        {
            await ShowHotkeyToastAsync("TACTICAL BRIEF HIDDEN", false);
            return;
        }

        try
        {
            var brief = BuildTacticalBriefText();
            if (string.IsNullOrWhiteSpace(brief) || brief.Length > 900)
            {
                throw new InvalidOperationException("Tactical brief is unavailable");
            }
            Clipboard.SetText(brief);
            CopyTacticalBriefButton.Content = "COPIED";
            await ShowHotkeyToastAsync("TACTICAL BRIEF COPIED", true);
        }
        catch
        {
            CopyTacticalBriefButton.Content = "UNAVAILABLE";
            await ShowHotkeyToastAsync("CLIPBOARD UNAVAILABLE", false);
        }

        await Task.Delay(1200);
        if (IsLoaded)
        {
            UpdateTacticalBrief();
        }
    }

    private async void CopyTacticalBriefButton_Click(object sender, RoutedEventArgs e) =>
        await CopyTacticalBriefAsync();

    private async void CopyTacticalLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || _tacticalEvents.Count == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(BuildTacticalLogText());
            CopyTacticalLogButton.Content = "COPIED";
            await ShowHotkeyToastAsync("TACTICAL LOG COPIED", true);
        }
        catch
        {
            CopyTacticalLogButton.Content = "UNAVAILABLE";
            await ShowHotkeyToastAsync("CLIPBOARD UNAVAILABLE", false);
        }

        await Task.Delay(1200);
        if (IsLoaded)
        {
            CopyTacticalLogButton.Content = "COPY LOG";
        }
    }

    private async void ClearTacticalLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || _tacticalEvents.Count == 0)
        {
            return;
        }

        if (_clearTacticalLogConfirmationPending)
        {
            _tacticalEvents.Clear();
            _clearTacticalLogConfirmationPending = false;
            _clearTacticalLogConfirmationRevision++;
            UpdateTacticalLog();
            await ShowHotkeyToastAsync("TACTICAL LOG CLEARED", true);
            return;
        }

        _clearTacticalLogConfirmationPending = true;
        var revision = ++_clearTacticalLogConfirmationRevision;
        UpdateTacticalLog();
        await Task.Delay(3000);
        if (IsLoaded
            && revision == _clearTacticalLogConfirmationRevision
            && _clearTacticalLogConfirmationPending)
        {
            _clearTacticalLogConfirmationPending = false;
            UpdateTacticalLog();
        }
    }

    private Button? _tacticalLogExportButton;

    private void EnsureTacticalLogExportButton()
    {
        if (_tacticalLogExportButton is not null
            || CopyTacticalLogButton?.Parent is not UniformGrid grid)
        {
            return;
        }

        grid.Columns = 3;
        _tacticalLogExportButton = new Button
        {
            Style = (Style)FindResource("DrawerCompactButton"),
            ToolTip = "Save this session's tactical timeline to a text or JSON file you choose",
            Content = "EXPORT",
            IsEnabled = false
        };
        _tacticalLogExportButton.Click += ExportTacticalLogButton_Click;
        grid.Children.Add(_tacticalLogExportButton);
    }

    private async void ExportTacticalLogButton_Click(object sender, RoutedEventArgs e) =>
        await ExportTacticalLogAsync();

    private async Task ExportTacticalLogAsync()
    {
        if (_streamerMode || _tacticalEvents.Count == 0)
        {
            await ShowHotkeyToastAsync("TACTICAL LOG UNAVAILABLE", false);
            return;
        }

        var events = _tacticalEvents
            .Select(entry => new TacticalLogExportEvent(
                entry.OccurredAt,
                entry.Category,
                entry.Title,
                entry.Detail,
                entry.Warning))
            .ToList();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Isley tactical log",
            Filter = "Text log (*.txt)|*.txt|JSON (*.json)|*.json",
            FileName = TacticalLogExportLogic.SuggestedFileName(DateTimeOffset.Now, json: false),
            DefaultExt = ".txt",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var asJson = dialog.FilterIndex == 2
                     || string.Equals(
                         Path.GetExtension(dialog.FileName),
                         ".json",
                         StringComparison.OrdinalIgnoreCase);
        var result = asJson
            ? TacticalLogExportLogic.BuildJson(events, DateTimeOffset.Now)
            : TacticalLogExportLogic.BuildPlainText(events, DateTimeOffset.Now);
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(dialog.FileName);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new IOException("No export directory was selected");
            }

            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(dialog.FileName)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporaryPath, result.Content);
            File.Move(temporaryPath, dialog.FileName, overwrite: true);
            temporaryPath = null;
            if (_tacticalLogExportButton is not null)
            {
                _tacticalLogExportButton.Content = "EXPORTED";
            }
            await ShowHotkeyToastAsync(
                $"TACTICAL LOG EXPORTED · {result.ExportedEventCount} EVENTS",
                true);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or System.Security.SecurityException
                                   or NotSupportedException)
        {
            if (_tacticalLogExportButton is not null)
            {
                _tacticalLogExportButton.Content = "FAILED";
            }
            await ShowHotkeyToastAsync("TACTICAL LOG EXPORT FAILED", false);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryPath))
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }

        await Task.Delay(1400);
        if (IsLoaded && _tacticalLogExportButton is not null)
        {
            _tacticalLogExportButton.Content = "EXPORT";
        }
    }

    private readonly List<TimerJournalEntry> _timerJournal = [];
    private readonly HashSet<string> _timerJournalExpiredAwayFlagged = new(StringComparer.Ordinal);
    private bool _timerJournalLoaded;

    private static string TimerJournalPath
    {
        get
        {
            var directory = Path.GetDirectoryName(PrimarySettingsPath);
            return Path.Combine(
                string.IsNullOrWhiteSpace(directory) ? AppContext.BaseDirectory : directory,
                "timer-journal.json");
        }
    }

    private void EnsureTimerJournalLoaded()
    {
        if (_timerJournalLoaded)
        {
            return;
        }

        _timerJournalLoaded = true;
        try
        {
            var path = TimerJournalPath;
            if (File.Exists(path)
                && TimerJournalLogic.TryDeserialize(File.ReadAllText(path), out var entries))
            {
                _timerJournal.AddRange(entries);
            }
        }
        catch
        {
            // The journal is advisory; a corrupt or locked file never blocks launch.
        }
    }

    private void AppendTimerJournalEvent(string eventKind, SurvivalTimer timer)
    {
        EnsureTimerJournalLoaded();
        _timerJournal.Add(TimerJournalLogic.Create(
            eventKind,
            DateTimeOffset.UtcNow,
            timer.Id,
            timer.Label,
            timer.DurationSeconds));
        SaveTimerJournal();
    }

    private void SaveTimerJournal()
    {
        string? temporaryPath = null;
        try
        {
            var pruned = TimerJournalLogic.Prune(_timerJournal);
            _timerJournal.Clear();
            _timerJournal.AddRange(pruned);
            var serialized = TimerJournalLogic.Serialize(pruned);
            var path = TimerJournalPath;
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, $".timer-journal.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporaryPath, serialized);
            File.Move(temporaryPath, path, overwrite: true);
            temporaryPath = null;
        }
        catch
        {
            // Best-effort journal; a failed write never interrupts timer flow.
            if (!string.IsNullOrWhiteSpace(temporaryPath))
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }
    }

    /// <summary>
    /// Surfaces restored timers that elapsed while Isley was closed. They already
    /// restore as silently completed (never re-firing an alarm); this only adds the
    /// honest "expired while away" timeline entry once per timer per expiry, and only
    /// when the journal tracked that timer's start.
    /// </summary>
    private void ReconcileTimerJournalAfterRestore()
    {
        EnsureTimerJournalLoaded();
        var expiredIds = TimerJournalLogic.FindExpiredWhileAway(
            _timerJournal,
            _survivalTimers
                .Where(timer => timer.Completed)
                .Select(timer => timer.Id));
        foreach (var timer in _survivalTimers)
        {
            var normalizedId = TimerJournalLogic.NormalizeTimerId(timer.Id);
            if (normalizedId.Length == 0
                || !expiredIds.Contains(normalizedId, StringComparer.Ordinal)
                || !_timerJournalExpiredAwayFlagged.Add(normalizedId))
            {
                continue;
            }

            AppendTimerJournalEvent(TimerJournalLogic.ExpiredAwayEvent, timer);
            AddTacticalEvent(
                "TIMER",
                "Timer expired while away",
                $"{timer.Label} · ended {timer.EndsAt.ToLocalTime():MMM d HH:mm} · no alarm while Isley was closed",
                warning: true);
        }
    }
}
