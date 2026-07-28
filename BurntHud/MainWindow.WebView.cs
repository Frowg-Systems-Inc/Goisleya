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
    private async Task InitializeLiveMapAsync()
    {
        if (!LiveMapServicesActive || _initializingMap || LiveMapWebView.CoreWebView2 is not null)
        {
            return;
        }

        _initializingMap = true;
        SetLoading(true, "Opening Isley's bundled map…");
        SetConnectionStatus("STARTING ISLEY MAP", Color.FromRgb(255, 178, 74));

        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userDataFolder = PortableModeEnabled
                ? Path.Combine(PortableDataDirectory, "WebView2")
                : Path.Combine(localAppData, "Isley", "WebView2");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder);
            await LiveMapWebView.EnsureCoreWebView2Async(environment);

            await ConfigureWebViewAsync();
            var mapFolder = Path.Combine(AppContext.BaseDirectory, "Map");
            var mapEntry = Path.Combine(mapFolder, "index.html");
            if (!File.Exists(mapEntry))
            {
                throw new FileNotFoundException("The Isley Live Map shell is missing.", mapEntry);
            }
            LiveMapWebView.CoreWebView2!.SetVirtualHostNameToFolderMapping(
                LocalMapHost,
                AppContext.BaseDirectory,
                CoreWebView2HostResourceAccessKind.DenyCors);
            LiveMapWebView.CoreWebView2.Navigate(LocalMapUri);
        }
        catch (Exception exception)
        {
            SetConnectionStatus("MAP UNAVAILABLE", Color.FromRgb(242, 96, 61));
            SetLoading(true, $"The live map could not start.\n{exception.Message}");
        }
        finally
        {
            _initializingMap = false;
        }
    }

    private async Task ConfigureWebViewAsync()
    {
        var webView = LiveMapWebView.CoreWebView2;
        webView.Settings.AreDevToolsEnabled = false;
        webView.Settings.AreDefaultContextMenusEnabled = false;
        webView.Settings.AreBrowserAcceleratorKeysEnabled = false;
        webView.Settings.IsStatusBarEnabled = false;
        webView.Settings.IsZoomControlEnabled = false;
        webView.Settings.IsPasswordAutosaveEnabled = false;
        webView.Settings.IsGeneralAutofillEnabled = false;

        webView.NavigationStarting += WebView_NavigationStarting;
        webView.NavigationCompleted += WebView_NavigationCompleted;
        webView.NewWindowRequested += WebView_NewWindowRequested;
        webView.ProcessFailed += WebView_ProcessFailed;
        webView.WebMessageReceived += WebView_WebMessageReceived;

        await webView.AddScriptToExecuteOnDocumentCreatedAsync(
            "window.__isleyPollControl={patched:false,targetDelayMs:500,delayMs:500,activeCallbacks:0,callbackRuns:0};");
    }

    private void WebView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!IsTrustedMapOrAuthUri(e.Uri))
        {
            e.Cancel = true;
            OpenExternalUri(e.Uri);
            return;
        }

        SetLoading(true, "Loading Isley Live Map and current Gateway layers…");
        _followControllerInstalled = false;
        _playerSnapshot = null;
        _playerSnapshotTransportState = "unavailable";
        _lastLiveDinoSample = null;
        _lastGrowthGateSample = null;
        ClearVitalsTrendSamples();
        _coreVitalsUiSignature = string.Empty;
        _waypointActive = false;
        _waypointArmed = false;
        _routePlanArmed = false;
        _routePlanActive = false;
        _routePlanComplete = false;
        _terrainNetworkReady = false;
        _terrainCourseStatus = _terrainRoadNetwork is null ? "loading" : "syncing";
        _tripRouteObstacleCount = 0;
        _tripRouteInsideObstacle = false;
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
        _currentSelfMapX = null;
        _currentSelfMapY = null;
        _activeResourceRouteId = string.Empty;
        _activeResourceRouteLabel = string.Empty;
        _resourceFinderUiSignature = string.Empty;
        _currentMarkerFreshnessAgeMs = 0;
        _currentMapScale = _zoomPresets[_zoomPresetIndex];
        UpdateFollowButton(following: true, markerAvailable: false);
        UpdateFreshnessStatus(markerAvailable: false, freshnessKnown: false, freshnessAgeMs: 0);
        UpdateAnimalCount(0);
        UpdateZoomDisplay();
        UpdateWaypointStatus(null, null, string.Empty);
        UpdateRoutePlanControls();
        UpdateMeasurementStatus();
        _resourceFinderUiSignature = string.Empty;
        UpdateResourceFinder(force: true);
        SetConnectionStatus("CONNECTING LOCAL MAP", Color.FromRgb(255, 178, 74));
    }

    private async void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            SetConnectionStatus("CONNECTION FAILED", Color.FromRgb(242, 96, 61));
            SetLoading(true, $"The bundled map returned {e.WebErrorStatus}. Use reload to try again.");
            return;
        }

        var currentUri = LiveMapWebView.Source;
        if (currentUri is not null && IsLiveMapUri(currentUri))
        {
            SetConnectionStatus("PREPARING MAP", Color.FromRgb(255, 178, 74));
            await ApplyMinimapPresentationAsync();
            SetConnectionStatus("INSTALLING FOLLOW", Color.FromRgb(255, 178, 74));
            await InstallPlayerFollowAsync();
            await SyncSoundFinderMapAsync();
            SetConnectionStatus("RESTORING LAYERS", Color.FromRgb(255, 178, 74));
            await ReapplyActiveFocusModeLayersAsync();
            await RefreshIndependentLiveDataAsync(force: true);
            SetConnectionStatus("ISLEY MAP READY", Color.FromRgb(90, 210, 132));
        }
        else
        {
            SetConnectionStatus("ISLEY MAP READY", Color.FromRgb(90, 210, 132));
        }

        SetLoading(false, string.Empty);
    }

    private void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            var root = message.RootElement;
            if (!root.TryGetProperty("type", out var type))
            {
                return;
            }

            var messageType = type.GetString();
            if (messageType == "isley-copy-location")
            {
                var kind = root.TryGetProperty("kind", out var kindValue)
                           && kindValue.ValueKind == JsonValueKind.String
                    ? kindValue.GetString()
                    : string.Empty;
                var clipboardText = root.TryGetProperty("text", out var textValue)
                                    && textValue.ValueKind == JsonValueKind.String
                    ? textValue.GetString() ?? string.Empty
                    : string.Empty;
                if (kind == "map-location"
                    && clipboardText.StartsWith("Isley location | ", StringComparison.Ordinal)
                    && clipboardText.Length <= 320
                    && !clipboardText.Contains('\r')
                    && !clipboardText.Contains('\n'))
                {
                    Dispatcher.Invoke(() => Clipboard.SetText(clipboardText));
                }
                return;
            }

            if (messageType == "isley-player-snapshot")
            {
                HandlePlayerSnapshotMessage(root);
                return;
            }

            if (messageType != "isley-follow")
            {
                return;
            }

            var following = root.TryGetProperty("following", out var followingValue)
                            && followingValue.ValueKind == JsonValueKind.True;
            var markerAvailable = root.TryGetProperty("markerAvailable", out var markerValue)
                                  && markerValue.ValueKind == JsonValueKind.True;
            var freshnessKnown = root.TryGetProperty("freshnessKnown", out var freshnessKnownValue)
                                 && freshnessKnownValue.ValueKind == JsonValueKind.True;
            var freshnessAgeMs = root.TryGetProperty("freshnessAgeMs", out var freshnessAgeValue)
                ? Math.Max(0, freshnessAgeValue.GetDouble())
                : 0;
            double? centerErrorPx = root.TryGetProperty("centerErrorPx", out var centerErrorValue)
                                    && centerErrorValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, centerErrorValue.GetDouble())
                : null;
            var otherAnimalCount = root.TryGetProperty("otherAnimalCount", out var animalCountValue)
                                   && animalCountValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, animalCountValue.GetInt32())
                : 0;
            var friendAnimalCount = root.TryGetProperty("friendAnimalCount", out var friendCountValue)
                                    && friendCountValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, friendCountValue.GetInt32())
                : 0;
            var authorizedAnimalCount = root.TryGetProperty("authorizedAnimalCount", out var authorizedCountValue)
                                        && authorizedCountValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, authorizedCountValue.GetInt32())
                : otherAnimalCount;
            var mapScale = root.TryGetProperty("scale", out var scaleValue)
                           && scaleValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(scaleValue.GetDouble(), 1, 25)
                : _currentMapScale;
            var smartZoomSuspended = root.TryGetProperty("smartZoomSuspended", out var smartZoomSuspendedValue)
                                     && smartZoomSuspendedValue.ValueKind == JsonValueKind.True;
            double? scaleBarUnits = root.TryGetProperty("scaleBarUnits", out var scaleBarUnitsValue)
                                    && scaleBarUnitsValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(scaleBarUnitsValue.GetDouble(), 0.1, 1000)
                : null;
            double? scaleBarPixels = root.TryGetProperty("scaleBarPixels", out var scaleBarPixelsValue)
                                     && scaleBarPixelsValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(scaleBarPixelsValue.GetDouble(), 20, 140)
                : null;
            var selfGridReference = root.TryGetProperty("selfGridReference", out var selfGridReferenceValue)
                                    && selfGridReferenceValue.ValueKind == JsonValueKind.String
                ? (selfGridReferenceValue.GetString() ?? string.Empty).Trim().ToUpperInvariant()
                : string.Empty;
            var gridRowValid = selfGridReference.Length is 2 or 3
                               && int.TryParse(selfGridReference[1..], out var gridRow)
                               && gridRow is >= 1 and <= 10;
            if (!gridRowValid || selfGridReference[0] is < 'A' or > 'J')
            {
                selfGridReference = string.Empty;
            }
            var waypointArmed = root.TryGetProperty("waypointArmed", out var waypointArmedValue)
                                && waypointArmedValue.ValueKind == JsonValueKind.True;
            var waypointActive = root.TryGetProperty("waypointActive", out var waypointActiveValue)
                                 && waypointActiveValue.ValueKind == JsonValueKind.True;
            double? waypointDistance = root.TryGetProperty("waypointDistance", out var waypointDistanceValue)
                                       && waypointDistanceValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, waypointDistanceValue.GetDouble())
                : null;
            double? waypointBearing = root.TryGetProperty("waypointBearing", out var waypointBearingValue)
                                      && waypointBearingValue.ValueKind == JsonValueKind.Number
                ? (waypointBearingValue.GetDouble() + 360) % 360
                : null;
            var waypointCardinal = root.TryGetProperty("waypointCardinal", out var waypointCardinalValue)
                                   && waypointCardinalValue.ValueKind == JsonValueKind.String
                ? waypointCardinalValue.GetString() ?? string.Empty
                : string.Empty;
            var waypointLabel = root.TryGetProperty("waypointLabel", out var waypointLabelValue)
                                && waypointLabelValue.ValueKind == JsonValueKind.String
                ? waypointLabelValue.GetString() ?? string.Empty
                : string.Empty;
            var waypointKind = root.TryGetProperty("waypointKind", out var waypointKindValue)
                               && waypointKindValue.ValueKind == JsonValueKind.String
                ? ApproachBriefLogic.NormalizeKind(waypointKindValue.GetString())
                : string.Empty;
            var waypointTrend = root.TryGetProperty("waypointTrend", out var waypointTrendValue)
                                && waypointTrendValue.ValueKind == JsonValueKind.String
                ? waypointTrendValue.GetString() ?? "waiting"
                : "waiting";
            waypointTrend = waypointTrend is "closing" or "away" or "steady" or "waiting"
                ? waypointTrend
                : "waiting";
            double? waypointClosingRate = root.TryGetProperty("waypointClosingRate", out var closingRateValue)
                                              && closingRateValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(closingRateValue.GetDouble(), -600, 600)
                : null;
            double? waypointProgressPercent = root.TryGetProperty("waypointProgressPercent", out var progressValue)
                                                  && progressValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(progressValue.GetDouble(), 0, 100)
                : null;
            var routePlanArmed = root.TryGetProperty("routePlanArmed", out var routePlanArmedValue)
                                 && routePlanArmedValue.ValueKind == JsonValueKind.True;
            var routePlanActive = root.TryGetProperty("routePlanActive", out var routePlanActiveValue)
                                  && routePlanActiveValue.ValueKind == JsonValueKind.True;
            var routePlanComplete = root.TryGetProperty("routePlanComplete", out var routePlanCompleteValue)
                                    && routePlanCompleteValue.ValueKind == JsonValueKind.True;
            var routePlanSource = root.TryGetProperty("routePlanSource", out var routePlanSourceValue)
                                  && routePlanSourceValue.ValueKind == JsonValueKind.String
                ? routePlanSourceValue.GetString() ?? string.Empty
                : string.Empty;
            routePlanSource = routePlanSource is "manual" or "breadcrumb" or "shared" or "terrain"
                ? routePlanSource
                : string.Empty;
            var routeStopCount = root.TryGetProperty("routeStopCount", out var routeStopCountValue)
                                 && routeStopCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(routeStopCountValue.GetInt32(), 0, 12)
                : 0;
            var routeCurrentIndex = root.TryGetProperty("routeCurrentIndex", out var routeCurrentIndexValue)
                                    && routeCurrentIndexValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(routeCurrentIndexValue.GetInt32(), 0, Math.Max(0, routeStopCount - 1))
                : 0;
            double? routePlanTotalDistance = ReadNullableNumber(root, "routePlanTotalDistance");
            double? routeRemainingDistance = ReadNullableNumber(root, "routeRemainingDistance");
            var terrainNetworkReady = root.TryGetProperty("terrainNetworkReady", out var terrainReadyValue)
                                      && terrainReadyValue.ValueKind == JsonValueKind.True;
            var terrainNetworkPathCount = root.TryGetProperty(
                                              "terrainNetworkPathCount", out var terrainPathCountValue)
                                          && terrainPathCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(terrainPathCountValue.GetInt32(), 0, 200)
                : 0;
            var terrainNetworkPointCount = root.TryGetProperty(
                                               "terrainNetworkPointCount", out var terrainPointCountValue)
                                           && terrainPointCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(terrainPointCountValue.GetInt32(), 0, 20_000)
                : 0;
            var terrainNetworkSourceVersion = root.TryGetProperty(
                                                  "terrainNetworkSourceVersion", out var terrainVersionValue)
                                              && terrainVersionValue.ValueKind == JsonValueKind.String
                ? (terrainVersionValue.GetString() ?? string.Empty).Trim()
                : string.Empty;
            if (terrainNetworkSourceVersion.Length > 24)
            {
                terrainNetworkSourceVersion = terrainNetworkSourceVersion[..24];
            }
            DateTimeOffset? terrainNetworkLoadedAt = null;
            if (root.TryGetProperty("terrainNetworkLoadedAt", out var terrainLoadedValue)
                && terrainLoadedValue.ValueKind == JsonValueKind.Number)
            {
                var loadedMilliseconds = terrainLoadedValue.GetDouble();
                if (double.IsFinite(loadedMilliseconds)
                    && loadedMilliseconds is >= 0 and <= 253402300799999)
                {
                    terrainNetworkLoadedAt = DateTimeOffset.FromUnixTimeMilliseconds(
                        (long)loadedMilliseconds);
                }
            }
            double? terrainCourseDirectDistance = ReadNullableNumber(
                root, "terrainCourseDirectDistance");
            double? terrainCourseDistance = ReadNullableNumber(root, "terrainCourseDistance");
            double? terrainCourseDetourPercent = ReadNullableNumber(
                root, "terrainCourseDetourPercent");
            var terrainCourseAvoidedZoneCount = root.TryGetProperty(
                                                    "terrainCourseAvoidedZoneCount",
                                                    out var terrainAvoidedZoneCountValue)
                                                && terrainAvoidedZoneCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(terrainAvoidedZoneCountValue.GetInt32(), 0, 20)
                : 0;
            var terrainCourseAvoidedWater = root.TryGetProperty(
                                                  "terrainCourseAvoidedWater",
                                                  out var terrainCourseAvoidedWaterValue)
                                              && terrainCourseAvoidedWaterValue.ValueKind == JsonValueKind.True;
            var terrainCourseRoadDistance = Math.Clamp(
                ReadNullableNumber(root, "terrainCourseRoadDistance") ?? 0,
                0,
                100_000);
            var terrainCourseTrailDistance = Math.Clamp(
                ReadNullableNumber(root, "terrainCourseTrailDistance") ?? 0,
                0,
                100_000);
            var terrainCourseLearnedDistance = Math.Clamp(
                ReadNullableNumber(root, "terrainCourseLearnedDistance") ?? 0,
                0,
                100_000);
            var terrainCourseUnknownDistance = Math.Clamp(
                ReadNullableNumber(root, "terrainCourseUnknownDistance") ?? 0,
                0,
                100_000);
            var terrainCourseLongestUnknown = Math.Clamp(
                ReadNullableNumber(root, "terrainCourseLongestUnknown") ?? 0,
                0,
                100_000);
            var terrainCourseUnknownSegmentCount = root.TryGetProperty(
                                                        "terrainCourseUnknownSegmentCount",
                                                        out var terrainCourseUnknownSegmentCountValue)
                                                    && terrainCourseUnknownSegmentCountValue.ValueKind
                                                        == JsonValueKind.Number
                ? Math.Clamp(terrainCourseUnknownSegmentCountValue.GetInt32(), 0, 100)
                : 0;
            var terrainRouteStyle = root.TryGetProperty(
                                        "terrainRouteStyle", out var terrainRouteStyleValue)
                                    && terrainRouteStyleValue.ValueKind == JsonValueKind.String
                ? TerrainRouteStyleLogic.Normalize(terrainRouteStyleValue.GetString())
                : TerrainRouteStyleLogic.BalancedId;
            var terrainGapPolicy = root.TryGetProperty(
                                       "terrainGapPolicy", out var terrainGapPolicyValue)
                                   && terrainGapPolicyValue.ValueKind == JsonValueKind.String
                ? TerrainGapPolicyLogic.Normalize(terrainGapPolicyValue.GetString())
                : TerrainGapPolicyLogic.BalancedId;
            var terrainWaterSafetyEnabled = root.TryGetProperty(
                                                  "terrainWaterSafetyEnabled",
                                                  out var terrainWaterSafetyEnabledValue)
                                              && terrainWaterSafetyEnabledValue.ValueKind == JsonValueKind.True;
            var terrainWaterMaskStatus = root.TryGetProperty(
                                                   "terrainWaterMaskStatus",
                                                   out var terrainWaterMaskStatusValue)
                                               && terrainWaterMaskStatusValue.ValueKind == JsonValueKind.String
                ? (terrainWaterMaskStatusValue.GetString() ?? string.Empty).Trim().ToLowerInvariant()
                : string.Empty;
            terrainWaterMaskStatus = terrainWaterMaskStatus is "loading" or "ready" or "unavailable" or "hidden"
                ? terrainWaterMaskStatus
                : "unavailable";
            var terrainWaterMaskSourceVersion = root.TryGetProperty(
                                                         "terrainWaterMaskSourceVersion",
                                                         out var terrainWaterMaskSourceVersionValue)
                                                     && terrainWaterMaskSourceVersionValue.ValueKind == JsonValueKind.String
                ? (terrainWaterMaskSourceVersionValue.GetString() ?? string.Empty).Trim()
                : string.Empty;
            if (terrainWaterMaskSourceVersion.Length > 24)
            {
                terrainWaterMaskSourceVersion = terrainWaterMaskSourceVersion[..24];
            }
            var terrainCommunityHazardsEnabled = root.TryGetProperty(
                                                      "terrainCommunityHazardsEnabled",
                                                      out var terrainCommunityHazardsEnabledValue)
                                                  && terrainCommunityHazardsEnabledValue.ValueKind
                                                      == JsonValueKind.True;
            var terrainCommunityHazardStatus = root.TryGetProperty(
                                                   "terrainCommunityHazardStatus",
                                                   out var terrainCommunityHazardStatusValue)
                                               && terrainCommunityHazardStatusValue.ValueKind
                                                   == JsonValueKind.String
                ? (terrainCommunityHazardStatusValue.GetString() ?? string.Empty)
                    .Trim().ToLowerInvariant()
                : "unavailable";
            terrainCommunityHazardStatus = terrainCommunityHazardStatus
                is "waiting-source" or "ready" or "unavailable" or "hidden"
                ? terrainCommunityHazardStatus
                : "unavailable";
            var terrainCommunityHazardCount = root.TryGetProperty(
                                                  "terrainCommunityHazardCount",
                                                  out var terrainCommunityHazardCountValue)
                                              && terrainCommunityHazardCountValue.ValueKind
                                                  == JsonValueKind.Number
                ? Math.Clamp(terrainCommunityHazardCountValue.GetInt32(), 0, 64)
                : 0;
            var terrainCommunityHazardSourceVersion = root.TryGetProperty(
                                                          "terrainCommunityHazardSourceVersion",
                                                          out var terrainCommunityHazardVersionValue)
                                                      && terrainCommunityHazardVersionValue.ValueKind
                                                          == JsonValueKind.String
                ? (terrainCommunityHazardVersionValue.GetString() ?? string.Empty).Trim()
                : string.Empty;
            if (terrainCommunityHazardSourceVersion.Length > 24)
            {
                terrainCommunityHazardSourceVersion =
                    terrainCommunityHazardSourceVersion[..24];
            }
            DateTimeOffset? terrainCommunityHazardLoadedAt = null;
            if (root.TryGetProperty(
                    "terrainCommunityHazardLoadedAt",
                    out var terrainCommunityHazardLoadedValue)
                && terrainCommunityHazardLoadedValue.ValueKind == JsonValueKind.Number)
            {
                var loadedMilliseconds = terrainCommunityHazardLoadedValue.GetDouble();
                if (double.IsFinite(loadedMilliseconds)
                    && loadedMilliseconds is >= 0 and <= 253402300799999)
                {
                    terrainCommunityHazardLoadedAt = DateTimeOffset.FromUnixTimeMilliseconds(
                        (long)loadedMilliseconds);
                }
            }
            var terrainCourseStatus = root.TryGetProperty(
                                          "terrainCourseStatus", out var terrainCourseStatusValue)
                                      && terrainCourseStatusValue.ValueKind == JsonValueKind.String
                ? (terrainCourseStatusValue.GetString() ?? string.Empty).Trim().ToLowerInvariant()
                : string.Empty;
            if (terrainCourseStatus.Length > 40)
            {
                terrainCourseStatus = terrainCourseStatus[..40];
            }
            var learnedPassageCount = root.TryGetProperty(
                                          "learnedPassageCount",
                                          out var learnedPassageCountValue)
                                      && learnedPassageCountValue.ValueKind
                                          == JsonValueKind.Number
                ? Math.Clamp(learnedPassageCountValue.GetInt32(), 0, 12)
                : 0;
            var learnedPassageActiveCount = root.TryGetProperty(
                                                "learnedPassageActiveCount",
                                                out var learnedPassageActiveCountValue)
                                            && learnedPassageActiveCountValue.ValueKind
                                                == JsonValueKind.Number
                ? Math.Clamp(learnedPassageActiveCountValue.GetInt32(), 0, 12)
                : 0;
            var learnedPassageStaleCount = root.TryGetProperty(
                                               "learnedPassageStaleCount",
                                               out var learnedPassageStaleCountValue)
                                           && learnedPassageStaleCountValue.ValueKind
                                               == JsonValueKind.Number
                ? Math.Clamp(learnedPassageStaleCountValue.GetInt32(), 0, 12)
                : 0;
            var learnedPassagePointCount = root.TryGetProperty(
                                               "learnedPassagePointCount",
                                               out var learnedPassagePointCountValue)
                                           && learnedPassagePointCountValue.ValueKind
                                               == JsonValueKind.Number
                ? Math.Clamp(learnedPassagePointCountValue.GetInt32(), 0, 1_440)
                : 0;
            var tripRouteObstacleCount = root.TryGetProperty(
                                             "tripRouteObstacleCount", out var tripRouteObstacleCountValue)
                                         && tripRouteObstacleCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(tripRouteObstacleCountValue.GetInt32(), 0, 20)
                : 0;
            var tripRouteInsideObstacle = root.TryGetProperty(
                                              "tripRouteInsideObstacle", out var tripRouteInsideObstacleValue)
                                          && tripRouteInsideObstacleValue.ValueKind == JsonValueKind.True;
            double? navigationEtaMinutes = ReadNullableNumber(root, "navigationEtaMinutes");
            double? navigationEtaPace = ReadNullableNumber(root, "navigationEtaPace");
            double? navigationEtaDistance = ReadNullableNumber(root, "navigationEtaDistance");
            var navigationEtaSource = root.TryGetProperty("navigationEtaSource", out var navigationEtaSourceValue)
                                      && navigationEtaSourceValue.ValueKind == JsonValueKind.String
                ? (navigationEtaSourceValue.GetString() ?? string.Empty).Trim().ToUpperInvariant()
                : string.Empty;
            navigationEtaSource = navigationEtaSource is "LIVE" or "RECENT" or "TRIP"
                ? navigationEtaSource
                : string.Empty;
            var routeStops = new List<RouteStopInfo>();
            if (root.TryGetProperty("routeStops", out var routeStopsValue)
                && routeStopsValue.ValueKind == JsonValueKind.Array)
            {
                foreach (var routeStopValue in routeStopsValue.EnumerateArray().Take(12))
                {
                    if (routeStopValue.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    var index = routeStopValue.TryGetProperty("index", out var routeStopIndexValue)
                                && routeStopIndexValue.ValueKind == JsonValueKind.Number
                        ? Math.Clamp(routeStopIndexValue.GetInt32(), 0, 11)
                        : routeStops.Count;
                    routeStops.Add(new RouteStopInfo(
                        index,
                        ReadNullableNumber(routeStopValue, "worldX"),
                        ReadNullableNumber(routeStopValue, "worldY")));
                }
            }
            var measurementArmed = root.TryGetProperty("measurementArmed", out var measurementArmedValue)
                                   && measurementArmedValue.ValueKind == JsonValueKind.True;
            var measurementHasStart = root.TryGetProperty("measurementHasStart", out var measurementHasStartValue)
                                      && measurementHasStartValue.ValueKind == JsonValueKind.True;
            var measurementActive = root.TryGetProperty("measurementActive", out var measurementActiveValue)
                                    && measurementActiveValue.ValueKind == JsonValueKind.True;
            double? measurementDistance = root.TryGetProperty("measurementDistance", out var measurementDistanceValue)
                                                  && measurementDistanceValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, measurementDistanceValue.GetDouble())
                : null;
            double? measurementBearing = root.TryGetProperty("measurementBearing", out var measurementBearingValue)
                                                 && measurementBearingValue.ValueKind == JsonValueKind.Number
                ? (measurementBearingValue.GetDouble() + 360) % 360
                : null;
            var measurementCardinal = root.TryGetProperty("measurementCardinal", out var measurementCardinalValue)
                                      && measurementCardinalValue.ValueKind == JsonValueKind.String
                ? measurementCardinalValue.GetString() ?? string.Empty
                : string.Empty;
            double? measurementStartWorldX = ReadNullableNumber(root, "measurementStartWorldX");
            double? measurementStartWorldY = ReadNullableNumber(root, "measurementStartWorldY");
            double? measurementEndWorldX = ReadNullableNumber(root, "measurementEndWorldX");
            double? measurementEndWorldY = ReadNullableNumber(root, "measurementEndWorldY");
            var measurementMarkedBoundaryCount = root.TryGetProperty(
                                                      "measurementMarkedBoundaryCount", out var measurementMarkedBoundaryCountValue)
                                                  && measurementMarkedBoundaryCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(measurementMarkedBoundaryCountValue.GetInt32(), 0, 20)
                : 0;
            var measurementInsideMarkedBoundary = root.TryGetProperty(
                                                       "measurementInsideMarkedBoundary", out var measurementInsideMarkedBoundaryValue)
                                                   && measurementInsideMarkedBoundaryValue.ValueKind == JsonValueKind.True;
            var friendRouteName = root.TryGetProperty("friendRouteName", out var friendRouteNameValue)
                                  && friendRouteNameValue.ValueKind == JsonValueKind.String
                ? friendRouteNameValue.GetString() ?? string.Empty
                : string.Empty;
            var packRouteActive = root.TryGetProperty("packRouteActive", out var packRouteActiveValue)
                                  && packRouteActiveValue.ValueKind == JsonValueKind.True;
            var packOutlierRouteActive = root.TryGetProperty(
                                             "packOutlierRouteActive", out var packOutlierRouteActiveValue)
                                         && packOutlierRouteActiveValue.ValueKind == JsonValueKind.True;
            var pinArmed = root.TryGetProperty("pinArmed", out var pinArmedValue)
                           && pinArmedValue.ValueKind == JsonValueKind.True;
            var pinType = root.TryGetProperty("pinType", out var pinTypeValue)
                          && pinTypeValue.ValueKind == JsonValueKind.String
                ? pinTypeValue.GetString() ?? "safe"
                : "safe";
            var pinCount = root.TryGetProperty("pinCount", out var pinCountValue)
                           && pinCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(pinCountValue.GetInt32(), 0, 20)
                : 0;
            var activePinId = root.TryGetProperty("activePinId", out var activePinIdValue)
                              && activePinIdValue.ValueKind == JsonValueKind.String
                ? (activePinIdValue.GetString() ?? string.Empty).Trim()
                : string.Empty;
            if (activePinId.Length > 96)
            {
                activePinId = activePinId[..96];
            }
            var pinRoster = new List<PinRouteInfo>();
            if (root.TryGetProperty("pinRoster", out var pinRosterValue)
                && pinRosterValue.ValueKind == JsonValueKind.Array)
            {
                foreach (var pinValue in pinRosterValue.EnumerateArray().Take(20))
                {
                    if (pinValue.ValueKind != JsonValueKind.Object
                        || !pinValue.TryGetProperty("id", out var pinIdValue)
                        || pinIdValue.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var pinId = (pinIdValue.GetString() ?? string.Empty).Trim();
                    if (pinId.Length == 0)
                    {
                        continue;
                    }
                    if (pinId.Length > 96)
                    {
                        pinId = pinId[..96];
                    }

                    var pinRouteType = pinValue.TryGetProperty("type", out var pinRouteTypeValue)
                                       && pinRouteTypeValue.ValueKind == JsonValueKind.String
                        ? pinRouteTypeValue.GetString() ?? "safe"
                        : "safe";
                    pinRouteType = pinRouteType is "safe" or "nest" or "food" or "danger"
                        or "water" or "rally" or "death"
                        ? pinRouteType
                        : "safe";
                    var pinLabel = pinValue.TryGetProperty("label", out var pinLabelValue)
                                   && pinLabelValue.ValueKind == JsonValueKind.String
                        ? (pinLabelValue.GetString() ?? string.Empty).Trim()
                        : string.Empty;
                    if (pinLabel.Length > 64)
                    {
                        pinLabel = pinLabel[..64];
                    }
                    if (pinLabel.Length == 0)
                    {
                        pinLabel = FormatPinType(pinRouteType);
                    }

                    var pinX = pinValue.TryGetProperty("x", out var pinXValue)
                               && pinXValue.ValueKind == JsonValueKind.Number
                        ? Math.Clamp(pinXValue.GetDouble(), 0, 1000)
                        : 0;
                    var pinY = pinValue.TryGetProperty("y", out var pinYValue)
                               && pinYValue.ValueKind == JsonValueKind.Number
                        ? Math.Clamp(pinYValue.GetDouble(), 0, 1000)
                        : 0;
                    double? pinWorldX = pinValue.TryGetProperty("worldX", out var pinWorldXValue)
                                                && pinWorldXValue.ValueKind == JsonValueKind.Number
                        ? pinWorldXValue.GetDouble()
                        : null;
                    double? pinWorldY = pinValue.TryGetProperty("worldY", out var pinWorldYValue)
                                                && pinWorldYValue.ValueKind == JsonValueKind.Number
                        ? pinWorldYValue.GetDouble()
                        : null;
                    double? pinDistance = pinValue.TryGetProperty("distance", out var pinDistanceValue)
                                                  && pinDistanceValue.ValueKind == JsonValueKind.Number
                        ? Math.Max(0, pinDistanceValue.GetDouble())
                        : null;
                    double? pinBearing = pinValue.TryGetProperty("bearing", out var pinBearingValue)
                                                 && pinBearingValue.ValueKind == JsonValueKind.Number
                        ? (pinBearingValue.GetDouble() + 360) % 360
                        : null;
                    var pinCardinal = pinValue.TryGetProperty("cardinal", out var pinCardinalValue)
                                      && pinCardinalValue.ValueKind == JsonValueKind.String
                        ? pinCardinalValue.GetString() ?? string.Empty
                        : string.Empty;
                    var pinFavorite = pinValue.TryGetProperty("favorite", out var pinFavoriteValue)
                                      && pinFavoriteValue.ValueKind == JsonValueKind.True;
                    double? pinExpiresAt = pinValue.TryGetProperty("expiresAt", out var pinExpiresAtValue)
                                                   && pinExpiresAtValue.ValueKind == JsonValueKind.Number
                        ? Math.Max(0, pinExpiresAtValue.GetDouble())
                        : null;
                    double? pinExpiresInMs = pinValue.TryGetProperty("expiresInMs", out var pinExpiresInValue)
                                                     && pinExpiresInValue.ValueKind == JsonValueKind.Number
                        ? Math.Max(0, pinExpiresInValue.GetDouble())
                        : null;
                    var pinExpiryMinutes = pinValue.TryGetProperty("expiryMinutes", out var pinExpiryMinutesValue)
                                           && pinExpiryMinutesValue.ValueKind == JsonValueKind.Number
                        ? pinExpiryMinutesValue.GetInt32()
                        : 0;
                    pinExpiryMinutes = pinExpiryMinutes is 5 or 15 or 30 or 60 ? pinExpiryMinutes : 0;
                    var pinAlertRadius = pinValue.TryGetProperty("alertRadius", out var pinAlertRadiusValue)
                                         && pinAlertRadiusValue.ValueKind == JsonValueKind.Number
                        ? pinAlertRadiusValue.GetInt32()
                        : 0;
                    pinAlertRadius = pinAlertRadius is 10 or 25 or 50 or 100 ? pinAlertRadius : 0;
                    var pinInsideAlertZone = pinValue.TryGetProperty(
                                                  "insideAlertZone", out var pinInsideAlertZoneValue)
                                             && pinInsideAlertZoneValue.ValueKind == JsonValueKind.True;
                    double? pinDistanceToAlertZone = pinValue.TryGetProperty(
                                                               "distanceToAlertZone", out var pinDistanceToAlertZoneValue)
                                                           && pinDistanceToAlertZoneValue.ValueKind == JsonValueKind.Number
                        ? Math.Max(0, pinDistanceToAlertZoneValue.GetDouble())
                        : null;
                    pinRoster.Add(new PinRouteInfo(
                        pinId,
                        pinRouteType,
                        pinLabel,
                        pinX,
                        pinY,
                        pinWorldX,
                        pinWorldY,
                        pinDistance,
                        pinBearing,
                        pinCardinal,
                        pinFavorite,
                        pinExpiresAt,
                        pinExpiresInMs,
                        pinExpiryMinutes,
                        pinAlertRadius,
                        pinInsideAlertZone,
                        pinDistanceToAlertZone));
                }
            }
            var noGoAreaCount = root.TryGetProperty("noGoAreaCount", out var noGoAreaCountValue)
                                && noGoAreaCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(noGoAreaCountValue.GetInt32(), 0, NoGoAreaLogic.MaximumAreaCount)
                : 0;
            var noGoTraceActive = root.TryGetProperty("noGoTraceActive", out var noGoTraceActiveValue)
                                  && noGoTraceActiveValue.ValueKind == JsonValueKind.True;
            var noGoTraceVertexCount = root.TryGetProperty(
                                           "noGoTraceVertexCount", out var noGoTraceVertexCountValue)
                                       && noGoTraceVertexCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(noGoTraceVertexCountValue.GetInt32(), 0, NoGoAreaLogic.MaximumVertexCount)
                : 0;
            var noGoSelectedAreaId = root.TryGetProperty(
                                         "noGoSelectedAreaId", out var noGoSelectedAreaIdValue)
                                     && noGoSelectedAreaIdValue.ValueKind == JsonValueKind.String
                ? (noGoSelectedAreaIdValue.GetString() ?? string.Empty).Trim()
                : string.Empty;
            if (noGoSelectedAreaId.Length > 80) noGoSelectedAreaId = noGoSelectedAreaId[..80];
            var noGoSelectedAreaLabel = root.TryGetProperty(
                                            "noGoSelectedAreaLabel", out var noGoSelectedAreaLabelValue)
                                        && noGoSelectedAreaLabelValue.ValueKind == JsonValueKind.String
                ? (noGoSelectedAreaLabelValue.GetString() ?? string.Empty).Trim()
                : string.Empty;
            if (noGoSelectedAreaLabel.Length > 40) noGoSelectedAreaLabel = noGoSelectedAreaLabel[..40];
            var noGoSelectedAreaVertexCount = root.TryGetProperty(
                                                  "noGoSelectedAreaVertexCount",
                                                  out var noGoSelectedAreaVertexCountValue)
                                              && noGoSelectedAreaVertexCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(noGoSelectedAreaVertexCountValue.GetInt32(), 0, NoGoAreaLogic.MaximumVertexCount)
                : 0;
            var noGoLastStatus = root.TryGetProperty("noGoLastStatus", out var noGoLastStatusValue)
                                 && noGoLastStatusValue.ValueKind == JsonValueKind.String
                ? (noGoLastStatusValue.GetString() ?? string.Empty).Trim().ToLowerInvariant()
                : string.Empty;
            if (noGoLastStatus.Length > 48) noGoLastStatus = noGoLastStatus[..48];
            var noGoAreaRoster = new List<NoGoAreaRosterInfo>();
            if (root.TryGetProperty("noGoAreaRoster", out var noGoAreaRosterValue)
                && noGoAreaRosterValue.ValueKind == JsonValueKind.Array)
            {
                foreach (var areaValue in noGoAreaRosterValue.EnumerateArray()
                             .Take(NoGoAreaLogic.MaximumAreaCount))
                {
                    if (areaValue.ValueKind != JsonValueKind.Object
                        || !areaValue.TryGetProperty("id", out var areaIdValue)
                        || areaIdValue.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var id = (areaIdValue.GetString() ?? string.Empty).Trim();
                    if (id.Length == 0) continue;
                    if (id.Length > 80) id = id[..80];
                    var label = areaValue.TryGetProperty("label", out var areaLabelValue)
                                && areaLabelValue.ValueKind == JsonValueKind.String
                        ? (areaLabelValue.GetString() ?? string.Empty).Trim()
                        : "No-go area";
                    if (label.Length > 40) label = label[..40];
                    var vertexCount = areaValue.TryGetProperty("vertexCount", out var areaVertexCountValue)
                                      && areaVertexCountValue.ValueKind == JsonValueKind.Number
                        ? Math.Clamp(
                            areaVertexCountValue.GetInt32(),
                            NoGoAreaLogic.MinimumVertexCount,
                            NoGoAreaLogic.MaximumVertexCount)
                        : NoGoAreaLogic.MinimumVertexCount;
                    noGoAreaRoster.Add(new NoGoAreaRosterInfo(
                        id,
                        string.IsNullOrWhiteSpace(label) ? "No-go area" : label,
                        vertexCount));
                }
            }
            var recentRoutes = new List<RecentRouteInfo>();
            if (root.TryGetProperty("recentRoutes", out var recentRoutesValue)
                && recentRoutesValue.ValueKind == JsonValueKind.Array)
            {
                foreach (var recentRouteValue in recentRoutesValue.EnumerateArray().Take(6))
                {
                    if (recentRouteValue.ValueKind != JsonValueKind.Object
                        || !recentRouteValue.TryGetProperty("id", out var recentRouteIdValue)
                        || recentRouteIdValue.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var id = (recentRouteIdValue.GetString() ?? string.Empty).Trim();
                    var label = recentRouteValue.TryGetProperty("label", out var recentRouteLabelValue)
                                && recentRouteLabelValue.ValueKind == JsonValueKind.String
                        ? (recentRouteLabelValue.GetString() ?? string.Empty).Trim()
                        : string.Empty;
                    var gridReference = recentRouteValue.TryGetProperty("gridReference", out var recentGridValue)
                                        && recentGridValue.ValueKind == JsonValueKind.String
                        ? (recentGridValue.GetString() ?? string.Empty).Trim().ToUpperInvariant()
                        : string.Empty;
                    var active = recentRouteValue.TryGetProperty("active", out var recentActiveValue)
                                 && recentActiveValue.ValueKind == JsonValueKind.True;
                    if (id.Length == 0 || label.Length == 0)
                    {
                        continue;
                    }
                    if (id.Length > 96) id = id[..96];
                    if (label.Length > 64) label = label[..64];
                    if (!Regex.IsMatch(gridReference, "^[A-J](?:[1-9]|10)$")) gridReference = string.Empty;
                    recentRoutes.Add(new RecentRouteInfo(id, label, gridReference, active));
                }
            }
            var canRouteBack = root.TryGetProperty("canRouteBack", out var canRouteBackValue)
                               && canRouteBackValue.ValueKind == JsonValueKind.True
                               && recentRoutes.Count > 1;
            var sessionStartAvailable = root.TryGetProperty("sessionStartAvailable", out var sessionStartAvailableValue)
                                        && sessionStartAvailableValue.ValueKind == JsonValueKind.True;
            double? sessionStartDistance = root.TryGetProperty("sessionStartDistance", out var sessionStartDistanceValue)
                                                   && sessionStartDistanceValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, sessionStartDistanceValue.GetDouble())
                : null;
            double? sessionStartBearing = root.TryGetProperty("sessionStartBearing", out var sessionStartBearingValue)
                                                  && sessionStartBearingValue.ValueKind == JsonValueKind.Number
                ? (sessionStartBearingValue.GetDouble() + 360) % 360
                : null;
            var sessionStartCardinal = root.TryGetProperty("sessionStartCardinal", out var sessionStartCardinalValue)
                                       && sessionStartCardinalValue.ValueKind == JsonValueKind.String
                ? sessionStartCardinalValue.GetString() ?? string.Empty
                : string.Empty;
            var breadcrumbReturnAvailable = root.TryGetProperty("breadcrumbReturnAvailable", out var breadcrumbAvailableValue)
                                            && breadcrumbAvailableValue.ValueKind == JsonValueKind.True;
            var breadcrumbPointCount = root.TryGetProperty("breadcrumbPointCount", out var breadcrumbPointCountValue)
                                       && breadcrumbPointCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(breadcrumbPointCountValue.GetInt32(), 0, 5000)
                : 0;
            var breadcrumbDistance = root.TryGetProperty("breadcrumbDistance", out var breadcrumbDistanceValue)
                                     && breadcrumbDistanceValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, breadcrumbDistanceValue.GetDouble())
                : 0;
            var lastPositionAvailable = root.TryGetProperty("lastPositionAvailable", out var lastPositionAvailableValue)
                                        && lastPositionAvailableValue.ValueKind == JsonValueKind.True;
            var lastPositionAgeMs = root.TryGetProperty("lastPositionAgeMs", out var lastPositionAgeValue)
                                    && lastPositionAgeValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, lastPositionAgeValue.GetDouble())
                : 0;
            var nearestFriendName = root.TryGetProperty("nearestFriendName", out var nearestFriendNameValue)
                                    && nearestFriendNameValue.ValueKind == JsonValueKind.String
                ? nearestFriendNameValue.GetString() ?? string.Empty
                : string.Empty;
            double? nearestFriendDistance = root.TryGetProperty("nearestFriendDistance", out var nearestFriendDistanceValue)
                                            && nearestFriendDistanceValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, nearestFriendDistanceValue.GetDouble())
                : null;
            double? nearestFriendBearing = root.TryGetProperty("nearestFriendBearing", out var nearestFriendBearingValue)
                                           && nearestFriendBearingValue.ValueKind == JsonValueKind.Number
                ? (nearestFriendBearingValue.GetDouble() + 360) % 360
                : null;
            var nearestFriendCardinal = root.TryGetProperty("nearestFriendCardinal", out var nearestFriendCardinalValue)
                                        && nearestFriendCardinalValue.ValueKind == JsonValueKind.String
                ? nearestFriendCardinalValue.GetString() ?? string.Empty
                : string.Empty;
            var packFriendCount = root.TryGetProperty("packFriendCount", out var packFriendCountValue)
                                  && packFriendCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(packFriendCountValue.GetInt32(), 0, 20)
                : 0;
            double? packSpread = root.TryGetProperty("packSpread", out var packSpreadValue)
                                 && packSpreadValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(packSpreadValue.GetDouble(), 0, 2000)
                : null;
            var packSpreadMotion = root.TryGetProperty("packSpreadMotion", out var packSpreadMotionValue)
                                   && packSpreadMotionValue.ValueKind == JsonValueKind.String
                ? packSpreadMotionValue.GetString() ?? string.Empty
                : string.Empty;
            if (packSpreadMotion is not ("spreading" or "regrouping" or "steady"))
            {
                packSpreadMotion = string.Empty;
            }
            double? packSpreadRate = root.TryGetProperty("packSpreadRate", out var packSpreadRateValue)
                                     && packSpreadRateValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(packSpreadRateValue.GetDouble(), -1200, 1200)
                : null;
            var packSpreadMotionSampleCount = root.TryGetProperty(
                                                  "packSpreadMotionSampleCount",
                                                  out var packSpreadMotionSampleCountValue)
                                              && packSpreadMotionSampleCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(packSpreadMotionSampleCountValue.GetInt32(), 0, 12)
                : 0;
            var packCourseState = root.TryGetProperty("packCourseState", out var packCourseStateValue)
                                  && packCourseStateValue.ValueKind == JsonValueKind.String
                ? packCourseStateValue.GetString() ?? string.Empty
                : string.Empty;
            if (packCourseState is not ("moving" or "stationary"))
            {
                packCourseState = string.Empty;
            }
            double? packCourseSpeed = root.TryGetProperty("packCourseSpeed", out var packCourseSpeedValue)
                                      && packCourseSpeedValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(packCourseSpeedValue.GetDouble(), 0, 1200)
                : null;
            double? packCourseBearing = root.TryGetProperty("packCourseBearing", out var packCourseBearingValue)
                                        && packCourseBearingValue.ValueKind == JsonValueKind.Number
                ? (packCourseBearingValue.GetDouble() + 360) % 360
                : null;
            var packCourseCardinal = root.TryGetProperty("packCourseCardinal", out var packCourseCardinalValue)
                                     && packCourseCardinalValue.ValueKind == JsonValueKind.String
                ? packCourseCardinalValue.GetString() ?? string.Empty
                : string.Empty;
            var packCourseSampleCount = root.TryGetProperty(
                                            "packCourseSampleCount", out var packCourseSampleCountValue)
                                        && packCourseSampleCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(packCourseSampleCountValue.GetInt32(), 0, 12)
                : 0;
            double? packRadius = root.TryGetProperty("packRadius", out var packRadiusValue)
                                 && packRadiusValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(packRadiusValue.GetDouble(), 0, 2000)
                : null;
            double? packCenterDistance = root.TryGetProperty("packCenterDistance", out var packCenterDistanceValue)
                                         && packCenterDistanceValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(packCenterDistanceValue.GetDouble(), 0, 2000)
                : null;
            double? packCenterBearing = root.TryGetProperty("packCenterBearing", out var packCenterBearingValue)
                                        && packCenterBearingValue.ValueKind == JsonValueKind.Number
                ? (packCenterBearingValue.GetDouble() + 360) % 360
                : null;
            var packCenterCardinal = root.TryGetProperty("packCenterCardinal", out var packCenterCardinalValue)
                                     && packCenterCardinalValue.ValueKind == JsonValueKind.String
                ? packCenterCardinalValue.GetString() ?? string.Empty
                : string.Empty;
            var packFarthestFriendName = root.TryGetProperty(
                                              "packFarthestFriendName", out var packFarthestFriendNameValue)
                                          && packFarthestFriendNameValue.ValueKind == JsonValueKind.String
                ? (packFarthestFriendNameValue.GetString() ?? string.Empty).Trim()
                : string.Empty;
            if (packFarthestFriendName.Length > 64)
            {
                packFarthestFriendName = packFarthestFriendName[..64];
            }
            double? packFarthestFriendDistance = root.TryGetProperty(
                                                       "packFarthestFriendDistance",
                                                       out var packFarthestFriendDistanceValue)
                                                   && packFarthestFriendDistanceValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(packFarthestFriendDistanceValue.GetDouble(), 0, 2000)
                : null;
            var packCenterAvailable = root.TryGetProperty("packCenterAvailable", out var packCenterAvailableValue)
                                      && packCenterAvailableValue.ValueKind == JsonValueKind.True;
            var encounterPlayerCount = root.TryGetProperty("encounterPlayerCount", out var encounterPlayerCountValue)
                                       && encounterPlayerCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(encounterPlayerCountValue.GetInt32(), 0, 200)
                : 0;
            double? nearestEncounterDistance = root.TryGetProperty(
                                                   "nearestEncounterDistance", out var nearestEncounterDistanceValue)
                                               && nearestEncounterDistanceValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(nearestEncounterDistanceValue.GetDouble(), 0, 2000)
                : null;
            double? nearestEncounterBearing = root.TryGetProperty(
                                                  "nearestEncounterBearing", out var nearestEncounterBearingValue)
                                              && nearestEncounterBearingValue.ValueKind == JsonValueKind.Number
                ? (nearestEncounterBearingValue.GetDouble() + 360) % 360
                : null;
            var nearestEncounterCardinal = root.TryGetProperty(
                                               "nearestEncounterCardinal", out var nearestEncounterCardinalValue)
                                           && nearestEncounterCardinalValue.ValueKind == JsonValueKind.String
                ? nearestEncounterCardinalValue.GetString() ?? string.Empty
                : string.Empty;
            var nearestEncounterMotion = root.TryGetProperty(
                                             "nearestEncounterMotion", out var nearestEncounterMotionValue)
                                         && nearestEncounterMotionValue.ValueKind == JsonValueKind.String
                ? nearestEncounterMotionValue.GetString() ?? string.Empty
                : string.Empty;
            if (nearestEncounterMotion is not ("closing" or "opening" or "steady"))
            {
                nearestEncounterMotion = string.Empty;
            }
            double? nearestEncounterRelativeSpeed = root.TryGetProperty(
                                                        "nearestEncounterRelativeSpeed",
                                                        out var nearestEncounterRelativeSpeedValue)
                                                    && nearestEncounterRelativeSpeedValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(nearestEncounterRelativeSpeedValue.GetDouble(), -1200, 1200)
                : null;
            double? nearestEncounterInterceptSeconds = root.TryGetProperty(
                                                           "nearestEncounterInterceptSeconds",
                                                           out var nearestEncounterInterceptSecondsValue)
                                                       && nearestEncounterInterceptSecondsValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(nearestEncounterInterceptSecondsValue.GetDouble(), 0, 900)
                : null;
            var nearestEncounterMotionSampleCount = root.TryGetProperty(
                                                        "nearestEncounterMotionSampleCount",
                                                        out var nearestEncounterMotionSampleCountValue)
                                                    && nearestEncounterMotionSampleCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(nearestEncounterMotionSampleCountValue.GetInt32(), 0, 12)
                : 0;
            var encounterWithin10 = root.TryGetProperty("encounterWithin10", out var encounterWithin10Value)
                                    && encounterWithin10Value.ValueKind == JsonValueKind.Number
                ? Math.Clamp(encounterWithin10Value.GetInt32(), 0, 200)
                : 0;
            var encounterWithin25 = root.TryGetProperty("encounterWithin25", out var encounterWithin25Value)
                                    && encounterWithin25Value.ValueKind == JsonValueKind.Number
                ? Math.Clamp(encounterWithin25Value.GetInt32(), 0, 200)
                : 0;
            var encounterWithin50 = root.TryGetProperty("encounterWithin50", out var encounterWithin50Value)
                                    && encounterWithin50Value.ValueKind == JsonValueKind.Number
                ? Math.Clamp(encounterWithin50Value.GetInt32(), 0, 200)
                : 0;
            var encounterMemoryTrackCount = root.TryGetProperty(
                                                "encounterMemoryTrackCount", out var encounterMemoryTrackCountValue)
                                            && encounterMemoryTrackCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(encounterMemoryTrackCountValue.GetInt32(), 0, 200)
                : 0;
            var rememberedEncounterCount = root.TryGetProperty(
                                               "rememberedEncounterCount", out var rememberedEncounterCountValue)
                                           && rememberedEncounterCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(rememberedEncounterCountValue.GetInt32(), 0, 200)
                : 0;
            double? rememberedEncounterNewestAgeMs = root.TryGetProperty(
                                                        "rememberedEncounterNewestAgeMs",
                                                        out var rememberedEncounterNewestAgeMsValue)
                                                    && rememberedEncounterNewestAgeMsValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(rememberedEncounterNewestAgeMsValue.GetDouble(), 0, 3600000)
                : null;
            double? nearestRememberedEncounterDistance = root.TryGetProperty(
                                                             "nearestRememberedEncounterDistance",
                                                             out var nearestRememberedEncounterDistanceValue)
                                                         && nearestRememberedEncounterDistanceValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(nearestRememberedEncounterDistanceValue.GetDouble(), 0, 2000)
                : null;
            double? nearestRememberedEncounterBearing = root.TryGetProperty(
                                                            "nearestRememberedEncounterBearing",
                                                            out var nearestRememberedEncounterBearingValue)
                                                        && nearestRememberedEncounterBearingValue.ValueKind == JsonValueKind.Number
                ? (nearestRememberedEncounterBearingValue.GetDouble() + 360) % 360
                : null;
            var nearestRememberedEncounterCardinal = root.TryGetProperty(
                                                         "nearestRememberedEncounterCardinal",
                                                         out var nearestRememberedEncounterCardinalValue)
                                                     && nearestRememberedEncounterCardinalValue.ValueKind == JsonValueKind.String
                ? nearestRememberedEncounterCardinalValue.GetString() ?? string.Empty
                : string.Empty;
            var nearestPlaceName = root.TryGetProperty("nearestPlaceName", out var nearestPlaceNameValue)
                                   && nearestPlaceNameValue.ValueKind == JsonValueKind.String
                ? (nearestPlaceNameValue.GetString() ?? string.Empty).Trim()
                : string.Empty;
            if (nearestPlaceName.Length > 64)
            {
                nearestPlaceName = nearestPlaceName[..64];
            }
            double? nearestPlaceDistance = root.TryGetProperty("nearestPlaceDistance", out var nearestPlaceDistanceValue)
                                           && nearestPlaceDistanceValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, nearestPlaceDistanceValue.GetDouble())
                : null;
            double? nearestPlaceBearing = root.TryGetProperty("nearestPlaceBearing", out var nearestPlaceBearingValue)
                                          && nearestPlaceBearingValue.ValueKind == JsonValueKind.Number
                ? (nearestPlaceBearingValue.GetDouble() + 360) % 360
                : null;
            var nearestPlaceCardinal = root.TryGetProperty("nearestPlaceCardinal", out var nearestPlaceCardinalValue)
                                       && nearestPlaceCardinalValue.ValueKind == JsonValueKind.String
                ? nearestPlaceCardinalValue.GetString() ?? string.Empty
                : string.Empty;
            var officialLandmarkCount = root.TryGetProperty("officialLandmarkCount", out var officialLandmarkCountValue)
                                        && officialLandmarkCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(officialLandmarkCountValue.GetInt32(), 0, 5000)
                : 0;
            var visibleLandmarkCount = root.TryGetProperty("visibleLandmarkCount", out var visibleLandmarkCountValue)
                                       && visibleLandmarkCountValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(visibleLandmarkCountValue.GetInt32(), 0, 5000)
                : officialLandmarkCount;
            var nearestDangerPinId = root.TryGetProperty("nearestDangerPinId", out var nearestDangerPinIdValue)
                                     && nearestDangerPinIdValue.ValueKind == JsonValueKind.String
                ? (nearestDangerPinIdValue.GetString() ?? string.Empty).Trim()
                : string.Empty;
            var nearestDangerLabel = root.TryGetProperty("nearestDangerLabel", out var nearestDangerLabelValue)
                                     && nearestDangerLabelValue.ValueKind == JsonValueKind.String
                ? (nearestDangerLabelValue.GetString() ?? string.Empty).Trim()
                : string.Empty;
            if (nearestDangerLabel.Length > 64)
            {
                nearestDangerLabel = nearestDangerLabel[..64];
            }
            double? nearestDangerDistance = root.TryGetProperty("nearestDangerDistance", out var nearestDangerDistanceValue)
                                            && nearestDangerDistanceValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, nearestDangerDistanceValue.GetDouble())
                : null;
            double? nearestDangerBearing = root.TryGetProperty("nearestDangerBearing", out var nearestDangerBearingValue)
                                           && nearestDangerBearingValue.ValueKind == JsonValueKind.Number
                ? (nearestDangerBearingValue.GetDouble() + 360) % 360
                : null;
            var nearestDangerCardinal = root.TryGetProperty("nearestDangerCardinal", out var nearestDangerCardinalValue)
                                        && nearestDangerCardinalValue.ValueKind == JsonValueKind.String
                ? nearestDangerCardinalValue.GetString() ?? string.Empty
                : string.Empty;
            var nearestAlertZonePinId = root.TryGetProperty(
                                            "nearestAlertZonePinId", out var nearestAlertZonePinIdValue)
                                        && nearestAlertZonePinIdValue.ValueKind == JsonValueKind.String
                ? (nearestAlertZonePinIdValue.GetString() ?? string.Empty).Trim()
                : string.Empty;
            if (nearestAlertZonePinId.Length > 96)
            {
                nearestAlertZonePinId = nearestAlertZonePinId[..96];
            }
            var nearestAlertZoneLabel = root.TryGetProperty(
                                            "nearestAlertZoneLabel", out var nearestAlertZoneLabelValue)
                                        && nearestAlertZoneLabelValue.ValueKind == JsonValueKind.String
                ? (nearestAlertZoneLabelValue.GetString() ?? string.Empty).Trim()
                : string.Empty;
            if (nearestAlertZoneLabel.Length > 64)
            {
                nearestAlertZoneLabel = nearestAlertZoneLabel[..64];
            }
            double? nearestAlertZoneDistance = root.TryGetProperty(
                                                   "nearestAlertZoneDistance", out var nearestAlertZoneDistanceValue)
                                               && nearestAlertZoneDistanceValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, nearestAlertZoneDistanceValue.GetDouble())
                : null;
            double? nearestAlertZoneBearing = root.TryGetProperty(
                                                  "nearestAlertZoneBearing", out var nearestAlertZoneBearingValue)
                                              && nearestAlertZoneBearingValue.ValueKind == JsonValueKind.Number
                ? (nearestAlertZoneBearingValue.GetDouble() + 360) % 360
                : null;
            var nearestAlertZoneCardinal = root.TryGetProperty(
                                               "nearestAlertZoneCardinal", out var nearestAlertZoneCardinalValue)
                                           && nearestAlertZoneCardinalValue.ValueKind == JsonValueKind.String
                ? nearestAlertZoneCardinalValue.GetString() ?? string.Empty
                : string.Empty;
            var nearestAlertZoneRadius = root.TryGetProperty(
                                             "nearestAlertZoneRadius", out var nearestAlertZoneRadiusValue)
                                         && nearestAlertZoneRadiusValue.ValueKind == JsonValueKind.Number
                ? nearestAlertZoneRadiusValue.GetDouble()
                : 0;
            nearestAlertZoneRadius = nearestAlertZoneRadius is 10 or 25 or 50 or 100
                ? nearestAlertZoneRadius
                : 0;
            double? nearestAlertZoneBoundaryDistance = root.TryGetProperty(
                                                           "nearestAlertZoneBoundaryDistance",
                                                           out var nearestAlertZoneBoundaryDistanceValue)
                                                       && nearestAlertZoneBoundaryDistanceValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, nearestAlertZoneBoundaryDistanceValue.GetDouble())
                : null;
            var insideAlertZone = root.TryGetProperty("insideAlertZone", out var insideAlertZoneValue)
                                  && insideAlertZoneValue.ValueKind == JsonValueKind.True;
            var friendRoster = new List<FriendRouteInfo>();
            if (root.TryGetProperty("friendRoster", out var friendRosterValue)
                && friendRosterValue.ValueKind == JsonValueKind.Array)
            {
                foreach (var friendValue in friendRosterValue.EnumerateArray().Take(20))
                {
                    if (friendValue.ValueKind != JsonValueKind.Object
                        || !friendValue.TryGetProperty("name", out var friendNameValue)
                        || friendNameValue.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var friendName = (friendNameValue.GetString() ?? string.Empty).Trim();
                    if (friendName.Length == 0)
                    {
                        continue;
                    }
                    if (friendName.Length > 64)
                    {
                        friendName = friendName[..64];
                    }

                    double? friendDistance = friendValue.TryGetProperty("distance", out var friendDistanceValue)
                                                     && friendDistanceValue.ValueKind == JsonValueKind.Number
                        ? Math.Max(0, friendDistanceValue.GetDouble())
                        : null;
                    double? friendBearing = friendValue.TryGetProperty("bearing", out var friendBearingValue)
                                                    && friendBearingValue.ValueKind == JsonValueKind.Number
                        ? (friendBearingValue.GetDouble() + 360) % 360
                        : null;
                    var friendCardinal = friendValue.TryGetProperty("cardinal", out var friendCardinalValue)
                                         && friendCardinalValue.ValueKind == JsonValueKind.String
                        ? friendCardinalValue.GetString() ?? string.Empty
                        : string.Empty;
                    friendRoster.Add(new FriendRouteInfo(
                        friendName,
                        friendDistance,
                        friendBearing,
                        friendCardinal));
                }
            }
            var markerResponseCount = root.TryGetProperty("markerResponseCount", out var responseCountValue)
                                      && responseCountValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, responseCountValue.GetInt32())
                : 0;
            var markerResponseStatus = root.TryGetProperty("markerResponseStatus", out var responseStatusValue)
                                       && responseStatusValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, responseStatusValue.GetInt32())
                : 0;
            var markerResponseOk = root.TryGetProperty("markerResponseOk", out var responseOkValue)
                                   && responseOkValue.ValueKind == JsonValueKind.True;
            var markerResponseSource = root.TryGetProperty("markerResponseSource", out var responseSourceValue)
                                       && responseSourceValue.ValueKind == JsonValueKind.String
                ? responseSourceValue.GetString() ?? "initial-model"
                : "initial-model";
            var fastPollIntervalMs = root.TryGetProperty("fastPollIntervalMs", out var pollIntervalValue)
                                     && pollIntervalValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, pollIntervalValue.GetDouble())
                : 0;
            var fastPollDelayMs = root.TryGetProperty("fastPollDelayMs", out var pollDelayValue)
                                  && pollDelayValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, pollDelayValue.GetDouble())
                : 0;
            var lastResponseIntervalMs = root.TryGetProperty("lastResponseIntervalMs", out var responseIntervalValue)
                                         && responseIntervalValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, responseIntervalValue.GetDouble())
                : 0;
            var lastFastPollDurationMs = root.TryGetProperty("lastFastPollDurationMs", out var pollDurationValue)
                                         && pollDurationValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, pollDurationValue.GetDouble())
                : 0;
            var pollControlPatched = root.TryGetProperty("pollControlPatched", out var pollControlValue)
                                     && pollControlValue.ValueKind == JsonValueKind.True;
            var markerNetworkCount = root.TryGetProperty("markerNetworkCount", out var networkCountValue)
                                     && networkCountValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, networkCountValue.GetInt32())
                : 0;
            var pollCallbackCount = root.TryGetProperty("pollCallbackCount", out var callbackCountValue)
                                    && callbackCountValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, callbackCountValue.GetInt32())
                : 0;
            var pollCallbackRuns = root.TryGetProperty("pollCallbackRuns", out var callbackRunsValue)
                                   && callbackRunsValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, callbackRunsValue.GetInt32())
                : 0;
            var controllerInstallCount = root.TryGetProperty("controllerInstallCount", out var installCountValue)
                                         && installCountValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, installCountValue.GetInt32())
                : 0;
            var selfPositionAt = root.TryGetProperty("selfPositionAt", out var selfPositionAtValue)
                                 && selfPositionAtValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, selfPositionAtValue.GetDouble())
                : 0;
            double? selfX = root.TryGetProperty("selfX", out var selfXValue)
                            && selfXValue.ValueKind == JsonValueKind.Number
                ? selfXValue.GetDouble()
                : null;
            double? selfY = root.TryGetProperty("selfY", out var selfYValue)
                             && selfYValue.ValueKind == JsonValueKind.Number
                ? selfYValue.GetDouble()
                : null;
            double? selfMapX = root.TryGetProperty("selfMapX", out var selfMapXValue)
                                && selfMapXValue.ValueKind == JsonValueKind.Number
                ? selfMapXValue.GetDouble()
                : null;
            double? selfMapY = root.TryGetProperty("selfMapY", out var selfMapYValue)
                                && selfMapYValue.ValueKind == JsonValueKind.Number
                ? selfMapYValue.GetDouble()
                : null;
            var selfBearing = root.TryGetProperty("selfBearing", out var selfBearingValue)
                              && selfBearingValue.ValueKind == JsonValueKind.Number
                ? (selfBearingValue.GetDouble() + 360) % 360
                : 0;
            var selfSpeed = root.TryGetProperty("selfSpeed", out var selfSpeedValue)
                            && selfSpeedValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, selfSpeedValue.GetDouble())
                : 0;
            var sessionDistance = root.TryGetProperty("sessionDistance", out var sessionDistanceValue)
                                  && sessionDistanceValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, sessionDistanceValue.GetDouble())
                : 0;
            var sessionStatsActive = root.TryGetProperty("sessionStatsActive", out var sessionStatsActiveValue)
                                     && sessionStatsActiveValue.ValueKind == JsonValueKind.True;
            var sessionElapsedMs = root.TryGetProperty("sessionElapsedMs", out var sessionElapsedValue)
                                   && sessionElapsedValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, sessionElapsedValue.GetDouble())
                : 0;
            var sessionMovingMs = root.TryGetProperty("sessionMovingMs", out var sessionMovingValue)
                                  && sessionMovingValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, sessionMovingValue.GetDouble())
                : 0;
            var sessionAverageSpeed = root.TryGetProperty("sessionAverageSpeed", out var sessionAverageSpeedValue)
                                      && sessionAverageSpeedValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, sessionAverageSpeedValue.GetDouble())
                : 0;
            var sessionMaxSpeed = root.TryGetProperty("sessionMaxSpeed", out var sessionMaxSpeedValue)
                                  && sessionMaxSpeedValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, sessionMaxSpeedValue.GetDouble())
                : 0;
            var explorationVisitedCount = root.TryGetProperty("explorationVisitedCount", out var explorationVisitedValue)
                                          && explorationVisitedValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(explorationVisitedValue.GetInt32(), 0, 400)
                : 0;
            var explorationTotalSectors = root.TryGetProperty("explorationTotalSectors", out var explorationTotalValue)
                                           && explorationTotalValue.ValueKind == JsonValueKind.Number
                ? Math.Clamp(explorationTotalValue.GetInt32(), 1, 400)
                : 400;
            var layersValue = root.TryGetProperty("officialLayers", out var officialLayersValue)
                              && officialLayersValue.ValueKind == JsonValueKind.Object
                ? officialLayersValue
                : default;
            var locationsLayer = ReadNullableBoolean(layersValue, "locations");
            var sanctuariesLayer = ReadNullableBoolean(layersValue, "sanctuaries");
            var migrationLayer = ReadNullableBoolean(layersValue, "migration");
            var patrolLayer = ReadNullableBoolean(layersValue, "patrol");
            var foodLayer = ReadNullableBoolean(layersValue, "food");
            var heatmapLayer = ReadNullableBoolean(layersValue, "heatmap");
            var officialSelfTrail = ReadNullableBoolean(layersValue, "selfTrail");
            var officialFriendTrails = ReadNullableBoolean(layersValue, "friendTrails");
            var shareLocation = ReadNullableBoolean(layersValue, "shareLocation");
            var isolationStylePresent = root.TryGetProperty("isolationStylePresent", out var isolationStyleValue)
                                        && isolationStyleValue.ValueKind == JsonValueKind.True;
            var mapIsolated = root.TryGetProperty("mapIsolated", out var mapIsolatedValue)
                              && mapIsolatedValue.ValueKind == JsonValueKind.True;
            var isolationHiddenCount = root.TryGetProperty("isolationHiddenCount", out var hiddenCountValue)
                                       && hiddenCountValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, hiddenCountValue.GetInt32())
                : 0;
            var isolatedMapWidth = root.TryGetProperty("isolatedMapWidth", out var isolatedWidthValue)
                                   && isolatedWidthValue.ValueKind == JsonValueKind.Number
                ? Math.Max(0, isolatedWidthValue.GetDouble())
                : 0;
            var selfPoseSource = root.TryGetProperty("selfPoseSource", out var selfPoseSourceValue)
                                 && selfPoseSourceValue.ValueKind == JsonValueKind.String
                ? selfPoseSourceValue.GetString() ?? "dom"
                : "dom";

            Dispatcher.Invoke(() =>
            {
                var wasMarkerAvailable = _markerAvailable;
                _markerAvailable = markerAvailable;
                _currentMapScale = mapScale;
                _smartZoomSuspended = smartZoomSuspended;
                _mapScaleBarUnits = scaleBarUnits;
                _mapScaleBarPixels = scaleBarPixels;
                _currentGridReference = selfGridReference;
                _waypointArmed = waypointArmed;
                _waypointActive = waypointActive;
                _currentWaypointDistance = waypointDistance;
                _currentWaypointBearing = waypointBearing;
                _waypointLabel = waypointLabel;
                _waypointKind = waypointKind;
                _waypointTrend = waypointTrend;
                _waypointClosingRate = waypointClosingRate;
                _waypointProgressPercent = waypointProgressPercent;
                _routePlanArmed = routePlanArmed;
                _routePlanActive = routePlanActive;
                _routePlanComplete = routePlanComplete;
                _routePlanSource = routePlanSource;
                _routeStopCount = routeStopCount;
                _routeCurrentIndex = routeCurrentIndex;
                _routePlanTotalDistance = routePlanTotalDistance;
                _routeRemainingDistance = routeRemainingDistance;
                _terrainNetworkReady = terrainNetworkReady;
                _terrainNetworkPathCount = terrainNetworkPathCount;
                _terrainNetworkPointCount = terrainNetworkPointCount;
                _terrainNetworkSourceVersion = terrainNetworkSourceVersion;
                _terrainNetworkLoadedAt = terrainNetworkLoadedAt;
                _terrainCourseDirectDistance = terrainCourseDirectDistance;
                _terrainCourseDistance = terrainCourseDistance;
                _terrainCourseDetourPercent = terrainCourseDetourPercent;
                _terrainCourseAvoidedZoneCount = terrainCourseAvoidedZoneCount;
                _terrainCourseAvoidedWater = terrainCourseAvoidedWater;
                _terrainCourseRoadDistance = terrainCourseRoadDistance;
                _terrainCourseTrailDistance = terrainCourseTrailDistance;
                _terrainCourseLearnedDistance = terrainCourseLearnedDistance;
                _terrainCourseUnknownDistance = terrainCourseUnknownDistance;
                _terrainCourseLongestUnknown = terrainCourseLongestUnknown;
                _terrainCourseUnknownSegmentCount = terrainCourseUnknownSegmentCount;
                _terrainRouteStyle = terrainRouteStyle;
                _terrainGapPolicy = terrainGapPolicy;
                _terrainWaterSafetyEnabled = terrainWaterSafetyEnabled;
                _terrainWaterMaskStatus = terrainWaterMaskStatus;
                _terrainWaterMaskSourceVersion = terrainWaterMaskSourceVersion;
                _terrainCommunityHazardsEnabled = terrainCommunityHazardsEnabled;
                _terrainCommunityHazardStatus = terrainCommunityHazardStatus;
                _terrainCommunityHazardCount = terrainCommunityHazardCount;
                _terrainCommunityHazardSourceVersion =
                    terrainCommunityHazardSourceVersion;
                _terrainCommunityHazardLoadedAt = terrainCommunityHazardLoadedAt;
                _terrainCourseStatus = terrainCourseStatus;
                _learnedPassageCount = learnedPassageCount;
                _learnedPassageActiveCount = learnedPassageActiveCount;
                _learnedPassageStaleCount = learnedPassageStaleCount;
                _learnedPassagePointCount = learnedPassagePointCount;
                _tripRouteObstacleCount = tripRouteObstacleCount;
                _tripRouteInsideObstacle = tripRouteInsideObstacle;
                _navigationEtaMinutes = navigationEtaMinutes;
                _navigationEtaPace = navigationEtaPace;
                _navigationEtaDistance = navigationEtaDistance;
                _navigationEtaSource = navigationEtaSource;
                _routeStops.Clear();
                _routeStops.AddRange(routeStops);
                _measurementArmed = measurementArmed;
                _measurementHasStart = measurementHasStart;
                _measurementActive = measurementActive;
                _measurementDistance = measurementDistance;
                _measurementBearing = measurementBearing;
                _measurementCardinal = measurementCardinal;
                _measurementStartWorldX = measurementStartWorldX;
                _measurementStartWorldY = measurementStartWorldY;
                _measurementEndWorldX = measurementEndWorldX;
                _measurementEndWorldY = measurementEndWorldY;
                _measurementMarkedBoundaryCount = measurementMarkedBoundaryCount;
                _measurementInsideMarkedBoundary = measurementInsideMarkedBoundary;
                _friendRouteName = friendRouteName;
                _packRouteActive = packRouteActive;
                _packOutlierRouteActive = packOutlierRouteActive;
                _pinArmed = pinArmed;
                _pinType = pinType;
                _pinCount = pinCount;
                _activePinId = activePinId;
                _pinRoster.Clear();
                _pinRoster.AddRange(pinRoster);
                _noGoAreaCount = noGoAreaCount;
                _noGoTraceActive = noGoTraceActive;
                _noGoTraceVertexCount = noGoTraceVertexCount;
                _noGoSelectedAreaId = noGoSelectedAreaId;
                _noGoSelectedAreaLabel = noGoSelectedAreaLabel;
                _noGoSelectedAreaVertexCount = noGoSelectedAreaVertexCount;
                _noGoLastStatus = noGoLastStatus;
                _noGoAreaRoster.Clear();
                _noGoAreaRoster.AddRange(noGoAreaRoster);
                _recentRoutes.Clear();
                _recentRoutes.AddRange(recentRoutes);
                _canRouteBack = canRouteBack;
                _sessionStartAvailable = sessionStartAvailable;
                _sessionStartDistance = sessionStartDistance;
                _sessionStartBearing = sessionStartBearing;
                _sessionStartCardinal = sessionStartCardinal;
                _breadcrumbReturnAvailable = breadcrumbReturnAvailable;
                _breadcrumbPointCount = breadcrumbPointCount;
                _breadcrumbDistance = breadcrumbDistance;
                _lastPositionAvailable = lastPositionAvailable;
                _lastPositionAgeMs = lastPositionAgeMs;
                _currentSelfX = selfX;
                _currentSelfY = selfY;
                _currentSelfMapX = selfMapX;
                _currentSelfMapY = selfMapY;
                _currentSelfBearing = selfBearing;
                _currentSelfSpeed = selfSpeed;
                _currentMarkerFreshnessAgeMs = freshnessAgeMs;
                _currentSessionDistance = sessionDistance;
                _sessionStatsActive = sessionStatsActive;
                _sessionElapsedMs = sessionElapsedMs;
                _sessionMovingMs = sessionMovingMs;
                _sessionAverageSpeed = sessionAverageSpeed;
                _sessionMaxSpeed = sessionMaxSpeed;
                _explorationVisitedCount = explorationVisitedCount;
                _explorationTotalSectors = explorationTotalSectors;
                _nearestFriendName = nearestFriendName;
                _nearestFriendDistance = nearestFriendDistance;
                _nearestFriendBearing = nearestFriendBearing;
                _nearestFriendCardinal = nearestFriendCardinal;
                _packFriendCount = packFriendCount;
                _packSpread = packSpread;
                _packSpreadMotion = packSpreadMotion;
                _packSpreadRate = packSpreadRate;
                _packSpreadMotionSampleCount = packSpreadMotionSampleCount;
                _packCourseState = packCourseState;
                _packCourseSpeed = packCourseSpeed;
                _packCourseBearing = packCourseBearing;
                _packCourseCardinal = packCourseCardinal;
                _packCourseSampleCount = packCourseSampleCount;
                _packRadius = packRadius;
                _packCenterDistance = packCenterDistance;
                _packCenterBearing = packCenterBearing;
                _packCenterCardinal = packCenterCardinal;
                _packFarthestFriendName = packFarthestFriendName;
                _packFarthestFriendDistance = packFarthestFriendDistance;
                _packCenterAvailable = packCenterAvailable;
                _encounterPlayerCount = encounterPlayerCount;
                _nearestEncounterDistance = nearestEncounterDistance;
                _nearestEncounterBearing = nearestEncounterBearing;
                _nearestEncounterCardinal = nearestEncounterCardinal;
                _nearestEncounterMotion = nearestEncounterMotion;
                _nearestEncounterRelativeSpeed = nearestEncounterRelativeSpeed;
                _nearestEncounterInterceptSeconds = nearestEncounterInterceptSeconds;
                _nearestEncounterMotionSampleCount = nearestEncounterMotionSampleCount;
                _encounterWithin10 = encounterWithin10;
                _encounterWithin25 = encounterWithin25;
                _encounterWithin50 = encounterWithin50;
                _encounterMemoryTrackCount = encounterMemoryTrackCount;
                _rememberedEncounterCount = rememberedEncounterCount;
                _rememberedEncounterNewestAgeMs = rememberedEncounterNewestAgeMs;
                _nearestRememberedEncounterDistance = nearestRememberedEncounterDistance;
                _nearestRememberedEncounterBearing = nearestRememberedEncounterBearing;
                _nearestRememberedEncounterCardinal = nearestRememberedEncounterCardinal;
                _nearestPlaceName = nearestPlaceName;
                _nearestPlaceDistance = nearestPlaceDistance;
                _nearestPlaceBearing = nearestPlaceBearing;
                _nearestPlaceCardinal = nearestPlaceCardinal;
                _officialLandmarkCount = officialLandmarkCount;
                _visibleLandmarkCount = visibleLandmarkCount;
                _nearestDangerPinId = nearestDangerPinId;
                _nearestDangerLabel = nearestDangerLabel;
                _nearestDangerDistance = nearestDangerDistance;
                _nearestDangerBearing = nearestDangerBearing;
                _nearestDangerCardinal = nearestDangerCardinal;
                _nearestAlertZonePinId = nearestAlertZonePinId;
                _nearestAlertZoneLabel = nearestAlertZoneLabel;
                _nearestAlertZoneDistance = nearestAlertZoneDistance;
                _nearestAlertZoneBearing = nearestAlertZoneBearing;
                _nearestAlertZoneCardinal = nearestAlertZoneCardinal;
                _nearestAlertZoneRadius = nearestAlertZoneRadius;
                _nearestAlertZoneBoundaryDistance = nearestAlertZoneBoundaryDistance;
                _insideAlertZone = insideAlertZone;
                _friendRoster.Clear();
                _friendRoster.AddRange(friendRoster);
                _locationsLayer = locationsLayer;
                _sanctuariesLayer = sanctuariesLayer;
                _migrationLayer = migrationLayer;
                _patrolLayer = patrolLayer;
                _foodLayer = foodLayer;
                _heatmapLayer = heatmapLayer;
                _officialSelfTrail = officialSelfTrail;
                _officialFriendTrails = officialFriendTrails;
                _shareLocation = shareLocation;
                if (!_tacticalMapReadyLogged && markerResponseOk)
                {
                    _tacticalMapReadyLogged = true;
                    AddTacticalEvent(
                        "SYSTEM",
                        "Live map connected",
                        $"Authorized feed active · {authorizedAnimalCount} player marker{(authorizedAnimalCount == 1 ? string.Empty : "s")} visible");
                }
                UpdateFollowButton(following, markerAvailable, centerErrorPx);
                UpdateFreshnessStatus(markerAvailable, freshnessKnown, freshnessAgeMs);
                UpdateFeedDiagnostics(
                    markerResponseCount,
                    markerResponseStatus,
                    markerResponseOk,
                    markerResponseSource,
                    fastPollIntervalMs,
                    fastPollDelayMs,
                    lastResponseIntervalMs,
                    lastFastPollDurationMs,
                    pollControlPatched,
                    markerNetworkCount,
                    pollCallbackCount,
                    pollCallbackRuns,
                    controllerInstallCount,
                    selfPositionAt,
                    selfX,
                    selfY,
                    selfPoseSource,
                    isolationStylePresent,
                    mapIsolated,
                    isolationHiddenCount,
                    isolatedMapWidth);
                UpdateAnimalCount(otherAnimalCount, friendAnimalCount, authorizedAnimalCount);
                UpdateNavigationReadout(markerAvailable);
                UpdateMapScaleBar();
                UpdateMapGridControl();
                UpdateLandmarkLabelDensityControl();
                UpdateOfficialLayerControls();
                UpdateZoomDisplay();
                UpdateSmartFollowControls();
                UpdateWaypointStatus(waypointDistance, waypointBearing, waypointCardinal, waypointLabel);
                UpdateResourceFinder();
                UpdateNearestPlaceContext();
                UpdateSessionStats();
                UpdateTacticalBrief();
                UpdateBreadcrumbTrailControls();
                UpdateExplorationControls();
                UpdateDangerProximity();
                UpdateRoutePlanControls();
                UpdateMeasurementStatus();
                UpdateWaterCrossingCheck();
                UpdateRecoveryControls();
                UpdatePinControls();
                UpdateRecentRoutes();
                UpdatePinLibrary();
                UpdateNoGoAreaControls();
                UpdateEncounterAwareness();
                UpdateFriendProximity();
                UpdateFightCheck();
                UpdateFriendRoster();
                UpdateTripReadiness();
                UpdateRecoveryPrompt(wasMarkerAvailable, markerAvailable);
            });
        }
        catch (JsonException)
        {
            // Ignore messages that do not belong to Isley's bridge protocol.
        }
    }

    private static bool? ReadNullableBoolean(JsonElement parent, string propertyName)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static double? ReadNullableNumber(JsonElement parent, string propertyName) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private void HandlePlayerSnapshotMessage(JsonElement root)
    {
        var state = root.TryGetProperty("state", out var stateValue)
                    && stateValue.ValueKind == JsonValueKind.String
            ? stateValue.GetString() ?? string.Empty
            : string.Empty;
        if (state is "unavailable")
        {
            _playerSnapshot = null;
            _playerSnapshotTransportState = "unavailable";
            _lastLiveDinoSample = null;
            _lastGrowthGateSample = null;
            ClearVitalsTrendSamples();
            RefreshPlayerSnapshotConsumers();
            return;
        }

        if (state is "error")
        {
            _playerSnapshotTransportState = "error";
            RefreshPlayerSnapshotConsumers();
            return;
        }

        var sourceState = state switch
        {
            "live" => PlayerSnapshotSourceState.Live,
            "last-dino" => PlayerSnapshotSourceState.LastKnown,
            _ => PlayerSnapshotSourceState.Unavailable
        };
        if (sourceState == PlayerSnapshotSourceState.Unavailable)
        {
            return;
        }

        var receivedAt = DateTimeOffset.UtcNow;
        var candidate = new PlayerSnapshotRaw(
            sourceState,
            ReadBoundedIdentifier(root, "speciesId", 32),
            ReadBoundedNumber(root, "growthPercent", 0, 100),
            ReadBoundedNumber(root, "healthCurrent", 0, 1_000_000),
            ReadBoundedNumber(root, "healthMaximum", double.Epsilon, 1_000_000),
            ReadBoundedNumber(root, "foodCurrent", 0, 1_000_000),
            ReadBoundedNumber(root, "foodMaximum", double.Epsilon, 1_000_000),
            ReadBoundedNumber(root, "waterCurrent", 0, 1_000_000),
            ReadBoundedNumber(root, "waterMaximum", double.Epsilon, 1_000_000),
            ReadBoundedInteger(root, "primeCompleted", 0, 10),
            ReadBoundedInteger(root, "primeRequired", 1, 10),
            ReadBoundedInteger(root, "primeTotal", 1, 10),
            receivedAt);
        var evaluation = PlayerSnapshotLogic.Evaluate(candidate, receivedAt);
        if (!evaluation.HasValidData)
        {
            _playerSnapshotTransportState = "error";
            _lastLiveDinoSample = null;
            _lastGrowthGateSample = null;
            RefreshPlayerSnapshotConsumers();
            return;
        }

        if (sourceState == PlayerSnapshotSourceState.Live)
        {
            ObserveLiveDinoTransition(evaluation, receivedAt);
            ObserveLiveGrowthGate(evaluation, receivedAt);
            RecordVitalsTrendSample(evaluation, receivedAt);
        }
        else
        {
            _lastLiveDinoSample = null;
            _lastGrowthGateSample = null;
            ClearVitalsTrendSamples();
        }
        _playerSnapshot = candidate;
        _playerSnapshotTransportState = "ok";
        RefreshPlayerSnapshotConsumers();
    }

    private void ObserveLiveDinoTransition(
        PlayerSnapshotEvaluation snapshot,
        DateTimeOffset observedAt)
    {
        if (!_lifeRunActive || _streamerMode || !LiveMapServicesActive)
        {
            _lastLiveDinoSample = null;
            return;
        }

        if (!snapshot.LiveFresh
            || LiveSpeciesBridgeLogic.SpeciesIndex(snapshot.SpeciesId) == 0)
        {
            _lastLiveDinoSample = null;
            return;
        }

        var current = new LiveDinoSample(
            snapshot.SpeciesId,
            snapshot.GrowthPercent,
            observedAt);
        var transition = LifeTransitionLogic.Analyze(_lastLiveDinoSample, current);
        _lastLiveDinoSample = current;
        if (!transition.Detected || _lifeTransitionPending is not null)
        {
            return;
        }

        _lifeTransitionPending = transition;
        _growthGatePending = null;
        _lastGrowthGateSample = null;
        _growthGateUiSignature = string.Empty;
        _lifeTransitionUiSignature = string.Empty;
        _lifeRunUiSignature = string.Empty;
        _nextMoveUiSignature = string.Empty;
        AddTacticalEvent(
            "LIFE",
            "New dinosaur signal",
            $"{transition.PreviousGrowthPercent}% to {transition.CurrentGrowthPercent}% · player review required",
            warning: true);
        UpdateLifeRun(force: true);
        UpdateNextMove(force: true);
        UpdateTacticalBrief();
        _ = ShowHotkeyToastAsync("CHECK NEW DINOSAUR · REVIEW LIFE RUN", false);
    }

    private void ObserveLiveGrowthGate(
        PlayerSnapshotEvaluation snapshot,
        DateTimeOffset observedAt)
    {
        if (!_lifeRunActive || _streamerMode || !LiveMapServicesActive)
        {
            _lastGrowthGateSample = null;
            return;
        }

        if (!snapshot.LiveFresh
            || LiveSpeciesBridgeLogic.SpeciesIndex(snapshot.SpeciesId) == 0)
        {
            _lastGrowthGateSample = null;
            return;
        }

        var current = new LiveGrowthGateSample(
            snapshot.SpeciesId,
            snapshot.GrowthPercent,
            observedAt);
        var analysis = GrowthGateWatchLogic.Analyze(_lastGrowthGateSample, current);
        _lastGrowthGateSample = current;
        if (_lifeTransitionPending?.Detected == true
            || !analysis.Detected
            || (_growthGatePending?.GatePercent ?? 0) >= analysis.GatePercent)
        {
            return;
        }

        _growthGatePending = analysis;
        _growthGateUiSignature = string.Empty;
        _growthPlannerUiSignature = string.Empty;
        _lifeRunUiSignature = string.Empty;
        _nextMoveUiSignature = string.Empty;
        AddTacticalEvent(
            "GROWTH",
            analysis.Heading,
            $"Live {analysis.PreviousGrowthPercent}% to {analysis.CurrentGrowthPercent}% · verify in game",
            warning: analysis.GatePercent >= 75);
        UpdateLifeRun(force: true);
        UpdateNextMove(force: true);
        UpdateTacticalBrief();
        _ = ShowHotkeyToastAsync(
            $"GROWTH GATE {analysis.GatePercent}% · {analysis.Heading}",
            analysis.GatePercent < 75);
    }

    private static double? ReadBoundedNumber(
        JsonElement parent,
        string propertyName,
        double minimum,
        double maximum)
    {
        var value = ReadNullableNumber(parent, propertyName);
        return value is not null
               && double.IsFinite(value.Value)
               && value.Value >= minimum
               && value.Value <= maximum
            ? value
            : null;
    }

    private static int? ReadBoundedInteger(
        JsonElement parent,
        string propertyName,
        int minimum,
        int maximum)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result)
            || result < minimum
            || result > maximum)
        {
            return null;
        }

        return result;
    }

    private static string? ReadBoundedIdentifier(
        JsonElement parent,
        string propertyName,
        int maximumLength)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var normalized = (value.GetString() ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length is > 0
               && normalized.Length <= Math.Clamp(maximumLength, 1, 64)
               && normalized.All(character => character is >= 'a' and <= 'z')
            ? normalized
            : null;
    }

    private void RefreshPlayerSnapshotConsumers()
    {
        ApplyAimCalibrationForSelection(
            useDefaultsWhenMissing: true,
            updatePresentation: true,
            force: false);
        _coreVitalsUiSignature = string.Empty;
        _coreVitalsDecisionSignature = string.Empty;
        _growthPlannerUiSignature = string.Empty;
        UpdateCoreVitals(force: true);
        UpdateDietCoachControls();
        UpdateGrowthClockControls(force: true);
        UpdateNextMove(force: true);
        UpdateTripReadiness(force: true);
        UpdateFightCheck(force: true);
    }

    private void WebView_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (IsTrustedMapOrAuthUri(e.Uri))
        {
            LiveMapWebView.CoreWebView2.Navigate(e.Uri);
            return;
        }

        OpenExternalUri(e.Uri);
    }

    private void WebView_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            SetConnectionStatus("MAP PROCESS STOPPED", Color.FromRgb(242, 96, 61));
            SetLoading(true, "The map renderer stopped. Use reload to restart it.");
        });
    }

    private async Task ApplyMinimapPresentationAsync()
    {
        if (LiveMapWebView.CoreWebView2 is null)
        {
            return;
        }

        const string script = """
            (() => {
              const id = 'the-isle-mapper-style';
              if (document.getElementById(id)) return;
              const style = document.createElement('style');
              style.id = id;
              style.textContent = `
                html, body {
                  width: 100vw !important;
                  height: 100vh !important;
                  min-height: 100vh !important;
                  margin: 0 !important;
                  padding: 0 !important;
                  overflow: hidden !important;
                  background: #080b0e !important;
                }
                .the-isle-mapper-hidden {
                  display: none !important;
                }
                .the-isle-mapper-landmark-hidden {
                  visibility: hidden !important;
                  pointer-events: none !important;
                }
                .the-isle-mapper-map-path {
                  width: 100% !important;
                  max-width: none !important;
                  height: 100% !important;
                  min-width: 0 !important;
                  min-height: 0 !important;
                  margin: 0 !important;
                  padding: 0 !important;
                  border: 0 !important;
                  overflow: hidden !important;
                }
                [data-isle-mapper-map="true"] {
                  position: fixed !important;
                  inset: auto !important;
                  left: 50% !important;
                  top: 50% !important;
                  width: min(100vw, 100vh) !important;
                  height: min(100vw, 100vh) !important;
                  max-width: none !important;
                  --isle-mapper-rotation: 0deg;
                  --isle-mapper-cover-scale: 1;
                  transform: translate(-50%, -50%)
                             rotate(var(--isle-mapper-rotation))
                             scale(var(--isle-mapper-cover-scale)) !important;
                  transform-origin: 50% 50% !important;
                  transition: transform 70ms linear !important;
                  aspect-ratio: 1 / 1 !important;
                  border: 0 !important;
                  border-radius: 0 !important;
                  background: #080b0e !important;
                }
                [data-isle-mapper-ui="true"] {
                  display: block !important;
                  position: fixed !important;
                  inset: 0 !important;
                  width: 100vw !important;
                  height: 100vh !important;
                  z-index: 2147483000 !important;
                  pointer-events: none !important;
                  overflow: visible !important;
                  font-family: "Segoe UI", sans-serif !important;
                }
                [data-isle-mapper-cursor="true"] {
                  position: absolute;
                  min-width: 126px;
                  padding: 8px 10px 7px;
                  border: 1px solid rgba(56, 189, 248, 0.62);
                  border-radius: 7px;
                  color: #e7f7ff;
                  background: rgba(3, 11, 17, 0.92);
                  box-shadow: 0 8px 22px rgba(0, 0, 0, 0.38);
                  backdrop-filter: blur(8px);
                  opacity: 0;
                  transform: translateY(3px);
                  transition: opacity 90ms ease-out, transform 90ms ease-out;
                  pointer-events: none;
                  user-select: none;
                }
                [data-isle-mapper-cursor="true"][data-visible="true"] {
                  opacity: 1;
                  transform: translateY(0);
                }
                .isle-mapper-cursor-grid {
                  color: #7dd3fc;
                  font-size: 13px;
                  font-weight: 900;
                  letter-spacing: 0.08em;
                  line-height: 1.1;
                }
                .isle-mapper-cursor-detail {
                  margin-top: 3px;
                  color: #d7e6ee;
                  font-size: 9px;
                  font-weight: 650;
                  letter-spacing: 0.035em;
                  line-height: 1.25;
                }
                .isle-mapper-cursor-hint {
                  margin-top: 5px;
                  color: #7393a3;
                  font-size: 8px;
                  font-weight: 700;
                  letter-spacing: 0.075em;
                  line-height: 1;
                }
                [data-isle-mapper-quick-actions="true"] {
                  position: absolute;
                  width: 184px;
                  padding: 10px;
                  border: 1px solid rgba(56, 189, 248, 0.7);
                  border-radius: 9px;
                  color: #f8fafc;
                  background: rgba(3, 11, 17, 0.965);
                  box-shadow: 0 14px 34px rgba(0, 0, 0, 0.48);
                  backdrop-filter: blur(10px);
                  pointer-events: auto;
                  user-select: none;
                  transform-origin: top left;
                  animation: isle-mapper-menu-in 110ms ease-out;
                }
                .isle-mapper-quick-title {
                  color: #7dd3fc;
                  font-size: 13px;
                  font-weight: 900;
                  letter-spacing: 0.06em;
                }
                .isle-mapper-quick-detail {
                  margin: 3px 0 8px;
                  color: #91aab8;
                  font-size: 8px;
                  font-weight: 650;
                  line-height: 1.3;
                }
                .isle-mapper-quick-actions {
                  display: grid;
                  gap: 5px;
                }
                .isle-mapper-quick-actions button {
                  width: 100%;
                  min-height: 30px;
                  padding: 6px 9px;
                  border: 1px solid rgba(125, 211, 252, 0.22);
                  border-radius: 6px;
                  color: #e7f7ff;
                  background: rgba(13, 31, 43, 0.92);
                  font: 750 10px/1.1 "Segoe UI", sans-serif;
                  text-align: left;
                  cursor: pointer;
                  transition: border-color 90ms ease-out, background 90ms ease-out,
                              transform 90ms ease-out;
                }
                .isle-mapper-quick-actions button:hover,
                .isle-mapper-quick-actions button:focus-visible {
                  border-color: #38bdf8;
                  background: rgba(19, 60, 82, 0.96);
                  outline: none;
                  transform: translateX(2px);
                }
                .isle-mapper-quick-actions button[data-success="true"] {
                  border-color: #34d399;
                  color: #bbf7d0;
                  background: rgba(6, 78, 59, 0.9);
                }
                [data-isle-mapper-waypoint-cue="true"] {
                  position: absolute;
                  left: 50%;
                  top: 50%;
                  width: 26px;
                  height: 26px;
                  opacity: 0;
                  pointer-events: none;
                  user-select: none;
                  transition: left 130ms ease-out, top 130ms ease-out, opacity 90ms ease-out;
                  will-change: left, top, opacity;
                }
                [data-isle-mapper-waypoint-cue="true"][data-visible="true"] {
                  opacity: 1;
                }
                .isle-mapper-waypoint-cue-arrow {
                  position: absolute;
                  inset: 0;
                  width: 26px;
                  height: 26px;
                  overflow: visible;
                  filter: drop-shadow(0 2px 5px rgba(0, 0, 0, 0.78));
                  transition: transform 120ms ease-out;
                  transform-origin: 50% 50%;
                  animation: isle-mapper-waypoint-pulse 1.45s ease-in-out infinite;
                }
                .isle-mapper-waypoint-cue-copy {
                  position: absolute;
                  display: flex;
                  align-items: center;
                  gap: 6px;
                  max-width: 154px;
                  min-height: 20px;
                  padding: 3px 6px;
                  border: 1px solid rgba(251, 146, 60, 0.65);
                  border-radius: 5px;
                  color: #fff7ed;
                  background: rgba(8, 12, 16, 0.9);
                  box-shadow: 0 6px 16px rgba(0, 0, 0, 0.38);
                  backdrop-filter: blur(7px);
                  font: 750 8px/1 "Segoe UI", sans-serif;
                  letter-spacing: 0.025em;
                  white-space: nowrap;
                }
                .isle-mapper-waypoint-cue-label {
                  min-width: 0;
                  overflow: hidden;
                  text-overflow: ellipsis;
                }
                .isle-mapper-waypoint-cue-distance {
                  flex: 0 0 auto;
                  color: #fdba74;
                  font-weight: 900;
                  letter-spacing: 0.045em;
                }
                [data-isle-mapper-waypoint-cue="true"][data-side="top"]
                  .isle-mapper-waypoint-cue-copy {
                  left: 50%;
                  top: 29px;
                  transform: translateX(-50%);
                }
                [data-isle-mapper-waypoint-cue="true"][data-side="bottom"]
                  .isle-mapper-waypoint-cue-copy {
                  left: 50%;
                  bottom: 29px;
                  transform: translateX(-50%);
                }
                [data-isle-mapper-waypoint-cue="true"][data-side="left"]
                  .isle-mapper-waypoint-cue-copy {
                  left: 29px;
                  top: 50%;
                  transform: translateY(-50%);
                }
                [data-isle-mapper-waypoint-cue="true"][data-side="right"]
                  .isle-mapper-waypoint-cue-copy {
                  right: 29px;
                  top: 50%;
                  transform: translateY(-50%);
                }
                html[data-isley-lite="true"] [data-isle-mapper-map="true"] {
                  transition: none !important;
                }
                html[data-isley-lite="true"] [data-isle-mapper-map="true"] *,
                html[data-isley-lite="true"] [data-isle-mapper-ui="true"] *,
                html[data-isley-lite="true"] [data-isle-mapper-waypoint-cue="true"] * {
                  animation: none !important;
                  transition: none !important;
                  backdrop-filter: none !important;
                }
                @keyframes isle-mapper-menu-in {
                  from { opacity: 0; transform: scale(0.96) translateY(3px); }
                  to { opacity: 1; transform: scale(1) translateY(0); }
                }
                @keyframes isle-mapper-waypoint-pulse {
                  0%, 100% { filter: drop-shadow(0 2px 5px rgba(0, 0, 0, 0.78)); }
                  50% { filter: drop-shadow(0 0 8px rgba(251, 104, 71, 0.78)); }
                }
              `;
              document.head.appendChild(style);
            })();
            """;

        try
        {
            await LiveMapWebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch
        {
            // The live page remains usable even if its presentation changes later.
        }
    }

    private async Task ApplyMapOptionsAsync()
    {
        if (LiveMapWebView.CoreWebView2 is null || !_followControllerInstalled)
        {
            return;
        }

        try
        {
            var rangeRingMode = _rangeRingModes[Math.Clamp(
                _rangeRingModeIndex, 0, _rangeRingModes.Length - 1)];
            var options = JsonSerializer.Serialize(new
            {
                playerLabelsVisible = _playerLabelsVisible,
                friendOnly = _friendOnly,
                markerStyle = _markerStyleModes[_markerStyleIndex],
                headingUp = _headingUp,
                lookAheadEnabled = _lookAheadEnabled,
                smartZoomEnabled = _smartZoomEnabled,
                liteMode = _liteModeEnabled,
                streamerMode = _streamerMode,
                rememberLastPosition = _rememberLastPosition,
                rangeRingsVisible = _rangeRingsVisible,
                rangeRingRadii = new[] { rangeRingMode.Inner, rangeRingMode.Outer },
                mapGridVisible = _mapGridVisible,
                landmarkLabelDensity = _landmarkLabelDensityModes[_landmarkLabelDensityIndex],
                breadcrumbTrailVisible = _breadcrumbTrailVisible,
                explorationEnabled = _explorationEnabled,
                terrainRouteStyle = _terrainRouteStyle,
                terrainGapPolicy = _terrainGapPolicy,
                terrainRouteEvidenceVisible = _terrainRouteConfidenceVisible,
                learnedPassageRoutingEnabled = _learnedPassageRoutingEnabled,
                learnedPassageVisible = _learnedPassageVisible,
                trailSeconds = _trailDurations[_trailDurationIndex],
                encounterMemorySeconds = _encounterMemoryDurations[_encounterMemoryIndex],
                routeAdvanceDistance = Math.Max(3, _arrivalAlertDistances[_arrivalAlertIndex])
            });
            await LiveMapWebView.CoreWebView2.ExecuteScriptAsync(
                $"window.__isley?.configure({options}) ?? false");
        }
        catch
        {
            // Optional local presentation settings must not take down the authorized map.
        }
    }

    private async Task<bool> PageRequiresSteamSignInAsync()
    {
        if (LiveMapWebView.CoreWebView2 is null)
        {
            return true;
        }

        try
        {
            await Task.Delay(300);
            var result = await LiveMapWebView.CoreWebView2.ExecuteScriptAsync(
                "document.body?.innerText?.toLowerCase().includes('sign in with steam to see the map') ?? false");
            return string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private async Task SuspendLiveMapServicesAsync()
    {
        _serverStatusTimer.Stop();
        _serverStatusCancellation?.Cancel();
        _followControllerInstalled = false;
        _playerSnapshot = null;
        _playerSnapshotTransportState = "unavailable";
        ClearLifeTransitionSession();
        ClearVitalsTrendSamples();
        _coreVitalsUiSignature = string.Empty;
        _markerAvailable = false;
        _currentSelfX = null;
        _currentSelfY = null;
        _currentSelfMapX = null;
        _currentSelfMapY = null;
        _currentMarkerFreshnessAgeMs = 0;
        _trackFinderMode = TrackFinderMode.Sound;
        _trackFinderScentTarget = ScentTargetKind.Water;
        _soundBearingFirst = null;
        _soundBearingSecond = null;
        _soundFinderAnalysis = SoundFinderLogic.Analyze(null, null, DateTimeOffset.UtcNow);
        _soundFinderUiSignature = string.Empty;
        _tripReadinessUiSignature = string.Empty;
        _resourceFinderQuery = "salt";
        _resourceFinderResultIndex = 0;
        _resourceFinderUiSignature = string.Empty;
        _activeResourceRouteId = string.Empty;
        _activeResourceRouteLabel = string.Empty;
        _staleAlertActive = false;
        _tacticalMapReadyLogged = false;
        _packFriendCount = 0;
        _encounterPlayerCount = 0;
        _rememberedEncounterCount = 0;
        _recoveryPromptRevision++;
        _recoveryPromptPending = false;
        _recoveryPromptDismissed = true;
        HideRecoveryPrompt();

        if (LiveMapWebView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            await LiveMapWebView.CoreWebView2.ExecuteScriptAsync(
                "window.__isley?.dispose?.(); window.__isley = null; window.__theIsleMapper = null; true");
        }
        catch
        {
            // The universal surface is already privacy-safe even if the old page stopped first.
        }

        try
        {
            LiveMapWebView.CoreWebView2.Navigate("about:blank");
        }
        catch
        {
        // A renderer shutdown cannot expose the covered map surface.
        }
    }

    private static bool IsTrustedMapOrAuthUri(string value)
    {
        if (string.Equals(value, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return uri.Host.Equals(LocalMapHost, StringComparison.OrdinalIgnoreCase)
               && uri.AbsolutePath.StartsWith("/map/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHostOrSubdomain(string host, string expectedDomain) =>
        host.Equals(expectedDomain, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith($".{expectedDomain}", StringComparison.OrdinalIgnoreCase);

    private static bool IsSteamUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (IsHostOrSubdomain(uri.Host, "steamcommunity.com")
            || IsHostOrSubdomain(uri.Host, "steampowered.com"));

    private static bool IsLiveMapUri(Uri uri) =>
        uri.Host.Equals(LocalMapHost, StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith("/map/", StringComparison.OrdinalIgnoreCase);

    private void SetConnectionStatus(string message, Color color)
    {
        if (!LiveMapServicesActive)
        {
            message = "UNIVERSAL SESSION";
            color = Color.FromRgb(56, 189, 248);
        }
        ConnectionStatusText.Text = message;
        ConnectionStatusDot.Fill = new SolidColorBrush(color);
    }

    private static bool RequiresLiveMapServices(string actionId) => actionId is
        "recenter" or "death-marker" or "session-trail" or "exploration" or
        "navigation" or "sound-finder" or "scent-finder" or "resource-finder" or "look-ahead" or "smart-zoom" or "routes" or "recovery" or
        "trip-check" or "water-crossing" or "measure-crossing" or "clear-crossing-check" or
        "players" or "marker-style" or "pack-center" or "pack-outlier" or "pack-alert" or
        "escape-route" or "encounter-hud" or "encounter-alert" or "encounter-memory" or
        "clear-encounter-memory" or "pins" or "alert-zones" or "no-go-areas" or "layers" or
        "server-status" or "copy-server-address" or "map-lighting" or "hud-detail" or
        "focus-modes" or "focus-combat" or "focus-nest" or
        "hub" or "heading" or "grid" or "place-labels" or "rings" or
        "paste-route" or "route-clipboard-coords" or "terrain-course" or "route-style" or "voice-share-route" or
        "waypoint" or "reload" or "preset-navigation" or "preset-survival";

    private void LiveMapWebView_MouseLeave(object sender, MouseEventArgs e) =>
        _ = CancelMapPointerGestureAsync();

    private void LiveMapWebView_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        _ = CancelMapPointerGestureAsync();

    private async Task CancelMapPointerGestureAsync()
    {
        if (!_followControllerInstalled || LiveMapWebView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            await LiveMapWebView.CoreWebView2.ExecuteScriptAsync(
                "window.__isley?.cancelMapPointerGesture?.() ?? false");
        }
        catch (Exception exception) when (exception is InvalidOperationException or COMException)
        {
            // The map may be navigating or shutting down; its gesture state is discarded with it.
        }
    }
}
