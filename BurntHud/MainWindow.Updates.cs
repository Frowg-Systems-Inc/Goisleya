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
    private async Task CheckForIsleyUpdateAfterStartupAsync()
    {
        await Task.Delay(4000);
        if (IsLoaded && _automaticUpdatesEnabled)
        {
            await RefreshIsleyUpdateAsync(userRequested: false);
        }
    }

    private async Task RefreshIsleyUpdateAsync(bool userRequested)
    {
        if (_isleyUpdateDownloading)
        {
            if (userRequested)
            {
                await ShowHotkeyToastAsync("ISLEY UPDATE IS DOWNLOADING", true);
            }
            return;
        }

        if (_isleyUpdateRefreshInFlight)
        {
            if (userRequested)
            {
                await ShowHotkeyToastAsync("UPDATE CHECK ALREADY RUNNING", true);
            }
            return;
        }

        _isleyUpdateRefreshInFlight = true;
        _isleyUpdateStatus = "CHECKING TRUSTED RELEASE CHANNEL";
        UpdateIsleyUpdatePresentation();
        _isleyUpdateCancellation?.Cancel();
        _isleyUpdateCancellation?.Dispose();
        _isleyUpdateCancellation = new CancellationTokenSource();

        try
        {
            var release = await IsleyUpdateClient.FetchReleaseAsync(
                _isleyUpdateCancellation.Token);
            if (IsleyReleaseLogic.IsNewer(CurrentIsleyVersion, release.Version))
            {
                _availableIsleyRelease = release;
                _isleyUpdateStatus =
                    $"v{release.VersionText} READY · VERIFIED DOWNLOAD";
                if (userRequested)
                {
                    _isleyUpdateSnoozedUntil = DateTimeOffset.MinValue;
                }

                if (!string.Equals(
                        _isleyUpdateAnnouncedVersion,
                        release.VersionText,
                        StringComparison.Ordinal))
                {
                    _isleyUpdateAnnouncedVersion = release.VersionText;
                    try { SystemSounds.Asterisk.Play(); } catch { }
                    _ = ShowHotkeyToastAsync(
                        $"ISLEY v{release.VersionText} READY · TOOLS → MORE → UPDATE",
                        true);
                }
            }
            else
            {
                _availableIsleyRelease = null;
                _isleyUpdateStatus =
                    $"v{IsleyReleaseLogic.DisplayVersion(CurrentIsleyVersion)} · UP TO DATE";
                if (userRequested)
                {
                    await ShowHotkeyToastAsync("ISLEY IS UP TO DATE", true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _isleyUpdateStatus = "UPDATE CHECK CANCELED";
        }
        catch
        {
            _isleyUpdateStatus = "UPDATE CHANNEL TEMPORARILY UNAVAILABLE";
            if (userRequested)
            {
                await ShowHotkeyToastAsync("UPDATE CHECK UNAVAILABLE · TRY AGAIN", false);
            }
        }
        finally
        {
            _isleyUpdateRefreshInFlight = false;
            UpdateIsleyUpdatePresentation();
        }
    }

    private void UpdateIsleyUpdatePresentation()
    {
        if (AutomaticUpdatesButton is null
            || CheckForIsleyUpdateButton is null
            || IsleyUpdateStatusText is null
            || IsleyUpdatePromptBorder is null)
        {
            return;
        }

        var currentVersion = IsleyReleaseLogic.DisplayVersion(CurrentIsleyVersion);
        AutomaticUpdatesButton.Content =
            $"Automatic updates · {(_automaticUpdatesEnabled ? "On" : "Off")}";
        SetToggleButtonState(AutomaticUpdatesButton, _automaticUpdatesEnabled);
        if (PreferBetaUpdatesButton is not null)
        {
            PreferBetaUpdatesButton.Content =
                $"Beta channel · {(_preferBetaUpdates ? "On" : "Off")}";
            SetToggleButtonState(PreferBetaUpdatesButton, _preferBetaUpdates);
        }
        if (WhatsNewStatusText is not null)
        {
            var version = IsleyReleaseLogic.DisplayVersion(CurrentIsleyVersion);
            WhatsNewStatusText.Text = WhatsNewLogic.ShouldHighlight(_whatsNewVersionSeen, version)
                ? $"New in v{version} · open What's new"
                : $"v{version} notes available · open What's new";
        }
        CheckForIsleyUpdateButton.IsEnabled =
            !_isleyUpdateRefreshInFlight && !_isleyUpdateDownloading;
        CheckForIsleyUpdateButton.Content = _isleyUpdateRefreshInFlight
            ? "Checking for updates…"
            : "Check for updates";

        if (string.IsNullOrWhiteSpace(_isleyUpdateStatus))
        {
            _isleyUpdateStatus = _automaticUpdatesEnabled
                ? $"v{currentVersion} · Automatic checks on · You choose when to restart"
                : $"v{currentVersion} · Automatic checks off · Manual check available";
        }
        IsleyUpdateStatusText.Text = _isleyUpdateStatus;
        IsleyUpdateStatusText.Foreground = _availableIsleyRelease is null
            ? (Brush)FindResource("SecondaryTextBrush")
            : (Brush)FindResource("BrandRedGlowBrush");

        var showPrompt = _availableIsleyRelease is not null
                         && DateTimeOffset.UtcNow >= _isleyUpdateSnoozedUntil;
        IsleyUpdatePromptBorder.Visibility = showPrompt
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_availableIsleyRelease is not { } release)
        {
            return;
        }

        IsleyUpdatePromptVersionText.Text = $"v{release.VersionText}";
        var lockDetail = _overlayLocked
            ? " Unlock Isley to choose an action."
            : string.Empty;
        IsleyUpdatePromptDetailText.Text = _isleyUpdateDownloading
            ? _isleyUpdateStatus
            : $"{release.Notes} The download is verified before installation.{lockDetail}";
        IsleyUpdateNowButton.IsEnabled = !_isleyUpdateDownloading;
        IsleyUpdateLaterButton.IsEnabled = !_isleyUpdateDownloading;
        if (!_isleyUpdateDownloading)
        {
            IsleyUpdateNowButton.Content = "UPDATE & RESTART";
        }
    }

    private async void AutomaticUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        _automaticUpdatesEnabled = !_automaticUpdatesEnabled;
        if (_automaticUpdatesEnabled)
        {
            _isleyUpdateTimer.Start();
            _isleyUpdateStatus = "AUTOMATIC CHECKS ON · CHECKING NOW";
            _ = RefreshIsleyUpdateAsync(userRequested: false);
        }
        else
        {
            _isleyUpdateTimer.Stop();
            _isleyUpdateStatus =
                $"v{IsleyReleaseLogic.DisplayVersion(CurrentIsleyVersion)} · " +
                "Automatic checks off · Manual check available";
        }
        UpdateIsleyUpdatePresentation();
        SaveSettings();
        await ShowHotkeyToastAsync(
            _automaticUpdatesEnabled
                ? "AUTOMATIC ISLEY UPDATE CHECKS ON"
                : "AUTOMATIC ISLEY UPDATE CHECKS OFF",
            true);
    }

    private async void CheckForIsleyUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        _isleyUpdateSnoozedUntil = DateTimeOffset.MinValue;
        await RefreshIsleyUpdateAsync(userRequested: true);
    }

    private async void PreferBetaUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        _preferBetaUpdates = !_preferBetaUpdates;
        // Stable channel remains the only trusted fetch path until a beta manifest ships.
        UpdateIsleyUpdatePresentation();
        SaveSettings();
        await ShowHotkeyToastAsync(
            _preferBetaUpdates
                ? "BETA CHANNEL PREFERENCE ON · STABLE STILL USED UNTIL BETA PUBLISHES"
                : "BETA CHANNEL PREFERENCE OFF · STABLE ONLY",
            true);
    }

    private async void WhatsNewButton_Click(object sender, RoutedEventArgs e)
    {
        var version = IsleyReleaseLogic.DisplayVersion(CurrentIsleyVersion);
        string? json = null;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "whats-new.json");
            if (File.Exists(path))
            {
                json = await File.ReadAllTextAsync(path);
            }
        }
        catch (IOException)
        {
            json = null;
        }

        var presentation = WhatsNewLogic.FromJson(json, version);
        _whatsNewVersionSeen = presentation.Version;
        if (WhatsNewStatusText is not null)
        {
            WhatsNewStatusText.Text = presentation.Body.Length > 220
                ? presentation.Body[..220].TrimEnd() + "…"
                : presentation.Body;
        }
        UpdateIsleyUpdatePresentation();
        SaveSettings();
        MessageBox.Show(
            presentation.Body,
            presentation.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        await ShowHotkeyToastAsync($"WHAT'S NEW · v{presentation.Version}", true);
    }

    private async void ExportPortableConfigButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SyncCurrentCommunityServerProfile();
            var allowlisted = new
            {
                HudDetailModeIndex = _hudDetailModeIndex,
                ActiveFocusModeId = _activeFocusModeId,
                HotkeyBindings = HotkeyBindingLogic.ToSettings(_hotkeyBindings.Values),
                CommunityServerProfiles = _communityServerProfiles.Select(profile =>
                    new
                    {
                        profile.Id,
                        profile.Name,
                        profile.Address,
                        profile.WatchEnabled,
                        profile.SlotAlertEnabled,
                        profile.GrowthMultiplierIndex,
                        IsleyJoinLink = SanitizeCommunityIsleyJoinLink(profile.IsleyJoinLink)
                    }).ToList(),
                PreferBetaUpdates = _preferBetaUpdates,
                AutomaticUpdatesEnabled = _automaticUpdatesEnabled,
                VoiceHudVisible = _voiceHudVisible,
                VoiceNatAssist = _voiceNatAssist,
                VoiceProximityEnabled = _voiceProximityEnabled,
                VoiceRangeIndex = _voiceRangeIndex
            };
            var exported = PortableConfigLogic.Export(allowlisted);
            Clipboard.SetText(exported);
            if (PortableConfigStatusText is not null)
            {
                PortableConfigStatusText.Text = "Portable prefs copied · secrets excluded";
            }
            await ShowHotkeyToastAsync("PORTABLE PREFS COPIED", true);
        }
        catch (Exception)
        {
            if (PortableConfigStatusText is not null)
            {
                PortableConfigStatusText.Text = "Clipboard unavailable · try again";
            }
            await ShowHotkeyToastAsync("CLIPBOARD UNAVAILABLE", false);
        }
    }

    private async void ImportPortableConfigButton_Click(object sender, RoutedEventArgs e)
    {
        string clipboard;
        try
        {
            clipboard = Clipboard.GetText() ?? string.Empty;
        }
        catch (Exception)
        {
            await ShowHotkeyToastAsync("CLIPBOARD UNAVAILABLE", false);
            return;
        }

        if (!PortableConfigLogic.TryParse(clipboard, out var settings, out _))
        {
            if (PortableConfigStatusText is not null)
            {
                PortableConfigStatusText.Text = "Invalid portable prefs · nothing imported";
            }
            await ShowHotkeyToastAsync("INVALID PORTABLE PREFS", false);
            return;
        }

        var preview = PortableConfigLogic.PreviewSummary(settings);
        var confirm = MessageBox.Show(
            preview +
            Environment.NewLine +
            Environment.NewLine +
            "Import these allowlisted prefs? Steam tokens and TURN secrets are never included.",
            "Import portable prefs",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        List<HotkeyBindingSettings>? restoredHotkeys = null;
        List<CommunityServerProfileSettings>? restoredProfiles = null;
        string? focusModeId = null;
        try
        {
            // Deserialize fallible payloads before mutating live state.
            if (settings.TryGetProperty("HotkeyBindings", out var hotkeys)
                && hotkeys.ValueKind == JsonValueKind.Array)
            {
                restoredHotkeys = JsonSerializer.Deserialize<List<HotkeyBindingSettings>>(
                                     hotkeys.GetRawText())
                                 ?? [];
            }
            if (settings.TryGetProperty("CommunityServerProfiles", out var profiles)
                && profiles.ValueKind == JsonValueKind.Array)
            {
                restoredProfiles = JsonSerializer.Deserialize<List<CommunityServerProfileSettings>>(
                                       profiles.GetRawText())
                                   ?? [];
            }
            if (settings.TryGetProperty("ActiveFocusModeId", out var focus)
                && focus.ValueKind == JsonValueKind.String
                && GetFocusModeDefinition(focus.GetString() ?? string.Empty) is { } definition)
            {
                focusModeId = definition.Id;
            }

            if (settings.TryGetProperty("HudDetailModeIndex", out var hud)
                && hud.TryGetInt32(out var hudIndex))
            {
                _hudDetailModeIndex = Math.Clamp(hudIndex, 0, _hudDetailModeLabels.Length - 1);
            }
            if (settings.TryGetProperty("PreferBetaUpdates", out var beta)
                && (beta.ValueKind is JsonValueKind.True or JsonValueKind.False))
            {
                _preferBetaUpdates = beta.GetBoolean();
            }
            if (settings.TryGetProperty("AutomaticUpdatesEnabled", out var auto)
                && (auto.ValueKind is JsonValueKind.True or JsonValueKind.False))
            {
                _automaticUpdatesEnabled = auto.GetBoolean();
            }
            if (settings.TryGetProperty("VoiceHudVisible", out var voiceHud)
                && (voiceHud.ValueKind is JsonValueKind.True or JsonValueKind.False))
            {
                _voiceHudVisible = voiceHud.GetBoolean();
            }
            if (settings.TryGetProperty("VoiceNatAssist", out var nat)
                && (nat.ValueKind is JsonValueKind.True or JsonValueKind.False))
            {
                _voiceNatAssist = nat.GetBoolean();
            }
            if (settings.TryGetProperty("VoiceProximityEnabled", out var prox)
                && (prox.ValueKind is JsonValueKind.True or JsonValueKind.False))
            {
                _voiceProximityEnabled = prox.GetBoolean();
            }
            if (settings.TryGetProperty("VoiceRangeIndex", out var range)
                && range.TryGetInt32(out var rangeIndex))
            {
                _voiceRangeIndex = VoiceIntegrationLogic.NormalizeRangeIndex(rangeIndex);
            }
            if (restoredHotkeys is not null)
            {
                RestoreHotkeyBindings(restoredHotkeys);
            }
            if (restoredProfiles is not null)
            {
                RestoreCommunityServerProfiles(
                    restoredProfiles,
                    _selectedCommunityServerProfileId);
            }
            if (!string.IsNullOrEmpty(focusModeId))
            {
                await ApplyFocusModeAsync(focusModeId);
            }

            UpdateIsleyUpdatePresentation();
            UpdateVoicePresentation();
            UpdateFocusModeControls();
            UpdateHotkeyStatus();
            UpdateCommunityServerWatchPresentation();
            UpdateIsleyRelayPresentation();
            SaveSettings();
            if (PortableConfigStatusText is not null)
            {
                PortableConfigStatusText.Text = preview + " · imported";
            }
            await ShowHotkeyToastAsync("PORTABLE PREFS IMPORTED", true);
        }
        catch (Exception ex)
        {
            // Roll back in-memory mutations from the last saved settings file.
            LoadSettings();
            UpdateIsleyUpdatePresentation();
            UpdateVoicePresentation();
            UpdateFocusModeControls();
            UpdateHotkeyStatus();
            UpdateCommunityServerWatchPresentation();
            UpdateIsleyRelayPresentation();
            if (PortableConfigStatusText is not null)
            {
                PortableConfigStatusText.Text = "Import failed · previous prefs restored";
            }
            await ShowHotkeyToastAsync($"IMPORT FAILED · {ex.GetType().Name}", false);
        }
    }

    private void IsleyUpdateLaterButton_Click(object sender, RoutedEventArgs e)
    {
        _isleyUpdateSnoozedUntil = DateTimeOffset.UtcNow.AddHours(6);
        UpdateIsleyUpdatePresentation();
        _ = ShowHotkeyToastAsync("UPDATE REMINDER SNOOZED · 6 HOURS", true);
    }

    private async void IsleyUpdateNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_availableIsleyRelease is not { } release || _isleyUpdateDownloading)
        {
            return;
        }

        var installDirectory = AppContext.BaseDirectory;
        if (!IsleyUpdateClient.CanWriteInstallDirectory(installDirectory))
        {
            OpenExternalUri(IsleyReleaseLogic.StableDownloadUrl);
            await ShowHotkeyToastAsync(
                "FOLDER IS READ-ONLY · DOWNLOAD OPENED IN BROWSER",
                false);
            return;
        }

        _isleyUpdateDownloading = true;
        _isleyUpdateCancellation?.Cancel();
        _isleyUpdateCancellation?.Dispose();
        _isleyUpdateCancellation = new CancellationTokenSource();
        _isleyUpdateStatus = $"DOWNLOADING ISLEY v{release.VersionText} · 0%";
        UpdateIsleyUpdatePresentation();
        var progress = new Progress<int>(percent =>
        {
            _isleyUpdateStatus =
                $"DOWNLOADING ISLEY v{release.VersionText} · {percent}%";
            IsleyUpdateNowButton.Content = $"{percent}%";
            UpdateIsleyUpdatePresentation();
        });

        try
        {
            var staged = await IsleyUpdateClient.StageAsync(
                release,
                progress,
                _isleyUpdateCancellation.Token);
            _isleyUpdateStatus = "VERIFIED · RESTARTING ISLEY";
            IsleyUpdateNowButton.Content = "RESTARTING…";
            UpdateIsleyUpdatePresentation();
            SaveSettings();
            using var updater = IsleyUpdateClient.LaunchUpdater(
                staged,
                Environment.ProcessId,
                installDirectory);
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            _isleyUpdateStatus = "UPDATE DOWNLOAD CANCELED · READY TO RETRY";
            _isleyUpdateDownloading = false;
            UpdateIsleyUpdatePresentation();
        }
        catch
        {
            _isleyUpdateStatus = "UPDATE COULD NOT FINISH · READY TO RETRY";
            _isleyUpdateDownloading = false;
            UpdateIsleyUpdatePresentation();
            await ShowHotkeyToastAsync(
                "UPDATE COULD NOT FINISH · NOTHING WAS INSTALLED",
                false);
        }
    }

    private void ConsumeUpdaterResult()
    {
        var resultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Isley",
            "Updater",
            "last-result.json");
        if (!File.Exists(resultPath))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(resultPath));
            var root = document.RootElement;
            var success = root.TryGetProperty("success", out var successValue)
                          && successValue.ValueKind == JsonValueKind.True;
            var version = root.TryGetProperty("version", out var versionValue)
                          && versionValue.ValueKind == JsonValueKind.String
                ? versionValue.GetString() ?? string.Empty
                : string.Empty;
            if (!Regex.IsMatch(version, @"^\d{1,4}\.\d{1,4}\.\d{1,6}$"))
            {
                version = string.Empty;
            }
            _ = ShowHotkeyToastAsync(
                success
                    ? version.Length > 0
                        ? $"ISLEY UPDATED TO v{version}"
                        : "ISLEY UPDATED"
                    : "ISLEY UPDATE DID NOT FINISH · TRY AGAIN",
                success);
        }
        catch
        {
            // A malformed diagnostic result must never affect normal startup.
        }
        finally
        {
            try { File.Delete(resultPath); } catch { }
        }
    }
}
