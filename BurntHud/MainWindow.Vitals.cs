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
    private void RecordVitalsTrendSample(
        PlayerSnapshotEvaluation snapshot,
        DateTimeOffset capturedAt)
    {
        if (!snapshot.LiveFresh)
        {
            return;
        }

        if (_vitalsTrendSamples.Count > 0
            && snapshot.GrowthPercent + 2 < _vitalsTrendSamples[^1].GrowthPercent)
        {
            _vitalsTrendSamples.Clear();
        }

        var sample = new VitalsTrendSample(
            capturedAt,
            snapshot.HealthPercent,
            snapshot.FoodPercent,
            snapshot.WaterPercent,
            snapshot.GrowthPercent);
        if (_vitalsTrendSamples.Count > 0
            && (capturedAt - _vitalsTrendSamples[^1].CapturedAt).TotalSeconds < 10)
        {
            _vitalsTrendSamples[^1] = sample;
        }
        else
        {
            _vitalsTrendSamples.Add(sample);
        }

        var minimumTime = capturedAt.AddMinutes(-VitalsTrendLogic.MaximumWindowMinutes);
        _vitalsTrendSamples.RemoveAll(item => item.CapturedAt < minimumTime);
        while (_vitalsTrendSamples.Count > VitalsTrendLogic.MaximumSampleCount)
        {
            _vitalsTrendSamples.RemoveAt(0);
        }
        _vitalsTrendUiSignature = string.Empty;
        UpdateVitalsTrendAlert(capturedAt);
    }

    private void ClearVitalsTrendSamples()
    {
        _vitalsTrendSamples.Clear();
        _vitalsTrendUiSignature = string.Empty;
        _vitalsTrendWarningKey = string.Empty;
    }

    private void UpdateVitalsTrendAlert(DateTimeOffset now)
    {
        var trend = VitalsTrendLogic.Analyze(_vitalsTrendSamples, now);
        var warningKey = trend.Warning
            ? trend.WarningHeading.StartsWith("WATER", StringComparison.Ordinal) ? "water" : "food"
            : string.Empty;
        if (string.IsNullOrEmpty(warningKey))
        {
            _vitalsTrendWarningKey = string.Empty;
            return;
        }
        if (string.Equals(warningKey, _vitalsTrendWarningKey, StringComparison.Ordinal))
        {
            return;
        }

        _vitalsTrendWarningKey = warningKey;
        AddTacticalEvent(
            "VITALS",
            "Resource trend warning",
            trend.WarningHeading,
            warning: true);
        _ = ShowHotkeyToastAsync($"{trend.WarningHeading} · CHECK VITALS", false);
    }

    private VisibleHudSensorSample? CurrentVisibleHudSensorSample(DateTimeOffset? now = null)
    {
        var currentTime = now ?? DateTimeOffset.UtcNow;
        return _visibleHudSensorEnabled
               && !_streamerMode
               && _visibleHudSensorSample is { } sample
               && VisibleHudSensorLogic.IsFresh(sample, currentTime)
            ? sample
            : null;
    }

    private void RefreshVisibleHudSensor()
    {
        if (!_visibleHudSensorEnabled)
        {
            _visibleHudSensorStatus = "OFF · enable for continuous estimated vitals";
            return;
        }

        if (_streamerMode)
        {
            _visibleHudSensorStatus = "PAUSED · Streamer Mode is hiding vital capture";
            return;
        }

        if (GetPlayFocusForeground() != PlayFocusForeground.Game)
        {
            _visibleHudSensorStatus = "ARMED · switch to The Isle to resume";
            return;
        }

        var gameWindow = NativeMethods.GetForegroundWindow();
        if (!IsTheIsleWindow(gameWindow))
        {
            _visibleHudSensorStatus = "ARMED · switch to The Isle to resume";
            return;
        }
        var capturedAt = DateTimeOffset.UtcNow;
        if (!VisibleHudSensor.TryRead(
                gameWindow,
                capturedAt,
                _visibleHudCalibration,
                out var sample))
        {
            _visibleHudSensorStatus =
                "HUD NOT FOUND · show the bottom-right HUD and use borderless/windowed fullscreen";
            _coreVitalsUiSignature = string.Empty;
            return;
        }

        _visibleHudSensorSamples.Enqueue(sample);
        while (_visibleHudSensorSamples.Count > 3)
        {
            _visibleHudSensorSamples.Dequeue();
        }

        _visibleHudSensorSample = VisibleHudSensorLogic.Median(
            _visibleHudSensorSamples,
            capturedAt);
        var current = _visibleHudSensorSample.Value;
        _visibleHudSensorStatus =
            $"READING · ~HP {current.HealthPercent}% · F {current.FoodPercent}% · " +
            $"W {current.WaterPercent}% · ST {current.StaminaPercent}%";
        _coreVitalsUiSignature = string.Empty;
        UpdateCoreVitals();
    }

    private PlayerSnapshotEvaluation CurrentPlayerSnapshotEvaluation(DateTimeOffset? now = null) =>
        PlayerSnapshotLogic.Evaluate(_playerSnapshot, now ?? DateTimeOffset.UtcNow);

    private VitalsTrendAnalysis CurrentVitalsTrendAnalysis(DateTimeOffset? now = null) =>
        VitalsTrendLogic.Analyze(_vitalsTrendSamples, now ?? DateTimeOffset.UtcNow);

    private CoreVitalsGuidance CurrentCoreVitalsGuidance(DateTimeOffset? now = null)
    {
        var currentTime = now ?? DateTimeOffset.UtcNow;
        var playerSnapshot = CurrentPlayerSnapshotEvaluation(currentTime);
        var useLiveSnapshot = LiveMapServicesActive && playerSnapshot.LiveFresh && _playerSnapshot is not null;
        var snapshotReceivedAt = useLiveSnapshot ? _playerSnapshot!.Value.ReceivedAt : default;
        var visibleHudSample = CurrentVisibleHudSensorSample(currentTime);
        var useVisibleHudSample = visibleHudSample is not null;
        var visibleCapturedAt = visibleHudSample?.CapturedAt ?? default;
        return CoreVitalsLogic.Evaluate(new CoreVitalsSnapshot(
            useLiveSnapshot
                ? playerSnapshot.HealthState
                : useVisibleHudSample
                    ? VisibleHudSensorLogic.HealthState(visibleHudSample!.Value.HealthPercent)
                    : _reportedHealthState,
            useLiveSnapshot ? snapshotReceivedAt : useVisibleHudSample ? visibleCapturedAt : _reportedHealthReportedAt,
            useLiveSnapshot
                ? playerSnapshot.FoodState
                : useVisibleHudSample
                    ? VisibleHudSensorLogic.VitalState(visibleHudSample!.Value.FoodPercent)
                    : _reportedFoodState,
            useLiveSnapshot ? snapshotReceivedAt : useVisibleHudSample ? visibleCapturedAt : _reportedFoodReportedAt,
            useLiveSnapshot
                ? playerSnapshot.WaterState
                : useVisibleHudSample
                    ? VisibleHudSensorLogic.VitalState(visibleHudSample!.Value.WaterPercent)
                    : _reportedWaterState,
            useLiveSnapshot ? snapshotReceivedAt : useVisibleHudSample ? visibleCapturedAt : _reportedWaterReportedAt,
            useVisibleHudSample
                ? VisibleHudSensorLogic.VitalState(visibleHudSample!.Value.StaminaPercent)
                : _reportedStaminaState,
            useVisibleHudSample ? visibleCapturedAt : _reportedStaminaReportedAt,
            currentTime));
    }

    private DockVitalsPresentation CurrentDockVitalsPresentation(DateTimeOffset? now = null)
    {
        var currentTime = now ?? DateTimeOffset.UtcNow;
        return DockVitalsLogic.Resolve(
            _vitalsHudVisible,
            _streamerMode,
            LiveMapServicesActive,
            CurrentPlayerSnapshotEvaluation(currentTime),
            CurrentCoreVitalsGuidance(currentTime),
            CurrentVitalsTrendAnalysis(currentTime).Health,
            CurrentVisibleHudSensorSample(currentTime));
    }

    private void SetVitalButtonAppearance(Button button, bool fresh, int severity)
    {
        if (!fresh)
        {
            button.Background = (Brush)FindResource("RaisedSurfaceBrush");
            button.Foreground = (Brush)FindResource("PrimaryTextBrush");
            button.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x7E, 0x89, 0x95));
            return;
        }

        if (severity >= 2)
        {
            button.Background = new SolidColorBrush(Color.FromArgb(0x70, 0x7F, 0x1D, 0x1D));
            button.Foreground = new SolidColorBrush(Color.FromRgb(255, 112, 112));
            button.BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0x70, 0x70));
            return;
        }

        if (severity == 1)
        {
            button.Background = new SolidColorBrush(Color.FromArgb(0x70, 0x78, 0x3A, 0x10));
            button.Foreground = new SolidColorBrush(Color.FromRgb(255, 163, 108));
            button.BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xA3, 0x6C));
            return;
        }

        SetToggleButtonState(button, true);
    }

    private void UpdateCoreVitals(bool force = false)
    {
        if (CoreVitalsStatusText is null
            || ReportedHealthButton is null
            || ReportedFoodButton is null
            || ReportedWaterButton is null
            || ReportedStaminaButton is null
            || StatusBeaconButton is null)
        {
            return;
        }

        var guidance = CurrentCoreVitalsGuidance();
        var playerSnapshot = CurrentPlayerSnapshotEvaluation();
        var vitalsTrend = CurrentVitalsTrendAnalysis();
        var useLiveSnapshot = LiveMapServicesActive && playerSnapshot.LiveFresh;
        var visibleHudSample = CurrentVisibleHudSensorSample();
        var useVisibleHudSample = visibleHudSample is not null;
        if (!string.IsNullOrEmpty(_woundObservationId)
            && !WoundCheckLogic.IsCurrent(
                _woundObservationId,
                _reportedHealthReportedAt,
                DateTimeOffset.UtcNow))
        {
            _woundObservationId = string.Empty;
        }
        var woundObservation = WoundCheckLogic.Find(_woundObservationId);
        var liveSpeciesName = LiveSpeciesBridgeLogic.DisplayName(playerSnapshot.SpeciesId);
        var liveSpeciesTooltip = string.IsNullOrEmpty(liveSpeciesName)
            ? string.Empty
            : $" · {liveSpeciesName}";
        var signature = string.Join('|',
            _streamerMode,
            _serverSessionProfileId,
            _playerSnapshotTransportState,
            playerSnapshot.State,
            playerSnapshot.HasValidData,
            playerSnapshot.AgeSeconds,
            playerSnapshot.SpeciesId,
            playerSnapshot.GrowthPercent,
            playerSnapshot.HealthPercent,
            playerSnapshot.FoodPercent,
            playerSnapshot.WaterPercent,
            playerSnapshot.PrimeCompleted,
            playerSnapshot.PrimeRequired,
            vitalsTrend.CompactLabel,
            vitalsTrend.Warning,
            vitalsTrend.WarningHeading,
            _visibleHudSensorEnabled,
            _visibleHudSensorStatus,
            _visibleHudCalibration.Scale,
            _visibleHudCalibration.OffsetX,
            _visibleHudCalibration.OffsetY,
            _visibleHudCalibration.Score,
            visibleHudSample?.CapturedAt.ToUnixTimeMilliseconds(),
            visibleHudSample?.HealthPercent,
            visibleHudSample?.FoodPercent,
            visibleHudSample?.WaterPercent,
            visibleHudSample?.StaminaPercent,
            _reportedHealthState,
            _reportedFoodState,
            _reportedWaterState,
            _reportedStaminaState,
            _woundObservationId,
            _woundCheckExpanded,
            guidance.HealthFresh,
            guidance.FoodFresh,
            guidance.WaterFresh,
            guidance.StaminaFresh,
            guidance.HealthAgeSeconds,
            guidance.FoodAgeSeconds,
            guidance.WaterAgeSeconds,
            guidance.StaminaAgeSeconds,
            guidance.Urgency,
            guidance.Heading,
            guidance.Action,
            guidance.RoutePinType);
        if (!force && string.Equals(signature, _coreVitalsUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _coreVitalsUiSignature = signature;

        if (_streamerMode)
        {
            CoreVitalsStatusText.Text = "Hidden in streamer mode";
            ReportedHealthButton.Visibility = Visibility.Collapsed;
            ReportedFoodButton.Visibility = Visibility.Collapsed;
            ReportedWaterButton.Visibility = Visibility.Collapsed;
            ReportedStaminaButton.Visibility = Visibility.Collapsed;
            CoreVitalsActionText.Visibility = Visibility.Collapsed;
            CoreVitalsDetailText.Visibility = Visibility.Collapsed;
            CoreVitalsFreshnessText.Visibility = Visibility.Collapsed;
            CoreVitalsRouteButton.Visibility = Visibility.Collapsed;
            CoreVitalsAllStableButton.Visibility = Visibility.Collapsed;
            CoreVitalsClearButton.Visibility = Visibility.Collapsed;
            PlayerSnapshotPanel.Visibility = Visibility.Collapsed;
            VisibleHudSensorPanel.Visibility = Visibility.Collapsed;
            CoreVitalsSourceDisclosureText.Visibility = Visibility.Collapsed;
            WoundCheckToggleButton.Visibility = Visibility.Collapsed;
            WoundCheckPanel.Visibility = Visibility.Collapsed;
            WoundCheckSummaryText.Visibility = Visibility.Collapsed;
            StatusBeaconButton.Content = "VITALS HIDDEN";
            StatusBeaconButton.Foreground = (Brush)FindResource("SecondaryTextBrush");
            StatusBeaconButton.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 100, 116, 139));
            StatusBeaconButton.ToolTip = "Vital details are hidden in Streamer Mode";
            UpdateDockVitals();
            return;
        }

        ReportedHealthButton.Visibility = Visibility.Visible;
        ReportedFoodButton.Visibility = Visibility.Visible;
        ReportedWaterButton.Visibility = Visibility.Visible;
        ReportedStaminaButton.Visibility = Visibility.Visible;
        CoreVitalsActionText.Visibility = Visibility.Visible;
        CoreVitalsDetailText.Visibility = Visibility.Visible;
        CoreVitalsFreshnessText.Visibility = Visibility.Visible;
        CoreVitalsRouteButton.Visibility = Visibility.Visible;
        CoreVitalsAllStableButton.Visibility = Visibility.Visible;
        CoreVitalsClearButton.Visibility = Visibility.Visible;
        PlayerSnapshotPanel.Visibility = Visibility.Visible;
        VisibleHudSensorPanel.Visibility = Visibility.Visible;
        CoreVitalsSourceDisclosureText.Visibility = Visibility.Visible;
        WoundCheckToggleButton.Visibility = Visibility.Visible;
        WoundCheckPanel.Visibility = _woundCheckExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdatePlayerSnapshotPresentation(playerSnapshot, vitalsTrend);
        if (useVisibleHudSample && !useLiveSnapshot)
        {
            CoreVitalsSourceDisclosureText.Text =
                "Visible HUD estimate · foreground The Isle window only · no game memory, packets, input, or stored screenshots.";
        }
        VisibleHudSensorToggleButton.Content = _visibleHudSensorEnabled ? "ON" : "ENABLE";
        VisibleHudSensorStatusText.Text = _visibleHudSensorStatus;
        SetToggleButtonState(VisibleHudSensorToggleButton, _visibleHudSensorEnabled);
        VisibleHudCalibrateButton.Content = _visibleHudCalibration.Score >= 0.45
            ? $"CALIBRATED · {_visibleHudCalibration.Score:P0}"
            : "CALIBRATE HUD";
        VisibleHudCalibrateButton.IsEnabled = _gameWasRunning;
        VisibleHudReadTextButton.Content = "READ VISIBLE TEXT";
        VisibleHudReadTextButton.IsEnabled = _gameWasRunning;

        var accent = guidance.Critical
            ? new SolidColorBrush(Color.FromRgb(255, 112, 112))
            : guidance.Warning
                ? new SolidColorBrush(Color.FromRgb(255, 163, 108))
                : vitalsTrend.Warning
                    ? new SolidColorBrush(Color.FromRgb(255, 178, 74))
                    : (Brush)FindResource("AccentBrush");
        CoreVitalsStatusText.Text = playerSnapshot.LastKnown && !useVisibleHudSample
            ? "NO LIVE VITALS"
            : guidance.Heading;
        CoreVitalsStatusText.Foreground = accent;
        CoreVitalsActionText.Text = useLiveSnapshot && !guidance.Warning
            ? "LIVE VITALS IN RANGE"
            : useVisibleHudSample && !guidance.Warning
                ? "VISIBLE HUD ESTIMATE IN RANGE"
                : guidance.Action;
        CoreVitalsActionText.Foreground = accent;
        CoreVitalsDetailText.Text = useLiveSnapshot && !guidance.Warning
            ? useVisibleHudSample
                ? "HP, food, and water are current from the Isley provider. Stamina is a broad visible-HUD estimate; named conditions remain manual."
                : "HP, food, and water are current from Isley provider. Stamina and named conditions still use Isley's manual controls."
            : useVisibleHudSample
                ? "Broad estimates from pixels already visible in The Isle. No game memory, packets, input, or screenshots are stored; confirm warnings against the in-game HUD."
            : playerSnapshot.LastKnown
                ? "Isley provider is showing the previous dinosaur. Those values are reference-only; report the live in-game bands below."
                : playerSnapshot.Stale
                    ? "The Isley provider snapshot expired safely. Manual Core Vitals are active until a fresh live response arrives."
                    : LiveMapServicesActive && !guidance.HasFreshReport
                        ? "No fresh Isley provider snapshot is available. Manual fallback bands expire after five minutes."
                        : guidance.Detail;
        CoreVitalsFreshnessText.Text = useLiveSnapshot
            ? $"Isley provider HP / food / water {PlayerSnapshotLogic.FormatAge(playerSnapshot.AgeSeconds)} ago" +
              (guidance.StaminaFresh
                  ? $" · manual stamina {CoreVitalsLogic.FormatAge(guidance.StaminaAgeSeconds)} ago"
                  : " · stamina not reported")
            : guidance.Freshness;

        ReportedHealthButton.Content = useLiveSnapshot
            ? $"HP · {playerSnapshot.HealthPercent}%"
            : $"HP - {SurvivalAssistantLogic.HealthLabel(guidance.Health)}";
        ReportedFoodButton.Content = useLiveSnapshot
            ? $"FOOD · {playerSnapshot.FoodPercent}%"
            : $"FOOD - {CoreVitalsLogic.Label(guidance.Food)}";
        ReportedWaterButton.Content = useLiveSnapshot
            ? $"WATER · {playerSnapshot.WaterPercent}%"
            : $"WATER - {CoreVitalsLogic.Label(guidance.Water)}";
        ReportedStaminaButton.Content = $"STAMINA - {CoreVitalsLogic.Label(guidance.Stamina)}";
        if (useVisibleHudSample)
        {
            var visible = visibleHudSample!.Value;
            CoreVitalsFreshnessText.Text = useLiveSnapshot
                ? $"Isley provider HP / food / water {PlayerSnapshotLogic.FormatAge(playerSnapshot.AgeSeconds)} ago · " +
                  $"visible HUD stamina {CoreVitalsLogic.FormatAge(guidance.StaminaAgeSeconds)} ago"
                : $"Visible HUD estimate {CoreVitalsLogic.FormatAge(guidance.HealthAgeSeconds)} ago · broad bands only";
            if (!useLiveSnapshot)
            {
                ReportedHealthButton.Content = $"HP · ~{visible.HealthPercent}%";
                ReportedFoodButton.Content = $"FOOD · ~{visible.FoodPercent}%";
                ReportedWaterButton.Content = $"WATER · ~{visible.WaterPercent}%";
            }
            ReportedStaminaButton.Content = $"STAMINA · ~{visible.StaminaPercent}%";
        }

        ReportedHealthButton.ToolTip = useLiveSnapshot
            ? "Live Isley provider health percentage; click to cycle the manual fallback used when live stats are unavailable"
            : useVisibleHudSample
            ? "Broad estimate from visible damage-edge pixels; click to set the manual fallback"
            : guidance.HealthFresh
            ? $"Manual in-game EKG band reported {CoreVitalsLogic.FormatAge(guidance.HealthAgeSeconds)} ago; click for the next band"
            : "Cycle Unknown, OK, Hurt, and Critical; each selection is timestamped";
        ReportedFoodButton.ToolTip = useLiveSnapshot
            ? "Live Isley provider hunger percentage; click to cycle the manual fallback used when live stats are unavailable"
            : useVisibleHudSample
            ? "Broad estimate from the visible food icon; click to set the manual fallback"
            : guidance.FoodFresh
            ? $"Manual food band reported {CoreVitalsLogic.FormatAge(guidance.FoodAgeSeconds)} ago; click for the next band"
            : "Cycle Unknown, OK, Low, and Empty; each selection is timestamped";
        ReportedWaterButton.ToolTip = useLiveSnapshot
            ? "Live Isley provider thirst percentage; click to cycle the manual fallback used when live stats are unavailable"
            : useVisibleHudSample
            ? "Broad estimate from the visible water icon; click to set the manual fallback"
            : guidance.WaterFresh
            ? $"Manual water band reported {CoreVitalsLogic.FormatAge(guidance.WaterAgeSeconds)} ago; click for the next band"
            : "Cycle Unknown, OK, Low, and Empty; each selection is timestamped";
        ReportedStaminaButton.ToolTip = useVisibleHudSample
            ? "Broad estimate from the visible stamina icon; click to set the manual fallback"
            : guidance.StaminaFresh
            ? $"Manual stamina band reported {CoreVitalsLogic.FormatAge(guidance.StaminaAgeSeconds)} ago; click for the next band"
            : "Cycle Unknown, OK, Low, and Empty; each selection is timestamped";

        SetVitalButtonAppearance(ReportedHealthButton, guidance.HealthFresh, guidance.Health switch
        {
            ReportedHealthState.Critical => 2,
            ReportedHealthState.Hurt => 1,
            _ => 0
        });
        SetVitalButtonAppearance(ReportedFoodButton, guidance.FoodFresh, guidance.Food switch
        {
            ReportedVitalState.Empty => 2,
            ReportedVitalState.Low => 1,
            _ => 0
        });
        SetVitalButtonAppearance(ReportedWaterButton, guidance.WaterFresh, guidance.Water switch
        {
            ReportedVitalState.Empty => 2,
            ReportedVitalState.Low => 1,
            _ => 0
        });
        SetVitalButtonAppearance(ReportedStaminaButton, guidance.StaminaFresh, guidance.Stamina switch
        {
            ReportedVitalState.Empty => 2,
            ReportedVitalState.Low => 1,
            _ => 0
        });

        WoundCheckToggleButton.Content = _woundCheckExpanded
            ? "WOUND CHECK · CLOSE"
            : woundObservation is { } selectedWound
                ? $"WOUND CHECK · {selectedWound.Label} {selectedWound.RangeLabel}"
                : "WOUND CHECK · VISUAL HP FALLBACK";
        WoundCheckToggleButton.ToolTip = useLiveSnapshot
            ? $"Live Isley provider HP {playerSnapshot.HealthPercent}% is active and more precise. Open the visual fallback only as a reference."
            : useVisibleHudSample
                ? $"Visible HUD sensor estimates HP near {visibleHudSample!.Value.HealthPercent}%. Use Wound Check to confirm the broad band."
            : "Open a screenshot-free visual fallback for translating wounds and screen-edge splatter into a broad manual HP band.";
        WoundCheckClearButton.IsEnabled = woundObservation is not null;
        WoundCheckDetailText.Text = woundObservation is { } detailWound
            ? $"{detailWound.VisualCue} {detailWound.Action}"
            : "Choose the closest broad visual state. The source has no distinct 30–40% visual band; when cues conflict, follow the in-game EKG and use the more conservative state.";
        WoundCheckSummaryText.Visibility = woundObservation is not null && !_woundCheckExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (woundObservation is { } summaryWound)
        {
            var manualBand = SurvivalAssistantLogic.HealthLabel(summaryWound.ManualHealth);
            WoundCheckSummaryText.Text = useLiveSnapshot
                ? $"{summaryWound.Label} WOUNDS · {summaryWound.RangeLabel} fallback · LIVE HP {playerSnapshot.HealthPercent}% ACTIVE"
                : $"{summaryWound.Label} WOUNDS · {summaryWound.RangeLabel} · MANUAL HP {manualBand}";
            WoundCheckSummaryText.Foreground = summaryWound.Severity switch
            {
                >= 2 => new SolidColorBrush(Color.FromRgb(255, 112, 112)),
                1 => new SolidColorBrush(Color.FromRgb(255, 163, 108)),
                _ => (Brush)FindResource("AccentBrush")
            };
        }
        SetVitalButtonAppearance(
            WoundLightButton,
            string.Equals(_woundObservationId, WoundCheckLogic.LightId, StringComparison.Ordinal),
            0);
        SetVitalButtonAppearance(
            WoundVisibleButton,
            string.Equals(_woundObservationId, WoundCheckLogic.VisibleId, StringComparison.Ordinal),
            0);
        SetVitalButtonAppearance(
            WoundHeavyButton,
            string.Equals(_woundObservationId, WoundCheckLogic.HeavyId, StringComparison.Ordinal),
            1);
        SetVitalButtonAppearance(
            WoundSevereButton,
            string.Equals(_woundObservationId, WoundCheckLogic.SevereId, StringComparison.Ordinal),
            2);

        CoreVitalsRouteButton.Content = guidance.RouteLabel;
        CoreVitalsRouteButton.IsEnabled = !string.IsNullOrEmpty(guidance.RoutePinType);
        CoreVitalsRouteButton.ToolTip = string.IsNullOrEmpty(guidance.RoutePinType)
            ? "Report a low or empty band to enable the matching recovery route"
            : $"Route to the nearest saved {guidance.RoutePinType} marker";
        CoreVitalsClearButton.IsEnabled = _reportedHealthState != ReportedHealthState.Unknown
                                           || _reportedFoodState != ReportedVitalState.Unknown
                                           || _reportedWaterState != ReportedVitalState.Unknown
                                           || _reportedStaminaState != ReportedVitalState.Unknown;
        var liveCompactLabel = useLiveSnapshot
            ? $"HP {playerSnapshot.HealthPercent}{VitalsTrendLogic.FooterGlyph(vitalsTrend.Health)} · " +
              $"F {playerSnapshot.FoodPercent}{VitalsTrendLogic.FooterGlyph(vitalsTrend.Food)} · " +
              $"W {playerSnapshot.WaterPercent}{VitalsTrendLogic.FooterGlyph(vitalsTrend.Water)} · " +
              $"ST {CoreVitalsLogic.ShortLabel(guidance.Stamina)}"
            : string.Empty;
        var visibleCompactLabel = useVisibleHudSample
            ? $"~HP {visibleHudSample!.Value.HealthPercent} · " +
              $"~F {visibleHudSample.Value.FoodPercent} · " +
              $"~W {visibleHudSample.Value.WaterPercent} · " +
              $"~ST {visibleHudSample.Value.StaminaPercent}"
            : string.Empty;
        StatusBeaconButton.Content = useLiveSnapshot
            ? liveCompactLabel
            : useVisibleHudSample
                ? visibleCompactLabel
                : guidance.CompactLabel;
        StatusBeaconButton.Foreground = accent;
        StatusBeaconButton.BorderBrush = accent;
        StatusBeaconButton.ToolTip = useLiveSnapshot
            ? $"Live Isley provider Core Vitals{liveSpeciesTooltip} · growth {playerSnapshot.GrowthPercent}% · refreshed " +
               $"{PlayerSnapshotLogic.FormatAge(playerSnapshot.AgeSeconds)} ago" +
               (vitalsTrend.Health.Rising
                   ? $" · {VitalsTrendLogic.HealthRecoveryDetail(vitalsTrend.Health)}"
                   : string.Empty) +
               (vitalsTrend.Warning ? $" · {vitalsTrend.WarningHeading.ToLowerInvariant()}" : string.Empty) +
              ". Click to inspect."
            : guidance.HasFreshReport
            ? $"Player-reported Core Vitals - {guidance.Freshness}. Click to update."
            : playerSnapshot.LastKnown
                ? "Isley provider has a last-dino reference, but no live vitals; click to inspect or report the current in-game bands"
                : playerSnapshot.Stale
                    ? "Isley provider stats are stale and excluded from decisions; click to inspect or use the manual fallback"
                    : LiveMapServicesActive
                        ? "Isley provider stats are waiting; click to inspect or report the in-game bands manually"
                : "This server session uses manual Core Vitals; click to report the in-game bands";

        if (useVisibleHudSample && !useLiveSnapshot)
        {
            StatusBeaconButton.ToolTip =
                "Broad visible-HUD estimates. No game memory, packets, input, or screenshots are stored. Click to inspect.";
        }

        var decisionSignature = string.Join('|',
            _streamerMode,
            guidance.Urgency,
            guidance.Heading,
            guidance.Action,
            guidance.Detail,
            guidance.RoutePinType,
            guidance.BriefLabel);
        if (!string.Equals(decisionSignature, _coreVitalsDecisionSignature, StringComparison.Ordinal))
        {
            _coreVitalsDecisionSignature = decisionSignature;
            CoreVitalsActionText.BeginAnimation(
                OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(
                    0.35,
                    1,
                    TimeSpan.FromMilliseconds(160)));
            UpdateNextMove();
            UpdateTripReadiness();
            UpdateTacticalBrief();
        }
        UpdateDockVitals();
    }

    private void UpdatePlayerSnapshotPresentation(
        PlayerSnapshotEvaluation snapshot,
        VitalsTrendAnalysis trend)
    {
        var speciesName = LiveSpeciesBridgeLogic.DisplayName(snapshot.SpeciesId);
        var speciesIdentityLine = string.IsNullOrEmpty(speciesName)
            ? string.Empty
            : $"{speciesName.ToUpperInvariant()} · ";
        PlayerSnapshotRefreshButton.IsEnabled = LiveMapServicesActive && !_streamerMode;
        PlayerSnapshotRefreshButton.Content = _playerSnapshotTransportState == "refreshing"
            ? "SYNCING"
            : "REFRESH";

        if (!LiveMapServicesActive)
        {
            PlayerSnapshotStateText.Text = "MANUAL SERVER SESSION";
            PlayerSnapshotStateText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            PlayerSnapshotValuesText.Text = "Use the four manual bands below for this server.";
            PlayerSnapshotPrimeText.Text = "Live current-dino stats are available only in Live Map mode.";
            SetVitalsTrendPresentation(
                "TREND · LIVE MAP ONLY",
                "Resource trends require fresh opt-in provider snapshots.",
                (Brush)FindResource("SecondaryTextBrush"));
            CoreVitalsSourceDisclosureText.Text =
                "Manual session-only bands expire after five minutes; no game memory or private server feed is read.";
            return;
        }

        if (snapshot.LiveFresh)
        {
            var cadenceLabel = _liteModeEnabled ? "5S LITE FEED" : "2S LIVE FEED";
            PlayerSnapshotStateText.Text =
                $"ISLEY PROVIDER LIVE · {cadenceLabel} · {PlayerSnapshotLogic.FormatAge(snapshot.AgeSeconds)}" +
                (_playerSnapshotTransportState == "error" ? " · RETRYING" : string.Empty);
            PlayerSnapshotStateText.Foreground = (Brush)FindResource("AccentBrush");
            PlayerSnapshotValuesText.Text =
                $"{speciesIdentityLine}GROWTH {snapshot.GrowthPercent}%\n" +
                $"HP {snapshot.HealthPercent}% · FOOD {snapshot.FoodPercent}% · WATER {snapshot.WaterPercent}%";
            PlayerSnapshotPrimeText.Text = snapshot.PrimeAvailable
                ? $"PRIME {snapshot.PrimeCompleted}/{snapshot.PrimeRequired} · {snapshot.PrimeCompleted} OF {snapshot.PrimeTotal} CONDITIONS"
                : "Prime progress is not present in this current-dino snapshot.";
            UpdateVitalsTrendPresentation(trend);
            CoreVitalsSourceDisclosureText.Text =
                "Same opt-in local provider · two-second numeric refresh in Full mode, " +
                "five seconds in Lite · stamina and named conditions stay manual.";
            return;
        }

        if (snapshot.LastKnown)
        {
            PlayerSnapshotStateText.Text = "LAST DINO · NOT LIVE";
            PlayerSnapshotStateText.Foreground = new SolidColorBrush(Color.FromRgb(255, 178, 74));
            PlayerSnapshotValuesText.Text =
                $"{speciesIdentityLine}GROWTH {snapshot.GrowthPercent}%\n" +
                $"HP {snapshot.HealthPercent}% · FOOD {snapshot.FoodPercent}% · WATER {snapshot.WaterPercent}%";
            PlayerSnapshotPrimeText.Text = snapshot.PrimeAvailable
                ? $"LAST PRIME PROGRESS {snapshot.PrimeCompleted}/{snapshot.PrimeRequired} · reference only"
                : "No last-known Prime progress was reported.";
            SetVitalsTrendPresentation(
                "TREND PAUSED · LAST DINO",
                "Offline last-dino values never enter resource-trend analysis.",
                (Brush)FindResource("SecondaryTextBrush"));
            CoreVitalsSourceDisclosureText.Text =
                "Offline values are reference-only and never drive alerts, routing, Trip Check, or Fight Check.";
            return;
        }

        if (snapshot.Stale)
        {
            PlayerSnapshotStateText.Text = $"ISLEY PROVIDER STALE · {PlayerSnapshotLogic.FormatAge(snapshot.AgeSeconds)}";
            PlayerSnapshotStateText.Foreground = new SolidColorBrush(Color.FromRgb(255, 178, 74));
            PlayerSnapshotValuesText.Text = "Live values expired safely; manual fallback is active.";
            PlayerSnapshotPrimeText.Text = "Select Refresh or wait for Isley provider to reconnect.";
            SetVitalsTrendPresentation(
                "TREND PAUSED · SNAPSHOT STALE",
                "Trend guidance is disabled until a fresh live snapshot arrives.",
                new SolidColorBrush(Color.FromRgb(255, 178, 74)));
            CoreVitalsSourceDisclosureText.Text =
                "Stale provider values never drive alerts, routing, Trip Check, or Fight Check.";
            return;
        }

        PlayerSnapshotStateText.Text = _playerSnapshotTransportState switch
        {
            "refreshing" => "ISLEY PROVIDER STATS SYNCING",
            "error" => "ISLEY PROVIDER STATS RETRYING",
            _ => "ISLEY PROVIDER STATS WAITING"
        };
        PlayerSnapshotStateText.Foreground = (Brush)FindResource("SecondaryTextBrush");
        PlayerSnapshotValuesText.Text =
            "Add a fresh local positions.json provider file to sync species, HP, food, water, and growth.";
        PlayerSnapshotPrimeText.Text = "Manual Core Vitals below remain available at all times.";
        SetVitalsTrendPresentation(
            "TREND · WAITING FOR LIVE SAMPLES",
            "Three fresh live snapshots are required before Isley shows a resource direction.",
            (Brush)FindResource("SecondaryTextBrush"));
        CoreVitalsSourceDisclosureText.Text =
            "Only one allowlisted species ID and bounded numeric stats cross from the local provider file; page HTML, cookies, and identity never do.";
    }

    private void UpdateVitalsTrendPresentation(VitalsTrendAnalysis trend)
    {
        var anyFalling = trend.Health.Direction == VitalTrendDirection.Falling
                         || trend.Food.Direction == VitalTrendDirection.Falling
                         || trend.Water.Direction == VitalTrendDirection.Falling;
        var anyRising = trend.Health.Direction == VitalTrendDirection.Rising
                        || trend.Food.Direction == VitalTrendDirection.Rising
                        || trend.Water.Direction == VitalTrendDirection.Rising;
        var brush = trend.Warning || anyFalling
            ? new SolidColorBrush(Color.FromRgb(255, 178, 74))
            : anyRising
                ? new SolidColorBrush(Color.FromRgb(110, 231, 183))
                : (Brush)FindResource("SecondaryTextBrush");
        var healthDetail = VitalsTrendLogic.HealthRecoveryDetail(trend.Health);
        var tooltip = trend.Warning
            ? $"{trend.WarningDetail} {healthDetail}"
            : trend.Active
                ? $"{healthDetail} Food and water arrows require the same steady evidence and " +
                  "reset independently after either resource rises by 3%."
                : "Three fresh live snapshots are required before Isley shows a vitals direction.";
        SetVitalsTrendPresentation(trend.CompactLabel, tooltip, brush);
    }

    private void SetVitalsTrendPresentation(string text, string tooltip, Brush brush)
    {
        var signature = $"{text}|{tooltip}|{brush}";
        PlayerSnapshotTrendText.Text = text;
        PlayerSnapshotTrendText.ToolTip = tooltip;
        PlayerSnapshotTrendText.Foreground = brush;
        if (string.Equals(signature, _vitalsTrendUiSignature, StringComparison.Ordinal))
        {
            return;
        }

        _vitalsTrendUiSignature = signature;
        PlayerSnapshotTrendText.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0.42,
                1,
                TimeSpan.FromMilliseconds(160)));
    }

    private void VisibleHudSensorToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode) return;
        _visibleHudSensorEnabled = !_visibleHudSensorEnabled;
        _visibleHudSensorSamples.Clear();
        _visibleHudSensorSample = null;
        _visibleHudSensorStatus = _visibleHudSensorEnabled
            ? "ARMED · switch to The Isle to begin"
            : "OFF · enable for continuous estimated vitals";
        _coreVitalsUiSignature = string.Empty;
        if (_visibleHudSensorEnabled)
        {
            RefreshVisibleHudSensor();
        }
        UpdateCoreVitals(force: true);
        SaveSettings();
    }

    private async void VisibleHudCalibrateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode) return;
        var gameWindow = FindTheIsleWindow();
        if (gameWindow == 0)
        {
            _visibleHudSensorStatus = "CALIBRATION WAITING · start The Isle first";
            UpdateCoreVitals(force: true);
            return;
        }

        VisibleHudCalibrateButton.IsEnabled = false;
        VisibleHudCalibrateButton.Content = "CALIBRATING";
        await Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        if (!VisibleHudSensor.TryCalibrate(
                gameWindow,
                DateTimeOffset.UtcNow,
                out var calibration))
        {
            _visibleHudSensorStatus =
                "CALIBRATION MISSED · show the bottom-right HUD, then try again";
            UpdateCoreVitals(force: true);
            return;
        }

        _visibleHudCalibration = calibration;
        _visibleHudSensorEnabled = true;
        _visibleHudSensorSamples.Clear();
        _visibleHudSensorSample = null;
        _visibleHudSensorStatus =
            $"CALIBRATED · scale {calibration.Scale:0.00} · alignment {calibration.Score:P0}";
        SaveSettings();
        UpdateCoreVitals(force: true);
        await ShowHotkeyToastAsync("VISIBLE HUD CALIBRATED", true);
    }

    private async void VisibleHudReadTextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode) return;
        var gameWindow = FindTheIsleWindow();
        if (gameWindow == 0)
        {
            _visibleHudSensorStatus = "TEXT READ WAITING · start The Isle first";
            UpdateCoreVitals(force: true);
            return;
        }

        VisibleHudReadTextButton.IsEnabled = false;
        VisibleHudReadTextButton.Content = "READING";
        try
        {
            var readout = await VisibleHudTextScanner.ReadAsync(gameWindow);
            var capturedAt = DateTimeOffset.UtcNow;
            if (readout.Position is { } position)
            {
                AcceptUniversalCoordinateCapture(position);
            }
            if (readout.HealthPercent is { } health)
            {
                _reportedHealthState = VisibleHudSensorLogic.HealthState(health);
                _reportedHealthReportedAt = capturedAt;
            }
            if (readout.FoodPercent is { } food)
            {
                _reportedFoodState = VisibleHudSensorLogic.VitalState(food);
                _reportedFoodReportedAt = capturedAt;
            }
            if (readout.WaterPercent is { } water)
            {
                _reportedWaterState = VisibleHudSensorLogic.VitalState(water);
                _reportedWaterReportedAt = capturedAt;
            }
            if (readout.StaminaPercent is { } stamina)
            {
                _reportedStaminaState = VisibleHudSensorLogic.VitalState(stamina);
                _reportedStaminaReportedAt = capturedAt;
            }

            _visibleHudSensorStatus = readout.FieldCount > 0
                ? $"VISIBLE TEXT · {readout.Summary}"
                : "VISIBLE TEXT · no supported location or vital text found";
            AddTacticalEvent(
                "VITALS",
                "Visible text read",
                readout.FieldCount > 0
                    ? $"{readout.FieldCount} allowlisted fields applied; raw image and text discarded"
                    : "No allowlisted fields applied; raw image and text discarded");
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            or NotSupportedException
            or COMException)
        {
            _visibleHudSensorStatus =
                "VISIBLE TEXT UNAVAILABLE · Windows OCR could not read this display mode";
        }
        finally
        {
            _coreVitalsUiSignature = string.Empty;
            UpdateCoreVitals(force: true);
        }
    }

    private void ReportedHealthButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode) return;
        _woundObservationId = string.Empty;
        var current = CurrentCoreVitalsGuidance();
        _reportedHealthState = SurvivalAssistantLogic.NextHealthState(
            current.HealthFresh ? _reportedHealthState : ReportedHealthState.Unknown);
        _reportedHealthReportedAt = _reportedHealthState == ReportedHealthState.Unknown
            ? default
            : DateTimeOffset.UtcNow;
        _coreVitalsUiSignature = string.Empty;
        AddTacticalEvent("VITALS", "Health band updated",
            $"Manual EKG · {SurvivalAssistantLogic.HealthLabel(_reportedHealthState)}",
            warning: _reportedHealthState is ReportedHealthState.Hurt or ReportedHealthState.Critical);
        UpdateCoreVitals(force: true);
    }

    private void ReportedFoodButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode) return;
        var current = CurrentCoreVitalsGuidance();
        _reportedFoodState = CoreVitalsLogic.Next(
            current.FoodFresh ? _reportedFoodState : ReportedVitalState.Unknown);
        _reportedFoodReportedAt = _reportedFoodState == ReportedVitalState.Unknown
            ? default
            : DateTimeOffset.UtcNow;
        _coreVitalsUiSignature = string.Empty;
        AddTacticalEvent("VITALS", "Food band updated",
            $"Manual HUD · {CoreVitalsLogic.Label(_reportedFoodState)}",
            warning: _reportedFoodState is ReportedVitalState.Low or ReportedVitalState.Empty);
        UpdateCoreVitals(force: true);
    }

    private void ReportedWaterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode) return;
        var current = CurrentCoreVitalsGuidance();
        _reportedWaterState = CoreVitalsLogic.Next(
            current.WaterFresh ? _reportedWaterState : ReportedVitalState.Unknown);
        _reportedWaterReportedAt = _reportedWaterState == ReportedVitalState.Unknown
            ? default
            : DateTimeOffset.UtcNow;
        _coreVitalsUiSignature = string.Empty;
        AddTacticalEvent("VITALS", "Water band updated",
            $"Manual HUD · {CoreVitalsLogic.Label(_reportedWaterState)}",
            warning: _reportedWaterState is ReportedVitalState.Low or ReportedVitalState.Empty);
        UpdateCoreVitals(force: true);
    }

    private void ReportedStaminaButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode) return;
        var current = CurrentCoreVitalsGuidance();
        _reportedStaminaState = CoreVitalsLogic.Next(
            current.StaminaFresh ? _reportedStaminaState : ReportedVitalState.Unknown);
        _reportedStaminaReportedAt = _reportedStaminaState == ReportedVitalState.Unknown
            ? default
            : DateTimeOffset.UtcNow;
        _coreVitalsUiSignature = string.Empty;
        AddTacticalEvent("VITALS", "Stamina band updated",
            $"Manual HUD · {CoreVitalsLogic.Label(_reportedStaminaState)}",
            warning: _reportedStaminaState is ReportedVitalState.Low or ReportedVitalState.Empty);
        UpdateCoreVitals(force: true);
    }

    private void CoreVitalsAllStableButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode) return;
        var now = DateTimeOffset.UtcNow;
        _woundObservationId = string.Empty;
        _reportedHealthState = ReportedHealthState.Stable;
        _reportedHealthReportedAt = now;
        _reportedFoodState = ReportedVitalState.Stable;
        _reportedFoodReportedAt = now;
        _reportedWaterState = ReportedVitalState.Stable;
        _reportedWaterReportedAt = now;
        _reportedStaminaState = ReportedVitalState.Stable;
        _reportedStaminaReportedAt = now;
        _coreVitalsUiSignature = string.Empty;
        AddTacticalEvent("VITALS", "All bands reported OK", "Manual HUD snapshot · expires in 5m");
        UpdateCoreVitals(force: true);
    }

    private void CoreVitalsClearButton_Click(object sender, RoutedEventArgs e) =>
        ClearCoreVitals(logEvent: true, updateUi: true);

    private void WoundCheckToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode) return;
        _woundCheckExpanded = !_woundCheckExpanded;
        _coreVitalsUiSignature = string.Empty;
        UpdateCoreVitals(force: true);
    }

    private void WoundObservationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode
            || sender is not Button { Tag: string requestedId }
            || WoundCheckLogic.Find(requestedId) is not { } observation)
        {
            return;
        }

        _woundObservationId = observation.Id;
        _reportedHealthState = observation.ManualHealth;
        _reportedHealthReportedAt = DateTimeOffset.UtcNow;
        _coreVitalsUiSignature = string.Empty;
        AddTacticalEvent(
            "VITALS",
            "Visual wound estimate applied",
            $"{observation.Label} {observation.RangeLabel} · manual HP {SurvivalAssistantLogic.HealthLabel(observation.ManualHealth)}",
            warning: observation.Severity > 0);
        UpdateCoreVitals(force: true);
    }

    private void WoundCheckClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || string.IsNullOrEmpty(_woundObservationId)) return;
        _woundObservationId = string.Empty;
        _reportedHealthState = ReportedHealthState.Unknown;
        _reportedHealthReportedAt = default;
        _coreVitalsUiSignature = string.Empty;
        AddTacticalEvent("VITALS", "Visual wound estimate cleared", "Manual HP returned to unknown");
        UpdateCoreVitals(force: true);
    }

    private void WoundCheckGuideButton_Click(object sender, RoutedEventArgs e) =>
        OpenExternalUri(OverlayLinks.CombatGuide);

    private async void PlayerSnapshotRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || !LiveMapServicesActive || _playerSnapshotTransportState == "refreshing")
        {
            return;
        }

        _playerSnapshotTransportState = "refreshing";
        _coreVitalsUiSignature = string.Empty;
        UpdateCoreVitals(force: true);
        var started = await ExecuteMapperCommandAsync(
            "window.__isley?.refreshPlayerSnapshot() ?? false");
        if (!started)
        {
            _playerSnapshotTransportState = "error";
            _coreVitalsUiSignature = string.Empty;
            UpdateCoreVitals(force: true);
            await ShowHotkeyToastAsync("CURRENT DINO STATS ARE NOT CONNECTED", false);
        }
    }

    private void ClearCoreVitals(bool logEvent, bool updateUi)
    {
        var hadReport = _reportedHealthState != ReportedHealthState.Unknown
                        || _reportedFoodState != ReportedVitalState.Unknown
                        || _reportedWaterState != ReportedVitalState.Unknown
                        || _reportedStaminaState != ReportedVitalState.Unknown;
        _reportedHealthState = ReportedHealthState.Unknown;
        _reportedHealthReportedAt = default;
        _reportedFoodState = ReportedVitalState.Unknown;
        _reportedFoodReportedAt = default;
        _reportedWaterState = ReportedVitalState.Unknown;
        _reportedWaterReportedAt = default;
        _reportedStaminaState = ReportedVitalState.Unknown;
        _reportedStaminaReportedAt = default;
        _woundObservationId = string.Empty;
        _woundCheckExpanded = false;
        _coreVitalsUiSignature = string.Empty;
        _coreVitalsDecisionSignature = string.Empty;
        if (hadReport && logEvent)
        {
            AddTacticalEvent("VITALS", "Vital reports cleared", "No manual band active");
        }
        if (updateUi)
        {
            UpdateCoreVitals(force: true);
        }
    }

    private async void CoreVitalsRouteButton_Click(object sender, RoutedEventArgs e)
    {
        var guidance = CurrentCoreVitalsGuidance();
        if (_streamerMode || string.IsNullOrEmpty(guidance.RoutePinType)) return;
        var routed = await ExecuteMapperCommandAsync(
            $"window.__isley?.routeToNearestPinType('{guidance.RoutePinType}') ?? false");
        if (routed)
        {
            AddTacticalEvent("VITALS", "Recovery route started",
                $"Nearest saved {guidance.RoutePinType} marker");
            await ShowHotkeyToastAsync(
                $"ROUTING TO SAVED {guidance.RoutePinType.ToUpperInvariant()} PIN",
                true);
            return;
        }

        if (guidance.RoutePinType == "food")
        {
            var layerEnabled = _foodLayer is true || await ExecuteMapperCommandAsync(
                "window.__isley?.setOfficialLayer('food', true) ?? false");
            await ShowHotkeyToastAsync(
                layerEnabled ? "FOOD LAYER ON - NO SAVED FOOD PIN" : "SAVE A FOOD PIN OR OPEN LAYERS",
                layerEnabled);
            if (layerEnabled)
            {
                AddTacticalEvent("VITALS", "Food layer opened", "No saved food route was available");
            }
            return;
        }

        await ShowHotkeyToastAsync($"SAVE A {guidance.RoutePinType.ToUpperInvariant()} PIN FIRST", false);
    }
}
