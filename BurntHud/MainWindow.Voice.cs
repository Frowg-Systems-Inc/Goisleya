using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Net.WebSockets;
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
    private void UniversalSessionVoiceButton_Click(object sender, RoutedEventArgs e) =>
        OpenToolsWorkspace("voice");

    private bool _voiceSessionConnectedThisSession;
    private DateTimeOffset _voiceAutoReconnectNotBefore = DateTimeOffset.MinValue;
    private int _voiceAutoReconnectDelaySeconds = 5;

    // Wave-2: per-peer volume memory (persisted via the overlay extras sidecar)
    // and per-peer quality snapshots (session-only, real WebRTC stats only).
    private readonly HashSet<string> _voicePeerVolumeRestoreAppliedPeerIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VoicePeerQualitySnapshot> _voicePeerQualities =
        new(StringComparer.Ordinal);

    private void RefreshVoiceStatus()
    {
        if (VoiceHudBorder is null)
        {
            return;
        }
        if (_voiceBridgeRunning)
        {
            PostVoiceCommand(new
            {
                type = "position",
                x = _currentSelfMapX,
                y = _currentSelfMapY
            });
        }
        // Auto proximity voice reconnects after an unexpected drop (server
        // restart, network blip) with escalating backoff (5s → 10s → … 60s cap).
        if (_voiceEnabled
            && _voiceAutoOpen
            && !_streamerMode
            && !_voiceUserDisconnectedThisSession
            && _voiceSessionConnectedThisSession
            && !_voiceBridgeRunning
            && !_voiceConnecting
            && !_voiceAutoConnectInFlight
            && _voiceEngineState is "DISCONNECTED" or "ERROR"
            && DateTimeOffset.UtcNow >= _voiceAutoReconnectNotBefore)
        {
            _voiceAutoReconnectNotBefore = DateTimeOffset.UtcNow.AddSeconds(_voiceAutoReconnectDelaySeconds);
            _voiceAutoReconnectDelaySeconds = Math.Min(60, _voiceAutoReconnectDelaySeconds * 2);
            _ = TryAutoConnectProximityVoiceAsync();
        }
        UpdateVoicePresentation();
    }

    private static string NormalizeVoiceServerUrl(string? value)
    {
        const string fallback = "ws://127.0.0.1:5198/voice";
        return VoiceInviteLogic.TryNormalizeServerUrl(value, out var normalized, out _)
            ? normalized
            : fallback;
    }

    private static bool IsBundledLocalVoiceServerUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
            || uri.Port != 5198
            || !string.Equals(uri.AbsolutePath.TrimEnd('/'), "/voice", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
               || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> EnsureBundledVoiceHostReadyAsync(bool showToast)
    {
        const string localServer = "ws://127.0.0.1:5198/voice";
        _voiceServerUrl = localServer;
        VoiceServerInputBox.Text = localServer;

        if (await CheckVoiceServerReadinessAsync(userInitiated: false))
        {
            if (showToast)
            {
                await ShowHotkeyToastAsync("LOCAL ISLEY VOICE HOST READY", true);
            }
            return true;
        }

        if (_voiceLocalHostProcess is { HasExited: true })
        {
            _voiceLocalHostProcess.Dispose();
            _voiceLocalHostProcess = null;
        }

        var hostDirectory = Path.Combine(AppContext.BaseDirectory, "VoiceServer");
        var hostExecutable = Path.Combine(hostDirectory, "Isley.VoiceServer.exe");
        if (!File.Exists(hostExecutable))
        {
            if (showToast)
            {
                await ShowHotkeyToastAsync("BUNDLED VOICE HOST NOT FOUND", false);
            }
            return false;
        }

        try
        {
            _voiceLocalHostProcess ??= Process.Start(new ProcessStartInfo(hostExecutable)
            {
                WorkingDirectory = hostDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            await Task.Delay(250);
            if (_voiceLocalHostProcess is null || _voiceLocalHostProcess.HasExited)
            {
                _voiceLocalHostProcess?.Dispose();
                _voiceLocalHostProcess = null;
                throw new InvalidOperationException("Local signaling host exited during startup.");
            }

            if (!await CheckVoiceServerReadinessAsync(userInitiated: false, startupAttempts: 20))
            {
                try { _voiceLocalHostProcess.Kill(entireProcessTree: true); } catch { }
                _voiceLocalHostProcess.Dispose();
                _voiceLocalHostProcess = null;
                throw new InvalidOperationException("Local signaling host did not pass readiness.");
            }

            _voiceUiSignature = string.Empty;
            UpdateVoicePresentation();
            if (showToast)
            {
                await ShowHotkeyToastAsync("LOCAL ISLEY VOICE HOST READY", true);
            }
            return true;
        }
        catch
        {
            _voiceUiSignature = string.Empty;
            UpdateVoicePresentation();
            if (showToast)
            {
                await ShowHotkeyToastAsync("LOCAL ISLEY VOICE HOST FAILED", false);
            }
            return false;
        }
    }

    private void VoiceServerInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var current = VoiceServerInputBox.Text.Trim();
        if (_voiceServerCheckState == VoiceServerCheckState.Ready
            && string.Equals(current, _voiceServerCheckedUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _voiceServerReadinessCancellation?.Cancel();
        _voiceServerCheckState = VoiceServerCheckState.Unchecked;
        _voiceServerReadiness = null;
        _voiceServerCheckedUrl = string.Empty;
        _voiceUiSignature = string.Empty;
        if (VoiceServerCheckStatusText is not null)
        {
            UpdateVoicePresentation();
        }
    }

    private async Task<bool> CheckVoiceServerReadinessAsync(
        bool userInitiated,
        int startupAttempts = 1)
    {
        if (_voiceServerCheckInFlight)
        {
            if (userInitiated)
            {
                await ShowHotkeyToastAsync("VOICE SERVER CHECK ALREADY RUNNING", true);
            }
            return false;
        }

        var requested = VoiceServerInputBox.Text.Trim();
        if (!VoiceInviteLogic.TryNormalizeServerUrl(
                requested,
                out var normalizedServer,
                out _)
            || !VoiceServerReadinessClient.TryCreateReadinessUri(normalizedServer, out _))
        {
            _voiceServerCheckState = VoiceServerCheckState.Incompatible;
            _voiceServerReadiness = null;
            _voiceServerCheckedUrl = string.Empty;
            _voiceEngineState = "READY";
            _voiceEngineDetail = "VOICE SERVER INVALID · MICROPHONE OFF";
            _voiceUiSignature = string.Empty;
            UpdateVoicePresentation();
            if (userInitiated)
            {
                await ShowHotkeyToastAsync("VOICE SERVER MUST USE WSS OR LOCALHOST WS", false);
            }
            return false;
        }

        _voiceServerUrl = normalizedServer;
        VoiceServerInputBox.Text = normalizedServer;
        _voiceServerReadinessCancellation?.Cancel();
        _voiceServerReadinessCancellation?.Dispose();
        _voiceServerReadinessCancellation = new CancellationTokenSource();
        var cancellationToken = _voiceServerReadinessCancellation.Token;
        _voiceServerCheckInFlight = true;
        _voiceServerCheckState = VoiceServerCheckState.Checking;
        _voiceServerReadiness = null;
        _voiceServerCheckedUrl = string.Empty;
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();

        try
        {
            VoiceServerReadinessSnapshot snapshot = default;
            var attempts = Math.Clamp(startupAttempts, 1, 20);
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                try
                {
                    snapshot = await VoiceServerReadinessClient.FetchAsync(
                        normalizedServer,
                        cancellationToken);
                    break;
                }
                catch (Exception ex) when (
                    attempt + 1 < attempts
                    && !cancellationToken.IsCancellationRequested
                    && (ex is HttpRequestException or TaskCanceledException))
                {
                    // HttpClient timeouts surface as TaskCanceledException and must
                    // still retry while the local bundled host is finishing startup.
                    await Task.Delay(100, cancellationToken);
                }
            }

            if (cancellationToken.IsCancellationRequested || !IsLoaded)
            {
                return false;
            }
            _voiceServerCheckState = VoiceServerCheckState.Ready;
            _voiceServerReadiness = snapshot;
            _voiceServerCheckedUrl = normalizedServer;
            _voiceEngineState = "READY";
            _voiceEngineDetail = "SERVER VERIFIED · MICROPHONE OFF UNTIL CONNECT";
            if (userInitiated)
            {
                await ShowHotkeyToastAsync("ISLEY VOICE SERVER READY", true);
            }
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            _voiceServerCheckState = VoiceServerCheckState.Incompatible;
            _voiceServerReadiness = null;
            _voiceServerCheckedUrl = string.Empty;
            _voiceEngineState = "READY";
            _voiceEngineDetail = "INCOMPATIBLE VOICE SERVER · MICROPHONE OFF";
            if (userInitiated)
            {
                await ShowHotkeyToastAsync("NOT A COMPATIBLE ISLEY VOICE SERVER", false);
            }
            return false;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _voiceServerCheckState = VoiceServerCheckState.Unavailable;
            _voiceServerReadiness = null;
            _voiceServerCheckedUrl = string.Empty;
            _voiceEngineState = "READY";
            _voiceEngineDetail = "VOICE SERVER UNAVAILABLE · MICROPHONE OFF";
            if (userInitiated)
            {
                await ShowHotkeyToastAsync("ISLEY VOICE SERVER UNAVAILABLE", false);
            }
            return false;
        }
        finally
        {
            _voiceServerCheckInFlight = false;
            _voiceUiSignature = string.Empty;
            if (IsLoaded)
            {
                UpdateVoicePresentation();
            }
        }
    }

    private static string NewVoiceSecret(int bytes) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    private void InitializeVoiceSessionFields()
    {
        _voiceRoomSecret = NewVoiceSecret(12);
        _voicePeerId = NewVoiceSecret(16);
        VoiceRoomKeyInputBox.Text = _voiceRoomSecret;
        VoiceServerInputBox.Text = _voiceServerUrl;
        VoiceDisplayNameInputBox.Text = "Isley Player";
        VoiceRoomInviteStatusText.Text = _voiceAutoOpen
            ? "AUTO PROXIMITY READY · HOLD PTT TO TALK · COPY INVITE FOR PACKMATES"
            : "NEW PRIVATE ROOM · COPY AN INVITE FOR TRUSTED PLAYERS";
        SetVoiceInputDeviceOptions([], string.Empty, "CONNECT TO CHOOSE");
        SetVoiceOutputDeviceOptions([], string.Empty, false, "CONNECT TO CHOOSE");
        ResetVoiceMicMeterState();
        ResetVoiceQualityState();
        UpdateVoicePresentation();
    }

    private void SetVoiceInputDeviceOptions(
        IReadOnlyList<VoiceInputDeviceInfo> devices,
        string? selectedDeviceId,
        string status)
    {
        _voiceInputDevices.Clear();
        _voiceInputDevices.AddRange(devices.Take(VoiceIntegrationLogic.MaximumInputDevices));
        var normalizedSelectedId = VoiceIntegrationLogic.NormalizeInputDeviceId(selectedDeviceId);
        if (!string.IsNullOrEmpty(normalizedSelectedId)
            && _voiceInputDevices.Any(device => device.Id == normalizedSelectedId))
        {
            _voiceSelectedInputDeviceId = normalizedSelectedId;
        }

        _suppressVoiceInputDeviceSelection = true;
        try
        {
            var visibleDevices = _voiceInputDevices.Count > 0
                ? _voiceInputDevices.ToList()
                : [new VoiceInputDeviceInfo(string.Empty, "Connect to choose")];
            VoiceInputDeviceComboBox.ItemsSource = visibleDevices;
            VoiceInputDeviceComboBox.SelectedValue = _voiceInputDevices.Any(
                device => device.Id == _voiceSelectedInputDeviceId)
                ? _voiceSelectedInputDeviceId
                : visibleDevices[0].Id;
        }
        finally
        {
            _suppressVoiceInputDeviceSelection = false;
        }

        _voiceInputDeviceStatus = string.IsNullOrWhiteSpace(status)
            ? "MICROPHONE READY"
            : status;
    }

    private void SetVoiceOutputDeviceOptions(
        IReadOnlyList<VoiceOutputDeviceInfo> devices,
        string? selectedDeviceId,
        bool selectionSupported,
        string status)
    {
        _voiceOutputDevices.Clear();
        _voiceOutputDevices.AddRange(devices.Take(VoiceIntegrationLogic.MaximumOutputDevices));
        _voiceOutputSelectionSupported = selectionSupported;
        var normalizedSelectedId = VoiceIntegrationLogic.NormalizeOutputDeviceId(selectedDeviceId);
        if (!string.IsNullOrEmpty(normalizedSelectedId)
            && _voiceOutputDevices.Any(device => device.Id == normalizedSelectedId))
        {
            _voiceSelectedOutputDeviceId = normalizedSelectedId;
        }

        _suppressVoiceOutputDeviceSelection = true;
        try
        {
            var placeholder = selectionSupported ? "Connect to choose" : "Windows default output";
            var visibleDevices = _voiceOutputDevices.Count > 0
                ? _voiceOutputDevices.ToList()
                : [new VoiceOutputDeviceInfo(string.Empty, placeholder)];
            VoiceOutputDeviceComboBox.ItemsSource = visibleDevices;
            VoiceOutputDeviceComboBox.SelectedValue = _voiceOutputDevices.Any(
                device => device.Id == _voiceSelectedOutputDeviceId)
                ? _voiceSelectedOutputDeviceId
                : visibleDevices[0].Id;
        }
        finally
        {
            _suppressVoiceOutputDeviceSelection = false;
        }

        _voiceOutputDeviceStatus = string.IsNullOrWhiteSpace(status)
            ? "SPEAKER READY"
            : status;
    }

    private void ReadVoiceAudioDevices(JsonElement root)
    {
        var devices = new List<VoiceInputDeviceInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (root.TryGetProperty("devices", out var devicesValue)
            && devicesValue.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in devicesValue.EnumerateArray().Take(VoiceIntegrationLogic.MaximumInputDevices))
            {
                var id = item.TryGetProperty("id", out var idValue)
                    ? VoiceIntegrationLogic.NormalizeInputDeviceId(idValue.GetString())
                    : string.Empty;
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                var label = item.TryGetProperty("label", out var labelValue)
                    ? VoiceIntegrationLogic.NormalizeInputDeviceLabel(labelValue.GetString(), index)
                    : VoiceIntegrationLogic.NormalizeInputDeviceLabel(null, index);
                devices.Add(new VoiceInputDeviceInfo(id, label));
                index++;
            }
        }

        var selectedId = root.TryGetProperty("selectedDeviceId", out var selectedValue)
            ? VoiceIntegrationLogic.NormalizeInputDeviceId(selectedValue.GetString())
            : string.Empty;
        var state = root.TryGetProperty("state", out var stateValue)
            ? (stateValue.GetString() ?? string.Empty).Trim().ToUpperInvariant()
            : string.Empty;
        var status = state switch
        {
            "READY" when devices.Count == 1 => "1 MICROPHONE · SESSION ONLY",
            "READY" => $"{devices.Count} MICROPHONES · SESSION ONLY",
            "LOCKED" => "CONNECT TO CHOOSE",
            _ => devices.Count > 0 ? "MICROPHONES READY" : "NO MICROPHONE FOUND"
        };
        SetVoiceInputDeviceOptions(devices, selectedId, status);

        var outputDevices = new List<VoiceOutputDeviceInfo>();
        var seenOutputIds = new HashSet<string>(StringComparer.Ordinal);
        if (root.TryGetProperty("outputDevices", out var outputDevicesValue)
            && outputDevicesValue.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in outputDevicesValue.EnumerateArray()
                         .Take(VoiceIntegrationLogic.MaximumOutputDevices))
            {
                var id = item.TryGetProperty("id", out var idValue)
                    ? VoiceIntegrationLogic.NormalizeOutputDeviceId(idValue.GetString())
                    : string.Empty;
                if (string.IsNullOrEmpty(id) || !seenOutputIds.Add(id)) continue;
                var label = item.TryGetProperty("label", out var labelValue)
                    ? VoiceIntegrationLogic.NormalizeOutputDeviceLabel(labelValue.GetString(), index)
                    : VoiceIntegrationLogic.NormalizeOutputDeviceLabel(null, index);
                outputDevices.Add(new VoiceOutputDeviceInfo(id, label));
                index++;
            }
        }

        var selectedOutputId = root.TryGetProperty("selectedOutputDeviceId", out var selectedOutputValue)
            ? VoiceIntegrationLogic.NormalizeOutputDeviceId(selectedOutputValue.GetString())
            : string.Empty;
        var outputSelectionSupported =
            root.TryGetProperty("outputSelectionSupported", out var outputSupportedValue)
            && outputSupportedValue.ValueKind == JsonValueKind.True;
        var outputStatus = !outputSelectionSupported
            ? "WINDOWS DEFAULT · OUTPUT SELECTION UNAVAILABLE"
            : state switch
            {
                "READY" when outputDevices.Count == 1 => "1 OUTPUT · SESSION ONLY",
                "READY" => $"{outputDevices.Count} OUTPUTS · SESSION ONLY",
                "LOCKED" => "CONNECT TO CHOOSE",
                _ => outputDevices.Count > 0 ? "OUTPUTS READY" : "NO OUTPUT FOUND"
            };
        SetVoiceOutputDeviceOptions(
            outputDevices,
            selectedOutputId,
            outputSelectionSupported,
            outputStatus);
    }

    private void ReadVoiceParticipants(JsonElement root)
    {
        var participants = new List<VoiceParticipantInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (root.TryGetProperty("participants", out var participantsValue)
            && participantsValue.ValueKind == JsonValueKind.Array)
        {
            var fallbackIndex = 0;
            foreach (var item in participantsValue.EnumerateArray()
                         .Take(VoiceIntegrationLogic.MaximumParticipants))
            {
                var id = item.TryGetProperty("id", out var idValue)
                    ? VoiceIntegrationLogic.NormalizePeerId(idValue.GetString())
                    : string.Empty;
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                var name = item.TryGetProperty("name", out var nameValue)
                    ? VoiceIntegrationLogic.NormalizeParticipantName(nameValue.GetString(), fallbackIndex)
                    : VoiceIntegrationLogic.NormalizeParticipantName(null, fallbackIndex);
                var muted = item.TryGetProperty("muted", out var mutedValue)
                            && mutedValue.ValueKind is JsonValueKind.True or JsonValueKind.False
                            && mutedValue.GetBoolean();
                var volume = item.TryGetProperty("volume", out var volumeValue)
                             && volumeValue.TryGetDouble(out var volumeRatio)
                    ? VoiceIntegrationLogic.NormalizeParticipantVolume((int)Math.Round(volumeRatio * 100))
                    : 100;
                var state = item.TryGetProperty("state", out var peerStateValue)
                    ? VoiceIntegrationLogic.NormalizePeerConnectionState(peerStateValue.GetString())
                    : "WAITING";
                var talking = item.TryGetProperty("talking", out var talkingValue)
                              && talkingValue.ValueKind is JsonValueKind.True or JsonValueKind.False
                              && talkingValue.GetBoolean();
                var distance = item.TryGetProperty("distance", out var distanceValue)
                               && distanceValue.TryGetDouble(out var distanceNumber)
                    ? VoiceIntegrationLogic.NormalizeParticipantDistance(distanceNumber)
                    : null;
                participants.Add(new VoiceParticipantInfo(id, name, muted, volume, state, talking, distance));
                fallbackIndex++;
            }
        }

        _voiceParticipants.Clear();
        _voiceParticipants.AddRange(participants);
        RestoreRememberedVoicePeerVolumes();
        _voiceParticipantRosterSignature = string.Empty;
        UpdateVoiceParticipantRoster();
    }

    // Per-peer volume memory: peer ids are session-random, so the remembered
    // level is keyed by a one-way hash of the peer's self-reported name. Each
    // peer is restored at most once per session so a manual mid-session change
    // is never overridden.
    private void RestoreRememberedVoicePeerVolumes()
    {
        if (!_voiceBridgeRunning || _voiceParticipants.Count == 0)
        {
            return;
        }

        EnsureOverlayExtrasLoaded();
        var now = DateTimeOffset.UtcNow;
        var changed = false;
        for (var index = 0; index < _voiceParticipants.Count; index++)
        {
            var participant = _voiceParticipants[index];
            if (!_voicePeerVolumeRestoreAppliedPeerIds.Add(participant.Id)
                || !VoicePeerVolumeLogic.TryComputePeerKey(participant.Name, out var peerKey)
                || !VoicePeerVolumeLogic.TryFindVolume(_overlayVoicePeerVolumes, peerKey, out var remembered))
            {
                continue;
            }

            if (remembered == participant.VolumePercent)
            {
                continue;
            }

            participant = participant with { VolumePercent = remembered };
            _voiceParticipants[index] = participant;
            changed = true;
            PostVoiceCommand(new
            {
                type = "participant-settings",
                peerId = participant.Id,
                muted = participant.Muted,
                volume = participant.VolumePercent / 100d
            });
            _overlayVoicePeerVolumes = VoicePeerVolumeLogic.Upsert(
                _overlayVoicePeerVolumes,
                peerKey,
                participant.VolumePercent,
                now);
        }

        if (changed)
        {
            SaveOverlayExtras();
        }
    }

    private void UpdateVoiceParticipantRoster()
    {
        if (VoiceParticipantListPanel is null || VoiceParticipantEmptyText is null) return;
        var signature = string.Join('|', _streamerMode,
            _voiceQualityMonitorEnabled && _voiceBridgeRunning,
            string.Join(';', _voiceParticipants.Select(participant =>
                $"{participant.Id}:{participant.Name}:{participant.Muted}:{participant.VolumePercent}:{participant.State}:{participant.Talking}:{participant.Distance}:" +
                $"{(_voicePeerQualities.TryGetValue(participant.Id, out var signatureQuality)
                    ? $"{signatureQuality.RoundTripMilliseconds}:{signatureQuality.JitterMilliseconds}:{signatureQuality.PacketLossPercent}"
                    : "-")}")));
        if (string.Equals(signature, _voiceParticipantRosterSignature, StringComparison.Ordinal)) return;
        _voiceParticipantRosterSignature = signature;

        VoiceParticipantListPanel.Children.Clear();
        VoiceParticipantEmptyText.Visibility = _voiceParticipants.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        for (var index = 0; index < _voiceParticipants.Count; index++)
        {
            var participant = _voiceParticipants[index];
            var visibleName = _streamerMode ? $"PLAYER {index + 1}" : participant.Name;
            var stateText = participant.Talking
                ? "TALKING"
                : !_voiceProximityEnabled && participant.State == "CONNECTED"
                    ? "ROOM RADIO"
                    : participant.State;
            if (_voiceProximityEnabled && participant.Distance.HasValue)
            {
                stateText += $" · {participant.Distance.Value} MU";
            }
            var peerQuality = _voicePeerQualities.TryGetValue(participant.Id, out var participantQuality)
                ? participantQuality
                : (VoicePeerQualitySnapshot?)null;
            var qualityMonitorActive = _voiceQualityMonitorEnabled && _voiceBridgeRunning;
            stateText += VoicePeerQualityLogic.FormatSuffix(peerQuality, qualityMonitorActive);
            var row = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });

            var identity = new StackPanel { Margin = new Thickness(2, 2, 4, 2) };
            identity.Children.Add(new TextBlock
            {
                Text = visibleName,
                FontSize = 8,
                FontWeight = FontWeights.SemiBold,
                Foreground = participant.Talking
                    ? (Brush)FindResource("AccentBrush")
                    : (Brush)FindResource("PrimaryTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = _streamerMode ? "Identity hidden by Streamer Mode" : participant.Name
            });
            identity.Children.Add(new TextBlock
            {
                Text = stateText,
                FontSize = 6.5,
                FontWeight = FontWeights.Bold,
                Foreground = participant.Talking
                    ? (Brush)FindResource("AccentBrush")
                    : participant.State == "CONNECTED"
                    ? (Brush)FindResource("SuccessBrush")
                    : (Brush)FindResource("SecondaryTextBrush"),
                ToolTip = VoicePeerQualityLogic.Describe(peerQuality, qualityMonitorActive)
            });
            Grid.SetColumn(identity, 0);
            row.Children.Add(identity);

            var muteButton = new Button
            {
                Content = participant.Muted ? "MUTED" : "MUTE",
                Tag = participant.Id,
                Height = 26,
                Padding = new Thickness(2, 0, 2, 0),
                Margin = new Thickness(2, 0, 0, 0),
                Style = (Style)FindResource("DrawerCompactButton"),
                ToolTip = participant.Muted ? $"Restore {visibleName}" : $"Mute {visibleName}"
            };
            muteButton.Click += VoiceParticipantMuteButton_Click;
            SetToggleButtonState(muteButton, participant.Muted);
            Grid.SetColumn(muteButton, 1);
            row.Children.Add(muteButton);

            var volumeButton = new Button
            {
                Content = $"VOL {participant.VolumePercent}",
                Tag = participant.Id,
                Height = 26,
                Padding = new Thickness(2, 0, 2, 0),
                Margin = new Thickness(2, 0, 0, 0),
                Style = (Style)FindResource("DrawerCompactButton"),
                ToolTip = $"Cycle {visibleName} volume: 100, 75, 50, 25"
            };
            volumeButton.Click += VoiceParticipantVolumeButton_Click;
            SetToggleButtonState(volumeButton, participant.VolumePercent < 100);
            Grid.SetColumn(volumeButton, 2);
            row.Children.Add(volumeButton);

            VoiceParticipantListPanel.Children.Add(row);
        }
    }

    private async Task<bool> InitializeVoiceEngineAsync()
    {
        if (VoiceWebView.CoreWebView2 is not null)
        {
            return true;
        }
        if (_voiceEngineInitializing) return false;

        _voiceEngineInitializing = true;
        _voiceEngineState = "STARTING";
        _voiceEngineDetail = "LOADING BUILT-IN ENGINE";
        UpdateVoicePresentation();
        try
        {
            var dataRoot = PortableModeEnabled
                ? Path.Combine(PortableDataDirectory, "VoiceWebView2")
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Isley",
                    "VoiceWebView2");
            Directory.CreateDirectory(dataRoot);
            var environment = await CoreWebView2Environment.CreateAsync(null, dataRoot);
            await VoiceWebView.EnsureCoreWebView2Async(environment);
            var core = VoiceWebView.CoreWebView2!;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.PermissionRequested += VoiceWebView_PermissionRequested;
            core.WebMessageReceived += VoiceWebView_WebMessageReceived;

            var voiceAssets = Path.Combine(AppContext.BaseDirectory, "Voice");
            if (!Directory.Exists(voiceAssets))
            {
                throw new DirectoryNotFoundException("Built-in voice assets are missing.");
            }
            core.SetVirtualHostNameToFolderMapping(
                "isley.voice.local",
                voiceAssets,
                CoreWebView2HostResourceAccessKind.DenyCors);

            var navigationReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void NavigationCompleted(object? _, CoreWebView2NavigationCompletedEventArgs args) =>
                navigationReady.TrySetResult(args.IsSuccess);
            core.NavigationCompleted += NavigationCompleted;
            try
            {
                core.Navigate("https://isley.voice.local/voice.html");
                var completed = await navigationReady.Task.WaitAsync(TimeSpan.FromSeconds(8));
                if (!completed) throw new InvalidOperationException("Built-in voice page failed to load.");
            }
            finally
            {
                core.NavigationCompleted -= NavigationCompleted;
            }

            _voiceEngineState = "READY";
            _voiceEngineDetail = "MICROPHONE OFF UNTIL CONNECT";
            return true;
        }
        catch (Exception ex)
        {
            _voiceEngineState = "ERROR";
            _voiceEngineDetail = Regex.Replace(ex.Message, @"\s+", " ").Trim()[..Math.Min(120, Regex.Replace(ex.Message, @"\s+", " ").Trim().Length)];
            return false;
        }
        finally
        {
            _voiceEngineInitializing = false;
            _voiceConnecting = false;
            _voiceUiSignature = string.Empty;
            UpdateVoicePresentation();
        }
    }

    private void VoiceWebView_PermissionRequested(
        object? sender,
        CoreWebView2PermissionRequestedEventArgs e)
    {
        var trustedOrigin = Uri.TryCreate(e.Uri, UriKind.Absolute, out var origin)
                            && string.Equals(origin.Host, "isley.voice.local", StringComparison.OrdinalIgnoreCase);
        if (e.PermissionKind == CoreWebView2PermissionKind.Microphone
            && trustedOrigin
            && _voicePermissionArmed)
        {
            e.State = CoreWebView2PermissionState.Allow;
            e.SavesInProfile = false;
        }
        else
        {
            e.State = CoreWebView2PermissionState.Deny;
            e.SavesInProfile = false;
        }
        DisarmVoiceMicrophonePermission();
    }

    private void ArmVoiceMicrophonePermission()
    {
        _voicePermissionArmed = true;
        var revision = ++_voicePermissionRevision;
        _ = ClearVoiceMicrophonePermissionArmAsync(revision);
    }

    private void DisarmVoiceMicrophonePermission()
    {
        _voicePermissionArmed = false;
        _voicePermissionRevision++;
    }

    private async Task ClearVoiceMicrophonePermissionArmAsync(int revision)
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        if (_voicePermissionRevision == revision)
        {
            _voicePermissionArmed = false;
        }
    }

    private void VoiceWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : string.Empty;
            if (type == "voice-status")
            {
                _voiceEngineState = root.TryGetProperty("state", out var stateValue)
                    ? (stateValue.GetString() ?? "READY").ToUpperInvariant()
                    : "READY";
                _voiceEngineDetail = root.TryGetProperty("detail", out var detailValue)
                    ? detailValue.GetString() ?? string.Empty
                    : string.Empty;
                _voiceParticipantCount = root.TryGetProperty("participantCount", out var countValue)
                                         && countValue.TryGetInt32(out var count)
                    ? Math.Clamp(count, 0, 32)
                    : 0;
                _voiceConnecting = _voiceEngineState is "STARTING" or "CONNECTING";
                var wasConnected = _voiceBridgeRunning;
                _voiceBridgeRunning = _voiceEngineState == "CONNECTED";
                if (_voiceBridgeRunning)
                {
                    _voiceSessionConnectedThisSession = true;
                    _voiceAutoReconnectNotBefore = DateTimeOffset.MinValue;
                    _voiceAutoReconnectDelaySeconds = 5;
                }
                if (wasConnected && !_voiceBridgeRunning)
                {
                    _voiceAutoReconnectNotBefore = DateTimeOffset.UtcNow.AddSeconds(5);
                    ClearVoiceRouteOffer("VOICE DISCONNECTED · ROUTE OFFERS CLEARED");
                    ResetVoiceQualityState();
                }
                VoiceRoomInviteStatusText.Text = _voiceEngineState switch
                {
                    "CONNECTED" => $"CONNECTED · {Math.Max(1, _voiceParticipantCount)} IN PRIVATE ROOM",
                    "ERROR" => "CONNECTION NEEDS ATTENTION · ROOM KEY REMAINS SESSION-ONLY",
                    "CONNECTING" or "STARTING" => "CONNECTING · MICROPHONE CONSENT REQUIRED",
                    _ when wasConnected => "DISCONNECTED · INVITE REMAINS READY THIS SESSION",
                    _ => VoiceRoomInviteStatusText.Text
                };
                if (wasConnected != _voiceBridgeRunning)
                {
                    AddTacticalEvent(
                        "VOICE",
                        _voiceBridgeRunning ? "Isley Voice connected" : "Isley Voice disconnected",
                        _voiceBridgeRunning
                            ? $"Private room · {Math.Max(1, _voiceParticipantCount)} participant(s)"
                            : _voiceEngineDetail,
                        warning: !_voiceBridgeRunning);
                }
            }
            else if (type == "voice-ptt"
                     && root.TryGetProperty("transmitting", out var transmittingValue)
                     && !transmittingValue.GetBoolean())
            {
                _voicePttHeld = false;
            }
            else if (type == "voice-network")
            {
                var networkState = root.TryGetProperty("state", out var networkStateValue)
                    ? (networkStateValue.GetString() ?? string.Empty).Trim().ToUpperInvariant()
                    : string.Empty;
                _voiceNetworkState = networkState is "NEW" or "CHECKING" or "CONNECTED" or "COMPLETED"
                    or "DISCONNECTED" or "FAILED" or "CLOSED"
                    ? networkState
                    : "WAITING";
                var networkRoute = root.TryGetProperty("route", out var networkRouteValue)
                    ? (networkRouteValue.GetString() ?? string.Empty).Trim().ToUpperInvariant()
                    : string.Empty;
                _voiceNetworkRoute = networkRoute is "TURN RELAY" or "DIRECT · NAT" or "DIRECT · LOCAL"
                    ? networkRoute
                    : string.Empty;
            }
            else if (type == "voice-quality")
            {
                _voiceQualityPeerCount = root.TryGetProperty("peerCount", out var peerCountValue)
                                         && peerCountValue.TryGetInt32(out var peerCount)
                    ? Math.Clamp(peerCount, 0, VoiceIntegrationLogic.MaximumParticipants)
                    : 0;
                _voiceQualitySampleCount = root.TryGetProperty("sampleCount", out var sampleCountValue)
                                           && sampleCountValue.TryGetInt32(out var sampleCount)
                    ? Math.Clamp(sampleCount, 0, _voiceQualityPeerCount)
                    : 0;
                _voiceQualityRoundTripMilliseconds = ReadVoiceQualityMetric(
                    root,
                    "roundTripMilliseconds",
                    5_000);
                _voiceQualityJitterMilliseconds = ReadVoiceQualityMetric(
                    root,
                    "jitterMilliseconds",
                    1_000);
                _voiceQualityPacketLossPercent = ReadVoiceQualityMetric(
                    root,
                    "packetLossPercent",
                    100);
                _voiceQualityAt = DateTimeOffset.UtcNow;
                _voicePeerQualities.Clear();
                if (root.TryGetProperty("peers", out var peersValue)
                    && peersValue.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in peersValue.EnumerateArray()
                                 .Take(VoicePeerQualityLogic.MaximumTrackedPeers))
                    {
                        var qualityPeerId = item.TryGetProperty("id", out var qualityIdValue)
                            ? VoiceIntegrationLogic.NormalizePeerId(qualityIdValue.GetString())
                            : string.Empty;
                        if (string.IsNullOrEmpty(qualityPeerId))
                        {
                            continue;
                        }

                        _voicePeerQualities[qualityPeerId] = new VoicePeerQualitySnapshot(
                            ReadVoiceQualityMetric(item, "roundTripMilliseconds", 5_000),
                            ReadVoiceQualityMetric(item, "jitterMilliseconds", 1_000),
                            ReadVoiceQualityMetric(item, "packetLossPercent", 100));
                    }
                }
                _voiceParticipantRosterSignature = string.Empty;
            }
            else if (type == "voice-devices")
            {
                ReadVoiceAudioDevices(root);
            }
            else if (type == "voice-device")
            {
                var state = root.TryGetProperty("state", out var deviceStateValue)
                    ? (deviceStateValue.GetString() ?? string.Empty).Trim().ToUpperInvariant()
                    : string.Empty;
                var label = root.TryGetProperty("label", out var deviceLabelValue)
                    ? VoiceIntegrationLogic.NormalizeInputDeviceLabel(deviceLabelValue.GetString(), 0)
                    : string.Empty;
                _voiceInputDeviceStatus = state switch
                {
                    "SWITCHING" => "SWITCHING · PTT MUTED",
                    "ACTIVE" => string.IsNullOrWhiteSpace(label) ? "MICROPHONE ACTIVE" : $"ACTIVE · {label}",
                    "NOT FOUND" => "DEVICE REMOVED · USING PREVIOUS",
                    "FAILED" => "SWITCH FAILED · USING PREVIOUS",
                    "DISCONNECTED" => "CONNECT TO CHOOSE",
                    _ => _voiceInputDeviceStatus
                };
            }
            else if (type == "voice-output-device")
            {
                var state = root.TryGetProperty("state", out var outputStateValue)
                    ? (outputStateValue.GetString() ?? string.Empty).Trim().ToUpperInvariant()
                    : string.Empty;
                var label = root.TryGetProperty("label", out var outputLabelValue)
                    ? VoiceIntegrationLogic.NormalizeOutputDeviceLabel(outputLabelValue.GetString(), 0)
                    : string.Empty;
                _voiceOutputDeviceStatus = state switch
                {
                    "SWITCHING" => "SWITCHING OUTPUT",
                    "ACTIVE" => string.IsNullOrWhiteSpace(label) ? "SPEAKER ACTIVE" : $"ACTIVE · {label}",
                    "NOT FOUND" => "DEVICE REMOVED · USING PREVIOUS",
                    "FAILED" => "SWITCH FAILED · USING PREVIOUS",
                    "UNSUPPORTED" => "WINDOWS DEFAULT · OUTPUT SELECTION UNAVAILABLE",
                    "DISCONNECTED" => "CONNECT TO CHOOSE",
                    _ => _voiceOutputDeviceStatus
                };
            }
            else if (type == "voice-meter")
            {
                var active = root.TryGetProperty("active", out var activeValue)
                             && activeValue.ValueKind == JsonValueKind.True;
                var level = root.TryGetProperty("level", out var levelValue)
                            && levelValue.ValueKind == JsonValueKind.Number
                            && levelValue.TryGetDouble(out var rawLevel)
                            && double.IsFinite(rawLevel)
                    ? (int)Math.Round(Math.Clamp(rawLevel, 0, 100))
                    : 0;
                _voiceMicLevel = active ? level : 0;
                _voiceMicClipped = active
                                    && root.TryGetProperty("clipped", out var clippedValue)
                                    && clippedValue.ValueKind == JsonValueKind.True;
                _voiceMicLevelAt = active ? DateTimeOffset.UtcNow : default;
            }
            else if (type == "voice-route-offer")
            {
                ReadVoiceRouteOffer(root);
            }
            else if (type == "voice-route-sent")
            {
                ReadVoiceRouteSent(root);
            }
            else if (type == "voice-participants")
            {
                ReadVoiceParticipants(root);
            }
            _voiceUiSignature = string.Empty;
            UpdateVoicePresentation();
        }
        catch
        {
            _voiceEngineState = "ERROR";
            _voiceEngineDetail = "INVALID BUILT-IN VOICE MESSAGE";
            _voiceUiSignature = string.Empty;
            UpdateVoicePresentation();
        }
    }

    private static double? ReadVoiceQualityMetric(
        JsonElement root,
        string propertyName,
        double maximum)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var number)
            || !double.IsFinite(number)
            || number < 0)
        {
            return null;
        }
        return Math.Clamp(number, 0, maximum);
    }

    private void ReadVoiceRouteOffer(JsonElement root)
    {
        var offerId = root.TryGetProperty("offerId", out var offerIdValue)
            ? offerIdValue.GetString()
            : null;
        var peerId = root.TryGetProperty("peerId", out var peerIdValue)
            ? peerIdValue.GetString()
            : null;
        var peerName = root.TryGetProperty("peerName", out var peerNameValue)
            ? peerNameValue.GetString()
            : null;
        var routeText = root.TryGetProperty("routeText", out var routeTextValue)
            ? routeTextValue.GetString()
            : null;
        if (!_voiceBridgeRunning
            || !VoiceRouteOfferLogic.TryCreateIncoming(
                offerId,
                peerId,
                peerName,
                routeText,
                DateTimeOffset.UtcNow,
                out var offer,
                out _))
        {
            _voiceRouteShareStatus = "INVALID OR OUT-OF-BOUNDS ROUTE OFFER BLOCKED";
            return;
        }

        if (_pendingVoiceRouteOffer is { } pending
            && string.Equals(pending.OfferId, offer.OfferId, StringComparison.Ordinal))
        {
            return;
        }

        _pendingVoiceRouteOffer = offer;
        _voiceRouteShareStatus =
            $"OFFER WAITING · {offer.Route.StopCount} STOPS · ACCEPT OR DECLINE";
        AddTacticalEvent(
            "VOICE",
            "Route offer received",
            $"{offer.Route.Kind} · {offer.Route.StopCount} stops · explicit acceptance required");
        _ = ShowHotkeyToastAsync(
            $"VOICE ROUTE OFFER · {offer.Route.StopCount} STOPS · OPEN VOICE",
            true);
    }

    private void ReadVoiceRouteSent(JsonElement root)
    {
        var offerId = root.TryGetProperty("offerId", out var offerIdValue)
            ? (offerIdValue.GetString() ?? string.Empty).Trim().ToLowerInvariant()
            : string.Empty;
        if (string.IsNullOrEmpty(_voiceRouteSendOfferId)
            || !string.Equals(offerId, _voiceRouteSendOfferId, StringComparison.Ordinal))
        {
            return;
        }

        _voiceRouteSendOfferId = string.Empty;
        var recipientCount = root.TryGetProperty("recipientCount", out var recipientCountValue)
                             && recipientCountValue.TryGetInt32(out var parsedCount)
            ? Math.Clamp(parsedCount, 0, 31)
            : 0;
        var state = root.TryGetProperty("state", out var stateValue)
            ? (stateValue.GetString() ?? string.Empty).Trim().ToUpperInvariant()
            : string.Empty;
        if (state == "SENT" && recipientCount > 0)
        {
            _voiceRouteShareStatus =
                $"ROUTE OFFER SENT PEER-TO-PEER · {recipientCount} RECIPIENT{(recipientCount == 1 ? string.Empty : "S")}";
            AddTacticalEvent(
                "VOICE",
                "Route offer sent",
                $"{recipientCount} room peer(s) · recipients must accept");
            _ = ShowHotkeyToastAsync(
                $"ROUTE OFFER SENT · {recipientCount} PEER{(recipientCount == 1 ? string.Empty : "S")}",
                true);
        }
        else
        {
            _voiceRouteShareStatus = state == "INVALID"
                ? "ROUTE OFFER BLOCKED BEFORE SEND"
                : "NO OPEN PEER CHANNEL · TRY AGAIN WHEN CONNECTED";
            _ = ShowHotkeyToastAsync(_voiceRouteShareStatus, false);
        }
    }

    private VoiceRouteOffer? CurrentVoiceRouteOffer()
    {
        if (_pendingVoiceRouteOffer is not { } offer)
        {
            return null;
        }

        if (!VoiceRouteOfferLogic.IsExpired(offer, DateTimeOffset.UtcNow))
        {
            return offer;
        }

        _pendingVoiceRouteOffer = null;
        _voiceRouteShareStatus = "ROUTE OFFER EXPIRED · NOTHING CHANGED";
        return null;
    }

    private void ClearVoiceRouteOffer(string status)
    {
        _pendingVoiceRouteOffer = null;
        _voiceRouteSendOfferId = string.Empty;
        _voiceRouteShareStatus = status;
        _voiceUiSignature = string.Empty;
    }

    private void UpdateVoiceRouteOfferControls(VoiceRouteOffer? pendingOffer)
    {
        var routeReady = TryBuildCurrentSharedRoute(out var sharedRoute);
        var peerReady = _voiceBridgeRunning && _voiceParticipants.Count > 0;
        var canShare = LiveMapServicesActive
                       && !_streamerMode
                       && peerReady
                       && routeReady
                       && string.IsNullOrEmpty(_voiceRouteSendOfferId);
        VoiceShareRouteButton.IsEnabled = canShare;
        VoiceShareRouteButton.Content = _voiceRouteSendOfferId.Length > 0
            ? "Sending route offer..."
            : routeReady
                ? $"Share {sharedRoute.StopCount}-stop {sharedRoute.Kind.ToLowerInvariant()}"
                : "Share current route";
        VoiceRouteShareStatusText.Text = _streamerMode
            ? "ROUTE SHARING HIDDEN IN STREAMER MODE"
            : !LiveMapServicesActive
            ? "LIVE MAP MODE REQUIRED FOR ROUTE SHARING"
                : !_voiceBridgeRunning
                    ? "CONNECT ISLEY VOICE TO SHARE A ROUTE"
                    : _voiceParticipants.Count == 0
                        ? "WAITING FOR ANOTHER ROOM PLAYER"
                        : !routeReady
                            ? "START A 2–12 STOP ROUTE, BREADCRUMB RETURN, OR ROAD / TRAIL COURSE"
                            : _voiceRouteShareStatus;

        var showOffer = pendingOffer.HasValue && !_streamerMode;
        VoiceRouteOfferPanel.Visibility = showOffer ? Visibility.Visible : Visibility.Collapsed;
        if (!showOffer)
        {
            return;
        }

        var offer = pendingOffer!.Value;
        var seconds = VoiceRouteOfferLogic.RemainingSeconds(offer, DateTimeOffset.UtcNow);
        VoiceRouteOfferTitleText.Text = "ROUTE OFFER WAITING";
        VoiceRouteOfferDetailText.Text =
            $"{VoiceRouteOfferLogic.Summary(offer, streamerMode: false)} · {seconds}s left\n" +
            "Accept validates and replaces the current route. Decline changes nothing.";
        VoiceRouteOfferAcceptButton.IsEnabled =
            LiveMapServicesActive && _followControllerInstalled && !_streamerMode;
        VoiceRouteOfferAcceptButton.Content = VoiceRouteOfferAcceptButton.IsEnabled
            ? "ACCEPT ROUTE"
            : "MAP REQUIRED";
    }

    private void PostVoiceCommand(object command)
    {
        try
        {
            VoiceWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(command));
        }
        catch
        {
            _voiceEngineState = "ERROR";
            _voiceEngineDetail = "VOICE ENGINE MESSAGE FAILED";
        }
    }

    private void UpdateVoicePresentation()
    {
        if (VoiceHudBorder is null
            || VoiceBridgeStatusText is null
            || VoiceServerCheckStatusText is null
            || VoiceServerCheckButton is null
            || VoiceServerInputBox is null
            || VoiceLocalHostButton is null)
        {
            return;
        }
        UpdateVoiceParticipantRoster();

        var foreground = GetPlayFocusForeground();
        var allowedForeground = foreground is PlayFocusForeground.Game or PlayFocusForeground.Mapper;
        var keyObserverReady = _voiceKeyboardHook != 0;
        var hasError = _voiceEngineState == "ERROR";
        var presentation = VoiceIntegrationLogic.Present(
            _voiceEnabled,
            _voiceBridgeRunning,
            _voiceConnecting,
            hasError,
            allowedForeground,
            _voicePttHeld,
            _voiceHudVisible,
            _streamerMode,
            _voicePttKeyIndex,
            _voiceParticipantCount);
        var localTransmit = keyObserverReady && presentation.Transmitting;
        var remoteSpeakerCount = _voiceParticipants.Count(participant =>
            participant.Talking && !participant.Muted && participant.VolumePercent > 0);
        var pendingVoiceRouteOffer = CurrentVoiceRouteOffer();
        var pendingOfferSeconds = pendingVoiceRouteOffer is { } pendingOffer
            ? VoiceRouteOfferLogic.RemainingSeconds(pendingOffer, DateTimeOffset.UtcNow)
            : 0;
        var voiceActive = localTransmit || remoteSpeakerCount > 0 || pendingVoiceRouteOffer.HasValue;
        var keyLabel = VoiceIntegrationLogic.KeyLabel(_voicePttKeyIndex);
        var voiceRange = VoiceIntegrationLogic.Range(_voiceRangeIndex);
        var micMeterAge = _voiceMicLevelAt == default
            ? int.MaxValue
            : (int)Math.Clamp(
                (DateTimeOffset.UtcNow - _voiceMicLevelAt).TotalMilliseconds,
                0,
                int.MaxValue);
        var micMeter = VoiceIntegrationLogic.PresentMicMeter(
            _voiceMicMeterEnabled,
            _voiceBridgeRunning,
            _voiceMicLevel,
            _voiceMicClipped,
            micMeterAge);
        var qualityAge = _voiceQualityAt == default
            ? int.MaxValue
            : (int)Math.Clamp(
                (DateTimeOffset.UtcNow - _voiceQualityAt).TotalMilliseconds,
                0,
                int.MaxValue);
        var voiceQuality = VoiceIntegrationLogic.PresentQuality(
            _voiceQualityMonitorEnabled,
            _voiceBridgeRunning,
            _voiceQualityPeerCount,
            _voiceQualitySampleCount,
            _voiceQualityRoundTripMilliseconds,
            _voiceQualityJitterMilliseconds,
            _voiceQualityPacketLossPercent,
            qualityAge);
        var serverCheck = VoiceServerReadinessClient.Present(
            _voiceServerCheckState,
            _voiceServerReadiness);
        // Honest CONNECTING sub-state: host start, backoff countdown, room join.
        // Display-only; the connect/reconnect logic above is untouched.
        var voiceConnectPhase = VoiceConnectPhaseLogic.Present(
            _voiceEnabled,
            _voiceBridgeRunning,
            _voiceConnecting,
            _voiceAutoConnectInFlight,
            _voiceEngineState,
            IsBundledLocalVoiceServerUrl(_voiceServerUrl),
            _voiceServerCheckState == VoiceServerCheckState.Ready,
            VoiceConnectPhaseLogic.AutoRetryArmed(
                _voiceEnabled,
                _voiceAutoOpen,
                _streamerMode,
                _voiceUserDisconnectedThisSession,
                _voiceSessionConnectedThisSession,
                _voiceBridgeRunning,
                _voiceConnecting,
                _voiceAutoConnectInFlight,
                _voiceEngineState),
            VoiceConnectPhaseLogic.RetrySecondsRemaining(
                DateTimeOffset.UtcNow,
                _voiceAutoReconnectNotBefore));
        var voiceProblem = !keyObserverReady || hasError || voiceQuality.Severity >= 2;
        var hudPriority = CurrentHudPriorityPresentation(voiceActive, voiceProblem);
        var showHud = presentation.ShowHud && !hudPriority.SuppressIdleVoice;
        var spatialSummary = _voiceProximityEnabled
            ? $"{VoiceIntegrationLogic.SpatialModeLabel(true)} · {voiceRange.Label}"
            : VoiceIntegrationLogic.SpatialModeLabel(false);
        var signature = string.Join('|', presentation.State, presentation.Transmitting, showHud,
            keyLabel, keyObserverReady, foreground, _voiceEngineState, _voiceEngineDetail,
            _voiceParticipantCount, _voiceDeafened, _voiceNetworkState, _voiceNetworkRoute,
            _voiceTurnRelayEnabled, _voiceProximityEnabled, _voiceRangeIndex,
            _voiceEchoCancellation, _voiceNoiseSuppression, _voiceAutoGainControl,
            _voiceSelectedInputDeviceId, _voiceInputDeviceStatus, _voiceInputDevices.Count,
            _voiceSelectedOutputDeviceId, _voiceOutputDeviceStatus, _voiceOutputDevices.Count,
            _voiceOutputSelectionSupported,
            _voiceMicMeterEnabled, micMeter.Label, micMeter.Level, micMeter.Severity,
            micMeter.Active, micMeter.Fresh, _voiceQualityMonitorEnabled,
            voiceQuality.Label, voiceQuality.Detail, voiceQuality.Severity, voiceQuality.Fresh,
            serverCheck.Label, serverCheck.Detail, serverCheck.Severity,
            _voiceServerCheckInFlight, _voiceServerCheckedUrl,
            voiceConnectPhase.Phase, voiceConnectPhase.Pill,
            remoteSpeakerCount,
            pendingVoiceRouteOffer?.OfferId, pendingOfferSeconds / 5, _voiceRouteShareStatus,
            _voiceRouteSendOfferId, _routeStopCount, _routeStops.Count, _routePlanSource);
        if (string.Equals(signature, _voiceUiSignature, StringComparison.Ordinal)) return;
        _voiceUiSignature = signature;

        VoiceHudBorder.Visibility = showHud ? Visibility.Visible : Visibility.Collapsed;
        VoiceHudTitle.Text = keyObserverReady
            ? !localTransmit && remoteSpeakerCount > 0
                ? $"ISLEY VOICE · {remoteSpeakerCount} TALKING"
                : !localTransmit && pendingVoiceRouteOffer is { } offer
                    ? $"ROUTE OFFER · {offer.Route.StopCount} STOPS"
                : !voiceActive && voiceQuality.Severity >= 2
                    ? "VOICE QUALITY POOR"
                : presentation.Heading
            : "PTT OBSERVER UNAVAILABLE";
        VoiceHudDetail.Text = keyObserverReady
            ? !localTransmit && remoteSpeakerCount > 0
                ? $"{Math.Max(1, _voiceParticipantCount)} IN ROOM · {spatialSummary}"
                : !localTransmit && pendingVoiceRouteOffer.HasValue
                    ? $"OPEN VOICE TO REVIEW · {pendingOfferSeconds}s LEFT"
                : !voiceActive && voiceQuality.Severity >= 2
                    ? "OPEN VOICE · CHECK QUALITY"
                : _voiceBridgeRunning ? $"{presentation.Detail} · {spatialSummary}" : presentation.Detail
            : "RESTART ISLEY TO RESTORE";
        VoiceHudKeyText.Text = keyLabel;
        var accent = hasError || !keyObserverReady || voiceQuality.Severity >= 2
            ? Color.FromRgb(255, 180, 74)
            : voiceActive
                ? Color.FromRgb(34, 211, 238)
                : _voiceBridgeRunning
                    ? Color.FromRgb(88, 214, 141)
                    : Color.FromRgb(100, 116, 139);
        var accentBrush = new SolidColorBrush(accent);
        VoiceHudStatusDot.Fill = accentBrush;
        VoiceHudBorder.BorderBrush = accentBrush;
        VoiceBridgeStatusDot.Fill = accentBrush;
        VoiceHudKeyText.Foreground = voiceActive
            ? (Brush)FindResource("PrimaryTextBrush")
            : (Brush)FindResource("AccentBrush");

        VoiceBridgeStatusText.Text = !_voiceEnabled
            ? "ISLEY VOICE DISABLED"
            : _voiceBridgeRunning
                ? $"ISLEY VOICE CONNECTED · {Math.Max(1, _voiceParticipantCount)}"
                : voiceConnectPhase.Phase != VoiceConnectPhase.None
                    ? voiceConnectPhase.BridgeLabel
                    : _voiceConnecting
                    ? "ISLEY VOICE CONNECTING"
                    : hasError ? "ISLEY VOICE NEEDS ATTENTION" : "BUILT-IN VOICE READY";
        VoiceBridgeDetailText.Text = !_voiceEnabled
            ? "Enable voice to connect a private room"
            : voiceConnectPhase.Phase != VoiceConnectPhase.None
                ? voiceConnectPhase.Detail
            : string.IsNullOrWhiteSpace(_voiceEngineDetail)
                ? "Microphone is off until Connect is pressed"
                : _voiceEngineDetail;
        VoiceClientStateText.Text = voiceConnectPhase.Phase != VoiceConnectPhase.None
            ? voiceConnectPhase.Pill
            : !_voiceEnabled
            ? "OFF"
            : _voiceBridgeRunning ? "CONNECTED" : _voiceConnecting ? "CONNECTING" : hasError ? "ERROR" : "READY";
        VoiceClientStateText.ToolTip = voiceConnectPhase.Phase != VoiceConnectPhase.None
            ? voiceConnectPhase.Detail
            : null;
        VoiceClientStateText.Foreground = _voiceBridgeRunning
            ? (Brush)FindResource("SuccessBrush")
            : hasError ? (Brush)FindResource("WarningBrush") : (Brush)FindResource("AccentBrush");
        VoicePttObserverStateText.Text = keyObserverReady ? $"{keyLabel} · READY" : $"{keyLabel} · RESTART";
        VoiceFocusStateText.Text = foreground switch
        {
            PlayFocusForeground.Game => "THE ISLE",
            PlayFocusForeground.Mapper => "ISLEY",
            _ => "PAUSED"
        };
        VoiceFocusStateText.Foreground = allowedForeground
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("SecondaryTextBrush");
        VoiceNetworkStateText.Text = !_voiceBridgeRunning
            ? "WAITING"
            : string.IsNullOrEmpty(_voiceNetworkRoute)
                ? _voiceNetworkState
                : _voiceNetworkRoute;
        VoiceNetworkStateText.Foreground = _voiceNetworkState is "FAILED" or "DISCONNECTED"
            ? (Brush)FindResource("WarningBrush")
            : _voiceNetworkState is "CONNECTED" or "COMPLETED"
                ? (Brush)FindResource("SuccessBrush")
                : (Brush)FindResource("SecondaryTextBrush");
        VoiceQualityStateText.Text = voiceQuality.Label;
        VoiceQualityStateText.ToolTip = voiceQuality.Detail;
        VoiceQualityStateText.Foreground = voiceQuality.Severity switch
        {
            2 => new SolidColorBrush(Color.FromRgb(248, 113, 113)),
            1 => (Brush)FindResource("WarningBrush"),
            _ when voiceQuality.Fresh => (Brush)FindResource("SuccessBrush"),
            _ => (Brush)FindResource("SecondaryTextBrush")
        };
        VoiceServerCheckStatusText.Text = $"{serverCheck.Label} · {serverCheck.Detail}";
        VoiceServerCheckStatusText.Foreground = serverCheck.Severity switch
        {
            2 => new SolidColorBrush(Color.FromRgb(248, 113, 113)),
            1 => (Brush)FindResource("WarningBrush"),
            _ when serverCheck.CanConnect => (Brush)FindResource("SuccessBrush"),
            _ => (Brush)FindResource("SecondaryTextBrush")
        };
        VoiceServerCheckButton.Content = _voiceServerCheckInFlight ? "CHECKING" : "CHECK SERVER";
        VoiceServerCheckButton.IsEnabled =
            !_voiceServerCheckInFlight && !_voiceBridgeRunning && !_voiceConnecting;
        VoiceServerInputBox.IsEnabled =
            !_voiceServerCheckInFlight && !_voiceBridgeRunning && !_voiceConnecting;
        VoiceLocalHostButton.IsEnabled =
            !_voiceServerCheckInFlight && !_voiceBridgeRunning && !_voiceConnecting;
        VoiceLocalHostButton.Content = _voiceLocalHostProcess is { HasExited: false }
            ? "STOP LOCAL HOST"
            : "LOCAL HOST · AUTO";
        VoiceEnabledButton.Content = _voiceEnabled ? "Voice on" : "Voice off";
        if (VoiceAutoOpenButton is not null)
        {
            VoiceAutoOpenButton.Content = _voiceAutoOpen ? "Auto proximity · On" : "Auto proximity · Off";
            VoiceAutoOpenButton.ToolTip = _voiceAutoOpen
                ? "Proximity voice connects automatically; hold PTT to talk. Stop voice pauses auto until you start again."
                : "Proximity voice stays off until you press Start voice";
            SetToggleButtonState(VoiceAutoOpenButton, _voiceAutoOpen);
        }
        VoicePttKeyButton.Content = $"PTT key · {keyLabel}";
        VoiceHudButton.Content = _voiceHudVisible ? "HUD · ON" : "HUD · OFF";
        VoiceQualityButton.Content = _voiceQualityMonitorEnabled ? "QUALITY · ON" : "QUALITY · OFF";
        VoiceConnectButton.Content = _voiceBridgeRunning || _voiceConnecting
            ? "Stop voice"
            : _voiceServerCheckInFlight
                ? "Checking voice"
            : "Start voice";
        VoiceConnectButton.IsEnabled = _voiceEnabled && !_voiceServerCheckInFlight;
        VoiceDeafenButton.IsEnabled = _voiceBridgeRunning;
        VoiceDeafenButton.Content = _voiceDeafened ? "Deafen · On" : "Deafen · Off";
        VoiceSpatialModeButton.Content = _voiceProximityEnabled
            ? "Proximity · On"
            : "Room radio · Private";
        VoiceRangeButton.Content = _voiceProximityEnabled
            ? $"Range · {voiceRange.Label} · {voiceRange.MaxDistance}"
            : "Range · Radio mode";
        VoiceRangeButton.IsEnabled = _voiceProximityEnabled;
        VoiceEchoCancellationButton.Content = _voiceEchoCancellation ? "ECHO ON" : "ECHO OFF";
        VoiceNoiseSuppressionButton.Content = _voiceNoiseSuppression ? "NOISE ON" : "NOISE OFF";
        VoiceAutoGainButton.Content = _voiceAutoGainControl ? "GAIN ON" : "GAIN OFF";
        VoiceInputDeviceComboBox.IsEnabled = _voiceBridgeRunning && _voiceInputDevices.Count > 0;
        VoiceInputDeviceRefreshButton.IsEnabled = _voiceBridgeRunning;
        VoiceInputDeviceStatusText.Text = _voiceInputDeviceStatus;
        VoiceOutputDeviceComboBox.IsEnabled =
            _voiceBridgeRunning && _voiceOutputSelectionSupported && _voiceOutputDevices.Count > 0;
        VoiceOutputDeviceRefreshButton.IsEnabled = _voiceBridgeRunning;
        VoiceOutputDeviceStatusText.Text = _voiceOutputDeviceStatus;
        VoiceMicMeterButton.Content = _voiceMicMeterEnabled ? "MIC METER · ON" : "MIC METER · OFF";
        VoiceMicMeterStatusText.Text = micMeter.Active
            ? $"{micMeter.Label} · {micMeter.Level}%"
            : micMeter.Label;
        VoiceMicLevelBar.Value = micMeter.Level;
        var micMeterBrush = micMeter.Severity switch
        {
            2 => new SolidColorBrush(Color.FromRgb(248, 113, 113)),
            1 => (Brush)FindResource("WarningBrush"),
            _ when micMeter.Active && micMeter.Level > 1 => (Brush)FindResource("SuccessBrush"),
            _ => (Brush)FindResource("SecondaryTextBrush")
        };
        VoiceMicMeterStatusText.Foreground = micMeterBrush;
        VoiceMicLevelBar.Foreground = micMeterBrush;
        if (micMeter.Severity == 2 && _voiceMicPresentedSeverity != 2)
        {
            VoiceMicMeterStatusText.BeginAnimation(
                OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(
                    0.35,
                    1,
                    TimeSpan.FromMilliseconds(180))
                {
                    AutoReverse = true,
                    FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
                });
        }
        _voiceMicPresentedSeverity = micMeter.Severity;
        VoiceNatAssistButton.Content = _voiceNatAssist ? "NAT assist · On" : "NAT assist · Off";
        VoiceTurnRelayButton.Content = _voiceTurnRelayEnabled ? "TURN relay · On" : "TURN relay · Off";
        VoiceTurnRelayPanel.Visibility = _voiceTurnRelayEnabled ? Visibility.Visible : Visibility.Collapsed;
        SetToggleButtonState(VoiceEnabledButton, _voiceEnabled);
        SetToggleButtonState(VoiceHudButton, _voiceHudVisible);
        SetToggleButtonState(VoiceQualityButton, _voiceQualityMonitorEnabled);
        SetToggleButtonState(VoiceDeafenButton, _voiceDeafened);
        SetToggleButtonState(VoiceSpatialModeButton, _voiceProximityEnabled);
        SetToggleButtonState(VoiceRangeButton, _voiceProximityEnabled);
        SetToggleButtonState(VoiceEchoCancellationButton, _voiceEchoCancellation);
        SetToggleButtonState(VoiceNoiseSuppressionButton, _voiceNoiseSuppression);
        SetToggleButtonState(VoiceAutoGainButton, _voiceAutoGainControl);
        SetToggleButtonState(VoiceMicMeterButton, _voiceMicMeterEnabled);
        SetToggleButtonState(VoiceNatAssistButton, _voiceNatAssist);
        SetToggleButtonState(VoiceTurnRelayButton, _voiceTurnRelayEnabled);
        UpdateVoiceNatCoachPresentation();
        UpdateVoiceRouteOfferControls(pendingVoiceRouteOffer);
        PostVoiceCommand(new { type = "ptt", held = localTransmit });
        _hudDockUiSignature = string.Empty;
        UpdateHudDockLayout();
        UpdateLiveHealthStrip();
    }

    private void UpdateVoiceNatCoachPresentation()
    {
        if (VoiceNatCoachText is null)
        {
            return;
        }

        var failed = _voiceBridgeRunning
                     && string.Equals(_voiceNetworkState, "FAILED", StringComparison.OrdinalIgnoreCase);
        if (!failed)
        {
            VoiceNatCoachText.Visibility = Visibility.Collapsed;
            VoiceNatCoachText.Text = string.Empty;
            _voiceNatCoachSignature = string.Empty;
            return;
        }

        var tip = !_voiceNatAssist
            ? "NAT path failed · turn NAT assist on, then reconnect voice."
            : !_voiceTurnRelayEnabled
                ? "NAT path failed · enable TURN relay with your session credentials, then reconnect."
                : "NAT path failed · confirm TURN URL/credentials, then reconnect voice.";
        VoiceNatCoachText.Text = tip;
        VoiceNatCoachText.Visibility = Visibility.Visible;
        if (!string.Equals(tip, _voiceNatCoachSignature, StringComparison.Ordinal))
        {
            _voiceNatCoachSignature = tip;
            _ = ShowHotkeyToastAsync("VOICE NAT FAILED · CHECK ASSIST / TURN", false);
        }
    }

    private nint VoiceKeyboardHook(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && lParam != 0)
        {
            var keyboard = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
            if (keyboard.VkCode == VoiceIntegrationLogic.KeyCode(_voicePttKeyIndex))
            {
                var message = unchecked((int)(long)wParam);
                var held = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
                var released = message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;
                var nextState = VoiceIntegrationLogic.ResolveObservedKeyState(
                    _voicePttHeld,
                    held,
                    released);
                if ((held || released) && nextState != _voicePttHeld)
                {
                    // Commit the edge before deferring the visual refresh. A fast synthetic
                    // key-down/key-up pair (for example, Ctrl+V paste) can otherwise enqueue
                    // only the down edge and leave the PTT indicator latched on.
                    _voicePttHeld = nextState;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _voiceUiSignature = string.Empty;
                        UpdateVoicePresentation();
                    }));
                }
            }
        }

        return NativeMethods.CallNextHookEx(_voiceKeyboardHook, code, wParam, lParam);
    }

    private void VoiceToolsTabButton_Click(object sender, RoutedEventArgs e) => ShowToolsSection("voice");

    private void HudVoiceButton_Click(object sender, RoutedEventArgs e)
    {
        VoiceHudButton_Click(VoiceHudButton, new RoutedEventArgs());
        UpdateHudSurfaceControls();
    }

    private async void VoiceEnabledButton_Click(object sender, RoutedEventArgs e)
    {
        _voiceEnabled = !_voiceEnabled;
        if (!_voiceEnabled)
        {
            _voicePttHeld = false;
            DisarmVoiceMicrophonePermission();
            PostVoiceCommand(new { type = "disconnect" });
            _voiceBridgeRunning = false;
            _voiceConnecting = false;
            _voiceEngineState = "READY";
            _voiceEngineDetail = "VOICE DISABLED";
            ResetVoiceMicMeterState();
            ResetVoiceQualityState();
            SetVoiceInputDeviceOptions([], _voiceSelectedInputDeviceId, "CONNECT TO CHOOSE");
            SetVoiceOutputDeviceOptions(
                [],
                _voiceSelectedOutputDeviceId,
                _voiceOutputSelectionSupported,
                "CONNECT TO CHOOSE");
            _voiceParticipants.Clear();
            _voicePeerVolumeRestoreAppliedPeerIds.Clear();
            _voiceParticipantRosterSignature = string.Empty;
            ClearVoiceRouteOffer("VOICE DISABLED · ROUTE OFFERS CLEARED");
            UpdateVoiceParticipantRoster();
        }
        else
        {
            _voiceUserDisconnectedThisSession = false;
            if (_voiceAutoOpen)
            {
                await TryAutoConnectProximityVoiceAsync();
            }
        }
        _voiceUiSignature = string.Empty;
        RefreshVoiceStatus();
        SaveSettings();
    }

    private async void VoiceAutoOpenButton_Click(object sender, RoutedEventArgs e)
    {
        _voiceAutoOpen = !_voiceAutoOpen;
        if (_voiceAutoOpen)
        {
            _voiceUserDisconnectedThisSession = false;
            _voiceProximityEnabled = true;
            await TryAutoConnectProximityVoiceAsync();
        }
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
        SaveSettings();
        await ShowHotkeyToastAsync(
            _voiceAutoOpen
                ? "PROXIMITY VOICE · AUTO ON"
                : "PROXIMITY VOICE · MANUAL START",
            true);
    }

    private void VoicePttKeyButton_Click(object sender, RoutedEventArgs e)
    {
        _voicePttKeyIndex = (_voicePttKeyIndex + 1) % VoiceIntegrationLogic.KeyCodes.Length;
        _voicePttHeld = false;
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
        SaveSettings();
    }

    private void VoiceHudButton_Click(object sender, RoutedEventArgs e)
    {
        _voiceHudVisible = !_voiceHudVisible;
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
        UpdateHudSurfaceControls();
        SaveSettings();
    }

    private async void VoiceLocalHostButton_Click(object sender, RoutedEventArgs e)
    {
        if (_voiceLocalHostProcess is { HasExited: false })
        {
            try { _voiceLocalHostProcess.Kill(entireProcessTree: true); } catch { }
            _voiceLocalHostProcess.Dispose();
            _voiceLocalHostProcess = null;
            _voiceServerCheckState = VoiceServerCheckState.Unchecked;
            _voiceServerReadiness = null;
            _voiceServerCheckedUrl = string.Empty;
            _voiceUiSignature = string.Empty;
            UpdateVoicePresentation();
            await ShowHotkeyToastAsync("LOCAL ISLEY VOICE HOST STOPPED", true);
            return;
        }

        await EnsureBundledVoiceHostReadyAsync(showToast: true);
    }

    private async void VoiceServerCheckButton_Click(object sender, RoutedEventArgs e) =>
        await CheckVoiceServerReadinessAsync(userInitiated: true);

    private async void VoiceConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_voiceEnabled) return;
        if (_voiceBridgeRunning || _voiceConnecting)
        {
            _voiceUserDisconnectedThisSession = true;
            DisarmVoiceMicrophonePermission();
            PostVoiceCommand(new { type = "disconnect" });
            _voiceBridgeRunning = false;
            _voiceConnecting = false;
            _voiceEngineState = "READY";
            _voiceEngineDetail = "USER DISCONNECTED";
            ResetVoiceMicMeterState();
            ResetVoiceQualityState();
            SetVoiceInputDeviceOptions([], _voiceSelectedInputDeviceId, "CONNECT TO CHOOSE");
            SetVoiceOutputDeviceOptions(
                [],
                _voiceSelectedOutputDeviceId,
                _voiceOutputSelectionSupported,
                "CONNECT TO CHOOSE");
            _voiceParticipants.Clear();
            _voicePeerVolumeRestoreAppliedPeerIds.Clear();
            _voiceParticipantRosterSignature = string.Empty;
            ClearVoiceRouteOffer("VOICE DISCONNECTED · ROUTE OFFERS CLEARED");
            UpdateVoiceParticipantRoster();
            _voiceUiSignature = string.Empty;
            UpdateVoicePresentation();
            return;
        }

        _voiceUserDisconnectedThisSession = false;
        await ConnectVoiceSessionAsync(userInitiated: true);
    }

    private async Task TryAutoConnectProximityVoiceAsync()
    {
        if (!_voiceEnabled
            || !_voiceAutoOpen
            || _streamerMode
            || _voiceUserDisconnectedThisSession
            || _voiceBridgeRunning
            || _voiceConnecting
            || _voiceAutoConnectInFlight)
        {
            return;
        }

        await SyncProximityVoiceLobbyAsync(reconnectIfNeeded: false);
        _voiceProximityEnabled = true;
        await ConnectVoiceSessionAsync(userInitiated: false);
    }

    private async Task SyncProximityVoiceLobbyAsync(bool reconnectIfNeeded)
    {
        var liveServerId = string.Equals(_isleyRelayState, "live", StringComparison.OrdinalIgnoreCase)
            ? _isleyRelayJoin?.ServerId
            : null;
        if (!VoiceProximityRoomLogic.TryResolveAutoRoomSecret(
                liveServerId,
                _voiceRoomSecret,
                out var roomSecret))
        {
            return;
        }

        var lobbyChanged = !string.Equals(roomSecret, _voiceRoomSecret, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                _voiceProximityLobbyServerId,
                liveServerId ?? string.Empty,
                StringComparison.Ordinal);
        _voiceRoomSecret = roomSecret;
        VoiceRoomKeyInputBox.Text = _voiceRoomSecret;
        _voiceProximityLobbyServerId = liveServerId ?? string.Empty;
        if (!string.IsNullOrEmpty(liveServerId))
        {
            VoiceRoomInviteStatusText.Text =
                "SERVER PROXIMITY LOBBY · AUTO · HOLD PTT TO TALK";
            _voiceEngineDetail = "PROXIMITY LOBBY READY";
        }
        else if (!_voiceBridgeRunning && !_voiceConnecting)
        {
            VoiceRoomInviteStatusText.Text =
                "AUTO PROXIMITY READY · COPY INVITE FOR TRUSTED PLAYERS";
        }

        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
        if (reconnectIfNeeded
            && lobbyChanged
            && _voiceEnabled
            && _voiceAutoOpen
            && !_streamerMode
            && !_voiceUserDisconnectedThisSession)
        {
            if (_voiceBridgeRunning || _voiceConnecting)
            {
                PostVoiceCommand(new { type = "disconnect" });
                _voiceBridgeRunning = false;
                _voiceConnecting = false;
            }
            await ConnectVoiceSessionAsync(userInitiated: false);
        }
    }

    private async Task ConnectVoiceSessionAsync(bool userInitiated)
    {
        if (!_voiceEnabled || _voiceBridgeRunning || _voiceConnecting || _voiceAutoConnectInFlight)
        {
            return;
        }

        _voiceAutoConnectInFlight = true;
        try
        {
        var requestedServer = VoiceServerInputBox.Text.Trim();
        var normalizedServer = NormalizeVoiceServerUrl(requestedServer);
        if (!string.Equals(normalizedServer, requestedServer, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(requestedServer.TrimEnd('/'), normalizedServer.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            await ShowHotkeyToastAsync("VOICE SERVER MUST USE WSS OR LOCALHOST WS", false);
            return;
        }

        _voiceServerUrl = normalizedServer;
        VoiceServerInputBox.Text = normalizedServer;
        var serverReady = IsBundledLocalVoiceServerUrl(normalizedServer)
            ? await EnsureBundledVoiceHostReadyAsync(showToast: false)
            : await CheckVoiceServerReadinessAsync(userInitiated: false);
        if (!serverReady)
        {
            await ShowHotkeyToastAsync("VOICE SERVER NOT READY · MICROPHONE KEPT OFF", false);
            return;
        }
        var normalizedName = Regex.Replace(VoiceDisplayNameInputBox.Text ?? string.Empty, @"\s+", " ").Trim();
        normalizedName = Regex.Replace(normalizedName, @"[^\p{L}\p{N} _.'-]", string.Empty);
        if (string.IsNullOrWhiteSpace(normalizedName)) normalizedName = "Isley Player";
        VoiceDisplayNameInputBox.Text = normalizedName[..Math.Min(32, normalizedName.Length)];
        if (!VoiceInviteLogic.TryNormalizeRoomSecret(_voiceRoomSecret, out var normalizedRoomSecret))
        {
            normalizedRoomSecret = NewVoiceSecret(12);
        }
        _voiceRoomSecret = normalizedRoomSecret;
        VoiceRoomKeyInputBox.Text = _voiceRoomSecret;

        VoiceRelayConfig? relayConfig = null;
        if (_voiceTurnRelayEnabled)
        {
            if (!VoiceRelayLogic.TryCreate(
                    VoiceTurnUrlInputBox.Text,
                    VoiceTurnUsernameInputBox.Text,
                    VoiceTurnCredentialInputBox.Password,
                    out var validatedRelay,
                    out var relayError))
            {
                await ShowHotkeyToastAsync(relayError, false);
                return;
            }

            relayConfig = validatedRelay;
            VoiceTurnUrlInputBox.Text = validatedRelay.Url;
            VoiceTurnUsernameInputBox.Text = validatedRelay.Username;
        }

        _voiceConnecting = true;
        _voiceEngineState = "STARTING";
        _voiceEngineDetail = "LOADING BUILT-IN ENGINE";
        ResetVoiceMicMeterState();
        ResetVoiceQualityState();
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
        if (!await InitializeVoiceEngineAsync())
        {
            DisarmVoiceMicrophonePermission();
            await ShowHotkeyToastAsync("BUILT-IN VOICE COULD NOT START", false);
            return;
        }

        ArmVoiceMicrophonePermission();
        _voiceConnecting = true;
        _voiceEngineState = "CONNECTING";
        _voiceEngineDetail = "REQUESTING MICROPHONE";
        VoiceRoomInviteStatusText.Text = "CONNECTING · MICROPHONE CONSENT REQUIRED";
        // Auto proximity keeps distance audio on; users can still switch to room radio after connect.
        if (!userInitiated)
        {
            _voiceProximityEnabled = true;
        }
        PostVoiceCommand(new
        {
            type = "connect",
            serverUrl = _voiceServerUrl,
            roomSecret = _voiceRoomSecret,
            peerId = _voicePeerId,
            displayName = VoiceDisplayNameInputBox.Text,
            natAssist = _voiceNatAssist,
            proximityEnabled = _voiceProximityEnabled,
            proximityMaxDistance = VoiceIntegrationLogic.Range(_voiceRangeIndex).MaxDistance,
            echoCancellation = _voiceEchoCancellation,
            noiseSuppression = _voiceNoiseSuppression,
            autoGainControl = _voiceAutoGainControl,
            micMeterEnabled = _voiceMicMeterEnabled,
            qualityMonitorEnabled = _voiceQualityMonitorEnabled,
            inputDeviceId = _voiceSelectedInputDeviceId,
            outputDeviceId = _voiceSelectedOutputDeviceId,
            turnRelay = relayConfig.HasValue,
            turnUrl = relayConfig?.Url ?? string.Empty,
            turnUsername = relayConfig?.Username ?? string.Empty,
            turnCredential = relayConfig?.Credential ?? string.Empty
        });
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
        SaveSettings();
        await ShowHotkeyToastAsync(
            userInitiated
                ? "ISLEY VOICE · CONNECTING"
                : "PROXIMITY VOICE · AUTO CONNECTING",
            true);
        }
        finally
        {
            _voiceAutoConnectInFlight = false;
        }
    }

    private async void VoiceNewRoomButton_Click(object sender, RoutedEventArgs e)
    {
        PrepareVoiceRoomChange("NEW PRIVATE ROOM READY");
        _voiceRoomSecret = NewVoiceSecret(12);
        VoiceRoomKeyInputBox.Text = _voiceRoomSecret;
        VoiceRoomInviteStatusText.Text = "NEW PRIVATE ROOM · COPY AN INVITE FOR TRUSTED PLAYERS";
        await ShowHotkeyToastAsync("NEW PRIVATE VOICE ROOM READY", true);
    }

    private async void VoiceCopyRoomButton_Click(object sender, RoutedEventArgs e)
    {
        if (!VoiceInviteLogic.TryCreate(
                VoiceServerInputBox.Text,
                _voiceRoomSecret,
                out var invite,
                out var inviteWarning))
        {
            await ShowHotkeyToastAsync(inviteWarning, false);
            return;
        }

        try
        {
            Clipboard.SetText(invite, TextDataFormat.UnicodeText);
            VoiceRoomInviteStatusText.Text = string.IsNullOrEmpty(inviteWarning)
                ? "INVITE COPIED · SERVER + PRIVATE ROOM · SHARE ONLY WITH TRUSTED PLAYERS"
                : inviteWarning;
            await ShowHotkeyToastAsync(
                string.IsNullOrEmpty(inviteWarning)
                    ? "PRIVATE ISLEY VOICE INVITE COPIED"
                    : inviteWarning,
                string.IsNullOrEmpty(inviteWarning));
        }
        catch
        {
            await ShowHotkeyToastAsync("CLIPBOARD UNAVAILABLE", false);
        }
        finally
        {
            invite = string.Empty;
        }
    }

    private async void VoiceJoinInviteButton_Click(object sender, RoutedEventArgs e) =>
        await PasteVoiceInviteFromClipboardAsync(showToast: true);

    private async Task<bool> PasteVoiceInviteFromClipboardAsync(bool showToast)
    {
        string clipboardText;
        try
        {
            if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                if (showToast) await ShowHotkeyToastAsync("NO ISLEY VOICE INVITE ON CLIPBOARD", false);
                return false;
            }
            clipboardText = Clipboard.GetText(TextDataFormat.UnicodeText);
        }
        catch
        {
            if (showToast) await ShowHotkeyToastAsync("CLIPBOARD UNAVAILABLE", false);
            return false;
        }

        if (!VoiceInviteLogic.TryParse(
                clipboardText,
                VoiceServerInputBox.Text,
                out var invite,
                out var inviteError))
        {
            clipboardText = string.Empty;
            if (showToast) await ShowHotkeyToastAsync(inviteError, false);
            return false;
        }
        clipboardText = string.Empty;

        PrepareVoiceRoomChange("PRIVATE ROOM INVITE READY");
        _voiceServerUrl = invite.ServerUrl;
        VoiceServerInputBox.Text = invite.ServerUrl;
        _voiceRoomSecret = invite.RoomSecret;
        VoiceRoomKeyInputBox.Text = invite.RoomSecret;
        VoiceRoomInviteStatusText.Text = invite.LegacyKeyOnly
            ? "LEGACY KEY READY · USING CURRENT SERVER · PRESS CONNECT"
            : invite.LocalOnly
                ? "LOCAL INVITE READY · SAME PC ONLY · PRESS CONNECT"
                : "INVITE READY · PRESS CONNECT · MICROPHONE STILL OFF";
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
        if (showToast)
        {
            await ShowHotkeyToastAsync(
                invite.LocalOnly
                    ? "LOCAL VOICE INVITE READY · SAME PC ONLY"
                    : "ISLEY VOICE INVITE READY · PRESS CONNECT",
                !invite.LocalOnly);
        }
        return true;
    }

    private void PrepareVoiceRoomChange(string detail)
    {
        DisarmVoiceMicrophonePermission();
        if (_voiceBridgeRunning || _voiceConnecting)
        {
            PostVoiceCommand(new { type = "disconnect" });
        }
        _voicePttHeld = false;
        _voiceBridgeRunning = false;
        _voiceConnecting = false;
        _voiceDeafened = false;
        _voiceEngineState = "READY";
        _voiceEngineDetail = detail;
        _voiceNetworkState = "WAITING";
        _voiceNetworkRoute = string.Empty;
        ResetVoiceMicMeterState();
        ResetVoiceQualityState();
        _voiceParticipantCount = 0;
        _voiceParticipants.Clear();
        _voicePeerVolumeRestoreAppliedPeerIds.Clear();
        _voiceParticipantRosterSignature = string.Empty;
        ClearVoiceRouteOffer("VOICE ROOM CHANGED · ROUTE OFFERS CLEARED");
        SetVoiceInputDeviceOptions([], _voiceSelectedInputDeviceId, "CONNECT TO CHOOSE");
        SetVoiceOutputDeviceOptions(
            [],
            _voiceSelectedOutputDeviceId,
            _voiceOutputSelectionSupported,
            "CONNECT TO CHOOSE");
        UpdateVoiceParticipantRoster();
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
    }

    private void VoiceDeafenButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_voiceBridgeRunning) return;
        _voiceDeafened = !_voiceDeafened;
        PostVoiceCommand(new { type = "deafen", enabled = _voiceDeafened });
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
    }

    private void PostVoicePreferences()
    {
        PostVoiceCommand(new
        {
            type = "preferences",
            proximityEnabled = _voiceProximityEnabled,
            proximityMaxDistance = VoiceIntegrationLogic.Range(_voiceRangeIndex).MaxDistance,
            echoCancellation = _voiceEchoCancellation,
            noiseSuppression = _voiceNoiseSuppression,
            autoGainControl = _voiceAutoGainControl,
            micMeterEnabled = _voiceMicMeterEnabled,
            qualityMonitorEnabled = _voiceQualityMonitorEnabled
        });
    }

    private void ResetVoiceMicMeterState()
    {
        _voiceMicLevel = 0;
        _voiceMicClipped = false;
        _voiceMicLevelAt = default;
        _voiceMicPresentedSeverity = 0;
    }

    private void ResetVoiceQualityState()
    {
        _voiceQualityPeerCount = 0;
        _voiceQualitySampleCount = 0;
        _voiceQualityRoundTripMilliseconds = null;
        _voiceQualityJitterMilliseconds = null;
        _voiceQualityPacketLossPercent = null;
        _voiceQualityAt = default;
        _voicePeerQualities.Clear();
    }

    private async void VoiceSpatialModeButton_Click(object sender, RoutedEventArgs e)
    {
        _voiceProximityEnabled = !_voiceProximityEnabled;
        PostVoicePreferences();
        RefreshVoiceStatus();
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
        SaveSettings();
        await ShowHotkeyToastAsync(
            _voiceProximityEnabled
                ? "PROXIMITY AUDIO · PEER POSITIONS ENABLED"
                : "ROOM RADIO · NO POSITION SHARING",
            true);
    }

    private void VoiceRangeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_voiceProximityEnabled) return;
        _voiceRangeIndex = (_voiceRangeIndex + 1) % VoiceIntegrationLogic.RangeOptions.Length;
        PostVoicePreferences();
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
        SaveSettings();
    }

    private void VoiceEchoCancellationButton_Click(object sender, RoutedEventArgs e)
    {
        _voiceEchoCancellation = !_voiceEchoCancellation;
        PostVoicePreferences();
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
        SaveSettings();
    }

    private void VoiceNoiseSuppressionButton_Click(object sender, RoutedEventArgs e)
    {
        _voiceNoiseSuppression = !_voiceNoiseSuppression;
        PostVoicePreferences();
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
        SaveSettings();
    }

    private void VoiceAutoGainButton_Click(object sender, RoutedEventArgs e)
    {
        _voiceAutoGainControl = !_voiceAutoGainControl;
        PostVoicePreferences();
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
        SaveSettings();
    }

    private void VoiceMicMeterButton_Click(object sender, RoutedEventArgs e)
    {
        _voiceMicMeterEnabled = !_voiceMicMeterEnabled;
        ResetVoiceMicMeterState();
        PostVoicePreferences();
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
        SaveSettings();
    }

    private void VoiceQualityButton_Click(object sender, RoutedEventArgs e)
    {
        _voiceQualityMonitorEnabled = !_voiceQualityMonitorEnabled;
        ResetVoiceQualityState();
        PostVoicePreferences();
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
        SaveSettings();
    }

    private void VoiceInputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressVoiceInputDeviceSelection
            || !_voiceBridgeRunning
            || VoiceInputDeviceComboBox.SelectedItem is not VoiceInputDeviceInfo selected
            || string.IsNullOrEmpty(selected.Id)
            || selected.Id == _voiceSelectedInputDeviceId)
        {
            return;
        }

        _voiceSelectedInputDeviceId = selected.Id;
        _voicePttHeld = false;
        _voiceInputDeviceStatus = "SWITCHING · PTT MUTED";
        ArmVoiceMicrophonePermission();
        PostVoiceCommand(new { type = "switch-input", deviceId = selected.Id });
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
    }

    private void VoiceInputDeviceRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_voiceBridgeRunning) return;
        _voiceInputDeviceStatus = "CHECKING MICROPHONES";
        _voiceOutputDeviceStatus = "CHECKING OUTPUTS";
        PostVoiceCommand(new { type = "enumerate-devices" });
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
    }

    private void VoiceOutputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressVoiceOutputDeviceSelection
            || !_voiceBridgeRunning
            || !_voiceOutputSelectionSupported
            || VoiceOutputDeviceComboBox.SelectedItem is not VoiceOutputDeviceInfo selected
            || string.IsNullOrEmpty(selected.Id)
            || selected.Id == _voiceSelectedOutputDeviceId)
        {
            return;
        }

        _voiceSelectedOutputDeviceId = selected.Id;
        _voiceOutputDeviceStatus = "SWITCHING OUTPUT";
        PostVoiceCommand(new { type = "switch-output", deviceId = selected.Id });
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
    }

    private void VoiceOutputDeviceRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_voiceBridgeRunning) return;
        _voiceInputDeviceStatus = "CHECKING MICROPHONES";
        _voiceOutputDeviceStatus = "CHECKING OUTPUTS";
        PostVoiceCommand(new { type = "enumerate-devices" });
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
    }

    private void VoiceParticipantMuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_voiceBridgeRunning || sender is not Button { Tag: string rawId }) return;
        var peerId = VoiceIntegrationLogic.NormalizePeerId(rawId);
        var index = _voiceParticipants.FindIndex(participant => participant.Id == peerId);
        if (index < 0) return;
        var participant = _voiceParticipants[index];
        participant = participant with { Muted = !participant.Muted };
        _voiceParticipants[index] = participant;
        PostVoiceCommand(new
        {
            type = "participant-settings",
            peerId,
            muted = participant.Muted,
            volume = participant.VolumePercent / 100d
        });
        _voiceParticipantRosterSignature = string.Empty;
        UpdateVoiceParticipantRoster();
    }

    private void VoiceParticipantVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_voiceBridgeRunning || sender is not Button { Tag: string rawId }) return;
        var peerId = VoiceIntegrationLogic.NormalizePeerId(rawId);
        var index = _voiceParticipants.FindIndex(participant => participant.Id == peerId);
        if (index < 0) return;
        var participant = _voiceParticipants[index];
        participant = participant with
        {
            VolumePercent = VoiceIntegrationLogic.NextParticipantVolume(participant.VolumePercent)
        };
        _voiceParticipants[index] = participant;
        PostVoiceCommand(new
        {
            type = "participant-settings",
            peerId,
            muted = participant.Muted,
            volume = participant.VolumePercent / 100d
        });
        if (VoicePeerVolumeLogic.TryComputePeerKey(participant.Name, out var changedPeerKey))
        {
            EnsureOverlayExtrasLoaded();
            _overlayVoicePeerVolumes = VoicePeerVolumeLogic.Upsert(
                _overlayVoicePeerVolumes,
                changedPeerKey,
                participant.VolumePercent,
                DateTimeOffset.UtcNow);
            SaveOverlayExtras();
        }
        _voiceParticipantRosterSignature = string.Empty;
        UpdateVoiceParticipantRoster();
    }

    private async void VoiceShareRouteButton_Click(object sender, RoutedEventArgs e) =>
        await ShareCurrentRouteToVoiceAsync(showToast: true);

    private async Task<bool> ShareCurrentRouteToVoiceAsync(bool showToast)
    {
        if (_streamerMode)
        {
            if (showToast) await ShowHotkeyToastAsync("ROUTE SHARING HIDDEN IN STREAMER MODE", false);
            return false;
        }
        if (!LiveMapServicesActive || !_followControllerInstalled)
        {
            if (showToast) await ShowHotkeyToastAsync("LIVE MAP MODE REQUIRED", false);
            return false;
        }
        if (!_voiceBridgeRunning || _voiceParticipants.Count == 0)
        {
            if (showToast) await ShowHotkeyToastAsync("CONNECT AT LEAST ONE ISLEY VOICE PEER", false);
            OpenToolsWorkspace("voice");
            return false;
        }
        if (!TryBuildCurrentSharedRoute(out var sharedRoute))
        {
            if (showToast) await ShowHotkeyToastAsync("START A 2–12 STOP ROUTE FIRST", false);
            return false;
        }

        _voiceRouteSendOfferId = NewVoiceSecret(12);
        _voiceRouteShareStatus =
            $"SENDING {sharedRoute.Kind} · {sharedRoute.StopCount} STOPS · PEER-TO-PEER";
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
        PostVoiceCommand(new
        {
            type = "send-route-offer",
            offerId = _voiceRouteSendOfferId,
            routeText = sharedRoute.Text
        });
        return true;
    }

    private async void VoiceRouteOfferAcceptButton_Click(object sender, RoutedEventArgs e)
    {
        var pendingOffer = CurrentVoiceRouteOffer();
        if (pendingOffer is not { } offer)
        {
            ClearVoiceRouteOffer("NO ACTIVE ROUTE OFFER");
            UpdateVoicePresentation();
            return;
        }
        if (_streamerMode || !LiveMapServicesActive || !_followControllerInstalled)
        {
            await ShowHotkeyToastAsync("LIVE MAP MODE REQUIRED TO ACCEPT", false);
            return;
        }

        var routed = await ExecuteMapperCommandAsync(
            $"window.__isley?.startSharedRouteText({JsonSerializer.Serialize(offer.Route.Text)}) ?? false");
        if (!routed)
        {
            _voiceRouteShareStatus = "ROUTE OFFER FAILED VALIDATION · NOTHING CHANGED";
            _voiceUiSignature = string.Empty;
            UpdateVoicePresentation();
            await ShowHotkeyToastAsync("ROUTE OFFER COULD NOT BE ACTIVATED", false);
            return;
        }

        ClearVoiceRouteOffer(
            $"VOICE ROUTE ACCEPTED · {offer.Route.Kind} · {offer.Route.StopCount} STOPS");
        AddTacticalEvent(
            "ROUTE",
            "Voice route accepted",
            $"{offer.Route.Kind} · {offer.Route.StopCount} stops · explicit peer handoff");
        var rallyDropped = await ExecuteMapperCommandAsync(
            "window.__isley?.dropPinAtSelf('rally') ?? false");
        if (rallyDropped)
        {
            AddTacticalEvent(
                "PACK",
                "Rally pin placed",
                "Pack rally marker dropped at your position after route accept");
        }
        UpdateVoicePresentation();
        await ShowHotkeyToastAsync(
            rallyDropped ? "VOICE ROUTE ACTIVE · RALLY PIN SET" : "VOICE ROUTE ACTIVE",
            true);
    }

    private async void VoiceRouteOfferDeclineButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentVoiceRouteOffer() is null)
        {
            return;
        }
        ClearVoiceRouteOffer("ROUTE OFFER DECLINED · NOTHING CHANGED");
        AddTacticalEvent(
            "VOICE",
            "Route offer declined",
            "Session-only offer discarded · navigation unchanged");
        UpdateVoicePresentation();
        await ShowHotkeyToastAsync("ROUTE OFFER DECLINED", true);
    }

    private async void VoiceNatAssistButton_Click(object sender, RoutedEventArgs e)
    {
        _voiceNatAssist = !_voiceNatAssist;
        if (_voiceBridgeRunning || _voiceConnecting)
        {
            PostVoiceCommand(new { type = "disconnect" });
            _voiceBridgeRunning = false;
            _voiceConnecting = false;
            _voiceEngineState = "READY";
            _voiceEngineDetail = "NAT SETTING CHANGED · RECONNECT";
            await ShowHotkeyToastAsync("NAT SETTING CHANGED · RECONNECT VOICE", true);
        }
        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
        SaveSettings();
    }

    private async void VoiceTurnRelayButton_Click(object sender, RoutedEventArgs e)
    {
        _voiceTurnRelayEnabled = !_voiceTurnRelayEnabled;
        if (_voiceBridgeRunning || _voiceConnecting)
        {
            PostVoiceCommand(new { type = "disconnect" });
            _voiceBridgeRunning = false;
            _voiceConnecting = false;
            _voiceEngineState = "READY";
            _voiceEngineDetail = "RELAY SETTING CHANGED · RECONNECT";
            await ShowHotkeyToastAsync("RELAY SETTING CHANGED · RECONNECT VOICE", true);
        }

        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
    }
}
