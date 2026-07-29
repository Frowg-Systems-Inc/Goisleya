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
    // Clipboard capture tick (capture-sound preference, session baseline).
    private bool _clipboardCaptureSoundEnabled = true;
    private UniversalCoordinatePoint? _captureTickLastPoint;

    // Lite Mode auto-suggest (session-scoped, never auto-applies).
    private bool _playFocusTickSeen;
    private DateTimeOffset _playFocusLastTickUtc;
    private int _liteModeSampleCount;
    private int _liteModeStarvedStreak;
    private double _liteModeLastStarvedRatio;
    private bool _liteModeSuggestOffered;
    private bool _liteModeSuggestSnoozed;
    private bool _liteModeSuggestTapArmed;
    private bool _liteModeSuggestTapWired;
    private int _liteModeSuggestRevision;
    private string _liteModeSuggestOfferMessage = string.Empty;

    // Layout profiles + programmatic settings UI (XAML stays untouched).
    private readonly List<HudLayoutProfile> _hudLayoutProfiles = [];
    private bool _overlayExtrasUiBuilt;
    private TextBlock? _layoutProfilesHeading;
    private TextBlock? _layoutProfilesStatusText;
    private TextBox? _layoutProfileNameBox;
    private Button? _layoutProfileSaveButton;
    private StackPanel? _layoutProfileListPanel;
    private string _layoutProfilesUiSignature = string.Empty;
    private Button? _captureSoundButton;
    private TextBlock? _captureSoundStatusText;
    private Button? _diagnosticsExportButton;
    private TextBlock? _diagnosticsStatusText;

    private void RefreshGameState()
    {
        var gameIsRunning = IsAnyProcessRunning(
            "TheIsleClient-Win64-Shipping",
            "TheIsle",
            "TheIsleClient");

        var wasRunning = _gameWasRunning;
        if (gameIsRunning && !wasRunning)
        {
            _gameSessionStartedAt = DateTimeOffset.Now;
        }
        else if (!gameIsRunning)
        {
            _gameSessionStartedAt = null;
        }

        _gameWasRunning = gameIsRunning;
        if (!_gameStateInitialized)
        {
            _gameStateInitialized = true;
            if (gameIsRunning)
            {
                AddTacticalEvent("SYSTEM", "The Isle detected", "Game process is running");
                EnsureOverlayPriority(forceToggle: true);
                _ = HandleGameStartedLocationResumeAsync();
            }
        }
        else if (gameIsRunning && !wasRunning)
        {
            AddTacticalEvent("SYSTEM", "The Isle started", "Game process detected");
            EnsureOverlayPriority(forceToggle: true);
            _ = HandleGameStartedLocationResumeAsync();
        }
        else if (!gameIsRunning && wasRunning)
        {
            AddTacticalEvent("SYSTEM", "The Isle closed", "Game process is no longer running");
        }
        var sessionMinutes = _gameSessionStartedAt is null
            ? 0
            : Math.Max(0, (int)(DateTimeOffset.Now - _gameSessionStartedAt.Value).TotalMinutes);
        GameStatusText.Text = gameIsRunning ? $"GAME LIVE {sessionMinutes}m" : "GAME OFFLINE";
        GameStatusDot.Fill = new SolidColorBrush(
            gameIsRunning
                ? Color.FromRgb(90, 210, 132)
                : Color.FromRgb(119, 115, 108));
        RefreshPlayFocus();
        RefreshAimGuideVisibility();
        UpdateServerStatusPresentation();
    }

    private static bool IsAnyProcessRunning(params string[] processNames)
    {
        foreach (var processName in processNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                var found = processes.Length > 0;
                foreach (var process in processes)
                {
                    process.Dispose();
                }

                if (found)
                {
                    return true;
                }
            }
            catch
            {
                // A process enumeration failure should not take down the minimap.
            }
        }

        return false;
    }

    private void SetLoading(bool visible, string detail)
    {
        LoadingOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        LoadingDetailText.Text = detail;
    }

    private void ApplyControlStates()
    {
        UpdateAutoLocateOnGameStartPresentation();
        OpacityButton.Content = $"{Opacity * 100:0}";
        SizeButton.Content = _expanded ? "SMALL" : "SIZE";
        UpdateOverlayLockPresentation();
        UpdateMapLightMode(animate: IsLoaded);
        UpdateHudDetailModeControls();
        UpdateSmartHudControls();
        ApplyLiteModeProfile();
        UpdateIsleyUpdatePresentation();
        UpdateHudDockLayout();
        UpdateHudSurfaceControls();
        HeadingModeButton.Content = _headingUp ? "Heading up" : "North up";
        UpdateSmartFollowControls();
        PlayerFilterButton.Content = _friendOnly ? "Friends only" : "All authorized players";
        PlayerLabelsButton.Content = _playerLabelsVisible ? "Names on" : "Names off";
        UpdateMarkerStyleControl();
        var trailSeconds = _trailDurations[_trailDurationIndex];
        TrailLengthButton.Content = trailSeconds == 0 ? "Trails off" : $"Trails {trailSeconds}s";
        UpdateRangeRingControl();
        UpdateMapGridControl();
        UpdateLandmarkLabelDensityControl();
        UpdateBreadcrumbTrailControls();
        UpdateExplorationControls();
        FriendRadarButton.Content = _friendRadarVisible ? "Pack HUD on" : "Pack HUD off";
        EncounterHudButton.Content = _encounterHudVisible ? "Encounter HUD on" : "Encounter HUD off";
        NearestPlaceButton.Content = _nearestPlaceVisible ? "Nearby place HUD on" : "Nearby place HUD off";
        StreamerModeButton.Content = _streamerMode ? "Streamer mode on" : "Streamer mode off";
        StaleSoundButton.Content = _staleSoundEnabled ? "Stale alert sound on" : "Stale alert sound off";
        AlwaysOnTopButton.Content = _alwaysOnTop ? "Always on top" : "Normal window level";
        SetToggleButtonState(HeadingModeButton, _headingUp);
        SetToggleButtonState(FollowFramingButton, _lookAheadEnabled);
        SetToggleButtonState(SmartZoomButton, _smartZoomEnabled && !_smartZoomSuspended);
        SetToggleButtonState(PlayerFilterButton, _friendOnly);
        SetToggleButtonState(PlayerLabelsButton, _playerLabelsVisible);
        SetToggleButtonState(TrailLengthButton, trailSeconds > 0);
        SetToggleButtonState(RangeRingsButton, _rangeRingsVisible);
        SetToggleButtonState(FriendRadarButton, _friendRadarVisible);
        SetToggleButtonState(EncounterHudButton, _encounterHudVisible);
        SetToggleButtonState(NearestPlaceButton, _nearestPlaceVisible);
        SetToggleButtonState(StreamerModeButton, _streamerMode);
        SetToggleButtonState(StaleSoundButton, _staleSoundEnabled);
        SetToggleButtonState(AlwaysOnTopButton, _alwaysOnTop);
        UpdateServerSessionPresentation();
        UpdatePlayFocusPresentation();
        UpdateHotkeyStatus();
        UpdateHotkeyStudio(force: true);
        UpdateVitalsHudControl();
        UpdateAimGuidePresentation();
        UpdateVoicePresentation();
        UpdateFieldGuide();
        UpdateToolsSection();
        UpdateOfficialLayerControls();
        UpdatePinControls();
        UpdateRecentRoutes();
        UpdatePinLibrary();
        UpdateRoutePlanControls();
        UpdateMeasurementStatus();
        UpdateRecoveryControls();
        UpdateNavigationReadout(_markerAvailable);
        UpdateSoundFinder(force: true);
        UpdateNearestPlaceContext();
        UpdateMapScaleBar();
        UpdateSessionStats();
        UpdateTacticalBrief();
        UpdateTacticalLog();
        UpdateBreadcrumbTrailControls();
        UpdateExplorationControls();
        UpdateDangerProximity();
        UpdateEncounterAwareness();
        UpdateFriendProximity();
        UpdateFriendRoster();
        UpdateSurvivalTimers(force: true);
        UpdateCoreVitals(force: true);
        UpdateSurvivalAssistant(force: true);
        UpdateFieldConditions(force: true);
        UpdateWaterCrossingCheck(force: true);
        UpdateShorelineCheck(force: true);
        UpdateTripReadiness(force: true);
        UpdateLifeRun(force: true);
        UpdateSettingsStorageStatus();
        EnsureOverlayExtrasUi();
        UpdateCaptureSoundControls();
        UpdateLayoutProfileControls(force: true);
    }

    private void ToggleVisibility()
    {
        if (_isDocked)
        {
            SetDocked(false);
            return;
        }

        if (!IsVisible && _playFocusSuppressed)
        {
            EnterPlayFocusInteraction();
            return;
        }

        if (_visibilityRequested && IsVisible)
        {
            if (_commandPaletteOpen)
            {
                CloseCommandPalette(returnFocus: false);
            }
            _visibilityRequested = false;
            _playFocusSuppressed = false;
            _playFocusInteractionOverride = false;
            Hide();
            UpdatePlayFocusPresentation();
            return;
        }

        _visibilityRequested = true;
        _playFocusInteractionOverride = _playFocusEnabled;
        Show();
        Topmost = _alwaysOnTop;
        if (_playFocusEnabled)
        {
            SetClickThrough(false);
            Activate();
            NativeMethods.SetForegroundWindow(_windowHandle);
        }
        UpdatePlayFocusPresentation();
    }

    private void ToggleInteractionMode()
    {
        if (!_playFocusEnabled)
        {
            SetClickThrough(!_clickThrough);
            return;
        }

        if (!IsVisible || _playFocusSuppressed || _clickThrough)
        {
            EnterPlayFocusInteraction();
            return;
        }

        _playFocusInteractionOverride = false;
        SetClickThrough(true);
        UpdatePlayFocusPresentation();
    }

    private void EnterPlayFocusInteraction()
    {
        if (_isDocked)
        {
            SetDocked(false);
        }
        _visibilityRequested = true;
        _playFocusSuppressed = false;
        _playFocusInteractionOverride = true;
        if (!IsVisible)
        {
            Show();
        }
        Topmost = _alwaysOnTop;
        SetClickThrough(false);
        Activate();
        if (_windowHandle != 0)
        {
            NativeMethods.SetForegroundWindow(_windowHandle);
        }
        UpdatePlayFocusPresentation();
    }

    private static PlayFocusForeground GetPlayFocusForeground()
    {
        var foregroundWindow = NativeMethods.GetForegroundWindow();
        if (foregroundWindow == 0)
        {
            return PlayFocusForeground.Other;
        }

        NativeMethods.GetWindowThreadProcessId(foregroundWindow, out var processId);
        if (processId == Environment.ProcessId)
        {
            return PlayFocusForeground.Mapper;
        }

        try
        {
            return IsTheIsleWindow(foregroundWindow)
                ? PlayFocusForeground.Game
                : PlayFocusForeground.Other;
        }
        catch
        {
            return PlayFocusForeground.Other;
        }
    }

    private static bool IsTheIsleWindow(nint windowHandle)
    {
        if (windowHandle == 0) return false;
        NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
        if (processId == 0 || processId == Environment.ProcessId) return false;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName is "TheIsleClient-Win64-Shipping" or "TheIsle" or "TheIsleClient";
        }
        catch
        {
            return false;
        }
    }

    private static nint FindTheIsleWindow()
    {
        foreach (var processName in new[]
                 {
                     "TheIsleClient-Win64-Shipping",
                     "TheIsle",
                     "TheIsleClient"
                 })
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    using (process)
                    {
                        if (process.MainWindowHandle != 0)
                        {
                            return process.MainWindowHandle;
                        }
                    }
                }
            }
            catch
            {
                // A process can close between enumeration and handle discovery.
            }
        }
        return 0;
    }

    private void RefreshPlayFocus()
    {
        TrackPlayFocusSignals(DateTimeOffset.UtcNow);
        TrackClipboardCaptureTick();

        if (_isDocked)
        {
            if (IsVisible) Hide();
            if (_dockWindow is not null) _dockWindow.Topmost = _alwaysOnTop;
            UpdatePlayFocusPresentation();
            return;
        }

        if (!IsLoaded || !_playFocusEnabled)
        {
            UpdatePlayFocusPresentation();
            return;
        }

        if (!_visibilityRequested)
        {
            if (IsVisible)
            {
                Hide();
            }
            _playFocusSuppressed = false;
            UpdatePlayFocusPresentation();
            return;
        }

        if (!_gameWasRunning)
        {
            _playFocusSuppressed = false;
            if (!IsVisible)
            {
                Show();
                Topmost = _alwaysOnTop;
            }
            UpdatePlayFocusPresentation();
            return;
        }

        switch (GetPlayFocusForeground())
        {
            case PlayFocusForeground.Mapper:
                _playFocusSuppressed = false;
                if (!IsVisible)
                {
                    Show();
                    Topmost = _alwaysOnTop;
                }
                if (_playFocusInteractionOverride && _clickThrough)
                {
                    SetClickThrough(false);
                }
                break;
            case PlayFocusForeground.Game:
                _playFocusInteractionOverride = false;
                _playFocusSuppressed = false;
                if (!IsVisible)
                {
                    Show();
                    Topmost = _alwaysOnTop;
                }
                if (!_clickThrough)
                {
                    SetClickThrough(true);
                }
                break;
            default:
                _playFocusInteractionOverride = false;
                _playFocusSuppressed = true;
                if (IsVisible)
                {
                    Hide();
                }
                break;
        }

        UpdatePlayFocusPresentation();
        EnsureOverlayPriority();
    }

    private void EnsureOverlayPriority(bool forceToggle = false)
    {
        if (_isDocked)
        {
            if (_dockWindow is not null
                && OverlayZOrderLogic.ShouldHoldAboveGame(
                    _alwaysOnTop,
                    _dockWindow.IsVisible,
                    _dockWindow.IsLoaded))
            {
                _dockWindow.Topmost = true;
                _dockWindow.EnsureTopMost(forceToggle);
            }
            return;
        }

        if (!OverlayZOrderLogic.ShouldHoldAboveGame(_alwaysOnTop, IsVisible, IsLoaded))
        {
            return;
        }

        // WPF Topmost can lose to fullscreen/borderless game focus on older GPUs;
        // reassert native HWND_TOPMOST without stealing activation from The Isle.
        if (!Topmost)
        {
            Topmost = true;
        }

        if (_windowHandle == 0)
        {
            _windowHandle = new WindowInteropHelper(this).Handle;
        }

        NativeMethods.TryReassertTopMost(_windowHandle, forceToggle);
    }

    private void UpdatePlayFocusPresentation()
    {
        if (PlayFocusButton is null || PlayFocusStatusText is null)
        {
            return;
        }

        PlayFocusButton.Content = _playFocusEnabled ? "Play Focus · On" : "Play Focus · Off";
        SetToggleButtonState(PlayFocusButton, _playFocusEnabled);
        if (!_playFocusEnabled)
        {
            PlayFocusStatusText.Text = "Manual visibility and interaction";
        }
        else if (!_visibilityRequested)
        {
            PlayFocusStatusText.Text = "Manually hidden · Ctrl+Shift+O restores";
        }
        else if (!_gameWasRunning)
        {
            PlayFocusStatusText.Text = "Waiting for The Isle · Mapper stays interactive";
        }
        else if (_playFocusSuppressed)
        {
            PlayFocusStatusText.Text = "Paused over other apps · Ctrl+Shift+I opens Mapper";
        }
        else if (_clickThrough)
        {
            PlayFocusStatusText.Text = "The Isle foreground · clicks pass through";
        }
        else
        {
            PlayFocusStatusText.Text = "Mapper open · interactive";
        }
    }

    private void SetClickThrough(bool enabled)
    {
        if (_windowHandle == 0)
        {
            return;
        }

        if (enabled && !IsHotkeyRegistered(HotkeyBindingLogic.InteractionId))
        {
            InteractionStatusText.Text = "CLICK-THROUGH DISABLED: HOTKEY UNAVAILABLE";
            InteractionStatusText.Foreground = (Brush)FindResource("AccentBrush");
            return;
        }

        var extendedStyle = NativeMethods.GetWindowLong(_windowHandle, NativeMethods.GwlExStyle);
        extendedStyle = enabled
            ? extendedStyle | NativeMethods.WsExTransparent
            : extendedStyle & ~NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLong(_windowHandle, NativeMethods.GwlExStyle, extendedStyle);

        _clickThrough = enabled;
        if (enabled && _commandPaletteOpen)
        {
            CloseCommandPalette(returnFocus: false);
        }
        if (enabled && _toolsOpen)
        {
            SetToolsOpen(false);
        }
        ClickThroughButton.Content = enabled ? "CLICK-THROUGH" : "INTERACT";
        UpdateHotkeyStatus();
    }

    private void UpdateDockVitals(bool animate = true) =>
        _dockWindow?.UpdateVitals(CurrentDockVitalsPresentation(), animate);

    private void SurvivalIncidentHudDetailButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || SurvivalAssistantLogic.Find(_survivalIncidentId) is null) return;
        if (CurrentResponsiveOverlayPresentation().IsMicroLayout)
        {
            OpenMapToolsAtSection("survival-assistant");
            return;
        }
        _survivalIncidentHudCollapsed = !_survivalIncidentHudCollapsed;
        _survivalIncidentUiSignature = string.Empty;
        UpdateSurvivalAssistant(force: true);
        SaveSettings();
    }

    private async Task ShowHotkeyToastAsync(string message, bool success)
    {
        if (!IsLoaded || HotkeyToastBorder is null)
        {
            return;
        }

        var revision = ++_hotkeyToastRevision;
        HotkeyToastText.Text = message;
        HotkeyToastBorder.BorderBrush = success
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("WarningBrush");
        HotkeyToastBorder.BeginAnimation(OpacityProperty, null);
        HotkeyToastBorder.Opacity = 1;
        HotkeyToastBorder.Visibility = Visibility.Visible;
        await Task.Delay(1350);
        if (!IsLoaded || revision != _hotkeyToastRevision)
        {
            return;
        }

        HotkeyToastBorder.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                1,
                0,
                TimeSpan.FromMilliseconds(220)));
        await Task.Delay(240);
        if (IsLoaded && revision == _hotkeyToastRevision)
        {
            HotkeyToastBorder.Visibility = Visibility.Collapsed;
            HotkeyToastBorder.BeginAnimation(OpacityProperty, null);
            HotkeyToastBorder.Opacity = 1;
        }
    }

    private static void OpenExternalUri(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // The embedded map remains available if Windows cannot open a browser.
        }
    }

    private void DragHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_overlayLocked && e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_overlayLocked && !IsUnlockButtonInput(e.OriginalSource))
        {
            e.Handled = true;
        }
    }

    private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_overlayLocked && !IsUnlockButtonInput(e.OriginalSource))
        {
            e.Handled = true;
        }
    }

    private bool IsUnlockButtonInput(object? originalSource)
    {
        if (originalSource is not DependencyObject current)
        {
            return false;
        }

        while (current is not null)
        {
            if (ReferenceEquals(current, LockButton))
            {
                return true;
            }

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_overlayLocked)
        {
            e.Handled = true;
            return;
        }

        ScrollViewer? target = null;
        if (_commandPaletteOpen && CommandPaletteBorder.IsMouseOver)
        {
            target = CommandPaletteScrollViewer;
        }
        else if (OnboardingTutorialLayer.Visibility == Visibility.Visible
                 && OnboardingTutorialLayer.IsMouseOver)
        {
            target = OnboardingScrollViewer;
        }
        else if (_toolsOpen && ToolsDrawer.IsMouseOver)
        {
            target = ToolsScrollViewer;
        }
        else if (UniversalSessionSurface.Visibility == Visibility.Visible
                 && UniversalSessionSurface.IsMouseOver)
        {
            target = UniversalSessionScrollViewer;
        }

        if (target is null || target.ScrollableHeight <= 0)
        {
            return;
        }

        var offsetChange = e.Delta > 0 ? -72d : 72d;
        target.ScrollToVerticalOffset(Math.Clamp(
            target.VerticalOffset + offsetChange,
            0,
            target.ScrollableHeight));
        e.Handled = true;
    }

    private void LockButton_Click(object sender, RoutedEventArgs e) => ToggleOverlayLock();

    private void ToggleOverlayLock()
    {
        _overlayLocked = !_overlayLocked;
        if (_overlayLocked)
        {
            SetClickThrough(false);
            Mouse.Capture(null);
            _ = CancelMapPointerGestureAsync();
        }
        UpdateOverlayLockPresentation();
        SaveSettings();
    }

    private void UpdateOverlayLockPresentation()
    {
        if (LockButton is null || LockGlyphText is null || ResizeGrip is null)
        {
            return;
        }

        LockGlyphText.Text = _overlayLocked ? "\uE72E" : "\uE785";
        LockButton.ToolTip = _overlayLocked
            ? "Unlock Isley; every other point passes clicks through to the game"
            : "Lock Isley in place";
        System.Windows.Automation.AutomationProperties.SetName(
            LockButton,
            _overlayLocked ? "Unlock Isley overlay" : "Lock Isley overlay");
        System.Windows.Automation.AutomationProperties.SetHelpText(
            LockButton,
            _overlayLocked
                ? "The only clickable control while Isley is locked"
                : "Makes the overlay ignore all pointer input except this unlock button");
        SetToggleButtonState(LockButton, _overlayLocked);

        LiveMapWebView.IsHitTestVisible = !_overlayLocked;
        ResizeGrip.IsEnabled = !_overlayLocked;
        ResizeGrip.Cursor = _overlayLocked ? Cursors.Arrow : Cursors.SizeNWSE;
        ResizeGrip.Opacity = _overlayLocked ? 0.25 : 1;
        ResizeGrip.ToolTip = _overlayLocked
            ? "Unlock Isley before resizing"
            : "Drag to resize the overlay";
        if (_overlayLocked && IsVisible)
        {
            LockButton.Focus();
        }
        UpdateIsleyUpdatePresentation();
        UpdateHotkeyStatus();
        _dockWindow?.UpdateLockState(_overlayLocked);
    }

    private async void NativeChrome_MouseEnter(object sender, MouseEventArgs e)
    {
        if (LiveMapWebView.CoreWebView2 is null || !_followControllerInstalled)
        {
            return;
        }

        await ExecuteMapperCommandAsync(
            "window.__isley?.dismissTacticalUi() ?? false");
    }

    private void OpacityButton_Click(object sender, RoutedEventArgs e)
    {
        _opacityIndex = (_opacityIndex + 1) % _opacityLevels.Length;
        Opacity = _opacityLevels[_opacityIndex];
        OpacityButton.Content = $"{Opacity * 100:0}";
    }

    private void MapLightModeButton_Click(object sender, RoutedEventArgs e) => CycleMapLightMode();

    private void CycleMapLightMode()
    {
        _mapLightModeIndex = (_mapLightModeIndex + 1) % _mapLightModeOpacities.Length;
        UpdateMapLightMode(animate: true);
        SaveSettings();
    }

    private void UpdateMapLightMode(bool animate)
    {
        if (MapLightOverlay is null || MapLightModeButton is null || MapLightModeStatusText is null)
        {
            return;
        }

        _mapLightModeIndex = Math.Clamp(_mapLightModeIndex, 0, _mapLightModeOpacities.Length - 1);
        var mode = _mapLightModeLabels[_mapLightModeIndex];
        var targetOpacity = _mapLightModeOpacities[_mapLightModeIndex];
        var currentOpacity = MapLightOverlay.Opacity;
        MapLightOverlay.BeginAnimation(OpacityProperty, null);
        MapLightOverlay.Opacity = targetOpacity;
        if (animate && Math.Abs(currentOpacity - targetOpacity) > 0.001)
        {
            var transition = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = currentOpacity,
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                },
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
            };
            MapLightOverlay.BeginAnimation(OpacityProperty, transition);
        }

        MapLightModeButton.Content = $"Map light · {mode}";
        MapLightModeStatusText.Text = _mapLightModeIndex switch
        {
            1 => "Reduced terrain glare · HUD and alerts stay clear",
            2 => "Low-light terrain · HUD and alerts stay clear",
            _ => "Full terrain brightness"
        };
        MapLightModeButton.ToolTip = _mapLightModeIndex switch
        {
            1 => "Dim mode reduces only the map plane while desktop guidance stays bright",
            2 => "Night mode applies the strongest terrain dimming while desktop guidance stays bright",
            _ => "Day mode uses the bundled map's full terrain brightness"
        };
        SetToggleButtonState(MapLightModeButton, _mapLightModeIndex > 0);
    }

    private void HudDetailButton_Click(object sender, RoutedEventArgs e) => CycleHudDetailMode();

    private void CycleHudDetailMode()
    {
        _hudDetailModeIndex = (_hudDetailModeIndex + 1) % _hudDetailModeLabels.Length;
        UpdateHudDetailModeControls();
        UpdateNavigationReadout(_markerAvailable);
        UpdateNearestPlaceContext();
        UpdateEncounterAwareness();
        UpdateFriendProximity();
        UpdateFieldConditions(force: true);
        UpdateLifeRun(force: true);
        SaveSettings();
    }

    private void UpdateHudDetailModeControls()
    {
        if (HudDetailButton is null || HudDetailStatusText is null)
        {
            return;
        }

        _hudDetailModeIndex = Math.Clamp(_hudDetailModeIndex, 0, _hudDetailModeLabels.Length - 1);
        HudDetailButton.Content = $"HUD detail · {_hudDetailModeLabels[_hudDetailModeIndex]}";
        HudDetailStatusText.Text = _hudDetailModeIndex switch
        {
            1 => "Navigation, active pack routes, and warnings · ambient cards hidden",
            2 => "Map-first · dedicated routes, timers, and safety warnings only",
            _ => "All enabled HUD cards visible"
        };
        HudDetailButton.ToolTip = _hudDetailModeIndex switch
        {
            1 => "Essential keeps navigation, active pack routes, and every warning while hiding ambient place and player cards",
            2 => "Clean hides ambient cards while dedicated route guidance, timers, danger, recovery, and safety warnings remain available",
            _ => "Full shows every HUD card you have enabled"
        };
        SetToggleButtonState(HudDetailButton, _hudDetailModeIndex > 0);
    }

    private void SmartHudButton_Click(object sender, RoutedEventArgs e) => ToggleSmartHud();

    private void ToggleSmartHud()
    {
        _smartHudEnabled = !_smartHudEnabled;
        _smartHudUiSignature = string.Empty;
        RefreshSmartHudPresentation(force: true);
        SaveSettings();
    }

    private async void LiteModeButton_Click(object sender, RoutedEventArgs e) =>
        await SetLiteModeAsync(!_liteModeEnabled);

    private async Task SetLiteModeAsync(bool enabled)
    {
        _liteModeEnabled = enabled;
        _liteModeStarvedStreak = 0;
        _liteModeSuggestTapArmed = false;
        _liteModeSuggestRevision++;
        ApplyLiteModeProfile();
        await ApplyMapOptionsAsync();
        _coreVitalsUiSignature = string.Empty;
        UpdateCoreVitals(force: true);
        SaveSettings();
    }

    private void ApplyLiteModeProfile()
    {
        var profile = LiteModeLogic.Resolve(_liteModeEnabled);
        _gamePollTimer.Interval = TimeSpan.FromMilliseconds(profile.GamePollMilliseconds);
        _playFocusTimer.Interval = TimeSpan.FromMilliseconds(profile.PlayFocusMilliseconds);
        _survivalTimerTick.Interval = TimeSpan.FromMilliseconds(profile.SurvivalRefreshMilliseconds);
        _voiceStatusTimer.Interval = TimeSpan.FromMilliseconds(profile.VoiceStatusMilliseconds);
        Shell.Effect = profile.UseShellShadow ? ShellShadowEffect : null;

        if (!profile.UseContinuousAnimations)
        {
            _survivalIncidentFinalMinutePulsing = false;
            SurvivalIncidentHudBorder.BeginAnimation(OpacityProperty, null);
            SurvivalIncidentHudBorder.Opacity = 1;
            _serverRestartWatchPulsing = false;
            ServerRestartWatchHudBorder.BeginAnimation(OpacityProperty, null);
            ServerRestartWatchHudBorder.Opacity = 1;
        }

        UpdateLiteModeControls(profile);
    }

    private void UpdateLiteModeControls(LiteModeProfile? current = null)
    {
        if (LiteModeButton is null || LiteModeStatusText is null) return;
        var profile = current ?? LiteModeLogic.Resolve(_liteModeEnabled);
        BrandNameText.Text = profile.Enabled ? "ISLEY · LITE" : "ISLEY";
        LiteModeButton.Content = profile.ButtonLabel;
        LiteModeButton.ToolTip = profile.Tooltip;
        LiteModeStatusText.Text = profile.Status;
        LiteModeStatusText.Foreground = profile.Enabled
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("SecondaryTextBrush");
        SetToggleButtonState(LiteModeButton, profile.Enabled);
    }

    private void UpdateSmartHudControls(HudPriorityPresentation? current = null)
    {
        if (SmartHudButton is null || SmartHudStatusText is null) return;
        var presentation = current ?? CurrentHudPriorityPresentation();
        SmartHudButton.Content = _smartHudEnabled ? "Smart HUD · On" : "Smart HUD · Off";
        SmartHudButton.ToolTip = presentation.Tooltip;
        SmartHudStatusText.Text = presentation.Status;
        SmartHudStatusText.Foreground = presentation.IsSafetyFocusActive
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("SecondaryTextBrush");
        SetToggleButtonState(SmartHudButton, _smartHudEnabled);
    }

    private void RefreshSmartHudPresentation(bool force = false)
    {
        if (SmartHudButton is null || NavigationReadoutPanel is null) return;
        var presentation = CurrentHudPriorityPresentation();
        var signature = string.Join('|',
            _smartHudEnabled,
            presentation.IsCompactViewport,
            presentation.IsSafetyFocusActive,
            presentation.HideWaitingNavigation,
            _markerAvailable);
        if (!force && string.Equals(signature, _smartHudUiSignature, StringComparison.Ordinal)) return;
        _smartHudUiSignature = signature;

        UpdateSmartHudControls(presentation);
        _voiceUiSignature = string.Empty;
        _fieldConditionsUiSignature = string.Empty;
        _lifeRunUiSignature = string.Empty;
        UpdateNavigationReadout(_markerAvailable);
        UpdateFriendProximity();
        UpdateVoicePresentation();
        UpdateFieldConditions(force: true);
        UpdateLifeRun(force: true);
    }

    private void HudDockButton_Click(object sender, RoutedEventArgs e)
    {
        _hudDockMirrored = !_hudDockMirrored;
        _hudDockUiSignature = string.Empty;
        UpdateHudDockLayout(animate: true);
        SaveSettings();
    }

    private void UpdateHudDockLayout(bool animate = false)
    {
        if (HudDockButton is null
            || HudDockStatusText is null
            || TacticalIntelStack is null
            || NavigationReadoutPanel is null
            || SurvivalHudStack is null
            || VoiceHudBorder is null)
        {
            return;
        }

        var plan = HudDockLogic.Resolve(
            _hudDockMirrored,
            VoiceHudBorder.Visibility == Visibility.Visible,
            VoiceHudBorder.ActualHeight,
            MapViewportBorder?.ActualHeight ?? 0);
        var quickKeysInset = HudSurfaceLogic.Show(_quickKeysHudVisible, _streamerMode)
            ? 52d
            : 0d;
        var signature = string.Join('|',
            _hudDockMirrored,
            plan.IntelBottomInset.ToString("0.0", CultureInfo.InvariantCulture),
            quickKeysInset.ToString("0.0", CultureInfo.InvariantCulture),
            VoiceHudBorder.Visibility,
            (MapViewportBorder?.ActualHeight ?? 0).ToString("0.0", CultureInfo.InvariantCulture));
        if (!animate && string.Equals(signature, _hudDockUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _hudDockUiSignature = signature;

        NavigationReadoutPanel.HorizontalAlignment = DockHorizontalAlignment(plan.NavigationSide);
        TacticalIntelStack.HorizontalAlignment = DockHorizontalAlignment(plan.IntelSide);
        SurvivalHudStack.HorizontalAlignment = DockHorizontalAlignment(plan.SurvivalSide);
        VoiceHudBorder.HorizontalAlignment = DockHorizontalAlignment(plan.VoiceSide);
        NavigationReadoutPanel.Margin = new Thickness(
            HudDockLogic.EdgeInset,
            HudDockLogic.EdgeInset,
            HudDockLogic.EdgeInset,
            HudDockLogic.EdgeInset + quickKeysInset);
        TacticalIntelStack.Margin = new Thickness(
            HudDockLogic.EdgeInset,
            HudDockLogic.EdgeInset,
            HudDockLogic.EdgeInset,
            plan.IntelBottomInset + quickKeysInset);
        VoiceHudBorder.Margin = new Thickness(
            HudDockLogic.EdgeInset,
            HudDockLogic.EdgeInset,
            HudDockLogic.EdgeInset,
            HudDockLogic.EdgeInset + quickKeysInset);

        HudDockButton.Content = $"HUD dock · {plan.Label}";
        HudDockButton.ToolTip = _hudDockMirrored
            ? "Restore navigation to the left and tactical cards to the right"
            : "Mirror navigation to the right and tactical cards to the left";
        HudDockStatusText.Text = plan.Description;
        SetToggleButtonState(HudDockButton, _hudDockMirrored);

        if (!animate || !IsLoaded)
        {
            return;
        }

        UIElement[] dockedElements =
        [
            NavigationReadoutPanel,
            TacticalIntelStack,
            SurvivalHudStack,
            VoiceHudBorder
        ];
        for (var index = 0; index < dockedElements.Length; index++)
        {
            dockedElements[index].BeginAnimation(
                OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.35,
                    To = 1,
                    BeginTime = TimeSpan.FromMilliseconds(index * 28),
                    Duration = TimeSpan.FromMilliseconds(170)
                });
        }
    }

    private static HorizontalAlignment DockHorizontalAlignment(string side) =>
        string.Equals(side, "right", StringComparison.Ordinal)
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;

    private void SizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDocked)
        {
            SetDocked(false);
            return;
        }
        _expanded = !_expanded;
        if (_expanded)
        {
            Width = Math.Min(720, SystemParameters.WorkArea.Width - 32);
            Height = Math.Min(800, SystemParameters.WorkArea.Height - 32);
            SizeButton.Content = "SMALL";
            return;
        }

        Width = 472;
        Height = 560;
        SizeButton.Content = "SIZE";
    }

    private async void StreamerModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_streamerMode)
        {
            MaybeShowPressureCoach(PressureCoachLogic.PreStream(_pressureCoachPreStreamSeen), () =>
            {
                _pressureCoachPreStreamSeen = true;
            });
        }

        _streamerMode = !_streamerMode;
        _playerSnapshot = null;
        _playerSnapshotTransportState = "unavailable";
        ClearLifeTransitionSession();
        ClearVitalsTrendSamples();
        _coreVitalsUiSignature = string.Empty;
        StreamerMask.Visibility = _streamerMode ? Visibility.Visible : Visibility.Collapsed;
        UpdateServerSessionPresentation();
        StreamerModeButton.Content = _streamerMode ? "Streamer mode on" : "Streamer mode off";
        StreamerModeButton.ToolTip = _streamerMode
            ? "Streamer mode is hiding identities, counts, and exact positions"
            : "Enable streamer mode to hide identities, counts, and exact positions";
        SetToggleButtonState(StreamerModeButton, _streamerMode);
        if (_streamerMode)
        {
            if (_voiceBridgeRunning || _voiceConnecting)
            {
                PostVoiceCommand(new { type = "disconnect" });
                _voiceBridgeRunning = false;
                _voiceConnecting = false;
                _voiceEngineState = "READY";
                _voiceEngineDetail = "STREAMER MODE · VOICE PAUSED";
            }
            CancelServerRestartWatch(logEvent: false, updateUi: false);
            await ClearSoundFinderAsync(showToast: false, logEvent: false);
            ClearManualSighting(logEvent: false, updateUi: false, resetDraft: true, collapse: true);
            _packSpreadMotion = string.Empty;
            _packSpreadRate = null;
            _packSpreadMotionSampleCount = 0;
            _packCourseState = string.Empty;
            _packCourseSpeed = null;
            _packCourseBearing = null;
            _packCourseCardinal = string.Empty;
            _packCourseSampleCount = 0;
            _encounterPlayerCount = 0;
            _nearestEncounterDistance = null;
            _nearestEncounterBearing = null;
            _nearestEncounterCardinal = string.Empty;
            _nearestEncounterMotion = string.Empty;
            _nearestEncounterRelativeSpeed = null;
            _nearestEncounterInterceptSeconds = null;
            _nearestEncounterMotionSampleCount = 0;
            _encounterWithin10 = 0;
            _encounterWithin25 = 0;
            _encounterWithin50 = 0;
            _encounterMemoryTrackCount = 0;
            _rememberedEncounterCount = 0;
            _rememberedEncounterNewestAgeMs = null;
            _nearestRememberedEncounterDistance = null;
            _nearestRememberedEncounterBearing = null;
            _nearestRememberedEncounterCardinal = string.Empty;
            _recentRoutes.Clear();
            _canRouteBack = false;
            _destinationSearchRevision++;
            ClearPlaceSuggestions();
            _pinArmed = false;
            if (_routePlanArmed)
            {
                _routePlanArmed = false;
                ClearRoutePlanValues();
            }
            _measurementArmed = false;
            ResetWaterCrossingCheck(logEvent: false);
            ResetShorelineCheck(logEvent: false);
            if (!_measurementActive)
            {
                _measurementHasStart = false;
                ClearMeasurementValues();
            }
            _pinRemovalConfirmationId = string.Empty;
            _clearPinsConfirmationPending = false;
            _clearBreadcrumbConfirmationPending = false;
            _clearBreadcrumbConfirmationRevision++;
            _clearExplorationConfirmationPending = false;
            _clearExplorationConfirmationRevision++;
        }
        UpdateAnimalCount(0);
        UpdateNavigationReadout(_currentSelfX is not null && _currentSelfY is not null);
        UpdateMapScaleBar();
                UpdateMapGridControl();
                UpdateLandmarkLabelDensityControl();
                UpdateWaypointStatus(null, null, string.Empty);
        UpdateRoutePlanControls();
        UpdateMeasurementStatus();
        _resourceFinderUiSignature = string.Empty;
        UpdateResourceFinder(force: true);
        UpdateRecoveryControls();
        UpdatePinControls();
        UpdateRecentRoutes();
        UpdatePinLibrary();
        UpdateNearestPlaceContext();
        UpdateSessionStats();
        UpdateServerRestartWatch(force: true);
        UpdateTacticalBrief();
        UpdateTacticalLog();
        UpdateBreadcrumbTrailControls();
        UpdateExplorationControls();
        UpdateCoreVitals(force: true);
        UpdateSurvivalAssistant(force: true);
        UpdateManualSighting(force: true);
        UpdateFieldConditions(force: true);
        UpdateShorelineCheck(force: true);
        UpdateLifeRun(force: true);
        UpdateFocusModeControls();
        UpdateTripReadiness(force: true);
        UpdateDangerProximity();
        UpdateEncounterAwareness();
        UpdateFriendProximity();
        UpdateFriendRoster();
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
        RefreshAimGuideVisibility();
        UpdateRecoveryPrompt(_markerAvailable, _markerAvailable);
        UpdateHudSurfaceControls();
        StaleAlertBorder.Visibility = _staleAlertActive
                                      && HudSurfaceLogic.Show(_alertHudVisible, _streamerMode)
            ? Visibility.Visible
            : Visibility.Collapsed;
        await EnsureFollowControllerAsync();
        await ApplyMapOptionsAsync();
    }

    private void AlwaysOnTopButton_Click(object sender, RoutedEventArgs e)
    {
        _alwaysOnTop = !_alwaysOnTop;
        Topmost = _alwaysOnTop;
        AlwaysOnTopButton.Content = _alwaysOnTop ? "Always on top" : "Normal window level";
        SetToggleButtonState(AlwaysOnTopButton, _alwaysOnTop);
        if (_alwaysOnTop)
        {
            EnsureOverlayPriority(forceToggle: true);
        }
        else if (_dockWindow is not null)
        {
            _dockWindow.Topmost = false;
        }
        SaveSettings();
    }

    private void PlayFocusButton_Click(object sender, RoutedEventArgs e) => TogglePlayFocus();

    private void TogglePlayFocus()
    {
        if (!_playFocusEnabled)
        {
            _playFocusRestoreClickThrough = _clickThrough;
            _playFocusEnabled = true;
            _visibilityRequested = true;
            _playFocusInteractionOverride = true;
            _playFocusSuppressed = false;
            SetClickThrough(false);
        }
        else
        {
            _playFocusEnabled = false;
            _playFocusSuppressed = false;
            _playFocusInteractionOverride = false;
            if (_visibilityRequested && !IsVisible)
            {
                Show();
                Topmost = _alwaysOnTop;
            }
            SetClickThrough(_playFocusRestoreClickThrough);
        }

        UpdatePlayFocusPresentation();
        SaveSettings();
    }

    private void ClickThroughButton_Click(object sender, RoutedEventArgs e) => ToggleInteractionMode();

    private async void ResetLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        _opacityIndex = 0;
        _mapLightModeIndex = 0;
        _hudDetailModeIndex = 0;
        _smartHudEnabled = true;
        _smartHudUiSignature = string.Empty;
        _liteModeEnabled = false;
        _hudDockMirrored = false;
        _hudDockUiSignature = string.Empty;
        _zoomPresetIndex = 1;
        _trailDurationIndex = 2;
        _arrivalAlertIndex = 2;
        _dangerAlertIndex = 2;
        _packSpreadAlertIndex = 2;
        _packSpreadAlertInitialized = false;
        _packSpreadAlertActive = false;
        _packSpreadMotion = string.Empty;
        _packSpreadRate = null;
        _packSpreadMotionSampleCount = 0;
        _packCourseState = string.Empty;
        _packCourseSpeed = null;
        _packCourseBearing = null;
        _packCourseCardinal = string.Empty;
        _packCourseSampleCount = 0;
        _encounterAlertIndex = 2;
        _encounterAlertInitialized = false;
        _encounterAlertActive = false;
        _toolsSection = "map";
        _playerLabelsVisible = true;
        _friendOnly = false;
        _markerStyleIndex = 0;
        _headingUp = false;
        _lookAheadEnabled = true;
        _smartZoomEnabled = true;
        _smartZoomSuspended = false;
        _rangeRingsVisible = false;
        _rangeRingModeIndex = 0;
        _mapGridVisible = false;
        _landmarkLabelDensityIndex = 0;
        _visibleLandmarkCount = 0;
        _breadcrumbTrailVisible = true;
        _clearBreadcrumbConfirmationPending = false;
        _clearBreadcrumbConfirmationRevision++;
        _focusModeRestoreSnapshot = null;
        _activeFocusModeId = string.Empty;
        _explorationEnabled = false;
        _terrainRouteStyle = TerrainRouteStyleLogic.BalancedId;
        _terrainGapPolicy = TerrainGapPolicyLogic.BalancedId;
        _learnedPassageRoutingEnabled = true;
        _learnedPassageVisible = true;
        _clearLearnedPassagesConfirmationPending = false;
        _clearLearnedPassagesConfirmationRevision++;
        _clearExplorationConfirmationPending = false;
        _clearExplorationConfirmationRevision++;
        _currentGridReference = string.Empty;
        _friendRadarVisible = true;
        _encounterHudVisible = true;
        _encounterMemoryIndex = 2;
        _encounterMemoryTrackCount = 0;
        _rememberedEncounterCount = 0;
        _rememberedEncounterNewestAgeMs = null;
        _nearestRememberedEncounterDistance = null;
        _nearestRememberedEncounterBearing = null;
        _nearestRememberedEncounterCardinal = string.Empty;
        _nearestEncounterMotion = string.Empty;
        _nearestEncounterRelativeSpeed = null;
        _nearestEncounterInterceptSeconds = null;
        _nearestEncounterMotionSampleCount = 0;
        _nearestPlaceVisible = true;
        _staleSoundEnabled = true;
        _timerSoundEnabled = true;
        _rememberLastPosition = true;
        _streamerMode = false;
        _autoLocateOnGameStart = true;
        _voiceEnabled = true;
        _voiceAutoOpen = true;
        _voiceNatAssist = true;
        _voiceProximityEnabled = true;
        _voiceRangeIndex = 1;
        _voiceEchoCancellation = true;
        _voiceNoiseSuppression = true;
        _voiceAutoGainControl = true;
        _voiceMicMeterEnabled = true;
        _voiceQualityMonitorEnabled = true;
        ResetVoiceMicMeterState();
        ResetVoiceQualityState();
        _voiceTurnRelayEnabled = false;
        _voiceSelectedInputDeviceId = string.Empty;
        SetVoiceInputDeviceOptions([], string.Empty, "CONNECT TO CHOOSE");
        _voiceSelectedOutputDeviceId = string.Empty;
        SetVoiceOutputDeviceOptions([], string.Empty, false, "CONNECT TO CHOOSE");
        _voiceParticipants.Clear();
        _voiceParticipantRosterSignature = string.Empty;
        UpdateVoiceParticipantRoster();
        VoiceTurnUrlInputBox?.Clear();
        VoiceTurnUsernameInputBox?.Clear();
        VoiceTurnCredentialInputBox?.Clear();
        _voiceHudVisible = true;
        _voicePttKeyIndex = 0;
        _voicePttHeld = false;
        _voiceUiSignature = string.Empty;
        _guideFilterId = "all";
        _guideSelectedSpeciesId = "allosaurus";
        _guideFavoriteSpeciesIds.Clear();
        _guideSearchResults = [];
        _guideUiSignature = string.Empty;
        _commandFavoriteActionIds.Clear();
        _commandRecentActionIds.Clear();
        _routePlanArmed = false;
        _routePlanActive = false;
        _routePlanComplete = false;
        ClearRoutePlanValues();
        _measurementArmed = false;
        _measurementHasStart = false;
        _measurementActive = false;
        ClearMeasurementValues();
        ResetWaterCrossingCheck(logEvent: false);
        ResetShorelineCheck(logEvent: false);
        _trackFinderMode = TrackFinderMode.Sound;
        _trackFinderScentTarget = ScentTargetKind.Water;
        _soundBearingFirst = null;
        _soundBearingSecond = null;
        _soundFinderAnalysis = SoundFinderLogic.Analyze(null, null, DateTimeOffset.UtcNow);
        _soundFinderUiSignature = string.Empty;
        _resourceFinderQuery = "salt";
        _resourceFinderResultIndex = 0;
        _resourceFinderUiSignature = string.Empty;
        _activeResourceRouteId = string.Empty;
        _activeResourceRouteLabel = string.Empty;
        _alwaysOnTop = true;
        _navigationHudVisible = true;
        _vitalsHudVisible = true;
        _survivalHudVisible = true;
        _alertHudVisible = true;
        _quickKeysHudVisible = false;
        _quickKeysModeIndex = 0;
        _quickKeysUiSignature = string.Empty;
        _aimGuideEnabled = false;
        _aimGuideGrowthIndex = AimCalibrationLogic.DefaultGrowthIndex;
        _aimGuideGrowthSyncEnabled = true;
        _aimGuideCameraIndex = AimCalibrationLogic.DefaultCameraIndex;
        _aimGuideModeIndex = 1;
        _aimGuideSize = 220;
        _aimGuideDepthScale = AimCalibrationLogic.DefaultDepthScale;
        _aimGuideHorizontalOffset = 0;
        _aimGuideVerticalOffset = 0;
        _aimGuideAttackIndex = 0;
        ApplyAimCalibrationForSelection(useDefaultsWhenMissing: false, updatePresentation: false);
        _playFocusEnabled = false;
        _visibilityRequested = true;
        _playFocusSuppressed = false;
        _playFocusInteractionOverride = false;
        _playFocusRestoreClickThrough = false;
        _currentMapScale = _zoomPresets[_zoomPresetIndex];
        _expanded = false;
        if (_isDocked)
        {
            SetDocked(false);
        }
        Width = 472;
        Height = 560;
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
        Opacity = _opacityLevels[_opacityIndex];
        Topmost = true;
        SetClickThrough(false);
        RestoreDefaultHotkeys(logEvent: false);
        StreamerMask.Visibility = Visibility.Collapsed;
        NavigationReadoutPanel.Visibility = Visibility.Visible;
        SetToolsOpen(false);
        ApplyControlStates();
        ResourceFinderSearchInputBox.Text = "salt";
        UpdateResourceFinder(force: true);
        UpdateZoomDisplay();
        await EnsureFollowControllerAsync();
        await ApplyMapOptionsAsync();
        await ExecuteMapperCommandAsync(
            "window.__isley?.clearRoutePlan() ?? false");
        await ExecuteMapperCommandAsync(
            "window.__isley?.clearMeasurement() ?? false");
        await ExecuteMapperCommandAsync(
            "window.__isley?.clearSoundFinder() ?? false");
        await ExecuteMapperCommandAsync(
            $"window.__isley?.setZoom({_currentMapScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}) ?? false");
        await ExecuteMapperCommandAsync(
            "window.__isley?.applyLayerPreset('navigation') ?? false");
        SaveSettings();
    }

    private void SetToggleButtonState(Button button, bool active)
    {
        button.Background = active
            ? (Brush)FindResource("ActiveToggleBrush")
            : (Brush)FindResource("RaisedSurfaceBrush");
        button.Foreground = (Brush)FindResource("PrimaryTextBrush");
        button.BorderBrush = active
            ? (Brush)FindResource("AccentHoverBrush")
            : new SolidColorBrush(Color.FromArgb(0x55, 0x7E, 0x89, 0x95));
    }

    private ResponsiveOverlayPresentation CurrentResponsiveOverlayPresentation() =>
        ResponsiveLayoutLogic.Resolve(
            ActualWidth > 0 ? ActualWidth : Width,
            ActualHeight > 0 ? ActualHeight : Height,
            requestedSurvivalDetails: !_survivalIncidentHudCollapsed);

    private void UpdateResponsiveOverlayLayout(bool force = false)
    {
        if (FooterSizeColumn is null
            || ClickThroughButton is null
            || StatusBeaconButton is null
            || SurvivalQuickButton is null
            || InteractionStatusText is null
            || WindowSizeText is null)
        {
            return;
        }

        var presentation = CurrentResponsiveOverlayPresentation();
        var ultraCompact = !_isDocked && (ActualWidth < 340 || ActualHeight < 280);
        var signature = $"{(presentation.IsMicroLayout ? "micro" : "normal")}:{ultraCompact}:{_isDocked}:{_vitalsHudVisible}";
        if (!force && string.Equals(signature, _responsiveLayoutUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _responsiveLayoutUiSignature = signature;

        // Keep the resize grip column large enough to grab even in ultra-compact mode.
        var sizeColumnWidth = ultraCompact
            ? 32
            : Math.Max(32, presentation.FooterSizeColumnWidth);
        FooterSizeColumn.Width = new GridLength(sizeColumnWidth);
        if (FooterSizeHost is not null)
        {
            FooterSizeHost.Width = sizeColumnWidth;
        }
        StatusBeaconButton.MinWidth = presentation.VitalsMinimumWidth;
        StatusBeaconButton.FontSize = presentation.IsMicroLayout ? 6.5 : 7.5;
        StatusBeaconButton.Padding = presentation.IsMicroLayout
            ? new Thickness(4, 3, 4, 3)
            : new Thickness(7, 3, 7, 3);
        ClickThroughButton.Padding = presentation.IsMicroLayout
            ? new Thickness(6, 4, 6, 4)
            : new Thickness(9, 4, 9, 4);
        ClickThroughButton.FontSize = presentation.IsMicroLayout ? 8 : 9;
        SurvivalQuickButton.MinWidth = presentation.IsMicroLayout ? 52 : 56;
        SurvivalQuickButton.FontSize = presentation.IsMicroLayout ? 6.8 : 7.5;
        SurvivalQuickButton.Padding = presentation.IsMicroLayout
            ? new Thickness(4, 3, 4, 3)
            : new Thickness(7, 3, 7, 3);
        InteractionStatusText.Margin = presentation.IsMicroLayout
            ? new Thickness(4, 0, 2, 0)
            : new Thickness(8, 0, 2, 0);
        InteractionStatusText.FontSize = presentation.IsMicroLayout ? 7 : 8;
        WindowSizeText.FontSize = presentation.IsMicroLayout ? 7 : 8;
        StatusBeaconButton.Visibility = _vitalsHudVisible ? Visibility.Visible : Visibility.Collapsed;
        SurvivalQuickButton.Visibility = ultraCompact ? Visibility.Collapsed : Visibility.Visible;
        InteractionStatusText.Visibility = ultraCompact ? Visibility.Collapsed : Visibility.Visible;
        WindowSizeText.Visibility = ultraCompact ? Visibility.Collapsed : Visibility.Visible;

        ToolsDrawer.Width = presentation.StretchToolsDrawer ? double.NaN : 224;
        ToolsDrawer.HorizontalAlignment = presentation.StretchToolsDrawer
            ? HorizontalAlignment.Stretch
            : HorizontalAlignment.Right;
        ToolsDrawer.Margin = new Thickness(
            presentation.StretchToolsDrawer ? 8 : 0,
            presentation.ToolsDrawerTopInset,
            8,
            presentation.IsMicroLayout ? 6 : 8);
        ToolsDrawer.Padding = new Thickness(presentation.ToolsDrawerPadding);
        ToolsDrawerBodyGrid.Margin = new Thickness(0, presentation.ToolsBodyTopInset, 0, 0);
        ToolsDrawerSubtitle.Visibility = presentation.ShowToolsDrawerSubtitle
            ? Visibility.Visible
            : Visibility.Collapsed;
        ToolsFindButton.Height = presentation.ToolsHeaderButtonHeight;
        ToolsFindButton.Width = presentation.IsMicroLayout ? 44 : 52;
        ToolsCloseButton.Height = presentation.ToolsHeaderButtonHeight;
        ToolsCloseButton.Width = presentation.IsMicroLayout ? 22 : 26;
        ToolsCategoryTabs.Margin = presentation.IsMicroLayout
            ? new Thickness(0, 0, 0, 2)
            : new Thickness(-2, 0, -2, 4);
        foreach (var button in new[]
                 {
                     MapToolsTabButton,
                     PinsToolsTabButton,
                     LayerToolsTabButton,
                     OverlayToolsTabButton,
                     VoiceToolsTabButton,
                     GuideToolsTabButton,
                     HubToolsTabButton
                 })
        {
            button.Height = presentation.ToolsCategoryButtonHeight;
            button.Margin = presentation.IsMicroLayout ? new Thickness(1) : new Thickness(2);
            button.FontSize = presentation.IsMicroLayout ? 7.2 : 8.5;
        }
        MapSectionJumpBar.Visibility = _toolsSection == "map" && presentation.ShowMapSectionJumpBar
            ? Visibility.Visible
            : Visibility.Collapsed;

        _survivalIncidentUiSignature = string.Empty;
        UpdateSurvivalAssistant(force: true);
        UpdateHotkeyStatus();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (WindowSizeText is not null)
        {
            WindowSizeText.Text = $"{ActualWidth:0}x{ActualHeight:0}";
        }

        if (QuickZoomOutButton is not null && QuickZoomInButton is not null)
        {
            var showQuickZoom = ActualWidth >= 430;
            QuickZoomOutButton.Visibility = showQuickZoom ? Visibility.Visible : Visibility.Collapsed;
            QuickZoomInButton.Visibility = showQuickZoom ? Visibility.Visible : Visibility.Collapsed;
        }

        if (HeaderStatusRail is not null && OpacityButton is not null && SizeButton is not null)
        {
            HeaderStatusRail.Visibility = !_isDocked && ActualWidth >= 410
                ? Visibility.Visible
                : Visibility.Collapsed;
            OpacityButton.Visibility = !_isDocked && ActualWidth >= 330
                ? Visibility.Visible
                : Visibility.Collapsed;
            // Always keep SIZE/SMALL available when undocked so a compact window can be restored.
            SizeButton.Visibility = !_isDocked
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        UpdateResponsiveOverlayLayout();
        UpdateHudDockLayout();
        UpdateQuickKeysPresentation();
        RefreshSmartHudPresentation();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e) =>
        _ = CancelMapPointerGestureAsync();

    private void Window_Deactivated(object? sender, EventArgs e) =>
        _ = CancelMapPointerGestureAsync();

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_isDocked || _overlayLocked)
        {
            return;
        }
        var workArea = SystemParameters.WorkArea;
        Width = Math.Clamp(ActualWidth + e.HorizontalChange, MinWidth, Math.Max(MinWidth, workArea.Width - 16));
        Height = Math.Clamp(ActualHeight + e.VerticalChange, MinHeight, Math.Max(MinHeight, workArea.Height - 16));
        _expanded = Width >= 600 || Height >= 680;
        SizeButton.Content = _expanded ? "SMALL" : "SIZE";
    }

    private void DockButton_Click(object sender, RoutedEventArgs e) => SetDocked(!_isDocked);

    private void SetDocked(bool docked)
    {
        if (_isDocked == docked || DockButton is null)
        {
            return;
        }

        if (docked)
        {
            _dockRestoreWidth = Math.Max(320, ActualWidth > 0 ? ActualWidth : Width);
            _dockRestoreHeight = Math.Max(280, ActualHeight > 0 ? ActualHeight : Height);
            _dockRestoreLeft = Left;
            _dockRestoreTop = Top;
            if (_commandPaletteOpen) CloseCommandPalette(returnFocus: false);
            if (_toolsOpen) SetToolsOpen(false);
        }

        if (docked)
        {
            _isDocked = true;
            _dockWindow?.CloseSilently();
            _dockWindow = new IsleyDockWindow(
                () => Dispatcher.Invoke(() => SetDocked(false)),
                () => Dispatcher.Invoke(() =>
                {
                    SetDocked(false);
                    OpenMapToolsAtSection("core-vitals");
                }),
                () => Dispatcher.Invoke(ToggleOverlayLock),
                () => Dispatcher.Invoke(Close),
                _overlayLocked,
                CurrentDockVitalsPresentation())
            {
                Topmost = _alwaysOnTop,
                Left = Math.Clamp(
                    Left,
                    SystemParameters.WorkArea.Left,
                    Math.Max(
                        SystemParameters.WorkArea.Left,
                        SystemParameters.WorkArea.Right - (_vitalsHudVisible && !_streamerMode ? 362 : 264))),
                Top = Math.Clamp(
                    Top,
                    SystemParameters.WorkArea.Top,
                    Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - 64))
            };
            _dockWindow.Show();
            Hide();
        }
        else
        {
            _isDocked = false;
            _dockWindow?.CloseSilently();
            _dockWindow = null;
            MinWidth = 320;
            MinHeight = 280;
            Width = Math.Clamp(
                _dockRestoreWidth,
                MinWidth,
                Math.Max(MinWidth, SystemParameters.WorkArea.Width - 16));
            Height = Math.Clamp(
                _dockRestoreHeight,
                MinHeight,
                Math.Max(MinHeight, SystemParameters.WorkArea.Height - 16));
            if (double.IsFinite(_dockRestoreLeft) && double.IsFinite(_dockRestoreTop))
            {
                Left = Math.Clamp(
                    _dockRestoreLeft,
                    SystemParameters.WorkArea.Left,
                    Math.Max(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Right - Width));
                Top = Math.Clamp(
                    _dockRestoreTop,
                    SystemParameters.WorkArea.Top,
                    Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - Height));
            }
            _expanded = Width >= 600 || Height >= 680;
            SizeButton.Content = _expanded ? "SMALL" : "SIZE";
            Show();
            Topmost = _alwaysOnTop;
            Activate();
        }

        UpdateResponsiveOverlayLayout(force: true);
        SaveSettings();
    }

    private HudSurfacePreferences CurrentHudSurfacePreferences() => new(
        _navigationHudVisible,
        _vitalsHudVisible,
        _friendRadarVisible,
        _encounterHudVisible,
        _survivalHudVisible,
        _voiceHudVisible,
        _alertHudVisible,
        _nearestPlaceVisible,
        _aimGuideEnabled,
        _quickKeysHudVisible);

    private void UpdateHudSurfaceControls()
    {
        if (HudNavigationButton is null
            || VitalsHudButton is null
            || HudPackButton is null
            || HudEncounterButton is null
            || HudSurvivalButton is null
            || HudVoiceButton is null
            || HudAlertsButton is null
            || HudNearbyButton is null
            || HudAimButton is null
            || HudQuickKeysButton is null
            || HudSurfaceStatusText is null)
        {
            return;
        }

        var preferences = CurrentHudSurfacePreferences();
        var presentation = HudSurfaceLogic.Present(preferences, _streamerMode);
        HudNavigationButton.Content = $"NAV · {(preferences.Navigation ? "ON" : "OFF")}";
        VitalsHudButton.Content = $"VITALS · {(preferences.Vitals ? "ON" : "OFF")}";
        HudPackButton.Content = $"PACK · {(preferences.Pack ? "ON" : "OFF")}";
        HudEncounterButton.Content = $"CONTACTS · {(preferences.Encounters ? "ON" : "OFF")}";
        HudSurvivalButton.Content = $"SURVIVAL · {(preferences.Survival ? "ON" : "OFF")}";
        HudVoiceButton.Content = $"VOICE · {(preferences.Voice ? "ON" : "OFF")}";
        HudAlertsButton.Content = $"ALERTS · {(preferences.Alerts ? "ON" : "OFF")}";
        HudNearbyButton.Content = $"NEARBY · {(preferences.Nearby ? "ON" : "OFF")}";
        HudAimButton.Content = $"AIM · {(preferences.Aim ? "ON" : "OFF")}";
        HudQuickKeysButton.Content = $"KEYS · {(preferences.QuickKeys ? "ON" : "OFF")}";
        HudSurfaceStatusText.Text = presentation.Status;
        HudSurfaceStatusText.Foreground = presentation.PrivacyHidden
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("SecondaryTextBrush");

        SetToggleButtonState(HudNavigationButton, preferences.Navigation);
        SetToggleButtonState(VitalsHudButton, preferences.Vitals);
        SetToggleButtonState(HudPackButton, preferences.Pack);
        SetToggleButtonState(HudEncounterButton, preferences.Encounters);
        SetToggleButtonState(HudSurvivalButton, preferences.Survival);
        SetToggleButtonState(HudVoiceButton, preferences.Voice);
        SetToggleButtonState(HudAlertsButton, preferences.Alerts);
        SetToggleButtonState(HudNearbyButton, preferences.Nearby);
        SetToggleButtonState(HudAimButton, preferences.Aim);
        SetToggleButtonState(HudQuickKeysButton, preferences.QuickKeys);

        SurvivalHudStack.Visibility = HudSurfaceLogic.Show(_survivalHudVisible, _streamerMode)
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateQuickKeysPresentation();
        if (!HudSurfaceLogic.Show(_navigationHudVisible, _streamerMode))
        {
            NavigationReadoutPanel.Visibility = Visibility.Collapsed;
            WaypointPanel.Visibility = Visibility.Collapsed;
            MeasurementPanel.Visibility = Visibility.Collapsed;
            TerrainRouteConfidencePanel.Visibility = Visibility.Collapsed;
            MapScalePanel.Visibility = Visibility.Collapsed;
        }
        if (!HudSurfaceLogic.Show(_alertHudVisible, _streamerMode))
        {
            DangerAlertBorder.Visibility = Visibility.Collapsed;
            StaleAlertBorder.Visibility = Visibility.Collapsed;
            RecoveryPromptBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshHudSurfaceVisibility()
    {
        UpdateHudSurfaceControls();
        if (HudSurfaceLogic.Show(_navigationHudVisible, _streamerMode))
        {
            UpdateNavigationReadout(_markerAvailable);
            UpdateMapScaleBar();
            WaypointPanel.Visibility = _waypointArmed || _routePlanComplete || _waypointActive
                ? Visibility.Visible
                : Visibility.Collapsed;
            MeasurementPanel.Visibility = _measurementArmed || _measurementActive
                ? Visibility.Visible
                : Visibility.Collapsed;
            TerrainRouteConfidencePanel.Visibility = _terrainRouteConfidenceVisible
                                                     && _terrainCourseDistance is not null
                                                     && string.Equals(
                                                         _routePlanSource,
                                                         "terrain",
                                                         StringComparison.Ordinal)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        if (HudSurfaceLogic.Show(_alertHudVisible, _streamerMode))
        {
            UpdateDangerProximity();
        }
        StaleAlertBorder.Visibility = HudSurfaceLogic.Show(_alertHudVisible, _streamerMode)
                                      && _staleAlertActive
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_recoveryPromptVisible && HudSurfaceLogic.Show(_alertHudVisible, _streamerMode))
        {
            ShowRecoveryPrompt();
        }
        else if (!HudSurfaceLogic.Show(_alertHudVisible, _streamerMode))
        {
            RecoveryPromptBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void HudNavigationButton_Click(object sender, RoutedEventArgs e)
    {
        _navigationHudVisible = !_navigationHudVisible;
        RefreshHudSurfaceVisibility();
        SaveSettings();
    }

    private void HudPackButton_Click(object sender, RoutedEventArgs e)
    {
        FriendRadarButton_Click(FriendRadarButton, new RoutedEventArgs());
        UpdateHudSurfaceControls();
    }

    private void HudEncounterButton_Click(object sender, RoutedEventArgs e)
    {
        EncounterHudButton_Click(EncounterHudButton, new RoutedEventArgs());
        UpdateHudSurfaceControls();
    }

    private void HudSurvivalButton_Click(object sender, RoutedEventArgs e)
    {
        _survivalHudVisible = !_survivalHudVisible;
        RefreshHudSurfaceVisibility();
        SaveSettings();
    }

    private void HudAlertsButton_Click(object sender, RoutedEventArgs e)
    {
        _alertHudVisible = !_alertHudVisible;
        RefreshHudSurfaceVisibility();
        SaveSettings();
    }

    private void HudNearbyButton_Click(object sender, RoutedEventArgs e)
    {
        NearestPlaceButton_Click(NearestPlaceButton, new RoutedEventArgs());
        UpdateHudSurfaceControls();
    }

    private void HudAimButton_Click(object sender, RoutedEventArgs e)
    {
        AimGuideButton_Click(AimGuideButton, new RoutedEventArgs());
        UpdateHudSurfaceControls();
    }

    private void VitalsHudButton_Click(object sender, RoutedEventArgs e)
    {
        _vitalsHudVisible = !_vitalsHudVisible;
        UpdateVitalsHudControl();
        UpdateHudSurfaceControls();
        SaveSettings();
    }

    private void UpdateVitalsHudControl()
    {
        if (StatusBeaconButton is null || VitalsHudButton is null)
        {
            return;
        }

        StatusBeaconButton.Visibility = _vitalsHudVisible ? Visibility.Visible : Visibility.Collapsed;
        VitalsHudButton.Content = _vitalsHudVisible ? "VITALS · ON" : "VITALS · OFF";
        SetToggleButtonState(VitalsHudButton, _vitalsHudVisible);
        UpdateDockVitals(animate: false);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void TrackPlayFocusSignals(DateTimeOffset nowUtc)
    {
        if (_playFocusTickSeen)
        {
            var sample = LiteModeSuggestLogic.Sample(
                _playFocusTimer.Interval.TotalMilliseconds,
                (nowUtc - _playFocusLastTickUtc).TotalMilliseconds);
            _liteModeSampleCount = Math.Min(_liteModeSampleCount + 1, 10_000);
            if (sample.Starved)
            {
                _liteModeStarvedStreak = Math.Min(_liteModeStarvedStreak + 1, 10_000);
                _liteModeLastStarvedRatio = sample.Ratio;
            }
            else
            {
                _liteModeStarvedStreak = 0;
            }

            if (LiteModeSuggestLogic.ShouldSuggest(
                    _liteModeSampleCount,
                    _liteModeStarvedStreak,
                    _liteModeEnabled,
                    _liteModeSuggestOffered,
                    _liteModeSuggestSnoozed))
            {
                _ = OfferLiteModeSuggestionAsync();
            }
        }

        _playFocusLastTickUtc = nowUtc;
        _playFocusTickSeen = true;
    }

    private async Task OfferLiteModeSuggestionAsync()
    {
        if (_liteModeSuggestOffered)
        {
            return;
        }

        // Once per session; expiry without a tap snoozes for the session.
        _liteModeSuggestOffered = true;
        var revision = ++_liteModeSuggestRevision;
        if (!IsLoaded || HotkeyToastBorder is null || HotkeyToastText is null)
        {
            _liteModeSuggestSnoozed = true;
            return;
        }

        if (!_liteModeSuggestTapWired)
        {
            _liteModeSuggestTapWired = true;
            HotkeyToastBorder.Cursor = Cursors.Hand;
            HotkeyToastBorder.MouseLeftButtonUp += LiteModeSuggestToast_MouseLeftButtonUp;
        }

        var message = LiteModeSuggestLogic.OfferMessage(_liteModeLastStarvedRatio);
        _hotkeyToastRevision++;
        _liteModeSuggestOfferMessage = message;
        _liteModeSuggestTapArmed = true;
        HotkeyToastText.Text = message;
        HotkeyToastBorder.BorderBrush = (Brush)FindResource("AccentBrush");
        HotkeyToastBorder.BeginAnimation(OpacityProperty, null);
        HotkeyToastBorder.Opacity = 1;
        HotkeyToastBorder.Visibility = Visibility.Visible;
        AddTacticalEvent(
            "SYSTEM",
            "Lite Mode suggested",
            "Repeated timer lag detected · waiting for the one-tap choice");
        await Task.Delay(9000);
        if (!IsLoaded || revision != _liteModeSuggestRevision || !_liteModeSuggestTapArmed)
        {
            return;
        }

        _liteModeSuggestTapArmed = false;
        _liteModeSuggestSnoozed = true;
        if (!string.Equals(HotkeyToastText.Text, message, StringComparison.Ordinal))
        {
            // Another toast took over; the suggestion still snoozes quietly.
            return;
        }

        HotkeyToastBorder.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                1,
                0,
                TimeSpan.FromMilliseconds(220)));
        await Task.Delay(240);
        if (IsLoaded && revision == _liteModeSuggestRevision)
        {
            HotkeyToastBorder.Visibility = Visibility.Collapsed;
            HotkeyToastBorder.BeginAnimation(OpacityProperty, null);
            HotkeyToastBorder.Opacity = 1;
        }
    }

    private async void LiteModeSuggestToast_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Only the visible offer toast is tappable; any other toast is inert.
        if (!_liteModeSuggestTapArmed
            || HotkeyToastText is null
            || !string.Equals(
                HotkeyToastText.Text,
                _liteModeSuggestOfferMessage,
                StringComparison.Ordinal))
        {
            return;
        }

        _liteModeSuggestTapArmed = false;
        _liteModeSuggestRevision++;
        await SetLiteModeAsync(true);
        AddTacticalEvent("SYSTEM", "Lite Mode suggestion accepted", "Enabled from the one-tap offer");
        await ShowHotkeyToastAsync("LITE MODE ON · UNDO ANYTIME IN VISUAL COMFORT", true);
    }

    private void TrackClipboardCaptureTick()
    {
        var current = _universalCoordinatePoint;
        var newCapture = current is not null
                         && !UniversalCoordinateLogic.SamePoint(current, _captureTickLastPoint);
        _captureTickLastPoint = current;
        if (!newCapture || !_clipboardCaptureSoundEnabled)
        {
            return;
        }

        try
        {
            SystemSounds.Beep.Play();
        }
        catch
        {
            // Audio feedback must never delay or break the capture path.
        }
    }

    private async Task ToggleCaptureSoundAsync()
    {
        _clipboardCaptureSoundEnabled = !_clipboardCaptureSoundEnabled;
        UpdateCaptureSoundControls();
        SaveSettings();
        await ShowHotkeyToastAsync(
            _clipboardCaptureSoundEnabled ? "CAPTURE SOUND ON" : "CAPTURE SOUND OFF",
            true);
    }

    private async void CaptureSoundButton_Click(object sender, RoutedEventArgs e) =>
        await ToggleCaptureSoundAsync();

    private void UpdateCaptureSoundControls()
    {
        if (_captureSoundButton is null)
        {
            return;
        }

        _captureSoundButton.Content =
            $"Capture sound · {(_clipboardCaptureSoundEnabled ? "On" : "Off")}";
        SetToggleButtonState(_captureSoundButton, _clipboardCaptureSoundEnabled);
    }

    private void RestoreLayoutProfiles(IEnumerable<HudLayoutProfileSettings>? savedProfiles)
    {
        _hudLayoutProfiles.Clear();
        _hudLayoutProfiles.AddRange(LayoutProfileLogic.NormalizeProfiles(
            savedProfiles?.Select(saved => new HudLayoutProfile(
                saved.Name,
                saved.HudDockMirrored,
                saved.Expanded,
                saved.Width,
                saved.Height,
                saved.HudDetailModeIndex,
                saved.NavigationHudVisible,
                saved.VitalsHudVisible,
                saved.SurvivalHudVisible,
                saved.AlertHudVisible,
                saved.QuickKeysHudVisible,
                saved.QuickKeysModeIndex,
                saved.SavedAtUnixMs))));
        _layoutProfilesUiSignature = string.Empty;
    }

    private HudLayoutProfile CaptureCurrentLayoutProfile(string name)
    {
        var width = _isDocked && double.IsFinite(_dockRestoreWidth)
            ? _dockRestoreWidth
            : double.IsFinite(ActualWidth) && ActualWidth > 0
                ? ActualWidth
                : double.IsFinite(Width) && Width > 0 ? Width : 472;
        var height = _isDocked && double.IsFinite(_dockRestoreHeight)
            ? _dockRestoreHeight
            : double.IsFinite(ActualHeight) && ActualHeight > 0
                ? ActualHeight
                : double.IsFinite(Height) && Height > 0 ? Height : 560;
        return new HudLayoutProfile(
            name,
            _hudDockMirrored,
            _expanded,
            width,
            height,
            _hudDetailModeIndex,
            _navigationHudVisible,
            _vitalsHudVisible,
            _survivalHudVisible,
            _alertHudVisible,
            _quickKeysHudVisible,
            _quickKeysModeIndex,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private async Task ApplyLayoutProfileAsync(HudLayoutProfile profile)
    {
        _hudDockMirrored = profile.HudDockMirrored;
        _hudDetailModeIndex = Math.Clamp(
            profile.HudDetailModeIndex,
            0,
            _hudDetailModeLabels.Length - 1);
        _navigationHudVisible = profile.NavigationHudVisible;
        _vitalsHudVisible = profile.VitalsHudVisible;
        _survivalHudVisible = profile.SurvivalHudVisible;
        _alertHudVisible = profile.AlertHudVisible;
        _quickKeysHudVisible = profile.QuickKeysHudVisible;
        _quickKeysModeIndex = QuickKeysLogic.NormalizeModeIndex(profile.QuickKeysModeIndex);
        _expanded = profile.Expanded;

        var sizeNote = string.Empty;
        if (_isDocked)
        {
            _dockRestoreWidth = profile.Width;
            _dockRestoreHeight = profile.Height;
            sizeNote = " · SIZE APPLIES ON UNDOCK";
        }
        else
        {
            var workArea = SystemParameters.WorkArea;
            Width = Math.Clamp(profile.Width, MinWidth, Math.Max(MinWidth, workArea.Width - 16));
            Height = Math.Clamp(profile.Height, MinHeight, Math.Max(MinHeight, workArea.Height - 16));
            if (SizeButton is not null)
            {
                SizeButton.Content = _expanded ? "SMALL" : "SIZE";
            }
        }

        _hudDockUiSignature = string.Empty;
        _quickKeysUiSignature = string.Empty;
        UpdateHudDetailModeControls();
        UpdateHudDockLayout(animate: IsLoaded);
        UpdateVitalsHudControl();
        RefreshHudSurfaceVisibility();
        SaveSettings();
        AddTacticalEvent("SYSTEM", "Layout profile applied", profile.Name);
        await ShowHotkeyToastAsync($"LAYOUT · {profile.Name.ToUpperInvariant()}{sizeNote}", true);
    }

    private void OpenLayoutProfilesSection()
    {
        OpenToolsWorkspace("overlay");
        EnsureOverlayExtrasUi();
        if (_layoutProfilesHeading is not null)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => _layoutProfilesHeading.BringIntoView()));
        }
    }

    private void SaveLayoutProfileFromCommand()
    {
        OpenLayoutProfilesSection();
        LayoutProfileSaveButton_Click(_layoutProfileSaveButton, new RoutedEventArgs());
    }

    private async void LayoutProfileSaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_hudLayoutProfiles.Count >= LayoutProfileLogic.MaximumProfiles)
        {
            await ShowHotkeyToastAsync(
                $"PROFILE LIMIT · KEEP UP TO {LayoutProfileLogic.MaximumProfiles}",
                false);
            return;
        }

        var name = LayoutProfileLogic.UniqueName(
            _layoutProfileNameBox?.Text,
            _hudLayoutProfiles.Select(profile => profile.Name),
            _hudLayoutProfiles.Count);
        _hudLayoutProfiles.Add(CaptureCurrentLayoutProfile(name));
        if (_layoutProfileNameBox is not null)
        {
            _layoutProfileNameBox.Text = string.Empty;
        }
        _layoutProfilesUiSignature = string.Empty;
        UpdateLayoutProfileControls(force: true);
        SaveSettings();
        AddTacticalEvent("SYSTEM", "Layout profile saved", name);
        await ShowHotkeyToastAsync($"LAYOUT SAVED · {name.ToUpperInvariant()}", true);
    }

    private async void LayoutProfileApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index }
            && index >= 0
            && index < _hudLayoutProfiles.Count)
        {
            await ApplyLayoutProfileAsync(_hudLayoutProfiles[index]);
        }
    }

    private async void LayoutProfileOverwriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int index }
            || index < 0
            || index >= _hudLayoutProfiles.Count)
        {
            return;
        }

        var name = _hudLayoutProfiles[index].Name;
        _hudLayoutProfiles[index] = CaptureCurrentLayoutProfile(name);
        _layoutProfilesUiSignature = string.Empty;
        UpdateLayoutProfileControls(force: true);
        SaveSettings();
        AddTacticalEvent("SYSTEM", "Layout profile updated", name);
        await ShowHotkeyToastAsync($"LAYOUT UPDATED · {name.ToUpperInvariant()}", true);
    }

    private async void LayoutProfileDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int index }
            || index < 0
            || index >= _hudLayoutProfiles.Count)
        {
            return;
        }

        var name = _hudLayoutProfiles[index].Name;
        _hudLayoutProfiles.RemoveAt(index);
        _layoutProfilesUiSignature = string.Empty;
        UpdateLayoutProfileControls(force: true);
        SaveSettings();
        AddTacticalEvent("SYSTEM", "Layout profile deleted", name);
        await ShowHotkeyToastAsync($"LAYOUT DELETED · {name.ToUpperInvariant()}", true);
    }

    private void UpdateLayoutProfileControls(bool force = false)
    {
        if (_layoutProfilesStatusText is null
            || _layoutProfileSaveButton is null
            || _layoutProfileListPanel is null)
        {
            return;
        }

        var signature = string.Join('|',
            _hudLayoutProfiles.Count,
            string.Join(';', _hudLayoutProfiles.Select(profile =>
                $"{profile.Name}:{LayoutProfileLogic.Summary(profile)}")));
        if (!force && string.Equals(signature, _layoutProfilesUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _layoutProfilesUiSignature = signature;

        var full = _hudLayoutProfiles.Count >= LayoutProfileLogic.MaximumProfiles;
        _layoutProfilesStatusText.Text = _hudLayoutProfiles.Count == 0
            ? "NONE SAVED · CAPTURE THE CURRENT LAYOUT"
            : $"{_hudLayoutProfiles.Count} / {LayoutProfileLogic.MaximumProfiles} SAVED" +
              (full ? " · DELETE ONE TO SAVE MORE" : string.Empty);
        _layoutProfilesStatusText.Foreground = full
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("SecondaryTextBrush");
        _layoutProfileSaveButton.IsEnabled = !full;
        _layoutProfileSaveButton.Content = full
            ? $"PROFILE LIMIT {LayoutProfileLogic.MaximumProfiles} REACHED"
            : "SAVE CURRENT LAYOUT";

        _layoutProfileListPanel.Children.Clear();
        for (var index = 0; index < _hudLayoutProfiles.Count; index++)
        {
            var profile = _hudLayoutProfiles[index];
            var row = new Grid { Margin = new Thickness(-2, 6, -2, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new StackPanel { Margin = new Thickness(2, 0, 4, 0) };
            label.Children.Add(new TextBlock
            {
                Text = profile.Name,
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("PrimaryTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            label.Children.Add(new TextBlock
            {
                Text = LayoutProfileLogic.Summary(profile),
                Margin = new Thickness(0, 1, 0, 0),
                FontSize = 7,
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            row.Children.Add(label);

            var applyButton = new Button
            {
                Tag = index,
                Style = (Style)FindResource("DrawerCompactButton"),
                Content = "APPLY",
                ToolTip = $"Apply {profile.Name}: dock side, window size, HUD detail, and HUD surfaces"
            };
            applyButton.Click += LayoutProfileApplyButton_Click;
            Grid.SetColumn(applyButton, 1);
            row.Children.Add(applyButton);

            var overwriteButton = new Button
            {
                Tag = index,
                Margin = new Thickness(3, 0, 0, 0),
                Style = (Style)FindResource("DrawerCompactButton"),
                Content = "SAVE",
                ToolTip = $"Overwrite {profile.Name} with the current layout"
            };
            overwriteButton.Click += LayoutProfileOverwriteButton_Click;
            Grid.SetColumn(overwriteButton, 2);
            row.Children.Add(overwriteButton);

            var deleteButton = new Button
            {
                Tag = index,
                Margin = new Thickness(3, 0, 0, 0),
                Style = (Style)FindResource("DrawerCompactButton"),
                Content = "✕",
                ToolTip = $"Delete {profile.Name}"
            };
            deleteButton.Click += LayoutProfileDeleteButton_Click;
            Grid.SetColumn(deleteButton, 3);
            row.Children.Add(deleteButton);

            _layoutProfileListPanel.Children.Add(row);
        }
    }

    private void EnsureOverlayExtrasUi()
    {
        if (_overlayExtrasUiBuilt || OverlayToolsPanel is null)
        {
            return;
        }
        _overlayExtrasUiBuilt = true;

        if (HudDockStatusText?.Parent is Panel dockParent)
        {
            var dockIndex = dockParent.Children.IndexOf(HudDockStatusText);
            if (dockIndex >= 0)
            {
                dockParent.Children.Insert(dockIndex + 1, BuildLayoutProfilesSection());
            }
        }

        if (PortableConfigStatusText?.Parent is Panel prefsParent)
        {
            var prefsIndex = prefsParent.Children.IndexOf(PortableConfigStatusText);
            if (prefsIndex >= 0)
            {
                prefsParent.Children.Insert(prefsIndex + 1, BuildFeedbackDiagnosticsSection());
            }
        }
    }

    private StackPanel BuildLayoutProfilesSection()
    {
        var section = new StackPanel();
        _layoutProfilesHeading = new TextBlock
        {
            Style = (Style)FindResource("SectionLabel"),
            Text = "LAYOUT PROFILES"
        };
        section.Children.Add(_layoutProfilesHeading);
        _layoutProfilesStatusText = new TextBlock
        {
            Margin = new Thickness(1, 0, 0, 5),
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("SecondaryTextBrush")
        };
        section.Children.Add(_layoutProfilesStatusText);

        var border = new Border
        {
            Margin = new Thickness(0, 0, 0, 5),
            Padding = new Thickness(8, 6),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x70, 0x14, 0x1B, 0x24)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x64, 0x74, 0x8B)),
            BorderThickness = new Thickness(1)
        };
        var content = new StackPanel();
        _layoutProfileNameBox = new TextBox
        {
            Style = (Style)FindResource("DrawerTextBox"),
            MaxLength = LayoutProfileLogic.MaximumNameLength,
            ToolTip = "Optional profile name (up to 24 characters) · blank uses Layout N"
        };
        System.Windows.Automation.AutomationProperties.SetName(
            _layoutProfileNameBox,
            "New layout profile name");
        content.Children.Add(_layoutProfileNameBox);
        _layoutProfileSaveButton = new Button
        {
            Margin = new Thickness(-2, 6, -2, -2),
            Style = (Style)FindResource("DrawerCompactButton"),
            ToolTip = "Save the current dock side, window size, HUD detail, and HUD surfaces",
            Content = "SAVE CURRENT LAYOUT"
        };
        System.Windows.Automation.AutomationProperties.SetName(
            _layoutProfileSaveButton,
            "Save current layout profile");
        _layoutProfileSaveButton.Click += LayoutProfileSaveButton_Click;
        content.Children.Add(_layoutProfileSaveButton);
        _layoutProfileListPanel = new StackPanel();
        content.Children.Add(_layoutProfileListPanel);
        border.Child = content;
        section.Children.Add(border);
        section.Children.Add(new TextBlock
        {
            Margin = new Thickness(1, 0, 0, 0),
            FontSize = 7,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            Text = "Up to 8 named layouts · dock side, window size, HUD detail, and HUD surfaces · saved in preferences."
        });
        return section;
    }

    private StackPanel BuildFeedbackDiagnosticsSection()
    {
        var section = new StackPanel();
        section.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("SectionLabel"),
            Text = "FEEDBACK & DIAGNOSTICS"
        });
        _captureSoundButton = new Button
        {
            Style = (Style)FindResource("DrawerButton"),
            ToolTip = "Play a subtle tick when a clipboard position capture succeeds",
            Content = "Capture sound · On"
        };
        _captureSoundButton.Click += CaptureSoundButton_Click;
        section.Children.Add(_captureSoundButton);
        _captureSoundStatusText = new TextBlock
        {
            Margin = new Thickness(2, -2, 2, 3),
            FontSize = 8,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            Text = "Tick plays after a position lands · never blocks the capture."
        };
        section.Children.Add(_captureSoundStatusText);
        _diagnosticsExportButton = new Button
        {
            Style = (Style)FindResource("DrawerButton"),
            ToolTip = "Save a support zip with crash logs, redacted settings, and environment info",
            Content = "Export diagnostics bundle"
        };
        _diagnosticsExportButton.Click += DiagnosticsExportButton_Click;
        section.Children.Add(_diagnosticsExportButton);
        _diagnosticsStatusText = new TextBlock
        {
            Margin = new Thickness(2, -2, 2, 3),
            FontSize = 8,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            Text = "Secrets stay out · crash logs capped at 256 KB."
        };
        section.Children.Add(_diagnosticsStatusText);
        return section;
    }
}
