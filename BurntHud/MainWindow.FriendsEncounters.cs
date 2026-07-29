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
    // Bounded session encounter history surfaced via the Quick Commands palette.
    private readonly List<(DateTimeOffset At, string Summary)> _encounterHistory = [];

    private void RecordEncounterHistory(string summary)
    {
        _encounterHistory.Add((DateTimeOffset.UtcNow, summary));
        if (_encounterHistory.Count > 10)
        {
            _encounterHistory.RemoveAt(0);
        }
    }

    private async Task CopyEncounterHistoryAsync()
    {
        if (_encounterHistory.Count == 0)
        {
            await ShowHotkeyToastAsync("NO ENCOUNTERS RECORDED THIS SESSION", false);
            return;
        }

        var text = "ISLEY ENCOUNTER HISTORY (THIS SESSION)" + Environment.NewLine
                   + string.Join(
                       Environment.NewLine,
                       _encounterHistory.Select(entry =>
                           $"{entry.At.ToLocalTime():HH:mm} · {entry.Summary}"));
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception exception) when (
            exception is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            await ShowHotkeyToastAsync("CLIPBOARD BUSY · TRY AGAIN", false);
            return;
        }

        await ShowHotkeyToastAsync(
            $"{_encounterHistory.Count} ENCOUNTER{(_encounterHistory.Count == 1 ? string.Empty : "S")} COPIED",
            true);
    }

    private static string FormatEncounterIntercept(double? seconds)
    {
        if (seconds is null)
        {
            return string.Empty;
        }

        if (seconds <= 30)
        {
            return "<30S";
        }

        return $"~{Math.Max(1, (int)Math.Ceiling(seconds.Value / 60))}M";
    }

    private static double? CalculatePackBoundarySeconds(
        double spread,
        double signedRate,
        string motion,
        double threshold)
    {
        if (threshold <= 0 || Math.Abs(signedRate) < 1.5)
        {
            return null;
        }

        var remainingDistance = motion switch
        {
            "spreading" when spread < threshold => threshold - spread,
            "regrouping" when spread > threshold => spread - threshold,
            _ => -1
        };
        if (remainingDistance < 0)
        {
            return null;
        }

        var seconds = remainingDistance / Math.Abs(signedRate) * 60;
        return seconds is >= 0 and <= 900 ? seconds : null;
    }

    private void UpdateEncounterAwareness()
    {
        EncounterEscapeButton.Visibility = Visibility.Collapsed;
        EncounterEscapeButton.IsEnabled = false;
        EncounterEscapeButton.Content = "ESCAPE ROUTE";
        EncounterHudButton.Content = _encounterHudVisible ? "Encounter HUD on" : "Encounter HUD off";
        EncounterHudButton.IsEnabled = !_streamerMode;
        SetToggleButtonState(EncounterHudButton, !_streamerMode && _encounterHudVisible);

        var alertDistance = _encounterAlertDistances[_encounterAlertIndex];
        EncounterAlertButton.Content = alertDistance <= 0
            ? "Encounter alert off"
            : $"Encounter alert {alertDistance:0} MU";
        EncounterAlertButton.ToolTip = alertDistance <= 0
            ? "Nearby authorized-player warnings are off"
            : $"Warn once when a provider-authorized non-friend enters {alertDistance:0} map units";
        EncounterAlertButton.IsEnabled = !_streamerMode;
        SetToggleButtonState(EncounterAlertButton, !_streamerMode && alertDistance > 0);

        var memorySeconds = _encounterMemoryDurations[_encounterMemoryIndex];
        EncounterMemoryButton.Content = memorySeconds <= 0
            ? "Memory · Off"
            : $"Memory · {memorySeconds / 60}m";
        EncounterMemoryButton.ToolTip = memorySeconds <= 0
            ? "Last-seen contact memory is off; selecting a duration starts a private session-only trace"
            : $"Keep fading last-known traces for {memorySeconds / 60} minutes after authorized non-friend markers disappear";
        EncounterMemoryButton.IsEnabled = !_streamerMode;
        SetToggleButtonState(EncounterMemoryButton, !_streamerMode && memorySeconds > 0);
        ClearEncounterMemoryButton.Content = _encounterMemoryTrackCount > 0
            ? $"Clear recent · {_encounterMemoryTrackCount}"
            : "Clear recent";
        ClearEncounterMemoryButton.ToolTip = _encounterMemoryTrackCount > 0
            ? $"Clear {_encounterMemoryTrackCount} session-only authorized contact track{(_encounterMemoryTrackCount == 1 ? string.Empty : "s")}"
            : "No session-only contact history is currently retained";
        ClearEncounterMemoryButton.IsEnabled = !_streamerMode && _encounterMemoryTrackCount > 0;

        if (_streamerMode)
        {
            EncounterStatusText.Text = "Encounter awareness hidden in streamer mode";
            EncounterAwarenessPanel.Visibility = Visibility.Collapsed;
            EncounterDirectionBadge.Visibility = Visibility.Collapsed;
            _encounterAlertInitialized = false;
            _encounterAlertActive = false;
            return;
        }

        var nearestAvailable = _nearestEncounterDistance is not null
                               && _nearestEncounterBearing is not null;
        var alerting = alertDistance > 0
                       && nearestAvailable
                       && _nearestEncounterDistance <= alertDistance;
        var motionAvailable = nearestAvailable
                              && _nearestEncounterMotionSampleCount >= 3
                              && _nearestEncounterRelativeSpeed is not null
                              && _nearestEncounterMotion is "closing" or "opening" or "steady";
        var motionSpeed = Math.Abs(_nearestEncounterRelativeSpeed ?? 0);
        var interceptLabel = FormatEncounterIntercept(_nearestEncounterInterceptSeconds);
        var motionStatus = !motionAvailable
            ? string.Empty
            : _nearestEncounterMotion switch
            {
                "closing" => $"closing {motionSpeed:0.0} MU/min"
                             + (string.IsNullOrEmpty(interceptLabel)
                                 ? string.Empty
                                 : $" · contact {interceptLabel.ToLowerInvariant()} if unchanged"),
                "opening" => $"opening {motionSpeed:0.0} MU/min",
                _ => "holding distance"
            };
        var motionEventSuffix = motionAvailable && _nearestEncounterMotion == "closing"
            ? $" · closing {motionSpeed:0.0} MU/min"
              + (string.IsNullOrEmpty(interceptLabel) ? string.Empty : $" · contact {interceptLabel}")
            : string.Empty;
        if (_encounterPlayerCount > 0)
        {
            EncounterEscapeButton.Visibility = Visibility.Visible;
            EncounterEscapeButton.IsEnabled = nearestAvailable && _markerAvailable;
            if (nearestAvailable)
            {
                var escapeBearing = (_nearestEncounterBearing.GetValueOrDefault() + 180) % 360;
                var escapeCardinal = ToCardinal(escapeBearing);
                EncounterEscapeButton.Content = $"ESCAPE {escapeCardinal}";
                EncounterEscapeButton.ToolTip =
                    $"Create a 75 MU route {escapeCardinal}, away from the latest live authorized contact. " +
                    "Saved Danger zones and traced no-go areas are avoided; no position is predicted.";
            }
            else
            {
                EncounterEscapeButton.Content = "ESCAPE WAITING";
                EncounterEscapeButton.ToolTip = "Waiting for your authorized live position and contact bearing";
            }
        }
        var shouldShow = (_encounterPlayerCount > 0 || _rememberedEncounterCount > 0)
                         && (_hudDetailModeIndex == 0 ? _encounterHudVisible || alerting : alerting);
        EncounterAwarenessPanel.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;

        if (_encounterPlayerCount <= 0)
        {
            if (_encounterAlertInitialized && _encounterAlertActive)
            {
                AddTacticalEvent("PLAYER", "Encounter clear", "No authorized non-friend remains inside the alert radius");
            }
            if (_rememberedEncounterCount > 0)
            {
                var rememberedLabel = _rememberedEncounterCount == 1
                    ? "1 CONTACT"
                    : $"{_rememberedEncounterCount} CONTACTS";
                var ageLabel = FormatElapsedAge(_rememberedEncounterNewestAgeMs ?? 0);
                var rememberedNearestAvailable = _nearestRememberedEncounterDistance is not null
                                                 && _nearestRememberedEncounterBearing is not null;
                EncounterStatusText.Text =
                    $"No live non-friends · {rememberedLabel.ToLowerInvariant()} last seen · newest {ageLabel} ago";
                EncounterHeadingText.Text = "ENCOUNTER · LAST SEEN";
                EncounterHeadingText.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
                EncounterPrimaryText.Text = $"{rememberedLabel} · {ageLabel} AGO";
                EncounterDetailText.Text = rememberedNearestAvailable
                    ? $"{_nearestRememberedEncounterDistance:0.0} MU {_nearestRememberedEncounterCardinal} · LAST KNOWN"
                    : "YOUR POSITION WAITING";
                EncounterDirectionBadge.Visibility = rememberedNearestAvailable
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                EncounterDirectionText.Text = _nearestRememberedEncounterCardinal;
                EncounterDirectionBadge.Background = new SolidColorBrush(Color.FromArgb(0x24, 0xF5, 0x9E, 0x0B));
                EncounterDirectionBadge.BorderBrush =
                    new SolidColorBrush(Color.FromArgb(0x77, 0xF5, 0x9E, 0x0B));
                EncounterAwarenessPanel.BorderBrush =
                    new SolidColorBrush(Color.FromArgb(0x66, 0xF5, 0x9E, 0x0B));
                EncounterAwarenessPanel.ToolTip = rememberedNearestAvailable
                    ? $"Session-only last-known contact · bearing {_nearestRememberedEncounterBearing:000}° · no stale alert is issued"
                    : "Session-only last-known authorized contact; no stale alert is issued";
                _encounterAlertInitialized = false;
                _encounterAlertActive = false;
                return;
            }

            EncounterStatusText.Text = "No authorized non-friend players visible";
            EncounterHeadingText.Text = "ENCOUNTER AWARENESS";
            EncounterPrimaryText.Text = "CLEAR";
            EncounterDetailText.Text = "NO OTHER PLAYERS";
            EncounterDirectionBadge.Visibility = Visibility.Collapsed;
            _encounterAlertInitialized = false;
            _encounterAlertActive = false;
            return;
        }

        var countLabel = _encounterPlayerCount == 1 ? "1 OTHER" : $"{_encounterPlayerCount} OTHERS";
        var rememberedSuffix = _rememberedEncounterCount > 0
            ? $" · {_rememberedEncounterCount} recent"
            : string.Empty;
        EncounterStatusText.Text = nearestAvailable
            ? $"{countLabel} authorized · nearest {_nearestEncounterDistance:0.0} MU {_nearestEncounterCardinal}"
              + (string.IsNullOrEmpty(motionStatus) ? string.Empty : $" · {motionStatus}")
              + rememberedSuffix
            : $"{countLabel} authorized · waiting for your live marker{rememberedSuffix}";
        EncounterHeadingText.Text = alerting
            ? "ENCOUNTER · PLAYER NEARBY"
            : _nearestEncounterMotion switch
            {
                "closing" when motionAvailable => "ENCOUNTER · CLOSING",
                "opening" when motionAvailable => "ENCOUNTER · OPENING",
                "steady" when motionAvailable => "ENCOUNTER · HOLDING",
                _ => "ENCOUNTER · TRACKED"
            };
        EncounterHeadingText.Foreground = alerting
            ? (Brush)FindResource("WarningBrush")
            : new SolidColorBrush(Color.FromRgb(251, 191, 36));
        EncounterAwarenessPanel.BorderBrush = alerting
            ? new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0x8A, 0x3D))
            : new SolidColorBrush(Color.FromArgb(0x80, 0xF5, 0x9E, 0x0B));
        EncounterPrimaryText.Text = nearestAvailable
            ? $"{countLabel} · {_nearestEncounterDistance:0.0} MU {_nearestEncounterCardinal}"
            : $"{countLabel} LIVE";
        EncounterDetailText.Text = nearestAvailable
            ? motionAvailable
                ? _nearestEncounterMotion switch
                {
                    "closing" => $"CLOSING {motionSpeed:0.0}/MIN"
                                 + (string.IsNullOrEmpty(interceptLabel) ? string.Empty : $" · {interceptLabel}"),
                    "opening" => $"OPENING {motionSpeed:0.0}/MIN",
                    _ => "DISTANCE STEADY"
                }
                : $"MOTION {_nearestEncounterMotionSampleCount}/3 · NEAR {_encounterWithin10}/{_encounterWithin25}/{_encounterWithin50}"
            : "YOUR POSITION WAITING";
        EncounterDirectionBadge.Visibility = nearestAvailable ? Visibility.Visible : Visibility.Collapsed;
        EncounterDirectionText.Text = _nearestEncounterCardinal;
        EncounterDirectionBadge.Background = alerting
            ? new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0x8A, 0x3D))
            : motionAvailable && _nearestEncounterMotion == "closing"
                ? new SolidColorBrush(Color.FromArgb(0x32, 0xF5, 0x9E, 0x0B))
                : new SolidColorBrush(Color.FromArgb(0x26, 0x1F, 0x29, 0x37));
        EncounterDirectionBadge.BorderBrush = alerting
            ? new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0x8A, 0x3D))
            : new SolidColorBrush(Color.FromArgb(0x99, 0xFB, 0xBF, 0x24));
        EncounterAwarenessPanel.ToolTip = nearestAvailable
            ? $"Only provider-authorized non-friend markers · nearest bearing {_nearestEncounterBearing:000}°"
              + (motionAvailable
                  ? $" · relative motion from {_nearestEncounterMotionSampleCount} accepted responses · {motionStatus}"
                  : $" · relative motion calibrating from accepted responses ({_nearestEncounterMotionSampleCount}/3)")
              + " · no position is predicted between responses"
            : "Other provider-authorized players are visible; your marker is needed for distance and direction";

        if (!_encounterAlertInitialized)
        {
            _encounterAlertInitialized = true;
            _encounterAlertActive = alerting;
            if (alerting)
            {
                RecordEncounterHistory(
                    $"{countLabel} · nearest {_nearestEncounterDistance:0.0} MU {_nearestEncounterCardinal}{motionEventSuffix}");
                AddTacticalEvent(
                    "PLAYER",
                    "Player nearby",
                    $"{countLabel} · nearest {_nearestEncounterDistance:0.0} MU {_nearestEncounterCardinal} · threshold {alertDistance:0} MU{motionEventSuffix}",
                    warning: true);
            }
        }
        else if (alerting && !_encounterAlertActive)
        {
            _encounterAlertActive = true;
            RecordEncounterHistory(
                $"{countLabel} · nearest {_nearestEncounterDistance:0.0} MU {_nearestEncounterCardinal}{motionEventSuffix}");
            AddTacticalEvent(
                "PLAYER",
                "Player nearby",
                $"{countLabel} · nearest {_nearestEncounterDistance:0.0} MU {_nearestEncounterCardinal} · threshold {alertDistance:0} MU{motionEventSuffix}",
                warning: true);
            SystemSounds.Asterisk.Play();
            var pulse = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.58,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(190),
                AutoReverse = true,
                RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2)
            };
            EncounterAwarenessPanel.BeginAnimation(OpacityProperty, pulse);
        }
        else if (!alerting)
        {
            if (_encounterAlertActive)
            {
                AddTacticalEvent("PLAYER", "Encounter clear", "No authorized non-friend remains inside the alert radius");
            }
            _encounterAlertActive = false;
        }
    }

    private void UpdateFriendProximity()
    {
        FriendRadarButton.Content = _friendRadarVisible ? "Pack HUD on" : "Pack HUD off";
        FriendRadarButton.IsEnabled = !_streamerMode;
        SetToggleButtonState(FriendRadarButton, _friendRadarVisible);
        var alertDistance = _packSpreadAlertDistances[_packSpreadAlertIndex];
        PackSpreadAlertButton.Content = alertDistance <= 0
            ? "Pack alert off"
            : $"Pack alert {alertDistance:0} MU";
        PackSpreadAlertButton.ToolTip = alertDistance <= 0
            ? "Pack-spread warnings are off"
            : $"Warn once when authorized live friends spread beyond {alertDistance:0} map units";
        PackSpreadAlertButton.IsEnabled = !_streamerMode;
        SetToggleButtonState(PackSpreadAlertButton, !_streamerMode && alertDistance > 0);

        var nearestAvailable = !string.IsNullOrWhiteSpace(_nearestFriendName)
                               && _nearestFriendDistance is not null
                               && _nearestFriendBearing is not null;
        var friendRouteActive = !string.IsNullOrWhiteSpace(_friendRouteName);
        var packAvailable = _packFriendCount > 0 && _packCenterAvailable;
        var outlierAvailable = _packFriendCount >= 2
                               && !string.IsNullOrWhiteSpace(_packFarthestFriendName)
                               && _packFarthestFriendDistance is not null;
        RoutePackCenterButton.Content = _packRouteActive
            ? "Stop pack-center route"
            : "Route to pack center";
        RoutePackCenterButton.ToolTip = _packRouteActive
            ? "Stop following the moving center of authorized live friends"
            : packAvailable
                ? $"Follow the moving center of {_packFriendCount} authorized live friend" +
                  (_packFriendCount == 1 ? string.Empty : "s")
                : "At least one authorized live friend is required";
        RoutePackCenterButton.IsEnabled = !_streamerMode && (_packRouteActive || packAvailable);
        SetToggleButtonState(RoutePackCenterButton, _packRouteActive);

        RoutePackOutlierButton.Content = _packOutlierRouteActive
            ? "Stop pack-outlier route"
            : "Route to pack outlier";
        RoutePackOutlierButton.ToolTip = _packOutlierRouteActive
            ? $"Stop following the current formation outlier, {_packFarthestFriendName}"
            : outlierAvailable
                ? $"Follow {_packFarthestFriendName}, currently {_packFarthestFriendDistance:0.0} map units from pack center; the route switches automatically if the outlier changes"
                : "At least two authorized live friends are required to identify a pack outlier";
        RoutePackOutlierButton.IsEnabled = !_streamerMode && (_packOutlierRouteActive || outlierAvailable);
        SetToggleButtonState(RoutePackOutlierButton, _packOutlierRouteActive);

        RouteNearestFriendButton.Content = friendRouteActive
            ? "Stop friend route"
            : "Route to nearest friend";
        RouteNearestFriendButton.ToolTip = friendRouteActive
            ? $"Stop the live route to {_friendRouteName}"
            : nearestAvailable
                ? $"Route continuously to {_nearestFriendName}"
                : "A live self marker and authorized friend are required";
        RouteNearestFriendButton.IsEnabled = !_streamerMode && (friendRouteActive || nearestAvailable);
        SetToggleButtonState(RouteNearestFriendButton, friendRouteActive);
        var spread = Math.Max(0, _packSpread ?? 0);
        var alerting = alertDistance > 0 && spread > alertDistance;
        var detailAllowsPackCard = _hudDetailModeIndex switch
        {
            0 => true,
            1 => alerting || _packRouteActive || _packOutlierRouteActive || friendRouteActive,
            _ => alerting
        };
        var packHudAvailable = _friendRadarVisible && !_streamerMode && _packFriendCount > 0;
        var available = packHudAvailable && detailAllowsPackCard;
        FriendProximityPanel.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        var compactPackHud = available && CurrentHudPriorityPresentation().CompactPackHud;
        PackMotionText.Visibility = compactPackHud ? Visibility.Collapsed : Visibility.Visible;
        PackCourseText.Visibility = compactPackHud ? Visibility.Collapsed : Visibility.Visible;
        NearestFriendText.Visibility = compactPackHud ? Visibility.Collapsed : Visibility.Visible;
        PackOutlierText.Visibility = compactPackHud ? Visibility.Collapsed : Visibility.Visible;
        FriendProximityPanel.Padding = compactPackHud
            ? new Thickness(9, 5, 9, 5)
            : new Thickness(9, 7, 9, 7);
        if (!packHudAvailable)
        {
            PackCohesionHeadingText.Text = "PACK COHESION";
            PackCohesionText.Text = "WAITING";
            PackMotionText.Text = "FORMATION WAITING";
            PackMotionText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            PackCourseText.Text = "PACK COURSE WAITING";
            PackCourseText.Foreground = new SolidColorBrush(Color.FromRgb(103, 232, 249));
            NearestFriendText.Text = "WAITING";
            PackOutlierText.Text = "OUTLIER WAITING";
            _packSpreadAlertInitialized = false;
            _packSpreadAlertActive = false;
            return;
        }
        if (!available)
        {
            if (_packSpreadAlertInitialized && _packSpreadAlertActive && !alerting)
            {
                AddTacticalEvent("PACK", "Pack regrouped", $"Spread returned within {alertDistance:0} MU");
            }
            _packSpreadAlertInitialized = true;
            _packSpreadAlertActive = alerting;
            return;
        }

        var cohesion = spread <= 25 ? "TIGHT" : spread <= 50 ? "LOOSE" : "SCATTERED";
        PackCohesionHeadingText.Text = alerting ? "PACK · SPREAD WARNING" : $"PACK · {cohesion}";
        PackCohesionHeadingText.Foreground = alerting
            ? (Brush)FindResource("WarningBrush")
            : new SolidColorBrush(Color.FromRgb(110, 231, 183));
        FriendProximityPanel.BorderBrush = alerting
            ? new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xB4, 0x4A))
            : new SolidColorBrush(Color.FromArgb(0x80, 0x34, 0xD3, 0x99));
        PackCohesionText.Text = $"{_packFriendCount} FRIEND" +
                                $"{(_packFriendCount == 1 ? string.Empty : "S")} · {spread:0.0} MU SPAN";

        var motionAvailable = _packFriendCount >= 2
                              && _packSpreadMotionSampleCount >= 3
                              && _packSpreadRate is not null
                              && _packSpreadMotion is "spreading" or "regrouping" or "steady";
        var motionRate = Math.Abs(_packSpreadRate ?? 0);
        var boundarySeconds = motionAvailable
            ? CalculatePackBoundarySeconds(spread, _packSpreadRate ?? 0, _packSpreadMotion, alertDistance)
            : null;
        var boundaryTime = FormatEncounterIntercept(boundarySeconds);
        var boundarySuffix = string.IsNullOrEmpty(boundaryTime)
            ? string.Empty
            : _packSpreadMotion == "spreading"
                ? $" · ALERT {boundaryTime}"
                : $" · BACK <{alertDistance:0} {boundaryTime}";
        PackMotionText.Text = _packFriendCount < 2
            ? "FORMATION NEEDS 2 FRIENDS"
            : !motionAvailable
                ? _packSpreadMotionSampleCount < 3
                    ? $"FORMATION CALIBRATING {_packSpreadMotionSampleCount}/3"
                    : "FORMATION ANALYZING"
                : _packSpreadMotion switch
                {
                    "spreading" => $"SPREADING +{motionRate:0.0}/MIN{boundarySuffix}",
                    "regrouping" => $"REGROUPING {motionRate:0.0}/MIN{boundarySuffix}",
                    _ => "FORMATION STEADY"
                };
        PackMotionText.Foreground = _packSpreadMotion switch
        {
            "spreading" => (Brush)FindResource("WarningBrush"),
            "regrouping" => new SolidColorBrush(Color.FromRgb(110, 231, 183)),
            _ => (Brush)FindResource("SecondaryTextBrush")
        };

        var courseAvailable = _packFriendCount >= 2
                              && _packCourseSampleCount >= 3
                              && _packCourseSpeed is not null
                              && _packCourseState is "moving" or "stationary";
        PackCourseText.Text = _packFriendCount < 2
            ? "COURSE NEEDS 2 FRIENDS"
            : !courseAvailable
                ? _packCourseSampleCount < 3
                    ? $"COURSE CALIBRATING {_packCourseSampleCount}/3"
                    : "COURSE ANALYZING"
                : _packCourseState == "moving"
                    ? $"COURSE {_packCourseCardinal} · {_packCourseSpeed:0.0}/MIN"
                    : "COURSE · STATIONARY";
        PackCourseText.Foreground = _packCourseState == "moving"
            ? new SolidColorBrush(Color.FromRgb(103, 232, 249))
            : (Brush)FindResource("SecondaryTextBrush");

        var centerDetail = _packCenterDistance is not null && _packCenterBearing is not null
            ? $"CENTER {_packCenterDistance:0.0} MU {_packCenterCardinal}"
            : "CENTER LIVE · YOU WAITING";
        var nearestDetail = nearestAvailable
            ? $" · NEAR {_nearestFriendName} {_nearestFriendDistance:0.0} MU"
            : string.Empty;
        NearestFriendText.Text = centerDetail + nearestDetail;
        PackOutlierText.Text = outlierAvailable
            ? $"OUTLIER {_packFarthestFriendName} · {_packFarthestFriendDistance:0.0} MU FROM CENTER"
            : "OUTLIER NEEDS 2 FRIENDS";
        var farthestDetail = outlierAvailable
            ? $" · outlier {_packFarthestFriendName} {_packFarthestFriendDistance:0.0} MU from center"
            : string.Empty;
        var motionDetail = motionAvailable
            ? $" · accepted-response trend {_packSpreadMotion} {motionRate:0.0} MU/min" +
              (string.IsNullOrEmpty(boundaryTime)
                  ? string.Empty
                  : $" · boundary {boundaryTime.ToLowerInvariant()} if unchanged")
            : " · formation trend calibrates from 3 accepted responses";
        var courseDetail = courseAvailable
            ? _packCourseState == "moving"
                ? $" · pack course {_packCourseCardinal} {_packCourseBearing:0}° at {_packCourseSpeed:0.0} MU/min"
                : " · pack course stationary"
            : " · pack course calibrates from the same accepted responses";
        FriendProximityPanel.ToolTip = $"Authorized friend pack · radius {(_packRadius ?? 0):0.0} MU" +
                                       farthestDetail + motionDetail + courseDetail +
                                       " · straight-line timing only; no position prediction";

        var eventMotionDetail = motionAvailable
            ? $" · {_packSpreadMotion} {motionRate:0.0} MU/min"
            : string.Empty;

        if (!_packSpreadAlertInitialized)
        {
            _packSpreadAlertInitialized = true;
            _packSpreadAlertActive = alerting;
            if (alerting)
            {
                AddTacticalEvent(
                    "PACK",
                    "Pack spread warning",
                    $"{_packFriendCount} friends · {spread:0.0} MU span · threshold {alertDistance:0} MU{eventMotionDetail}",
                    warning: true);
            }
        }
        else if (alerting && !_packSpreadAlertActive)
        {
            _packSpreadAlertActive = true;
            AddTacticalEvent(
                "PACK",
                "Pack spread warning",
                $"{_packFriendCount} friends · {spread:0.0} MU span · threshold {alertDistance:0} MU{eventMotionDetail}",
                warning: true);
            SystemSounds.Exclamation.Play();
            var pulse = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.58,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(220),
                AutoReverse = true,
                RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2)
            };
            FriendProximityPanel.BeginAnimation(OpacityProperty, pulse);
        }
        else if (!alerting)
        {
            if (_packSpreadAlertActive)
            {
                AddTacticalEvent("PACK", "Pack regrouped", $"Spread returned within {alertDistance:0} MU");
            }
            _packSpreadAlertActive = false;
        }
    }

    private void UpdateFriendRoster()
    {
        EnsureMapWatchlistBridgeInstalled();
        UpdateSteamFriendPicker();
        UpdateSteamFriendWatchlist();
        UpdateFriendGroups();
        UpdateEncounterWatchlist();
        _ = TryAutoFollowSteamFriendAsync();
        var signature = string.Join('|', new[]
        {
            _streamerMode ? "private" : "visible",
            _friendRouteName,
            _currentSelfX is not null && _currentSelfY is not null ? "self" : "offline",
            string.Join(';', _friendRoster.Select(friend =>
                $"{friend.Name}:{friend.Distance?.ToString("0.0", CultureInfo.InvariantCulture) ?? "-"}:" +
                $"{friend.Bearing?.ToString("0", CultureInfo.InvariantCulture) ?? "-"}:{friend.Cardinal}"))
        });
        if (string.Equals(signature, _friendRosterUiSignature, StringComparison.Ordinal))
        {
            return;
        }

        _friendRosterUiSignature = signature;
        FriendRosterPanel.Children.Clear();
        if (_streamerMode)
        {
            FriendRosterStatus.Text = "Friend roster hidden in streamer mode";
            FriendRosterStatus.Foreground = (Brush)FindResource("SecondaryTextBrush");
            FriendRosterPanel.Visibility = Visibility.Collapsed;
            return;
        }

        FriendRosterPanel.Visibility = Visibility.Visible;
        FriendRosterStatus.Foreground = (Brush)FindResource("SecondaryTextBrush");
        if (_friendRoster.Count == 0)
        {
            FriendRosterStatus.Text = "No authorized friends are live";
            return;
        }

        const int visibleLimit = 8;
        var visibleFriends = _friendRoster.Take(visibleLimit).ToList();
        FriendRosterStatus.Text = _friendRoster.Count > visibleLimit
            ? $"{_friendRoster.Count} authorized friends · showing nearest {visibleLimit}"
            : $"{_friendRoster.Count} authorized friend{(_friendRoster.Count == 1 ? string.Empty : "s")} · select to route";

        foreach (var friend in visibleFriends)
        {
            var displayName = friend.Name.Length <= 18 ? friend.Name : $"{friend.Name[..17]}…";
            var liveDetail = friend.Distance is not null && friend.Bearing is not null
                ? $"{friend.Distance:0.0} MU {friend.Cardinal}"
                : "LIVE";
            var button = new Button
            {
                Style = (Style)FindResource("DrawerButton"),
                Height = 30,
                Margin = new Thickness(0, 0, 0, 4),
                FontSize = 9,
                Content = $"{displayName} · {liveDetail}",
                Tag = friend.Name,
                ToolTip = friend.Distance is not null && friend.Bearing is not null
                    ? $"Route to {friend.Name} · {friend.Distance:0.0} map units · " +
                      $"{friend.Cardinal} {friend.Bearing:000}°"
                    : $"Route to {friend.Name}; your live marker is needed for distance and bearing"
            };
            button.Click += FriendRosterButton_Click;
            SetToggleButtonState(
                button,
                string.Equals(friend.Name, _friendRouteName, StringComparison.Ordinal));
            FriendRosterPanel.Children.Add(button);
        }
    }

    private void UpdateSteamFriendPicker()
    {
        if (SteamFriendLiveFriendPicker is null)
        {
            return;
        }

        var liveNames = !_streamerMode && LiveMapServicesActive
            ? _friendRoster
                .Select(friend => SteamFriendLogic.NormalizeMapName(friend.Name))
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToList()
            : [];
        var signature = string.Join('|',
            _streamerMode,
            LiveMapServicesActive,
            string.Join(';', liveNames));
        if (string.Equals(signature, _steamFriendPickerSignature, StringComparison.Ordinal))
        {
            return;
        }

        _steamFriendPickerSignature = signature;
        _updatingSteamFriendPicker = true;
        try
        {
            var currentName = SteamFriendLiveFriendPicker.SelectedItem as string;
            SteamFriendLiveFriendPicker.Items.Clear();
            var placeholder = _streamerMode
                ? "HIDDEN IN STREAMER MODE"
                : !LiveMapServicesActive
            ? "LIVE PICKER · LIVE MAP ONLY"
                    : liveNames.Count == 0
                        ? "NO AUTHORIZED FRIENDS LIVE"
                        : "CHOOSE A LIVE FRIEND…";
            SteamFriendLiveFriendPicker.Items.Add(placeholder);
            foreach (var name in liveNames)
            {
                SteamFriendLiveFriendPicker.Items.Add(name);
            }

            var restoredIndex = currentName is null
                ? -1
                : liveNames.FindIndex(name =>
                    string.Equals(name, currentName, StringComparison.OrdinalIgnoreCase));
            SteamFriendLiveFriendPicker.SelectedIndex = restoredIndex >= 0 ? restoredIndex + 1 : 0;
            SteamFriendLiveFriendPicker.IsEnabled = liveNames.Count > 0;
            SteamFriendLiveFriendPicker.ToolTip = liveNames.Count > 0
                ? "Choose an authorized live friend to fill the exact map name"
                : LiveMapServicesActive
                    ? "No authorized friend markers are currently live"
            : "Live friend selection is available in Live Map mode";
        }
        finally
        {
            _updatingSteamFriendPicker = false;
        }
    }

    private void SteamFriendLiveFriendPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSteamFriendPicker
            || _streamerMode
            || SteamFriendLiveFriendPicker.SelectedIndex <= 0
            || SteamFriendLiveFriendPicker.SelectedItem is not string liveName)
        {
            return;
        }

        SteamFriendMapNameInputBox.Text = liveName;
        SteamFriendWatchStatusText.Text = $"Selected authorized live friend · {liveName}";
        SteamFriendWatchStatusText.Foreground = (Brush)FindResource("SuccessBrush");
    }

    private SteamFriendAutoFollowDecision CurrentSteamFriendAutoFollowDecision()
    {
        var autoEntry = _steamFriendWatchlist.FirstOrDefault(entry =>
            string.Equals(entry.Id, _autoFollowSteamFriendWatchId, StringComparison.Ordinal));
        var routeBusy = _waypointActive
                        || _waypointArmed
                        || _routePlanArmed
                        || _routePlanActive
                        || _measurementArmed
                        || _pinArmed;
        return SteamFriendLogic.EvaluateAutoFollow(
            _autoFollowSteamFriendWatchId,
            autoEntry,
            _friendRoster.Select(friend => friend.Name),
            _streamerMode,
            LiveMapServicesActive,
            routeBusy,
            _friendRouteName);
    }

    private async Task TryAutoFollowSteamFriendAsync()
    {
        var decision = CurrentSteamFriendAutoFollowDecision();
        if (!decision.ShouldStart
            || _steamAutoFollowCommandPending
            || DateTimeOffset.UtcNow - _steamAutoFollowLastAttemptAt < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _steamAutoFollowCommandPending = true;
        _steamAutoFollowLastAttemptAt = DateTimeOffset.UtcNow;
        try
        {
            var routed = await ExecuteMapperCommandAsync(
                $"window.__isley?.routeToFriend({JsonSerializer.Serialize(decision.LiveName)}) ?? false");
            _steamFriendWatchUiSignature = string.Empty;
            UpdateSteamFriendWatchlist();
            if (routed && !_streamerMode)
            {
                await ShowHotkeyToastAsync(
                    $"AUTO FOLLOWING {decision.LiveName.ToUpperInvariant()}",
                    true);
            }
        }
        finally
        {
            _steamAutoFollowCommandPending = false;
        }
    }

    private void UpdateSteamFriendWatchlist()
    {
        if (SteamFriendWatchlistPanel is null || SteamFriendWatchStatusText is null)
        {
            return;
        }

        var selected = _steamFriendWatchlist.FirstOrDefault(entry =>
            string.Equals(entry.Id, _selectedSteamFriendWatchId, StringComparison.Ordinal));
        if (selected is null && _steamFriendWatchlist.Count > 0)
        {
            selected = _steamFriendWatchlist[0];
            _selectedSteamFriendWatchId = selected.Id;
        }

        var signature = string.Join('|',
            _streamerMode,
            LiveMapServicesActive,
            _friendRouteName,
            _waypointActive,
            _waypointArmed,
            _routePlanActive,
            _measurementArmed,
            _selectedSteamFriendWatchId,
            _autoFollowSteamFriendWatchId,
            _steamAutoFollowCommandPending,
            _removeSteamFriendWatchConfirmationPending,
            string.Join(';', _steamFriendWatchlist.Select(entry =>
            {
                var live = _friendRoster.FirstOrDefault(friend =>
                    string.Equals(friend.Name, entry.MapName, StringComparison.OrdinalIgnoreCase));
                return $"{entry.Id}:{entry.MapName}:{live?.Distance?.ToString("0.0", CultureInfo.InvariantCulture) ?? "-"}:" +
                       $"{live?.Bearing?.ToString("0", CultureInfo.InvariantCulture) ?? "-"}";
            })));
        if (string.Equals(signature, _steamFriendWatchUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _steamFriendWatchUiSignature = signature;

        SteamFriendWatchlistPanel.Children.Clear();
        if (_streamerMode)
        {
            SteamFriendWatchStatusText.Text = "Steam friend watch hidden in streamer mode";
            SteamFriendWatchStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            SteamFriendWatchContentPanel.Visibility = Visibility.Collapsed;
            return;
        }

        SteamFriendWatchContentPanel.Visibility = Visibility.Visible;
        SteamFriendWatchStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
        if (_steamFriendWatchlist.Count == 0)
        {
            SteamFriendWatchStatusText.Text =
                "No watched friends · add a Steam profile and its exact authorized map name";
            TrackSteamFriendButton.IsEnabled = false;
            AutoTrackSteamFriendButton.IsEnabled = false;
            AutoTrackSteamFriendButton.Content = "AUTO OFF";
            OpenSteamFriendButton.IsEnabled = false;
            RemoveSteamFriendWatchButton.IsEnabled = false;
            RemoveSteamFriendWatchButton.Content = "REMOVE";
            return;
        }

        var liveCount = 0;
        foreach (var entry in _steamFriendWatchlist)
        {
            var live = LiveMapServicesActive
                ? _friendRoster.FirstOrDefault(friend =>
                    string.Equals(friend.Name, entry.MapName, StringComparison.OrdinalIgnoreCase))
                : null;
            if (live is not null)
            {
                liveCount++;
            }

            var selectedEntry = string.Equals(entry.Id, _selectedSteamFriendWatchId, StringComparison.Ordinal);
            var autoEntry = string.Equals(entry.Id, _autoFollowSteamFriendWatchId, StringComparison.Ordinal);
            var displayName = entry.MapName.Length <= 18 ? entry.MapName : $"{entry.MapName[..17]}…";
            var state = !LiveMapServicesActive
                ? "PAUSED"
                : live is null
                    ? "WAITING"
                    : live.Distance is not null && live.Bearing is not null
                        ? $"{live.Distance:0.0} MU {live.Cardinal}"
                        : "LIVE";
            var autoDecision = autoEntry
                ? CurrentSteamFriendAutoFollowDecision()
                : new SteamFriendAutoFollowDecision(SteamFriendAutoFollowState.Off, string.Empty);
            if (autoEntry)
            {
                state = autoDecision.State switch
                {
                    SteamFriendAutoFollowState.ServerPaused => "AUTO PAUSED",
                    SteamFriendAutoFollowState.WaitingForFriend => "AUTO WAITING",
                    SteamFriendAutoFollowState.RouteBusy => "AUTO · ROUTE BUSY",
                    SteamFriendAutoFollowState.Following when live?.Distance is not null =>
                        $"AUTO · {live.Distance:0.0} MU {live.Cardinal}",
                    SteamFriendAutoFollowState.Following => "AUTO FOLLOW",
                    SteamFriendAutoFollowState.Ready => "AUTO STARTING",
                    _ => state
                };
            }
            var button = new Button
            {
                Style = (Style)FindResource("DrawerButton"),
                Height = 30,
                Margin = new Thickness(0, 0, 0, 4),
                FontSize = 9,
                Content = $"{(autoEntry ? "◎" : live is null ? "○" : "●")} {displayName} · {state}",
                Tag = entry.Id,
                ToolTip = autoEntry && autoDecision.State == SteamFriendAutoFollowState.RouteBusy
                    ? $"{entry.MapName} · auto-follow is armed and preserving the current route"
                    : autoEntry && autoDecision.State == SteamFriendAutoFollowState.WaitingForFriend
                        ? $"{entry.MapName} · auto-follow is armed and waiting for an authorized live marker"
                    : !LiveMapServicesActive
                ? $"{entry.MapName} · authorized map tracking is available in Live Map mode"
                    : live is null
                        ? $"{entry.MapName} · waiting for an authorized live friend marker"
                        : $"{entry.MapName} · authorized live friend · select, then track"
            };
            button.Click += SteamFriendWatchEntryButton_Click;
            SetToggleButtonState(button, selectedEntry);
            SteamFriendWatchlistPanel.Children.Add(button);
        }

        selected = _steamFriendWatchlist.FirstOrDefault(entry =>
            string.Equals(entry.Id, _selectedSteamFriendWatchId, StringComparison.Ordinal));
        var selectedLive = selected is null || !LiveMapServicesActive
            ? null
            : _friendRoster.FirstOrDefault(friend =>
                string.Equals(friend.Name, selected.MapName, StringComparison.OrdinalIgnoreCase));
        var routeActive = selected is not null
                          && string.Equals(selected.MapName, _friendRouteName, StringComparison.OrdinalIgnoreCase);
        var autoSelected = selected is not null
                           && string.Equals(
                               selected.Id,
                               _autoFollowSteamFriendWatchId,
                               StringComparison.Ordinal);
        var autoDecisionForSelected = autoSelected
            ? CurrentSteamFriendAutoFollowDecision()
            : new SteamFriendAutoFollowDecision(SteamFriendAutoFollowState.Off, string.Empty);
        var autoEntryName = _steamFriendWatchlist.FirstOrDefault(entry =>
            string.Equals(entry.Id, _autoFollowSteamFriendWatchId, StringComparison.Ordinal))?.MapName;
        SteamFriendWatchStatusText.Text =
            $"{_steamFriendWatchlist.Count} WATCHED · {liveCount} LIVE" +
            (selected is null ? string.Empty : $" · SELECTED {selected.MapName}") +
            (string.IsNullOrWhiteSpace(autoEntryName) ? string.Empty : $" · AUTO {autoEntryName}");
        TrackSteamFriendButton.Content = routeActive ? "STOP" : "TRACK";
        TrackSteamFriendButton.IsEnabled = selectedLive is not null || routeActive;
        TrackSteamFriendButton.ToolTip = routeActive
            ? $"Stop the live route to {selected?.MapName}"
            : selectedLive is null
            ? LiveMapServicesActive
                ? "The selected friend needs an authorized live map marker before tracking"
            : "Switch to Live Map mode to track authorized live friends"
            : $"Route continuously to {selectedLive.Name}";
        AutoTrackSteamFriendButton.Content = autoSelected ? "AUTO ON" : "AUTO OFF";
        AutoTrackSteamFriendButton.IsEnabled = selected is not null;
        AutoTrackSteamFriendButton.ToolTip = autoSelected
            ? autoDecisionForSelected.State switch
            {
                SteamFriendAutoFollowState.ServerPaused =>
            "Auto-follow is armed and will resume in Live Map mode",
                SteamFriendAutoFollowState.WaitingForFriend =>
                    "Auto-follow is armed and waiting for this authorized friend marker",
                SteamFriendAutoFollowState.RouteBusy =>
                    "Auto-follow is armed; the current route is protected until navigation is clear",
                SteamFriendAutoFollowState.Following =>
                    "Auto-follow is active; select AUTO ON to disarm it without clearing the current route",
                SteamFriendAutoFollowState.Ready => "Auto-follow is starting",
                _ => "Select to turn auto-follow off"
            }
            : "Arm auto-follow for this watch; Isley waits for a live marker and free navigation";
        OpenSteamFriendButton.IsEnabled = selected is not null;
        RemoveSteamFriendWatchButton.IsEnabled = selected is not null;
        RemoveSteamFriendWatchButton.Content = _removeSteamFriendWatchConfirmationPending
            ? "SURE?"
            : "REMOVE";
        SetToggleButtonState(TrackSteamFriendButton, routeActive);
        SetToggleButtonState(AutoTrackSteamFriendButton, autoSelected);
    }

    private async Task SaveSteamFriendWatchAsync(bool openSteamAdd, bool armAutoFollow)
    {
        if (_streamerMode)
        {
            return;
        }

        if (!SteamFriendLogic.TryCreateEntry(
                SteamFriendProfileInputBox.Text,
                SteamFriendMapNameInputBox.Text,
                DateTimeOffset.UtcNow,
                out var entry,
                out var error))
        {
            SteamFriendWatchStatusText.Text = error;
            SteamFriendWatchStatusText.Foreground = (Brush)FindResource("WarningBrush");
            await ShowHotkeyToastAsync("CHECK STEAM FRIEND DETAILS", false);
            return;
        }

        var updated = SteamFriendLogic.Upsert(
            _steamFriendWatchlist,
            entry,
            DateTimeOffset.UtcNow);
        _steamFriendWatchlist.Clear();
        _steamFriendWatchlist.AddRange(updated);
        entry = _steamFriendWatchlist[0];
        if (!_steamFriendWatchlist.Any(item =>
                string.Equals(item.Id, _autoFollowSteamFriendWatchId, StringComparison.Ordinal)))
        {
            _autoFollowSteamFriendWatchId = string.Empty;
        }
        _selectedSteamFriendWatchId = entry.Id;
        if (armAutoFollow)
        {
            _autoFollowSteamFriendWatchId = entry.Id;
        }
        _removeSteamFriendWatchConfirmationPending = false;
        _removeSteamFriendWatchConfirmationRevision++;
        _steamFriendWatchUiSignature = string.Empty;
        SteamFriendProfileInputBox.Text = string.Empty;
        SteamFriendMapNameInputBox.Text = string.Empty;
        _updatingSteamFriendPicker = true;
        SteamFriendLiveFriendPicker.SelectedIndex = SteamFriendLiveFriendPicker.Items.Count > 0 ? 0 : -1;
        _updatingSteamFriendPicker = false;
        var steamOpened = !openSteamAdd || OpenSteamFriendTarget(entry, addFlow: true);
        SaveSettings();
        UpdateSteamFriendWatchlist();
        await ShowHotkeyToastAsync(
            openSteamAdd
                ? steamOpened
                    ? "STEAM ADD OPENED · AUTO-FOLLOW ARMED"
                    : "AUTO-FOLLOW ARMED · STEAM COULD NOT OPEN"
                : "STEAM FRIEND WATCH SAVED",
            steamOpened);
        if (armAutoFollow)
        {
            await TryAutoFollowSteamFriendAsync();
        }
    }

    private static bool OpenSteamFriendTarget(SteamFriendWatchEntry entry, bool addFlow)
    {
        var clientUri = addFlow
            ? SteamFriendLogic.BuildAddClientUri(entry)
              ?? SteamFriendLogic.BuildProfileClientUri(entry)
            : SteamFriendLogic.BuildProfileClientUri(entry);
        if (!string.IsNullOrWhiteSpace(clientUri))
        {
            try
            {
                Process.Start(new ProcessStartInfo(clientUri) { UseShellExecute = true });
                return true;
            }
            catch
            {
                // Fall back to the canonical HTTPS profile below.
            }
        }

        if (!SteamFriendLogic.TryParseTarget(entry.ProfileUrl, out var target))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target.CanonicalProfileUrl) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async void AddSteamFriendWatchButton_Click(object sender, RoutedEventArgs e) =>
        await SaveSteamFriendWatchAsync(openSteamAdd: true, armAutoFollow: true);

    private async void SaveSteamFriendWatchButton_Click(object sender, RoutedEventArgs e) =>
        await SaveSteamFriendWatchAsync(openSteamAdd: false, armAutoFollow: false);

    private void SteamFriendWatchEntryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || sender is not Button { Tag: string entryId })
        {
            return;
        }

        _selectedSteamFriendWatchId = entryId;
        _removeSteamFriendWatchConfirmationPending = false;
        _removeSteamFriendWatchConfirmationRevision++;
        _steamFriendWatchUiSignature = string.Empty;
        UpdateSteamFriendWatchlist();
        SaveSettings();
    }

    private async void TrackSteamFriendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || !LiveMapServicesActive)
        {
            return;
        }

        var selected = _steamFriendWatchlist.FirstOrDefault(entry =>
            string.Equals(entry.Id, _selectedSteamFriendWatchId, StringComparison.Ordinal));
        var live = selected is null
            ? null
            : _friendRoster.FirstOrDefault(friend =>
                string.Equals(friend.Name, selected.MapName, StringComparison.OrdinalIgnoreCase));
        var stopping = selected is not null
                       && string.Equals(
                           selected.MapName,
                           _friendRouteName,
                           StringComparison.OrdinalIgnoreCase);
        if (live is null && !stopping)
        {
            SteamFriendWatchStatusText.Text = "Selected friend is not currently available as an authorized live marker";
            SteamFriendWatchStatusText.Foreground = (Brush)FindResource("WarningBrush");
            await ShowHotkeyToastAsync("WATCHED FRIEND NOT LIVE", false);
            return;
        }

        if (stopping
            && string.Equals(selected?.Id, _autoFollowSteamFriendWatchId, StringComparison.Ordinal))
        {
            _autoFollowSteamFriendWatchId = string.Empty;
            SaveSettings();
        }
        var command = stopping
            ? "window.__isley?.clearWaypoint() ?? false"
            : $"window.__isley?.routeToFriend({JsonSerializer.Serialize(live!.Name)}) ?? false";
        var routed = await ExecuteMapperCommandAsync(command);
        _steamFriendWatchUiSignature = string.Empty;
        UpdateSteamFriendWatchlist();
        await ShowHotkeyToastAsync(
            routed
                ? stopping
                    ? "FRIEND TRACK STOPPED"
                    : $"TRACKING {live!.Name.ToUpperInvariant()}"
                : "FRIEND TRACK UNAVAILABLE",
            routed);
    }

    private async void AutoTrackSteamFriendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        var selected = _steamFriendWatchlist.FirstOrDefault(entry =>
            string.Equals(entry.Id, _selectedSteamFriendWatchId, StringComparison.Ordinal));
        if (selected is null)
        {
            return;
        }

        var disabling = string.Equals(
            selected.Id,
            _autoFollowSteamFriendWatchId,
            StringComparison.Ordinal);
        _autoFollowSteamFriendWatchId = disabling ? string.Empty : selected.Id;
        _steamFriendWatchUiSignature = string.Empty;
        SaveSettings();
        UpdateSteamFriendWatchlist();
        if (disabling)
        {
            await ShowHotkeyToastAsync("STEAM FRIEND AUTO-FOLLOW OFF", true);
            return;
        }

        var decision = CurrentSteamFriendAutoFollowDecision();
        await ShowHotkeyToastAsync(
            decision.State switch
            {
                SteamFriendAutoFollowState.WaitingForFriend => "AUTO-FOLLOW ARMED · WAITING FOR FRIEND",
            SteamFriendAutoFollowState.ServerPaused => "AUTO-FOLLOW ARMED · LIVE MAP ONLY",
                SteamFriendAutoFollowState.RouteBusy => "AUTO-FOLLOW ARMED · CURRENT ROUTE PROTECTED",
                SteamFriendAutoFollowState.Following => "AUTO-FOLLOW ALREADY ACTIVE",
                _ => "STEAM FRIEND AUTO-FOLLOW ARMED"
            },
            true);
        await TryAutoFollowSteamFriendAsync();
    }

    private async void OpenSteamFriendButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _steamFriendWatchlist.FirstOrDefault(entry =>
            string.Equals(entry.Id, _selectedSteamFriendWatchId, StringComparison.Ordinal));
        var opened = selected is not null && OpenSteamFriendTarget(selected, addFlow: false);
        await ShowHotkeyToastAsync(opened ? "STEAM PROFILE OPENED" : "STEAM PROFILE UNAVAILABLE", opened);
    }

    private async void RemoveSteamFriendWatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        var selected = _steamFriendWatchlist.FirstOrDefault(entry =>
            string.Equals(entry.Id, _selectedSteamFriendWatchId, StringComparison.Ordinal));
        if (selected is null)
        {
            return;
        }

        if (!_removeSteamFriendWatchConfirmationPending)
        {
            _removeSteamFriendWatchConfirmationPending = true;
            var revision = ++_removeSteamFriendWatchConfirmationRevision;
            _steamFriendWatchUiSignature = string.Empty;
            UpdateSteamFriendWatchlist();
            await Task.Delay(3000);
            if (IsLoaded
                && revision == _removeSteamFriendWatchConfirmationRevision
                && _removeSteamFriendWatchConfirmationPending)
            {
                _removeSteamFriendWatchConfirmationPending = false;
                _steamFriendWatchUiSignature = string.Empty;
                UpdateSteamFriendWatchlist();
            }
            return;
        }

        _removeSteamFriendWatchConfirmationPending = false;
        _removeSteamFriendWatchConfirmationRevision++;
        var routeActive = string.Equals(
            selected.MapName,
            _friendRouteName,
            StringComparison.OrdinalIgnoreCase);
        if (string.Equals(selected.Id, _autoFollowSteamFriendWatchId, StringComparison.Ordinal))
        {
            _autoFollowSteamFriendWatchId = string.Empty;
        }
        _steamFriendWatchlist.RemoveAll(entry =>
            string.Equals(entry.Id, selected.Id, StringComparison.Ordinal));
        _selectedSteamFriendWatchId = _steamFriendWatchlist.FirstOrDefault()?.Id ?? string.Empty;
        if (routeActive)
        {
            await ExecuteMapperCommandAsync("window.__isley?.clearWaypoint() ?? false");
        }
        _steamFriendWatchUiSignature = string.Empty;
        SaveSettings();
        UpdateSteamFriendWatchlist();
        await ShowHotkeyToastAsync("STEAM FRIEND WATCH REMOVED", true);
    }

    private void FriendRadarButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        _friendRadarVisible = !_friendRadarVisible;
        UpdateFriendProximity();
        UpdateHudSurfaceControls();
        SaveSettings();
    }

    private void EncounterHudButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        _encounterHudVisible = !_encounterHudVisible;
        UpdateEncounterAwareness();
        UpdateHudSurfaceControls();
        SaveSettings();
    }

    private void EncounterAlertButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        _encounterAlertIndex = (_encounterAlertIndex + 1) % _encounterAlertDistances.Length;
        _encounterAlertInitialized = false;
        _encounterAlertActive = false;
        UpdateEncounterAwareness();
        SaveSettings();
    }

    private async void EncounterMemoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        _encounterMemoryIndex = (_encounterMemoryIndex + 1) % _encounterMemoryDurations.Length;
        if (_encounterMemoryDurations[_encounterMemoryIndex] <= 0)
        {
            _encounterMemoryTrackCount = 0;
            _rememberedEncounterCount = 0;
            _rememberedEncounterNewestAgeMs = null;
            _nearestRememberedEncounterDistance = null;
            _nearestRememberedEncounterBearing = null;
            _nearestRememberedEncounterCardinal = string.Empty;
        }
        UpdateEncounterAwareness();
        SaveSettings();
        await EnsureFollowControllerAsync();
        await ApplyMapOptionsAsync();
    }

    private async void ClearEncounterMemoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || _encounterMemoryTrackCount <= 0)
        {
            return;
        }

        if (!await ExecuteMapperCommandAsync(
                "window.__isley?.clearEncounterMemory() ?? false"))
        {
            return;
        }

        _encounterMemoryTrackCount = 0;
        _rememberedEncounterCount = 0;
        _rememberedEncounterNewestAgeMs = null;
        _nearestRememberedEncounterDistance = null;
        _nearestRememberedEncounterBearing = null;
        _nearestRememberedEncounterCardinal = string.Empty;
        UpdateEncounterAwareness();
    }

    private async void EncounterEscapeButton_Click(object sender, RoutedEventArgs e)
    {
        await StartEscapeRouteAsync();
    }

    private async Task StartEscapeRouteAsync()
    {
        if (_streamerMode)
        {
            await ShowHotkeyToastAsync("ESCAPE ROUTE HIDDEN IN STREAMER MODE", false);
            return;
        }

        var result = await ExecuteMapperJsonAsync<EscapeRouteResult>(
            "window.__isley?.startEscapeRoute() ?? null");
        if (result?.Ok is true)
        {
            var distance = Math.Max(0, result.Distance ?? 0);
            var cardinal = string.IsNullOrWhiteSpace(result.Cardinal)
                ? result.Bearing is double bearing ? ToCardinal(bearing) : "AWAY"
                : result.Cardinal.ToUpperInvariant();
            var deflection = result.Deflection is > 0.5
                ? $" · {result.Deflection:0}° clear-line adjustment"
                : " · directly away";
            var exit = result.ExitedObstacleCount > 0
                ? $" · exiting {result.ExitedObstacleCount} marked zone{(result.ExitedObstacleCount == 1 ? string.Empty : "s")}"
                : string.Empty;
            AddTacticalEvent(
                "PLAYER",
                "Escape route planned",
                $"{distance:0} MU {cardinal} from the latest authorized contact{deflection}{exit}",
                warning: true);
            await ShowHotkeyToastAsync($"ESCAPE {cardinal} · {distance:0} MU", true);
            return;
        }

        var message = result?.Reason switch
        {
            "NO_LIVE_CONTACT" => "NO LIVE AUTHORIZED CONTACT",
            "NO_SELF_POSITION" => "WAITING FOR YOUR LIVE POSITION",
            "NO_CLEAR_ROUTE" => "NO CLEAR ESCAPE LINE · CHECK MAP",
            "STREAMER_MODE" => "ESCAPE ROUTE HIDDEN IN STREAMER MODE",
            _ => "ESCAPE ROUTE UNAVAILABLE"
        };
        await ShowHotkeyToastAsync(message, false);
    }

    private void PackSpreadAlertButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        _packSpreadAlertIndex = (_packSpreadAlertIndex + 1) % _packSpreadAlertDistances.Length;
        _packSpreadAlertInitialized = false;
        _packSpreadAlertActive = false;
        UpdateFriendProximity();
        SaveSettings();
    }

    private async void RoutePackCenterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        var command = _packRouteActive
            ? "window.__isley?.clearWaypoint() ?? false"
            : "window.__isley?.routeToPackCenter() ?? false";
        if (!await ExecuteMapperCommandAsync(command))
        {
            RoutePackCenterButton.ToolTip = "No authorized live pack center is currently available";
        }
    }

    private async void RoutePackOutlierButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        var command = _packOutlierRouteActive
            ? "window.__isley?.clearWaypoint() ?? false"
            : "window.__isley?.routeToPackOutlier() ?? false";
        if (!await ExecuteMapperCommandAsync(command))
        {
            RoutePackOutlierButton.ToolTip =
                "At least two authorized live friends are required to identify a pack outlier";
        }
    }

    private async void RouteNearestFriendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        var command = string.IsNullOrWhiteSpace(_friendRouteName)
            ? "window.__isley?.routeToNearestFriend() ?? false"
            : "window.__isley?.clearWaypoint() ?? false";
        var routed = await ExecuteMapperCommandAsync(command);
        if (!routed)
        {
            RouteNearestFriendButton.ToolTip = "No routable authorized friend is currently available";
        }
    }

    private async void FriendRosterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode || sender is not Button { Tag: string friendName })
        {
            return;
        }

        var command = string.Equals(friendName, _friendRouteName, StringComparison.Ordinal)
            ? "window.__isley?.clearWaypoint() ?? false"
            : $"window.__isley?.routeToFriend({JsonSerializer.Serialize(friendName)}) ?? false";
        var routed = await ExecuteMapperCommandAsync(command);
        if (!routed)
        {
            FriendRosterStatus.Text = $"{friendName} is no longer available to route";
            FriendRosterStatus.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        FriendRosterStatus.Foreground = (Brush)FindResource("SecondaryTextBrush");
    }

    // ===== Wave-2: Steam friend groups (persisted via overlay extras sidecar)
    // and the session-only encounter watchlist (map context action). All UI is
    // built programmatically so the shared XAML stays untouched.

    private readonly List<EncounterWatchEntry> _encounterWatchlist = [];
    private string _selectedFriendGroupId = string.Empty;
    private string _friendGroupUiSignature = string.Empty;
    private StackPanel? _friendGroupRootPanel;
    private TextBlock? _friendGroupStatusText;
    private TextBox? _friendGroupNameInputBox;
    private StackPanel? _friendGroupListPanel;
    private Button? _friendGroupCreateButton;
    private Button? _friendGroupAddMemberButton;
    private Button? _friendGroupRemoveButton;
    private StackPanel? _encounterWatchRootPanel;
    private TextBlock? _encounterWatchStatusText;
    private StackPanel? _encounterWatchListPanel;
    private string _encounterWatchUiSignature = string.Empty;

    private void EnsureFriendGroupsUi()
    {
        if (_friendGroupRootPanel is not null || SteamFriendWatchContentPanel is null)
        {
            return;
        }

        _friendGroupRootPanel = new StackPanel();
        _friendGroupStatusText = new TextBlock
        {
            Margin = new Thickness(1, 0, 1, 6),
            FontSize = 8,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            Text = "Group watched Steam friends into named squads."
        };
        _friendGroupNameInputBox = new TextBox
        {
            Style = (Style)FindResource("DrawerTextBox"),
            MaxLength = 48,
            ToolTip = "Name a squad (24 characters after cleanup)"
        };
        _friendGroupNameInputBox.TextChanged += (_, _) =>
        {
            _friendGroupUiSignature = string.Empty;
            UpdateFriendGroups();
        };
        _friendGroupCreateButton = new Button
        {
            Style = (Style)FindResource("DrawerCompactButton"),
            Content = "CREATE GROUP",
            ToolTip = $"Create a named squad (up to {SteamFriendGroupLogic.MaximumGroups})"
        };
        _friendGroupCreateButton.Click += FriendGroupCreateButton_Click;
        _friendGroupRemoveButton = new Button
        {
            Style = (Style)FindResource("DrawerCompactButton"),
            Content = "REMOVE GROUP",
            ToolTip = "Delete the selected squad; watched friends stay watched"
        };
        _friendGroupRemoveButton.Click += FriendGroupRemoveButton_Click;
        var createGrid = new UniformGrid { Columns = 2, Margin = new Thickness(-2, 1, -2, 3) };
        createGrid.Children.Add(_friendGroupCreateButton);
        createGrid.Children.Add(_friendGroupRemoveButton);
        _friendGroupListPanel = new StackPanel();
        _friendGroupAddMemberButton = new Button
        {
            Style = (Style)FindResource("DrawerCompactButton"),
            Content = "ADD SELECTED WATCH",
            Margin = new Thickness(-2, 1, -2, 0),
            ToolTip = "Add the selected watched friend above to the selected squad"
        };
        _friendGroupAddMemberButton.Click += FriendGroupAddMemberButton_Click;
        var groupNote = new TextBlock
        {
            Margin = new Thickness(1, 3, 1, 0),
            FontSize = 7,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            Text = $"Squads are stored locally · {SteamFriendGroupLogic.MaximumGroups} groups max · " +
                   $"{SteamFriendGroupLogic.MaximumTotalMembers} memberships max · " +
                   "LIVE counts use the authorized live map only."
        };
        _friendGroupRootPanel.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("SectionLabel"),
            Margin = new Thickness(1, 6, 0, 6),
            Text = "FRIEND GROUPS"
        });
        _friendGroupRootPanel.Children.Add(_friendGroupStatusText);
        _friendGroupRootPanel.Children.Add(_friendGroupNameInputBox);
        _friendGroupRootPanel.Children.Add(createGrid);
        _friendGroupRootPanel.Children.Add(_friendGroupListPanel);
        _friendGroupRootPanel.Children.Add(_friendGroupAddMemberButton);
        _friendGroupRootPanel.Children.Add(groupNote);
        SteamFriendWatchContentPanel.Children.Add(_friendGroupRootPanel);
    }

    private void UpdateFriendGroups()
    {
        EnsureFriendGroupsUi();
        if (_friendGroupRootPanel is null
            || _friendGroupStatusText is null
            || _friendGroupListPanel is null
            || _friendGroupCreateButton is null
            || _friendGroupAddMemberButton is null
            || _friendGroupRemoveButton is null
            || _friendGroupNameInputBox is null)
        {
            return;
        }

        EnsureOverlayExtrasLoaded();
        // Members reference watch entries by opaque id; pruning drops ids whose
        // watch was removed (the sidecar is rewritten on the next real save and
        // load-time normalization applies the same filter).
        _overlayFriendGroups = SteamFriendGroupLogic.NormalizeGroups(
            _overlayFriendGroups,
            _steamFriendWatchlist.Select(entry => entry.Id),
            DateTimeOffset.UtcNow);
        var selectedGroup = _overlayFriendGroups.FirstOrDefault(group =>
            string.Equals(group.Id, _selectedFriendGroupId, StringComparison.Ordinal));
        if (selectedGroup is null)
        {
            selectedGroup = _overlayFriendGroups.FirstOrDefault();
            _selectedFriendGroupId = selectedGroup?.Id ?? string.Empty;
        }

        var signature = string.Join('|',
            _streamerMode,
            LiveMapServicesActive,
            _selectedFriendGroupId,
            _selectedSteamFriendWatchId,
            SteamFriendGroupLogic.NormalizeGroupName(_friendGroupNameInputBox.Text),
            string.Join(';', _overlayFriendGroups.Select(group =>
                $"{group.Id}:{group.Name}:{group.MemberWatchIds.Count}:" +
                $"{SteamFriendGroupLogic.CountLiveMembers(
                    group,
                    _steamFriendWatchlist,
                    LiveMapServicesActive ? _friendRoster.Select(friend => friend.Name) : null)}")));
        if (string.Equals(signature, _friendGroupUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _friendGroupUiSignature = signature;

        if (_streamerMode)
        {
            _friendGroupRootPanel.Visibility = Visibility.Collapsed;
            return;
        }

        _friendGroupRootPanel.Visibility = Visibility.Visible;
        _friendGroupListPanel.Children.Clear();
        var totalMembers = _overlayFriendGroups.Sum(group => group.MemberWatchIds.Count);
        _friendGroupStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
        _friendGroupStatusText.Text = _overlayFriendGroups.Count == 0
            ? "No squads yet · name one and create it, then add watched friends"
            : $"{_overlayFriendGroups.Count}/{SteamFriendGroupLogic.MaximumGroups} SQUADS · " +
              $"{totalMembers}/{SteamFriendGroupLogic.MaximumTotalMembers} MEMBERSHIPS" +
              (LiveMapServicesActive ? string.Empty : " · PRESENCE PAUSED");
        foreach (var group in _overlayFriendGroups)
        {
            var liveCount = SteamFriendGroupLogic.CountLiveMembers(
                group,
                _steamFriendWatchlist,
                LiveMapServicesActive ? _friendRoster.Select(friend => friend.Name) : null);
            var memberNames = group.MemberWatchIds
                .Select(id => _steamFriendWatchlist.FirstOrDefault(entry =>
                    string.Equals(entry.Id, id, StringComparison.Ordinal))?.MapName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
            var state = !LiveMapServicesActive
                ? $"{group.MemberWatchIds.Count} MEMBERS · PAUSED"
                : group.MemberWatchIds.Count == 0
                    ? "EMPTY"
                    : $"{liveCount}/{group.MemberWatchIds.Count} LIVE";
            var displayName = group.Name.Length <= 18 ? group.Name : $"{group.Name[..17]}…";
            var button = new Button
            {
                Style = (Style)FindResource("DrawerButton"),
                Height = 30,
                Margin = new Thickness(0, 0, 0, 4),
                FontSize = 9,
                Content = $"{(LiveMapServicesActive && liveCount > 0 ? "●" : "○")} {displayName} · {state}",
                Tag = group.Id,
                ToolTip = memberNames.Count == 0
                    ? $"{group.Name} · no members yet · select a watched friend, then ADD SELECTED WATCH"
                    : $"{group.Name} · {string.Join(", ", memberNames.Take(8))}" +
                      (memberNames.Count > 8 ? $" +{memberNames.Count - 8} more" : string.Empty)
            };
            button.Click += FriendGroupButton_Click;
            SetToggleButtonState(
                button,
                string.Equals(group.Id, _selectedFriendGroupId, StringComparison.Ordinal));
            _friendGroupListPanel.Children.Add(button);
        }

        var selectedWatch = _steamFriendWatchlist.FirstOrDefault(entry =>
            string.Equals(entry.Id, _selectedSteamFriendWatchId, StringComparison.Ordinal));
        _friendGroupCreateButton.IsEnabled =
            SteamFriendGroupLogic.NormalizeGroupName(_friendGroupNameInputBox.Text).Length > 0
            && _overlayFriendGroups.Count < SteamFriendGroupLogic.MaximumGroups;
        _friendGroupAddMemberButton.IsEnabled = selectedGroup is not null
                                                && selectedWatch is not null
                                                && !selectedGroup.MemberWatchIds.Any(id =>
                                                    string.Equals(id, selectedWatch.Id, StringComparison.Ordinal));
        _friendGroupRemoveButton.IsEnabled = selectedGroup is not null;
    }

    private void FriendGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string groupId })
        {
            return;
        }

        _selectedFriendGroupId = groupId;
        _friendGroupUiSignature = string.Empty;
        UpdateFriendGroups();
    }

    private async void FriendGroupCreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        EnsureOverlayExtrasLoaded();
        if (!SteamFriendGroupLogic.TryCreateGroup(
                _friendGroupNameInputBox?.Text,
                _overlayFriendGroups,
                DateTimeOffset.UtcNow,
                out var group,
                out var error))
        {
            await ShowHotkeyToastAsync(error.ToUpperInvariant(), false);
            return;
        }

        _overlayFriendGroups.Add(group);
        _selectedFriendGroupId = group.Id;
        if (_friendGroupNameInputBox is not null)
        {
            _friendGroupNameInputBox.Text = string.Empty;
        }
        SaveOverlayExtras();
        _friendGroupUiSignature = string.Empty;
        UpdateFriendGroups();
        await ShowHotkeyToastAsync($"SQUAD {group.Name.ToUpperInvariant()} READY", true);
    }

    private async void FriendGroupAddMemberButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        EnsureOverlayExtrasLoaded();
        var watch = _steamFriendWatchlist.FirstOrDefault(entry =>
            string.Equals(entry.Id, _selectedSteamFriendWatchId, StringComparison.Ordinal));
        if (!SteamFriendGroupLogic.TryAddMember(
                _overlayFriendGroups,
                _selectedFriendGroupId,
                watch?.Id,
                DateTimeOffset.UtcNow,
                out var error))
        {
            await ShowHotkeyToastAsync(error.ToUpperInvariant(), false);
            return;
        }

        SaveOverlayExtras();
        _friendGroupUiSignature = string.Empty;
        UpdateFriendGroups();
        await ShowHotkeyToastAsync(
            $"{(watch?.MapName ?? "FRIEND").ToUpperInvariant()} ADDED TO SQUAD",
            true);
    }

    private async void FriendGroupRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamerMode)
        {
            return;
        }

        EnsureOverlayExtrasLoaded();
        var removed = _overlayFriendGroups.FirstOrDefault(group =>
            string.Equals(group.Id, _selectedFriendGroupId, StringComparison.Ordinal));
        if (removed is null)
        {
            return;
        }

        _overlayFriendGroups.RemoveAll(group =>
            string.Equals(group.Id, removed.Id, StringComparison.Ordinal));
        _selectedFriendGroupId = _overlayFriendGroups.FirstOrDefault()?.Id ?? string.Empty;
        SaveOverlayExtras();
        _friendGroupUiSignature = string.Empty;
        UpdateFriendGroups();
        await ShowHotkeyToastAsync(
            $"SQUAD {removed.Name.ToUpperInvariant()} REMOVED · WATCHES KEPT",
            true);
    }

    private void EnsureEncounterWatchlistUi()
    {
        if (_encounterWatchRootPanel is not null
            || EncounterStatusText?.Parent is not Panel parent)
        {
            return;
        }

        _encounterWatchRootPanel = new StackPanel();
        _encounterWatchStatusText = new TextBlock
        {
            Margin = new Thickness(1, 0, 1, 4),
            FontSize = 8,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            Text = "Right-click a live player marker to watch them."
        };
        _encounterWatchListPanel = new StackPanel();
        var watchNote = new TextBlock
        {
            Margin = new Thickness(1, 3, 1, 0),
            FontSize = 7,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            Text = $"Session-only · {EncounterWatchlistLogic.MaximumWatchedPlayers} max · " +
                   "distance and direction are the snapshot from the moment of the watch."
        };
        _encounterWatchRootPanel.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("SectionLabel"),
            Margin = new Thickness(1, 6, 0, 6),
            Text = "ENCOUNTER WATCHLIST"
        });
        _encounterWatchRootPanel.Children.Add(_encounterWatchStatusText);
        _encounterWatchRootPanel.Children.Add(_encounterWatchListPanel);
        _encounterWatchRootPanel.Children.Add(watchNote);
        parent.Children.Insert(parent.Children.IndexOf(EncounterStatusText) + 1, _encounterWatchRootPanel);
    }

    private void UpdateEncounterWatchlist()
    {
        EnsureEncounterWatchlistUi();
        if (_encounterWatchRootPanel is null
            || _encounterWatchStatusText is null
            || _encounterWatchListPanel is null)
        {
            return;
        }

        if (_streamerMode)
        {
            _encounterWatchRootPanel.Visibility = Visibility.Collapsed;
            _encounterWatchUiSignature = string.Empty;
            return;
        }

        _encounterWatchRootPanel.Visibility = Visibility.Visible;
        var signature = string.Join('|', _encounterWatchlist.Select(entry =>
            $"{entry.Name}:{entry.DistanceMu}:{entry.Cardinal}"));
        if (string.Equals(signature, _encounterWatchUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _encounterWatchUiSignature = signature;

        _encounterWatchListPanel.Children.Clear();
        _encounterWatchStatusText.Text = _encounterWatchlist.Count == 0
            ? "Right-click a live player marker to watch them this session"
            : $"{_encounterWatchlist.Count}/{EncounterWatchlistLogic.MaximumWatchedPlayers} WATCHED · SESSION ONLY";
        foreach (var entry in _encounterWatchlist)
        {
            var displayName = entry.Name.Length <= 22 ? entry.Name : $"{entry.Name[..21]}…";
            var snapshot = entry.DistanceMu.HasValue
                ? $"WAS {entry.DistanceMu.Value} MU {(string.IsNullOrEmpty(entry.Cardinal) ? string.Empty : entry.Cardinal)}".TrimEnd()
                : "NO DISTANCE SNAPSHOT";
            var button = new Button
            {
                Style = (Style)FindResource("DrawerButton"),
                Height = 30,
                Margin = new Thickness(0, 0, 0, 4),
                FontSize = 9,
                Content = $"◈ {displayName} · {snapshot}",
                Tag = entry.Name,
                ToolTip = $"{entry.Name} · watched from the live map context action · select to remove"
            };
            button.Click += EncounterWatchEntryButton_Click;
            _encounterWatchListPanel.Children.Add(button);
        }
    }

    private async void EncounterWatchEntryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string entryName })
        {
            return;
        }

        _encounterWatchlist.RemoveAll(entry =>
            string.Equals(entry.Name, entryName, StringComparison.OrdinalIgnoreCase));
        _encounterWatchUiSignature = string.Empty;
        UpdateEncounterWatchlist();
        await ShowHotkeyToastAsync("WATCH REMOVED", true);
    }

    // Bridge entry point for the map shell's right-click "Watch player" context
    // action. Input is already structurally validated by the bridge in
    // MainWindow.MapTools.cs; names are normalized here before any use.
    private async Task AddEncounterWatchFromMapAsync(string? rawName, double? distanceMu, string? cardinal)
    {
        if (_streamerMode)
        {
            // Fail-closed: encounter context stays hidden in streamer mode.
            return;
        }

        var name = EncounterWatchlistLogic.NormalizeName(rawName);
        if (name.Length == 0)
        {
            await ShowHotkeyToastAsync("WATCHLIST ADD BLOCKED · INVALID PLAYER", false);
            return;
        }

        var alreadyWatched = _encounterWatchlist.Any(entry =>
            string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
        var updated = EncounterWatchlistLogic.Upsert(
            _encounterWatchlist,
            name,
            EncounterWatchlistLogic.NormalizeDistanceMu(distanceMu),
            cardinal,
            DateTimeOffset.UtcNow);
        _encounterWatchlist.Clear();
        _encounterWatchlist.AddRange(updated);
        _encounterWatchUiSignature = string.Empty;
        UpdateEncounterWatchlist();
        AddTacticalEvent(
            "PLAYER",
            alreadyWatched ? "Watch refreshed" : "Player watched",
            $"{name} · live map context action · session-only watchlist");
        await ShowHotkeyToastAsync(
            alreadyWatched
                ? $"WATCH REFRESHED · {name.ToUpperInvariant()}"
                : $"WATCHING {name.ToUpperInvariant()} · SESSION ONLY",
            true);
    }
}
