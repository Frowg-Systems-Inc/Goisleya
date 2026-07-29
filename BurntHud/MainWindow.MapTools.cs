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
    private const string PinShareCodePrefix = "ISLEYPINS1.";
    private const string RouteShareCodePrefix = "ISLEYROUTE1.";
    private const string NoGoShareCodePrefix = "ISLEYNOGO1.";

    // Session-scoped map-tool toggles owned by this partial (the shared
    // settings schema lives outside map tools and stays untouched).
    private bool _routeAutoReplanEnabled = true;

    private async Task CopyPinShareCodeAsync()
    {
        var code = await ExecuteMapperJsonAsync<string>("window.__isley?.exportPinShareCode?.()");
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(PinShareCodePrefix, StringComparison.Ordinal))
        {
            await ShowHotkeyToastAsync("NO SAVED PINS TO SHARE YET", false);
            return;
        }

        try
        {
            Clipboard.SetText(code);
        }
        catch (Exception exception) when (
            exception is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            await ShowHotkeyToastAsync("CLIPBOARD BUSY · TRY AGAIN", false);
            return;
        }

        await ShowHotkeyToastAsync("PIN SHARE CODE COPIED · SEND IT TO YOUR PACK", true);
    }

    private async Task ImportPinShareCodeFromClipboardAsync()
    {
        string clipboard;
        try
        {
            clipboard = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : string.Empty;
        }
        catch (Exception exception) when (
            exception is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            clipboard = string.Empty;
        }

        if (!clipboard.StartsWith(PinShareCodePrefix, StringComparison.Ordinal) || clipboard.Length > 8192)
        {
            await ShowHotkeyToastAsync("COPY A PIN SHARE CODE FIRST", false);
            return;
        }

        var added = await ExecuteMapperJsonAsync<int?>(
            $"window.__isley?.importPinShareCode?.({JsonSerializer.Serialize(clipboard)})");
        await ShowHotkeyToastAsync(
            added switch
            {
                > 0 => $"{added} SHARED PIN{(added == 1 ? string.Empty : "S")} ADDED TO YOUR MAP",
                0 => "NO NEW PINS · ALREADY ON YOUR MAP",
                _ => "SHARE CODE NOT VALID"
            },
            added > 0);
    }

    private async Task CopyRouteShareCodeAsync()
    {
        var code = await ExecuteMapperJsonAsync<string>("window.__isley?.exportRouteShareCode?.()");
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(RouteShareCodePrefix, StringComparison.Ordinal))
        {
            await ShowHotkeyToastAsync("NO ACTIVE ROUTE TO SHARE YET", false);
            return;
        }

        try
        {
            Clipboard.SetText(code);
        }
        catch (Exception exception) when (
            exception is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            await ShowHotkeyToastAsync("CLIPBOARD BUSY · TRY AGAIN", false);
            return;
        }

        await ShowHotkeyToastAsync("ROUTE SHARE CODE COPIED · SEND IT TO YOUR PACK", true);
    }

    private async Task ImportRouteShareCodeFromClipboardAsync()
    {
        string clipboard;
        try
        {
            clipboard = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : string.Empty;
        }
        catch (Exception exception) when (
            exception is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            clipboard = string.Empty;
        }

        if (!clipboard.StartsWith(RouteShareCodePrefix, StringComparison.Ordinal) || clipboard.Length > 8192)
        {
            await ShowHotkeyToastAsync("COPY A ROUTE SHARE CODE FIRST", false);
            return;
        }

        var stops = await ExecuteMapperJsonAsync<int?>(
            $"window.__isley?.importRouteShareCode?.({JsonSerializer.Serialize(clipboard)})");
        await ShowHotkeyToastAsync(
            stops switch
            {
                > 0 => $"SHARED ROUTE STARTED · {stops} STOPS",
                0 => "NO NEW ROUTE · ALREADY ON YOUR MAP",
                _ => "SHARE CODE NOT VALID"
            },
            stops > 0);
    }

    private async Task CopyNoGoShareCodeAsync()
    {
        var code = await ExecuteMapperJsonAsync<string>("window.__isley?.exportNoGoShareCode?.()");
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(NoGoShareCodePrefix, StringComparison.Ordinal))
        {
            await ShowHotkeyToastAsync("NO NO-GO AREAS TO SHARE YET", false);
            return;
        }

        try
        {
            Clipboard.SetText(code);
        }
        catch (Exception exception) when (
            exception is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            await ShowHotkeyToastAsync("CLIPBOARD BUSY · TRY AGAIN", false);
            return;
        }

        await ShowHotkeyToastAsync("NO-GO SHARE CODE COPIED · SEND IT TO YOUR PACK", true);
    }

    private async Task ImportNoGoShareCodeFromClipboardAsync()
    {
        string clipboard;
        try
        {
            clipboard = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : string.Empty;
        }
        catch (Exception exception) when (
            exception is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            clipboard = string.Empty;
        }

        if (!clipboard.StartsWith(NoGoShareCodePrefix, StringComparison.Ordinal) || clipboard.Length > 8192)
        {
            await ShowHotkeyToastAsync("COPY A NO-GO SHARE CODE FIRST", false);
            return;
        }

        var added = await ExecuteMapperJsonAsync<int?>(
            $"window.__isley?.importNoGoShareCode?.({JsonSerializer.Serialize(clipboard)})");
        await ShowHotkeyToastAsync(
            added switch
            {
                > 0 => $"{added} SHARED NO-GO AREA{(added == 1 ? string.Empty : "S")} ADDED TO YOUR MAP",
                0 => "NO NEW AREAS · ALREADY ON YOUR MAP",
                _ => "SHARE CODE NOT VALID"
            },
            added > 0);
    }

    private async Task UndoMapClearAsync()
    {
        var undone = await ExecuteMapperJsonAsync<string>("window.__isley?.undoLastClear?.()");
        await ShowHotkeyToastAsync(
            undone switch
            {
                "pins" => "PIN CLEAR UNDONE · MARKERS RESTORED",
                "route" => "ROUTE CLEAR UNDONE · PLAN RESTORED",
                "noGo" => "NO-GO REMOVAL UNDONE · AREA RESTORED",
                "measurement" => "MEASUREMENT CLEAR UNDONE",
                _ => "NOTHING TO UNDO"
            },
            !string.IsNullOrEmpty(undone));
    }

    private async Task ToggleRouteAutoReplanAsync()
    {
        _routeAutoReplanEnabled = !_routeAutoReplanEnabled;
        await ExecuteMapperCommandAsync(
            $"window.__isley?.configure({{ routeAutoReplan: {(_routeAutoReplanEnabled ? "true" : "false")} }}) ?? false");
        await ShowHotkeyToastAsync(
            _routeAutoReplanEnabled
                ? "ROUTE AUTO-REPLAN ON · STRAYS RE-PLOT FROM YOU"
                : "ROUTE AUTO-REPLAN OFF",
            true);
    }

    private async Task LoadTerrainRoadNetworkAsync()
    {
        _terrainRoadNetworkCancellation?.Cancel();
        _terrainRoadNetworkCancellation?.Dispose();
        _terrainRoadNetworkCancellation = new CancellationTokenSource();
        var cancellationToken = _terrainRoadNetworkCancellation.Token;
        _terrainCourseStatus = "loading";
        UpdateRoutePlanControls();
        try
        {
            var network = await TerrainRoadNetworkClient.FetchAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _terrainRoadNetwork = network;
            _terrainCourseStatus = "syncing";
            UpdateRoutePlanControls();
            await SyncTerrainRoadNetworkAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            _terrainRoadNetwork = null;
            _terrainNetworkReady = false;
            _terrainNetworkPathCount = 0;
            _terrainNetworkPointCount = 0;
            _terrainNetworkSourceVersion = string.Empty;
            _terrainNetworkLoadedAt = null;
            _terrainCourseStatus = "source-unavailable";
            UpdateRoutePlanControls();
        }
    }

    private async Task SyncTerrainRoadNetworkAsync()
    {
        if (_terrainRoadNetwork is null
            || LiveMapWebView.CoreWebView2 is null
            || !_followControllerInstalled)
        {
            return;
        }

        try
        {
            var payload = _terrainRoadNetwork.ToMapperJson();
            var result = await LiveMapWebView.CoreWebView2.ExecuteScriptAsync(
                $"window.__isley?.loadTerrainRoadNetwork({payload}) ?? false");
            if (!string.Equals(result, "true", StringComparison.OrdinalIgnoreCase))
            {
                _terrainNetworkReady = false;
                _terrainCourseStatus = "syncing";
                UpdateRoutePlanControls();
            }
        }
        catch
        {
            _terrainNetworkReady = false;
            _terrainCourseStatus = "syncing";
            UpdateRoutePlanControls();
        }
    }

    private void TerrainProbeToggleButton_Click(object sender, RoutedEventArgs e) =>
        ToggleUniversalCoordinateCapture();

    private void TerrainProbeClearButton_Click(object sender, RoutedEventArgs e) =>
        ClearUniversalCoordinateSession();

    private async void TerrainProbeSaveAvoidanceButton_Click(object sender, RoutedEventArgs e) =>
        await SaveMeasuredSlopeAvoidanceAsync(showToast: true);

    private async Task<bool> SaveMeasuredSlopeAvoidanceAsync(bool showToast)
    {
        var hill = UniversalCoordinateLogic.DescribeHill(_universalCoordinateMovement);
        var presentation = SlopeSafetyLogic.Present(
            _universalCoordinateCaptureEnabled,
            _universalCoordinateMovement,
            LiveMapServicesActive && _followControllerInstalled
                                      && LiveMapWebView.CoreWebView2 is not null,
            _noGoAreaCount >= NoGoAreaLogic.MaximumAreaCount);
        if (_streamerMode
            || !LiveMapServicesActive
            || !presentation.CanSaveAvoidance
            || hill is null
            || hill.Direction == "LEVEL"
            || _universalCoordinatePreviousPoint is null
            || _universalCoordinatePoint is null)
        {
            if (showToast)
            {
                await ShowHotkeyToastAsync(
                    _noGoAreaCount >= NoGoAreaLogic.MaximumAreaCount
                        ? "NO-GO LIMIT REACHED · REMOVE ONE FIRST"
                        : "MEASURE A CLIMB OR DESCENT FIRST",
                    false);
            }
            return false;
        }

        TerrainProbeSaveAvoidanceButton.IsEnabled = false;
        TerrainProbeStateText.Text = "SAVING";
        TerrainProbeStateText.Foreground = (Brush)FindResource("WarningBrush");
        var label =
            $"Measured {hill.Direction.ToLowerInvariant()} {hill.GradePercent:0}%";
        var saved = await ExecuteMapperCommandAsync(
            "window.__isley?.saveMeasuredSlopeAvoidance(" +
            $"{_universalCoordinatePreviousPoint.X.ToString("R", CultureInfo.InvariantCulture)}," +
            $"{_universalCoordinatePreviousPoint.Y.ToString("R", CultureInfo.InvariantCulture)}," +
            $"{_universalCoordinatePoint.X.ToString("R", CultureInfo.InvariantCulture)}," +
            $"{_universalCoordinatePoint.Y.ToString("R", CultureInfo.InvariantCulture)}," +
            $"{JsonSerializer.Serialize(label)})?.ok === true");
        _universalCoordinateUiSignature = string.Empty;
        UpdateUniversalCoordinatePresentation(force: true);
        if (!saved)
        {
            if (showToast)
            {
                await ShowHotkeyToastAsync(
                    "SLOPE COULD NOT BE ALIGNED · VERIFY MAP CALIBRATION",
                    false);
            }
            return false;
        }

        AddTacticalEvent(
            "ROUTE",
            "Measured slope avoidance saved",
            $"{hill.Direction.ToLowerInvariant()} {hill.GradePercent:0}% · reversible local No-Go corridor");
        if (showToast)
        {
            await ShowHotkeyToastAsync("SLOPE AVOIDANCE SAVED · COURSE REPLANNING", true);
        }
        return true;
    }

    private void UpdateFollowButton(bool following, bool markerAvailable, double? centerErrorPx = null)
    {
        if (!following)
        {
            RecenterButton.Content = "RECENTER";
            RecenterButton.ToolTip = "Jump back to your icon and resume follow mode";
            RecenterButton.Background = (Brush)FindResource("AccentBrush");
            RecenterButton.Foreground = new SolidColorBrush(Color.FromRgb(3, 19, 27));
            UpdateTrackingCoachTip(markerAvailable, following);
            return;
        }

        RecenterButton.Content = markerAvailable
            ? _lookAheadEnabled ? "TRACKING AHEAD" : "TRACKING YOU"
            : "FIND ME";
        var framingDescription = _lookAheadEnabled ? "look-ahead framed" : "centered";
        RecenterButton.ToolTip = markerAvailable && centerErrorPx is not null
            ? $"Following you · {framingDescription} within {centerErrorPx:0.0}px · drag the map to pause"
            : markerAvailable
                ? $"Following you with a {framingDescription} view · drag the map to pause, then select RECENTER"
            : _universalCoordinateCaptureEnabled
                ? "No icon yet · in The Isle press Tab and click Asset Location, then try again"
                : "No icon yet · select SYNC ON first, then copy Asset Location in The Isle";
        RecenterButton.Background = (Brush)FindResource("RaisedSurfaceBrush");
        RecenterButton.Foreground = (Brush)FindResource("PrimaryTextBrush");
        UpdateTrackingCoachTip(markerAvailable, following);
    }

    private void UpdateFreshnessStatus(bool markerAvailable, bool freshnessKnown, double freshnessAgeMs)
    {
        if (!markerAvailable)
        {
            if (!_universalCoordinateCaptureEnabled)
            {
                MapFreshnessText.Text = "SYNC OFF · TAP SYNC ON";
                FreshnessStatusDot.Fill = new SolidColorBrush(Color.FromRgb(126, 137, 149));
            }
            else if (_gameWasRunning)
            {
                MapFreshnessText.Text = "TAB · COPY ASSET LOCATION";
                FreshnessStatusDot.Fill = new SolidColorBrush(Color.FromRgb(255, 178, 74));
            }
            else
            {
                MapFreshnessText.Text = "START THE ISLE · THEN COPY LOCATION";
                FreshnessStatusDot.Fill = new SolidColorBrush(Color.FromRgb(126, 137, 149));
            }
            MapFreshnessText.ToolTip = !_universalCoordinateCaptureEnabled
                ? "Player Sync is off, so Isley will not place your map icon from Asset Location copies"
                : _gameWasRunning
                    ? "Focus The Isle, press Tab, click Asset Location, then return here if the icon does not appear"
                    : "Launch The Isle, keep Player Sync on, then copy Asset Location from the in-game Tab menu";
            _liveHealthMapLabel = MapFreshnessText.Text;
            SetStaleAlert(false);
            UpdateTrackingCoachTip(markerAvailable: false, following: true);
            UpdateLiveHealthStrip();
            return;
        }

        if (!freshnessKnown)
        {
            MapFreshnessText.Text = "SYNCING";
            _liveHealthMapLabel = "SYNCING";
            FreshnessStatusDot.Fill = (Brush)FindResource("WarningBrush");
            SetStaleAlert(false);
            UpdateTrackingCoachTip(markerAvailable: true, following: true);
            UpdateLiveHealthStrip();
            return;
        }

        var ageSeconds = (int)Math.Floor(freshnessAgeMs / 1000);
        if (ageSeconds <= 22)
        {
            MapFreshnessText.Text = $"LIVE · {ageSeconds}s";
            _liveHealthMapLabel = $"{ageSeconds}s";
            FreshnessStatusDot.Fill = (Brush)FindResource("SuccessBrush");
            SetStaleAlert(false);
            UpdateTrackingCoachTip(markerAvailable: true, following: true);
            UpdateLiveHealthStrip();
            return;
        }

        if (ageSeconds <= 45)
        {
            MapFreshnessText.Text = $"SYNCING · {ageSeconds}s";
            _liveHealthMapLabel = $"SYNC · {ageSeconds}s";
            FreshnessStatusDot.Fill = (Brush)FindResource("WarningBrush");
            SetStaleAlert(false);
            UpdateTrackingCoachTip(markerAvailable: true, following: true);
            UpdateLiveHealthStrip();
            return;
        }

        MapFreshnessText.Text = $"STALE · {ageSeconds}s";
        _liveHealthMapLabel = $"STALE · {ageSeconds}s";
        FreshnessStatusDot.Fill = (Brush)FindResource("AccentBrush");
        SetStaleAlert(true, ageSeconds);
        UpdateTrackingCoachTip(markerAvailable: true, following: true);
        UpdateLiveHealthStrip();
    }

    private void UpdateTrackingCoachTip(bool markerAvailable, bool following)
    {
        if (HelpTipBorder is null || HelpTipText is null)
        {
            return;
        }

        if (_streamerMode
            || _recoveryPromptVisible
            || !LiveMapServicesActive
            || StaleAlertBorder?.Visibility == Visibility.Visible)
        {
            HelpTipBorder.Visibility = Visibility.Collapsed;
            return;
        }

        if (!markerAvailable)
        {
            HelpTipText.Text = !_universalCoordinateCaptureEnabled
                ? "Your icon is missing. Select SYNC ON, then in The Isle press Tab and click Asset Location."
                : _gameWasRunning
                    ? "Your icon is missing. Keep The Isle focused, press Tab, and click Asset Location."
                    : "Your icon is missing. Start The Isle, keep SYNC ON, then copy Asset Location from Tab.";
            HelpTipBorder.Visibility = Visibility.Visible;
            return;
        }

        if (!following)
        {
            HelpTipText.Text = "Map explore paused. Select RECENTER to follow your icon again.";
            HelpTipBorder.Visibility = Visibility.Visible;
            return;
        }

        HelpTipBorder.Visibility = Visibility.Collapsed;
    }

    private void UpdatePlayerSyncMapButton()
    {
        if (PlayerSyncMapButton is null)
        {
            return;
        }

        PlayerSyncMapButton.Content = _universalCoordinateCaptureEnabled ? "SYNC ON" : "SYNC OFF";
        PlayerSyncMapButton.ToolTip = _universalCoordinateCaptureEnabled
            ? "Player Sync is listening for Asset Location copies while The Isle or Isley is focused"
            : "Turn Player Sync on so Asset Location copies can place your map icon";
        SetToggleButtonState(PlayerSyncMapButton, _universalCoordinateCaptureEnabled);
        PlayerSyncMapButton.Visibility = LiveMapServicesActive && !_streamerMode
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateFeedDiagnostics(
        int markerResponseCount,
        int markerResponseStatus,
        bool markerResponseOk,
        string markerResponseSource,
        double fastPollIntervalMs,
        double fastPollDelayMs,
        double lastResponseIntervalMs,
        double lastFastPollDurationMs,
        bool pollControlPatched,
        int markerNetworkCount,
        int pollCallbackCount,
        int pollCallbackRuns,
        int controllerInstallCount,
        double selfPositionAt,
        double? selfX,
        double? selfY,
        string selfPoseSource,
        bool isolationStylePresent,
        bool mapIsolated,
        int isolationHiddenCount,
        double isolatedMapWidth)
    {
        var positionAgeSeconds = selfPositionAt > 0
            ? Math.Max(0, (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - selfPositionAt) / 1000)
            : -1;
        var sourceLabel = selfPoseSource switch
        {
            "server-model" => "authorized server response",
            "react-model" => "live map model",
            _ => "rendered map marker"
        };
        var responseSourceLabel = markerResponseSource == "fast-poll"
            ? "fast refresh"
            : markerResponseSource == "official-map"
                ? pollControlPatched ? "accelerated official refresh" : "official page refresh"
                : "initial map model";
        var responseLabel = markerResponseCount > 0
            ? $"{markerResponseCount} accepted marker update{(markerResponseCount == 1 ? string.Empty : "s")} · {markerNetworkCount} network request{(markerNetworkCount == 1 ? string.Empty : "s")} · HTTP {markerResponseStatus} · {responseSourceLabel}"
            : markerResponseStatus > 0
                ? $"latest marker request HTTP {markerResponseStatus}"
                : "starting adaptive fast refresh";
        var cadenceLabel = lastResponseIntervalMs > 0
            ? $"actual cadence {lastResponseIntervalMs / 1000:0.0}s"
            : $"target cadence {Math.Max(0.25, fastPollIntervalMs / 1000):0.#}s";
        var pollLabel = fastPollDelayMs > fastPollIntervalMs + 100
            ? $"backed off to {fastPollDelayMs / 1000:0.#}s"
            : $"adaptive {Math.Max(0.25, fastPollIntervalMs / 1000):0.#}s polling{(pollControlPatched ? " active" : " fallback")} · {pollCallbackCount} page callback{(pollCallbackCount == 1 ? string.Empty : "s")} · {pollCallbackRuns} callback run{(pollCallbackRuns == 1 ? string.Empty : "s")} · controller {controllerInstallCount}";
        var latencyLabel = lastFastPollDurationMs > 0
            ? $"last request {lastFastPollDurationMs:0}ms"
            : "request latency pending";
        var positionLabel = selfX is not null && selfY is not null
            ? $"position {selfX:0.0}, {selfY:0.0}"
            : "position unavailable";
        var ageLabel = positionAgeSeconds >= 0
            ? $"last position / heading change {positionAgeSeconds}s ago"
            : "waiting for the first position / heading change";
        if (markerResponseOk && positionAgeSeconds is >= 0 and <= 3)
        {
            MapFreshnessText.Text = $"UPDATED · {positionAgeSeconds}s";
        }
        var isolationLabel = $"map-only {(mapIsolated && isolationStylePresent ? "active" : "inactive")} · " +
                             $"{isolationHiddenCount} page regions hidden · map width {isolatedMapWidth:0}px";
        MapFreshnessText.ToolTip = $"{sourceLabel} · {responseLabel} · {cadenceLabel} · {pollLabel} · {latencyLabel} · {positionLabel} · {ageLabel} · {isolationLabel}";
        if (markerResponseStatus > 0 && !markerResponseOk)
        {
            MapFreshnessText.ToolTip = $"Marker feed did not return usable live data · {responseLabel}";
        }
    }

    private void UpdateAnimalCount(int otherAnimalCount, int friendAnimalCount = 0, int authorizedAnimalCount = 0)
    {
        if (_streamerMode)
        {
            AnimalCountText.Text = "PRIVACY MODE";
            return;
        }

        if (_friendOnly)
        {
            AnimalCountText.Text = friendAnimalCount switch
            {
                0 => "NO FRIENDS LIVE",
                1 => "1 FRIEND LIVE",
                _ => $"{friendAnimalCount} FRIENDS LIVE"
            };
            return;
        }

        var liveCount = Math.Max(otherAnimalCount, authorizedAnimalCount);
        AnimalCountText.Text = liveCount switch
        {
            0 => "YOU ONLY",
            1 => "1 OTHER LIVE",
            _ when friendAnimalCount > 0 => $"{liveCount} LIVE / {friendAnimalCount} FRIENDS",
            _ => $"{liveCount} OTHERS LIVE"
        };
    }

    private void UpdateMarkerStyleControl()
    {
        _markerStyleIndex = Math.Clamp(_markerStyleIndex, 0, _markerStyleModes.Length - 1);
        var label = _markerStyleLabels[_markerStyleIndex];
        MarkerStyleButton.Content = $"Markers · {label}";
        MarkerStyleButton.ToolTip = _markerStyleIndex switch
        {
            1 => "High-contrast markers use bright fills, heavy outlines, and distinct friend/player geometry",
            2 => "Shape-coded markers show friends as plus-circles and other authorized players as alert-diamonds",
            _ => "Standard markers use blue for you, green for friends, and amber for other authorized players"
        };
        AnimalCountText.ToolTip = _markerStyleIndex switch
        {
            1 => "Cyan-outlined arrow circle is you · white circle is a friend · yellow diamond is another authorized player",
            2 => "Blue arrow circle is you · green plus-circle is a friend · amber alert-diamond is another authorized player",
            _ => "Blue is you · green is a friend · amber is another authorized player"
        };
        SetToggleButtonState(MarkerStyleButton, _markerStyleIndex > 0);
    }

    private void UpdateNavigationReadout(bool markerAvailable)
    {
        var hudPriority = CurrentHudPriorityPresentation();
        NavigationReadoutPanel.Visibility = !HudSurfaceLogic.Show(_navigationHudVisible, _streamerMode)
                                            || _hudDetailModeIndex >= 2
                                            || hudPriority.HideWaitingNavigation
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (!markerAvailable || _currentSelfX is null || _currentSelfY is null)
        {
            PositionText.Text = !_universalCoordinateCaptureEnabled
                ? "TURN SYNC ON FOR YOUR ICON"
                : _gameWasRunning
                    ? "TAB → CLICK ASSET LOCATION"
                    : "START THE ISLE · THEN COPY LOCATION";
            PositionText.ToolTip = !_universalCoordinateCaptureEnabled
                ? "Select SYNC ON in the map chrome or Tools → Player Sync, then copy Asset Location in The Isle"
                : _gameWasRunning
                    ? "Open The Isle Tab menu, click Asset Location while the game is focused, then your icon appears here"
                    : "Launch The Isle first so Player Sync can accept an Asset Location copy";
            HeadingText.Text = "HEADING --";
            CompassLeftText.Text = "--";
            CompassRightText.Text = "--";
            CompassRibbon.Opacity = 0.55;
            UpdateCompassCourseMarker();
            MovementText.Text = "SPEED --";
            CopyPositionButton.IsEnabled = false;
            return;
        }

        CopyPositionButton.IsEnabled = true;
        PositionText.Text = string.IsNullOrWhiteSpace(_currentGridReference)
            ? $"X {_currentSelfX:0}  /  Y {_currentSelfY:0}"
            : $"GRID {_currentGridReference} · X {_currentSelfX:0}  Y {_currentSelfY:0}";
        PositionText.ToolTip = string.IsNullOrWhiteSpace(_currentGridReference)
            ? "Authorized live position"
            : $"Tactical grid {_currentGridReference} · authorized live position";
        CompassRibbon.Opacity = 1;
        CompassLeftText.Text = ToCardinal(_currentSelfBearing - 45);
        HeadingText.Text = $"{ToCardinal(_currentSelfBearing)} {_currentSelfBearing:000}°";
        CompassRightText.Text = ToCardinal(_currentSelfBearing + 45);
        UpdateCompassCourseMarker();
        MovementText.Text = _currentSelfSpeed >= 0.15
            ? $"{_currentSelfSpeed:0.0} MU/MIN  /  TRIP {_currentSessionDistance:0.0}"
            : $"STILL  /  TRIP {_currentSessionDistance:0.0} MU";
    }

    private void UpdateSoundFinder(bool force = false)
    {
        if (SoundFinderStatusText is null
            || SoundFinderStageText is null
            || SoundFinderDetailText is null
            || TrackFinderModeButton is null
            || TrackFinderTargetButton is null
            || SoundFinderCaptureButton is null
            || SoundFinderRouteButton is null
            || SoundFinderClearButton is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var isScent = _trackFinderMode == TrackFinderMode.Scent;
        var scentTargetLabel = TrackFinderModeLogic.TargetLabel(_trackFinderScentTarget);
        var cueLabel = TrackFinderModeLogic.CueLabel(_trackFinderMode, _trackFinderScentTarget);
        var verificationPhrase = TrackFinderModeLogic.VerificationPhrase(_trackFinderMode);
        _soundFinderAnalysis = SoundFinderLogic.Analyze(_soundBearingFirst, _soundBearingSecond, now);
        var captureReady = LiveMapServicesActive
                           && !_streamerMode
                           && _markerAvailable
                           && _currentSelfMapX is not null
                           && _currentSelfMapY is not null
                           && _currentMarkerFreshnessAgeMs <= 8000;
        var ageSeconds = _soundBearingFirst is null
            ? 0
            : Math.Max(0, (now - _soundBearingFirst.CapturedAt).TotalSeconds);
        var signature = string.Join('|',
            _soundFinderAnalysis.Status,
            _soundFinderAnalysis.Confidence,
            Math.Round(_soundFinderAnalysis.UncertaintyRadius),
            Math.Floor(ageSeconds),
            captureReady,
            _trackFinderMode,
            _trackFinderScentTarget,
            _streamerMode,
            LiveMapServicesActive);
        if (!force && string.Equals(signature, _soundFinderUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _soundFinderUiSignature = signature;

        TrackFinderModeButton.Content = isScent ? "MODE · SCENT" : "MODE · SOUND";
        TrackFinderModeButton.ToolTip = isScent
            ? "Switch to audible calls, footsteps, and growls"
            : "Switch to in-game scent clues revealed with your scent key";
        TrackFinderTargetButton.Visibility = isScent ? Visibility.Visible : Visibility.Collapsed;
        TrackFinderTargetButton.Content = scentTargetLabel;
        TrackFinderTargetButton.ToolTip = _trackFinderScentTarget switch
        {
            ScentTargetKind.Water => "Follow a water scent clue; click to choose Food",
            ScentTargetKind.Food => "Follow an edible-food scent clue; click to choose Trail",
            ScentTargetKind.Trail => "Follow blood, footprints, or another animal's trail; click to choose Carcass",
            _ => "Follow a carcass scent clue; click to choose Water"
        };
        Grid.SetColumnSpan(TrackFinderModeButton, isScent ? 1 : 2);
        SetToggleButtonState(TrackFinderModeButton, isScent);
        SoundFinderCaptureButton.IsEnabled = captureReady;
        SoundFinderClearButton.IsEnabled = _soundBearingFirst is not null || _soundBearingSecond is not null;
        SoundFinderRouteButton.IsEnabled = !_streamerMode && _soundFinderAnalysis.HasEstimate;
        SoundFinderCaptureButton.ToolTip = captureReady
            ? $"Capture authorized position and facing · {HotkeyBindingLogic.Format(CurrentHotkeyBinding(HotkeyBindingLogic.TrackBearingId))}"
            : _streamerMode
                ? "Track Finder is hidden in Streamer Mode"
            : "A fresh authorized Live Map position is required";
        SoundFinderRouteButton.ToolTip = isScent
            ? $"Route to the estimated {scentTargetLabel.ToLowerInvariant()} scent area"
            : "Route to the estimated sound area";

        if (_streamerMode)
        {
            SoundFinderStatusText.Text = "HIDDEN";
            SoundFinderStageText.Text = "STREAMER";
            SoundFinderDetailText.Text = "Sound and scent bearings are removed in Streamer Mode.";
            SoundFinderCaptureButton.Content = "CAPTURE UNAVAILABLE";
            SoundFinderStatusText.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }
        if (!LiveMapServicesActive)
        {
            SoundFinderStatusText.Text = "LIVE MAP REQUIRED";
            SoundFinderStageText.Text = "OFF";
            SoundFinderDetailText.Text = "Track Finder needs Live Map mode's authorized position and heading.";
            SoundFinderCaptureButton.Content = "RETURN TO LIVE MAP";
            SoundFinderStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            return;
        }

        SoundFinderStatusText.Foreground = _soundFinderAnalysis.Status == SoundFinderStatus.Ready
            ? new SolidColorBrush(Color.FromRgb(255, 178, 74))
            : _soundFinderAnalysis.Status is SoundFinderStatus.WaitingFirst or SoundFinderStatus.WaitingSecond
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("WarningBrush");
        switch (_soundFinderAnalysis.Status)
        {
            case SoundFinderStatus.WaitingFirst:
                SoundFinderStatusText.Text = captureReady ? "READY" : "POSITION WAITING";
                SoundFinderStageText.Text = "0 / 2";
                SoundFinderDetailText.Text = captureReady
                    ? isScent
                        ? $"Use scent in game, face the {scentTargetLabel.ToLowerInvariant()} clue, then capture its bearing."
                        : "Face a call, footstep, or growl, then capture its bearing."
                    : "Waiting for a fresh authorized player position and heading.";
                SoundFinderCaptureButton.Content = isScent ? "CAPTURE SCENT B1" : "CAPTURE SOUND B1";
                break;
            case SoundFinderStatus.WaitingSecond:
                var secondsLeft = Math.Max(0,
                    (int)Math.Ceiling((SoundFinderLogic.MaximumReadingAge - (now - _soundBearingFirst!.CapturedAt)).TotalSeconds));
                SoundFinderStatusText.Text = "BEARING 1 SAVED";
                SoundFinderStageText.Text = "1 / 2";
                SoundFinderDetailText.Text = $"Move at least {SoundFinderLogic.MinimumBaseline:0} MU, re-face the same {cueLabel}, then capture again · {secondsLeft}s left.";
                SoundFinderCaptureButton.Content = isScent ? "CAPTURE SCENT B2" : "CAPTURE SOUND B2";
                break;
            case SoundFinderStatus.FirstExpired:
                SoundFinderStatusText.Text = "BEARING EXPIRED";
                SoundFinderStageText.Text = "RESET";
                SoundFinderDetailText.Text = "The cue may have moved or faded. Face it again to begin a fresh estimate.";
                SoundFinderCaptureButton.Content = "CAPTURE NEW B1";
                break;
            case SoundFinderStatus.TooClose:
                SoundFinderStatusText.Text = "MOVE FARTHER";
                SoundFinderStageText.Text = "RETAKE B2";
                SoundFinderDetailText.Text = $"Only {_soundFinderAnalysis.BaselineDistance:0.0} MU between readings · move at least {SoundFinderLogic.MinimumBaseline:0} MU.";
                SoundFinderCaptureButton.Content = "RETAKE BEARING 2";
                break;
            case SoundFinderStatus.Parallel:
                SoundFinderStatusText.Text = "WIDEN THE ANGLE";
                SoundFinderStageText.Text = "RETAKE B2";
                SoundFinderDetailText.Text = "The two bearings are nearly parallel. Move sideways and face the same cue again.";
                SoundFinderCaptureButton.Content = "RETAKE BEARING 2";
                break;
            case SoundFinderStatus.Diverging:
                SoundFinderStatusText.Text = "BEARINGS DO NOT MEET";
                SoundFinderStageText.Text = "RETAKE B2";
                SoundFinderDetailText.Text = $"One reading points behind the other. Re-face the same {cueLabel} and retake bearing 2.";
                SoundFinderCaptureButton.Content = "RETAKE BEARING 2";
                break;
            case SoundFinderStatus.TooDistant:
                SoundFinderStatusText.Text = "ESTIMATE OUT OF RANGE";
                SoundFinderStageText.Text = "RETAKE B2";
                SoundFinderDetailText.Text = "The rays meet too far away or outside the map. Move wider and capture bearing 2 again.";
                SoundFinderCaptureButton.Content = "RETAKE BEARING 2";
                break;
            case SoundFinderStatus.Ready:
                var estimateDistance = _currentSelfMapX is not null && _currentSelfMapY is not null
                    ? Math.Sqrt(
                        Math.Pow(_soundFinderAnalysis.EstimateX!.Value - _currentSelfMapX.Value, 2)
                        + Math.Pow(_soundFinderAnalysis.EstimateY!.Value - _currentSelfMapY.Value, 2))
                    : _soundFinderAnalysis.DistanceFromSecond ?? 0;
                var estimateBearing = _currentSelfMapX is not null && _currentSelfMapY is not null
                    ? (Math.Atan2(
                           _soundFinderAnalysis.EstimateX!.Value - _currentSelfMapX.Value,
                           -(_soundFinderAnalysis.EstimateY!.Value - _currentSelfMapY.Value))
                       * 180 / Math.PI + 360) % 360
                    : 0;
                SoundFinderStatusText.Text = $"{_soundFinderAnalysis.Confidence} ESTIMATE";
                SoundFinderStageText.Text = "2 / 2";
                SoundFinderDetailText.Text = $"AREA {estimateDistance:0} MU {ToCardinal(estimateBearing)} · ±{_soundFinderAnalysis.UncertaintyRadius:0} MU · {verificationPhrase}.";
                SoundFinderCaptureButton.Content = isScent ? "CAPTURE NEW SCENT" : "CAPTURE NEW SOUND";
                break;
        }
    }

    private async Task CaptureTrackBearingAsync()
    {
        if (!LiveMapServicesActive)
        {
            await ShowHotkeyToastAsync("LIVE MAP MODE REQUIRED", false);
            return;
        }
        if (_streamerMode)
        {
            await ShowHotkeyToastAsync("TRACK FINDER HIDDEN IN STREAMER MODE", false);
            return;
        }
        if (!_markerAvailable
            || _currentSelfMapX is null
            || _currentSelfMapY is null
            || _currentMarkerFreshnessAgeMs > 8000)
        {
            await ShowHotkeyToastAsync("FRESH PLAYER HEADING UNAVAILABLE", false);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var reading = SoundFinderLogic.Normalize(new SoundBearingReading(
            _currentSelfMapX.Value,
            _currentSelfMapY.Value,
            _currentSelfBearing,
            now));
        var current = SoundFinderLogic.Analyze(_soundBearingFirst, _soundBearingSecond, now);
        if (_soundBearingFirst is null
            || current.Status is SoundFinderStatus.FirstExpired or SoundFinderStatus.Ready)
        {
            _soundBearingFirst = reading;
            _soundBearingSecond = null;
        }
        else
        {
            _soundBearingSecond = reading;
        }

        _soundFinderAnalysis = SoundFinderLogic.Analyze(_soundBearingFirst, _soundBearingSecond, now);
        _soundFinderUiSignature = string.Empty;
        UpdateSoundFinder(force: true);
        await SyncSoundFinderMapAsync();
        var isScent = _trackFinderMode == TrackFinderMode.Scent;
        var targetLabel = TrackFinderModeLogic.TargetLabel(_trackFinderScentTarget);
        var trackLabel = isScent ? $"Scent {targetLabel.ToLowerInvariant()}" : "Sound";
        var toastLabel = isScent ? $"SCENT {targetLabel}" : "SOUND";
        var verificationPhrase = TrackFinderModeLogic.VerificationPhrase(_trackFinderMode);
        if (_soundFinderAnalysis.HasEstimate)
        {
            AddTacticalEvent(
                "NAV",
                $"{trackLabel} estimate ready",
                $"{_soundFinderAnalysis.Confidence} guidance · ±{_soundFinderAnalysis.UncertaintyRadius:0} MU · {verificationPhrase}");
        }

        var message = _soundFinderAnalysis.Status switch
        {
            SoundFinderStatus.WaitingSecond => $"{toastLabel} B1 SAVED · MOVE 5+ MU",
            SoundFinderStatus.Ready => $"{toastLabel} ESTIMATE {_soundFinderAnalysis.Confidence} · ±{_soundFinderAnalysis.UncertaintyRadius:0} MU",
            SoundFinderStatus.TooClose => "MOVE 5+ MU · RETAKE B2",
            SoundFinderStatus.Parallel => "MOVE SIDEWAYS · RETAKE B2",
            SoundFinderStatus.Diverging => $"REFACE {(isScent ? "SCENT" : "SOUND")} · RETAKE B2",
            SoundFinderStatus.TooDistant => "ESTIMATE OUT OF RANGE",
            _ => $"{toastLabel} BEARING READY"
        };
        await ShowHotkeyToastAsync(message, _soundFinderAnalysis.Status is SoundFinderStatus.WaitingSecond or SoundFinderStatus.Ready);
    }

    private async Task SyncSoundFinderMapAsync()
    {
        if (!LiveMapServicesActive || LiveMapWebView.CoreWebView2 is null)
        {
            return;
        }
        if (_streamerMode)
        {
            await ExecuteMapperCommandAsync("window.__isley?.clearSoundFinder() ?? false");
            return;
        }

        static Dictionary<string, object> ReadingPayload(SoundBearingReading reading) => new()
        {
            ["x"] = reading.X,
            ["y"] = reading.Y,
            ["bearing"] = reading.BearingDegrees
        };
        var payload = new Dictionary<string, object?>
        {
            ["mode"] = TrackFinderModeLogic.ModeId(_trackFinderMode),
            ["target"] = TrackFinderModeLogic.TargetId(_trackFinderScentTarget),
            ["first"] = _soundBearingFirst is null ? null : ReadingPayload(_soundBearingFirst),
            ["second"] = _soundBearingSecond is null ? null : ReadingPayload(_soundBearingSecond),
            ["estimate"] = _soundFinderAnalysis.HasEstimate
                ? new Dictionary<string, object>
                {
                    ["x"] = _soundFinderAnalysis.EstimateX!.Value,
                    ["y"] = _soundFinderAnalysis.EstimateY!.Value,
                    ["uncertainty"] = _soundFinderAnalysis.UncertaintyRadius,
                    ["confidence"] = _soundFinderAnalysis.Confidence
                }
                : null
        };
        await ExecuteMapperCommandAsync(
            $"window.__isley?.setSoundFinder({JsonSerializer.Serialize(payload)}) ?? false");
    }

    private async Task ClearSoundFinderAsync(bool showToast, bool logEvent)
    {
        var hadReading = _soundBearingFirst is not null || _soundBearingSecond is not null;
        _soundBearingFirst = null;
        _soundBearingSecond = null;
        _soundFinderAnalysis = SoundFinderLogic.Analyze(null, null, DateTimeOffset.UtcNow);
        _soundFinderUiSignature = string.Empty;
        UpdateSoundFinder(force: true);
        if (LiveMapServicesActive && LiveMapWebView.CoreWebView2 is not null)
        {
            await SyncSoundFinderMapAsync();
        }
        if (logEvent && hadReading)
        {
            AddTacticalEvent("NAV", "Track Finder cleared", "Session-only sound or scent bearings removed");
        }
        if (showToast)
        {
            await ShowHotkeyToastAsync(hadReading ? "TRACK FINDER CLEARED" : "NO TRACK BEARINGS", hadReading);
        }
    }

    private async void SoundFinderCaptureButton_Click(object sender, RoutedEventArgs e) =>
        await CaptureTrackBearingAsync();

    private async void SoundFinderClearButton_Click(object sender, RoutedEventArgs e) =>
        await ClearSoundFinderAsync(showToast: true, logEvent: true);

    private async Task SetTrackFinderModeAsync(TrackFinderMode mode, bool showToast)
    {
        if (_trackFinderMode == mode)
        {
            _soundFinderUiSignature = string.Empty;
            UpdateSoundFinder(force: true);
            if (showToast)
            {
                var currentLabel = mode == TrackFinderMode.Scent
                    ? $"SCENT · {TrackFinderModeLogic.TargetLabel(_trackFinderScentTarget)}"
                    : "SOUND MODE";
                await ShowHotkeyToastAsync(currentLabel, true);
            }
            return;
        }

        var hadTrack = _soundBearingFirst is not null || _soundBearingSecond is not null;
        _trackFinderMode = mode;
        await ClearSoundFinderAsync(showToast: false, logEvent: hadTrack);
        _soundFinderUiSignature = string.Empty;
        UpdateSoundFinder(force: true);
        if (showToast)
        {
            var label = mode == TrackFinderMode.Scent
                ? $"SCENT MODE · {TrackFinderModeLogic.TargetLabel(_trackFinderScentTarget)}"
                : "SOUND MODE";
            await ShowHotkeyToastAsync(label, true);
        }
    }

    private async void TrackFinderModeButton_Click(object sender, RoutedEventArgs e) =>
        await SetTrackFinderModeAsync(TrackFinderModeLogic.Next(_trackFinderMode), showToast: true);

    private async void TrackFinderTargetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_trackFinderMode != TrackFinderMode.Scent)
        {
            return;
        }
        var hadTrack = _soundBearingFirst is not null || _soundBearingSecond is not null;
        _trackFinderScentTarget = TrackFinderModeLogic.Next(_trackFinderScentTarget);
        await ClearSoundFinderAsync(showToast: false, logEvent: hadTrack);
        _soundFinderUiSignature = string.Empty;
        UpdateSoundFinder(force: true);
        await ShowHotkeyToastAsync(
            $"SCENT TARGET · {TrackFinderModeLogic.TargetLabel(_trackFinderScentTarget)}",
            true);
    }

    private async void SoundFinderRouteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_soundFinderAnalysis.HasEstimate || _streamerMode)
        {
            await ShowHotkeyToastAsync("TRACK ESTIMATE UNAVAILABLE", false);
            return;
        }
        var routed = await ExecuteMapperCommandAsync(
            "window.__isley?.routeSoundFinderEstimate() ?? false");
        if (routed)
        {
            var isScent = _trackFinderMode == TrackFinderMode.Scent;
            var routeLabel = isScent
                ? $"Routing to {TrackFinderModeLogic.TargetLabel(_trackFinderScentTarget).ToLowerInvariant()} scent estimate"
                : "Routing to sound estimate";
            AddTacticalEvent(
                "NAV",
                routeLabel,
                $"Directional estimate · {TrackFinderModeLogic.VerificationPhrase(_trackFinderMode)}");
        }
        await ShowHotkeyToastAsync(routed ? "ROUTING TO TRACK ESTIMATE" : "TRACK ROUTE UNAVAILABLE", routed);
    }

    private void UpdateCompassCourseMarker()
    {
        var available = !_streamerMode
                        && _waypointActive
                        && _currentWaypointBearing is not null
                        && _currentSelfX is not null
                        && _currentSelfY is not null;
        if (!available)
        {
            CompassCourseMarker.Visibility = Visibility.Collapsed;
            if (CompassCourseMarker.RenderTransform is TranslateTransform hiddenTransform)
            {
                hiddenTransform.BeginAnimation(TranslateTransform.XProperty, null);
                hiddenTransform.X = 0;
            }
            return;
        }

        var relativeBearing = ((_currentWaypointBearing!.Value - _currentSelfBearing + 540) % 360) - 180;
        var targetOffset = Math.Clamp(relativeBearing / 180 * 84, -84, 84);
        var transform = CompassCourseMarker.RenderTransform as TranslateTransform;
        if (transform is null)
        {
            transform = new TranslateTransform();
            CompassCourseMarker.RenderTransform = transform;
        }
        CompassCourseMarker.Visibility = Visibility.Visible;
        CompassCourseMarker.ToolTip = Math.Abs(relativeBearing) <= 8
            ? "Active course is straight ahead"
            : relativeBearing > 0
                ? $"Active course is {Math.Abs(relativeBearing):0}° right"
                : $"Active course is {Math.Abs(relativeBearing):0}° left";
        var animation = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = targetOffset,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        };
        transform.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private void UpdateMapScaleBar()
    {
        var available = HudSurfaceLogic.Show(_navigationHudVisible, _streamerMode)
                        && _mapScaleBarUnits is not null
                        && _mapScaleBarPixels is not null;
        MapScalePanel.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        if (!available)
        {
            return;
        }

        MapScaleText.Text = $"{_mapScaleBarUnits:0.#} MU";
        MapScaleGraphic.Width = Math.Clamp(_mapScaleBarPixels!.Value, 28, 112);
        MapScalePanel.ToolTip =
            $"{_mapScaleBarUnits:0.#} bundled map units at {_currentMapScale:0.#}x zoom";
    }

    private void UpdateNearestPlaceContext()
    {
        var available = !_streamerMode
                        && _currentSelfX is not null
                        && _currentSelfY is not null
                        && !string.IsNullOrWhiteSpace(_nearestPlaceName)
                        && _nearestPlaceDistance is not null
                        && _nearestPlaceBearing is not null;
        var routeActive = _waypointActive
                          && _waypointLabel.StartsWith("Nearest place · ", StringComparison.Ordinal);

        NearestPlaceButton.IsEnabled = !_streamerMode;
        NearestPlaceButton.Content = _streamerMode
            ? "Nearby place HUD hidden"
            : _nearestPlaceVisible ? "Nearby place HUD on" : "Nearby place HUD off";
        NearestPlaceButton.ToolTip = _streamerMode
            ? "Nearest-place context is hidden in streamer mode"
            : _nearestPlaceVisible
                ? "Hide your nearest bundled map place, distance, and bearing"
                : "Show your nearest bundled map place, distance, and bearing";
        SetToggleButtonState(NearestPlaceButton, _nearestPlaceVisible && !_streamerMode);

        NearestPlacePanel.Visibility = _hudDetailModeIndex == 0 && _nearestPlaceVisible && available
            ? Visibility.Visible
            : Visibility.Collapsed;
        NearestPlaceText.Text = available
            ? $"{_nearestPlaceName} · {_nearestPlaceDistance:0.0} MU · " +
              $"{_nearestPlaceCardinal} {_nearestPlaceBearing:000}°"
            : "WAITING";
        NearestPlaceText.ToolTip = available
            ? $"Nearest of {_officialLandmarkCount} visible bundled map labels"
            : "Waiting for an authorized live position and visible bundled map labels";

        RouteNearestPlaceButton.Content = routeActive
            ? "Clear nearest-place route"
            : available
                ? $"Route to {(_nearestPlaceName.Length <= 24 ? _nearestPlaceName : $"{_nearestPlaceName[..23]}…")}"
                : "Route to nearest place";
        RouteNearestPlaceButton.IsEnabled = !_streamerMode && (routeActive || available);
        RouteNearestPlaceButton.ToolTip = routeActive
            ? "Clear the active nearest-place waypoint"
            : available
                ? $"Route to {_nearestPlaceName} · {_nearestPlaceDistance:0.0} map units · " +
                  $"{_nearestPlaceCardinal} {_nearestPlaceBearing:000}°"
                : "A live self marker and a visible bundled map place are required";
        SetToggleButtonState(RouteNearestPlaceButton, routeActive);
    }

    private static string FormatSessionDuration(double milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes:00}m"
            : $"{duration.Minutes}m {duration.Seconds:00}s";
    }

    private void UpdateResourceFinder(bool force = false)
    {
        if (ResourceFinderStatusText is null
            || ResourceFinderSearchInputBox is null
            || ResourceFinderHeadingText is null
            || ResourceFinderDetailText is null
            || ResourceFinderSourceText is null
            || ResourceFinderPreviousButton is null
            || ResourceFinderRouteButton is null
            || ResourceFinderNextButton is null)
        {
            return;
        }

        _resourceFinderQuery = ResourceFinderLogic.NormalizeQuery(_resourceFinderQuery);
        _resourceFinderSelection = _gatewayResourceNetwork is null
            ? null
            : ResourceFinderLogic.Select(
                _gatewayResourceNetwork.Points,
                _resourceFinderQuery,
                _markerAvailable ? _currentSelfMapX : null,
                _markerAvailable ? _currentSelfMapY : null,
                _resourceFinderResultIndex);
        if (_resourceFinderSelection is not null)
        {
            _resourceFinderResultIndex = _resourceFinderSelection.SelectedIndex;
        }

        if (!_waypointActive)
        {
            _activeResourceRouteId = string.Empty;
            _activeResourceRouteLabel = string.Empty;
        }

        var signature = string.Join('|',
            _resourceFinderStatus,
            _resourceFinderQuery,
            _resourceFinderResultIndex,
            _gatewayResourceNetwork?.Version,
            _gatewayResourceNetwork?.PointCount,
            _resourceFinderSelection?.Site.Id,
            _resourceFinderSelection?.Distance?.ToString("0.0", CultureInfo.InvariantCulture),
            _markerAvailable,
            _streamerMode,
            _waypointActive,
            _waypointArmed,
            _routePlanArmed,
            _routePlanActive,
            _routePlanComplete,
            _activeResourceRouteId);
        if (!force && string.Equals(signature, _resourceFinderUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _resourceFinderUiSignature = signature;

        ResourceFinderSearchInputBox.IsEnabled = !_streamerMode && LiveMapServicesActive;
        if (_streamerMode)
        {
            ResourceFinderStatusText.Text = "HIDDEN";
            ResourceFinderHeadingText.Text = "RESOURCE FINDER HIDDEN";
            ResourceFinderDetailText.Text = "Private route context is redacted in Streamer Mode.";
            ResourceFinderSourceText.Text = "Public source remains unloaded from the rendered and accessible result.";
            ResourceFinderPreviousButton.IsEnabled = false;
            ResourceFinderRouteButton.IsEnabled = false;
            ResourceFinderNextButton.IsEnabled = false;
            return;
        }

        if (!LiveMapServicesActive)
        {
            ResourceFinderStatusText.Text = "MAP PAUSED";
            ResourceFinderHeadingText.Text = "LIVE MAP MODE ONLY";
            ResourceFinderDetailText.Text = "Select Live Map mode to route on the bundled map.";
            ResourceFinderSourceText.Text = "No provider player or resource context is requested in this session.";
            ResourceFinderPreviousButton.IsEnabled = false;
            ResourceFinderRouteButton.IsEnabled = false;
            ResourceFinderNextButton.IsEnabled = false;
            return;
        }

        if (_gatewayResourceNetwork is null)
        {
            var unavailable = string.Equals(
                _resourceFinderStatus,
                "source-unavailable",
                StringComparison.Ordinal);
            ResourceFinderStatusText.Text = unavailable ? "SOURCE OFFLINE" : "SOURCE LOADING";
            ResourceFinderHeadingText.Text = unavailable ? "RESOURCE SOURCE UNAVAILABLE" : "LOADING RESOURCE SITES";
            ResourceFinderDetailText.Text = unavailable
                ? "Use the bundled Food layer or saved resource markers for this session."
                : "Checking the current public Gateway game-file map.";
            ResourceFinderSourceText.Text = "Static source only · no live animal or resource telemetry";
            ResourceFinderPreviousButton.IsEnabled = false;
            ResourceFinderRouteButton.IsEnabled = false;
            ResourceFinderNextButton.IsEnabled = false;
            return;
        }

        ResourceFinderStatusText.Text = $"{_gatewayResourceNetwork.PointCount} SITES · V{_gatewayResourceNetwork.Version}";
        if (_resourceFinderSelection is null)
        {
            ResourceFinderHeadingText.Text = "NO MATCHING SITE";
            ResourceFinderDetailText.Text = "Try salt, mud, gastrolith, prey, plant, fish, crab, boar, deer, or a named food.";
            ResourceFinderSourceText.Text = "Search stays local inside the validated public site catalog.";
            ResourceFinderPreviousButton.IsEnabled = false;
            ResourceFinderRouteButton.IsEnabled = false;
            ResourceFinderNextButton.IsEnabled = false;
            return;
        }

        var selection = _resourceFinderSelection;
        ResourceFinderHeadingText.Text = selection.Site.Name.ToUpperInvariant();
        ResourceFinderDetailText.Text = selection.HasLiveDistance
            ? $"SITE {selection.SelectedIndex + 1}/{selection.MatchCount} · {selection.Distance:0.0} MU · " +
              $"{selection.Cardinal} {selection.Bearing:000}°"
            : $"SITE {selection.SelectedIndex + 1}/{selection.MatchCount} · PLAYER POSITION WAITING";
        var sourceAge = DateTimeOffset.UtcNow - _gatewayResourceNetwork.RetrievedAt;
        var siteDate = selection.Site.Updated is null
            ? "site date unavailable"
            : $"site map {selection.Site.Updated:MMM d}";
        ResourceFinderSourceText.Text =
            $"{siteDate} · checked {FormatStatusAge(sourceAge)} ago · static site, not a live spawn";

        var activeSameResource = _waypointActive
                                 && string.Equals(
                                     _activeResourceRouteId,
                                     selection.Site.Id,
                                     StringComparison.Ordinal);
        var routeBusy = _waypointActive
                        || _waypointArmed
                        || _routePlanArmed
                        || _routePlanActive
                        || _routePlanComplete
                        || _measurementArmed
                        || _measurementActive;
        ResourceFinderPreviousButton.IsEnabled = selection.MatchCount > 1;
        ResourceFinderNextButton.IsEnabled = selection.MatchCount > 1;
        ResourceFinderRouteButton.IsEnabled = activeSameResource || !routeBusy;
        ResourceFinderRouteButton.Content = activeSameResource
            ? "CLEAR"
            : routeBusy ? "ROUTE BUSY" : "ROUTE";
        ResourceFinderRouteButton.ToolTip = activeSameResource
            ? "Clear this resource waypoint"
            : routeBusy
                ? "Finish or clear the current waypoint, route, or measurement first"
                : $"Route to this public {selection.Site.Name} site; verify it in game";
    }

    private void ResourceFinderSearchInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _resourceFinderQuery = ResourceFinderSearchInputBox?.Text ?? "salt";
        _resourceFinderResultIndex = 0;
        _resourceFinderUiSignature = string.Empty;
        UpdateResourceFinder(force: true);
    }

    private void ResourceFinderSearchInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            ResourceFinderSearchInputBox.Text = "salt";
            Focus();
            return;
        }
        if (e.Key == Key.Enter && ResourceFinderRouteButton.IsEnabled)
        {
            e.Handled = true;
            _ = RouteSelectedResourceAsync();
        }
    }

    private void ResourceFinderPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string query } && !_streamerMode)
        {
            ResourceFinderSearchInputBox.Text = query;
            ResourceFinderSearchInputBox.SelectAll();
            ResourceFinderSearchInputBox.Focus();
        }
    }

    private void ResourceFinderPreviousButton_Click(object sender, RoutedEventArgs e)
    {
        _resourceFinderResultIndex--;
        _resourceFinderUiSignature = string.Empty;
        UpdateResourceFinder(force: true);
    }

    private void ResourceFinderNextButton_Click(object sender, RoutedEventArgs e)
    {
        _resourceFinderResultIndex++;
        _resourceFinderUiSignature = string.Empty;
        UpdateResourceFinder(force: true);
    }

    private async void ResourceFinderRouteButton_Click(object sender, RoutedEventArgs e) =>
        await RouteSelectedResourceAsync();

    private async Task RouteSelectedResourceAsync()
    {
        var selection = _resourceFinderSelection;
        if (_streamerMode || !LiveMapServicesActive || selection is null)
        {
            await ShowHotkeyToastAsync("RESOURCE ROUTE UNAVAILABLE", false);
            return;
        }

        var activeSameResource = _waypointActive
                                 && string.Equals(
                                     _activeResourceRouteId,
                                     selection.Site.Id,
                                     StringComparison.Ordinal);
        if (activeSameResource)
        {
            var cleared = await ExecuteMapperCommandAsync(
                "window.__isley?.clearWaypoint() ?? false");
            if (cleared)
            {
                _activeResourceRouteId = string.Empty;
                _activeResourceRouteLabel = string.Empty;
                _resourceFinderUiSignature = string.Empty;
                UpdateResourceFinder(force: true);
            }
            await ShowHotkeyToastAsync(cleared ? "RESOURCE ROUTE CLEARED" : "ROUTE CLEAR FAILED", cleared);
            return;
        }

        var routeBusy = _waypointActive
                        || _waypointArmed
                        || _routePlanArmed
                        || _routePlanActive
                        || _routePlanComplete
                        || _measurementArmed
                        || _measurementActive;
        if (routeBusy)
        {
            await ShowHotkeyToastAsync("CLEAR THE CURRENT ROUTE FIRST", false);
            return;
        }

        var label = $"Resource · {selection.Site.Name}";
        var payload = JsonSerializer.Serialize(new
        {
            x = selection.Site.X,
            y = selection.Site.Y,
            label,
            kind = ResourceFinderLogic.ApproachKind(selection.Site)
        });
        var routed = await ExecuteMapperCommandAsync(
            $"window.__isley?.routeMapPoint({payload}) ?? false");
        if (routed)
        {
            _activeResourceRouteId = selection.Site.Id;
            _activeResourceRouteLabel = label;
            _resourceFinderUiSignature = string.Empty;
            SetToolsOpen(false);
        }
        await ShowHotkeyToastAsync(
            routed ? $"ROUTE · {selection.Site.Name.ToUpperInvariant()}" : "RESOURCE ROUTE UNAVAILABLE",
            routed);
    }

    private void UpdateSessionStats()
    {
        if (_streamerMode)
        {
            SessionStatsText.Text = "Session activity hidden in streamer mode";
            CopySessionStatsButton.IsEnabled = false;
            ResetSessionStatsButton.IsEnabled = false;
            return;
        }

        if (!_sessionStatsActive)
        {
            SessionStatsText.Text = "Waiting for an authorized live position...";
            CopySessionStatsButton.IsEnabled = false;
            ResetSessionStatsButton.IsEnabled = false;
            return;
        }

        SessionStatsText.Text =
            $"{FormatSessionDuration(_sessionElapsedMs)} elapsed · " +
            $"{FormatSessionDuration(_sessionMovingMs)} moving\n" +
            $"{_currentSessionDistance:0.0} MU · {_sessionAverageSpeed:0.0} avg · {_sessionMaxSpeed:0.0} peak";
        SessionStatsText.ToolTip =
            "Local, session-only movement statistics · average speed uses moving time · speeds are MU/min";
        CopySessionStatsButton.IsEnabled = true;
        ResetSessionStatsButton.IsEnabled = true;
    }

    private static string FormatBriefEta(double? minutes)
    {
        if (minutes is null || minutes < 0)
        {
            return string.Empty;
        }

        if (minutes < 1)
        {
            return "<1m";
        }

        var roundedMinutes = Math.Max(1, (int)Math.Ceiling(minutes.Value));
        return roundedMinutes < 60
            ? $"{roundedMinutes}m"
            : $"{roundedMinutes / 60}h {roundedMinutes % 60:00}m";
    }

    private void UpdateBreadcrumbTrailControls()
    {
        BreadcrumbTrailToggleButton.IsEnabled = !_streamerMode;
        BreadcrumbTrailToggleButton.Content = _streamerMode
            ? "TRACE HIDDEN"
            : _breadcrumbTrailVisible ? "TRACE ON" : "SHOW TRACE";
        BreadcrumbTrailToggleButton.ToolTip = _streamerMode
            ? "The private session trail is hidden in streamer mode"
            : _breadcrumbTrailVisible
                ? "Hide the session trail without clearing its path"
                : "Show the session-only path behind your authorized live marker";
        SetToggleButtonState(
            BreadcrumbTrailToggleButton,
            _breadcrumbTrailVisible && !_streamerMode);

        ClearBreadcrumbTrailButton.IsEnabled = !_streamerMode && _breadcrumbPointCount > 0;
        ClearBreadcrumbTrailButton.Content = _clearBreadcrumbConfirmationPending
            ? "CONFIRM CLEAR"
            : "CLEAR TRACE";
        ClearBreadcrumbTrailButton.ToolTip = _clearBreadcrumbConfirmationPending
            ? "Select again within three seconds to clear this session's path"
            : "Clear the private session trail and stop an active breadcrumb return route";
        BreadcrumbTrailStatusText.Foreground = _clearBreadcrumbConfirmationPending
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("SecondaryTextBrush");
        BreadcrumbTrailStatusText.Text = _streamerMode
            ? "Session trail hidden in streamer mode"
            : _clearBreadcrumbConfirmationPending
                ? $"Select Clear Trace again within 3 seconds · {_breadcrumbDistance:0} MU"
                : _breadcrumbPointCount <= 0
                    ? "Waiting for authorized movement · session-only"
                    : _breadcrumbPointCount == 1
                        ? "Path started · move farther to draw the trace"
                        : _breadcrumbTrailVisible
                            ? $"{_breadcrumbDistance:0} MU · {_breadcrumbPointCount} samples · visible · session-only"
                            : $"{_breadcrumbDistance:0} MU · {_breadcrumbPointCount} samples · map trace hidden";
        LearnedPassageRoutingButton.IsEnabled = !_streamerMode && _learnedPassageCount > 0;
        LearnedPassageRoutingButton.Content = _streamerMode
            ? "LEARNED ROUTES HIDDEN"
            : $"LEARNED ROUTES {(_learnedPassageRoutingEnabled ? "ON" : "OFF")}";
        LearnedPassageRoutingButton.ToolTip = _learnedPassageRoutingEnabled
            ? "Current player-traveled passages may participate in route planning; select to use public roads and trails only"
            : "Enable current player-traveled passages as local route evidence";
        SetToggleButtonState(
            LearnedPassageRoutingButton,
            _learnedPassageRoutingEnabled && !_streamerMode && _learnedPassageCount > 0);

        LearnedPassageVisibilityButton.IsEnabled = !_streamerMode && _learnedPassageCount > 0;
        LearnedPassageVisibilityButton.Content = _streamerMode
            ? "LEARNED HIDDEN"
            : _learnedPassageVisible ? "LEARNED VISIBLE" : "SHOW LEARNED";
        SetToggleButtonState(
            LearnedPassageVisibilityButton,
            _learnedPassageVisible && !_streamerMode && _learnedPassageCount > 0);

        SaveLearnedPassageButton.IsEnabled = !_streamerMode
                                             && _terrainNetworkReady
                                             && _breadcrumbPointCount >= 8
                                             && _breadcrumbDistance >= 30;
        SaveLearnedPassageButton.Content = _learnedPassageCount >= 12
            ? "SAVE · REPLACE OLDEST"
            : "SAVE PASSAGE";
        SaveLearnedPassageButton.ToolTip = !_terrainNetworkReady
            ? "Wait for the current road and trail source before saving versioned route evidence"
            : _breadcrumbPointCount < 8 || _breadcrumbDistance < 30
                ? "Move at least 30 MU and collect 8 session samples before saving"
                : "Explicitly save bounded map geometry only; identity, raw world coordinates, and elevation are not stored";

        ClearLearnedPassagesButton.IsEnabled = !_streamerMode && _learnedPassageCount > 0;
        ClearLearnedPassagesButton.Content = _clearLearnedPassagesConfirmationPending
            ? "CONFIRM CLEAR"
            : "CLEAR LEARNED";
        LearnedPassageStatusText.Foreground = _clearLearnedPassagesConfirmationPending
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("SecondaryTextBrush");
        LearnedPassageStatusText.Text = _streamerMode
            ? "Learned passages hidden in streamer mode"
            : _clearLearnedPassagesConfirmationPending
                ? $"Select Clear Learned again within 3 seconds · {_learnedPassageCount} saved"
                : _learnedPassageCount <= 0
                    ? "No saved passages · save a traveled session trail explicitly"
                    : $"{_learnedPassageCount} saved · {_learnedPassageActiveCount} current" +
                      (_learnedPassageStaleCount > 0
                          ? $" · {_learnedPassageStaleCount} held after age/source change"
                          : string.Empty) +
                      $" · {_learnedPassagePointCount} bounded map points · local only";
    }

    private void UpdateExplorationControls()
    {
        var total = Math.Max(1, _explorationTotalSectors);
        var visited = Math.Clamp(_explorationVisitedCount, 0, total);
        var coverage = visited * 100d / total;
        var progressTarget = _streamerMode ? 0 : coverage / 100d;
        ExplorationProgressTransform.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new System.Windows.Media.Animation.DoubleAnimation
            {
                To = progressTarget,
                Duration = TimeSpan.FromMilliseconds(190),
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            });
        ExplorationToggleButton.IsEnabled = !_streamerMode;
        ExplorationToggleButton.Content = _streamerMode
            ? "TRACKING HIDDEN"
            : _explorationEnabled ? "TRACKING ON" : "START TRACKING";
        ExplorationToggleButton.ToolTip = _streamerMode
            ? "Exploration tracking is paused and hidden in streamer mode"
            : _explorationEnabled
                ? "Pause private exploration tracking without clearing visited sectors"
                : "Record visited sectors from authorized self positions on this PC";
        SetToggleButtonState(ExplorationToggleButton, _explorationEnabled && !_streamerMode);

        ClearExplorationButton.IsEnabled = !_streamerMode && visited > 0;
        ClearExplorationButton.Content = _clearExplorationConfirmationPending
            ? "CONFIRM CLEAR"
            : "CLEAR MAP";
        ExplorationStatusText.Foreground = _clearExplorationConfirmationPending
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("SecondaryTextBrush");
        ExplorationStatusText.Text = _streamerMode
            ? "Exploration is hidden and paused in streamer mode"
            : _clearExplorationConfirmationPending
                ? $"Select Clear Map again within 3 seconds · {visited}/{total} sectors"
                : _explorationEnabled
                    ? _markerAvailable
                        ? $"Exploring · {visited}/{total} sectors · {coverage:0.0}% · local only"
                        : $"Tracking ready · {visited}/{total} sectors · {coverage:0.0}% · waiting for you"
                    : visited > 0
                        ? $"Paused · {visited}/{total} sectors · {coverage:0.0}% preserved locally"
                        : "Tracking off · exploration stays private on this PC";
    }

    private void UpdateDangerProximity()
    {
        var alertDistance = _dangerAlertDistances[_dangerAlertIndex];
        var publicTerrainDanger = _nearestDangerPinId.StartsWith(
            "community-terrain-hazard-",
            StringComparison.Ordinal);
        var dangerAvailable = !_streamerMode
                              && !string.IsNullOrWhiteSpace(_nearestDangerPinId)
                              && _nearestDangerDistance is not null
                              && _nearestDangerBearing is not null;
        var dangerInRange = dangerAvailable
                            && alertDistance > 0
                            && _nearestDangerDistance <= alertDistance;
        var alertZoneInRange = !_streamerMode
                               && _insideAlertZone
                               && !string.IsNullOrWhiteSpace(_nearestAlertZonePinId)
                               && _nearestAlertZoneRadius > 0
                               && _nearestAlertZoneDistance is not null
                               && _nearestAlertZoneBearing is not null;

        DangerAlertButton.IsEnabled = !_streamerMode;
        DangerAlertButton.Content = _streamerMode
            ? "Danger alert hidden"
            : alertDistance <= 0 ? "Danger alert off" : $"Danger alert {alertDistance:0} MU";
        DangerAlertButton.ToolTip = _streamerMode
            ? "Danger-marker proximity is hidden in streamer mode"
            : alertDistance <= 0
                ? "Enable a warning radius around saved Danger markers and enabled public terrain-danger points"
                : dangerAvailable
                    ? $"Nearest {(publicTerrainDanger ? "public terrain danger" : "saved Danger marker")}: " +
                      $"{_nearestDangerLabel} · {_nearestDangerDistance:0.0} MU · " +
                      $"{_nearestDangerCardinal} {_nearestDangerBearing:000}°"
                    : $"Warn once within {alertDistance:0} MU of a saved Danger marker or enabled public terrain-danger point";
        SetToggleButtonState(DangerAlertButton, alertDistance > 0 && !_streamerMode);

        DangerAlertBorder.Visibility = HudSurfaceLogic.Show(_alertHudVisible, _streamerMode)
                                       && (alertZoneInRange || dangerInRange)
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!alertZoneInRange && !dangerInRange)
        {
            if (!string.IsNullOrWhiteSpace(_dangerAlertKey))
            {
                AddTacticalEvent(
                    "RECOVERY",
                    "Proximity clear",
                    "Outside configured saved, public-terrain, and alert-zone boundaries");
            }
            _dangerAlertKey = string.Empty;
            DangerAlertBorder.BeginAnimation(OpacityProperty, null);
            DangerAlertBorder.Opacity = 1;
            ProximityAlertHeadingText.Text = "DANGER MARKER NEARBY";
            return;
        }

        string alertKey;
        if (alertZoneInRange)
        {
            var displayLabel = string.IsNullOrWhiteSpace(_nearestAlertZoneLabel)
                ? "Saved alert zone"
                : _nearestAlertZoneLabel;
            var boundaryDepth = Math.Max(0, _nearestAlertZoneRadius - _nearestAlertZoneDistance!.Value);
            ProximityAlertHeadingText.Text = "ALERT ZONE ENTERED";
            DangerAlertText.Text = $"{displayLabel} · {boundaryDepth:0.0} MU inside " +
                                   $"{_nearestAlertZoneRadius:0} MU boundary · " +
                                   $"{_nearestAlertZoneCardinal} {_nearestAlertZoneBearing:000}° to center";
            alertKey = $"zone:{_nearestAlertZonePinId}";
        }
        else
        {
            var displayLabel = string.IsNullOrWhiteSpace(_nearestDangerLabel)
                ? publicTerrainDanger
                    ? "Public terrain danger"
                    : "Saved Danger marker"
                : _nearestDangerLabel;
            ProximityAlertHeadingText.Text = publicTerrainDanger
                ? "TERRAIN DANGER NEARBY"
                : "DANGER MARKER NEARBY";
            DangerAlertText.Text = $"{displayLabel} · {_nearestDangerDistance:0.0} MU · " +
                                   $"{_nearestDangerCardinal} {_nearestDangerBearing:000}°";
            alertKey = $"danger:{_nearestDangerPinId}";
        }

        if (!string.Equals(_dangerAlertKey, alertKey, StringComparison.Ordinal))
        {
            _dangerAlertKey = alertKey;
            AddTacticalEvent(
                "RECOVERY",
                alertZoneInRange
                    ? "Alert zone entered"
                    : publicTerrainDanger
                        ? "Terrain danger nearby"
                        : "Danger marker nearby",
                DangerAlertText.Text,
                warning: true);
            SystemSounds.Exclamation.Play();
            var pulse = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.4,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(260),
                AutoReverse = true
            };
            DangerAlertBorder.BeginAnimation(OpacityProperty, pulse);
        }
    }

    private void UpdateMapGridControl()
    {
        MapGridButton.IsEnabled = !_streamerMode;
        MapGridButton.Content = _streamerMode
            ? "Map grid hidden"
            : _mapGridVisible
                ? string.IsNullOrWhiteSpace(_currentGridReference)
                    ? "Map grid on"
                    : $"Map grid on · {_currentGridReference}"
                : "Map grid off";
        MapGridButton.ToolTip = _streamerMode
            ? "The tactical grid is hidden with the map in streamer mode"
            : !string.IsNullOrWhiteSpace(_settingsPersistenceError)
                ? $"The grid works for this run, but local preferences could not be saved · {_settingsPersistenceError}"
            : _mapGridVisible
                ? "Hide the A1 through J10 tactical reference grid"
                : "Show the A1 through J10 tactical reference grid";
        SetToggleButtonState(MapGridButton, _mapGridVisible && !_streamerMode);
    }

    private void UpdateLandmarkLabelDensityControl()
    {
        _landmarkLabelDensityIndex = Math.Clamp(
            _landmarkLabelDensityIndex, 0, _landmarkLabelDensityModes.Length - 1);
        var mode = _landmarkLabelDensityModes[_landmarkLabelDensityIndex];
        var modeLabel = mode switch
        {
            "focus" => "Focus",
            "all" => "Full",
            _ => "Auto"
        };
        LandmarkLabelDensityButton.IsEnabled = !_streamerMode;
        LandmarkLabelDensityButton.Content = _streamerMode
            ? "Place labels hidden"
            : $"Place labels · {modeLabel}";
        LandmarkLabelDensityButton.ToolTip = _streamerMode
            ? "Official place labels are hidden with the map in streamer mode"
            : mode switch
            {
                "focus" => "Focused detail keeps only the clearest nearby official place names",
                "all" => "Full detail shows every official place label, including overlaps",
                _ => "Adaptive detail removes duplicate and overlapping official place names"
            };
        LandmarkLabelDensityStatusText.Text = _streamerMode
            ? "Map detail is redacted in streamer mode"
            : _officialLandmarkCount <= 0
                ? "Reading official place labels..."
                : mode == "all"
                    ? $"All {_officialLandmarkCount} official labels visible"
                    : $"Showing {_visibleLandmarkCount} of {_officialLandmarkCount} official labels · search still uses all";
        SetToggleButtonState(
            LandmarkLabelDensityButton,
            !_streamerMode && mode != "all");
    }

    private void UpdateOfficialLayerControls()
    {
        SetOfficialLayerButton(LocationsLayerButton, "Place names", _locationsLayer);
        SetOfficialLayerButton(SanctuariesLayerButton, "Sanctuaries (safe)", _sanctuariesLayer);
        SetOfficialLayerButton(MigrationLayerButton, "Migration (diet)", _migrationLayer);
        SetOfficialLayerButton(PatrolLayerButton, "Patrol (hunt)", _patrolLayer);
        SetOfficialLayerButton(FoodLayerButton, "Food sites", _foodLayer);
        SetOfficialLayerButton(HeatmapLayerButton, "Nearby live players", _heatmapLayer);
        SetOfficialLayerButton(OfficialSelfTrailButton, "My session trail", _officialSelfTrail);
        SetOfficialLayerButton(OfficialFriendTrailsButton, "Friend session trails", _officialFriendTrails);
        if (DietFoodLayerButton is not null)
        {
            DietFoodLayerButton.IsEnabled = _foodLayer is not null && _lifeRunActive && !_streamerMode;
            DietFoodLayerButton.Content = _foodLayer is true ? "FOOD LAYER ON" : "SHOW FOOD";
            SetToggleButtonState(DietFoodLayerButton, _foodLayer is true);
        }

        bool?[] layers =
        [
            _locationsLayer,
            _sanctuariesLayer,
            _migrationLayer,
            _patrolLayer,
            _foodLayer,
            _heatmapLayer,
            _officialSelfTrail,
            _officialFriendTrails
        ];
        var knownCount = layers.Count(value => value is not null);
        var enabledCount = layers.Count(value => value is true);
        var activePreset = DetectActiveLayerPreset();
        LayerStatusText.Text = knownCount == 0
            ? "Loading map layers… turn each one on or off below"
            : activePreset is null
                ? $"{enabledCount} of {knownCount} layers visible · custom mix"
                : $"{enabledCount} of {knownCount} layers visible · {FormatLayerPreset(activePreset)}";

        var presetsAvailable = knownCount >= 6;
        CleanLayerPresetButton.IsEnabled = presetsAvailable;
        NavigationLayerPresetButton.IsEnabled = presetsAvailable;
        SurvivalLayerPresetButton.IsEnabled = presetsAvailable;
        AllLayerPresetButton.IsEnabled = presetsAvailable;
        SetToggleButtonState(CleanLayerPresetButton, activePreset == "clean");
        SetToggleButtonState(NavigationLayerPresetButton, activePreset == "navigation");
        SetToggleButtonState(SurvivalLayerPresetButton, activePreset == "survival");
        SetToggleButtonState(AllLayerPresetButton, activePreset == "all");

        LocationSharingText.Text = _shareLocation switch
        {
            true => "Friend visibility: ON - friends can see you",
            false => "Friend visibility: OFF - you are hidden",
            _ => "Friend visibility: unavailable"
        };
        LocationSharingText.Foreground = _shareLocation switch
        {
            true => (Brush)FindResource("SuccessBrush"),
            false => (Brush)FindResource("WarningBrush"),
            _ => (Brush)FindResource("SecondaryTextBrush")
        };
        UpdateFocusModeControls();
    }

    private string? DetectActiveLayerPreset()
    {
        bool?[] layers =
        [
            _locationsLayer,
            _sanctuariesLayer,
            _migrationLayer,
            _patrolLayer,
            _foodLayer,
            _heatmapLayer,
            _officialSelfTrail,
            _officialFriendTrails
        ];
        if (layers.Any(value => value is null))
        {
            return null;
        }

        if (layers.All(value => value is false))
        {
            return "clean";
        }
        if (_locationsLayer is true && _sanctuariesLayer is true &&
            _migrationLayer is true && _patrolLayer is true &&
            _foodLayer is false && _heatmapLayer is false &&
            _officialSelfTrail is false && _officialFriendTrails is false)
        {
            return "navigation";
        }
        if (_locationsLayer is true && _sanctuariesLayer is true &&
            _migrationLayer is true && _patrolLayer is true &&
            _foodLayer is true && _heatmapLayer is true &&
            _officialSelfTrail is false && _officialFriendTrails is false)
        {
            return "survival";
        }
        return layers.All(value => value is true) ? "all" : null;
    }

    private void SetOfficialLayerButton(Button button, string label, bool? state)
    {
        button.IsEnabled = state is not null;
        button.Content = state switch
        {
            true => $"{label} - ON",
            false => $"{label} - OFF",
            _ => $"{label} - unavailable"
        };
        SetToggleButtonState(button, state is true);
    }

    private static string ToCardinal(double bearing)
    {
        string[] cardinals = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        return cardinals[(int)Math.Round(((bearing % 360) + 360) % 360 / 45) % 8];
    }

    private static string FormatLayerPreset(string preset) => preset switch
    {
        "clean" => "MINIMAL (basemap only)",
        "navigation" => "TRAVEL (zones + places)",
        "survival" => "SURVIVAL (food + players)",
        "all" => "EVERYTHING",
        _ => preset
    };

    private static FocusModeDefinition? GetFocusModeDefinition(string id) => FocusModeLogic.Find(id);

    private bool FocusLayerProfileMatches(string layerProfile)
    {
        var expected = FocusModeLogic.LayerState(layerProfile);
        if (expected.Count == 0)
        {
            return false;
        }

        var actual = new Dictionary<string, bool?>
        {
            ["locations"] = _locationsLayer,
            ["sanctuaries"] = _sanctuariesLayer,
            ["migration"] = _migrationLayer,
            ["patrol"] = _patrolLayer,
            ["food"] = _foodLayer,
            ["heatmap"] = _heatmapLayer,
            ["selfTrail"] = _officialSelfTrail,
            ["friendTrails"] = _officialFriendTrails
        };
        return expected.All(pair => actual.TryGetValue(pair.Key, out var state) && state == pair.Value);
    }

    private bool FocusDisplaySettingsMatch(FocusModeDefinition definition) =>
        _playerLabelsVisible == definition.PlayerLabelsVisible
        && _friendOnly == definition.FriendOnly
        && _headingUp == definition.HeadingUp
        && _rangeRingModeIndex == definition.RangeRingModeIndex
        && _mapGridVisible == definition.MapGridVisible
        && _landmarkLabelDensityIndex == definition.LandmarkLabelDensityIndex
        && _breadcrumbTrailVisible == definition.BreadcrumbTrailVisible
        && _friendRadarVisible == definition.FriendRadarVisible
        && _nearestPlaceVisible == definition.NearestPlaceVisible
        && _trailDurationIndex == definition.TrailDurationIndex
        && _arrivalAlertIndex == definition.ArrivalAlertIndex
        && _dangerAlertIndex == definition.DangerAlertIndex
        && _markerStyleIndex == definition.MarkerStyleIndex
        && _hudDetailModeIndex == definition.HudDetailModeIndex
        && _encounterHudVisible == definition.EncounterHudVisible
        && _encounterAlertIndex == definition.EncounterAlertIndex
        && _encounterMemoryIndex == definition.EncounterMemoryIndex;

    private string? DetectActiveFocusMode()
    {
        foreach (var id in FocusModeLogic.Definitions.Select(definition => definition.Id))
        {
            var definition = GetFocusModeDefinition(id);
            if (definition is null
                || !FocusDisplaySettingsMatch(definition)
                || !FocusLayerProfileMatches(definition.LayerProfile))
            {
                continue;
            }

            return id;
        }

        return null;
    }

    private void UpdateFocusModeControls()
    {
        var rememberedDefinition = GetFocusModeDefinition(_activeFocusModeId);
        var activeId = _streamerMode
            ? null
            : rememberedDefinition is not null && FocusDisplaySettingsMatch(rememberedDefinition)
                ? _activeFocusModeId
                : DetectActiveFocusMode();
        var activeDefinition = activeId is null ? null : GetFocusModeDefinition(activeId);
        foreach (var button in new[]
                 {
                     BalancedFocusModeButton,
                     TravelFocusModeButton,
                     SurvivalFocusModeButton,
                     PackFocusModeButton,
                     CombatFocusModeButton,
                     NestFocusModeButton
                 })
        {
            button.IsEnabled = !_streamerMode;
            SetToggleButtonState(
                button,
                !_streamerMode
                && string.Equals(button.Tag as string, activeId, StringComparison.Ordinal));
        }

        RestoreFocusModeButton.IsEnabled = !_streamerMode && _focusModeRestoreSnapshot is not null;
        FocusModeStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
        FocusModeStatusText.Text = _streamerMode
            ? "Focus Modes unavailable in streamer mode"
            : activeDefinition is not null
                ? $"{activeDefinition.Label} · {activeDefinition.Description}"
                : _focusModeRestoreSnapshot is not null
                    ? "Custom setup · Restore returns to the saved pre-mode setup"
                    : "Custom setup · select a mode to preserve it for restore";
    }

    private FocusModeSnapshotSettings CaptureFocusModeSnapshot() => new()
    {
        PlayerLabelsVisible = _playerLabelsVisible,
        FriendOnly = _friendOnly,
        HeadingUp = _headingUp,
        RangeRingsVisible = _rangeRingsVisible,
        RangeRingModeIndex = _rangeRingModeIndex,
        MapGridVisible = _mapGridVisible,
        LandmarkLabelDensityIndex = _landmarkLabelDensityIndex,
        BreadcrumbTrailVisible = _breadcrumbTrailVisible,
        FriendRadarVisible = _friendRadarVisible,
        NearestPlaceVisible = _nearestPlaceVisible,
        TrailDurationIndex = _trailDurationIndex,
        ArrivalAlertIndex = _arrivalAlertIndex,
        DangerAlertIndex = _dangerAlertIndex,
        MarkerStyleIndex = _markerStyleIndex,
        HudDetailModeIndex = _hudDetailModeIndex,
        EncounterHudVisible = _encounterHudVisible,
        EncounterAlertIndex = _encounterAlertIndex,
        EncounterMemoryIndex = _encounterMemoryIndex,
        LocationsLayer = _locationsLayer,
        SanctuariesLayer = _sanctuariesLayer,
        MigrationLayer = _migrationLayer,
        PatrolLayer = _patrolLayer,
        FoodLayer = _foodLayer,
        HeatmapLayer = _heatmapLayer,
        OfficialSelfTrail = _officialSelfTrail,
        OfficialFriendTrails = _officialFriendTrails
    };

    private void ApplyFocusModeDefinition(FocusModeDefinition definition)
    {
        _playerLabelsVisible = definition.PlayerLabelsVisible;
        _friendOnly = definition.FriendOnly;
        _headingUp = definition.HeadingUp;
        _rangeRingModeIndex = Math.Clamp(definition.RangeRingModeIndex, 0, _rangeRingModes.Length - 1);
        _rangeRingsVisible = _rangeRingModeIndex > 0;
        _mapGridVisible = definition.MapGridVisible;
        _landmarkLabelDensityIndex = Math.Clamp(
            definition.LandmarkLabelDensityIndex, 0, _landmarkLabelDensityModes.Length - 1);
        _breadcrumbTrailVisible = definition.BreadcrumbTrailVisible;
        _friendRadarVisible = definition.FriendRadarVisible;
        _nearestPlaceVisible = definition.NearestPlaceVisible;
        _trailDurationIndex = Math.Clamp(definition.TrailDurationIndex, 0, _trailDurations.Length - 1);
        _arrivalAlertIndex = Math.Clamp(definition.ArrivalAlertIndex, 0, _arrivalAlertDistances.Length - 1);
        _dangerAlertIndex = Math.Clamp(definition.DangerAlertIndex, 0, _dangerAlertDistances.Length - 1);
        _markerStyleIndex = Math.Clamp(definition.MarkerStyleIndex, 0, _markerStyleModes.Length - 1);
        _hudDetailModeIndex = Math.Clamp(definition.HudDetailModeIndex, 0, _hudDetailModeLabels.Length - 1);
        _encounterHudVisible = definition.EncounterHudVisible;
        _encounterAlertIndex = Math.Clamp(
            definition.EncounterAlertIndex, 0, _encounterAlertDistances.Length - 1);
        _encounterMemoryIndex = Math.Clamp(
            definition.EncounterMemoryIndex, 0, _encounterMemoryDurations.Length - 1);
    }

    private void ApplyFocusModeSnapshot(FocusModeSnapshotSettings snapshot)
    {
        _playerLabelsVisible = snapshot.PlayerLabelsVisible;
        _friendOnly = snapshot.FriendOnly;
        _headingUp = snapshot.HeadingUp;
        _rangeRingModeIndex = snapshot.RangeRingModeIndex is int savedMode
            ? Math.Clamp(savedMode, 0, _rangeRingModes.Length - 1)
            : snapshot.RangeRingsVisible ? 2 : 0;
        _rangeRingsVisible = _rangeRingModeIndex > 0;
        _mapGridVisible = snapshot.MapGridVisible;
        _landmarkLabelDensityIndex = Math.Clamp(
            snapshot.LandmarkLabelDensityIndex, 0, _landmarkLabelDensityModes.Length - 1);
        _breadcrumbTrailVisible = snapshot.BreadcrumbTrailVisible;
        _friendRadarVisible = snapshot.FriendRadarVisible;
        _nearestPlaceVisible = snapshot.NearestPlaceVisible;
        _trailDurationIndex = Math.Clamp(snapshot.TrailDurationIndex, 0, _trailDurations.Length - 1);
        _arrivalAlertIndex = Math.Clamp(snapshot.ArrivalAlertIndex, 0, _arrivalAlertDistances.Length - 1);
        _dangerAlertIndex = Math.Clamp(snapshot.DangerAlertIndex, 0, _dangerAlertDistances.Length - 1);
        _markerStyleIndex = snapshot.MarkerStyleIndex is int markerStyleIndex
            ? Math.Clamp(markerStyleIndex, 0, _markerStyleModes.Length - 1)
            : _markerStyleIndex;
        _hudDetailModeIndex = snapshot.HudDetailModeIndex is int hudDetailModeIndex
            ? Math.Clamp(hudDetailModeIndex, 0, _hudDetailModeLabels.Length - 1)
            : _hudDetailModeIndex;
        _encounterHudVisible = snapshot.EncounterHudVisible ?? _encounterHudVisible;
        _encounterAlertIndex = snapshot.EncounterAlertIndex is int encounterAlertIndex
            ? Math.Clamp(encounterAlertIndex, 0, _encounterAlertDistances.Length - 1)
            : _encounterAlertIndex;
        _encounterMemoryIndex = snapshot.EncounterMemoryIndex is int encounterMemoryIndex
            ? Math.Clamp(encounterMemoryIndex, 0, _encounterMemoryDurations.Length - 1)
            : _encounterMemoryIndex;
    }

    private static Dictionary<string, bool?> BuildFocusLayerState(string profile) =>
        FocusModeLogic.LayerState(profile).ToDictionary(pair => pair.Key, pair => pair.Value);

    private static Dictionary<string, bool?> BuildFocusLayerState(FocusModeSnapshotSettings snapshot) => new()
    {
        ["locations"] = snapshot.LocationsLayer,
        ["sanctuaries"] = snapshot.SanctuariesLayer,
        ["migration"] = snapshot.MigrationLayer,
        ["patrol"] = snapshot.PatrolLayer,
        ["food"] = snapshot.FoodLayer,
        ["heatmap"] = snapshot.HeatmapLayer,
        ["selfTrail"] = snapshot.OfficialSelfTrail,
        ["friendTrails"] = snapshot.OfficialFriendTrails
    };

    private async Task<bool> ApplyFocusLayerStateAsync(Dictionary<string, bool?> state)
    {
        var knownState = state
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        if (knownState.Count == 0)
        {
            return true;
        }

        var layerOptions = JsonSerializer.Serialize(knownState);
        return await ExecuteMapperCommandAsync(
            $"window.__isley?.applyLayerState({layerOptions}) ?? false");
    }

    private async Task ReapplyActiveFocusModeLayersAsync()
    {
        if (_streamerMode
            || GetFocusModeDefinition(_activeFocusModeId) is not { } definition
            || !FocusDisplaySettingsMatch(definition))
        {
            return;
        }

        var layersApplied = await ApplyFocusLayerStateAsync(
            BuildFocusLayerState(definition.LayerProfile));
        UpdateFocusModeControls();
        if (!layersApplied)
        {
            FocusModeStatusText.Text = $"{definition.Label} display restored · current map layers unavailable";
            FocusModeStatusText.Foreground = (Brush)FindResource("WarningBrush");
        }
    }

    private void SetStaleAlert(bool active, int ageSeconds = 0)
    {
        var wasActive = _staleAlertActive;
        StaleAlertBorder.Visibility = active && HudSurfaceLogic.Show(_alertHudVisible, _streamerMode)
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (active)
        {
            StaleAlertText.Text = $"LOCATION DATA STALE · {ageSeconds}s";
            if (!_staleAlertActive && _staleSoundEnabled)
            {
                SystemSounds.Exclamation.Play();
            }
        }

        _staleAlertActive = active;
        if (active && !wasActive)
        {
            AddTacticalEvent(
                "SYSTEM",
                "Location feed stale",
                $"No fresh authorized position for {ageSeconds}s",
                warning: true);
        }
        else if (!active && wasActive)
        {
            AddTacticalEvent("SYSTEM", "Location feed recovered", "Fresh authorized updates resumed");
        }
    }

    private void UpdateZoomDisplay()
    {
        ZoomPresetButton.Content = $"Zoom {_currentMapScale:0.#}x";
        ZoomPresetButton.ToolTip = _smartZoomEnabled && !_smartZoomSuspended
            ? $"Smart Zoom currently selected {_currentMapScale:0.#}x; manual selection pauses it until RECENTER"
            : $"Current map zoom {_currentMapScale:0.#}x; select to cycle presets";
    }

    private void UpdateSmartFollowControls()
    {
        FollowFramingButton.Content = _lookAheadEnabled
            ? "Follow framing · Look ahead"
            : "Follow framing · Centered";
        FollowFramingButton.ToolTip = _lookAheadEnabled
            ? "Your marker is held behind its facing direction to reveal more terrain ahead"
            : "Your marker stays in the exact center; select to reveal more terrain ahead";
        SmartZoomButton.Content = !_smartZoomEnabled
            ? "Smart zoom · Off"
            : _smartZoomSuspended
                ? "Smart zoom · Paused"
                : "Smart zoom · On";
        SmartZoomButton.ToolTip = !_smartZoomEnabled
            ? "Select to adapt zoom between close, travel, and fast-movement scales"
            : _smartZoomSuspended
                ? "Manual zoom has priority; select or RECENTER to resume Smart Zoom"
                : "Zoom adapts to authorized movement pace; wheel or zoom buttons pause it until RECENTER";
        SetToggleButtonState(FollowFramingButton, _lookAheadEnabled);
        SetToggleButtonState(SmartZoomButton, _smartZoomEnabled && !_smartZoomSuspended);

        if (!_markerAvailable)
        {
            var offlineFraming = _lookAheadEnabled ? "Look-ahead" : "Centered";
            SmartFollowStatusText.Text = _smartZoomSuspended
                ? $"Manual {_currentMapScale:0.#}x · RECENTER resumes automatic zoom"
                : !_smartZoomEnabled
                    ? $"{offlineFraming} framing ready · fixed {_currentMapScale:0.#}x"
                    : "Smart Follow is ready when your authorized marker appears";
            return;
        }

        var framing = _lookAheadEnabled ? "Ahead view" : "Centered view";
        SmartFollowStatusText.Text = !_smartZoomEnabled
            ? $"{framing} · fixed {_currentMapScale:0.#}x"
            : _smartZoomSuspended
                ? $"{framing} · manual {_currentMapScale:0.#}x · RECENTER resumes"
                : $"{framing} · auto {_currentMapScale:0.#}x at {_currentSelfSpeed:0.#} MU/min";
    }

    private void UpdateWaypointStatus(double? distance, double? bearing, string cardinal, string label = "")
    {
        WaypointStatusText.ToolTip = null;
        if (!string.IsNullOrWhiteSpace(_activeResourceRouteLabel)
            && (!_waypointActive
                || !string.Equals(label, _activeResourceRouteLabel, StringComparison.Ordinal)))
        {
            _activeResourceRouteId = string.Empty;
            _activeResourceRouteLabel = string.Empty;
            _resourceFinderUiSignature = string.Empty;
        }
        if (_streamerMode)
        {
            HideWaypointApproachVisual();
            WaypointPanel.Visibility = Visibility.Collapsed;
            WaypointButton.Content = "Place waypoint";
            WaypointLabelText.Text = "WAYPOINT";
            WaypointGuidanceText.Text = "ON COURSE";
            _arrivalRouteKey = string.Empty;
            _arrivalAlertTriggered = false;
            _approachBriefNoticeKey = string.Empty;
            return;
        }

        if (_waypointArmed)
        {
            HideWaypointApproachVisual();
            WaypointPanel.Visibility = HudSurfaceLogic.Show(_navigationHudVisible, _streamerMode)
                ? Visibility.Visible
                : Visibility.Collapsed;
            WaypointLabelText.Text = "NEW WAYPOINT";
            WaypointGuidanceText.Text = "SELECT POINT";
            WaypointGuidanceText.Foreground = (Brush)FindResource("AccentBrush");
            WaypointStatusText.Text = "CLICK MAP TO PLACE";
            WaypointButton.Content = "Cancel waypoint";
            return;
        }

        if (_routePlanComplete)
        {
            HideWaypointApproachVisual();
            WaypointPanel.Visibility = HudSurfaceLogic.Show(_navigationHudVisible, _streamerMode)
                ? Visibility.Visible
                : Visibility.Collapsed;
            WaypointButton.Content = "Clear completed route";
            var breadcrumbRoute = string.Equals(_routePlanSource, "breadcrumb", StringComparison.Ordinal);
            WaypointLabelText.Text = breadcrumbRoute ? "BREADCRUMB RETURN" : "MULTI-STOP ROUTE";
            WaypointGuidanceText.Text = breadcrumbRoute ? "BACKTRACK COMPLETE" : "ROUTE COMPLETE";
            WaypointGuidanceText.Foreground = (Brush)FindResource("SuccessBrush");
            WaypointStatusText.Text = _routePlanTotalDistance is not null
                ? $"{_routeStopCount} STOPS  /  {_routePlanTotalDistance:0.0} MU PLANNED"
                : $"{_routeStopCount} STOPS COMPLETE";
            if (!string.IsNullOrWhiteSpace(_arrivalRouteKey))
            {
                AddTacticalEvent(
                    "ROUTE",
                    "Route complete",
                    _routePlanTotalDistance is not null
                        ? $"{_routeStopCount} stops · {_routePlanTotalDistance:0.0} MU planned"
                        : $"{_routeStopCount} stops completed");
            }
            _arrivalRouteKey = string.Empty;
            _arrivalAlertTriggered = false;
            _approachBriefNoticeKey = string.Empty;
            return;
        }

        if (!_waypointActive)
        {
            HideWaypointApproachVisual();
            WaypointPanel.Visibility = Visibility.Collapsed;
            WaypointButton.Content = "Place waypoint";
            WaypointLabelText.Text = "WAYPOINT";
            WaypointGuidanceText.Text = "ON COURSE";
            if (!string.IsNullOrWhiteSpace(_arrivalRouteKey))
            {
                AddTacticalEvent("ROUTE", "Route cleared", "No active destination");
            }
            _arrivalRouteKey = string.Empty;
            _arrivalAlertTriggered = false;
            _approachBriefNoticeKey = string.Empty;
            return;
        }

        WaypointPanel.Visibility = HudSurfaceLogic.Show(_navigationHudVisible, _streamerMode)
            ? Visibility.Visible
            : Visibility.Collapsed;
        WaypointButton.Content = "Clear waypoint";
        WaypointLabelText.Text = string.IsNullOrWhiteSpace(label)
            ? "WAYPOINT"
            : label.ToUpperInvariant();
        var routeKey = $"{label}\u001f{_waypointKind}\u001f{_friendRouteName}\u001f{_packRouteActive}\u001f{_packOutlierRouteActive}\u001f{_activePinId}";
        if (!string.Equals(routeKey, _arrivalRouteKey, StringComparison.Ordinal))
        {
            var routeWasActive = !string.IsNullOrWhiteSpace(_arrivalRouteKey);
            _arrivalRouteKey = routeKey;
            _arrivalAlertTriggered = false;
            _approachBriefNoticeKey = string.Empty;
            var routeName = string.IsNullOrWhiteSpace(label) ? "Waypoint" : label;
            AddTacticalEvent(
                "ROUTE",
                routeWasActive ? "Route updated" : "Route started",
                _routePlanActive
                    ? $"{routeName} · stop {Math.Clamp(_routeCurrentIndex + 1, 1, Math.Max(1, _routeStopCount))}/{Math.Max(1, _routeStopCount)}"
                    : routeName);
        }
        if (distance is null || bearing is null)
        {
            HideWaypointApproachVisual();
            WaypointGuidanceText.Text = "ROUTE READY";
            WaypointGuidanceText.Foreground = (Brush)FindResource("AccentBrush");
            WaypointStatusText.Text = _packOutlierRouteActive
                ? "PACK OUTLIER OR YOUR POSITION UNAVAILABLE"
                : _packRouteActive
                    ? "PACK CENTER OR YOUR POSITION UNAVAILABLE"
                : string.IsNullOrWhiteSpace(_friendRouteName)
                    ? "WAITING FOR YOUR POSITION"
                    : "FRIEND OR YOUR POSITION UNAVAILABLE";
            WaypointStatusText.ToolTip = "ETA appears after the mapper receives an authorized live position and movement pace";
            return;
        }

        var configuredArrivalDistance = _arrivalAlertDistances[_arrivalAlertIndex];
        var displayArrivalDistance = configuredArrivalDistance > 0 ? configuredArrivalDistance : 3;
        if (distance <= displayArrivalDistance)
        {
            WaypointGuidanceText.Text = "ARRIVED";
            WaypointGuidanceText.Foreground = (Brush)FindResource("SuccessBrush");
            if (configuredArrivalDistance > 0 && !_arrivalAlertTriggered)
            {
                _arrivalAlertTriggered = true;
                AddTacticalEvent(
                    "ROUTE",
                    "Destination reached",
                    $"{WaypointLabelText.Text} · {distance:0.0} MU remaining");
                SystemSounds.Asterisk.Play();
                var pulse = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.58,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(240),
                    AutoReverse = true,
                    RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2)
                };
                WaypointPanel.BeginAnimation(OpacityProperty, pulse);
            }
        }
        else if (string.Equals(_waypointTrend, "away", StringComparison.Ordinal)
                 && _waypointClosingRate is <= -2)
        {
            WaypointGuidanceText.Text = "MOVING AWAY";
            WaypointGuidanceText.Foreground = (Brush)FindResource("WarningBrush");
        }
        else
        {
            var turnDelta = ((bearing.Value - _currentSelfBearing + 540) % 360) - 180;
            var absoluteTurn = Math.Abs(turnDelta);
            WaypointGuidanceText.Text = absoluteTurn <= 8
                ? "ON COURSE"
                : turnDelta > 0
                    ? $"TURN RIGHT {absoluteTurn:0}°"
                    : $"TURN LEFT {absoluteTurn:0}°";
            WaypointGuidanceText.Foreground = (Brush)FindResource("AccentBrush");
        }

        var eta = BuildNavigationEtaText(_routePlanActive);
        WaypointStatusText.Text = string.IsNullOrWhiteSpace(eta)
            ? $"{distance:0.0} MU  /  {cardinal} {bearing:000} DEG"
            : $"{distance:0.0} MU  /  {cardinal} {bearing:000} DEG  /  {eta}";
        WaypointStatusText.ToolTip = string.IsNullOrWhiteSpace(eta)
            ? "Move briefly to establish a stable ETA pace"
            : $"Estimated from {_navigationEtaSource.ToLowerInvariant()} pace at {_navigationEtaPace:0.0} MU/min" +
              (_routePlanActive ? " across the remaining route" : " to this destination");
        UpdateWaypointApproachVisual(distance <= displayArrivalDistance);
        UpdateWaypointApproachBrief(distance.Value);
    }

    private void HideWaypointApproachVisual()
    {
        WaypointApproachText.Visibility = Visibility.Collapsed;
        WaypointApproachBriefText.Visibility = Visibility.Collapsed;
        WaypointPanel.Margin = new Thickness(9, 9, 9, 54);
        _approachBriefUiSignature = string.Empty;
        WaypointProgressTrack.Visibility = Visibility.Collapsed;
        WaypointProgressTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        WaypointProgressTransform.ScaleX = 0;
    }

    private void UpdateWaypointApproachVisual(bool arrived)
    {
        var progress = arrived
            ? 100
            : Math.Clamp(_waypointProgressPercent ?? 0, 0, 100);
        var hasProgress = arrived || progress >= 0.5;
        var hasTrend = _waypointTrend is "closing" or "away" or "steady";
        WaypointProgressTrack.Visibility = hasProgress ? Visibility.Visible : Visibility.Collapsed;
        if (hasProgress)
        {
            WaypointProgressFill.Background = arrived
                ? (Brush)FindResource("SuccessBrush")
                : string.Equals(_waypointTrend, "away", StringComparison.Ordinal)
                    ? (Brush)FindResource("WarningBrush")
                    : (Brush)FindResource("AccentBrush");
            var animation = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = progress / 100,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            };
            WaypointProgressTransform.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                animation,
                System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            WaypointProgressTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            WaypointProgressTransform.ScaleX = 0;
        }

        if (!hasTrend || arrived)
        {
            WaypointApproachText.Visibility = Visibility.Collapsed;
            return;
        }

        var progressText = hasProgress ? $" · {progress:0}% OF LEG" : string.Empty;
        switch (_waypointTrend)
        {
            case "closing" when _waypointClosingRate is not null:
                WaypointApproachText.Text = $"CLOSING {_waypointClosingRate.Value:0.0} MU/MIN{progressText}";
                WaypointApproachText.Foreground = (Brush)FindResource("AccentBrush");
                break;
            case "away" when _waypointClosingRate is not null:
                WaypointApproachText.Text = $"OPENING {Math.Abs(_waypointClosingRate.Value):0.0} MU/MIN · CHECK COURSE";
                WaypointApproachText.Foreground = (Brush)FindResource("WarningBrush");
                break;
            default:
                WaypointApproachText.Text = $"HOLDING RANGE{progressText}";
                WaypointApproachText.Foreground = (Brush)FindResource("SecondaryTextBrush");
                break;
        }
        WaypointApproachText.Visibility = Visibility.Visible;
    }

    private ApproachBriefView CurrentApproachBrief(double? distance = null) =>
        ApproachBriefLogic.Evaluate(new ApproachBriefSnapshot(
            _streamerMode,
            _waypointActive,
            distance ?? _currentWaypointDistance,
            _waypointKind,
            CurrentEffectiveSpeciesId(),
            string.Equals(_waypointTrend, "away", StringComparison.Ordinal)));

    private void UpdateWaypointApproachBrief(double distance)
    {
        var view = CurrentApproachBrief(distance);
        var signature = string.Join('|',
            view.Visible,
            view.Key,
            view.Heading,
            view.HudLine,
            view.Detail,
            view.Tone);
        if (string.Equals(signature, _approachBriefUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _approachBriefUiSignature = signature;

        WaypointApproachBriefText.Visibility = view.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
        WaypointPanel.Margin = view.Visible
            ? new Thickness(9, 9, 9, 78)
            : new Thickness(9, 9, 9, 54);
        if (!view.Visible) return;

        WaypointApproachBriefText.Text = view.HudLine;
        WaypointApproachBriefText.ToolTip = view.Detail;
        WaypointApproachBriefText.Foreground = view.Tone switch
        {
            ApproachBriefTone.Warning => (Brush)FindResource("WarningBrush"),
            ApproachBriefTone.Active => (Brush)FindResource("AccentBrush"),
            _ => (Brush)FindResource("SecondaryTextBrush")
        };
        WaypointApproachBriefText.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0.35,
                1,
                TimeSpan.FromMilliseconds(160)));

        var noticeKey = $"{_arrivalRouteKey}\u001f{view.Kind}";
        if (string.Equals(noticeKey, _approachBriefNoticeKey, StringComparison.Ordinal))
        {
            return;
        }

        _approachBriefNoticeKey = noticeKey;
        AddTacticalEvent(
            "APPROACH",
            view.Heading,
            $"{view.Kind.ToUpperInvariant()} · verify destination and terrain in game");
        _ = ShowHotkeyToastAsync(
            $"{view.Heading} · {view.HudLine}",
            view.Tone != ApproachBriefTone.Warning);
    }

    private string BuildNavigationEtaText(bool routeEta)
    {
        if (_navigationEtaMinutes is null || _navigationEtaPace is null
            || !double.IsFinite(_navigationEtaMinutes.Value)
            || _navigationEtaMinutes.Value < 0 || string.IsNullOrWhiteSpace(_navigationEtaSource))
        {
            return string.Empty;
        }

        var source = _navigationEtaSource switch
        {
            "LIVE" => "LIVE",
            "RECENT" => "RECENT",
            "TRIP" => "TRIP",
            _ => string.Empty
        };
        return $"{(routeEta ? "ROUTE ETA" : "ETA")} {FormatEtaMinutes(_navigationEtaMinutes.Value)} · {source}";
    }

    private static string FormatEtaMinutes(double minutes)
    {
        if (!double.IsFinite(minutes) || minutes < 0)
        {
            return string.Empty;
        }
        if (minutes < 1)
        {
            return "<1m";
        }

        var roundedMinutes = (int)Math.Ceiling(minutes);
        if (roundedMinutes < 60)
        {
            return $"{roundedMinutes}m";
        }

        var hours = roundedMinutes / 60;
        var remainingMinutes = roundedMinutes % 60;
        return remainingMinutes == 0 ? $"{hours}h" : $"{hours}h {remainingMinutes}m";
    }

    private void ClearMeasurementValues()
    {
        _measurementDistance = null;
        _measurementBearing = null;
        _measurementCardinal = string.Empty;
        _measurementStartWorldX = null;
        _measurementStartWorldY = null;
        _measurementEndWorldX = null;
        _measurementEndWorldY = null;
        _measurementMarkedBoundaryCount = 0;
        _measurementInsideMarkedBoundary = false;
    }

    private void ClearRoutePlanValues()
    {
        _routePlanSource = string.Empty;
        _tripRouteObstacleCount = 0;
        _tripRouteInsideObstacle = false;
        _routeStopCount = 0;
        _routeCurrentIndex = 0;
        _routePlanTotalDistance = null;
        _routeRemainingDistance = null;
        _routeStops.Clear();
    }

    private void UpdateRoutePlanControls()
    {
        UpdateTerrainCourseControls();
        RoutePlanStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
        RoutePlanStatusText.ToolTip = null;
        var hasShareableRoute = _routeStopCount >= 2
                                && _routeStops.Count == _routeStopCount
                                && _routeStops.All(stop => stop.WorldX is not null && stop.WorldY is not null);
        var routeExists = _routePlanArmed || _routePlanActive || _routePlanComplete || _routeStopCount > 0;
        var advanceDistance = Math.Max(3, _arrivalAlertDistances[_arrivalAlertIndex]);
        var breadcrumbRoute = string.Equals(_routePlanSource, "breadcrumb", StringComparison.Ordinal);
        var terrainRoute = string.Equals(_routePlanSource, "terrain", StringComparison.Ordinal);

        if (_streamerMode)
        {
            RoutePlanStatusText.Text = "Route plans hidden in streamer mode";
            RoutePlanButton.Content = "Plan multi-stop route";
            RoutePlanButton.ToolTip = "Route planning is unavailable in streamer mode";
            RoutePlanButton.IsEnabled = false;
            UndoRouteStopButton.IsEnabled = false;
            SkipRouteStopButton.IsEnabled = false;
            CopyRoutePlanButton.IsEnabled = false;
            PasteRoutePlanButton.IsEnabled = false;
            ClearRoutePlanButton.IsEnabled = false;
            SetToggleButtonState(RoutePlanButton, false);
            return;
        }

        UndoRouteStopButton.IsEnabled = _routePlanArmed && _routeStopCount > 0;
        SkipRouteStopButton.IsEnabled = _routePlanActive;
        CopyRoutePlanButton.IsEnabled = hasShareableRoute;
        PasteRoutePlanButton.IsEnabled = LiveMapServicesActive && _followControllerInstalled;
        ClearRoutePlanButton.IsEnabled = routeExists;
        SetToggleButtonState(RoutePlanButton, _routePlanArmed || _routePlanActive || _routePlanComplete);

        if (_routePlanArmed)
        {
            RoutePlanButton.ToolTip = _routeStopCount >= 2
                ? "Start navigating the ordered route"
                : "Place at least two stops on the map";
            RoutePlanButton.Content = _routeStopCount >= 2
                ? $"Start route · {_routeStopCount} stops"
                : "Add at least 2 stops";
            RoutePlanButton.IsEnabled = _routeStopCount >= 2;
            RoutePlanStatusText.Text = _routeStopCount switch
            {
                0 => "Click the map to place stop 1",
                1 => "1 stop set · click the map again",
                >= 12 => $"12-stop limit reached · {_routePlanTotalDistance:0.0} MU planned",
                _ => $"{_routeStopCount} stops · {_routePlanTotalDistance:0.0} MU · add more or start"
            };
            return;
        }

        if (_routePlanActive)
        {
            var current = Math.Clamp(_routeCurrentIndex + 1, 1, Math.Max(1, _routeStopCount));
            RoutePlanButton.Content = breadcrumbRoute
                ? current < _routeStopCount
                    ? $"Next backtrack · {current}/{_routeStopCount}"
                    : $"Complete backtrack · {current}/{_routeStopCount}"
                : terrainRoute
                    ? current < _routeStopCount
                        ? $"Next course bend · {current}/{_routeStopCount}"
                        : $"Complete course · {current}/{_routeStopCount}"
                : current < _routeStopCount
                    ? $"Next stop · {current}/{_routeStopCount}"
                    : $"Complete route · {current}/{_routeStopCount}";
            RoutePlanButton.ToolTip = current < _routeStopCount
                ? "Manually advance to the next route stop"
                : "Mark the final route stop complete";
            RoutePlanButton.IsEnabled = true;
            var stopLabel = breadcrumbRoute ? "Breadcrumb" : terrainRoute ? "Course" : "Stop";
            var routeEta = BuildNavigationEtaText(true);
            var routeEtaSuffix = string.IsNullOrWhiteSpace(routeEta)
                ? string.Empty
                : $" · {routeEta.Replace("ROUTE ", string.Empty, StringComparison.Ordinal)}";
            RoutePlanStatusText.Text = !_markerAvailable
                ? $"{stopLabel} {current} of {_routeStopCount} · waiting for your position · advances at {advanceDistance:0} MU"
                : _routeRemainingDistance is not null
                ? $"{stopLabel} {current} of {_routeStopCount} · {_routeRemainingDistance:0.0} MU remaining{routeEtaSuffix} · advances at {advanceDistance:0} MU"
                : $"{stopLabel} {current} of {_routeStopCount} · advances at {advanceDistance:0} MU";
            RoutePlanStatusText.ToolTip = string.IsNullOrWhiteSpace(routeEta)
                ? "ETA appears after movement establishes a stable pace"
                : $"Whole-route ETA uses {_navigationEtaSource.ToLowerInvariant()} pace at {_navigationEtaPace:0.0} MU/min";
            return;
        }

        if (_routePlanComplete)
        {
            RoutePlanButton.Content = breadcrumbRoute
                ? $"Backtrack complete · {_routeStopCount}/{_routeStopCount}"
                : terrainRoute
                    ? $"Course complete · {_routeStopCount}/{_routeStopCount}"
                : $"Route complete · {_routeStopCount}/{_routeStopCount}";
            RoutePlanButton.ToolTip = "The route sequence is complete; use Clear to remove it";
            RoutePlanButton.IsEnabled = false;
            RoutePlanStatusText.Text = _routePlanTotalDistance is not null
                ? breadcrumbRoute
                    ? $"Session path retraced · {_routePlanTotalDistance:0.0} MU planned"
                    : terrainRoute
                        ? $"Road/trail course complete · {_routePlanTotalDistance:0.0} MU"
                    : $"Route sequence complete · {_routePlanTotalDistance:0.0} MU planned"
                : breadcrumbRoute
                    ? "Session path retraced"
                    : terrainRoute ? "Road/trail course complete" : "Route sequence complete";
            return;
        }

        RoutePlanButton.Content = "Plan multi-stop route";
        RoutePlanButton.ToolTip = "Start planning an ordered route on the map";
        RoutePlanButton.IsEnabled = true;
        RoutePlanStatusText.Text = "Build a route with up to 12 map stops";
    }

    private void UpdateTerrainCourseControls()
    {
        var terrainRoute = string.Equals(_routePlanSource, "terrain", StringComparison.Ordinal);
        var routeStyle = TerrainRouteStyleLogic.Resolve(_terrainRouteStyle);
        var gapPolicy = TerrainGapPolicyLogic.Resolve(_terrainGapPolicy);
        TerrainRouteConfidenceButton.IsEnabled = !_streamerMode;
        TerrainRouteConfidenceButton.Content =
            $"EVIDENCE · {(_terrainRouteConfidenceVisible ? "ON" : "OFF")}";
        SetToggleButtonState(TerrainRouteConfidenceButton, _terrainRouteConfidenceVisible);
        TerrainRouteConfidencePanel.Visibility = Visibility.Collapsed;
        TerrainBlockedPassageButton.IsEnabled = !_streamerMode
                                                 && terrainRoute
                                                 && _routePlanActive
                                                 && _markerAvailable
                                                 && _noGoAreaCount < NoGoAreaLogic.MaximumAreaCount;
        TerrainBlockedPassageButton.Content = _noGoAreaCount >= NoGoAreaLogic.MaximumAreaCount
            ? "OBSTACLE LIMIT REACHED · MANAGE NO-GO AREAS"
            : "BLOCK CURRENT PASSAGE · REPLAN";
        TerrainBlockedPassageButton.ToolTip = terrainRoute
            ? "Save a small reversible local obstacle across the course ahead of you and immediately replan around it"
            : "Plot a road/trail course before reporting a blocked passage";
        var hasUsableDestination = _waypointActive
                                   && !_waypointArmed
                                   && (!_routePlanActive || terrainRoute);
        TerrainCourseButton.IsEnabled = !_streamerMode
                                        && _terrainNetworkReady
                                        && _markerAvailable
                                        && (hasUsableDestination || terrainRoute);
        TerrainCourseButton.Content = terrainRoute
            ? "REPLAN ROAD / TRAIL COURSE"
            : "PLOT ROAD / TRAIL COURSE";
        TerrainCourseButton.ToolTip = terrainRoute
            ? "Recalculate from your current authorized position"
            : "Choose a destination first, then follow mapped roads and trails around saved Danger zones and traced No-Go obstacles";
        SetToggleButtonState(TerrainCourseButton, terrainRoute);
        TerrainRouteStyleButton.IsEnabled = !_streamerMode && _terrainNetworkReady;
        TerrainRouteStyleButton.Content = $"STYLE · {routeStyle.Label}";
        TerrainRouteStyleButton.ToolTip = $"{routeStyle.Description} " +
                                          "All styles still enforce saved Danger zones, traced No-Go areas, and enabled water safety.";
        TerrainGapPolicyButton.IsEnabled = !_streamerMode && _terrainNetworkReady;
        TerrainGapPolicyButton.Content =
            $"OFF-NETWORK GAPS · {gapPolicy.Label.ToUpperInvariant()} ≤{gapPolicy.MaximumConnectorDistance:0} MU";
        TerrainGapPolicyButton.ToolTip =
            $"{gapPolicy.Description} This limit applies only to unmapped endpoint connectors; mapped roads and trails remain available.";
        TerrainWaterSafetyButton.IsEnabled = !_streamerMode
                                             && _terrainWaterMaskStatus is not "hidden";
        TerrainWaterSafetyButton.Content = _terrainWaterMaskStatus == "loading"
            ? "WATER SAFETY · SYNCING"
            : $"WATER SAFETY · {(_terrainWaterSafetyEnabled ? "ON" : "OFF")}";
        TerrainWaterSafetyButton.ToolTip = _terrainWaterMaskStatus switch
        {
            "ready" => "Current game-file drinkable-water mask is ready. When enabled, unmapped course connectors avoid water; mapped bridges and fords remain available.",
            "loading" => "Loading and aligning the current drinkable-water mask",
            _ => "The current drinkable-water mask is unavailable. Saved Danger zones and traced No-Go areas still apply."
        };
        SetToggleButtonState(
            TerrainWaterSafetyButton,
            _terrainWaterSafetyEnabled && _terrainWaterMaskStatus == "ready");
        TerrainCommunityHazardsButton.IsEnabled = !_streamerMode
                                                  && _terrainCommunityHazardStatus == "ready";
        TerrainCommunityHazardsButton.Content = _terrainCommunityHazardStatus switch
        {
            "waiting-source" or "loading" => "TERRAIN DANGER · SYNCING",
            "ready" => $"TERRAIN DANGER · {(_terrainCommunityHazardsEnabled ? "ON" : "OFF")} ({_terrainCommunityHazardCount})",
            _ => "TERRAIN DANGER · UNAVAILABLE"
        };
        var communityHazardLoaded = _terrainCommunityHazardLoadedAt is null
            ? string.Empty
            : $" Loaded {_terrainCommunityHazardLoadedAt.Value.ToLocalTime():g}.";
        TerrainCommunityHazardsButton.ToolTip = _terrainCommunityHazardStatus switch
        {
            "ready" => $"{_terrainCommunityHazardCount} public DANGER spot{(_terrainCommunityHazardCount == 1 ? string.Empty : "s")} on the map.{communityHazardLoaded} " +
                       "When ON, routes stay outside each marked circle (~12 MU). Community reports only — not full cliff/mountain coverage. Your Danger pins and No-Go areas still apply.",
            "waiting-source" or "loading" => "Loading public DANGER spots for the map",
            _ => "Public DANGER spots unavailable. Your saved Danger pins and traced No-Go areas still apply."
        };
        SetToggleButtonState(
            TerrainCommunityHazardsButton,
            _terrainCommunityHazardsEnabled && _terrainCommunityHazardStatus == "ready");
        TerrainCourseStatusText.ToolTip = null;

        TerrainCourseStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
        if (_streamerMode)
        {
            TerrainCourseStatusText.Text = "Course details hidden in streamer mode";
            TerrainCourseButton.IsEnabled = false;
            TerrainRouteStyleButton.IsEnabled = false;
            TerrainGapPolicyButton.IsEnabled = false;
            TerrainCommunityHazardsButton.IsEnabled = false;
            return;
        }

        if (!_terrainNetworkReady)
        {
            TerrainCourseStatusText.Text = _terrainCourseStatus switch
            {
                "source-unavailable" => "Current road/trail source unavailable · normal routes still work",
                "syncing" => "Calibrating the current road/trail network to this map",
                _ => "Loading the current Gateway road and trail network"
            };
            TerrainCourseStatusText.Foreground = _terrainCourseStatus == "source-unavailable"
                ? (Brush)FindResource("WarningBrush")
                : (Brush)FindResource("SecondaryTextBrush");
            return;
        }

        if (terrainRoute && _terrainCourseDistance is not null)
        {
            var confidence = TerrainRouteConfidenceLogic.Evaluate(
                _terrainCourseRoadDistance,
                _terrainCourseTrailDistance,
                _terrainCourseUnknownDistance,
                _terrainCourseLongestUnknown,
                _terrainCourseUnknownSegmentCount,
                _terrainWaterSafetyEnabled && _terrainWaterMaskStatus == "ready",
                _terrainCourseLearnedDistance);
            TerrainRouteConfidencePanel.Visibility = _terrainRouteConfidenceVisible
                                                   && HudSurfaceLogic.Show(_navigationHudVisible, _streamerMode)
                ? Visibility.Visible
                : Visibility.Collapsed;
            TerrainRouteConfidenceHeadingText.Text = $"MAPPED {confidence.MappedPercent:0}%";
            TerrainRouteConfidenceLevelText.Text = confidence.Label;
            TerrainRouteConfidenceProgressBar.Value = confidence.MappedPercent;
            TerrainRouteConfidenceDetailText.Text = confidence.Detail;
            TerrainRouteConfidenceGuidanceText.Text = confidence.Guidance;
            var confidenceBrush = confidence.Level switch
            {
                TerrainRouteConfidenceLogic.High => (Brush)FindResource("SuccessBrush"),
                TerrainRouteConfidenceLogic.Moderate => (Brush)FindResource("WarningBrush"),
                _ => Brushes.OrangeRed
            };
            TerrainRouteConfidenceLevelText.Foreground = confidenceBrush;
            TerrainRouteConfidenceProgressBar.Foreground = confidenceBrush;
            var direct = _terrainCourseDirectDistance is not null
                ? $" vs {_terrainCourseDirectDistance:0} direct"
                : string.Empty;
            var detour = _terrainCourseDetourPercent is not null
                ? $" · +{_terrainCourseDetourPercent:0}%"
                : string.Empty;
            var avoided = _terrainCourseAvoidedZoneCount > 0
                ? $" · {_terrainCourseAvoidedZoneCount} obstacle{(_terrainCourseAvoidedZoneCount == 1 ? string.Empty : "s")} avoided"
                : string.Empty;
            var water = _terrainWaterSafetyEnabled && _terrainWaterMaskStatus == "ready"
                ? _terrainCourseAvoidedWater
                    ? " · direct water shortcut avoided"
                    : " · water-safe connectors"
                : " · water safety off";
            var terrainDanger = _terrainCommunityHazardStatus == "ready"
                                && _terrainCommunityHazardsEnabled
                ? $" · {_terrainCommunityHazardCount} public terrain danger point{(_terrainCommunityHazardCount == 1 ? string.Empty : "s")} enforced"
                : _terrainCommunityHazardStatus == "ready"
                    ? " · public terrain danger off"
                    : " · public terrain danger unavailable";
            var learned = _terrainCourseLearnedDistance > 0.5
                ? $" · {_terrainCourseLearnedDistance:0} MU player-traveled"
                : _learnedPassageRoutingEnabled && _learnedPassageActiveCount > 0
                    ? " · learned passages available"
                    : " · learned passages unused";
            TerrainCourseStatusText.Text = _terrainCourseStatus == "rerouting"
                ? $"RECALCULATING · {gapPolicy.Label} gaps ≤{gapPolicy.MaximumConnectorDistance:0} MU"
                : $"{routeStyle.Label} COURSE · {_terrainCourseDistance:0} MU{direct}{detour}{avoided}{water}{terrainDanger}{learned} · gaps ≤{gapPolicy.MaximumConnectorDistance:0} MU";
            TerrainCourseStatusText.Foreground = _terrainCourseStatus == "rerouting"
                ? (Brush)FindResource("WarningBrush")
                : (Brush)FindResource("AccentBrush");
            return;
        }

        var failureText = _terrainCourseStatus switch
        {
            "waiting-position" => "Waiting for your authorized live position",
            "choose-destination" => "Choose any destination, then plot its road/trail course",
            "inside-danger-zone" => "Move outside the saved Danger zone before plotting",
            "destination-inside-danger-zone" => "The destination is inside a saved Danger zone",
            "inside-community-terrain-hazard" => "Move outside the marked public terrain-danger area before plotting",
            "destination-inside-community-terrain-hazard" => "The destination is inside a marked public terrain-danger area",
            "inside-no-go-area" => "You are inside the highlighted no-go area · move outside it first",
            "destination-inside-no-go-area" => "The destination is inside the highlighted no-go area",
            "no-road-near-player" => $"No mapped path within {gapPolicy.MaximumConnectorDistance:0} MU of you · relax gap limit or move closer",
            "no-road-near-destination" => $"No mapped path within {gapPolicy.MaximumConnectorDistance:0} MU of that point · relax gap limit or choose another",
            "no-connected-road-course" => "No connected road/trail course is available for those points",
            "start-in-water" => "Move onto land before plotting a water-safe road/trail course",
            "destination-in-water" => "That destination is inside the current drinkable-water mask",
            "course-too-complex" => "A safe course needs more than 12 bends · add a closer destination or trace a tighter No-Go area",
            _ => string.Empty
        };
        if (!string.IsNullOrEmpty(failureText))
        {
            TerrainCourseStatusText.Text = failureText;
            TerrainCourseStatusText.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        var version = string.IsNullOrWhiteSpace(_terrainNetworkSourceVersion)
            ? "LIVE"
            : $"V{_terrainNetworkSourceVersion.ToUpperInvariant()}";
        var waterState = !_terrainWaterSafetyEnabled
            ? "water safety off"
            : _terrainWaterMaskStatus switch
            {
                "ready" => $"water mask {(_terrainWaterMaskSourceVersion.Length > 0 ? _terrainWaterMaskSourceVersion : "live")} ready",
                "loading" => "water mask syncing",
                _ => "water mask unavailable"
            };
        var communityHazardState = _terrainCommunityHazardStatus == "ready"
            ? _terrainCommunityHazardsEnabled
                ? $"{_terrainCommunityHazardCount} public terrain danger point{(_terrainCommunityHazardCount == 1 ? string.Empty : "s")} enforced"
                : "public terrain danger off"
            : "public terrain danger unavailable";
        var learnedState = !_learnedPassageRoutingEnabled
            ? "learned passages off"
            : _learnedPassageActiveCount > 0
                ? $"{_learnedPassageActiveCount} current player-traveled passage{(_learnedPassageActiveCount == 1 ? string.Empty : "s")}"
                : _learnedPassageStaleCount > 0
                    ? "saved passages held after source/age change"
                    : "no player-traveled passages";
        TerrainCourseStatusText.Text = hasUsableDestination
            ? $"READY · {routeStyle.Label} · gaps ≤{gapPolicy.MaximumConnectorDistance:0} MU · {_terrainNetworkPathCount} mapped paths / {_terrainNetworkPointCount} points · {learnedState} + saved/traced obstacles + {communityHazardState} + {waterState} · {version}"
            : $"Choose a destination · {routeStyle.Label} · gaps ≤{gapPolicy.MaximumConnectorDistance:0} MU · {_terrainNetworkPathCount} mapped paths · {learnedState} + Danger zones + {_noGoAreaCount} traced No-Go area{(_noGoAreaCount == 1 ? string.Empty : "s")} + {communityHazardState} · {waterState}";
        TerrainCourseStatusText.ToolTip = _terrainNetworkLoadedAt is null
            ? "Community roads/trails are used as routing evidence; this is not a terrain guarantee"
            : $"Loaded {_terrainNetworkLoadedAt.Value.ToLocalTime():g} from My Isle Map. " +
              "The water mask is sourced from current game-file data; enabled public terrain danger points come from Vulnona. " +
              "Learned passages store bounded local map geometry only and are held after their source version or 90-day freshness window changes. " +
              "These sources improve decision support but still do not provide complete elevation or traversability guarantees.";
    }

    private void UpdateMeasurementStatus()
    {
        var crossingView = _waterCrossingCheckActive ? CurrentWaterCrossingView() : default;
        var measurementAccent = _waterCrossingCheckActive
            ? WaterCrossingAccent(crossingView.State)
            : new SolidColorBrush(Color.FromRgb(103, 232, 249));
        RulerStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
        MeasurementHeadingText.Text = _waterCrossingCheckActive ? "WATER CROSSING" : "MAP RULER";
        MeasurementHeadingText.Foreground = measurementAccent;
        MeasurementDetailText.Foreground = (Brush)FindResource("SecondaryTextBrush");
        MeasurementDetailText.ToolTip = null;
        MeasurementPanel.BorderBrush = _waterCrossingCheckActive
            ? measurementAccent
            : new SolidColorBrush(Color.FromArgb(0xAA, 0x22, 0xD3, 0xEE));
        if (_streamerMode)
        {
            MeasurementPanel.Visibility = Visibility.Collapsed;
            MeasureButton.Content = "Measure distance";
            MeasureButton.IsEnabled = false;
            CopyMeasurementButton.IsEnabled = false;
            RulerStatusText.Text = "Map measurements hidden in streamer mode";
            SetToggleButtonState(MeasureButton, false);
            return;
        }

        MeasureButton.IsEnabled = true;
        CopyMeasurementButton.IsEnabled = false;
        SetToggleButtonState(MeasureButton, _measurementArmed || _measurementActive);

        if (_measurementArmed)
        {
            MeasurementPanel.Visibility = HudSurfaceLogic.Show(_navigationHudVisible, _streamerMode)
                ? Visibility.Visible
                : Visibility.Collapsed;
            MeasurementValueText.Text = _measurementHasStart ? "SELECT END" : "SELECT START";
            MeasurementDetailText.Text = _measurementHasStart ? "CLICK SECOND POINT" : "CLICK ANY POINT";
            MeasureButton.Content = "Cancel measurement";
            RulerStatusText.Text = _measurementHasStart
                ? "Start point set · select the endpoint"
                : "Select the first point directly on the map";
            return;
        }

        if (!_measurementActive)
        {
            MeasurementPanel.Visibility = Visibility.Collapsed;
            MeasureButton.Content = "Measure distance";
            RulerStatusText.Text = "Measure between any two map points";
            return;
        }

        MeasurementPanel.Visibility = HudSurfaceLogic.Show(_navigationHudVisible, _streamerMode)
            ? Visibility.Visible
            : Visibility.Collapsed;
        MeasureButton.Content = "Clear measurement";
        MeasurementValueText.Text = _measurementDistance is not null && _measurementBearing is not null
            ? $"{_measurementDistance:0.0} MU · {_measurementCardinal} {_measurementBearing:000}°"
            : "MEASUREMENT READY";
        var endpointsAvailable = _measurementStartWorldX is not null
                                 && _measurementStartWorldY is not null
                                 && _measurementEndWorldX is not null
                                 && _measurementEndWorldY is not null;
        MeasurementDetailText.Text = _waterCrossingCheckActive
            ? $"{crossingView.HudLabel} · VERIFY IN GAME"
            : endpointsAvailable ? "A TO B · COORDINATES READY" : "A TO B";
        if (_waterCrossingCheckActive)
        {
            MeasurementDetailText.Foreground = measurementAccent;
            MeasurementDetailText.ToolTip = crossingView.Detail;
        }
        RulerStatusText.Text = _measurementDistance is not null
            ? $"{_measurementDistance:0.0} MU · {_measurementCardinal} {_measurementBearing:000}°"
            : "Measurement ready";
        CopyMeasurementButton.IsEnabled = endpointsAvailable;
    }

    private static string FormatElapsedAge(double ageMs)
    {
        var age = TimeSpan.FromMilliseconds(Math.Max(0, ageMs));
        if (age.TotalDays >= 1) return $"{Math.Floor(age.TotalDays):0}d";
        if (age.TotalHours >= 1) return $"{Math.Floor(age.TotalHours):0}h";
        if (age.TotalMinutes >= 1) return $"{Math.Floor(age.TotalMinutes):0}m";
        return $"{Math.Floor(age.TotalSeconds):0}s";
    }

    private void UpdatePinControls()
    {
        var formattedType = FormatPinType(_pinType);
        PinStatusText.Text = _clearPinsConfirmationPending
            ? "Select again within 5 seconds to remove every saved marker"
            : _pinArmed
                ? $"Click the map to place a {formattedType.ToLowerInvariant()} marker"
                : _pinCount == 0
                    ? $"No saved markers · {formattedType} selected"
                    : $"{_pinCount} saved marker{(_pinCount == 1 ? string.Empty : "s")} · {formattedType} selected";
        PinStatusText.Foreground = _pinArmed || _clearPinsConfirmationPending
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("SecondaryTextBrush");
        PlacePinButton.Content = _pinArmed
            ? "Cancel placing marker"
            : $"Place {formattedType.ToLowerInvariant()} marker on map";
        DropPinButton.Content = $"Mark my position as {formattedType.ToLowerInvariant()}";
        PlacePinButton.IsEnabled = !_streamerMode;
        DropPinButton.IsEnabled = !_streamerMode && _currentSelfX is not null && _currentSelfY is not null;
        var destinationText = DestinationInputBox?.Text?.Trim() ?? string.Empty;
        var hasDestination = !string.IsNullOrWhiteSpace(destinationText);
        var isSharedRoute = Regex.IsMatch(destinationText, @"(?:->|>|;)");
        RouteDestinationButton.Content = isSharedRoute ? "Start shared route" : "Route to destination";
        RouteDestinationButton.ToolTip = isSharedRoute
            ? "Start an ordered route with every entered stop"
            : "Route to the entered place, grid cell, or coordinates";
        SaveDestinationPinButton.Content = isSharedRoute
            ? "Choose one stop to save as a pin"
            : $"Save destination as {formattedType.ToLowerInvariant()}";
        SaveDestinationPinButton.ToolTip = isSharedRoute
            ? "A saved marker represents one location; enter a single stop to save it"
            : "Save the entered destination using the selected marker type";
        if (PasteDestinationCoordinatesButton is not null)
        {
            PasteDestinationCoordinatesButton.IsEnabled =
                !_streamerMode && LiveMapServicesActive && _followControllerInstalled;
        }
        RouteDestinationButton.IsEnabled = !_streamerMode && hasDestination;
        SaveDestinationPinButton.IsEnabled = !_streamerMode && hasDestination && !isSharedRoute;
        ClearPinsButton.IsEnabled = !_streamerMode && _pinCount > 0;
        ClearPinsButton.Content = _clearPinsConfirmationPending
            ? "Confirm clear all markers"
            : "Clear all saved markers";
        SetToggleButtonState(SafePinTypeButton, _pinType == "safe");
        SetToggleButtonState(NestPinTypeButton, _pinType == "nest");
        SetToggleButtonState(FoodPinTypeButton, _pinType == "food");
        SetToggleButtonState(DangerPinTypeButton, _pinType == "danger");
        SetToggleButtonState(WaterPinTypeButton, _pinType == "water");
        SetToggleButtonState(RallyPinTypeButton, _pinType == "rally");
        SetToggleButtonState(DeathPinTypeButton, _pinType == "death");
        SetToggleButtonState(PlacePinButton, _pinArmed);
    }

    private void UpdateRecentRoutes()
    {
        var signature = string.Join('|', new[]
        {
            _streamerMode ? "private" : "visible",
            _canRouteBack ? "back" : "start",
            string.Join(';', _recentRoutes.Select(route =>
                $"{route.Id}:{route.Label}:{route.GridReference}:{route.Active}"))
        });
        if (string.Equals(signature, _recentRoutesUiSignature, StringComparison.Ordinal))
        {
            return;
        }

        _recentRoutesUiSignature = signature;
        RecentDestinationsPanel.Children.Clear();
        PreviousDestinationButton.IsEnabled = !_streamerMode && _canRouteBack;
        PreviousDestinationButton.Content = "Back to previous destination";
        PreviousDestinationButton.ToolTip = _canRouteBack && _recentRoutes.Count > 1
            ? $"Route back to {_recentRoutes[1].Label}"
            : "Choose two destinations this session to enable quick backtracking";

        if (_streamerMode)
        {
            RecentDestinationsStatus.Text = "Recent destinations hidden in streamer mode";
            RecentDestinationsStatus.Foreground = (Brush)FindResource("SecondaryTextBrush");
            RecentDestinationsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        RecentDestinationsPanel.Visibility = Visibility.Visible;
        RecentDestinationsStatus.Foreground = (Brush)FindResource("SecondaryTextBrush");
        if (_recentRoutes.Count == 0)
        {
            RecentDestinationsStatus.Text = "No session routes yet · nothing is saved";
            return;
        }

        RecentDestinationsStatus.Text = $"{_recentRoutes.Count} recent destination" +
                                        $"{(_recentRoutes.Count == 1 ? string.Empty : "s")} · session only";
        foreach (var route in _recentRoutes.Take(6))
        {
            var displayLabel = route.Label.Length <= 21 ? route.Label : $"{route.Label[..20]}…";
            var detail = string.IsNullOrWhiteSpace(route.GridReference) ? "SESSION" : $"GRID {route.GridReference}";
            var button = new Button
            {
                Style = (Style)FindResource("DrawerButton"),
                Height = 30,
                Margin = new Thickness(0, 0, 0, 4),
                FontSize = 9,
                Content = $"{(route.Active ? "NOW  " : string.Empty)}{displayLabel} · {detail}",
                Tag = route.Id,
                ToolTip = route.Active
                    ? $"Clear the active route to {route.Label}"
                    : $"Route to {route.Label}" +
                      (string.IsNullOrWhiteSpace(route.GridReference) ? string.Empty : $" · Grid {route.GridReference}")
            };
            button.Click += RecentDestinationButton_Click;
            SetToggleButtonState(button, route.Active);
            RecentDestinationsPanel.Children.Add(button);
        }
    }

    private void UpdatePinLibrary()
    {
        var signature = string.Join('|', new[]
        {
            _streamerMode ? "private" : "visible",
            _activePinId,
            _pinRemovalConfirmationId,
            _noGoAreaCount.ToString(CultureInfo.InvariantCulture),
            string.Join(';', _pinRoster.Select(pin =>
                $"{pin.Id}:{pin.Distance?.ToString("0.0", CultureInfo.InvariantCulture) ?? "-"}:" +
                $"{pin.Bearing?.ToString("0", CultureInfo.InvariantCulture) ?? "-"}:" +
                $"{pin.Favorite}:{FormatPinExpiry(pin.ExpiresInMs)}:{pin.AlertRadius}:" +
                $"{pin.InsideAlertZone}:{pin.Label}"))
        });
        if (string.Equals(signature, _pinRosterUiSignature, StringComparison.Ordinal))
        {
            return;
        }

        _pinRosterUiSignature = signature;
        PinLibraryPanel.Children.Clear();
        if (_streamerMode)
        {
            PinLibraryStatus.Text = "Destination library hidden in streamer mode";
            PinLibraryStatus.Foreground = (Brush)FindResource("SecondaryTextBrush");
            PinLibraryPanel.Visibility = Visibility.Collapsed;
            SelectedPinEditorPanel.Visibility = Visibility.Collapsed;
            FavoriteSelectedPinButton.IsEnabled = false;
            PinExpiryButton.IsEnabled = false;
            PinAlertRadiusButton.IsEnabled = false;
            RenameSelectedPinButton.IsEnabled = false;
            CopySelectedPinButton.IsEnabled = false;
            CopyPinLibraryButton.IsEnabled = false;
            ImportPinLibraryButton.IsEnabled = false;
            RemoveSelectedPinButton.IsEnabled = false;
            _pendingPinImportText = string.Empty;
            _pinImportConfirmationRevision++;
            ImportPinLibraryButton.Content = "PASTE BACKUP";
            PinBackupStatusText.Text = "Backups are hidden in streamer mode";
            return;
        }

        PinLibraryPanel.Visibility = Visibility.Visible;
        var selectedPin = _pinRoster.FirstOrDefault(pin =>
            string.Equals(pin.Id, _activePinId, StringComparison.Ordinal));
        CopyPinLibraryButton.IsEnabled = _pinRoster.Count > 0 || _noGoAreaCount > 0;
        ImportPinLibraryButton.IsEnabled = true;
        if (string.IsNullOrWhiteSpace(_pendingPinImportText))
        {
            ImportPinLibraryButton.Content = "PASTE BACKUP";
            if (string.Equals(
                    PinBackupStatusText.Text, "Backups are hidden in streamer mode", StringComparison.Ordinal))
            {
                PinBackupStatusText.Text = "Copy this local library or restore a validated Isley backup.";
                PinBackupStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            }
        }
        SelectedPinEditorPanel.Visibility = selectedPin is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (selectedPin is null)
        {
            _pinNameEditingId = string.Empty;
            SetPinNameInput(string.Empty);
        }
        else if (!string.Equals(_pinNameEditingId, selectedPin.Id, StringComparison.Ordinal)
                 || (!PinNameInputBox.IsKeyboardFocusWithin
                     && !string.Equals(PinNameInputBox.Text, selectedPin.Label, StringComparison.Ordinal)))
        {
            _pinNameEditingId = selectedPin.Id;
            SetPinNameInput(selectedPin.Label);
        }
        UpdatePinNameControls();
        FavoriteSelectedPinButton.IsEnabled = selectedPin is not null;
        FavoriteSelectedPinButton.Content = selectedPin?.Favorite == true ? "FAVORITED" : "FAVORITE";
        SetToggleButtonState(FavoriteSelectedPinButton, selectedPin?.Favorite == true);
        PinExpiryButton.IsEnabled = selectedPin is not null;
        var selectedExpiry = FormatPinExpiry(selectedPin?.ExpiresInMs);
        PinExpiryButton.Content = string.IsNullOrWhiteSpace(selectedExpiry)
            ? "EXPIRES: NEVER"
            : $"EXPIRES IN {selectedExpiry}";
        SetToggleButtonState(PinExpiryButton, !string.IsNullOrWhiteSpace(selectedExpiry));
        PinAlertRadiusButton.IsEnabled = selectedPin is not null;
        PinAlertRadiusButton.Content = selectedPin?.AlertRadius > 0
            ? $"ALERT ZONE: {selectedPin.AlertRadius} MU"
            : "ALERT ZONE: OFF";
        PinAlertRadiusButton.ToolTip = selectedPin is null
            ? "Select a saved destination to configure a local proximity zone"
            : selectedPin.AlertRadius > 0
                ? $"Warn once when entering the {selectedPin.AlertRadius} MU zone around {selectedPin.Label}; select to resize"
                : $"Create a local proximity zone around {selectedPin.Label}";
        SetToggleButtonState(PinAlertRadiusButton, selectedPin?.AlertRadius > 0);
        CopySelectedPinButton.IsEnabled = selectedPin?.WorldX is not null && selectedPin.WorldY is not null;
        RemoveSelectedPinButton.IsEnabled = selectedPin is not null;
        CopySelectedPinButton.Content = "COPY COORDS";
        RemoveSelectedPinButton.Content = !string.IsNullOrWhiteSpace(_pinRemovalConfirmationId)
            ? "Confirm remove selected marker"
            : "Remove selected marker";

        if (!string.IsNullOrWhiteSpace(_pinRemovalConfirmationId))
        {
            PinLibraryStatus.Text = "Select remove again within 5 seconds";
            PinLibraryStatus.Foreground = (Brush)FindResource("WarningBrush");
        }
        else if (_pinRoster.Count == 0)
        {
            PinLibraryStatus.Text = "No saved destinations yet";
            PinLibraryStatus.Foreground = (Brush)FindResource("SecondaryTextBrush");
            return;
        }
        else
        {
            var favoriteCount = _pinRoster.Count(pin => pin.Favorite);
            var alertZoneCount = _pinRoster.Count(pin => pin.AlertRadius > 0);
            PinLibraryStatus.Text = $"{_pinRoster.Count} saved destination{(_pinRoster.Count == 1 ? string.Empty : "s")}" +
                                    (favoriteCount > 0 ? $" · {favoriteCount} favorite{(favoriteCount == 1 ? string.Empty : "s")} first" : " · newest first") +
                                    (alertZoneCount > 0 ? $" · {alertZoneCount} alert zone{(alertZoneCount == 1 ? string.Empty : "s")}" : string.Empty);
            PinLibraryStatus.Foreground = (Brush)FindResource("SecondaryTextBrush");
        }

        const int visibleLimit = 10;
        foreach (var pin in _pinRoster.OrderByDescending(pin => pin.Favorite).Take(visibleLimit))
        {
            var typeLabel = FormatPinType(pin.Type).ToUpperInvariant();
            var cleanLabel = string.Equals(pin.Label, FormatPinType(pin.Type), StringComparison.OrdinalIgnoreCase)
                ? "Saved marker"
                : pin.Label;
            var displayLabel = cleanLabel.Length <= 18 ? cleanLabel : $"{cleanLabel[..17]}…";
            var expiryDetail = FormatPinExpiry(pin.ExpiresInMs);
            var navigationDetail = pin.Distance is not null && pin.Bearing is not null
                ? $" · {pin.Distance:0.0} MU {pin.Cardinal}"
                : " · SAVED";
            if (!string.IsNullOrWhiteSpace(expiryDetail))
            {
                navigationDetail += $" · {expiryDetail}";
            }
            if (pin.AlertRadius > 0)
            {
                navigationDetail += $" · ZONE {pin.AlertRadius}";
            }
            var routeButton = new Button
            {
                Style = (Style)FindResource("DrawerButton"),
                Height = 31,
                Margin = new Thickness(0, 0, 0, 4),
                FontSize = 9,
                BorderThickness = new Thickness(3, 1, 1, 1),
                Content = $"{(pin.Favorite ? "★  " : string.Empty)}{typeLabel}  {displayLabel}{navigationDetail}",
                Tag = pin.Id,
                ToolTip = BuildPinToolTip(pin)
            };
            routeButton.Click += PinLibraryButton_Click;
            var active = string.Equals(pin.Id, _activePinId, StringComparison.Ordinal);
            SetToggleButtonState(routeButton, active);
            if (!active)
            {
                routeButton.BorderBrush = PinTypeBrush(pin.Type);
            }
            PinLibraryPanel.Children.Add(routeButton);
        }

        if (_pinRoster.Count > visibleLimit)
        {
            PinLibraryStatus.Text += $" · showing {visibleLimit}";
        }
    }

    private void UpdateNoGoAreaControls()
    {
        var signature = string.Join('|',
            _streamerMode,
            _noGoAreaCount,
            _noGoTraceActive,
            _noGoTraceVertexCount,
            _noGoSelectedAreaId,
            _noGoSelectedAreaLabel,
            _noGoSelectedAreaVertexCount,
            _noGoLastStatus,
            _noGoAreaRemovalConfirmationId,
            string.Join(';', _noGoAreaRoster.Select(area =>
                $"{area.Id}:{area.Label}:{area.VertexCount}")));
        if (string.Equals(signature, _noGoAreaUiSignature, StringComparison.Ordinal)) return;
        _noGoAreaUiSignature = signature;

        if (_streamerMode)
        {
            NoGoAreaStatusText.Text = "Terrain notes hidden in streamer mode";
            NoGoAreaStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            BeginNoGoTraceButton.IsEnabled = false;
            UndoNoGoPointButton.IsEnabled = false;
            FinishNoGoTraceButton.IsEnabled = false;
            CancelNoGoTraceButton.IsEnabled = false;
            SelectedNoGoAreaPanel.Visibility = Visibility.Collapsed;
            return;
        }

        BeginNoGoTraceButton.IsEnabled = !_noGoTraceActive
                                           && _noGoAreaCount < NoGoAreaLogic.MaximumAreaCount;
        BeginNoGoTraceButton.Content = _noGoAreaCount >= NoGoAreaLogic.MaximumAreaCount
            ? "8 AREA LIMIT REACHED"
            : _noGoTraceActive
                ? $"TRACING · {_noGoTraceVertexCount}/{NoGoAreaLogic.MaximumVertexCount} POINTS"
                : "TRACE AREA ON MAP";
        UndoNoGoPointButton.IsEnabled = _noGoTraceActive && _noGoTraceVertexCount > 0;
        FinishNoGoTraceButton.IsEnabled = _noGoTraceActive
                                          && _noGoTraceVertexCount >= NoGoAreaLogic.MinimumVertexCount;
        CancelNoGoTraceButton.IsEnabled = _noGoTraceActive;
        SetToggleButtonState(BeginNoGoTraceButton, _noGoTraceActive);

        var status = _noGoLastStatus switch
        {
            "click-map-boundary" => "TRACING · click around the obstacle boundary",
            "add-2-more-points" => "POINT 1 SAVED · add at least 2 more",
            "add-1-more-point" => "POINT 2 SAVED · add at least 1 more",
            "ready-to-finish" => $"{_noGoTraceVertexCount} POINTS · select Finish to close the boundary",
            "maximum-12-points" => "12 POINT LIMIT · finish or undo a point",
            "move-farther-for-next-point" => "That point is too close to the last one",
            "add-at-least-3-points" => "Add at least 3 boundary points",
            "boundary-lines-cross" => "Boundary lines cross · undo and retrace",
            "trace-a-larger-area" => "Boundary is too small · trace a larger area",
            "invalid-boundary" => "Boundary could not be validated",
            "area-saved" => "AREA SAVED · road/trail courses now avoid it",
            "blocked-passage-saved" => "BLOCKED PASSAGE SAVED · active course is replanning",
            "measured-slope-saved" => "MEASURED SLOPE SAVED · route avoidance active",
            "area-removed" => "Area removed · active terrain course recalculated",
            "area-restored" => "AREA RESTORED · active terrain course recalculated",
            "areas-imported" => "SHARED AREAS IMPORTED · routes go around them",
            "trace-cancelled" => "Unfinished boundary discarded",
            "maximum-8-areas" => "8 AREA LIMIT · remove one before tracing another",
            "save-failed" => "Area is visible now but local saving failed",
            "storage-reset" => "Invalid saved terrain notes were safely reset",
            _ when _noGoTraceActive => $"TRACING · {_noGoTraceVertexCount} point" +
                                      (_noGoTraceVertexCount == 1 ? string.Empty : "s"),
            _ when _noGoAreaCount == 0 => "No blocked areas yet · use TRACE AREA ON MAP",
            _ => $"{_noGoAreaCount} blocked area" + (_noGoAreaCount == 1 ? string.Empty : "s") +
                 " · routes go around them"
        };
        NoGoAreaStatusText.Text = status;
        NoGoAreaStatusText.Foreground = _noGoLastStatus is "boundary-lines-cross" or
            "trace-a-larger-area" or "invalid-boundary" or "save-failed" or "maximum-8-areas"
            ? (Brush)FindResource("WarningBrush")
            : _noGoLastStatus is "area-saved" or "blocked-passage-saved" or "measured-slope-saved"
                or "area-restored" or "areas-imported"
                ? (Brush)FindResource("SuccessBrush")
                : (Brush)FindResource("SecondaryTextBrush");

        var hasSelection = !string.IsNullOrWhiteSpace(_noGoSelectedAreaId)
                           && _noGoAreaRoster.Any(area =>
                               string.Equals(area.Id, _noGoSelectedAreaId, StringComparison.Ordinal));
        SelectedNoGoAreaPanel.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        if (!hasSelection) return;
        SelectedNoGoAreaText.Text = $"SELECTED · {_noGoSelectedAreaLabel} · " +
                                    $"{_noGoSelectedAreaVertexCount} points";
        PreviousNoGoAreaButton.IsEnabled = _noGoAreaCount > 1;
        NextNoGoAreaButton.IsEnabled = _noGoAreaCount > 1;
        RemoveNoGoAreaButton.IsEnabled = true;
        RemoveNoGoAreaButton.Content = string.Equals(
            _noGoAreaRemovalConfirmationId, _noGoSelectedAreaId, StringComparison.Ordinal)
            ? "CONFIRM"
            : "REMOVE";
    }

    private void SetPinNameInput(string value)
    {
        _suppressPinNameChanges = true;
        try
        {
            PinNameInputBox.Text = value;
            PinNameInputBox.CaretIndex = PinNameInputBox.Text.Length;
        }
        finally
        {
            _suppressPinNameChanges = false;
        }
    }

    private void UpdatePinNameControls()
    {
        if (RenameSelectedPinButton is null)
        {
            return;
        }

        var selectedPin = _pinRoster.FirstOrDefault(pin =>
            string.Equals(pin.Id, _activePinId, StringComparison.Ordinal));
        var requestedName = NormalizePinName(PinNameInputBox?.Text ?? string.Empty);
        RenameSelectedPinButton.IsEnabled = !_streamerMode
                                            && selectedPin is not null
                                            && !string.IsNullOrWhiteSpace(requestedName)
                                            && !string.Equals(
                                                requestedName, selectedPin.Label, StringComparison.Ordinal);
    }

    private static string NormalizePinName(string value)
    {
        var withoutControls = Regex.Replace(value ?? string.Empty, @"[\u0000-\u001F\u007F]+", " ");
        var normalized = Regex.Replace(withoutControls, @"\s+", " ").Trim();
        return normalized.Length <= 40 ? normalized : normalized[..40];
    }

    private string BuildPinToolTip(PinRouteInfo pin)
    {
        var distance = pin.Distance is not null && pin.Bearing is not null
            ? $" · {pin.Distance:0.0} map units · {pin.Cardinal} {pin.Bearing:000}°"
            : string.Empty;
        var coordinates = pin.WorldX is not null && pin.WorldY is not null
            ? $" · world {pin.WorldX:0}, {pin.WorldY:0}"
            : $" · map {pin.X:0.0}, {pin.Y:0.0}";
        var favorite = pin.Favorite ? " · favorite" : string.Empty;
        var expiry = FormatPinExpiry(pin.ExpiresInMs);
        var expiryText = string.IsNullOrWhiteSpace(expiry) ? string.Empty : $" · expires in {expiry}";
        var alertZone = pin.AlertRadius > 0 ? $" · {pin.AlertRadius} MU alert zone" : string.Empty;
        return $"Route to {pin.Label} · {FormatPinType(pin.Type)}{favorite}{expiryText}{alertZone}{distance}{coordinates}";
    }

    private static string FormatPinExpiry(double? expiresInMs)
    {
        if (expiresInMs is null || expiresInMs <= 0)
        {
            return string.Empty;
        }

        var minutes = Math.Max(1, (int)Math.Ceiling(expiresInMs.Value / 60000));
        if (minutes < 60)
        {
            return $"{minutes}M";
        }
        var hours = (int)Math.Ceiling(minutes / 60d);
        return $"{hours}H";
    }

    private Brush PinTypeBrush(string type) => type switch
    {
        "nest" => new SolidColorBrush(Color.FromRgb(167, 139, 250)),
        "food" => new SolidColorBrush(Color.FromRgb(52, 211, 153)),
        "danger" => new SolidColorBrush(Color.FromRgb(251, 146, 60)),
        "water" => new SolidColorBrush(Color.FromRgb(96, 165, 250)),
        "rally" => new SolidColorBrush(Color.FromRgb(250, 204, 21)),
        "death" => new SolidColorBrush(Color.FromRgb(248, 113, 113)),
        _ => (Brush)FindResource("AccentBrush")
    };

    private static string FormatPinType(string type) => type switch
    {
        "nest" => "Nest",
        "food" => "Food",
        "danger" => "Danger",
        "water" => "Water",
        "rally" => "Rally",
        "death" => "Death",
        _ => "Safe"
    };

    private static bool TryParseCoordinatePair(string input, out double x, out double y)
    {
        x = 0;
        y = 0;
        var matches = Regex.Matches(input, @"[-+]?(?:\d+(?:\.\d+)?|\.\d+)");
        return matches.Count == 2
               && double.TryParse(matches[0].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out x)
               && double.TryParse(matches[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out y)
               && double.IsFinite(x)
               && double.IsFinite(y);
    }

    private async Task<bool> DropDeathMarkerAsync()
    {
        var requestTick = Environment.TickCount64;
        if (requestTick - _lastDeathMarkerRequestTick < 500)
        {
            return false;
        }
        _lastDeathMarkerRequestTick = requestTick;

        if (_streamerMode)
        {
            _deathMarkerAttemptCount++;
            _lastDeathMarkerAttemptSucceeded = false;
            _deathMarkerActionStatus = "Death marking unavailable · streamer mode is on";
            _deathMarkerActionAt = DateTimeOffset.UtcNow;
            DeathMarkerButton.Content = "Death marking hidden";
            InteractionStatusText.Text = "DEATH MARKING HIDDEN IN STREAMER MODE";
            InteractionStatusText.Foreground = (Brush)FindResource("WarningBrush");
            UpdateRecoveryControls();
            await ShowHotkeyToastAsync("DEATH MARKING HIDDEN IN STREAMER MODE", false);
            UpdateHotkeyStatus();
            return false;
        }

        _deathMarkerAttemptCount++;
        DeathMarkerButton.Content = "Saving Death marker...";
        InteractionStatusText.Text = "SAVING BODY MARKER...";
        InteractionStatusText.Foreground = (Brush)FindResource("AccentBrush");
        _ = ShowHotkeyToastAsync("SAVING DEATH MARKER...", true);
        var added = await ExecuteMapperCommandAsync(
            "window.__isley?.dropDeathPin() ?? false");
        _lastDeathMarkerAttemptSucceeded = added;
        _deathMarkerActionStatus = added
            ? "Death marker saved · previous body marker replaced"
            : "Death marker unavailable · no current or remembered authorized position";
        _deathMarkerActionAt = DateTimeOffset.UtcNow;
        InteractionStatusText.Text = added
            ? "DEATH MARKER SAVED"
            : "NO AUTHORIZED POSITION AVAILABLE";
        InteractionStatusText.Foreground = added
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("WarningBrush");
        DeathMarkerButton.Content = added
            ? "Death marker saved"
            : "No authorized position";
        var firstDeathCoach = PressureCoachLogic.FirstDeath(_pressureCoachFirstDeathSeen);
        if (added)
        {
            AddTacticalEvent("RECOVERY", "Death marker saved", "Previous Death marker replaced");
            _recoveryPromptRevision++;
            _recoveryPromptPending = false;
            _recoveryPromptDismissed = true;
            HideRecoveryPrompt();
            if (firstDeathCoach.Show)
            {
                _pressureCoachFirstDeathSeen = true;
                SaveSettings();
                AddTacticalEvent("COACH", firstDeathCoach.Title, firstDeathCoach.Detail);
            }
        }
        UpdateRecoveryControls();
        await ShowHotkeyToastAsync(
            !added
                ? "NO AUTHORIZED POSITION AVAILABLE"
                : firstDeathCoach.Show
                    ? $"DEATH MARKER SAVED · {firstDeathCoach.Detail}"
                    : "DEATH MARKER SAVED",
            added);
        UpdateHotkeyStatus();
        return added;
    }

    private async Task OpenResourceFinderForQueryAsync(
        string query,
        string emptyMessage,
        string eventDetail)
    {
        if (_streamerMode || !LiveMapServicesActive)
        {
            await ShowHotkeyToastAsync("LIVE MAP RESOURCE SOURCE UNAVAILABLE", false);
            return;
        }

        if (_gatewayResourceNetwork is null)
        {
            await LoadGatewayResourceNetworkAsync();
        }

        _resourceFinderQuery = query;
        _resourceFinderResultIndex = 0;
        ResourceFinderSearchInputBox.Text = query;
        _resourceFinderUiSignature = string.Empty;
        UpdateResourceFinder(force: true);
        OpenMapToolsAtSection("resource-finder");
        if (_resourceFinderSelection is not null)
        {
            AddTacticalEvent(
                "SURVIVAL",
                "Recovery resource opened",
                $"{eventDetail} · static public Gateway site");
        }
        await ShowHotkeyToastAsync(
            _resourceFinderSelection is null
                ? emptyMessage
                : $"RESOURCE FINDER · {_resourceFinderSelection.Site.Name.ToUpperInvariant()}",
            _resourceFinderSelection is not null);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (LiveMapWebView.CoreWebView2 is null)
        {
            await InitializeLiveMapAsync();
            return;
        }

        LiveMapWebView.CoreWebView2.Reload();
    }

    private async void MapButton_Click(object sender, RoutedEventArgs e)
    {
        if (LiveMapWebView.CoreWebView2 is null)
        {
            await InitializeLiveMapAsync();
            return;
        }

        LiveMapWebView.CoreWebView2.Navigate(LocalMapUri);
    }

    private async void RecenterButton_Click(object sender, RoutedEventArgs e)
    {
        if (LiveMapServicesActive && !_markerAvailable)
        {
            if (!_universalCoordinateCaptureEnabled)
            {
                OpenMapToolsAtSection("terrain-probe");
                await ShowHotkeyToastAsync("TURN SYNC ON, THEN COPY ASSET LOCATION", true);
                return;
            }

            await ShowHotkeyToastAsync(
                _gameWasRunning
                    ? "IN THE ISLE: TAB → ASSET LOCATION"
                    : "START THE ISLE, THEN COPY ASSET LOCATION",
                true);
        }

        await RecenterAsync();
    }

    private async Task<bool> RecenterAsync()
    {
        if (LiveMapWebView.CoreWebView2 is null)
        {
            await InitializeLiveMapAsync();
            return false;
        }

        if (!_followControllerInstalled)
        {
            await InstallPlayerFollowAsync();
        }

        try
        {
            _smartZoomSuspended = false;
            var centered = await ExecuteMapperCommandAsync(
                "window.__isley?.recenter() ?? false");
            UpdateSmartFollowControls();
            return centered;
        }
        catch
        {
            UpdateFollowButton(following: true, markerAvailable: false);
            return false;
        }
    }

    private async void PlayerLabelsButton_Click(object sender, RoutedEventArgs e)
    {
        _playerLabelsVisible = !_playerLabelsVisible;
        PlayerLabelsButton.Content = _playerLabelsVisible ? "Names on" : "Names off";
        PlayerLabelsButton.ToolTip = _playerLabelsVisible
            ? "Hide player names while keeping markers visible"
            : "Show player names";
        SetToggleButtonState(PlayerLabelsButton, _playerLabelsVisible);
        SaveSettings();
        await EnsureFollowControllerAsync();
        await ApplyMapOptionsAsync();
    }

    private async void MarkerStyleButton_Click(object sender, RoutedEventArgs e)
    {
        _markerStyleIndex = (_markerStyleIndex + 1) % _markerStyleModes.Length;
        UpdateMarkerStyleControl();
        SaveSettings();
        await EnsureFollowControllerAsync();
        await ApplyMapOptionsAsync();
    }

    private async void ZoomInButton_Click(object sender, RoutedEventArgs e) =>
        await ExecuteMapperCommandAsync("window.__isley?.zoomBy(1.25) ?? false");

    private async void ZoomOutButton_Click(object sender, RoutedEventArgs e) =>
        await ExecuteMapperCommandAsync("window.__isley?.zoomBy(0.8) ?? false");

    private async void ZoomPresetButton_Click(object sender, RoutedEventArgs e)
    {
        _zoomPresetIndex = (_zoomPresetIndex + 1) % _zoomPresets.Length;
        _currentMapScale = _zoomPresets[_zoomPresetIndex];
        UpdateZoomDisplay();
        await ExecuteMapperCommandAsync(
            $"window.__isley?.setZoom({_currentMapScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}) ?? false");
    }

    private async void HeadingModeButton_Click(object sender, RoutedEventArgs e)
    {
        _headingUp = !_headingUp;
        HeadingModeButton.Content = _headingUp ? "Heading up" : "North up";
        HeadingModeButton.ToolTip = _headingUp
            ? "Heading-up mode; select for north-up"
            : "North-up mode; select for heading-up";
        SetToggleButtonState(HeadingModeButton, _headingUp);
        await EnsureFollowControllerAsync();
        await ApplyMapOptionsAsync();
    }

    private async void FollowFramingButton_Click(object sender, RoutedEventArgs e)
    {
        _lookAheadEnabled = !_lookAheadEnabled;
        UpdateSmartFollowControls();
        SaveSettings();
        await EnsureFollowControllerAsync();
        await ApplyMapOptionsAsync();
    }

    private async void SmartZoomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_smartZoomEnabled && _smartZoomSuspended)
        {
            _smartZoomSuspended = false;
            await RecenterAsync();
        }
        else
        {
            _smartZoomEnabled = !_smartZoomEnabled;
            _smartZoomSuspended = false;
            await EnsureFollowControllerAsync();
            await ApplyMapOptionsAsync();
        }

        UpdateSmartFollowControls();
        SaveSettings();
    }

    private async void PlayerFilterButton_Click(object sender, RoutedEventArgs e)
    {
        _friendOnly = !_friendOnly;
        PlayerFilterButton.Content = _friendOnly ? "Friends only" : "All authorized players";
        PlayerFilterButton.ToolTip = _friendOnly
            ? "Showing friends only; select to show all authorized players"
            : "Showing all authorized players; select for friends only";
        SetToggleButtonState(PlayerFilterButton, _friendOnly);
        SaveSettings();
        await EnsureFollowControllerAsync();
        await ApplyMapOptionsAsync();
    }

    private async void TrailLengthButton_Click(object sender, RoutedEventArgs e)
    {
        _trailDurationIndex = (_trailDurationIndex + 1) % _trailDurations.Length;
        var seconds = _trailDurations[_trailDurationIndex];
        TrailLengthButton.Content = seconds == 0 ? "Trails off" : $"Trails {seconds}s";
        TrailLengthButton.ToolTip = seconds == 0
            ? "Movement trails are off; select to cycle trail length"
            : $"Showing the last {seconds} seconds of authorized movement; select to cycle";
        SetToggleButtonState(TrailLengthButton, seconds > 0);
        SaveSettings();
        await EnsureFollowControllerAsync();
        await ApplyMapOptionsAsync();
    }

    private async void WaypointButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        if (_waypointActive || _waypointArmed)
        {
            _waypointActive = false;
            _waypointArmed = false;
            UpdateWaypointStatus(null, null, string.Empty);
            await ExecuteMapperCommandAsync("window.__isley?.clearWaypoint() ?? false");
            return;
        }

        _waypointArmed = true;
        if (_routePlanArmed)
        {
            _routePlanArmed = false;
            ClearRoutePlanValues();
            UpdateRoutePlanControls();
        }
        UpdateWaypointStatus(null, null, string.Empty);
        await ExecuteMapperCommandAsync("window.__isley?.armWaypoint() ?? false");
    }

    private async void PreviousDestinationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || !_canRouteBack)
        {
            return;
        }

        if (!await ExecuteMapperCommandAsync("window.__isley?.routeBack() ?? false"))
        {
            PreviousDestinationButton.ToolTip = "The previous session destination is no longer available";
        }
    }

    private async void RoutePlanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || _routePlanComplete)
        {
            return;
        }

        if (_routePlanActive)
        {
            await ExecuteMapperCommandAsync("window.__isley?.advanceRouteStop() ?? false");
            return;
        }

        if (_routePlanArmed)
        {
            if (_routeStopCount < 2)
            {
                return;
            }
            if (!await ExecuteMapperCommandAsync("window.__isley?.startRoutePlan() ?? false"))
            {
                RoutePlanStatusText.Text = "The planned route could not be started";
                RoutePlanStatusText.Foreground = (Brush)FindResource("WarningBrush");
            }
            return;
        }

        _routePlanArmed = true;
        _routePlanActive = false;
        _routePlanComplete = false;
        ClearRoutePlanValues();
        _waypointActive = false;
        _waypointArmed = false;
        if (_measurementArmed)
        {
            _measurementArmed = false;
            _measurementHasStart = false;
            if (!_measurementActive)
            {
                ClearMeasurementValues();
            }
            ResetWaterCrossingCheck(logEvent: true);
        }
        UpdateWaypointStatus(null, null, string.Empty);
        UpdateMeasurementStatus();
        UpdateRoutePlanControls();
        if (!await ExecuteMapperCommandAsync("window.__isley?.armRoutePlan() ?? false"))
        {
            _routePlanArmed = false;
            UpdateRoutePlanControls();
            RoutePlanStatusText.Text = "The live map is not ready for route planning";
            RoutePlanStatusText.Foreground = (Brush)FindResource("WarningBrush");
        }
    }

    private async void UndoRouteStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_routePlanArmed || _routeStopCount == 0 || _streamerMode)
        {
            return;
        }
        await ExecuteMapperCommandAsync("window.__isley?.undoRouteStop() ?? false");
    }

    private async void SkipRouteStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_routePlanActive || _streamerMode)
        {
            return;
        }
        await ExecuteMapperCommandAsync("window.__isley?.advanceRouteStop() ?? false");
    }

    private async void ClearRoutePlanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || !(_routePlanArmed || _routePlanActive || _routePlanComplete || _routeStopCount > 0))
        {
            return;
        }
        await ExecuteMapperCommandAsync("window.__isley?.clearRoutePlan() ?? false");
    }

    private async void PasteRoutePlanButton_Click(object sender, RoutedEventArgs e) =>
        await PasteSharedRouteFromClipboardAsync();

    private async Task PasteSharedRouteFromClipboardAsync()
    {
        if (_streamerMode)
        {
            await ShowHotkeyToastAsync("ROUTE PASTE HIDDEN IN STREAMER MODE", false);
            return;
        }

        if (!LiveMapServicesActive || !_followControllerInstalled)
        {
            RoutePlanStatusText.Text = "Live Map mode required to paste a route";
            RoutePlanStatusText.Foreground = (Brush)FindResource("WarningBrush");
            await ShowHotkeyToastAsync("LIVE MAP NOT READY", false);
            return;
        }

        string clipboardText;
        try
        {
            if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                RoutePlanStatusText.Text = "Clipboard has no shared route text";
                RoutePlanStatusText.Foreground = (Brush)FindResource("WarningBrush");
                await ShowHotkeyToastAsync("NO ROUTE TEXT TO PASTE", false);
                return;
            }

            clipboardText = Clipboard.GetText(TextDataFormat.UnicodeText);
        }
        catch
        {
            RoutePlanStatusText.Text = "Clipboard is temporarily unavailable";
            RoutePlanStatusText.Foreground = (Brush)FindResource("WarningBrush");
            await ShowHotkeyToastAsync("CLIPBOARD UNAVAILABLE", false);
            return;
        }

        if (string.IsNullOrWhiteSpace(clipboardText)
            || clipboardText.Length > MaximumSharedRouteClipboardCharacters)
        {
            RoutePlanStatusText.Text = "Shared routes must contain 2–12 stops and fit the safe paste limit";
            RoutePlanStatusText.Foreground = (Brush)FindResource("WarningBrush");
            await ShowHotkeyToastAsync("INVALID SHARED ROUTE", false);
            return;
        }

        // Single Asset Location / X,Y clipboard paste plots a one-destination route.
        if (UniversalCoordinateLogic.TryParseDestinationWorldPoint(clipboardText, out _, out _)
            || TryParseCoordinatePair(clipboardText.Trim(), out _, out _))
        {
            await RouteClipboardCoordinatesAsync(openSection: false);
            return;
        }

        var routed = await ExecuteMapperCommandAsync(
            $"window.__isley?.startSharedRouteText({JsonSerializer.Serialize(clipboardText)}) ?? false");
        clipboardText = string.Empty;
        if (!routed)
        {
            RoutePlanStatusText.Text = "Route not recognized · use 2–12 valid grid cells, coordinates, pins, or places";
            RoutePlanStatusText.Foreground = (Brush)FindResource("WarningBrush");
            await ShowHotkeyToastAsync("ROUTE COULD NOT BE RESOLVED", false);
            return;
        }

        RoutePlanStatusText.Text = "Shared route accepted · syncing stops";
        RoutePlanStatusText.Foreground = (Brush)FindResource("AccentBrush");
        AddTacticalEvent("ROUTE", "Shared route started", "Validated one-shot clipboard handoff");
        await ShowHotkeyToastAsync("SHARED ROUTE ACTIVE", true);
    }

    private async void TerrainCourseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || !_terrainNetworkReady || !_markerAvailable)
        {
            return;
        }

        TerrainCourseButton.IsEnabled = false;
        TerrainCourseStatusText.Text = "CALCULATING · mapped roads/trails plus saved and traced obstacles";
        TerrainCourseStatusText.Foreground = (Brush)FindResource("AccentBrush");
        if (!await ExecuteMapperCommandAsync("window.__isley?.startTerrainCourse() ?? false"))
        {
            TerrainCourseStatusText.Text = "A connected road/trail course is not available for this destination";
            TerrainCourseStatusText.Foreground = (Brush)FindResource("WarningBrush");
        }
    }

    private void TerrainCourseSourceButton_Click(object sender, RoutedEventArgs e) =>
        OpenExternalUri(TerrainRoadNetworkClient.AttributionPage);

    private async void TerrainRouteStyleButton_Click(object sender, RoutedEventArgs e) =>
        await CycleTerrainRouteStyleAsync(showToast: true);

    private async void TerrainGapPolicyButton_Click(object sender, RoutedEventArgs e) =>
        await CycleTerrainGapPolicyAsync(showToast: true);

    private async void TerrainRouteConfidenceButton_Click(object sender, RoutedEventArgs e)
    {
        _terrainRouteConfidenceVisible = !_terrainRouteConfidenceVisible;
        UpdateTerrainCourseControls();
        SaveSettings();
        await ExecuteMapperCommandAsync(
            $"window.__isley?.setTerrainRouteEvidenceVisible(" +
            $"{(_terrainRouteConfidenceVisible ? "true" : "false")}) ?? false");
    }

    private async void TerrainBlockedPassageButton_Click(object sender, RoutedEventArgs e) =>
        await ReportBlockedTerrainPassageAsync(showToast: true);

    private async Task<bool> ReportBlockedTerrainPassageAsync(bool showToast)
    {
        if (_streamerMode || !_routePlanActive
            || !string.Equals(_routePlanSource, "terrain", StringComparison.Ordinal)
            || !_markerAvailable || LiveMapWebView.CoreWebView2 is null)
        {
            if (showToast) await ShowHotkeyToastAsync("PLOT AN ACTIVE COURSE FIRST", false);
            return false;
        }

        TerrainBlockedPassageButton.IsEnabled = false;
        TerrainCourseStatusText.Text = "BLOCKING PASSAGE · saving local obstacle and replanning";
        TerrainCourseStatusText.Foreground = (Brush)FindResource("WarningBrush");
        var reported = await ExecuteMapperCommandAsync(
            "window.__isley?.reportBlockedTerrainPassage()?.ok === true");
        if (!reported)
        {
            UpdateTerrainCourseControls();
            if (showToast)
            {
                await ShowHotkeyToastAsync(
                    _noGoAreaCount >= NoGoAreaLogic.MaximumAreaCount
                        ? "NO-GO LIMIT REACHED · REMOVE ONE FIRST"
                        : "PASSAGE REPORT NEEDS MORE COURSE AHEAD",
                    false);
            }
            return false;
        }

        AddTacticalEvent(
            "ROUTE",
            "Blocked passage reported",
            "Local reversible obstacle saved · terrain course replanning");
        if (showToast)
        {
            await ShowHotkeyToastAsync("BLOCKED PASSAGE SAVED · REPLANNING", true);
        }
        return true;
    }

    private async Task<bool> CycleTerrainRouteStyleAsync(bool showToast)
    {
        if (_streamerMode || !_terrainNetworkReady || LiveMapWebView.CoreWebView2 is null)
        {
            return false;
        }

        var requested = TerrainRouteStyleLogic.Next(_terrainRouteStyle);
        var requestedJson = JsonSerializer.Serialize(requested);
        TerrainRouteStyleButton.IsEnabled = false;
        TerrainCourseStatusText.Text = $"RECALCULATING · {TerrainRouteStyleLogic.Resolve(requested).Label}";
        TerrainCourseStatusText.Foreground = (Brush)FindResource("AccentBrush");
        var applied = await ExecuteMapperCommandAsync(
            $"window.__isley?.setTerrainRouteStyle({requestedJson}) === {requestedJson}");
        if (!applied)
        {
            if (showToast)
            {
                await ShowHotkeyToastAsync("ROUTE STYLE CHANGE FAILED", false);
            }
            UpdateTerrainCourseControls();
            return false;
        }

        _terrainRouteStyle = requested;
        SaveSettings();
        UpdateTerrainCourseControls();
        if (showToast)
        {
            await ShowHotkeyToastAsync(
                $"ROUTE STYLE · {TerrainRouteStyleLogic.Resolve(requested).Label}",
                true);
        }
        return true;
    }

    private async Task<bool> CycleTerrainGapPolicyAsync(bool showToast)
    {
        if (_streamerMode || !_terrainNetworkReady || LiveMapWebView.CoreWebView2 is null)
        {
            return false;
        }

        var requested = TerrainGapPolicyLogic.Next(_terrainGapPolicy);
        var option = TerrainGapPolicyLogic.Resolve(requested);
        var requestedJson = JsonSerializer.Serialize(requested);
        TerrainGapPolicyButton.IsEnabled = false;
        TerrainCourseStatusText.Text =
            $"RECALCULATING · {option.Label} gaps ≤{option.MaximumConnectorDistance:0} MU";
        TerrainCourseStatusText.Foreground = (Brush)FindResource("AccentBrush");
        var applied = await ExecuteMapperCommandAsync(
            $"window.__isley?.setTerrainGapPolicy({requestedJson}) === {requestedJson}");
        if (!applied)
        {
            if (showToast)
            {
                await ShowHotkeyToastAsync("OFF-NETWORK GAP CHANGE FAILED", false);
            }
            UpdateTerrainCourseControls();
            return false;
        }

        _terrainGapPolicy = requested;
        SaveSettings();
        UpdateTerrainCourseControls();
        if (showToast)
        {
            await ShowHotkeyToastAsync(
                $"OFF-NETWORK GAPS · {option.Label.ToUpperInvariant()} ≤{option.MaximumConnectorDistance:0} MU",
                true);
        }
        return true;
    }

    private async void TerrainWaterSafetyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || LiveMapWebView.CoreWebView2 is null)
        {
            return;
        }

        var requested = !_terrainWaterSafetyEnabled;
        TerrainWaterSafetyButton.IsEnabled = false;
        var applied = await ExecuteMapperCommandAsync(
            $"window.__isley?.setTerrainWaterSafety({requested.ToString().ToLowerInvariant()}) === {requested.ToString().ToLowerInvariant()}");
        if (!applied)
        {
            await ShowHotkeyToastAsync("WATER SAFETY CHANGE FAILED", false);
            UpdateTerrainCourseControls();
            return;
        }
        _terrainWaterSafetyEnabled = requested;
        UpdateTerrainCourseControls();
        await ShowHotkeyToastAsync(
            requested ? "WATER-SAFE CONNECTORS ON" : "WATER-SAFE CONNECTORS OFF",
            true);
    }

    private async void TerrainCommunityHazardsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode
            || _terrainCommunityHazardStatus != "ready"
            || LiveMapWebView.CoreWebView2 is null)
        {
            return;
        }

        var requested = !_terrainCommunityHazardsEnabled;
        TerrainCommunityHazardsButton.IsEnabled = false;
        var requestedJson = requested.ToString().ToLowerInvariant();
        var applied = await ExecuteMapperCommandAsync(
            $"window.__isley?.setTerrainCommunityHazardsEnabled({requestedJson}) === {requestedJson}");
        if (!applied)
        {
            await ShowHotkeyToastAsync("TERRAIN DANGER CHANGE FAILED", false);
            UpdateTerrainCourseControls();
            return;
        }

        _terrainCommunityHazardsEnabled = requested;
        UpdateTerrainCourseControls();
        await ShowHotkeyToastAsync(
            requested
                ? $"TERRAIN DANGER ON · {_terrainCommunityHazardCount} CURRENT POINT{(_terrainCommunityHazardCount == 1 ? string.Empty : "S")}"
                : "TERRAIN DANGER OFF",
            true);
    }

    private bool TryBuildCurrentSharedRoute(out VoiceSharedRoute sharedRoute)
    {
        var stops = _routeStops
            .OrderBy(stop => stop.Index)
            .Where(stop => stop.WorldX is not null && stop.WorldY is not null)
            .ToList();
        if (stops.Count < 2 || stops.Count != _routeStopCount)
        {
            sharedRoute = default;
            return false;
        }

        var route = string.Join(" > ", stops.Select(stop =>
            $"{stop.WorldX!.Value.ToString("0.##", CultureInfo.InvariantCulture)}, " +
            stop.WorldY!.Value.ToString("0.##", CultureInfo.InvariantCulture)));
        var total = _routePlanTotalDistance is not null
            ? $" | {_routePlanTotalDistance.Value.ToString("0.0", CultureInfo.InvariantCulture)} MU planned"
            : string.Empty;
        var routeKind = string.Equals(_routePlanSource, "breadcrumb", StringComparison.Ordinal)
            ? "breadcrumb return"
            : string.Equals(_routePlanSource, "terrain", StringComparison.Ordinal)
                ? "road/trail course"
                : "route";
        return VoiceRouteOfferLogic.TryParseRoute(
            $"Isley {routeKind} | {route}{total}",
            out sharedRoute,
            out _);
    }

    private async void CopyRoutePlanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || !TryBuildCurrentSharedRoute(out var sharedRoute))
        {
            return;
        }

        try
        {
            Clipboard.SetText(sharedRoute.Text);
            CopyRoutePlanButton.Content = "COPIED";
        }
        catch
        {
            CopyRoutePlanButton.Content = "FAILED";
        }

        await Task.Delay(1400);
        if (IsLoaded)
        {
            CopyRoutePlanButton.Content = "COPY";
        }
    }

    private async void MeasureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        if (_measurementActive || _measurementArmed)
        {
            await ClearMeasurementAsync();
            return;
        }

        await ArmFreshMeasurementAsync();
    }

    private async Task<bool> ArmFreshMeasurementAsync()
    {
        if (_streamerMode)
        {
            return false;
        }
        _measurementArmed = true;
        _measurementHasStart = false;
        _measurementActive = false;
        _measurementMarkedBoundaryCount = 0;
        _measurementInsideMarkedBoundary = false;
        _waypointArmed = false;
        if (_routePlanArmed)
        {
            _routePlanArmed = false;
            ClearRoutePlanValues();
            UpdateRoutePlanControls();
        }
        ClearMeasurementValues();
        UpdateWaypointStatus(null, null, string.Empty);
        UpdateMeasurementStatus();
        _waterCrossingUiSignature = string.Empty;
        UpdateWaterCrossingCheck(force: true);
        var accepted = await ExecuteMapperCommandAsync(
            "window.__isley?.armMeasurement() ?? false");
        if (!accepted)
        {
            _measurementArmed = false;
            UpdateMeasurementStatus();
            RulerStatusText.Text = "The live map is not ready for measurement";
            RulerStatusText.Foreground = (Brush)FindResource("WarningBrush");
            _waterCrossingUiSignature = string.Empty;
            UpdateWaterCrossingCheck(force: true);
        }
        return accepted;
    }

    private async Task ClearMeasurementAsync()
    {
        _measurementArmed = false;
        _measurementHasStart = false;
        _measurementActive = false;
        _measurementMarkedBoundaryCount = 0;
        _measurementInsideMarkedBoundary = false;
        ClearMeasurementValues();
        UpdateMeasurementStatus();
        _waterCrossingUiSignature = string.Empty;
        UpdateWaterCrossingCheck(force: true);
        await ExecuteMapperCommandAsync("window.__isley?.clearMeasurement() ?? false");
    }

    private async void CopyMeasurementButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode
            || !_measurementActive
            || _measurementDistance is null
            || _measurementBearing is null
            || _measurementStartWorldX is null
            || _measurementStartWorldY is null
            || _measurementEndWorldX is null
            || _measurementEndWorldY is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(
                $"A: {_measurementStartWorldX.Value.ToString("0.##", CultureInfo.InvariantCulture)}, " +
                $"{_measurementStartWorldY.Value.ToString("0.##", CultureInfo.InvariantCulture)} | " +
                $"B: {_measurementEndWorldX.Value.ToString("0.##", CultureInfo.InvariantCulture)}, " +
                $"{_measurementEndWorldY.Value.ToString("0.##", CultureInfo.InvariantCulture)} | " +
                $"{_measurementDistance.Value.ToString("0.0", CultureInfo.InvariantCulture)} MU " +
                $"{_measurementCardinal} {_measurementBearing.Value.ToString("000", CultureInfo.InvariantCulture)}°");
            CopyMeasurementButton.Content = "Measurement copied";
        }
        catch
        {
            CopyMeasurementButton.Content = "Clipboard unavailable";
        }

        await Task.Delay(1400);
        if (IsLoaded)
        {
            CopyMeasurementButton.Content = "Copy measurement";
        }
    }

    private async void RouteSessionStartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        var active = _waypointActive
                     && string.Equals(_waypointLabel, "Session start", StringComparison.Ordinal);
        var command = active
            ? "window.__isley?.clearWaypoint() ?? false"
            : "window.__isley?.routeToSessionStart() ?? false";
        if (!await ExecuteMapperCommandAsync(command))
        {
            RecoveryStatusText.Text = "Session start is not available yet";
            RecoveryStatusText.Foreground = (Brush)FindResource("WarningBrush");
        }
    }

    private async void BreadcrumbReturnButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        var breadcrumbRouteActive = string.Equals(_routePlanSource, "breadcrumb", StringComparison.Ordinal)
                                    && (_routePlanActive || _routePlanComplete);
        var command = breadcrumbRouteActive
            ? "window.__isley?.clearRoutePlan() ?? false"
            : "window.__isley?.startBreadcrumbReturn() ?? false";
        if (!await ExecuteMapperCommandAsync(command))
        {
            RecoveryStatusText.Text = "Move farther before starting a breadcrumb return";
            RecoveryStatusText.Foreground = (Brush)FindResource("WarningBrush");
        }
    }

    private async void RouteLastPositionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        var active = _waypointActive
                     && string.Equals(_waypointLabel, "Last live position", StringComparison.Ordinal);
        var command = active
            ? "window.__isley?.clearWaypoint() ?? false"
            : "window.__isley?.routeToLastPosition() ?? false";
        if (!await ExecuteMapperCommandAsync(command))
        {
            RecoveryStatusText.Text = "No stored last live position is available";
            RecoveryStatusText.Foreground = (Brush)FindResource("WarningBrush");
        }
    }

    private async void DeathMarkerButton_Click(object sender, RoutedEventArgs e)
    {
        await DropDeathMarkerAsync();
    }

    private void DeathMarkerButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _ = DropDeathMarkerAsync();
    }

    private async void ArrivalAlertButton_Click(object sender, RoutedEventArgs e)
    {
        _arrivalAlertIndex = (_arrivalAlertIndex + 1) % _arrivalAlertDistances.Length;
        _arrivalAlertTriggered = false;
        UpdateRecoveryControls();
        UpdateRoutePlanControls();
        SaveSettings();
        await EnsureFollowControllerAsync();
        await ApplyMapOptionsAsync();
    }

    private void DangerAlertButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        _dangerAlertIndex = (_dangerAlertIndex + 1) % _dangerAlertDistances.Length;
        _dangerAlertKey = string.Empty;
        UpdateDangerProximity();
        SaveSettings();
    }

    private void NearestPlaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        _nearestPlaceVisible = !_nearestPlaceVisible;
        UpdateNearestPlaceContext();
        UpdateHudSurfaceControls();
        SaveSettings();
    }

    private async void RouteNearestPlaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        var routeActive = _waypointActive
                          && _waypointLabel.StartsWith("Nearest place · ", StringComparison.Ordinal);
        var command = routeActive
            ? "window.__isley?.clearWaypoint() ?? false"
            : "window.__isley?.routeToNearestPlace() ?? false";
        if (!await ExecuteMapperCommandAsync(command))
        {
            RouteNearestPlaceButton.ToolTip = "No routable bundled map place is currently available";
        }
    }

    private async void PinTypeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string type })
        {
            return;
        }

        _pinType = type;
        UpdatePinControls();
        await ExecuteMapperCommandAsync(
            $"window.__isley?.setPinType('{type}') ?? false");
    }

    private async void BeginNoGoTraceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || _noGoTraceActive) return;
        var label = NormalizePinName(NoGoAreaNameInputBox.Text);
        if (string.IsNullOrWhiteSpace(label)) label = $"No-go area {_noGoAreaCount + 1}";
        var started = await ExecuteMapperCommandAsync(
            $"window.__isley?.beginNoGoTrace({JsonSerializer.Serialize(label)}) ?? false");
        await ShowHotkeyToastAsync(
            started ? "TRACE THE BOUNDARY ON THE MAP" : "NO-GO TRACE UNAVAILABLE",
            started);
    }

    private async void UndoNoGoPointButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_noGoTraceActive || _noGoTraceVertexCount <= 0) return;
        await ExecuteMapperCommandAsync("window.__isley?.undoNoGoTracePoint() ?? false");
    }

    private async void FinishNoGoTraceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_noGoTraceActive) return;
        var finished = await ExecuteMapperCommandAsync("window.__isley?.finishNoGoTrace() ?? false");
        await ShowHotkeyToastAsync(
            finished ? "NO-GO AREA SAVED · COURSE AVOIDANCE ACTIVE" : "FIX THE BOUNDARY BEFORE FINISHING",
            finished);
    }

    private async void CancelNoGoTraceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_noGoTraceActive) return;
        await ExecuteMapperCommandAsync("window.__isley?.cancelNoGoTrace() ?? false");
    }

    private async void PreviousNoGoAreaButton_Click(object sender, RoutedEventArgs e)
    {
        if (_noGoAreaCount <= 1) return;
        await ExecuteMapperCommandAsync("window.__isley?.cycleNoGoArea(-1) ?? false");
    }

    private async void NextNoGoAreaButton_Click(object sender, RoutedEventArgs e)
    {
        if (_noGoAreaCount <= 1) return;
        await ExecuteMapperCommandAsync("window.__isley?.cycleNoGoArea(1) ?? false");
    }

    private async void RemoveNoGoAreaButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_noGoSelectedAreaId) || _streamerMode) return;
        var id = _noGoSelectedAreaId;
        if (!string.Equals(_noGoAreaRemovalConfirmationId, id, StringComparison.Ordinal))
        {
            _noGoAreaRemovalConfirmationId = id;
            var revision = ++_noGoAreaRemovalConfirmationRevision;
            _noGoAreaUiSignature = string.Empty;
            UpdateNoGoAreaControls();
            await Task.Delay(5000);
            if (revision == _noGoAreaRemovalConfirmationRevision
                && string.Equals(_noGoAreaRemovalConfirmationId, id, StringComparison.Ordinal))
            {
                _noGoAreaRemovalConfirmationId = string.Empty;
                _noGoAreaUiSignature = string.Empty;
                UpdateNoGoAreaControls();
            }
            return;
        }

        _noGoAreaRemovalConfirmationId = string.Empty;
        _noGoAreaRemovalConfirmationRevision++;
        var removed = await ExecuteMapperCommandAsync(
            $"window.__isley?.removeNoGoArea({JsonSerializer.Serialize(id)}) ?? false");
        await ShowHotkeyToastAsync(removed ? "NO-GO AREA REMOVED" : "AREA WAS NOT REMOVED", removed);
    }

    private async void PlacePinButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        var command = _pinArmed
            ? "window.__isley?.cancelPin() ?? false"
            : $"window.__isley?.armPin('{_pinType}') ?? false";
        var accepted = await ExecuteMapperCommandAsync(command);
        if (!accepted)
        {
            PinStatusText.Text = "The live map is not ready for marker placement";
        }
    }

    private async void DropPinButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        var added = await ExecuteMapperCommandAsync(
            $"window.__isley?.dropPinAtSelf('{_pinType}') ?? false");
        if (!added)
        {
            PinStatusText.Text = "Your live position is unavailable";
        }
    }

    private async void DestinationInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || RouteDestinationButton is null)
        {
            return;
        }

        UpdatePinControls();
        var revision = ++_destinationSearchRevision;
        ClearPlaceSuggestions();
        if (_suppressDestinationSuggestions || _streamerMode)
        {
            return;
        }

        var query = DestinationInputBox.Text.Trim();
        if (query.Length < 2 || IsDirectDestinationInput(query))
        {
            return;
        }

        PlaceSearchStatusText.Text = "Searching map and saved destinations...";
        PlaceSearchStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
        PlaceSearchStatusText.Visibility = Visibility.Visible;
        await Task.Delay(170);
        if (revision != _destinationSearchRevision || _streamerMode)
        {
            return;
        }

        await EnsureFollowControllerAsync();
        if (revision != _destinationSearchRevision || LiveMapWebView.CoreWebView2 is null
            || !_followControllerInstalled)
        {
            return;
        }

        try
        {
            var encodedQuery = JsonSerializer.Serialize(query);
            var result = await LiveMapWebView.CoreWebView2.ExecuteScriptAsync(
                $"JSON.stringify(window.__isley?.searchDestinations({encodedQuery}, 5) ?? [])");
            var json = JsonSerializer.Deserialize<string>(result) ?? "[]";
            var suggestions = JsonSerializer.Deserialize<List<PlaceSearchSuggestion>>(
                json, MapperJsonOptions) ?? [];
            if (revision != _destinationSearchRevision || _streamerMode)
            {
                return;
            }

            ShowPlaceSuggestions(suggestions);
        }
        catch
        {
            if (revision != _destinationSearchRevision)
            {
                return;
            }

            ClearPlaceSuggestions();
            PlaceSearchStatusText.Text = "Destination search is waiting for the live map";
            PlaceSearchStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            PlaceSearchStatusText.Visibility = Visibility.Visible;
        }
    }

    private async void DestinationInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _destinationSearchRevision++;
            ClearPlaceSuggestions();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && PlaceSuggestionsPanel.Children.Count > 0)
        {
            if (PlaceSuggestionsPanel.Children[0] is Button firstSuggestion)
            {
                firstSuggestion.Focus();
            }
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        ApplyBestPlaceSuggestion();
        await RouteDestinationInputAsync();
    }

    private void ShowPlaceSuggestions(IEnumerable<PlaceSearchSuggestion> suggestions)
    {
        _placeSuggestions.Clear();
        _placeSuggestions.AddRange(suggestions.Where(suggestion =>
            !string.IsNullOrWhiteSpace(suggestion.Label)));
        PlaceSuggestionsPanel.Children.Clear();
        if (_placeSuggestions.Count == 0)
        {
            PlaceSuggestionsPanel.Visibility = Visibility.Collapsed;
            PlaceSearchStatusText.Text = "No destination match";
            PlaceSearchStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            PlaceSearchStatusText.Visibility = Visibility.Visible;
            return;
        }

        PlaceSearchStatusText.Text = _placeSuggestions.Count == 1
            ? "1 destination - Enter to route"
            : $"{_placeSuggestions.Count} destinations - Enter routes the best match";
        PlaceSearchStatusText.Foreground = (Brush)FindResource("AccentBrush");
        PlaceSearchStatusText.Visibility = Visibility.Visible;
        foreach (var suggestion in _placeSuggestions)
        {
            var context = suggestion.Distance is not null
                ? $"{suggestion.Distance.Value:0.0} MU {suggestion.Cardinal}".TrimEnd()
                : suggestion.GridReference;
            var label = string.IsNullOrWhiteSpace(context)
                ? suggestion.Label
                : $"{suggestion.Label}  ·  {context}";
            if (string.Equals(suggestion.Kind, "pin", StringComparison.OrdinalIgnoreCase))
            {
                label = $"{(suggestion.Favorite ? "★ " : string.Empty)}MY PIN  {label}";
                var expiry = FormatPinExpiry(suggestion.ExpiresInMs);
                if (!string.IsNullOrWhiteSpace(expiry)) label += $"  ·  {expiry}";
            }
            var button = new Button
            {
                Style = (Style)FindResource("DrawerButton"),
                Height = 30,
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(9, 0, 9, 0),
                Tag = suggestion.Label,
                ToolTip = string.Equals(suggestion.Kind, "pin", StringComparison.OrdinalIgnoreCase)
                    ? $"Route to saved destination {suggestion.Label}" +
                      (string.IsNullOrWhiteSpace(FormatPinExpiry(suggestion.ExpiresInMs))
                          ? string.Empty
                          : $" · expires in {FormatPinExpiry(suggestion.ExpiresInMs)}")
                    : string.IsNullOrWhiteSpace(suggestion.GridReference)
                        ? $"Use {suggestion.Label}"
                        : $"Use {suggestion.Label} in {suggestion.GridReference}",
                Content = new TextBlock
                {
                    Text = label,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            button.Click += PlaceSuggestionButton_Click;
            PlaceSuggestionsPanel.Children.Add(button);
        }

        PlaceSuggestionsPanel.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (PlaceSuggestionsPanel.Children.Count > 0
                && PlaceSuggestionsPanel.Children[0] is FrameworkElement firstSuggestion)
            {
                firstSuggestion.BringIntoView();
            }
        });
    }

    private void ClearPlaceSuggestions()
    {
        _placeSuggestions.Clear();
        if (PlaceSuggestionsPanel is not null)
        {
            PlaceSuggestionsPanel.Children.Clear();
            PlaceSuggestionsPanel.Visibility = Visibility.Collapsed;
        }
        if (PlaceSearchStatusText is not null)
        {
            PlaceSearchStatusText.Visibility = Visibility.Collapsed;
        }
    }

    private void PlaceSuggestionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string label })
        {
            return;
        }

        SetDestinationInput(label);
        DestinationInputBox.Focus();
    }

    private void ApplyBestPlaceSuggestion()
    {
        var input = DestinationInputBox.Text.Trim();
        if (_placeSuggestions.Count > 0 && !IsDirectDestinationInput(input))
        {
            SetDestinationInput(_placeSuggestions[0].Label);
        }
    }

    private void SetDestinationInput(string value)
    {
        _suppressDestinationSuggestions = true;
        try
        {
            DestinationInputBox.Text = value;
            DestinationInputBox.CaretIndex = DestinationInputBox.Text.Length;
        }
        finally
        {
            _suppressDestinationSuggestions = false;
        }
        _destinationSearchRevision++;
        ClearPlaceSuggestions();
        UpdatePinControls();
    }

    private static bool IsDirectDestinationInput(string input) =>
        Regex.IsMatch(input, @"(?:->|>|;)")
        || Regex.IsMatch(input, @"^(?:grid\s*)?[a-j](?:10|[1-9])$", RegexOptions.IgnoreCase)
        || UniversalCoordinateLogic.TryParseDestinationWorldPoint(input, out _, out _)
        || TryParseCoordinatePair(input, out _, out _);

    private async void RouteDestinationButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyBestPlaceSuggestion();
        await RouteDestinationInputAsync();
    }

    private async void PasteDestinationCoordinatesButton_Click(object sender, RoutedEventArgs e) =>
        await RouteClipboardCoordinatesAsync(openSection: true);

    private async Task RouteClipboardCoordinatesAsync(bool openSection)
    {
        if (_streamerMode)
        {
            await ShowHotkeyToastAsync("COORD ROUTE HIDDEN IN STREAMER MODE", false);
            return;
        }

        if (!LiveMapServicesActive || !_followControllerInstalled)
        {
            if (PinStatusText is not null)
            {
                PinStatusText.Text = "Live Map mode required to route to coordinates";
                PinStatusText.Foreground = (Brush)FindResource("WarningBrush");
            }
            await ShowHotkeyToastAsync("LIVE MAP NOT READY", false);
            return;
        }

        string clipboardText;
        try
        {
            if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                await ShowHotkeyToastAsync("NO COORDINATES TO PASTE", false);
                return;
            }

            clipboardText = Clipboard.GetText(TextDataFormat.UnicodeText);
        }
        catch
        {
            await ShowHotkeyToastAsync("CLIPBOARD UNAVAILABLE", false);
            return;
        }

        if (!UniversalCoordinateLogic.TryParseDestinationWorldPoint(clipboardText, out var worldX, out var worldY)
            && !TryParseCoordinatePair(clipboardText.Trim(), out worldX, out worldY))
        {
            await ShowHotkeyToastAsync("CLIPBOARD IS NOT COORDINATES", false);
            return;
        }

        if (openSection)
        {
            OpenMapToolsAtSection("pins");
        }

        if (DestinationInputBox is not null)
        {
            DestinationInputBox.Text = clipboardText.Trim();
            DestinationInputBox.CaretIndex = DestinationInputBox.Text.Length;
        }

        var routed = await ExecuteMapperCommandAsync(
            $"window.__isley?.routeToWorldCoordinates(" +
            $"{worldX.ToString("R", CultureInfo.InvariantCulture)}," +
            $"{worldY.ToString("R", CultureInfo.InvariantCulture)}) ?? false");
        if (!routed)
        {
            if (PinStatusText is not null)
            {
                PinStatusText.Text = "Coordinates could not be plotted · check the values are on Gateway";
                PinStatusText.Foreground = (Brush)FindResource("WarningBrush");
            }
            await ShowHotkeyToastAsync("COORDINATES NOT ON MAP", false);
            return;
        }

        _destinationSearchRevision++;
        ClearPlaceSuggestions();
        if (PinStatusText is not null)
        {
            PinStatusText.Text = "Routed to pasted coordinates";
            PinStatusText.Foreground = (Brush)FindResource("AccentBrush");
        }
        await ShowHotkeyToastAsync("ROUTED TO COORDINATES", true);
    }

    private async Task RouteDestinationInputAsync()
    {
        var input = DestinationInputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(input) || _streamerMode)
        {
            return;
        }

        string command;
        var isSharedRoute = Regex.IsMatch(input, @"(?:->|>|;)");
        if (!isSharedRoute
            && (UniversalCoordinateLogic.TryParseDestinationWorldPoint(input, out var worldX, out var worldY)
                || TryParseCoordinatePair(input, out worldX, out worldY)))
        {
            command = $"window.__isley?.routeToWorldCoordinates(" +
                      $"{worldX.ToString("R", CultureInfo.InvariantCulture)}," +
                      $"{worldY.ToString("R", CultureInfo.InvariantCulture)}) ?? false";
        }
        else
        {
            command = $"window.__isley?.routeToNamedPlace({JsonSerializer.Serialize(input)}) ?? false";
        }

        var routed = await ExecuteMapperCommandAsync(command);
        if (!routed)
        {
            PinStatusText.Text = "Not found · check every place, grid cell, coordinate, or route stop";
            PinStatusText.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        _destinationSearchRevision++;
        ClearPlaceSuggestions();
    }

    private async void SaveDestinationPinButton_Click(object sender, RoutedEventArgs e)
    {
        var input = DestinationInputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(input) || _streamerMode)
        {
            return;
        }

        string command;
        if (UniversalCoordinateLogic.TryParseDestinationWorldPoint(input, out var worldX, out var worldY)
            || TryParseCoordinatePair(input, out worldX, out worldY))
        {
            command = $"window.__isley?.saveWorldCoordinatePin(" +
                      $"{worldX.ToString("R", CultureInfo.InvariantCulture)}," +
                      $"{worldY.ToString("R", CultureInfo.InvariantCulture)}," +
                      $"'{_pinType}') ?? false";
        }
        else
        {
            command = $"window.__isley?.saveNamedPlacePin(" +
                      $"{JsonSerializer.Serialize(input)},'{_pinType}') ?? false";
        }

        var saved = await ExecuteMapperCommandAsync(command);
        if (!saved)
        {
            PinStatusText.Text = "Destination could not be saved · check the place, grid cell, or coordinates";
            PinStatusText.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        _destinationSearchRevision++;
        ClearPlaceSuggestions();
    }

    private async void PinLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || sender is not Button { Tag: string pinId })
        {
            return;
        }

        _pinRemovalConfirmationId = string.Empty;
        var command = string.Equals(pinId, _activePinId, StringComparison.Ordinal)
            ? "window.__isley?.clearWaypoint() ?? false"
            : $"window.__isley?.routeToPin({JsonSerializer.Serialize(pinId)}) ?? false";
        var routed = await ExecuteMapperCommandAsync(command);
        if (!routed)
        {
            PinLibraryStatus.Text = "That saved destination is no longer available";
            PinLibraryStatus.Foreground = (Brush)FindResource("WarningBrush");
        }
    }

    private async void RecentDestinationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || sender is not Button { Tag: string routeId })
        {
            return;
        }

        var recentRoute = _recentRoutes.FirstOrDefault(route =>
            string.Equals(route.Id, routeId, StringComparison.Ordinal));
        if (recentRoute is null)
        {
            return;
        }

        var command = recentRoute.Active
            ? "window.__isley?.clearWaypoint() ?? false"
            : $"window.__isley?.routeToRecentDestination({JsonSerializer.Serialize(routeId)}) ?? false";
        if (!await ExecuteMapperCommandAsync(command))
        {
            RecentDestinationsStatus.Text = "That recent destination is no longer available";
            RecentDestinationsStatus.Foreground = (Brush)FindResource("WarningBrush");
        }
    }

    private void PinNameInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded && !_suppressPinNameChanges)
        {
            UpdatePinNameControls();
        }
    }

    private async void PinNameInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            var selectedPin = _pinRoster.FirstOrDefault(pin =>
                string.Equals(pin.Id, _activePinId, StringComparison.Ordinal));
            if (selectedPin is not null)
            {
                SetPinNameInput(selectedPin.Label);
                UpdatePinNameControls();
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RenameSelectedPinAsync();
        }
    }

    private async void RenameSelectedPinButton_Click(object sender, RoutedEventArgs e) =>
        await RenameSelectedPinAsync();

    private async void FavoriteSelectedPinButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedPin = _pinRoster.FirstOrDefault(pin =>
            string.Equals(pin.Id, _activePinId, StringComparison.Ordinal));
        if (_streamerMode || selectedPin is null)
        {
            return;
        }

        var changed = await ExecuteMapperCommandAsync(
            $"window.__isley?.togglePinFavorite(" +
            $"{JsonSerializer.Serialize(selectedPin.Id)}) ?? false");
        if (!changed)
        {
            PinLibraryStatus.Text = "That saved destination is no longer available";
            PinLibraryStatus.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        PinLibraryStatus.Text = selectedPin.Favorite
            ? $"{selectedPin.Label} removed from favorites"
            : $"{selectedPin.Label} added to favorites";
        PinLibraryStatus.Foreground = (Brush)FindResource("SuccessBrush");
    }

    private async void PinExpiryButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedPin = _pinRoster.FirstOrDefault(pin =>
            string.Equals(pin.Id, _activePinId, StringComparison.Ordinal));
        if (_streamerMode || selectedPin is null)
        {
            return;
        }

        var changed = await ExecuteMapperCommandAsync(
            $"window.__isley?.cyclePinExpiry(" +
            $"{JsonSerializer.Serialize(selectedPin.Id)}) ?? false");
        if (!changed)
        {
            PinLibraryStatus.Text = "That saved destination is no longer available";
            PinLibraryStatus.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        PinLibraryStatus.Text = "Destination expiry updated";
        PinLibraryStatus.Foreground = (Brush)FindResource("SuccessBrush");
    }

    private async void PinAlertRadiusButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedPin = _pinRoster.FirstOrDefault(pin =>
            string.Equals(pin.Id, _activePinId, StringComparison.Ordinal));
        if (_streamerMode || selectedPin is null)
        {
            return;
        }

        var changed = await ExecuteMapperCommandAsync(
            $"window.__isley?.cyclePinAlertRadius(" +
            $"{JsonSerializer.Serialize(selectedPin.Id)}) ?? false");
        if (!changed)
        {
            PinLibraryStatus.Text = "That saved destination is no longer available";
            PinLibraryStatus.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        PinLibraryStatus.Text = selectedPin.AlertRadius switch
        {
            0 => $"Alert zone enabled for {selectedPin.Label}",
            100 => $"Alert zone disabled for {selectedPin.Label}",
            _ => $"Alert zone resized for {selectedPin.Label}"
        };
        PinLibraryStatus.Foreground = (Brush)FindResource("SuccessBrush");
    }

    private async Task RenameSelectedPinAsync()
    {
        var selectedPin = _pinRoster.FirstOrDefault(pin =>
            string.Equals(pin.Id, _activePinId, StringComparison.Ordinal));
        var requestedName = NormalizePinName(PinNameInputBox.Text);
        if (_streamerMode || selectedPin is null || string.IsNullOrWhiteSpace(requestedName)
            || string.Equals(requestedName, selectedPin.Label, StringComparison.Ordinal))
        {
            return;
        }

        var renamed = await ExecuteMapperCommandAsync(
            $"window.__isley?.renamePin(" +
            $"{JsonSerializer.Serialize(selectedPin.Id)},{JsonSerializer.Serialize(requestedName)}) ?? false");
        if (!renamed)
        {
            PinLibraryStatus.Text = "The selected destination could not be renamed";
            PinLibraryStatus.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        SetPinNameInput(requestedName);
        RenameSelectedPinButton.IsEnabled = false;
        PinLibraryStatus.Text = $"Renamed to {requestedName}";
        PinLibraryStatus.Foreground = (Brush)FindResource("SuccessBrush");
    }

    private async void CopyPinLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || _pinCount <= 0)
        {
            return;
        }

        try
        {
            await EnsureFollowControllerAsync();
            if (LiveMapWebView.CoreWebView2 is null || !_followControllerInstalled)
            {
                throw new InvalidOperationException();
            }

            var result = await LiveMapWebView.CoreWebView2.ExecuteScriptAsync(
                "window.__isley?.exportPinLibrary() ?? ''");
            var backup = JsonSerializer.Deserialize<string>(result) ?? string.Empty;
            if (backup.Length == 0 || backup.Length > 20000
                || !backup.Contains("\"schema\":\"the-isle-mapper-pins\"", StringComparison.Ordinal))
            {
                throw new InvalidDataException();
            }

            Clipboard.SetText(backup);
            CopyPinLibraryButton.Content = "BACKUP COPIED";
            var areaSummary = _noGoAreaCount > 0
                ? $" · {_noGoAreaCount} no-go area{(_noGoAreaCount == 1 ? string.Empty : "s")}"
                : string.Empty;
            PinBackupStatusText.Text = $"{_pinCount} destination{(_pinCount == 1 ? string.Empty : "s")}" +
                                       $"{areaSummary} copied";
            PinBackupStatusText.Foreground = (Brush)FindResource("SuccessBrush");
        }
        catch
        {
            CopyPinLibraryButton.Content = "COPY FAILED";
            PinBackupStatusText.Text = "The destination backup could not be copied";
            PinBackupStatusText.Foreground = (Brush)FindResource("WarningBrush");
        }

        await Task.Delay(1400);
        if (IsLoaded)
        {
            CopyPinLibraryButton.Content = "COPY BACKUP";
        }
    }

    private async void ImportPinLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_pendingPinImportText))
        {
            var pendingBackup = _pendingPinImportText;
            _pendingPinImportText = string.Empty;
            _pinImportConfirmationRevision++;
            ImportPinLibraryButton.Content = "PASTE BACKUP";
            var imported = await ExecuteMapperJsonAsync<PinImportResult>(
                $"window.__isley?.importPinLibrary({JsonSerializer.Serialize(pendingBackup)})");
            if (imported is not { Valid: true, Imported: true })
            {
                PinBackupStatusText.Text = imported?.Error is { Length: > 0 }
                    ? imported.Error
                    : "The destination backup was not imported";
                PinBackupStatusText.Foreground = (Brush)FindResource("WarningBrush");
                return;
            }

            var trimmed = imported.TrimmedCount > 0
                ? $" · {imported.TrimmedCount} oldest removed at the 20-pin limit"
                : string.Empty;
            var expired = imported.ExpiredCount > 0
                ? $" · {imported.ExpiredCount} expired skipped"
                : string.Empty;
            var importedAreas = imported.AddedAreaCount > 0
                ? $" · {imported.AddedAreaCount} no-go area{(imported.AddedAreaCount == 1 ? string.Empty : "s")}"
                : string.Empty;
            var trimmedAreas = imported.TrimmedAreaCount > 0
                ? $" · {imported.TrimmedAreaCount} area{(imported.TrimmedAreaCount == 1 ? string.Empty : "s")} over the 8-area limit skipped"
                : string.Empty;
            PinBackupStatusText.Text = $"Imported {imported.AddedCount} destination" +
                                       $"{(imported.AddedCount == 1 ? string.Empty : "s")}" +
                                       $"{importedAreas}{expired}{trimmed}{trimmedAreas}";
            PinBackupStatusText.Foreground = (Brush)FindResource("SuccessBrush");
            return;
        }

        string backup;
        try
        {
            backup = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : string.Empty;
        }
        catch
        {
            backup = string.Empty;
        }

        if (backup.Length == 0 || backup.Length > 20000)
        {
            PinBackupStatusText.Text = "Clipboard does not contain a supported Mapper backup";
            PinBackupStatusText.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        var preview = await ExecuteMapperJsonAsync<PinImportResult>(
            $"window.__isley?.previewPinLibraryImport({JsonSerializer.Serialize(backup)})");
        if (preview is not { Valid: true })
        {
            PinBackupStatusText.Text = preview?.Error is { Length: > 0 }
                ? preview.Error
                : "Clipboard does not contain a supported Mapper backup";
            PinBackupStatusText.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        var totalAdded = preview.AddedCount + preview.AddedAreaCount;
        if (totalAdded <= 0)
        {
            PinBackupStatusText.Text = preview.ExpiredCount > 0
                                       && preview.DuplicateCount == 0
                                       && preview.DuplicateAreaCount == 0
                ? "Every timed destination in this backup has expired"
                : preview.DuplicateCount > 0 || preview.DuplicateAreaCount > 0
                    ? "Every destination and no-go area in this backup is already saved"
                    : "This backup contains no destinations or no-go areas";
            PinBackupStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            return;
        }

        _pendingPinImportText = backup;
        var confirmationRevision = ++_pinImportConfirmationRevision;
        ImportPinLibraryButton.Content = $"IMPORT {totalAdded}";
        var duplicateSummary = preview.DuplicateCount > 0
            ? $" · skips {preview.DuplicateCount} duplicate{(preview.DuplicateCount == 1 ? string.Empty : "s")}"
            : string.Empty;
        var trimSummary = preview.TrimmedCount > 0
            ? $" · removes {preview.TrimmedCount} oldest at the limit"
            : string.Empty;
        var expiredSummary = preview.ExpiredCount > 0
            ? $" · skips {preview.ExpiredCount} expired"
            : string.Empty;
        var areaSummary = preview.AddedAreaCount > 0
            ? $" + {preview.AddedAreaCount} area{(preview.AddedAreaCount == 1 ? string.Empty : "s")}"
            : string.Empty;
        var duplicateAreaSummary = preview.DuplicateAreaCount > 0
            ? $" · skips {preview.DuplicateAreaCount} duplicate area{(preview.DuplicateAreaCount == 1 ? string.Empty : "s")}"
            : string.Empty;
        var trimmedAreaSummary = preview.TrimmedAreaCount > 0
            ? $" · skips {preview.TrimmedAreaCount} over area limit"
            : string.Empty;
        PinBackupStatusText.Text = $"Select Import again: adds {preview.AddedCount} destination" +
                                   $"{(preview.AddedCount == 1 ? string.Empty : "s")}{areaSummary}" +
                                   $"{duplicateSummary}{duplicateAreaSummary}{expiredSummary}" +
                                   $"{trimSummary}{trimmedAreaSummary}";
        PinBackupStatusText.Foreground = (Brush)FindResource("WarningBrush");
        await Task.Delay(5000);
        if (confirmationRevision == _pinImportConfirmationRevision
            && !string.IsNullOrWhiteSpace(_pendingPinImportText))
        {
            _pendingPinImportText = string.Empty;
            ImportPinLibraryButton.Content = "PASTE BACKUP";
            PinBackupStatusText.Text = "Import preview expired · paste again to retry";
            PinBackupStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
        }
    }

    private async void CopySelectedPinButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        var pin = _pinRoster.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, _activePinId, StringComparison.Ordinal));
        if (pin?.WorldX is null || pin.WorldY is null)
        {
            PinLibraryStatus.Text = "Shareable coordinates are unavailable for this marker";
            PinLibraryStatus.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        try
        {
            var coordinates = $"{pin.WorldX.Value.ToString("0.##", CultureInfo.InvariantCulture)}, " +
                              pin.WorldY.Value.ToString("0.##", CultureInfo.InvariantCulture);
            Clipboard.SetText(coordinates);
            CopySelectedPinButton.Content = "COPIED";
        }
        catch
        {
            CopySelectedPinButton.Content = "COPY FAILED";
        }

        await Task.Delay(1400);
        if (IsLoaded)
        {
            CopySelectedPinButton.Content = "COPY COORDS";
        }
    }

    private async void RemoveSelectedPinButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || string.IsNullOrWhiteSpace(_activePinId))
        {
            return;
        }

        var selectedId = _activePinId;
        if (!string.Equals(_pinRemovalConfirmationId, selectedId, StringComparison.Ordinal))
        {
            _pinRemovalConfirmationId = selectedId;
            UpdatePinLibrary();
            await Task.Delay(5000);
            if (string.Equals(_pinRemovalConfirmationId, selectedId, StringComparison.Ordinal))
            {
                _pinRemovalConfirmationId = string.Empty;
                UpdatePinLibrary();
            }
            return;
        }

        _pinRemovalConfirmationId = string.Empty;
        var removed = await ExecuteMapperCommandAsync(
            $"window.__isley?.removePin({JsonSerializer.Serialize(selectedId)}) ?? false");
        if (!removed)
        {
            PinLibraryStatus.Text = "The selected destination could not be removed";
            PinLibraryStatus.Foreground = (Brush)FindResource("WarningBrush");
        }
    }

    private async void ClearPinsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_clearPinsConfirmationPending)
        {
            _clearPinsConfirmationPending = true;
            UpdatePinControls();
            PinStatusText.Text = "Select again within 5 seconds to remove every saved marker";
            PinStatusText.Foreground = (Brush)FindResource("WarningBrush");
            await Task.Delay(5000);
            if (_clearPinsConfirmationPending)
            {
                _clearPinsConfirmationPending = false;
                UpdatePinControls();
            }
            return;
        }

        _clearPinsConfirmationPending = false;
        await ExecuteMapperCommandAsync(
            "window.__isley?.clearPins() ?? false");
    }

    private async void LastPositionMemoryButton_Click(object sender, RoutedEventArgs e)
    {
        _rememberLastPosition = !_rememberLastPosition;
        if (!_rememberLastPosition)
        {
            _recoveryPromptRevision++;
            _recoveryPromptPending = false;
            _recoveryPromptDismissed = true;
            _lastPositionAvailable = false;
            _lastPositionAgeMs = 0;
            HideRecoveryPrompt();
        }
        UpdateRecoveryControls();
        SaveSettings();
        await EnsureFollowControllerAsync();
        await ApplyMapOptionsAsync();
    }

    private void StaleSoundButton_Click(object sender, RoutedEventArgs e)
    {
        _staleSoundEnabled = !_staleSoundEnabled;
        StaleSoundButton.Content = _staleSoundEnabled ? "Stale alert sound on" : "Stale alert sound off";
        StaleSoundButton.ToolTip = _staleSoundEnabled
            ? "Stale-location sound alert enabled"
            : "Stale-location sound alert disabled";
        SetToggleButtonState(StaleSoundButton, _staleSoundEnabled);
    }

    private void UpdateRangeRingControl()
    {
        _rangeRingModeIndex = Math.Clamp(_rangeRingModeIndex, 0, _rangeRingModes.Length - 1);
        _rangeRingsVisible = _rangeRingModeIndex > 0;
        if (!_rangeRingsVisible)
        {
            RangeRingsButton.Content = "Range rings · Off";
            RangeRingsButton.ToolTip = "Cycle to Near 10/25 MU distance rings";
        }
        else
        {
            var mode = _rangeRingModes[_rangeRingModeIndex];
            var label = _rangeRingModeIndex switch
            {
                1 => "Near",
                2 => "Standard",
                _ => "Wide"
            };
            RangeRingsButton.Content = $"Range rings · {label} {mode.Inner}/{mode.Outer}";
            RangeRingsButton.ToolTip =
                $"{label} distance awareness: {mode.Inner} and {mode.Outer} map units; select to cycle";
        }
        SetToggleButtonState(RangeRingsButton, _rangeRingsVisible);
    }

    private async void RangeRingsButton_Click(object sender, RoutedEventArgs e)
    {
        _rangeRingModeIndex = (_rangeRingModeIndex + 1) % _rangeRingModes.Length;
        UpdateRangeRingControl();
        SaveSettings();
        await EnsureFollowControllerAsync();
        await ApplyMapOptionsAsync();
    }

    private async void BreadcrumbTrailToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        _breadcrumbTrailVisible = !_breadcrumbTrailVisible;
        _clearBreadcrumbConfirmationPending = false;
        _clearBreadcrumbConfirmationRevision++;
        UpdateBreadcrumbTrailControls();
        SaveSettings();
        await EnsureFollowControllerAsync();
        await ApplyMapOptionsAsync();
    }

    private async void ClearBreadcrumbTrailButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || _breadcrumbPointCount <= 0)
        {
            return;
        }

        if (_clearBreadcrumbConfirmationPending)
        {
            _clearBreadcrumbConfirmationPending = false;
            _clearBreadcrumbConfirmationRevision++;
            var cleared = await ExecuteMapperCommandAsync(
                "window.__isley?.clearBreadcrumbTrail() ?? false");
            if (cleared)
            {
                _breadcrumbPointCount = 0;
                _breadcrumbDistance = 0;
                _breadcrumbReturnAvailable = false;
                if (string.Equals(_routePlanSource, "breadcrumb", StringComparison.Ordinal))
                {
                    _routePlanArmed = false;
                    _routePlanActive = false;
                    _routePlanComplete = false;
                    ClearRoutePlanValues();
                }
            }
            UpdateBreadcrumbTrailControls();
            UpdateRecoveryControls();
            UpdateRoutePlanControls();
            await ShowHotkeyToastAsync(
                cleared ? "SESSION TRAIL CLEARED" : "SESSION TRAIL UNCHANGED",
                cleared);
            return;
        }

        _clearBreadcrumbConfirmationPending = true;
        var revision = ++_clearBreadcrumbConfirmationRevision;
        UpdateBreadcrumbTrailControls();
        await Task.Delay(3000);
        if (!IsLoaded
            || revision != _clearBreadcrumbConfirmationRevision
            || !_clearBreadcrumbConfirmationPending)
        {
            return;
        }

        _clearBreadcrumbConfirmationPending = false;
        UpdateBreadcrumbTrailControls();
    }

    private async void LearnedPassageRoutingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || _learnedPassageCount <= 0)
        {
            return;
        }

        var requested = !_learnedPassageRoutingEnabled;
        var applied = await ExecuteMapperCommandAsync(
            $"window.__isley?.setLearnedPassageRoutingEnabled({requested.ToString().ToLowerInvariant()}) ?? false");
        if (!applied)
        {
            return;
        }

        _learnedPassageRoutingEnabled = requested;
        _clearLearnedPassagesConfirmationPending = false;
        _clearLearnedPassagesConfirmationRevision++;
        SaveSettings();
        UpdateBreadcrumbTrailControls();
        UpdateTerrainCourseControls();
    }

    private async void LearnedPassageVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || _learnedPassageCount <= 0)
        {
            return;
        }

        var requested = !_learnedPassageVisible;
        var applied = await ExecuteMapperCommandAsync(
            $"window.__isley?.setLearnedPassageVisible({requested.ToString().ToLowerInvariant()}) ?? false");
        if (!applied)
        {
            return;
        }

        _learnedPassageVisible = requested;
        _clearLearnedPassagesConfirmationPending = false;
        _clearLearnedPassagesConfirmationRevision++;
        SaveSettings();
        UpdateBreadcrumbTrailControls();
    }

    private async void SaveLearnedPassageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode
            || !_terrainNetworkReady
            || _breadcrumbPointCount < 8
            || _breadcrumbDistance < 30)
        {
            return;
        }

        _clearLearnedPassagesConfirmationPending = false;
        _clearLearnedPassagesConfirmationRevision++;
        var saved = await ExecuteMapperCommandAsync(
            "window.__isley?.saveCurrentSessionPassage() ?? false");
        await ShowHotkeyToastAsync(
            saved ? "PLAYER-TRAVELED PASSAGE SAVED" : "PASSAGE NOT SAVED",
            saved);
    }

    private async void ClearLearnedPassagesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || _learnedPassageCount <= 0)
        {
            return;
        }

        if (_clearLearnedPassagesConfirmationPending)
        {
            _clearLearnedPassagesConfirmationPending = false;
            _clearLearnedPassagesConfirmationRevision++;
            var cleared = await ExecuteMapperCommandAsync(
                "window.__isley?.clearLearnedPassages() ?? false");
            if (cleared)
            {
                _learnedPassageCount = 0;
                _learnedPassageActiveCount = 0;
                _learnedPassageStaleCount = 0;
                _learnedPassagePointCount = 0;
            }
            UpdateBreadcrumbTrailControls();
            UpdateTerrainCourseControls();
            await ShowHotkeyToastAsync(
                cleared ? "LEARNED PASSAGES CLEARED" : "LEARNED PASSAGES UNCHANGED",
                cleared);
            return;
        }

        _clearLearnedPassagesConfirmationPending = true;
        var revision = ++_clearLearnedPassagesConfirmationRevision;
        UpdateBreadcrumbTrailControls();
        await Task.Delay(3000);
        if (!IsLoaded
            || revision != _clearLearnedPassagesConfirmationRevision
            || !_clearLearnedPassagesConfirmationPending)
        {
            return;
        }

        _clearLearnedPassagesConfirmationPending = false;
        UpdateBreadcrumbTrailControls();
    }

    private async void ExplorationToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        _explorationEnabled = !_explorationEnabled;
        _clearExplorationConfirmationPending = false;
        _clearExplorationConfirmationRevision++;
        UpdateExplorationControls();
        SaveSettings();
        await EnsureFollowControllerAsync();
        await ApplyMapOptionsAsync();
    }

    private async void ClearExplorationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || _explorationVisitedCount <= 0)
        {
            return;
        }

        if (_clearExplorationConfirmationPending)
        {
            _clearExplorationConfirmationPending = false;
            _clearExplorationConfirmationRevision++;
            var cleared = await ExecuteMapperCommandAsync(
                "window.__isley?.clearExploration() ?? false");
            if (cleared)
            {
                _explorationVisitedCount = 0;
            }
            UpdateExplorationControls();
            await ShowHotkeyToastAsync(
                cleared ? "EXPLORATION MAP CLEARED" : "EXPLORATION MAP UNCHANGED",
                cleared);
            return;
        }

        _clearExplorationConfirmationPending = true;
        var revision = ++_clearExplorationConfirmationRevision;
        UpdateExplorationControls();
        await Task.Delay(3000);
        if (!IsLoaded
            || revision != _clearExplorationConfirmationRevision
            || !_clearExplorationConfirmationPending)
        {
            return;
        }

        _clearExplorationConfirmationPending = false;
        UpdateExplorationControls();
    }

    private async void MapGridButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        _mapGridVisible = !_mapGridVisible;
        UpdateMapGridControl();
        SaveSettings();
        // Surface a rare local-write failure immediately instead of waiting for the
        // next control refresh. The map itself remains usable either way.
        UpdateMapGridControl();
        await EnsureFollowControllerAsync();
        await ApplyMapOptionsAsync();
    }

    private async void LandmarkLabelDensityButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        _landmarkLabelDensityIndex =
            (_landmarkLabelDensityIndex + 1) % _landmarkLabelDensityModes.Length;
        UpdateLandmarkLabelDensityControl();
        SaveSettings();
        await EnsureFollowControllerAsync();
        await ApplyMapOptionsAsync();
    }

    private async void FocusModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string modeId })
        {
            return;
        }

        await ApplyFocusModeAsync(modeId);
    }

    private async Task ApplyFocusModeAsync(string modeId)
    {
        if (_streamerMode)
        {
            await ShowHotkeyToastAsync("FOCUS MODES UNAVAILABLE IN STREAMER MODE", false);
            return;
        }

        if (GetFocusModeDefinition(modeId) is not { } definition)
        {
            return;
        }

        _focusModeRestoreSnapshot ??= CaptureFocusModeSnapshot();
        ApplyFocusModeDefinition(definition);
        _activeFocusModeId = modeId;
        _arrivalAlertTriggered = false;
        _dangerAlertKey = string.Empty;
        ApplyControlStates();
        SaveSettings();
        await EnsureFollowControllerAsync();
        await ApplyMapOptionsAsync();
        var layersApplied = await ApplyFocusLayerStateAsync(
            BuildFocusLayerState(definition.LayerProfile));
        UpdateFocusModeControls();
        if (!layersApplied)
        {
            FocusModeStatusText.Text = $"{definition.Label} display applied · current map layers unavailable";
            FocusModeStatusText.Foreground = (Brush)FindResource("WarningBrush");
        }
        await ShowHotkeyToastAsync(
            layersApplied
                ? $"{definition.Label.ToUpperInvariant()} FOCUS APPLIED"
                : $"{definition.Label.ToUpperInvariant()} DISPLAY APPLIED",
            true);
    }

    private async void RestoreFocusModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || _focusModeRestoreSnapshot is not { } snapshot)
        {
            return;
        }

        ApplyFocusModeSnapshot(snapshot);
        _focusModeRestoreSnapshot = null;
        _activeFocusModeId = string.Empty;
        _arrivalAlertTriggered = false;
        _dangerAlertKey = string.Empty;
        ApplyControlStates();
        SaveSettings();
        await EnsureFollowControllerAsync();
        await ApplyMapOptionsAsync();
        var layersRestored = await ApplyFocusLayerStateAsync(
            BuildFocusLayerState(snapshot));
        UpdateFocusModeControls();
        if (!layersRestored)
        {
            FocusModeStatusText.Text = "Display restored · current map layers unavailable";
            FocusModeStatusText.Foreground = (Brush)FindResource("WarningBrush");
        }
        await ShowHotkeyToastAsync(
            layersRestored ? "SAVED SETUP RESTORED" : "DISPLAY SETUP RESTORED",
            true);
    }

    private async void LayerPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string preset })
        {
            return;
        }

        LayerStatusText.Text = $"Applying {FormatLayerPreset(preset).ToLowerInvariant()} preset...";
        _activeFocusModeId = string.Empty;
        SaveSettings();
        var applied = await ExecuteMapperCommandAsync(
            $"window.__isley?.applyLayerPreset('{preset}') ?? false");
        if (!applied)
        {
            LayerStatusText.Text = "Isley provider layer controls are unavailable";
        }
    }

    private async Task ToggleOfficialLayerAsync(string key)
    {
        LayerStatusText.Text = "Updating Isley provider layer...";
        _activeFocusModeId = string.Empty;
        SaveSettings();
        var toggled = await ExecuteMapperCommandAsync(
            $"window.__isley?.toggleOfficialLayer('{key}') ?? false");
        if (!toggled)
        {
            LayerStatusText.Text = "This Isley provider layer is unavailable";
        }
    }

    private async void LocationsLayerButton_Click(object sender, RoutedEventArgs e) =>
        await ToggleOfficialLayerAsync("locations");

    private async void SanctuariesLayerButton_Click(object sender, RoutedEventArgs e) =>
        await ToggleOfficialLayerAsync("sanctuaries");

    private async void MigrationLayerButton_Click(object sender, RoutedEventArgs e) =>
        await ToggleOfficialLayerAsync("migration");

    private async void PatrolLayerButton_Click(object sender, RoutedEventArgs e) =>
        await ToggleOfficialLayerAsync("patrol");

    private async void FoodLayerButton_Click(object sender, RoutedEventArgs e) =>
        await ToggleOfficialLayerAsync("food");

    private async void HeatmapLayerButton_Click(object sender, RoutedEventArgs e) =>
        await ToggleOfficialLayerAsync("heatmap");

    private async void OfficialSelfTrailButton_Click(object sender, RoutedEventArgs e) =>
        await ToggleOfficialLayerAsync("selfTrail");

    private async void OfficialFriendTrailsButton_Click(object sender, RoutedEventArgs e) =>
        await ToggleOfficialLayerAsync("friendTrails");

    private async void CopySessionStatsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || !_sessionStatsActive)
        {
            return;
        }

        try
        {
            Clipboard.SetText(
                $"Isley session | {FormatSessionDuration(_sessionElapsedMs)} elapsed | " +
                $"{FormatSessionDuration(_sessionMovingMs)} moving | {_currentSessionDistance:0.0} MU | " +
                $"{_sessionAverageSpeed:0.0} MU/min moving average | {_sessionMaxSpeed:0.0} MU/min peak");
            CopySessionStatsButton.Content = "COPIED";
        }
        catch
        {
            CopySessionStatsButton.Content = "UNAVAILABLE";
        }

        await Task.Delay(1400);
        if (IsLoaded)
        {
            CopySessionStatsButton.Content = "COPY";
        }
    }

    private async void ResetSessionStatsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || !_sessionStatsActive)
        {
            return;
        }

        if (!await ExecuteMapperCommandAsync(
                "window.__isley?.resetActivityStats() ?? false"))
        {
            ResetSessionStatsButton.ToolTip = "The live map is not ready to reset activity statistics";
        }
    }

    private async void OpenFullMapButton_Click(object sender, RoutedEventArgs e)
    {
        if (LiveMapWebView.CoreWebView2 is null)
        {
            await InitializeLiveMapAsync();
            return;
        }
        LiveMapWebView.CoreWebView2.Navigate(LocalMapUri);
    }

    // ===== Wave-2: encounter watchlist bridge (map shell context action) =====
    // A dedicated, additive WebMessageReceived subscription so the shared map
    // bridge handler in MainWindow.WebView.cs stays untouched. Only the
    // bounded, whitelisted "isley-watch-player" message is acted on; every
    // other message type is ignored here and handled by the primary bridge.

    private bool _mapWatchlistBridgeInstalled;

    private void EnsureMapWatchlistBridgeInstalled()
    {
        if (_mapWatchlistBridgeInstalled || LiveMapWebView?.CoreWebView2 is null)
        {
            return;
        }

        _mapWatchlistBridgeInstalled = true;
        LiveMapWebView.CoreWebView2.WebMessageReceived += MapWatchlistBridge_WebMessageReceived;
    }

    private void MapWatchlistBridge_WebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? name = null;
        double? distanceMu = null;
        var cardinal = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeValue)
                || typeValue.ValueKind != JsonValueKind.String
                || !string.Equals(typeValue.GetString(), "isley-watch-player", StringComparison.Ordinal)
                || !root.TryGetProperty("kind", out var kindValue)
                || kindValue.ValueKind != JsonValueKind.String
                || !string.Equals(kindValue.GetString(), "map-player", StringComparison.Ordinal))
            {
                return;
            }

            name = root.TryGetProperty("name", out var nameValue)
                   && nameValue.ValueKind == JsonValueKind.String
                ? nameValue.GetString()
                : null;
            if (name is { Length: > 64 })
            {
                return;
            }

            if (root.TryGetProperty("distanceMu", out var distanceValue)
                && distanceValue.ValueKind == JsonValueKind.Number
                && distanceValue.TryGetDouble(out var rawDistance)
                && double.IsFinite(rawDistance)
                && rawDistance is >= 0 and <= 1_000_000)
            {
                distanceMu = rawDistance;
            }

            cardinal = root.TryGetProperty("cardinal", out var cardinalValue)
                       && cardinalValue.ValueKind == JsonValueKind.String
                ? (cardinalValue.GetString() ?? string.Empty).Trim().ToUpperInvariant()
                : string.Empty;
        }
        catch (JsonException)
        {
            return;
        }

        _ = AddEncounterWatchFromMapAsync(name, distanceMu, cardinal);
    }
}
