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
    private bool _diagnosticsExportInFlight;

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
            var fetch = await IsleyUpdateClient.FetchReleaseAsync(
                _preferBetaUpdates,
                _isleyUpdateCancellation.Token);
            var release = fetch.Release;
            var channelTag = string.Equals(
                release.Channel,
                IsleyReleaseLogic.BetaChannel,
                StringComparison.Ordinal)
                ? " BETA"
                : string.Empty;
            if (IsleyReleaseLogic.IsNewer(CurrentIsleyVersion, release.Version))
            {
                _availableIsleyRelease = release;
                _isleyUpdateStatus =
                    $"v{release.VersionText}{channelTag} READY · VERIFIED DOWNLOAD";
                if (fetch.BetaFallback)
                {
                    _isleyUpdateStatus += " · BETA CHANNEL UNAVAILABLE · SHOWING STABLE";
                }
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
                        $"ISLEY v{release.VersionText}{channelTag} READY · TOOLS → MORE → UPDATE",
                        true);
                }
            }
            else
            {
                _availableIsleyRelease = null;
                _isleyUpdateStatus =
                    $"v{IsleyReleaseLogic.DisplayVersion(CurrentIsleyVersion)} · UP TO DATE";
                if (fetch.BetaFallback)
                {
                    _isleyUpdateStatus += " · BETA CHANNEL UNAVAILABLE · STABLE SHOWN";
                }
                if (userRequested)
                {
                    await ShowHotkeyToastAsync(
                        fetch.BetaFallback
                            ? "ISLEY IS UP TO DATE · BETA CHANNEL UNAVAILABLE"
                            : "ISLEY IS UP TO DATE",
                        true);
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
        // The beta toggle fetches the pinned beta manifest; if none is published
        // the client falls back to stable and says so in the status line.
        UpdateIsleyUpdatePresentation();
        SaveSettings();
        await ShowHotkeyToastAsync(
            _preferBetaUpdates
                ? "BETA CHANNEL ON · BETA RELEASES PREFERRED WHEN PUBLISHED"
                : "BETA CHANNEL OFF · STABLE ONLY",
            true);
        _ = RefreshIsleyUpdateAsync(userRequested: false);
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
            var exported = PortableConfigLogic.Export(BuildPortableAllowlistedSettings());
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
                CurrentIsleyVersion,
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

        string? pendingBootVersion = null;
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
            if (!IsleyReleaseLogic.IsValidVersionText(version))
            {
                version = string.Empty;
            }
            if (success && version.Length > 0)
            {
                var markerPath = ResolveBootOkMarkerPath();
                if (IsleyUpdateClient.TryReadBootOkMarker(markerPath, out var confirmedVersion)
                    && string.Equals(confirmedVersion, version, StringComparison.Ordinal))
                {
                    _ = ShowHotkeyToastAsync(
                        $"ISLEY UPDATED TO v{version} · BOOT CONFIRMED",
                        true);
                    return;
                }

                // Defer the announcement until the app proves a healthy boot;
                // the result file stays until ConfirmUpdatedBootAsync finishes
                // so a crash during first boot keeps the pending state honest.
                pendingBootVersion = version;
            }
            else
            {
                _ = ShowHotkeyToastAsync(
                    success
                        ? "ISLEY UPDATED"
                        : "ISLEY UPDATE DID NOT FINISH · TRY AGAIN",
                    success);
            }
        }
        catch
        {
            // A malformed diagnostic result must never affect normal startup.
        }
        finally
        {
            if (pendingBootVersion is null)
            {
                try { File.Delete(resultPath); } catch { }
            }
        }

        if (pendingBootVersion is not null)
        {
            _ = ConfirmUpdatedBootAsync(pendingBootVersion, resultPath);
        }
    }

    private async Task ConfirmUpdatedBootAsync(string version, string resultPath)
    {
        try
        {
            // Healthy steady state: the window finished loading and the
            // survival/vitals tick is running (it starts right after the first
            // forced UpdateCoreVitals pass in MainWindow_Loaded).
            await Task.Delay(TimeSpan.FromSeconds(4));
            if (!IsLoaded || !_survivalTimerTick.IsEnabled)
            {
                _isleyUpdateStatus =
                    $"UPDATED TO v{version} · BOOT NOT CONFIRMED · WATCH FOR ISSUES";
                UpdateIsleyUpdatePresentation();
                await ShowHotkeyToastAsync(
                    $"ISLEY UPDATED TO v{version} · BOOT NOT CONFIRMED",
                    false);
                return;
            }

            IsleyUpdateClient.WriteBootOkMarker(ResolveBootOkMarkerPath(), version);
            _isleyUpdateStatus = $"UPDATED TO v{version} · BOOT CONFIRMED";
            UpdateIsleyUpdatePresentation();
            await ShowHotkeyToastAsync(
                $"ISLEY UPDATED TO v{version} · BOOT CONFIRMED",
                true);
        }
        catch
        {
            // Boot confirmation is diagnostic; it must never break startup.
            // Announce the update plainly, without claiming a confirmed boot.
            try
            {
                await ShowHotkeyToastAsync($"ISLEY UPDATED TO v{version}", true);
            }
            catch
            {
            }
        }
        finally
        {
            try { File.Delete(resultPath); } catch { }
        }
    }

    private static string ResolveBootOkMarkerPath() =>
        PortableModeEnabled
            ? Path.Combine(AppContext.BaseDirectory, "IsleyData", "last-boot-ok.json")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Isley",
                "last-boot-ok.json");

    private object BuildPortableAllowlistedSettings() =>
        new
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

    private async void DiagnosticsExportButton_Click(object sender, RoutedEventArgs e) =>
        await ExportDiagnosticsBundleAsync();

    private async Task ExportDiagnosticsBundleAsync()
    {
        if (_diagnosticsExportInFlight)
        {
            return;
        }

        _diagnosticsExportInFlight = true;
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Isley diagnostics bundle",
                Filter = "Isley diagnostics bundle (*.zip)|*.zip",
                FileName = DiagnosticsBundleLogic.SuggestFileName(DateTimeOffset.Now),
                DefaultExt = ".zip",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            SyncCurrentCommunityServerProfile();
            var settingsJson = PortableConfigLogic.Export(BuildPortableAllowlistedSettings());
            var crashDirectory = ResolveCrashLogDirectory();
            var selectedLogs = Directory.Exists(crashDirectory)
                ? DiagnosticsBundleLogic.SelectCrashLogs(
                    new DirectoryInfo(crashDirectory)
                        .GetFiles("crash-*.txt")
                        .Select(file => new CrashLogCandidate(
                            file.Name,
                            file.Length,
                            file.CreationTimeUtc)))
                : (IReadOnlyList<CrashLogCandidate>)[];
            var environmentText = BuildDiagnosticsEnvironmentText(selectedLogs);

            try
            {
                await Task.Run(() => WriteDiagnosticsBundle(
                    dialog.FileName,
                    environmentText,
                    settingsJson,
                    crashDirectory,
                    selectedLogs));
            }
            catch
            {
                if (_diagnosticsStatusText is not null)
                {
                    _diagnosticsStatusText.Text = "Export failed · folder not writable";
                }
                await ShowHotkeyToastAsync("DIAGNOSTICS EXPORT FAILED", false);
                return;
            }

            if (_diagnosticsStatusText is not null)
            {
                _diagnosticsStatusText.Text =
                    $"Saved · {selectedLogs.Count} log{(selectedLogs.Count == 1 ? string.Empty : "s")} · secrets stay out";
            }
            AddTacticalEvent(
                "SYSTEM",
                "Diagnostics bundle exported",
                $"{selectedLogs.Count} crash logs · {selectedLogs.Sum(log => log.SizeBytes)} bytes · redacted settings");
            await ShowHotkeyToastAsync(
                $"DIAGNOSTICS SAVED · {selectedLogs.Count} LOG{(selectedLogs.Count == 1 ? string.Empty : "S")}",
                true);
        }
        finally
        {
            _diagnosticsExportInFlight = false;
        }
    }

    private static string ResolveCrashLogDirectory()
    {
        var portable = Path.Combine(AppContext.BaseDirectory, "IsleyData");
        if (Directory.Exists(portable))
        {
            return Path.Combine(portable, "Logs");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Isley",
            "Logs");
    }

    private string BuildDiagnosticsEnvironmentText(IReadOnlyList<CrashLogCandidate> selectedLogs)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Schema: {DiagnosticsBundleLogic.BundleSchema}");
        builder.AppendLine($"Exported (UTC): {DateTimeOffset.UtcNow:O}");
        builder.AppendLine(
            $"Isley version: {IsleyReleaseLogic.DisplayVersion(CurrentIsleyVersion)} ({CurrentIsleyVersion})");
        builder.AppendLine($"OS: {Environment.OSVersion}");
        builder.AppendLine(
            $"64-bit OS: {Environment.Is64BitOperatingSystem} · 64-bit process: {Environment.Is64BitProcess}");
        builder.AppendLine($"Portable mode: {(PortableModeEnabled ? "yes" : "no")}");
        builder.AppendLine(
            "Preferences storage: " +
            (string.Equals(_activeSettingsPath, PortableSettingsPath, StringComparison.OrdinalIgnoreCase)
                ? "portable"
                : "local app data"));
        builder.AppendLine($"whats-new.json version: {ReadWhatsNewVersion()}");
        builder.AppendLine(
            $"Crash logs included: {selectedLogs.Count} ({selectedLogs.Sum(log => log.SizeBytes)} bytes)");
        builder.Append(
            "No Steam tokens, TURN credentials, relay secrets, or precise coordinates are included.");
        return builder.ToString();
    }

    private static string ReadWhatsNewVersion()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "whats-new.json");
            if (!File.Exists(path))
            {
                return "unavailable";
            }

            var buffer = new byte[DiagnosticsBundleLogic.MaximumWhatsNewBytes];
            int read;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                read = stream.Read(buffer, 0, buffer.Length);
            }
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, read));
            var version = document.RootElement.TryGetProperty("version", out var value)
                          && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(version))
            {
                return "unavailable";
            }

            var cleaned = new string(version.Trim()
                .Where(ch => !char.IsControl(ch))
                .Take(32)
                .ToArray());
            return cleaned.Length == 0 ? "unavailable" : cleaned;
        }
        catch
        {
            return "unavailable";
        }
    }

    private static void WriteDiagnosticsBundle(
        string targetPath,
        string environmentText,
        string settingsJson,
        string crashDirectory,
        IReadOnlyList<CrashLogCandidate> selectedLogs)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException("The chosen folder is not available.");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var archive = new System.IO.Compression.ZipArchive(
                       stream,
                       System.IO.Compression.ZipArchiveMode.Create))
            {
                WriteDiagnosticsEntry(archive, "environment.txt", environmentText);
                WriteDiagnosticsEntry(archive, "settings-redacted.json", settingsJson);
                foreach (var log in selectedLogs)
                {
                    byte[] bytes;
                    try
                    {
                        bytes = File.ReadAllBytes(Path.Combine(crashDirectory, log.FileName));
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }

                    if (bytes.LongLength > DiagnosticsBundleLogic.MaximumSingleLogBytes)
                    {
                        continue;
                    }

                    var entry = archive.CreateEntry(
                        $"logs/{DiagnosticsBundleLogic.SanitizeEntryName(log.FileName)}");
                    using var entryStream = entry.Open();
                    entryStream.Write(bytes, 0, bytes.Length);
                }
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }
    }

    private static void WriteDiagnosticsEntry(
        System.IO.Compression.ZipArchive archive,
        string entryName,
        string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
