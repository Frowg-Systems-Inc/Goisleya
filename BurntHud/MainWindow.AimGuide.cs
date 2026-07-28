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
    private void AimGuideButton_Click(object sender, RoutedEventArgs e)
    {
        _aimGuideEnabled = !_aimGuideEnabled;
        UpdateAimGuidePresentation();
        UpdateHudSurfaceControls();
        SaveSettings();
    }

    private void AimGuideModeButton_Click(object sender, RoutedEventArgs e)
    {
        _aimGuideModeIndex = (_aimGuideModeIndex + 1) % 3;
        ResetAimCalibrationEvidence();
        UpdateAimGuidePresentation();
        SaveSettings();
    }

    private void AimGuideAttackButton_Click(object sender, RoutedEventArgs e)
    {
        _aimGuideAttackIndex = AimCalibrationLogic.NextAttackIndex(_aimGuideAttackIndex);
        ApplyAimCalibrationForSelection(useDefaultsWhenMissing: true);
        SaveSettings();
    }

    private void AimGuideGrowthButton_Click(object sender, RoutedEventArgs e)
    {
        if (_aimGuideGrowthSyncEnabled)
        {
            _aimGuideGrowthIndex = CurrentAimGrowthContext().Index;
        }
        _aimGuideGrowthSyncEnabled = false;
        _aimGuideGrowthIndex = AimCalibrationLogic.NextGrowthIndex(_aimGuideGrowthIndex);
        ApplyAimCalibrationForSelection(useDefaultsWhenMissing: true);
        SaveSettings();
    }

    private void AimGuideGrowthSyncButton_Click(object sender, RoutedEventArgs e)
    {
        _aimGuideGrowthSyncEnabled = !_aimGuideGrowthSyncEnabled;
        ApplyAimCalibrationForSelection(useDefaultsWhenMissing: true);
        SaveSettings();
    }

    private void AimGuideCameraButton_Click(object sender, RoutedEventArgs e)
    {
        _aimGuideCameraIndex = AimCalibrationLogic.NextCameraIndex(_aimGuideCameraIndex);
        ApplyAimCalibrationForSelection(useDefaultsWhenMissing: true);
        SaveSettings();
    }

    private void UpsertCurrentAimCalibrationProfile(string speciesId, int growthIndex) =>
        AimCalibrationLogic.Upsert(_aimCalibrationProfiles, new AimCalibrationProfile(
            speciesId,
            AimCalibrationLogic.AttackId(_aimGuideAttackIndex),
            growthIndex,
            _aimGuideCameraIndex,
            _aimGuideModeIndex,
            _aimGuideSize,
            _aimGuideDepthScale,
            _aimGuideHorizontalOffset,
            _aimGuideVerticalOffset,
            _aimGuideConfirmedMatches,
            _aimGuideInsideMisses,
            _aimGuideOutsideHits,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

    private async void AimGuideSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (FieldGuideLogic.Find(ActiveAimCalibrationSpeciesId()) is not { } species) return;
        var growthContext = CurrentAimGrowthContext(species.Id);

        UpsertCurrentAimCalibrationProfile(species.Id, growthContext.Index);
        UpdateAimGuidePresentation();
        SaveSettings();
        await ShowHotkeyToastAsync(
            $"{species.Name.ToUpperInvariant()} · {AimCalibrationLogic.AttackLabel(_aimGuideAttackIndex)} · CALIBRATION SAVED",
            true);
    }

    private async void AimGuideConfirmMatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (FieldGuideLogic.Find(ActiveAimCalibrationSpeciesId()) is not { } species) return;
        var growthContext = CurrentAimGrowthContext(species.Id);

        _aimGuideConfirmedMatches = Math.Clamp(
            _aimGuideConfirmedMatches + 1,
            0,
            AimCalibrationLogic.MaxConfirmedMatches);
        UpsertCurrentAimCalibrationProfile(species.Id, growthContext.Index);
        UpdateAimGuidePresentation();
        SaveSettings();
        await ShowHotkeyToastAsync(
            $"USER-REPORTED EDGE MATCH x{_aimGuideConfirmedMatches} · PROFILE SAVED",
            true);
    }

    private async void AimGuideEvidenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string evidenceId }
            || FieldGuideLogic.Find(ActiveAimCalibrationSpeciesId()) is not { } species)
        {
            return;
        }

        switch (evidenceId)
        {
            case "inside-miss":
                _aimGuideInsideMisses = Math.Clamp(
                    _aimGuideInsideMisses + 1,
                    0,
                    AimCalibrationLogic.MaxEvidenceReports);
                break;
            case "outside-hit":
                _aimGuideOutsideHits = Math.Clamp(
                    _aimGuideOutsideHits + 1,
                    0,
                    AimCalibrationLogic.MaxEvidenceReports);
                break;
            default:
                return;
        }

        var growthContext = CurrentAimGrowthContext(species.Id);
        UpsertCurrentAimCalibrationProfile(species.Id, growthContext.Index);
        var evidence = AimCalibrationLogic.EvaluateEvidence(
            _aimGuideConfirmedMatches,
            _aimGuideInsideMisses,
            _aimGuideOutsideHits);
        UpdateAimGuidePresentation();
        SaveSettings();
        await ShowHotkeyToastAsync(
            $"{(evidenceId == "inside-miss" ? "INSIDE MISS" : "OUTSIDE HIT")} LOGGED · {evidence.Label}",
            true);
    }

    private async void AimGuideClearEvidenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (FieldGuideLogic.Find(ActiveAimCalibrationSpeciesId()) is not { } species) return;

        var growthContext = CurrentAimGrowthContext(species.Id);
        if (AimCalibrationLogic.TryFind(
                _aimCalibrationProfiles,
                species.Id,
                _aimGuideAttackIndex,
                growthContext.Index,
                _aimGuideCameraIndex,
                out var savedProfile))
        {
            AimCalibrationLogic.Upsert(_aimCalibrationProfiles, savedProfile with
            {
                ConfirmedMatches = 0,
                InsideMisses = 0,
                OutsideHits = 0,
                UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }
        ResetAimCalibrationEvidence();
        UpdateAimGuidePresentation();
        SaveSettings();
        await ShowHotkeyToastAsync("AIM TEST EVIDENCE CLEARED · GEOMETRY KEPT", true);
    }

    private async void AimGuideResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (FieldGuideLogic.Find(ActiveAimCalibrationSpeciesId()) is not { } species) return;
        var growthContext = CurrentAimGrowthContext(species.Id);

        AimCalibrationLogic.Remove(
            _aimCalibrationProfiles,
            species.Id,
            _aimGuideAttackIndex,
            growthContext.Index,
            _aimGuideCameraIndex);
        SetDefaultAimCalibration();
        UpdateAimGuidePresentation();
        SaveSettings();
        await ShowHotkeyToastAsync(
            $"{species.Name.ToUpperInvariant()} · {AimCalibrationLogic.AttackLabel(_aimGuideAttackIndex)} · PROFILE RESET",
            true);
    }

    private void ApplyAimCalibrationForSelection(
        bool useDefaultsWhenMissing,
        bool updatePresentation = true,
        bool force = true)
    {
        var speciesId = ResolveAimCalibrationSpeciesId();
        var growthContext = CurrentAimGrowthContext(speciesId);
        if (!force
            && string.Equals(speciesId, _aimGuideAppliedSpeciesId, StringComparison.OrdinalIgnoreCase)
            && growthContext.Index == _aimGuideAppliedGrowthIndex)
        {
            if (updatePresentation) UpdateAimGuidePresentation();
            return;
        }

        _aimGuideAppliedSpeciesId = speciesId;
        _aimGuideAppliedGrowthIndex = growthContext.Index;
        if (AimCalibrationLogic.TryFind(
                _aimCalibrationProfiles,
                speciesId,
                _aimGuideAttackIndex,
                growthContext.Index,
                _aimGuideCameraIndex,
                out var profile))
        {
            _aimGuideModeIndex = profile.ModeIndex;
            _aimGuideSize = profile.Size;
            _aimGuideDepthScale = profile.DepthScale;
            _aimGuideHorizontalOffset = profile.HorizontalOffset;
            _aimGuideVerticalOffset = profile.VerticalOffset;
            _aimGuideConfirmedMatches = profile.ConfirmedMatches;
            _aimGuideInsideMisses = profile.InsideMisses;
            _aimGuideOutsideHits = profile.OutsideHits;
        }
        else if (useDefaultsWhenMissing)
        {
            SetDefaultAimCalibration();
        }

        if (updatePresentation)
        {
            UpdateAimGuidePresentation();
        }
    }

    private string ResolveAimCalibrationSpeciesId()
    {
        var liveSpecies = CurrentLiveSpeciesBridge();
        return AimCalibrationLogic.ResolveSpeciesId(
            liveSpecies.Available,
            liveSpecies.LiveSpeciesId,
            _guideSelectedSpeciesId,
            speciesId => FieldGuideLogic.Find(speciesId) is not null);
    }

    private string ActiveAimCalibrationSpeciesId() =>
        FieldGuideLogic.Find(_aimGuideAppliedSpeciesId)?.Id ?? ResolveAimCalibrationSpeciesId();

    private void SetDefaultAimCalibration()
    {
        _aimGuideModeIndex = AimCalibrationLogic.DefaultModeIndex;
        _aimGuideSize = AimCalibrationLogic.DefaultSize;
        _aimGuideDepthScale = AimCalibrationLogic.DefaultDepthScale;
        _aimGuideHorizontalOffset = AimCalibrationLogic.DefaultHorizontalOffset;
        _aimGuideVerticalOffset = AimCalibrationLogic.DefaultVerticalOffset;
        ResetAimCalibrationEvidence();
    }

    private void ResetAimCalibrationEvidence()
    {
        _aimGuideConfirmedMatches = 0;
        _aimGuideInsideMisses = 0;
        _aimGuideOutsideHits = 0;
    }

    private void AimGuideSizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && double.TryParse(value, out var delta))
        {
            _aimGuideSize = Math.Clamp(_aimGuideSize + delta, 90, 520);
            ResetAimCalibrationEvidence();
            UpdateAimGuidePresentation();
            SaveSettings();
        }
    }

    private void AimGuideOffsetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && double.TryParse(value, out var delta))
        {
            _aimGuideVerticalOffset = Math.Clamp(_aimGuideVerticalOffset + delta, -240, 240);
            ResetAimCalibrationEvidence();
            UpdateAimGuidePresentation();
            SaveSettings();
        }
    }

    private void AimGuideHorizontalOffsetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && double.TryParse(value, out var delta))
        {
            _aimGuideHorizontalOffset = Math.Clamp(_aimGuideHorizontalOffset + delta, -240, 240);
            ResetAimCalibrationEvidence();
            UpdateAimGuidePresentation();
            SaveSettings();
        }
    }

    private void AimGuideDepthButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && double.TryParse(value, out var delta))
        {
            _aimGuideDepthScale = Math.Clamp(_aimGuideDepthScale + delta, 0.55, 1.40);
            ResetAimCalibrationEvidence();
            UpdateAimGuidePresentation();
            SaveSettings();
        }
    }

    private void AimGuideElementButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string element }) return;

        switch (element)
        {
            case "area":
                _aimGuideAreaVisible = !_aimGuideAreaVisible;
                break;
            case "center":
                _aimGuideCenterCueVisible = !_aimGuideCenterCueVisible;
                break;
            case "uncertainty":
                _aimGuideUncertaintyVisible = !_aimGuideUncertaintyVisible;
                break;
            case "label":
                _aimGuideLabelVisible = !_aimGuideLabelVisible;
                break;
            default:
                return;
        }

        UpdateAimGuidePresentation();
        SaveSettings();
    }

    private void UpdateAimGuidePresentation()
    {
        if (AimGuideButton is null || AimGuideModeButton is null || AimGuideAttackButton is null
            || AimGuideGrowthButton is null || AimGuideGrowthSyncButton is null
            || AimGuideCameraButton is null
            || AimGuideAreaButton is null || AimGuideCenterButton is null
            || AimGuideUncertaintyButton is null || AimGuideLabelButton is null
            || AimGuideConfirmMatchButton is null
            || AimGuideInsideMissButton is null || AimGuideOutsideHitButton is null
            || AimGuideEvidenceStatusText is null
            || AimGuideStatusText is null
            || AimGuideViewportStatusText is null)
        {
            return;
        }

        var mode = (AimGuideMode)Math.Clamp(_aimGuideModeIndex, 0, 2);
        AimGuideButton.Content = _aimGuideEnabled ? "Aim guide · On" : "Aim guide · Off";
        AimGuideModeButton.Content = mode switch
        {
            AimGuideMode.Reticle => "Guide shape · Reticle",
            AimGuideMode.FrontAndRear => "Guide shape · Front + rear",
            _ => "Guide shape · Front arc"
        };
        AimGuideAttackButton.Content = $"Attack profile · {AimCalibrationLogic.AttackLabel(_aimGuideAttackIndex)}";
        var speciesId = ActiveAimCalibrationSpeciesId();
        var growthContext = CurrentAimGrowthContext(speciesId);
        var growthLabel = AimCalibrationLogic.GrowthLabel(growthContext.Index);
        var growthRange = AimCalibrationLogic.GrowthRangeLabel(growthContext.Index);
        var overlayGrowthLabel = growthContext.Live
            ? $"{growthLabel} {growthContext.Percent}%"
            : growthLabel;
        AimGuideGrowthButton.Content = $"Growth · {growthLabel}";
        AimGuideGrowthButton.ToolTip = growthContext.Live
            ? $"Fresh Live Map growth is selecting {growthLabel} ({growthRange}). Click to switch to the next manual context."
            : "Cycle the manual Hatchling, Juvenile, Subadult, Adult, or Elder calibration context";
        AimGuideGrowthSyncButton.Content = growthContext.Live
            ? $"LIVE GROWTH · {growthContext.Percent}%"
            : _aimGuideGrowthSyncEnabled
                ? "LIVE GROWTH · WAITING"
                : "LIVE GROWTH · OFF";
        AimGuideGrowthSyncButton.ToolTip = _aimGuideGrowthSyncEnabled
            ? "Fresh provider growth selects the matching calibration automatically; click for manual control"
            : "Use fresh provider growth to select the matching calibration automatically";
        AimGuideCameraButton.Content = $"Camera · {AimCalibrationLogic.CameraLabel(_aimGuideCameraIndex)}";
        var species = FieldGuideLogic.Find(speciesId);
        var speciesLabel = species?.Name.ToUpperInvariant() ?? speciesId.ToUpperInvariant();
        var profileState = AimCalibrationLogic.TryFind(
            _aimCalibrationProfiles,
            speciesId,
            _aimGuideAttackIndex,
            growthContext.Index,
            _aimGuideCameraIndex,
            out var savedProfile)
            ? AimCalibrationLogic.Matches(
                savedProfile,
                _aimGuideModeIndex,
                _aimGuideSize,
                _aimGuideDepthScale,
                _aimGuideHorizontalOffset,
                _aimGuideVerticalOffset)
                ? "SAVED USER CALIBRATION"
                : "MODIFIED · SAVE TO KEEP"
            : "UNSAVED USER CALIBRATION";
        var evidence = AimCalibrationLogic.EvaluateEvidence(
            _aimGuideConfirmedMatches,
            _aimGuideInsideMisses,
            _aimGuideOutsideHits);
        var confidence = AimCalibrationLogic.ConfidenceLabel(
            _aimGuideConfirmedMatches,
            _aimGuideInsideMisses,
            _aimGuideOutsideHits);
        var growthSource = growthContext.Live
            ? $"LIVE GROWTH {growthContext.Percent}%"
            : _aimGuideGrowthSyncEnabled
                ? $"LIVE GROWTH WAITING · MANUAL {growthRange}"
                : $"MANUAL GROWTH {growthRange}";
        AimGuideStatusText.Text =
            $"{speciesLabel} · {AimCalibrationLogic.AttackLabel(_aimGuideAttackIndex)} · " +
            $"{growthLabel} · {AimCalibrationLogic.CameraLabel(_aimGuideCameraIndex)}\n" +
            $"{growthSource} · {profileState} · {confidence}\n" +
            $"{(_aimGuideEnabled ? "ON" : "OFF")} · {_aimGuideSize:0} PX WIDE · " +
            $"{_aimGuideDepthScale:0.00}× DEPTH · X {_aimGuideHorizontalOffset:+0;-0;0} · " +
            $"Y {_aimGuideVerticalOffset:+0;-0;0}";
        AimGuideConfirmMatchButton.Content = $"MATCH {_aimGuideConfirmedMatches}";
        AimGuideInsideMissButton.Content = $"MISS IN {_aimGuideInsideMisses}";
        AimGuideOutsideHitButton.Content = $"HIT OUT {_aimGuideOutsideHits}";
        AimGuideEvidenceStatusText.Text =
            $"{evidence.Label} · M {evidence.Matches} · IN MISS {evidence.InsideMisses} · " +
            $"OUT HIT {evidence.OutsideHits}\n{evidence.Instruction}";
        AimGuideEvidenceStatusText.Foreground = evidence.HasContradiction
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("SecondaryTextBrush");
        SetToggleButtonState(AimGuideButton, _aimGuideEnabled);
        SetToggleButtonState(AimGuideGrowthSyncButton, _aimGuideGrowthSyncEnabled);
        SetToggleButtonState(AimGuideAreaButton, _aimGuideAreaVisible);
        SetToggleButtonState(AimGuideCenterButton, _aimGuideCenterCueVisible);
        SetToggleButtonState(AimGuideUncertaintyButton, _aimGuideUncertaintyVisible);
        SetToggleButtonState(AimGuideLabelButton, _aimGuideLabelVisible);
        AimGuideAreaButton.Content = _aimGuideAreaVisible ? "AREA · ON" : "AREA · OFF";
        AimGuideCenterButton.Content = _aimGuideCenterCueVisible ? "CENTER · ON" : "CENTER · OFF";
        AimGuideUncertaintyButton.Content = _aimGuideUncertaintyVisible ? "MARGIN · ON" : "MARGIN · OFF";
        AimGuideLabelButton.Content = _aimGuideLabelVisible ? "LABEL · ON" : "LABEL · OFF";
        _aimGuideWindow?.Configure(
            mode,
            _aimGuideSize,
            _aimGuideDepthScale,
            _aimGuideHorizontalOffset,
            _aimGuideVerticalOffset,
            _aimGuideAreaVisible,
            _aimGuideCenterCueVisible,
            _aimGuideUncertaintyVisible,
            _aimGuideLabelVisible,
            speciesLabel,
            AimCalibrationLogic.AttackLabel(_aimGuideAttackIndex),
            overlayGrowthLabel,
            AimCalibrationLogic.CameraLabel(_aimGuideCameraIndex),
            _aimGuideConfirmedMatches,
            _aimGuideInsideMisses,
            _aimGuideOutsideHits);
        RefreshAimGuideVisibility();
    }

    private void RefreshAimGuideVisibility()
    {
        if (!_aimGuideEnabled || _streamerMode || _isDocked)
        {
            _aimGuideWindow?.Hide();
            var hiddenReason = !_aimGuideEnabled
                ? "AIM GUIDE OFF"
                : _streamerMode
                    ? "STREAMER MODE"
                    : "MINIMIZED";
            SetAimGuideViewportStatus(
                $"VIEWPORT · HIDDEN · {hiddenReason}",
                warning: false);
            return;
        }

        var foreground = GetPlayFocusForeground();
        var shouldShow = foreground == PlayFocusForeground.Mapper
                         || _gameWasRunning && foreground == PlayFocusForeground.Game;
        if (!shouldShow)
        {
            _aimGuideWindow?.Hide();
            SetAimGuideViewportStatus(
                "VIEWPORT · WAITING FOR ISLEY OR THE ISLE",
                warning: false);
            return;
        }

        _aimGuideWindow ??= new AimGuideWindow();
        var growthContext = CurrentAimGrowthContext(ActiveAimCalibrationSpeciesId());
        var growthLabel = AimCalibrationLogic.GrowthLabel(growthContext.Index);
        _aimGuideWindow.Configure(
            (AimGuideMode)Math.Clamp(_aimGuideModeIndex, 0, 2),
            _aimGuideSize,
            _aimGuideDepthScale,
            _aimGuideHorizontalOffset,
            _aimGuideVerticalOffset,
            _aimGuideAreaVisible,
            _aimGuideCenterCueVisible,
            _aimGuideUncertaintyVisible,
            _aimGuideLabelVisible,
            FieldGuideLogic.Find(ActiveAimCalibrationSpeciesId())?.Name.ToUpperInvariant()
                ?? ActiveAimCalibrationSpeciesId().ToUpperInvariant(),
            AimCalibrationLogic.AttackLabel(_aimGuideAttackIndex),
            growthContext.Live ? $"{growthLabel} {growthContext.Percent}%" : growthLabel,
            AimCalibrationLogic.CameraLabel(_aimGuideCameraIndex),
            _aimGuideConfirmedMatches,
            _aimGuideInsideMisses,
            _aimGuideOutsideHits);

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var alignment = _aimGuideWindow.AlignToForegroundViewport(
            foregroundWindow,
            foreground == PlayFocusForeground.Game);
        switch (alignment)
        {
            case AimGuideAlignmentMode.GameClient:
                SetAimGuideViewportStatus(
                    "VIEWPORT · GAME CLIENT ALIGNED",
                    warning: false);
                break;
            case AimGuideAlignmentMode.MonitorPreview:
                SetAimGuideViewportStatus(
                    "VIEWPORT · MONITOR PREVIEW",
                    warning: false);
                break;
            case AimGuideAlignmentMode.MonitorFallback:
                SetAimGuideViewportStatus(
                    "VIEWPORT · MONITOR FALLBACK · VERIFY ALIGNMENT",
                    warning: true);
                break;
            default:
                SetAimGuideViewportStatus(
                    "VIEWPORT · ALIGNMENT UNAVAILABLE",
                    warning: true);
                break;
        }

        if (!_aimGuideWindow.IsVisible)
        {
            _aimGuideWindow.Show();
        }
    }

    private void SetAimGuideViewportStatus(string text, bool warning)
    {
        if (AimGuideViewportStatusText is null)
        {
            return;
        }

        AimGuideViewportStatusText.Text = text;
        AimGuideViewportStatusText.Foreground = warning
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("SecondaryTextBrush");
    }
}
