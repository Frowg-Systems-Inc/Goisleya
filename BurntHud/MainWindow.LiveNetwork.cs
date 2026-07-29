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
    private void EnsureCommunityServerProfiles()
    {
        if (_communityServerProfiles.Count > 0)
        {
            return;
        }

        RestoreCommunityServerProfiles(null, null);
    }

    private void RestoreCommunityServerProfiles(
        IEnumerable<CommunityServerProfileSettings>? savedProfiles,
        string? selectedProfileId)
    {
        var legacyGrowthIndex = CommunitySessionActive
            ? _growthServerMultiplierIndex
            : -1;
        var normalizedProfiles = CommunityServerWatchLogic.NormalizeProfiles(
            savedProfiles,
            _serverSessionName,
            _communityServerAddress,
            _communityServerWatchEnabled,
            _communityServerSlotAlertEnabled,
            legacyGrowthIndex);
        _communityServerProfiles.Clear();
        _communityServerProfiles.AddRange(normalizedProfiles);
        var selectedIndex = CommunityServerWatchLogic.FindProfileIndex(
            _communityServerProfiles,
            selectedProfileId);
        LoadCommunityServerProfile(selectedIndex, applyGrowthRate: CommunitySessionActive);
    }

    private int CurrentCommunityServerProfileIndex()
    {
        EnsureCommunityServerProfiles();
        return CommunityServerWatchLogic.FindProfileIndex(
            _communityServerProfiles,
            _selectedCommunityServerProfileId);
    }

    private CommunityServerProfileSettings CurrentCommunityServerProfile() =>
        _communityServerProfiles[CurrentCommunityServerProfileIndex()];

    private void SyncCurrentCommunityServerProfile(bool includeGrowthRate = false)
    {
        EnsureCommunityServerProfiles();
        var profile = CurrentCommunityServerProfile();
        profile.Name = _serverSessionName;
        profile.Address = CommunityServerWatchLogic.SanitizeAddressInput(_communityServerAddress);
        profile.WatchEnabled = _communityServerWatchEnabled
                               && CommunityServerWatchLogic.TryNormalizeAddress(profile.Address, out _);
        profile.SlotAlertEnabled = _communityServerSlotAlertEnabled;
        profile.IsleyJoinLink = SanitizeCommunityIsleyJoinLink(_isleyRelayJoinLink);
        if (includeGrowthRate)
        {
            profile.GrowthMultiplierIndex = Math.Clamp(
                _growthServerMultiplierIndex,
                0,
                GrowthPlannerLogic.ServerMultipliers.Length - 1);
        }
    }

    private static string SanitizeCommunityIsleyJoinLink(string? value)
    {
        var sanitized = new string((value ?? string.Empty)
            .Where(character => !char.IsControl(character))
            .ToArray())
            .Trim();
        if (sanitized.Length > 1024)
        {
            return string.Empty;
        }
        return sanitized;
    }

    private void LoadCommunityServerProfile(int index, bool applyGrowthRate)
    {
        EnsureCommunityServerProfiles();
        var normalizedIndex = Math.Clamp(index, 0, _communityServerProfiles.Count - 1);
        var profile = _communityServerProfiles[normalizedIndex];
        _selectedCommunityServerProfileId = profile.Id;
        _serverSessionName = profile.Name;
        _communityServerAddress = profile.Address;
        _communityServerWatchEnabled = profile.WatchEnabled
                                       && CommunityServerWatchLogic.TryNormalizeAddress(
                                           profile.Address,
                                           out _);
        _communityServerSlotAlertEnabled = profile.SlotAlertEnabled;
        // Always bind the profile's join link, including empty, so switching
        // away from a linked profile cannot contaminate an unbound slot.
        _isleyRelayJoinLink = SanitizeCommunityIsleyJoinLink(profile.IsleyJoinLink);
        _communityServerWasFull = null;
        _lastCommunityServerStatus = null;
        _communityServerStatusError = string.Empty;
        ClearUniversalCoordinateSession(updateUi: false);
        ResetUniversalCoordinateClipboardBaseline();
        if (applyGrowthRate && profile.GrowthMultiplierIndex >= 0)
        {
            _growthServerMultiplierIndex = Math.Clamp(
                profile.GrowthMultiplierIndex,
                0,
                GrowthPlannerLogic.ServerMultipliers.Length - 1);
            _growthPlannerUiSignature = string.Empty;
            _lifeRunUiSignature = string.Empty;
        }
    }

    private void SetServerSessionNameInput(string value)
    {
        if (ServerSessionNameInputBox is null)
        {
            return;
        }

        _suppressServerSessionNameChanges = true;
        try
        {
            ServerSessionNameInputBox.Text = value;
            ServerSessionNameInputBox.CaretIndex = ServerSessionNameInputBox.Text.Length;
        }
        finally
        {
            _suppressServerSessionNameChanges = false;
        }
    }

    private void SetCommunityServerAddressInput(string value)
    {
        if (CommunityServerAddressInputBox is null)
        {
            return;
        }

        _suppressCommunityServerAddressChanges = true;
        try
        {
            CommunityServerAddressInputBox.Text = value;
            CommunityServerAddressInputBox.CaretIndex = CommunityServerAddressInputBox.Text.Length;
        }
        finally
        {
            _suppressCommunityServerAddressChanges = false;
        }
    }

    private void UpdateCommunityServerDeckPresentation()
    {
        if (CommunityServerDeckPanel is null || CommunityServerDeckStatusText is null)
        {
            return;
        }

        EnsureCommunityServerProfiles();
        var selectedIndex = CurrentCommunityServerProfileIndex();
        var count = _communityServerProfiles.Count;
        CommunityServerDeckStatusText.Text = $"SAVED SERVER · {selectedIndex + 1} OF {count}";
        CommunityServerPreviousButton.IsEnabled = count > 1;
        CommunityServerNextButton.IsEnabled = count > 1;
        CommunityServerNewButton.IsEnabled = count < CommunityServerWatchLogic.MaximumProfiles;
        CommunityServerNewButton.Content = count < CommunityServerWatchLogic.MaximumProfiles
            ? "NEW"
            : "FULL";
        CommunityServerRemoveButton.IsEnabled = count > 1;
        CommunityServerRemoveButton.Content = _communityServerRemoveConfirmationPending
            ? "SURE"
            : "DEL";
        CommunityServerRemoveButton.ToolTip = count <= 1
            ? "Keep at least one saved Any Server profile"
            : _communityServerRemoveConfirmationPending
                ? "Press again within three seconds to remove this saved server"
                : "Remove this saved server after confirmation";
        SetToggleButtonState(
            CommunityServerRemoveButton,
            _communityServerRemoveConfirmationPending);
    }

    private void AnimateCommunityServerProfileSwitch()
    {
        var animation = new System.Windows.Media.Animation.DoubleAnimation(
            0.35,
            1,
            TimeSpan.FromMilliseconds(140));
        ServerSessionNamePanel.BeginAnimation(OpacityProperty, animation);
        CommunityServerWatchPanel.BeginAnimation(
            OpacityProperty,
            animation.Clone());
    }

    private async Task CancelServerStatusRefreshAsync()
    {
        _serverStatusCancellation?.Cancel();
        for (var attempt = 0; attempt < 20 && _serverStatusRefreshInFlight; attempt++)
        {
            await Task.Delay(25);
        }
    }

    private async Task SelectCommunityServerProfileAsync(
        int targetIndex,
        bool announce,
        bool saveCurrentProfile = true)
    {
        EnsureCommunityServerProfiles();
        if (saveCurrentProfile)
        {
            SyncCurrentCommunityServerProfile();
        }

        _serverStatusTimer.Stop();
        await CancelServerStatusRefreshAsync();
        var previousProfileId = _selectedCommunityServerProfileId;
        LoadCommunityServerProfile(targetIndex, applyGrowthRate: true);
        if (!string.Equals(previousProfileId, _selectedCommunityServerProfileId, StringComparison.Ordinal))
        {
            ClearCoreVitals(logEvent: false, updateUi: true);
            ClearFieldConditions(logEvent: false, updateUi: true);
        }
        _communityServerRemoveConfirmationPending = false;
        _communityServerRemoveConfirmationRevision++;
        SetServerSessionNameInput(_serverSessionName);
        SetCommunityServerAddressInput(_communityServerAddress);
        _growthPlannerUiSignature = string.Empty;
        _lifeRunUiSignature = string.Empty;
        UpdateServerSessionPresentation();
        UpdateIsleyRelayPresentation();
        AnimateCommunityServerProfileSwitch();
        SaveSettings();

        if (ShouldPollServerStatus)
        {
            _serverStatusTimer.Start();
            await RefreshServerStatusAsync();
        }

        if (!announce)
        {
            return;
        }

        var selectedIndex = CurrentCommunityServerProfileIndex();
        AddTacticalEvent(
            "SESSION",
            $"Saved server {selectedIndex + 1} selected",
            ServerSessionLogic.DisplayName(_serverSessionProfileId, _serverSessionName));
        await ShowHotkeyToastAsync(
            $"SERVER {selectedIndex + 1}/{_communityServerProfiles.Count} · " +
            ServerSessionLogic.DisplayName(_serverSessionProfileId, _serverSessionName).ToUpperInvariant(),
            true);
    }

    private async void CommunityServerPreviousButton_Click(object sender, RoutedEventArgs e)
    {
        var targetIndex = CommunityServerWatchLogic.MoveProfileIndex(
            _communityServerProfiles.Count,
            CurrentCommunityServerProfileIndex(),
            -1);
        await SelectCommunityServerProfileAsync(targetIndex, announce: true);
    }

    private async void CommunityServerNextButton_Click(object sender, RoutedEventArgs e)
    {
        var targetIndex = CommunityServerWatchLogic.MoveProfileIndex(
            _communityServerProfiles.Count,
            CurrentCommunityServerProfileIndex(),
            1);
        await SelectCommunityServerProfileAsync(targetIndex, announce: true);
    }

    private async void CommunityServerNewButton_Click(object sender, RoutedEventArgs e)
    {
        EnsureCommunityServerProfiles();
        if (_communityServerProfiles.Count >= CommunityServerWatchLogic.MaximumProfiles)
        {
            await ShowHotkeyToastAsync("SAVED SERVER LIMIT · 6", false);
            return;
        }

        SyncCurrentCommunityServerProfile();
        _communityServerProfiles.Add(
            CommunityServerWatchLogic.CreateProfile(_communityServerProfiles));
        await SelectCommunityServerProfileAsync(
            _communityServerProfiles.Count - 1,
            announce: false,
            saveCurrentProfile: false);
        ServerSessionNameInputBox.Focus();
        ServerSessionNameInputBox.SelectAll();
        await ShowHotkeyToastAsync("NEW SERVER SLOT READY", true);
    }

    private async void CommunityServerRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        EnsureCommunityServerProfiles();
        if (_communityServerProfiles.Count <= 1)
        {
            await ShowHotkeyToastAsync("KEEP ONE SAVED SERVER", false);
            return;
        }

        if (!_communityServerRemoveConfirmationPending)
        {
            _communityServerRemoveConfirmationPending = true;
            var revision = ++_communityServerRemoveConfirmationRevision;
            UpdateCommunityServerDeckPresentation();
            await ShowHotkeyToastAsync("PRESS REMOVE AGAIN", false);
            await Task.Delay(3000);
            if (!IsLoaded
                || !_communityServerRemoveConfirmationPending
                || revision != _communityServerRemoveConfirmationRevision)
            {
                return;
            }
            _communityServerRemoveConfirmationPending = false;
            UpdateCommunityServerDeckPresentation();
            return;
        }

        var removedName = ServerSessionLogic.DisplayName(
            _serverSessionProfileId,
            _serverSessionName);
        _communityServerRemoveConfirmationPending = false;
        _communityServerRemoveConfirmationRevision++;
        var removal = CommunityServerWatchLogic.RemoveProfileAt(
            _communityServerProfiles,
            CurrentCommunityServerProfileIndex());
        _communityServerProfiles.Clear();
        _communityServerProfiles.AddRange(removal.Profiles);
        await SelectCommunityServerProfileAsync(
            removal.SelectedIndex,
            announce: false,
            saveCurrentProfile: false);
        AddTacticalEvent("SESSION", "Saved server removed", removedName);
        await ShowHotkeyToastAsync("SAVED SERVER REMOVED", true);
    }

    private void UpdateServerSessionPresentation(bool animate = false)
    {
        if (UniversalSessionSurface is null || ServerSessionModeText is null)
        {
            return;
        }

        var profile = ServerSessionLogic.Find(_serverSessionProfileId);
        var displayName = ServerSessionLogic.DisplayName(profile.Id, _serverSessionName);
        var live = profile.LiveMapServicesAvailable;

        ServerSessionModeText.Text = displayName.ToUpperInvariant();
        ServerSessionCapabilityText.Text = profile.ModeLabel;
        ServerSessionDescriptionText.Text = profile.Description;
        ServerCompatibilityStatusText.Text = profile.CompatibilityStatus;
        ServerCapabilityDetailText.Text = profile.CapabilitySummary;
        ServerSessionModeButton.Content = "NEXT";
        ServerSessionModeButton.ToolTip = "Cycle Live Map, Official, and Any Server modes";
        SetToggleButtonState(
            ServerModeLiveMapButton,
            string.Equals(profile.Id, ServerSessionLogic.LiveMapId, StringComparison.Ordinal));
        SetToggleButtonState(
            ServerModeOfficialButton,
            string.Equals(profile.Id, ServerSessionLogic.OfficialId, StringComparison.Ordinal));
        SetToggleButtonState(
            ServerModeAnyServerButton,
            string.Equals(profile.Id, ServerSessionLogic.CommunityId, StringComparison.Ordinal));
        SetToggleButtonState(
            OnboardingServerLiveMapButton,
            string.Equals(profile.Id, ServerSessionLogic.LiveMapId, StringComparison.Ordinal));
        SetToggleButtonState(
            OnboardingServerOfficialButton,
            string.Equals(profile.Id, ServerSessionLogic.OfficialId, StringComparison.Ordinal));
        SetToggleButtonState(
            OnboardingServerAnyButton,
            string.Equals(profile.Id, ServerSessionLogic.CommunityId, StringComparison.Ordinal));
        OnboardingServerModeStatusText.Text =
            $"{profile.SelectorLabel} · {profile.CompatibilityStatus}";
        CommunityServerDeckPanel.Visibility = profile.Id == ServerSessionLogic.CommunityId
            ? Visibility.Visible
            : Visibility.Collapsed;
        ServerSessionNamePanel.Visibility = profile.Id == ServerSessionLogic.CommunityId
            ? Visibility.Visible
            : Visibility.Collapsed;
        CommunityServerWatchPanel.Visibility = profile.Id == ServerSessionLogic.CommunityId
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!ServerSessionNameInputBox.IsKeyboardFocusWithin)
        {
            SetServerSessionNameInput(_serverSessionName);
        }
        if (!CommunityServerAddressInputBox.IsKeyboardFocusWithin)
        {
            SetCommunityServerAddressInput(_communityServerAddress);
        }

        var growthMultiplierIndex = CurrentServerGrowthMultiplierIndex();
        var ratePresetSuffix = CurrentRatePreset() is { } sessionRatePreset
            ? $" · preset {sessionRatePreset.Label}"
            : string.Empty;
        ServerSessionGrowthButton.Content = growthMultiplierIndex >= 0
            ? CommunitySessionActive
                ? $"SERVER RATE {GrowthPlannerLogic.ServerMultipliers[growthMultiplierIndex]:0.#}X"
                : $"USE {GrowthPlannerLogic.ServerMultipliers[growthMultiplierIndex]:0.#}X GROWTH"
            : "SET GROWTH RATE";
        ServerSessionGrowthButton.ToolTip = (growthMultiplierIndex >= 0
            ? CommunitySessionActive
                ? "Apply this saved server's growth multiplier to the current Growth Clock"
                : "Apply this profile's suggested multiplier to the current Growth Clock"
            : "Open Growth Clock and choose the multiplier advertised by this server") + ratePresetSuffix;
        ServerSessionLiveMapButton.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        LiveMapServerSectionLabel.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        ServerStatusCard.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        PinsToolsTabButton.IsEnabled = live;
        LayerToolsTabButton.IsEnabled = live;
        HubToolsTabButton.IsEnabled = true;
        PinsToolsTabButton.ToolTip = live
            ? "Persistent local map markers and routes"
                : "Saved destinations require Live Map mode";
        LayerToolsTabButton.ToolTip = live
            ? "Bundled Isley map layers"
                : "Live map layers require Live Map mode";
        HubToolsTabButton.ToolTip = "Open Isley's local companion tools";

        if (!live && _toolsSection is "pins" or "layers")
        {
            _toolsSection = "overlay";
            UpdateToolsSection();
        }

        UniversalSessionTitleText.Text = displayName.ToUpperInvariant();
        UniversalSessionDetailText.Text =
            "Voice, Core Vitals, Field Conditions, Guide, Life Run, combat, growth, nesting, mutations, survival triage, safe logout, timers, and Terrain Probe remain ready. " +
            "Server-fed positions and map layers are unavailable in universal mode; optional public status never includes player positions.";
        if (live || _streamerMode)
        {
            UniversalSessionSurface.BeginAnimation(OpacityProperty, null);
            UniversalSessionSurface.Opacity = 1;
            UniversalSessionSurface.Visibility = Visibility.Collapsed;
        }
        else
        {
            SetConnectionStatus("UNIVERSAL SESSION", Color.FromRgb(56, 189, 248));
            var wasHidden = UniversalSessionSurface.Visibility != Visibility.Visible;
            UniversalSessionSurface.Visibility = Visibility.Visible;
            if (animate && wasHidden)
            {
                UniversalSessionSurface.Opacity = 0;
                UniversalSessionSurface.BeginAnimation(
                    OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(
                        0,
                        1,
                        TimeSpan.FromMilliseconds(180)));
            }
            else
            {
                UniversalSessionSurface.BeginAnimation(OpacityProperty, null);
                UniversalSessionSurface.Opacity = 1;
            }
        }

        _voiceUiSignature = string.Empty;
        UpdateVoicePresentation();
        UpdateCoreVitals(force: true);
        UpdateFieldConditions(force: true);
        UpdateLifeRun(force: true);
        UpdateTacticalBrief();
        UpdateCommunityServerDeckPresentation();
        UpdateCommunityServerWatchPresentation();
        UpdateServerStatusPresentation();
        UpdateUniversalCoordinatePresentation(force: true);
    }

    private void ResetUniversalCoordinateClipboardBaseline() =>
        _universalCoordinateClipboardSequence = NativeMethods.GetClipboardSequenceNumber();

    private void RefreshUniversalCoordinateCapture()
    {
        UpdateUniversalCoordinatePresentation();
        if (!_universalCoordinateCaptureEnabled || _streamerMode)
        {
            return;
        }

        var sequence = NativeMethods.GetClipboardSequenceNumber();
        if (sequence == 0 || sequence == _universalCoordinateClipboardSequence)
        {
            return;
        }

        // Advance the baseline before reading. Clipboard changes made while an unrelated
        // application is active are deliberately discarded and never revisited later.
        _universalCoordinateClipboardSequence = sequence;
        var foreground = GetPlayFocusForeground();
        if (foreground is not (PlayFocusForeground.Game or PlayFocusForeground.Mapper))
        {
            return;
        }

        try
        {
            if (!Clipboard.ContainsText(TextDataFormat.Text))
            {
                return;
            }

            var clipboardText = Clipboard.GetText(TextDataFormat.Text);
            if (!UniversalCoordinateLogic.TryParseClipboard(clipboardText, out var point))
            {
                return;
            }

            AcceptUniversalCoordinateCapture(point);
        }
        catch
        {
            // Clipboard ownership can change between the sequence check and the read.
            // The next game-originated copy will retry without surfacing unrelated data.
        }
    }

    private async void AutoLocateOnGameStartButton_Click(object sender, RoutedEventArgs e)
    {
        _autoLocateOnGameStart = !_autoLocateOnGameStart;
        UpdateAutoLocateOnGameStartPresentation();
        SaveSettings();
        await ShowHotkeyToastAsync(
            _autoLocateOnGameStart
                ? "AUTO LOCATION · ON"
                : "AUTO LOCATION · OFF",
            true);
    }

    private void UpdateAutoLocateOnGameStartPresentation()
    {
        if (AutoLocateOnGameStartButton is null)
        {
            return;
        }

        AutoLocateOnGameStartButton.Content = _autoLocateOnGameStart
            ? "Auto location · On"
            : "Auto location · Off";
        AutoLocateOnGameStartButton.ToolTip = _autoLocateOnGameStart
            ? "When The Isle starts, resume a saved Live Network session or coach Tab → Asset Location"
            : "Location resume on game start is off";
        SetToggleButtonState(AutoLocateOnGameStartButton, _autoLocateOnGameStart);
    }

    private bool HasFreshAuthorizedSelfLocation()
    {
        if (_lastAuthorizedSelfAppliedAt is { } appliedAt
            && DateTimeOffset.UtcNow - appliedAt <= IsleyLiveDataProvider.FreshnessLimit)
        {
            return true;
        }

        return _markerAvailable
               && _currentSelfX is not null
               && _currentSelfY is not null;
    }

    private void MarkAuthorizedSelfApplied(DateTimeOffset appliedAt) =>
        _lastAuthorizedSelfAppliedAt = appliedAt;

    private async Task HandleGameStartedLocationResumeAsync()
    {
        if (!_autoLocateOnGameStart
            || _streamerMode
            || _locationResumeInFlight
            || !LiveMapServicesActive)
        {
            return;
        }

        _locationResumeInFlight = true;
        _locationResumePendingToast = true;
        try
        {
            await RefreshIndependentLiveDataAsync(force: true);
            if (HasFreshAuthorizedSelfLocation())
            {
                _locationResumePendingToast = false;
                await ShowHotkeyToastAsync("LOCATION LIVE", true);
                return;
            }

            var canResumeRelay = IsleyRelayJoinLogic.TryParse(_isleyRelayJoinLink, out var join)
                && _isleyRelayClient.TryReadCredential(join, out _)
                && !string.Equals(_isleyRelayState, "live", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(_isleyRelayState, "connecting", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(_isleyRelayState, "signing-in", StringComparison.OrdinalIgnoreCase);
            if (canResumeRelay)
            {
                await ShowHotkeyToastAsync("RESUMING LIVE LOCATION…", true);
                await InitializeIsleyRelayAsync();
                for (var attempt = 0; attempt < 12 && !HasFreshAuthorizedSelfLocation(); attempt++)
                {
                    await Task.Delay(250);
                }

                if (HasFreshAuthorizedSelfLocation())
                {
                    _locationResumePendingToast = false;
                    await ShowHotkeyToastAsync("LOCATION LIVE FROM SERVER", true);
                    return;
                }
            }

            if (!_universalCoordinateCaptureEnabled)
            {
                _universalCoordinateCaptureEnabled = true;
                UpdateUniversalCoordinatePresentation(force: true);
                SaveSettings();
            }

            _locationResumePendingToast = false;
            await ShowHotkeyToastAsync("IN GAME: TAB → ASSET LOCATION", true);
        }
        finally
        {
            _locationResumeInFlight = false;
        }
    }

    private async Task InitializeIsleyRelayAsync()
    {
        if (IsleyRelayJoinLinkInputBox is not null
            && !IsleyRelayJoinLinkInputBox.IsKeyboardFocusWithin)
        {
            IsleyRelayJoinLinkInputBox.Text = _isleyRelayJoinLink;
        }
        if (!IsleyRelayJoinLogic.TryParse(_isleyRelayJoinLink, out var join))
        {
            _isleyRelayJoin = null;
            _isleyRelayState = "disconnected";
            _isleyRelayDetail = string.IsNullOrWhiteSpace(_isleyRelayJoinLink)
                ? "Optional · paste a participating server link"
                : "That participating server link is not valid";
            UpdateIsleyRelayPresentation();
            return;
        }

        _isleyRelayJoin = join;
        if (!_isleyRelayClient.TryReadCredential(join, out var accessToken))
        {
            _isleyRelayState = "ready";
            _isleyRelayDetail = $"Ready for Steam sign-in · {join.DisplayText}";
            UpdateIsleyRelayPresentation();
            return;
        }

        await _isleyRelayClient.ConnectAsync(join, accessToken);
        await RefreshIsleyRelayPrivacyAsync(join, accessToken);
        UpdateIsleyRelayPresentation();
    }

    private async void IsleyRelayConnectButton_Click(object sender, RoutedEventArgs e)
    {
        await ConnectIsleyRelayAsync(IsleyRelayJoinLinkInputBox.Text.Trim());
    }

    // One-step private server connect: validates the clipboard join link,
    // fills the input, and runs the same connect + Steam sign-in flow.
    private async Task ConnectPrivateServerFromClipboardAsync()
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

        if (!IsleyRelayJoinLogic.TryParse(clipboard, out var join))
        {
            OpenToolsWorkspace("overlay");
            await ShowHotkeyToastAsync("COPY YOUR SERVER'S ISLEY LINK, THEN RUN THIS AGAIN", false);
            return;
        }

        if (IsleyRelayJoinLinkInputBox is not null)
        {
            IsleyRelayJoinLinkInputBox.Text = clipboard;
        }

        await ShowHotkeyToastAsync($"CONNECTING · {join.DisplayText}", true);
        await ConnectIsleyRelayAsync(clipboard);
        var linking = _isleyRelayState is "live" or "connecting" or "signing-in";
        await ShowHotkeyToastAsync(
            linking
                ? "PRIVATE SERVER LINKED · FINISH STEAM SIGN-IN IF PROMPTED"
                : "COULD NOT CONNECT · CHECK THE LINK OR SERVER",
            linking);
    }

    private async Task ConnectIsleyRelayAsync(string input)
    {
        if (!IsleyRelayJoinLogic.TryParse(input, out var join))
        {
            _isleyRelayState = "error";
            _isleyRelayDetail =
                "Paste the participating server's Isley link, then connect again.";
            UpdateIsleyRelayPresentation();
            return;
        }

        _isleyRelayJoinLink = input;
        _isleyRelayJoin = join;
        SyncCurrentCommunityServerProfile();
        SaveSettings();
        _isleyRelaySignInCancellation?.Cancel();
        _isleyRelaySignInCancellation?.Dispose();
        _isleyRelaySignInCancellation = new CancellationTokenSource();
        var cancellationToken = _isleyRelaySignInCancellation.Token;
        try
        {
            if (!_isleyRelayClient.TryReadCredential(join, out var accessToken))
            {
                _isleyRelayState = "signing-in";
                _isleyRelayDetail = "Opening Steam · Isley will finish automatically";
                UpdateIsleyRelayPresentation();
                var authorization = await _isleyRelayClient.StartDeviceAuthorizationAsync(
                    join,
                    cancellationToken);
                OpenExternalUri(authorization.VerificationUri.AbsoluteUri);
                var credential = await _isleyRelayClient.CompleteDeviceAuthorizationAsync(
                    join,
                    authorization,
                    cancellationToken);
                accessToken = credential.AccessToken;
            }
            await _isleyRelayClient.ConnectAsync(join, accessToken, cancellationToken);
            await RefreshIsleyRelayPrivacyAsync(join, accessToken, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _isleyRelayState = "disconnected";
            _isleyRelayDetail = "Connection cancelled";
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or WebSocketException
            or InvalidDataException
            or TimeoutException
            or InvalidOperationException)
        {
            _isleyRelayState = "error";
            _isleyRelayDetail = $"Could not connect: {ex.Message}";
        }
        UpdateIsleyRelayPresentation();
    }

    private async void IsleyRelayDisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        _isleyRelaySignInCancellation?.Cancel();
        await _isleyRelayClient.StopAsync();
        _isleyRelayState = "disconnected";
        _isleyRelayDetail = "Disconnected · protected Steam session kept";
        UpdateIsleyRelayPresentation();
    }

    private async void IsleyRelayForgetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isleyRelayJoin is not { } join
            && !IsleyRelayJoinLogic.TryParse(
                IsleyRelayJoinLinkInputBox.Text,
                out join))
        {
            return;
        }
        _isleyRelaySignInCancellation?.Cancel();
        await _isleyRelayClient.StopAsync();
        _isleyRelayClient.ForgetCredential(join);
        _isleyRelayState = "ready";
        _isleyRelayDetail = "Steam session removed from Windows · connect to sign in again";
        _isleyRelayShareWithSteamFriends = false;
        _isleyRelayExplicitGrantCount = 0;
        _isleyRelayPrivacyDetail = "Sign in to control who can see your player node.";
        UpdateIsleyRelayPresentation();
    }

    private async Task RefreshIsleyRelayPrivacyAsync(
        IsleyRelayJoin join,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (_isleyRelayPrivacyRequestInFlight)
        {
            return;
        }
        _isleyRelayPrivacyRequestInFlight = true;
        try
        {
            var privacy = await _isleyRelayClient.GetPrivacyAsync(
                join,
                accessToken,
                cancellationToken);
            ApplyIsleyRelayPrivacy(privacy);
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or InvalidDataException
            or JsonException
            or OperationCanceledException)
        {
            _isleyRelayPrivacyDetail = cancellationToken.IsCancellationRequested
                ? "Friend visibility update cancelled."
                : "Friend visibility controls are temporarily unavailable.";
        }
        finally
        {
            _isleyRelayPrivacyRequestInFlight = false;
            UpdateIsleyRelayPresentation();
        }
    }

    private async void IsleyRelayFriendSharingButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!TryGetIsleyRelayCredential(out var join, out var accessToken))
        {
            _isleyRelayPrivacyDetail = "Connect with Steam before changing visibility.";
            UpdateIsleyRelayPresentation();
            return;
        }
        await UpdateIsleyRelayPrivacyAsync(async cancellationToken =>
            await _isleyRelayClient.UpdatePrivacyAsync(
                join,
                accessToken,
                !_isleyRelayShareWithSteamFriends,
                cancellationToken));
    }

    private async void IsleyRelayGrantViewerButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await SetIsleyRelayViewerGrantAsync(allowed: true);

    private async void IsleyRelayRevokeViewerButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await SetIsleyRelayViewerGrantAsync(allowed: false);

    private async Task SetIsleyRelayViewerGrantAsync(bool allowed)
    {
        var viewerSteamId = IsleyRelayViewerSteamIdInputBox.Text.Trim();
        if (!TelemetryValidation.IsSteamId(viewerSteamId))
        {
            _isleyRelayPrivacyDetail = "Enter the trusted player's 17-digit SteamID64.";
            UpdateIsleyRelayPresentation();
            return;
        }
        if (!TryGetIsleyRelayCredential(out var join, out var accessToken))
        {
            _isleyRelayPrivacyDetail = "Connect with Steam before changing visibility.";
            UpdateIsleyRelayPresentation();
            return;
        }
        await UpdateIsleyRelayPrivacyAsync(async cancellationToken =>
            await _isleyRelayClient.SetViewerGrantAsync(
                join,
                accessToken,
                viewerSteamId,
                allowed,
                cancellationToken));
        if (!_isleyRelayPrivacyRequestInFlight)
        {
            IsleyRelayViewerSteamIdInputBox.Clear();
        }
    }

    private async Task UpdateIsleyRelayPrivacyAsync(
        Func<CancellationToken, Task<IsleyRelayPrivacy>> update)
    {
        if (_isleyRelayPrivacyRequestInFlight)
        {
            return;
        }
        _isleyRelayPrivacyRequestInFlight = true;
        _isleyRelayPrivacyDetail = "Saving visibility choice…";
        UpdateIsleyRelayPresentation();
        try
        {
            var privacy = await update(CancellationToken.None);
            ApplyIsleyRelayPrivacy(privacy);
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or InvalidDataException
            or JsonException
            or ArgumentException)
        {
            _isleyRelayPrivacyDetail = $"Visibility was not changed: {ex.Message}";
        }
        finally
        {
            _isleyRelayPrivacyRequestInFlight = false;
            UpdateIsleyRelayPresentation();
        }
    }

    private void ApplyIsleyRelayPrivacy(IsleyRelayPrivacy privacy)
    {
        _isleyRelayShareWithSteamFriends = privacy.ShareWithSteamFriends;
        _isleyRelayExplicitGrantCount = privacy.ExplicitViewerSteamIds.Count;
        _isleyRelayPrivacyDetail = privacy.ShareWithSteamFriends
            ? $"Verified Steam friends may see you · {_isleyRelayExplicitGrantCount} explicit allow"
            : $"Steam friend sharing is off · {_isleyRelayExplicitGrantCount} explicit allow";
    }

    private bool TryGetIsleyRelayCredential(
        out IsleyRelayJoin join,
        out string accessToken)
    {
        join = _isleyRelayJoin!;
        accessToken = string.Empty;
        return join is not null
               && _isleyRelayClient.TryReadCredential(join, out accessToken);
    }

    private void IsleyRelayClient_StateChanged(
        object? sender,
        IsleyRelayConnectionState state)
    {
        _ = Dispatcher.BeginInvoke(async () =>
        {
            _isleyRelayState = state.State;
            _isleyRelayDetail = state.Detail;
            UpdateIsleyRelayPresentation();
            if (string.Equals(state.State, "live", StringComparison.OrdinalIgnoreCase))
            {
                await SyncProximityVoiceLobbyAsync(reconnectIfNeeded: true);
            }
        });
    }

    private void IsleyRelayClient_SnapshotReceived(
        object? sender,
        ViewerTelemetrySnapshot snapshot)
    {
        var scheduleDrain = false;
        lock (_isleyRelaySnapshotGate)
        {
            _isleyRelayPendingSnapshot = snapshot;
            if (!_isleyRelaySnapshotDrainScheduled)
            {
                _isleyRelaySnapshotDrainScheduled = true;
                scheduleDrain = true;
            }
        }
        if (scheduleDrain)
        {
            ScheduleIsleyRelaySnapshotDrain();
        }
    }

    private void ScheduleIsleyRelaySnapshotDrain() =>
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() => _ = DrainIsleyRelaySnapshotsAsync()));

    private async Task DrainIsleyRelaySnapshotsAsync()
    {
        try
        {
            while (true)
            {
                ViewerTelemetrySnapshot? snapshot;
                lock (_isleyRelaySnapshotGate)
                {
                    snapshot = _isleyRelayPendingSnapshot;
                    _isleyRelayPendingSnapshot = null;
                }
                if (snapshot is null)
                {
                    return;
                }
                await ApplyIsleyRelaySnapshotAsync(snapshot);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            _isleyRelayState = "error";
            _isleyRelayDetail = $"Live telemetry could not update the map: {ex.Message}";
            UpdateIsleyRelayPresentation();
        }
        finally
        {
            var restartDrain = false;
            lock (_isleyRelaySnapshotGate)
            {
                _isleyRelaySnapshotDrainScheduled = false;
                if (_isleyRelayPendingSnapshot is not null)
                {
                    _isleyRelaySnapshotDrainScheduled = true;
                    restartDrain = true;
                }
            }
            if (restartDrain)
            {
                ScheduleIsleyRelaySnapshotDrain();
            }
        }
    }

    private async Task ApplyIsleyRelaySnapshotAsync(ViewerTelemetrySnapshot snapshot)
    {
        if (_isleyRelayJoin is not { } join
            || !string.Equals(snapshot.ServerId, join.ServerId, StringComparison.Ordinal))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var self = snapshot.Self is null
            ? null
            : ToLocalPlayer(snapshot.Self);
        var players = snapshot.Players
            .Take(IsleyLiveDataProvider.MaximumPlayers)
            .Select(ToLocalPlayer)
            .ToArray();
        IsleyLiveVitals? vitals = null;
        if (snapshot.Self is
            {
                GrowthPercent: double growth,
                HealthPercent: double health,
                FoodPercent: double food,
                WaterPercent: double water
            } selfVitals)
        {
            vitals = new IsleyLiveVitals(
                selfVitals.SpeciesId,
                growth,
                health,
                100,
                food,
                100,
                water,
                100);
            var candidate = new PlayerSnapshotRaw(
                PlayerSnapshotSourceState.Live,
                selfVitals.SpeciesId,
                growth,
                health,
                100,
                food,
                100,
                water,
                100,
                null,
                null,
                null,
                snapshot.SampledAt);
            var evaluation = PlayerSnapshotLogic.Evaluate(candidate, now);
            if (evaluation.HasValidData)
            {
                ObserveLiveDinoTransition(evaluation, now);
                ObserveLiveGrowthGate(evaluation, now);
                RecordVitalsTrendSample(evaluation, now);
                _playerSnapshot = candidate;
                _playerSnapshotTransportState = "ok";
                _isleyRelayStaminaPercent = selfVitals.StaminaPercent;
                RefreshPlayerSnapshotConsumers();
            }
        }

        var localSnapshot = new IsleyLiveDataSnapshot(
            snapshot.SampledAt,
            self,
            players,
            vitals);
        if (!_streamerMode
            && LiveMapServicesActive
            && LiveMapWebView.CoreWebView2 is not null)
        {
            await LiveMapWebView.CoreWebView2.ExecuteScriptAsync(
                $"window.__isleyLocalMap?.setSnapshot({localSnapshot.ToMapJson()}); window.__isley?.refreshSelfFreshness?.()");
        }
        _liveDataAppliedUpdatedAt = snapshot.SampledAt;
        _liveDataAppliedSignature =
            $"relay|{snapshot.ServerId}|{snapshot.Sequence}|{snapshot.SampledAt.ToUnixTimeMilliseconds()}";
        _isleyRelaySnapshotAppliedAt = now;
        _isleyRelayLastUpdateRateHz = snapshot.UpdateRateHz;
        _isleyRelayLastRelayAgeMilliseconds = snapshot.RelayAgeMilliseconds;
        if (snapshot.Self is not null)
        {
            MarkAuthorizedSelfApplied(now);
        }
        if (_locationResumePendingToast
            && snapshot.Self is not null
            && !_streamerMode)
        {
            _locationResumePendingToast = false;
            _ = ShowHotkeyToastAsync("LOCATION LIVE FROM SERVER", true);
        }
        ApplyIsleyRelayConditions(snapshot.Self?.Conditions ?? []);

        var conditionLabel = snapshot.Self?.Conditions.Count > 0
            ? $" · {string.Join(", ", snapshot.Self.Conditions.Take(2)).ToUpperInvariant()}"
            : string.Empty;
        var friendCount = snapshot.Players.Count(player => player.Friend);
        var direction = snapshot.Self?.DirectionQuality switch
        {
            TelemetryDirectionQuality.ServerAuthoritative => "facing live",
            TelemetryDirectionQuality.MotionInferred => "movement heading",
            _ => "direction unavailable"
        };
        var updateRate = snapshot.UpdateRateHz is double rate
            ? $" · {rate:0.#} Hz"
            : string.Empty;
        var nodeCount = Math.Max(1, snapshot.ConnectedPlayerNodes);
        var visibility = snapshot.VisibilityPolicy == TelemetryVisibilityPolicy.ServerWide
            ? "server-wide"
            : "consent-filtered";
        var networkSummary =
            $" · {nodeCount} node{(nodeCount == 1 ? string.Empty : "s")}" +
            $" · {snapshot.VisibleEntityCount} visible · {visibility}{updateRate}";
        _isleyRelayAgeMs = snapshot.RelayAgeMilliseconds;
        _isleyRelayHz = snapshot.UpdateRateHz;
        _isleyRelayConsentFiltered =
            snapshot.VisibilityPolicy == TelemetryVisibilityPolicy.PrivacyFiltered;
        _isleyRelayFriendCount = friendCount;
        _isleyRelayState = snapshot.Self is null ? "waiting" : "live";
        _isleyRelayDetail = snapshot.Self is null
            ? $"{snapshot.ServerName} connected{networkSummary} · waiting for your Steam player"
            : $"{snapshot.ServerName} · {snapshot.RelayAgeMilliseconds:0} ms · {direction}" +
              networkSummary +
              $" · {friendCount} friend{(friendCount == 1 ? string.Empty : "s")}{conditionLabel}";
        UpdateIsleyRelayPresentation();
        MaybeShowConsentRosterCoach();
    }

    private static IsleyLivePlayer ToLocalPlayer(ViewerTelemetryEntity player) =>
        new(
            player.Id,
            player.Label,
            player.X,
            player.Y,
            player.Z,
            player.Yaw ?? 0,
            player.Self,
            player.Friend);

    private void ApplyIsleyRelayConditions(IReadOnlyList<string> conditions)
    {
        var normalized = conditions
            .Select(value => value.Trim().ToLowerInvariant())
            .Where(value => value.Length is > 0 and <= 48)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var signature = string.Join("|", normalized);
        if (string.Equals(signature, _isleyRelayConditionSignature, StringComparison.Ordinal))
        {
            return;
        }

        var previousIncident = RelayIncidentId(_isleyRelayConditionSignature.Split(
            '|',
            StringSplitOptions.RemoveEmptyEntries));
        var currentIncident = RelayIncidentId(normalized);
        _isleyRelayConditionSignature = signature;
        if (!string.IsNullOrEmpty(currentIncident))
        {
            if (!string.Equals(_survivalIncidentId, currentIncident, StringComparison.Ordinal))
            {
                ActivateSurvivalIncident(currentIncident, logEvent: true);
            }
        }
        else if (!string.IsNullOrEmpty(previousIncident)
                 && string.Equals(
                     _survivalIncidentId,
                     previousIncident,
                     StringComparison.Ordinal))
        {
            ClearSurvivalIncident(logEvent: false);
            AddTacticalEvent(
                "SURVIVAL",
                "Server-reported condition cleared",
                "The participating server no longer reports the condition");
        }
    }

    private static string RelayIncidentId(IEnumerable<string> conditions)
    {
        foreach (var condition in conditions)
        {
            if (condition.Contains("vomit", StringComparison.Ordinal)) return "vomit";
            if (condition.Contains("food-poison", StringComparison.Ordinal)
                || condition.Contains("rotten", StringComparison.Ordinal)) return "food-poisoning";
            if (condition.Contains("bacteria", StringComparison.Ordinal)) return "bacteria";
            if (condition.Contains("venom", StringComparison.Ordinal)) return "venom";
            if (condition.Contains("fracture", StringComparison.Ordinal)) return "fracture";
            if (condition.Contains("blind", StringComparison.Ordinal)) return "blindness";
            if (condition.Contains("dehydrat", StringComparison.Ordinal)) return "dehydrated";
            if (condition.Contains("starv", StringComparison.Ordinal)) return "starving";
            if (condition.Contains("sick", StringComparison.Ordinal)) return "long-sickness";
        }
        return string.Empty;
    }

    private void UpdateIsleyRelayPresentation()
    {
        if (IsleyRelayStatusText is null)
        {
            return;
        }
        if (!IsleyRelayJoinLinkInputBox.IsKeyboardFocusWithin
            && !string.Equals(
                IsleyRelayJoinLinkInputBox.Text,
                _isleyRelayJoinLink,
                StringComparison.Ordinal))
        {
            IsleyRelayJoinLinkInputBox.Text = _isleyRelayJoinLink;
        }
        var presentationState = _isleyRelayState;
        var presentationDetail = _isleyRelayDetail;
        if (_isleyRelayState == "live")
        {
            var health = TelemetryStreamHealthLogic.Assess(
                _isleyRelaySnapshotAppliedAt,
                DateTimeOffset.UtcNow,
                _isleyRelayLastUpdateRateHz,
                _isleyRelayLastRelayAgeMilliseconds);
            if (health.State == TelemetryStreamState.Waiting)
            {
                presentationState = "waiting";
                presentationDetail = "Connected - waiting for the first live telemetry frame";
            }
            else if (health.State == TelemetryStreamState.Stalled)
            {
                presentationState = "error";
                presentationDetail =
                    $"Telemetry stalled - no fresh stats for {health.SilenceMilliseconds / 1000:0.#}s - reconnecting automatically";
            }
            else if (health.State == TelemetryStreamState.Delayed)
            {
                presentationState = "waiting";
                var rate = health.UpdateRateHz is double updateRate
                    ? $"{updateRate:0.#} Hz"
                    : "rate unknown";
                presentationDetail =
                    $"{_isleyRelayDetail} - update delayed ({rate}, {health.EffectiveAgeMilliseconds:0} ms)";
            }
        }
        IsleyRelayStatusText.Text = presentationDetail.ToUpperInvariant();
        IsleyRelayStatusText.Foreground = presentationState switch
        {
            "live" => (Brush)FindResource("SuccessBrush"),
            "error" => (Brush)FindResource("WarningBrush"),
            "reconnecting" or "connecting" or "signing-in" or "waiting" =>
                (Brush)FindResource("AccentBrush"),
            _ => (Brush)FindResource("SecondaryTextBrush")
        };
        IsleyRelayConnectButton.IsEnabled =
            _isleyRelayState is not ("connecting" or "signing-in");
        IsleyRelayConnectButton.Content = _isleyRelayState == "live"
            ? "RECONNECT"
            : _isleyRelayState is "connecting" or "signing-in"
                ? "CONNECTING…"
                : "CONNECT WITH STEAM";
        IsleyRelayDisconnectButton.IsEnabled =
            _isleyRelayState is "live" or "connecting" or "reconnecting" or "waiting";
        var hasCredential = _isleyRelayJoin is { } join
                            && _isleyRelayClient.TryReadCredential(join, out _);
        IsleyRelayForgetButton.IsEnabled = hasCredential;
        IsleyRelayFriendSharingButton.IsEnabled =
            hasCredential && !_isleyRelayPrivacyRequestInFlight;
        IsleyRelayFriendSharingButton.Content = _isleyRelayShareWithSteamFriends
            ? "FRIEND SHARING · ON"
            : "FRIEND SHARING · OFF";
        IsleyRelayGrantViewerButton.IsEnabled =
            hasCredential && !_isleyRelayPrivacyRequestInFlight;
        IsleyRelayRevokeViewerButton.IsEnabled =
            hasCredential && !_isleyRelayPrivacyRequestInFlight;
        IsleyRelayViewerSteamIdInputBox.IsEnabled =
            hasCredential && !_isleyRelayPrivacyRequestInFlight;
        IsleyRelayPrivacyStatusText.Text = _isleyRelayPrivacyDetail;
        IsleyRelayPrivacyStatusText.Foreground = _isleyRelayPrivacyDetail.StartsWith(
            "Visibility was not changed",
            StringComparison.Ordinal)
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("SecondaryTextBrush");
        UpdateLiveHealthStrip();
    }

    private void UpdateLiveHealthStrip()
    {
        if (LiveHealthText is null)
        {
            return;
        }

        var voiceQualityLabel = string.Empty;
        if (_voiceBridgeRunning && _voiceQualityMonitorEnabled)
        {
            var qualityAge = _voiceQualityAt == default
                ? int.MaxValue
                : (int)Math.Clamp(
                    (DateTimeOffset.UtcNow - _voiceQualityAt).TotalMilliseconds,
                    0,
                    int.MaxValue);
            voiceQualityLabel = VoiceIntegrationLogic.PresentQuality(
                _voiceQualityMonitorEnabled,
                _voiceBridgeRunning,
                _voiceQualityPeerCount,
                _voiceQualitySampleCount,
                _voiceQualityRoundTripMilliseconds,
                _voiceQualityJitterMilliseconds,
                _voiceQualityPacketLossPercent,
                qualityAge).Label;
        }

        var health = LiveHealthLogic.Present(
            _liveHealthMapLabel,
            _isleyRelayState,
            _isleyRelayAgeMs,
            _isleyRelayHz,
            _voiceBridgeRunning,
            voiceQualityLabel,
            _voiceNetworkState,
            _streamerMode);
        LiveHealthText.Text = health.Strip;
        LiveHealthText.ToolTip = health.ToolTip;
        LiveHealthText.Foreground = health.Tone switch
        {
            "ok" => (Brush)FindResource("SuccessBrush"),
            "warn" => (Brush)FindResource("WarningBrush"),
            _ => (Brush)FindResource("SecondaryTextBrush")
        };
        if (!string.Equals(health.Announcement, _liveHealthAnnouncementSignature, StringComparison.Ordinal))
        {
            _liveHealthAnnouncementSignature = health.Announcement;
            System.Windows.Automation.AutomationProperties.SetName(
                LiveHealthText,
                health.Announcement);
        }
    }

    private async Task RefreshIndependentLiveDataAsync(bool force = false)
    {
        if (_liveDataRefreshInFlight
            || !LiveMapServicesActive
            || LiveMapWebView.CoreWebView2 is null)
        {
            return;
        }
        if (!force
            && _isleyRelaySnapshotAppliedAt is { } relayAppliedAt
            && DateTimeOffset.UtcNow - relayAppliedAt
            <= IsleyLiveDataProvider.FreshnessLimit)
        {
            return;
        }

        _liveDataRefreshInFlight = true;
        try
        {
            var path = IndependentLiveDataPath;
            if (!File.Exists(path))
            {
                if (!string.IsNullOrEmpty(_liveDataAppliedSignature))
                {
                    await LiveMapWebView.CoreWebView2.ExecuteScriptAsync(
                        "window.__isleyLocalMap?.setSnapshot({self:null,players:[],vitals:null})");
                    _liveDataAppliedSignature = string.Empty;
                    _liveDataAppliedUpdatedAt = null;
                    _playerSnapshotTransportState = "unavailable";
                    _playerSnapshot = null;
                    RefreshPlayerSnapshotConsumers();
                }
                return;
            }

            var info = new FileInfo(path);
            if (!force
                && info.LastWriteTimeUtc == _liveDataLastWriteUtc
                && info.Length == _liveDataLastLength)
            {
                if (_liveDataAppliedUpdatedAt is not null
                    && DateTimeOffset.UtcNow - _liveDataAppliedUpdatedAt.Value
                    > IsleyLiveDataProvider.FreshnessLimit
                    && !_liveDataAppliedSignature.StartsWith("stale|", StringComparison.Ordinal))
                {
                    await LiveMapWebView.CoreWebView2.ExecuteScriptAsync(
                        "window.__isleyLocalMap?.setSnapshot({self:null,players:[],vitals:null})");
                    _liveDataAppliedSignature =
                        $"stale|{_liveDataAppliedUpdatedAt.Value.ToUnixTimeMilliseconds()}";
                    _playerSnapshotTransportState = "unavailable";
                    _playerSnapshot = null;
                    RefreshPlayerSnapshotConsumers();
                }
                return;
            }
            if (info.Length <= 0 || info.Length > IsleyLiveDataProvider.MaximumBytes)
            {
                throw new InvalidDataException("The Isley live-data file was empty or too large.");
            }

            var json = await File.ReadAllTextAsync(path);
            var now = DateTimeOffset.UtcNow;
            var snapshot = IsleyLiveDataProvider.Parse(json, now);
            _liveDataAppliedUpdatedAt = snapshot.UpdatedAt;
            _liveDataLastWriteUtc = info.LastWriteTimeUtc;
            _liveDataLastLength = info.Length;
            var fresh = now - snapshot.UpdatedAt <= IsleyLiveDataProvider.FreshnessLimit;
            var signature = fresh
                ? $"{snapshot.UpdatedAt.ToUnixTimeMilliseconds()}|{info.Length}"
                : $"stale|{snapshot.UpdatedAt.ToUnixTimeMilliseconds()}";
            if (!force && string.Equals(signature, _liveDataAppliedSignature, StringComparison.Ordinal))
            {
                return;
            }

            var payload = fresh
                ? snapshot.ToMapJson()
                : """{"self":null,"players":[],"vitals":null}""";
            await LiveMapWebView.CoreWebView2.ExecuteScriptAsync(
                fresh
                    ? $"window.__isleyLocalMap?.setSnapshot({payload}); window.__isley?.refreshSelfFreshness?.()"
                    : $"window.__isleyLocalMap?.setSnapshot({payload})");
            _liveDataAppliedSignature = signature;
            if (fresh && snapshot.Self is not null)
            {
                MarkAuthorizedSelfApplied(now);
            }
            if (!fresh)
            {
                _playerSnapshotTransportState = "unavailable";
                _playerSnapshot = null;
                RefreshPlayerSnapshotConsumers();
            }
        }
        catch (Exception ex) when (
            ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidDataException
            or InvalidOperationException)
        {
            _liveDataAppliedSignature = $"error|{ex.GetType().Name}";
            _playerSnapshotTransportState = "error";
            RefreshPlayerSnapshotConsumers();
        }
        finally
        {
            _liveDataRefreshInFlight = false;
        }
    }

    private void AcceptUniversalCoordinateCapture(UniversalCoordinatePoint point)
    {
        var now = DateTimeOffset.UtcNow;
        MarkAuthorizedSelfApplied(now);
        if (!UniversalCoordinateLogic.SamePoint(_universalCoordinatePoint, point))
        {
            _universalCoordinatePreviousPoint = _universalCoordinatePoint;
            _universalCoordinatePreviousCapturedAt = _universalCoordinateCapturedAt;
            _universalCoordinatePoint = point;
            _universalCoordinateCapturedAt = now;
            _universalCoordinateTrack.Add(new UniversalTrackSample(point, now));
            if (_universalCoordinateTrack.Count > 9)
            {
                _universalCoordinateTrack.RemoveAt(0);
            }
            _universalTrackEstimate = UniversalCoordinateLogic.EstimateTrack(_universalCoordinateTrack);
            if (_universalTrackEstimate is { DirectionAgreement: >= 0.35 } estimate)
            {
                _universalCoordinateHeadingDegrees = estimate.HeadingDegrees;
                _universalCoordinateHeadingAvailable = true;
            }
            else
            {
                var heading = UniversalCoordinateLogic.ResolveHeading(
                    _universalCoordinatePreviousPoint,
                    _universalCoordinatePoint,
                    _universalCoordinateHeadingDegrees,
                    _universalCoordinateHeadingAvailable);
                if (heading.Updated)
                {
                    _universalCoordinateHeadingDegrees = heading.Degrees;
                    _universalCoordinateHeadingAvailable = true;
                }
            }
            _universalCoordinateMovement = UniversalCoordinateLogic.DescribeMovement(
                _universalCoordinatePreviousPoint,
                _universalCoordinatePoint,
                _universalCoordinatePreviousCapturedAt is null
                    ? TimeSpan.Zero
                    : now - _universalCoordinatePreviousCapturedAt.Value);
        }
        else
        {
            _universalCoordinateCapturedAt = now;
        }

        _universalCoordinateCaptureCount++;
        _universalCoordinateUiSignature = string.Empty;
        _ = SyncUniversalCoordinateToLocalMapAsync(point);
        UpdateUniversalCoordinatePresentation(force: true);
        AddTacticalEvent(
            "ROUTE",
            "Asset Location captured",
            $"Player Sync update {_universalCoordinateCaptureCount} · exact coordinates remain session-only");

        UniversalCoordinatePositionText.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.42,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(220)
            });
    }

    private async Task SyncUniversalCoordinateToLocalMapAsync(UniversalCoordinatePoint point)
    {
        if (!LiveMapServicesActive
            || _streamerMode
            || LiveMapWebView.CoreWebView2 is null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            id = "self-coordinate",
            label = "You",
            x = point.X,
            y = point.Y,
            z = point.Z,
            yaw = _universalCoordinateHeadingAvailable ? _universalCoordinateHeadingDegrees : 0,
            self = true,
            updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
        try
        {
            await LiveMapWebView.CoreWebView2.ExecuteScriptAsync(
                $"window.__isleyLocalMap?.setSelf({payload}); window.__isley?.refreshSelfFreshness?.()");
        }
        catch (InvalidOperationException)
        {
            // A navigation or shutdown can dispose the map between capture and display.
        }
    }

    private void UpdateUniversalCoordinatePresentation(bool force = false)
    {
        if (UniversalCoordinatePanel is null
            || UniversalCoordinateStatusText is null
            || UniversalCoordinateAdviceText is null
            || TerrainProbePanel is null
            || TerrainProbeStateText is null)
        {
            return;
        }

        var universalAvailable = !LiveMapServicesActive && !_streamerMode;
        var mapAvailable = LiveMapServicesActive && !_streamerMode;
        UniversalCoordinatePanel.Visibility = universalAvailable ? Visibility.Visible : Visibility.Collapsed;
        TerrainProbePanel.Visibility = mapAvailable ? Visibility.Visible : Visibility.Collapsed;
        if (_streamerMode)
        {
            return;
        }

        var mapReady = mapAvailable
                       && _followControllerInstalled
                       && LiveMapWebView.CoreWebView2 is not null;
        var obstacleLimitReached = _noGoAreaCount >= NoGoAreaLogic.MaximumAreaCount;
        var ageMs = _universalCoordinateCapturedAt is null
            ? 0
            : Math.Max(0, (DateTimeOffset.UtcNow - _universalCoordinateCapturedAt.Value).TotalMilliseconds);
        var ageBucket = (int)Math.Floor(ageMs / 1000);
        var hill = UniversalCoordinateLogic.DescribeHill(_universalCoordinateMovement);
        var slopePresentation = SlopeSafetyLogic.Present(
            _universalCoordinateCaptureEnabled,
            _universalCoordinateMovement,
            mapReady,
            obstacleLimitReached);
        var signature = string.Join('|',
            LiveMapServicesActive,
            _universalCoordinateCaptureEnabled,
            _universalCoordinateCaptureCount,
            _universalCoordinatePoint,
            _universalCoordinateMovement,
            _universalTrackEstimate,
            mapReady,
            _noGoAreaCount,
            ageBucket);
        if (!force && string.Equals(signature, _universalCoordinateUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _universalCoordinateUiSignature = signature;

        UniversalCoordinateCaptureButton.Content = _universalCoordinateCaptureEnabled
            ? "PLAYER SYNC ON"
            : "PLAYER SYNC OFF";
        UniversalCoordinateCaptureButton.ToolTip = _universalCoordinateCaptureEnabled
            ? "Pause Player Sync; Isley will stop accepting Asset Location copies"
            : "Enable coordinate-only Player Sync while The Isle or Isley is active";
        SetToggleButtonState(UniversalCoordinateCaptureButton, _universalCoordinateCaptureEnabled);
        TerrainProbeToggleButton.Content = _universalCoordinateCaptureEnabled
            ? "PLAYER SYNC ON"
            : "PLAYER SYNC OFF";
        TerrainProbeToggleButton.ToolTip = UniversalCoordinateCaptureButton.ToolTip;
        SetToggleButtonState(TerrainProbeToggleButton, _universalCoordinateCaptureEnabled);
        UpdatePlayerSyncMapButton();
        UniversalCoordinateClearButton.IsEnabled = _universalCoordinatePoint is not null;
        TerrainProbeClearButton.IsEnabled = _universalCoordinatePoint is not null;

        TerrainProbeHeadingText.Text = slopePresentation.Heading;
        TerrainProbeStateText.Text = slopePresentation.State;
        TerrainProbeDetailText.Text = slopePresentation.Detail;
        TerrainProbeGuidanceText.Text = slopePresentation.Guidance;
        TerrainProbeSaveAvoidanceButton.Content = slopePresentation.SaveLabel;
        TerrainProbeSaveAvoidanceButton.ToolTip = slopePresentation.SaveTooltip;
        TerrainProbeSaveAvoidanceButton.IsEnabled = slopePresentation.CanSaveAvoidance
                                                     && _universalCoordinatePreviousPoint is not null
                                                     && _universalCoordinatePoint is not null;
        var slopeBrush = slopePresentation.State switch
        {
            "LEVEL" => (Brush)FindResource("SuccessBrush"),
            "HIGH" => Brushes.OrangeRed,
            "ELEVATED" => (Brush)FindResource("WarningBrush"),
            "MEASURED" => (Brush)FindResource("AccentBrush"),
            _ => (Brush)FindResource("SecondaryTextBrush")
        };
        TerrainProbeStateText.Foreground = slopeBrush;
        TerrainProbeHeadingText.Foreground = slopeBrush;
        TerrainProbeGuidanceText.Foreground = slopePresentation.Severity > 0
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("SecondaryTextBrush");
        UniversalCoordinateAdviceText.Text = slopePresentation.Guidance;
        UniversalCoordinateAdviceText.Foreground = TerrainProbeGuidanceText.Foreground;

        if (_universalCoordinatePoint is null)
        {
            UniversalCoordinateStatusText.Text = _universalCoordinateCaptureEnabled
                ? "PLAYER SYNC READY · COPY LOCATION"
                : "PLAYER SYNC OFF · CLIPBOARD UNTOUCHED";
            UniversalCoordinateStatusText.Foreground = _universalCoordinateCaptureEnabled
                ? (Brush)FindResource("SuccessBrush")
                : (Brush)FindResource("SecondaryTextBrush");
            UniversalCoordinatePositionText.Text = "X —   Y —   Z —";
            UniversalCoordinateMotionText.Text = _universalCoordinateCaptureEnabled
                ? "IN THE ISLE: TAB → CLICK ASSET LOCATION"
                : "1 TURN SYNC ON · 2 TAB → ASSET LOCATION IN GAME";
            UniversalCoordinateHillText.Text = "HILL CHECK · COPY TWO POINTS AFTER MOVING";
            UniversalCoordinateHillText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            UniversalCoordinatePositionText.ToolTip = null;
            return;
        }

        UniversalCoordinateStatusText.Text = _universalCoordinateCaptureEnabled
            ? $"LIVE · {FormatElapsedAge(ageMs)} · {_universalCoordinateCaptureCount}"
            : $"PAUSED · {FormatElapsedAge(ageMs)}";
        UniversalCoordinateStatusText.Foreground = _universalCoordinateCaptureEnabled
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("WarningBrush");
        UniversalCoordinatePositionText.Text =
            $"X {FormatUniversalCoordinate(_universalCoordinatePoint.X)}   " +
            $"Y {FormatUniversalCoordinate(_universalCoordinatePoint.Y)}   " +
            $"Z {FormatUniversalCoordinate(_universalCoordinatePoint.Z)}";
        UniversalCoordinatePositionText.ToolTip =
            "Session-only position copied from The Isle's Asset Location control";
        UniversalCoordinateMotionText.Text = _universalCoordinateMovement is null
            ? "COPY AGAIN AFTER MOVING TO CALCULATE THE SESSION DELTA"
            : $"MOVE {_universalCoordinateMovement.HorizontalDistance:0} WU · " +
              $"{_universalCoordinateMovement.AxisCourse} · " +
              $"Z {_universalCoordinateMovement.AltitudeDelta:+0;-0;0} · " +
              $"{FormatUniversalCaptureInterval(_universalCoordinateMovement.ElapsedSeconds)}" +
              (_universalTrackEstimate is null
                  ? string.Empty
                  : $" · COURSE {_universalTrackEstimate.HeadingDegrees:0}°" +
                    $" ~{_universalTrackEstimate.SpeedWorldUnitsPerSecond:0.0} WU/S" +
                    $" {_universalTrackEstimate.ConfidenceLabel}");
        UniversalCoordinateHillText.Text = hill is null
            ? _universalCoordinateMovement is null
                ? "HILL CHECK · COPY A SECOND POINT AFTER MOVING"
                : "HILL CHECK · MOVE 5+ WU, THEN COPY AGAIN"
            : hill.Direction == "LEVEL"
                ? $"HILL CHECK · LEVEL · {hill.GradePercent:0.0}% · {hill.AngleDegrees:0.0}°"
                : $"HILL CHECK · {hill.Direction} {hill.GradePercent:0.0}% · " +
                  $"{hill.AngleDegrees:0.0}° · {hill.RiseOrDrop:0} WU";
        UniversalCoordinateHillText.Foreground = hill is null
            ? (Brush)FindResource("SecondaryTextBrush")
            : (Brush)FindResource("WarningBrush");
    }

    private static string FormatUniversalCoordinate(double value) =>
        value.ToString("#,0.0", CultureInfo.InvariantCulture);

    private static string FormatUniversalCaptureInterval(double seconds) =>
        seconds >= 60
            ? $"{Math.Floor(seconds / 60):0}M {Math.Floor(seconds % 60):0}S"
            : $"{Math.Max(1, Math.Floor(seconds)):0}S";

    private void ClearUniversalCoordinateSession(bool updateUi = true)
    {
        _universalCoordinatePoint = null;
        _universalCoordinatePreviousPoint = null;
        _universalCoordinateMovement = null;
        _universalCoordinateTrack.Clear();
        _universalTrackEstimate = null;
        _universalCoordinateHeadingDegrees = 0;
        _universalCoordinateHeadingAvailable = false;
        _universalCoordinateCapturedAt = null;
        _universalCoordinatePreviousCapturedAt = null;
        _universalCoordinateCaptureCount = 0;
        _universalCoordinateUiSignature = string.Empty;
        if (updateUi)
        {
            UpdateUniversalCoordinatePresentation(force: true);
        }
    }

    private void ToggleUniversalCoordinateCapture()
    {
        _universalCoordinateCaptureEnabled = !_universalCoordinateCaptureEnabled;
        ResetUniversalCoordinateClipboardBaseline();
        _universalCoordinateUiSignature = string.Empty;
        UpdateUniversalCoordinatePresentation(force: true);
        SaveSettings();
    }

    private void UniversalCoordinateCaptureButton_Click(object sender, RoutedEventArgs e) =>
        ToggleUniversalCoordinateCapture();

    private void UniversalCoordinateClearButton_Click(object sender, RoutedEventArgs e) =>
        ClearUniversalCoordinateSession();

    private async Task ApplyServerSessionAsync(string profileId)
    {
        var nextId = ServerSessionLogic.NormalizeProfileId(profileId);
        if (string.Equals(nextId, _serverSessionProfileId, StringComparison.Ordinal))
        {
            UpdateServerSessionPresentation();
            return;
        }

        var previousName = ServerSessionLogic.DisplayName(_serverSessionProfileId, _serverSessionName);
        SyncCurrentCommunityServerProfile();
        _serverStatusTimer.Stop();
        _serverStatusCancellation?.Cancel();
        CancelServerRestartWatch(logEvent: false, updateUi: false);
        ResetWaterCrossingCheck(logEvent: false);
        ResetShorelineCheck(logEvent: false);
        _measurementArmed = false;
        _measurementHasStart = false;
        _measurementActive = false;
        ClearMeasurementValues();
        _serverSessionProfileId = nextId;
        _playerSnapshot = null;
        _playerSnapshotTransportState = "unavailable";
        ClearLifeTransitionSession();
        ClearVitalsTrendSamples();
        ClearCoreVitals(logEvent: false, updateUi: false);
        ClearFieldConditions(logEvent: false, updateUi: false);
        ClearManualSighting(logEvent: false, updateUi: false, resetDraft: true, collapse: true);
        ClearUniversalCoordinateSession(updateUi: false);
        ResetUniversalCoordinateClipboardBaseline();
        var nextProfile = ServerSessionLogic.Find(nextId);
        if (string.Equals(nextId, ServerSessionLogic.CommunityId, StringComparison.Ordinal)
            && CurrentCommunityServerProfile().GrowthMultiplierIndex >= 0)
        {
            _growthServerMultiplierIndex = CurrentCommunityServerProfile().GrowthMultiplierIndex;
            _growthPlannerUiSignature = string.Empty;
            _lifeRunUiSignature = string.Empty;
        }
        UpdateServerSessionPresentation(animate: true);
        UpdateManualSighting(force: true);
        SaveSettings();

        if (nextProfile.LiveMapServicesAvailable)
        {
            if (_terrainRoadNetwork is null)
            {
                _ = LoadTerrainRoadNetworkAsync();
            }
            if (_gatewayResourceNetwork is null)
            {
                _ = LoadGatewayResourceNetworkAsync();
            }
            _serverStatusTimer.Start();
            if (LiveMapWebView.CoreWebView2 is null)
            {
                await InitializeLiveMapAsync();
            }
            else if (LiveMapWebView.Source is null || !IsLiveMapUri(LiveMapWebView.Source))
            {
                LiveMapWebView.CoreWebView2.Navigate(LocalMapUri);
            }
            _ = RefreshIndependentLiveDataAsync(force: true);
        }
        else
        {
            await SuspendLiveMapServicesAsync();
            if (ShouldPollServerStatus)
            {
                _serverStatusTimer.Start();
                _ = RefreshServerStatusAsync();
            }
        }

        var displayName = ServerSessionLogic.DisplayName(nextId, _serverSessionName);
        AddTacticalEvent(
            "SESSION",
            $"Session set to {displayName}",
            nextProfile.LiveMapServicesAvailable
            ? "Bundled Isley map and independent provider services enabled"
                : "Universal tools active; server-fed live services unavailable");
        await ShowHotkeyToastAsync(
            nextProfile.LiveMapServicesAvailable
            ? "LIVE MAP SERVICES ON"
                : $"{displayName.ToUpperInvariant()} · UNIVERSAL TOOLS",
            true);
        if (!string.Equals(previousName, displayName, StringComparison.OrdinalIgnoreCase))
        {
            UpdateTacticalBrief();
        }
    }

    private async void ServerSessionModeButton_Click(object sender, RoutedEventArgs e) =>
        await ApplyServerSessionAsync(ServerSessionLogic.NextProfileId(_serverSessionProfileId));

    private async void ServerSessionProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string profileId })
        {
            return;
        }

        await ApplyServerSessionAsync(profileId);
        if (_onboardingTutorialOpen)
        {
            UpdateOnboardingTutorial();
            OnboardingNextButton.Focus();
        }
    }

    private async void ServerSessionLiveMapButton_Click(object sender, RoutedEventArgs e) =>
        await ApplyServerSessionAsync(ServerSessionLogic.LiveMapId);

    private void ServerSessionNameInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressServerSessionNameChanges
            || !string.Equals(_serverSessionProfileId, ServerSessionLogic.CommunityId, StringComparison.Ordinal))
        {
            return;
        }

        _serverSessionName = ServerSessionNameInputBox.Text;
        UniversalSessionTitleText.Text = ServerSessionLogic.DisplayName(
            _serverSessionProfileId, _serverSessionName).ToUpperInvariant();
        ServerSessionModeText.Text = UniversalSessionTitleText.Text;
        SaveSettings();
    }

    private void NormalizeServerSessionName()
    {
        _serverSessionName = ServerSessionLogic.NormalizeCustomServerName(ServerSessionNameInputBox.Text);
        SetServerSessionNameInput(_serverSessionName);
        UpdateServerSessionPresentation();
        SaveSettings();
    }

    private void ServerSessionNameInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        NormalizeServerSessionName();
        Focus();
    }

    private void ServerSessionNameInputBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        NormalizeServerSessionName();

    private void CommunityServerAddressInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressCommunityServerAddressChanges || !CommunitySessionActive)
        {
            return;
        }

        var nextAddress = CommunityServerWatchLogic.SanitizeAddressInput(
            CommunityServerAddressInputBox.Text);
        if (string.Equals(nextAddress, _communityServerAddress, StringComparison.Ordinal))
        {
            return;
        }

        _communityServerAddress = nextAddress;
        _communityServerWatchEnabled = false;
        _lastCommunityServerStatus = null;
        _communityServerStatusError = string.Empty;
        _communityServerWasFull = null;
        _serverStatusCancellation?.Cancel();
        if (!LiveMapServicesActive)
        {
            _serverStatusTimer.Stop();
        }
        UpdateCommunityServerWatchPresentation();
        UpdateServerStatusPresentation();
        SaveSettings();
    }

    private void NormalizeCommunityServerAddress()
    {
        var input = CommunityServerWatchLogic.SanitizeAddressInput(
            CommunityServerAddressInputBox.Text);
        _communityServerAddress = CommunityServerWatchLogic.TryNormalizeAddress(input, out var normalized)
            ? normalized
            : input;
        SetCommunityServerAddressInput(_communityServerAddress);
        UpdateCommunityServerWatchPresentation();
        UpdateServerStatusPresentation();
        SaveSettings();
    }

    private async void CommunityServerAddressInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        NormalizeCommunityServerAddress();
        Focus();
        if (CommunityServerAddressValid)
        {
            await RefreshServerStatusAsync(userInitiated: true);
        }
    }

    private void CommunityServerAddressInputBox_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e) => NormalizeCommunityServerAddress();

    private async void CommunityServerWatchButton_Click(object sender, RoutedEventArgs e)
    {
        NormalizeCommunityServerAddress();
        if (!CommunityServerWatchLogic.TryNormalizeAddress(_communityServerAddress, out _))
        {
            await ShowHotkeyToastAsync("ENTER A VALID HOST:PORT", false);
            return;
        }

        _communityServerWatchEnabled = !_communityServerWatchEnabled;
        if (_communityServerWatchEnabled)
        {
            _serverStatusTimer.Start();
            await RefreshServerStatusAsync(userInitiated: true);
        }
        else
        {
            _serverStatusTimer.Stop();
            _serverStatusCancellation?.Cancel();
            await ShowHotkeyToastAsync("OPTIONAL PUBLIC WATCH OFF", true);
        }
        UpdateCommunityServerWatchPresentation();
        SaveSettings();
    }

    private async void CommunityServerRefreshButton_Click(object sender, RoutedEventArgs e) =>
        await RefreshServerStatusAsync(userInitiated: true);

    private async void CommunityServerSlotAlertButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CommunityServerAddressValid)
        {
            await ShowHotkeyToastAsync("ENTER A VALID HOST:PORT", false);
            return;
        }

        _communityServerSlotAlertEnabled = !_communityServerSlotAlertEnabled;
        _communityServerWasFull = _lastCommunityServerStatus is { Online: true } current
            ? current.Players >= current.Capacity
            : null;
        UpdateCommunityServerWatchPresentation();
        SaveSettings();
        await ShowHotkeyToastAsync(
            _communityServerSlotAlertEnabled ? "SLOT ALERT ARMED" : "SLOT ALERT OFF",
            true);
    }

    private async void CommunityServerCopyNameButton_Click(object sender, RoutedEventArgs e)
    {
        var serverName = ServerSessionLogic.NormalizeCustomServerName(_serverSessionName);
        try
        {
            Clipboard.SetText(serverName);
            await ShowHotkeyToastAsync("SERVER SEARCH NAME COPIED", true);
        }
        catch
        {
            await ShowHotkeyToastAsync("CLIPBOARD UNAVAILABLE", false);
        }
    }

    private async void CommunityServerJoinGuideButton_Click(object sender, RoutedEventArgs e)
    {
        EnsureCommunityServerProfiles();
        var boundJoin = SanitizeCommunityIsleyJoinLink(CurrentCommunityServerProfile().IsleyJoinLink);
        if (!string.IsNullOrWhiteSpace(boundJoin)
            && IsleyRelayJoinLogic.TryParse(boundJoin, out _))
        {
            _isleyRelayJoinLink = boundJoin;
            if (IsleyRelayJoinLinkInputBox is not null)
            {
                IsleyRelayJoinLinkInputBox.Text = boundJoin;
            }
            UpdateIsleyRelayPresentation();
            SaveSettings();
            await ShowHotkeyToastAsync("ISLEY JOIN LINK LOADED FROM COMMUNITY PROFILE", true);
            return;
        }

        OpenExternalUri(OverlayLinks.UnofficialServerGuide);
    }

    private void UpdateCommunityServerWatchPresentation()
    {
        if (CommunityServerWatchPanel is null || CommunityServerStatusText is null)
        {
            return;
        }

        var valid = CommunityServerWatchLogic.TryNormalizeAddress(
            _communityServerAddress, out var normalizedAddress);
        CommunityServerWatchButton.IsEnabled = valid;
        CommunityServerWatchButton.Content = _communityServerWatchEnabled ? "WATCH ON" : "WATCH OFF";
        SetToggleButtonState(CommunityServerWatchButton, _communityServerWatchEnabled);
        CommunityServerRefreshButton.IsEnabled = valid && !_serverStatusRefreshInFlight;
        CommunityServerRefreshButton.Content = _serverStatusRefreshInFlight && CommunitySessionActive
            ? "CHECKING"
            : "CHECK NOW";
        CommunityServerSlotAlertButton.IsEnabled = valid;
        CommunityServerSlotAlertButton.Content = _communityServerSlotAlertEnabled
            ? "SLOT ON"
            : "SLOT OFF";
        SetToggleButtonState(CommunityServerSlotAlertButton, _communityServerSlotAlertEnabled);
        CommunityServerCopyNameButton.IsEnabled = CommunitySessionActive;

        var neutralBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
        var warningBrush = (Brush)FindResource("WarningBrush");
        var successBrush = (Brush)FindResource("SuccessBrush");
        var offlineBrush = new SolidColorBrush(Color.FromRgb(248, 113, 113));
        if (!valid)
        {
            CommunityServerStatusText.Text = string.IsNullOrWhiteSpace(_communityServerAddress)
                ? "ENTER HOST:PORT"
                : "ADDRESS NEEDS HOST:PORT";
            CommunityServerStatusDetailText.Text =
                "Opt-in public listing only · no live positions";
            CommunityServerStatusDot.Fill = neutralBrush;
            CommunityServerOccupancyFill.Width = 0;
            return;
        }

        var status = _lastCommunityServerStatus;
        if (status is null)
        {
            CommunityServerStatusText.Text = _serverStatusRefreshInFlight && CommunitySessionActive
                ? "CHECKING PUBLIC LISTING"
                : string.IsNullOrEmpty(_communityServerStatusError)
                    ? _communityServerWatchEnabled ? "WATCH ARMED · FIRST CHECK" : "READY · WATCH OFF"
                    : "NOT LISTED OR UNAVAILABLE";
            CommunityServerStatusDetailText.Text = string.IsNullOrEmpty(_communityServerStatusError)
                ? $"{normalizedAddress} · fixed public provider"
                : $"{normalizedAddress} · verify the public query address";
            CommunityServerStatusDot.Fill = string.IsNullOrEmpty(_communityServerStatusError)
                ? _serverStatusRefreshInFlight ? warningBrush : neutralBrush
                : warningBrush;
            CommunityServerOccupancyFill.Width = 0;
            return;
        }

        var sourceAge = status.SourceUpdatedAt is null
            ? (TimeSpan?)null
            : DateTimeOffset.Now - status.SourceUpdatedAt.Value;
        var checkAge = DateTimeOffset.Now - status.RetrievedAt;
        var stale = !string.IsNullOrEmpty(_communityServerStatusError)
                    || sourceAge is { TotalMinutes: > 30 }
                    || checkAge.TotalMinutes > 3;
        var full = status.Online && status.Players >= status.Capacity;
        var openSlots = status.Online ? Math.Max(0, status.Capacity - status.Players) : 0;
        var stateBrush = !status.Online
            ? offlineBrush
            : stale || full
                ? warningBrush
                : successBrush;
        CommunityServerStatusText.Text = !status.Online
            ? "PUBLIC LISTING · OFFLINE"
            : full
                ? $"FULL · {status.Players}/{status.Capacity}"
                : $"{openSlots} SLOT{(openSlots == 1 ? string.Empty : "S")} OPEN · {status.Players}/{status.Capacity}";
        CommunityServerStatusDetailText.Text =
            $"{status.DisplayName} · {status.Map}\n" +
            (string.IsNullOrEmpty(_communityServerStatusError)
                ? $"Checked {FormatStatusAge(checkAge)} ago · public listing only"
                : "Last refresh failed · showing last good snapshot");
        CommunityServerStatusDot.Fill = stateBrush;
        CommunityServerOccupancyFill.Width = 148 * status.Occupancy;
        CommunityServerOccupancyFill.Background = stateBrush;
        var tooltip =
            $"{status.DisplayName}\n{status.ConnectAddress}\n" +
            $"{status.Players}/{status.Capacity} players · {status.Version}";
        CommunityServerStatusText.ToolTip = tooltip;
        CommunityServerStatusDot.ToolTip = tooltip;
    }

    private async void ServerSessionGrowthButton_Click(object sender, RoutedEventArgs e)
    {
        var growthMultiplierIndex = CurrentServerGrowthMultiplierIndex();
        if (growthMultiplierIndex < 0)
        {
            OpenMapToolsAtSection("growth-clock");
            await ShowHotkeyToastAsync("SET THE SERVER'S ADVERTISED GROWTH RATE", true);
            return;
        }

        _growthServerMultiplierIndex = growthMultiplierIndex;
        _growthPlannerUiSignature = string.Empty;
        UpdateLifeRun(force: true);
        SaveSettings();
        await ShowHotkeyToastAsync(
            $"GROWTH CLOCK · {GrowthPlannerLogic.ServerMultipliers[_growthServerMultiplierIndex]:0.#}X SERVER",
            true);
    }

    private void UniversalSessionGuideButton_Click(object sender, RoutedEventArgs e) =>
        OpenToolsWorkspace("guide");

    private void UniversalSessionSightingButton_Click(object sender, RoutedEventArgs e) =>
        OpenManualSighting();

    private void UniversalSessionLifeRunButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("life-run");

    private void UniversalSessionTimersButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("timers");

    private void UniversalSessionFieldConditionsButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("field-conditions");

    private void UniversalSessionVitalsButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("core-vitals");

    private void UniversalSessionTripButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("trip-check");

    private void UniversalSessionFightButton_Click(object sender, RoutedEventArgs e) =>
        OpenFightCheck();

    private void UniversalSessionGrowthButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("growth-clock");

    private void UniversalSessionNestButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("nest-planner");

    private void UniversalSessionAppButton_Click(object sender, RoutedEventArgs e) =>
        OpenToolsWorkspace("overlay");

    private PatchWatchGuidance CurrentPatchWatchGuidance() =>
        PatchWatchLogic.Evaluate(
            _lastOfficialPatch,
            _officialPatchRefreshInFlight,
            !string.IsNullOrEmpty(_officialPatchError),
            DateTimeOffset.Now,
            LiveMapServicesActive ? _lastServerStatus?.Version : null);

    private async Task RefreshOfficialPatchAsync(bool userInitiated = false)
    {
        if (_officialPatchRefreshInFlight)
        {
            if (userInitiated)
            {
                await ShowHotkeyToastAsync("PATCH CHECK ALREADY RUNNING", true);
            }
            return;
        }

        _officialPatchRefreshInFlight = true;
        UpdateOfficialPatchPresentation();
        _officialPatchCancellation?.Cancel();
        _officialPatchCancellation?.Dispose();
        _officialPatchCancellation = new CancellationTokenSource();
        var cancellationToken = _officialPatchCancellation.Token;

        try
        {
            var latest = await OfficialPatchWatchClient.FetchAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested || !IsLoaded)
            {
                return;
            }

            _lastOfficialPatch = latest;
            _officialPatchError = string.Empty;
            var guidance = CurrentPatchWatchGuidance();
            UpdateOfficialPatchPresentation();
            var warningAnnounced = await AnnouncePatchReviewIfNeededAsync();
            if (userInitiated && !warningAnnounced)
            {
                var message = guidance.State switch
                {
                    PatchWatchState.Current => $"PUBLIC {latest.Version} · GUIDES CURRENT",
                    PatchWatchState.BaselineAhead => "ISLEY BASELINE AHEAD OF FEED",
                    _ => $"PATCH {latest.Version} · REVIEW GUIDES"
                };
                await ShowHotkeyToastAsync(message, guidance.State == PatchWatchState.Current);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Closing the overlay or replacing a request cancels the fixed official feed check.
        }
        catch (Exception ex) when (ex is HttpRequestException
                                   or TaskCanceledException
                                   or JsonException
                                   or InvalidDataException)
        {
            _officialPatchError = "The official Steam news feed did not answer.";
            UpdateOfficialPatchPresentation();
            if (userInitiated)
            {
                await ShowHotkeyToastAsync("OFFICIAL PATCH CHECK UNAVAILABLE", false);
            }
        }
        finally
        {
            _officialPatchRefreshInFlight = false;
            if (IsLoaded)
            {
                UpdateOfficialPatchPresentation();
            }
        }
    }

    private void UpdateOfficialPatchPresentation()
    {
        if (PatchWatchHeadingText is null
            || PatchWatchVersionText is null
            || PatchWatchFreshnessText is null
            || PatchWatchDetailText is null
            || PatchWatchStatusDot is null
            || PatchWatchCard is null
            || PatchWatchImpactPanel is null
            || PatchWatchImpactHeadingText is null
            || PatchWatchImpactDetailText is null
            || PatchWatchImpactScopeText is null
            || PatchWatchImpactCopyButton is null
            || PatchWatchRefreshButton is null
            || PatchWatchNotesButton is null)
        {
            return;
        }

        var guidance = CurrentPatchWatchGuidance();
        var neutralBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
        var brush = guidance.State switch
        {
            PatchWatchState.Current => (Brush)FindResource("SuccessBrush"),
            PatchWatchState.ReviewNeeded or PatchWatchState.ServerAhead => (Brush)FindResource("WarningBrush"),
            PatchWatchState.Checking => (Brush)FindResource("AccentBrush"),
            _ => neutralBrush
        };

        PatchWatchStatusDot.Fill = brush;
        PatchWatchCard.BorderBrush = brush;
        PatchWatchHeadingText.Foreground = brush;
        PatchWatchHeadingText.Text = guidance.Heading;
        PatchWatchVersionText.Text = guidance.VersionLine;
        PatchWatchFreshnessText.Text = guidance.FreshnessLine;
        PatchWatchDetailText.Text = guidance.Detail;
        var impact = PatchWatchLogic.BuildImpact(guidance, _lastOfficialPatch?.NotesUrl);
        PatchWatchImpactPanel.Visibility = impact.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
        PatchWatchImpactHeadingText.Text = impact.Heading;
        PatchWatchImpactDetailText.Text = impact.Detail;
        PatchWatchImpactScopeText.Text = impact.ScopeLine;
        PatchWatchImpactCopyButton.IsEnabled = impact.Visible;
        PatchWatchRefreshButton.IsEnabled = !_officialPatchRefreshInFlight;
        PatchWatchRefreshButton.Content = _officialPatchRefreshInFlight ? "CHECKING" : "REFRESH";
        PatchWatchNotesButton.Content = guidance.HasNotes ? "NOTES" : "SOURCE";
        PatchWatchNotesButton.ToolTip = guidance.HasNotes
            ? $"Open {guidance.VersionLine.Split('·')[0].Trim()} official patch notes"
            : "Open The Isle's official Steam announcements";
    }

    private async Task RefreshServerStatusAsync(bool userInitiated = false)
    {
        if (!CommunitySessionActive)
        {
            if (userInitiated)
            {
                await ShowHotkeyToastAsync("PUBLIC WATCH IS OPTIONAL IN ANY SERVER MODE", true);
            }
            return;
        }

        var communityAddress = string.Empty;
        if (!CommunityServerWatchLogic.TryNormalizeAddress(
                _communityServerAddress, out communityAddress))
        {
            if (userInitiated)
            {
                await ShowHotkeyToastAsync("ENTER A VALID HOST:PORT", false);
            }
            UpdateCommunityServerWatchPresentation();
            return;
        }
        if (!_communityServerWatchEnabled && !userInitiated)
        {
            return;
        }

        if (_serverStatusRefreshInFlight)
        {
            if (userInitiated)
            {
                await ShowHotkeyToastAsync("SERVER CHECK ALREADY RUNNING", true);
            }
            return;
        }

        _serverStatusRefreshInFlight = true;
        RefreshServerStatusButton.IsEnabled = false;
        CommunityServerRefreshButton.IsEnabled = false;
        UpdateCommunityServerWatchPresentation();

        _serverStatusCancellation?.Cancel();
        _serverStatusCancellation?.Dispose();
        _serverStatusCancellation = new CancellationTokenSource();
        var cancellationToken = _serverStatusCancellation.Token;
        var slotAlertTriggered = false;

        try
        {
            var status = await IsleServerStatusClient.FetchPublicAsync(
                communityAddress,
                ServerSessionLogic.DisplayName(_serverSessionProfileId, _serverSessionName),
                cancellationToken);
            if (cancellationToken.IsCancellationRequested || !IsLoaded)
            {
                return;
            }

            var decision = CommunityServerWatchLogic.EvaluateSlotTransition(
                _communityServerWasFull,
                _communityServerSlotAlertEnabled,
                status.Online,
                status.Players,
                status.Capacity);
            _lastCommunityServerStatus = status;
            _communityServerStatusError = string.Empty;
            if (status.Online)
            {
                _communityServerWasFull = decision.IsFull;
            }
            if (decision.Alert)
            {
                slotAlertTriggered = true;
                await AnnounceCommunitySlotOpenAsync(status, decision.OpenSlots);
            }
            UpdateServerStatusPresentation();
            UpdateOfficialPatchPresentation();
            UpdateCommunityServerWatchPresentation();
            await AnnouncePatchReviewIfNeededAsync();
            if (userInitiated && !slotAlertTriggered)
            {
                await ShowHotkeyToastAsync(
                    status.Online
                        ? $"PUBLIC SERVER {status.Players}/{status.Capacity}"
                        : "PUBLIC SERVER REPORTED OFFLINE",
                    status.Online);
            }
        }
        catch (OperationCanceledException)
        {
            // Closing the overlay cancels an in-flight public status request.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            _communityServerStatusError = "The public status source did not answer.";
            UpdateServerStatusPresentation();
            UpdateOfficialPatchPresentation();
            UpdateCommunityServerWatchPresentation();
            if (userInitiated)
            {
                await ShowHotkeyToastAsync("SERVER STATUS UNAVAILABLE", false);
            }
        }
        finally
        {
            _serverStatusRefreshInFlight = false;
            if (IsLoaded)
            {
                RefreshServerStatusButton.IsEnabled = true;
                CommunityServerRefreshButton.IsEnabled = CommunityServerAddressValid;
                UpdateCommunityServerWatchPresentation();
            }
        }
    }

    private async Task AnnounceCommunitySlotOpenAsync(
        IsleServerStatus status,
        int openSlots)
    {
        AddTacticalEvent(
            "SERVER",
            "Public server slot opened",
            $"{ServerSessionLogic.DisplayName(_serverSessionProfileId, _serverSessionName)} · " +
            $"{openSlots} slot{(openSlots == 1 ? string.Empty : "s")} open");
        SystemSounds.Exclamation.Play();
        await ShowHotkeyToastAsync(
            $"SLOT OPEN · {status.Players}/{status.Capacity}",
            true);
    }

    private void RecordServerPopulationSample(IsleServerStatus status)
    {
        var sample = new ServerPopulationSample(status.RetrievedAt, Math.Max(0, status.Players));
        if (_serverPopulationSamples.Count > 0
            && sample.CapturedAt - _serverPopulationSamples[^1].CapturedAt < TimeSpan.FromSeconds(45))
        {
            _serverPopulationSamples[^1] = sample;
        }
        else
        {
            _serverPopulationSamples.Add(sample);
        }

        var oldestAllowed = sample.CapturedAt - TimeSpan.FromHours(2);
        _serverPopulationSamples.RemoveAll(candidate => candidate.CapturedAt < oldestAllowed);
        if (_serverPopulationSamples.Count > 120)
        {
            _serverPopulationSamples.RemoveRange(0, _serverPopulationSamples.Count - 120);
        }
    }

    private string BuildServerPopulationTrend()
    {
        if (_serverPopulationSamples.Count == 0)
        {
            return "TREND COLLECTING · FIRST SAMPLE PENDING";
        }

        var latest = _serverPopulationSamples[^1];
        if (_serverPopulationSamples.Count == 1)
        {
            return $"TREND COLLECTING · {latest.Players} NOW · NEXT SAMPLE ABOUT 1M";
        }

        var earliest = _serverPopulationSamples[0];
        var delta = latest.Players - earliest.Players;
        var direction = delta >= 3 ? "RISING" : delta <= -3 ? "FALLING" : "STEADY";
        var signedDelta = delta > 0 ? $"+{delta}" : delta.ToString(CultureInfo.InvariantCulture);
        var elapsed = latest.CapturedAt - earliest.CapturedAt;
        var elapsedText = elapsed.TotalHours >= 1
            ? $"{elapsed.TotalHours:0.#}H"
            : elapsed.TotalMinutes >= 1
                ? $"{Math.Max(1, (int)Math.Round(elapsed.TotalMinutes)):0}M"
                : "<1M";
        var minimum = _serverPopulationSamples.Min(sample => sample.Players);
        var maximum = _serverPopulationSamples.Max(sample => sample.Players);
        return $"{direction} · {signedDelta} / {elapsedText} · {minimum}-{maximum} SESSION RANGE";
    }

    private void UpdateServerStatusPresentation()
    {
        UpdateTacticalBrief();
        if (ServerStatusText is null || ServerDetailTitleText is null)
        {
            return;
        }

        if (!LiveMapServicesActive)
        {
            var sessionBrush = (Brush)FindResource("AccentBrush");
            var sessionLabel = ServerSessionLogic.HeaderLabel(
                _serverSessionProfileId, _serverSessionName);
            var communityStatus = CommunitySessionActive ? _lastCommunityServerStatus : null;
            if (communityStatus is not null)
            {
                var communitySourceAge = communityStatus.SourceUpdatedAt is null
                    ? (TimeSpan?)null
                    : DateTimeOffset.Now - communityStatus.SourceUpdatedAt.Value;
                var communityStale = !string.IsNullOrEmpty(_communityServerStatusError)
                                     || communitySourceAge is { TotalMinutes: > 30 }
                                     || DateTimeOffset.Now - communityStatus.RetrievedAt > TimeSpan.FromMinutes(3);
                var full = communityStatus.Online
                           && communityStatus.Players >= communityStatus.Capacity;
                sessionBrush = !communityStatus.Online
                    ? new SolidColorBrush(Color.FromRgb(248, 113, 113))
                    : communityStale || full
                        ? (Brush)FindResource("WarningBrush")
                        : (Brush)FindResource("SuccessBrush");
                ServerStatusText.Text = !communityStatus.Online
                    ? "SERVER OFFLINE"
                    : full
                        ? $"FULL {communityStatus.Players}/{communityStatus.Capacity}"
                        : $"OPEN {communityStatus.Players}/{communityStatus.Capacity}";
                ServerStatusText.ToolTip =
                    $"{ServerSessionLogic.DisplayName(_serverSessionProfileId, _serverSessionName)} · " +
                    $"public listing {communityStatus.Players}/{communityStatus.Capacity} · " +
                    "no live positions";
            }
            else
            {
                ServerStatusText.Text = sessionLabel;
                ServerStatusText.ToolTip =
                    $"{ServerSessionLogic.DisplayName(_serverSessionProfileId, _serverSessionName)} session · " +
                    "universal tools active · server-fed live services unavailable";
            }
            ServerStatusDot.Fill = sessionBrush;
            ServerStatusDot.ToolTip = ServerStatusText.ToolTip;
            ServerStatusCard.Visibility = Visibility.Collapsed;
            UpdateCommunityServerWatchPresentation();
            return;
        }

        ServerStatusCard.Visibility = Visibility.Collapsed;
        ServerStatusText.Text = "LOCAL MAP";
        ServerStatusText.ToolTip =
            "Isley's bundled map is independent of every server operator. " +
            "Live positions and vitals require opt-in coordinate capture or an Isley-compatible provider.";
        ServerStatusDot.Fill = (Brush)FindResource("SuccessBrush");
        ServerStatusDot.ToolTip = ServerStatusText.ToolTip;
        return;

#pragma warning disable CS0162
        var neutralBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
        var warningBrush = (Brush)FindResource("WarningBrush");
        var successBrush = (Brush)FindResource("SuccessBrush");
        var offlineBrush = new SolidColorBrush(Color.FromRgb(248, 113, 113));
        var now = DateTimeOffset.Now;
        var status = _lastServerStatus;
        if (status is null)
        {
            var checking = _serverStatusRefreshInFlight;
            ServerStatusText.Text = checking ? "SERVER ..." : "SERVER UNKNOWN";
        ServerDetailTitleText.Text = checking ? "CHECKING PUBLIC SERVER" : "STATUS UNAVAILABLE";
            ServerDetailPopulationText.Text = checking
                ? "Fetching public server status..."
                : "No public snapshot is available";
            ServerPopulationTrendText.Text = BuildServerPopulationTrend();
            ServerDetailMetaText.Text = "Isley bundled map · local";
            ServerDetailFreshnessText.Text = checking
                ? "Public snapshot · waiting for first check"
                : "Refresh or open the source · no game data is read";
            ServerPopulationFill.Width = 0;
            ServerStatusDot.Fill = checking ? warningBrush : neutralBrush;
            ServerDetailStatusDot.Fill = ServerStatusDot.Fill;
            var tooltip = checking
            ? "Checking the optional public server listing."
            : "Optional public server status is unavailable. Live Map mode can still work.";
            ServerStatusText.ToolTip = tooltip;
            ServerStatusDot.ToolTip = tooltip;
            return;
        }

        var sourceAge = status.SourceUpdatedAt is null
            ? (TimeSpan?)null
            : now - status.SourceUpdatedAt.Value;
        var checkAge = now - status.RetrievedAt;
        var stale = !string.IsNullOrEmpty(_serverStatusError)
                    || sourceAge is { TotalMinutes: > 30 }
                    || checkAge.TotalMinutes > 3;
        var statusBrush = !status.Online
            ? offlineBrush
            : stale
                ? warningBrush
                : successBrush;
        var occupancyPercent = status.Occupancy * 100;

        ServerStatusText.Text = status.Online
            ? $"SERVER {status.Players}/{status.Capacity}"
            : "SERVER OFFLINE";
        ServerStatusDot.Fill = statusBrush;
        ServerDetailStatusDot.Fill = statusBrush;
        ServerDetailTitleText.Text = status.Online
            ? stale ? "PUBLIC SERVER · SNAPSHOT AGED" : "PUBLIC SERVER · ONLINE"
            : "PUBLIC SERVER · REPORTED OFFLINE";
        ServerDetailPopulationText.Text =
            $"{status.Players} / {status.Capacity} players · {occupancyPercent:0}% full";
        ServerPopulationFill.Width = 152 * status.Occupancy;
        ServerPopulationFill.Background = statusBrush;
        ServerPopulationTrendText.Text = BuildServerPopulationTrend();
        ServerPopulationTrendText.ToolTip =
            $"Session-only trend from {_serverPopulationSamples.Count} existing public status " +
            $"sample{(_serverPopulationSamples.Count == 1 ? string.Empty : "s")}; no additional requests";
        ServerDetailMetaText.Text =
            $"{status.Map} · {status.Version}\n{status.ConnectAddress}";

        var reported = sourceAge is null
            ? "Provider time unavailable"
            : $"Reported {FormatStatusAge(sourceAge.Value)} ago";
        var checkedText = $"checked {FormatStatusAge(checkAge)} ago";
        ServerDetailFreshnessText.Text = string.IsNullOrEmpty(_serverStatusError)
            ? $"{reported} · {checkedText}\nPublic GameMonitoring snapshot"
            : $"{reported} · last refresh failed\nShowing the last good public snapshot";

        var tooltipText =
            $"Optional public server: {(status.Online ? "online" : "reported offline")}\n" +
            $"{status.Players}/{status.Capacity} players · {status.Map}\n" +
            $"{reported} from GameMonitoring";
        ServerStatusText.ToolTip = tooltipText;
        ServerStatusDot.ToolTip = tooltipText;
#pragma warning restore CS0162
    }

    private static string FormatStatusAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
        {
            return "just now";
        }
        if (age.TotalMinutes < 1)
        {
            return "under 1m";
        }
        if (age.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)age.TotalMinutes)}m";
        }
        if (age.TotalDays < 1)
        {
            return $"{Math.Max(1, (int)age.TotalHours)}h";
        }
        return $"{Math.Max(1, (int)age.TotalDays)}d";
    }

    private async void RefreshServerStatusButton_Click(object sender, RoutedEventArgs e) =>
        await RefreshServerStatusAsync(userInitiated: true);

    private async void CopyServerAddressButton_Click(object sender, RoutedEventArgs e) =>
        await CopyServerAddressAsync();

    private void OpenServerStatusSourceButton_Click(object sender, RoutedEventArgs e) =>
        OpenExternalUri(IsleServerStatusClient.PublicStatusSourcePage);

    private async void PatchWatchRefreshButton_Click(object sender, RoutedEventArgs e) =>
        await RefreshOfficialPatchAsync(userInitiated: true);

    private void PatchWatchNotesButton_Click(object sender, RoutedEventArgs e) =>
        OpenExternalUri(_lastOfficialPatch?.NotesUrl ?? OfficialPatchWatchClient.NewsSourcePage);

    private async void PatchWatchImpactCopyButton_Click(object sender, RoutedEventArgs e)
    {
        var impact = PatchWatchLogic.BuildImpact(
            CurrentPatchWatchGuidance(),
            _lastOfficialPatch?.NotesUrl);
        if (!impact.Visible || string.IsNullOrWhiteSpace(impact.CopyText))
        {
            await ShowHotkeyToastAsync("ISLEY GUIDES MATCH THE DOCUMENTED PATCH", true);
            return;
        }

        try
        {
            Clipboard.SetText(impact.CopyText);
            await ShowHotkeyToastAsync("VERSION REVIEW CHECKLIST COPIED", true);
        }
        catch
        {
            await ShowHotkeyToastAsync("COULD NOT COPY VERSION CHECKLIST", false);
        }
    }

    private async Task CopyServerAddressAsync()
    {
        try
        {
            if (!CommunityServerWatchLogic.TryNormalizeAddress(_communityServerAddress, out var address))
            {
                await ShowHotkeyToastAsync("NO SERVER ADDRESS CONFIGURED", false);
                return;
            }
            Clipboard.SetText(address);
            await ShowHotkeyToastAsync("SERVER ADDRESS COPIED", true);
        }
        catch
        {
            await ShowHotkeyToastAsync("COULD NOT COPY SERVER ADDRESS", false);
        }
    }
}
