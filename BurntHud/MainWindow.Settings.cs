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
    private static IEnumerable<string> GetSettingsCandidatePaths() =>
        new[]
        {
            PrimarySettingsPath,
            PortableSettingsPath,
            LegacyMapperSettingsPath,
            LegacyPortableSettingsPath
        }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private void LoadSettings()
    {
        var settingsPaths = GetSettingsCandidatePaths()
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
        if (settingsPaths.Count == 0)
        {
            _activeSettingsPath = PrimarySettingsPath;
            return;
        }

        MapperSettings? settings = null;
        var failures = new List<string>();
        foreach (var settingsPath in settingsPaths)
        {
            try
            {
                settings = JsonSerializer.Deserialize<MapperSettings>(File.ReadAllText(settingsPath));
                if (settings is null)
                {
                    failures.Add($"{Path.GetFileName(settingsPath)}: empty settings");
                    continue;
                }

                _activeSettingsPath = string.Equals(
                    settingsPath,
                    LegacyMapperSettingsPath,
                    StringComparison.OrdinalIgnoreCase)
                    ? PrimarySettingsPath
                    : string.Equals(
                        settingsPath,
                        LegacyPortableSettingsPath,
                        StringComparison.OrdinalIgnoreCase)
                        ? PortableSettingsPath
                        : settingsPath;
                _settingsPersistenceError = string.Empty;
                break;
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(settingsPath)}: {ex.GetType().Name}");
            }
        }

        if (settings is null)
        {
            _activeSettingsPath = PrimarySettingsPath;
            _settingsPersistenceError = string.Join(" · ", failures);
            return;
        }

        try
        {
            var workArea = SystemParameters.WorkArea;
            Width = Math.Clamp(settings.Width, MinWidth, Math.Max(MinWidth, workArea.Width - 16));
            Height = Math.Clamp(settings.Height, MinHeight, Math.Max(MinHeight, workArea.Height - 16));
            if (double.IsFinite(settings.Left) && double.IsFinite(settings.Top))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                // Clamp to the virtual desktop so multi-monitor placements are
                // preserved, while a disconnected monitor still pulls the
                // overlay back into visible bounds.
                var virtualLeft = SystemParameters.VirtualScreenLeft;
                var virtualTop = SystemParameters.VirtualScreenTop;
                var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
                var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
                Left = Math.Clamp(settings.Left, virtualLeft, Math.Max(virtualLeft, virtualRight - Width));
                Top = Math.Clamp(settings.Top, virtualTop, Math.Max(virtualTop, virtualBottom - Height));
            }

            _opacityIndex = Math.Clamp(settings.OpacityIndex, 0, _opacityLevels.Length - 1);
            _mapLightModeIndex = Math.Clamp(settings.MapLightModeIndex, 0, _mapLightModeOpacities.Length - 1);
            _hudDetailModeIndex = Math.Clamp(settings.HudDetailModeIndex, 0, _hudDetailModeLabels.Length - 1);
            _smartHudEnabled = settings.SmartHudEnabled;
            _liteModeEnabled = settings.LiteModeEnabled;
            _automaticUpdatesEnabled = settings.AutomaticUpdatesEnabled;
            _onboardingTutorialVersionCompleted = Math.Max(
                0,
                settings.OnboardingTutorialVersionCompleted);
            _hudDockMirrored = settings.HudDockMirrored;
            _clipboardCaptureSoundEnabled = settings.ClipboardCaptureSoundEnabled;
            RestoreLayoutProfiles(settings.HudLayoutProfiles);
            _zoomPresetIndex = Math.Clamp(settings.ZoomPresetIndex, 0, _zoomPresets.Length - 1);
            _trailDurationIndex = Math.Clamp(settings.TrailDurationIndex, 0, _trailDurations.Length - 1);
            _markerStyleIndex = Math.Clamp(settings.MarkerStyleIndex, 0, _markerStyleModes.Length - 1);
            _arrivalAlertIndex = Math.Clamp(settings.ArrivalAlertIndex, 0, _arrivalAlertDistances.Length - 1);
            _dangerAlertIndex = Math.Clamp(settings.DangerAlertIndex, 0, _dangerAlertDistances.Length - 1);
            _packSpreadAlertIndex = Math.Clamp(
                settings.PackSpreadAlertIndex, 0, _packSpreadAlertDistances.Length - 1);
            _encounterAlertIndex = Math.Clamp(
                settings.EncounterAlertIndex, 0, _encounterAlertDistances.Length - 1);
            _encounterMemoryIndex = Math.Clamp(
                settings.EncounterMemoryIndex, 0, _encounterMemoryDurations.Length - 1);
            _landmarkLabelDensityIndex = Math.Clamp(
                settings.LandmarkLabelDensityIndex, 0, _landmarkLabelDensityModes.Length - 1);
            _currentMapScale = _zoomPresets[_zoomPresetIndex];
            _playerLabelsVisible = settings.PlayerLabelsVisible;
            _friendOnly = settings.FriendOnly;
            _headingUp = settings.HeadingUp;
            _lookAheadEnabled = settings.LookAheadEnabled;
            _smartZoomEnabled = settings.SmartZoomEnabled;
            _smartZoomSuspended = false;
            _rangeRingModeIndex = settings.RangeRingModeIndex is int savedRingMode
                ? Math.Clamp(savedRingMode, 0, _rangeRingModes.Length - 1)
                : settings.RangeRingsVisible ? 2 : 0;
            _rangeRingsVisible = _rangeRingModeIndex > 0;
            _mapGridVisible = settings.MapGridVisible;
            _breadcrumbTrailVisible = settings.BreadcrumbTrailVisible;
            _explorationEnabled = settings.ExplorationEnabled;
            _terrainRouteStyle = TerrainRouteStyleLogic.Normalize(settings.TerrainRouteStyle);
            _terrainGapPolicy = TerrainGapPolicyLogic.Normalize(settings.TerrainGapPolicy);
            _terrainRouteConfidenceVisible = settings.TerrainRouteConfidenceVisible;
            _learnedPassageRoutingEnabled = settings.LearnedPassageRoutingEnabled;
            _learnedPassageVisible = settings.LearnedPassageVisible;
            _friendRadarVisible = settings.FriendRadarVisible;
            _encounterHudVisible = settings.EncounterHudVisible;
            _nearestPlaceVisible = settings.NearestPlaceVisible;
            _staleSoundEnabled = settings.StaleSoundEnabled;
            _timerSoundEnabled = settings.TimerSoundEnabled;
            _rememberLastPosition = settings.RememberLastPosition;
            _alwaysOnTop = settings.AlwaysOnTop;
            _overlayLocked = settings.OverlayLocked;
            _playFocusEnabled = settings.PlayFocusEnabled;
            _navigationHudVisible = settings.NavigationHudVisible;
            _vitalsHudVisible = settings.VitalsHudVisible;
            _survivalHudVisible = settings.SurvivalHudVisible;
            _alertHudVisible = settings.AlertHudVisible;
            _quickKeysHudVisible = settings.QuickKeysHudVisible;
            _quickKeysModeIndex = QuickKeysLogic.NormalizeModeIndex(settings.QuickKeysModeIndex);
            _aimGuideEnabled = settings.AimGuideEnabled;
            _aimGuideGrowthIndex = AimCalibrationLogic.NormalizeGrowthIndex(settings.AimGuideGrowthIndex);
            _aimGuideGrowthSyncEnabled = settings.AimGuideGrowthSyncEnabled;
            _aimGuideCameraIndex = AimCalibrationLogic.NormalizeCameraIndex(settings.AimGuideCameraIndex);
            _aimGuideModeIndex = Math.Clamp(settings.AimGuideModeIndex, 0, 2);
            _aimGuideSize = Math.Clamp(settings.AimGuideSize, 90, 520);
            _aimGuideDepthScale = Math.Clamp(settings.AimGuideDepthScale, 0.55, 1.40);
            _aimGuideHorizontalOffset = Math.Clamp(settings.AimGuideHorizontalOffset, -240, 240);
            _aimGuideVerticalOffset = Math.Clamp(settings.AimGuideVerticalOffset, -240, 240);
            _aimGuideAttackIndex = AimCalibrationLogic.NormalizeAttackIndex(settings.AimGuideAttackIndex);
            _aimGuideAreaVisible = settings.AimGuideAreaVisible;
            _aimGuideCenterCueVisible = settings.AimGuideCenterCueVisible;
            _aimGuideUncertaintyVisible = settings.AimGuideUncertaintyVisible;
            _aimGuideLabelVisible = settings.AimGuideLabelVisible;
            _serverSessionProfileId = ServerSessionLogic.NormalizeProfileId(settings.ServerSessionProfileId);
            _serverSessionName = ServerSessionLogic.NormalizeCustomServerName(settings.ServerSessionName);
            _communityServerAddress = CommunityServerWatchLogic.SanitizeAddressInput(
                settings.CommunityServerAddress);
            _communityServerWatchEnabled = settings.CommunityServerWatchEnabled
                                           && CommunityServerWatchLogic.TryNormalizeAddress(
                                               _communityServerAddress, out _);
            _communityServerSlotAlertEnabled = settings.CommunityServerSlotAlertEnabled;
            _universalCoordinateCaptureEnabled =
                settings.PlayerSyncSetupVersion < CurrentPlayerSyncSetupVersion
                || settings.UniversalCoordinateCaptureEnabled;
            _visibleHudSensorEnabled = settings.VisibleHudSensorEnabled;
            _visibleHudCalibration = VisibleHudSensorLogic.NormalizeCalibration(
                new VisibleHudCalibration(
                    settings.VisibleHudCalibrationScale,
                    settings.VisibleHudCalibrationOffsetX,
                    settings.VisibleHudCalibrationOffsetY,
                    settings.VisibleHudCalibrationScore,
                    default));
            _autoLocateOnGameStart = settings.AutoLocateOnGameStart;
            RestoreHotkeyBindings(settings.HotkeyBindings);
            var commandActionIds = CommandPaletteActions.Select(action => action.Id);
            _commandFavoriteActionIds.Clear();
            _commandFavoriteActionIds.AddRange(
                CommandQuickAccessLogic.NormalizeFavorites(
                    settings.CommandFavoriteActionIds,
                    commandActionIds));
            _commandRecentActionIds.Clear();
            _commandRecentActionIds.AddRange(
                CommandQuickAccessLogic.NormalizeRecents(
                    settings.CommandRecentActionIds,
                    commandActionIds));
            _voiceEnabled = settings.VoiceEnabled;
            _voiceAutoOpen = settings.VoiceAutoOpen;
            _voiceHudVisible = settings.VoiceHudVisible;
            _voicePttKeyIndex = VoiceIntegrationLogic.NormalizeKeyIndex(settings.VoicePttKeyIndex);
            _voiceServerUrl = NormalizeVoiceServerUrl(settings.VoiceServerUrl);
            _voiceNatAssist = settings.VoiceNatAssist;
            _voiceProximityEnabled = settings.VoiceProximityEnabled;
            _voiceRangeIndex = VoiceIntegrationLogic.NormalizeRangeIndex(settings.VoiceRangeIndex);
            _voiceEchoCancellation = settings.VoiceEchoCancellation;
            _voiceNoiseSuppression = settings.VoiceNoiseSuppression;
            _voiceAutoGainControl = settings.VoiceAutoGainControl;
            _voiceMicMeterEnabled = settings.VoiceMicMeterEnabled;
            _voiceQualityMonitorEnabled = settings.VoiceQualityMonitorEnabled;
            RestoreSteamFriendWatchlist(
                settings.SteamFriendWatchlist,
                settings.SelectedSteamFriendWatchId,
                settings.AutoFollowSteamFriendWatchId);
            _isleyRelayJoinLink = (settings.IsleyRelayJoinLink ?? string.Empty)
                .Trim();
            if (_isleyRelayJoinLink.Length > 1024)
            {
                _isleyRelayJoinLink = string.Empty;
            }
            _guideSelectedSpeciesId = FieldGuideLogic.Find(settings.GuideSelectedSpeciesId)?.Id ?? "allosaurus";
            _aimCalibrationProfiles.Clear();
            _aimCalibrationProfiles.AddRange(AimCalibrationLogic.NormalizeProfiles(
                (settings.AimCalibrationProfiles ?? []).Select(profile => new AimCalibrationProfile(
                    profile.SpeciesId,
                    profile.AttackId,
                    profile.GrowthIndex ?? AimCalibrationLogic.DefaultGrowthIndex,
                    profile.CameraIndex ?? AimCalibrationLogic.DefaultCameraIndex,
                    profile.ModeIndex,
                    profile.Size,
                    profile.DepthScale ?? AimCalibrationLogic.DefaultDepthScale,
                    profile.HorizontalOffset,
                    profile.VerticalOffset,
                    profile.ConfirmedMatches,
                    profile.InsideMisses,
                    profile.OutsideHits,
                    profile.UpdatedAtUnixMs)),
                speciesId => FieldGuideLogic.Find(speciesId) is not null));
            ApplyAimCalibrationForSelection(useDefaultsWhenMissing: false, updatePresentation: false);
            _guideFavoriteSpeciesIds.Clear();
            _guideFavoriteSpeciesIds.AddRange(FieldGuideLogic.NormalizeFavorites(settings.GuideFavoriteSpeciesIds));
            _focusModeRestoreSnapshot = settings.FocusModeRestoreSnapshot;
            _activeFocusModeId = _focusModeRestoreSnapshot is not null
                                 && GetFocusModeDefinition(settings.ActiveFocusModeId) is not null
                ? settings.ActiveFocusModeId
                : string.Empty;
            _pressureCoachFirstDeathSeen = settings.PressureCoachFirstDeathSeen;
            _pressureCoachFirstNestSeen = settings.PressureCoachFirstNestSeen;
            _pressureCoachConsentRosterSeen = settings.PressureCoachConsentRosterSeen;
            _pressureCoachPreStreamSeen = settings.PressureCoachPreStreamSeen;
            _whatsNewVersionSeen = (settings.WhatsNewVersionSeen ?? string.Empty).Trim();
            _preferBetaUpdates = settings.PreferBetaUpdates;
            RestoreSurvivalTimers(settings.SurvivalTimers);
            RestoreSurvivalIncident(
                settings.SurvivalIncidentId,
                settings.SurvivalIncidentStartedAtUnixMs,
                settings.SurvivalIncidentAdditionalSeconds,
                settings.SurvivalIncidentHudCollapsed);
            RestoreLifeRun(settings.LifeRun);
            RestoreLifeRunHistory(settings.LifeRunHistory);
            RestoreCommunityServerProfiles(
                settings.CommunityServerProfiles,
                settings.SelectedCommunityServerProfileId);
            _expanded = Width >= 600 || Height >= 680;
        }
        catch (Exception ex)
        {
            // Invalid individual values should never stop the mapper from launching.
            _settingsPersistenceError = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private void RestoreSurvivalTimers(IEnumerable<SurvivalTimerSettings>? savedTimers)
    {
        _survivalTimers.Clear();
        if (savedTimers is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var saved in savedTimers.Take(4))
        {
            if (saved.DurationSeconds is < 60 or > 21600)
            {
                continue;
            }

            DateTimeOffset endsAt;
            try
            {
                endsAt = DateTimeOffset.FromUnixTimeMilliseconds(saved.EndsAtUnixMs);
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }

            var label = NormalizeTimerLabel(saved.Label);
            if (string.IsNullOrWhiteSpace(label))
            {
                label = $"Timer {_survivalTimers.Count + 1}";
            }

            var isPaused = saved.IsPaused && !saved.Completed;
            var pausedRemaining = isPaused
                ? Math.Clamp(saved.PausedRemainingSeconds, 1, saved.DurationSeconds)
                : 0;
            var completed = saved.Completed || (!isPaused && endsAt <= now);
            if (completed && now - endsAt > TimeSpan.FromHours(12))
            {
                continue;
            }

            if (!completed && !isPaused && endsAt - now > TimeSpan.FromSeconds(saved.DurationSeconds + 5))
            {
                endsAt = now.AddSeconds(saved.DurationSeconds);
            }

            var savedId = saved.Id ?? string.Empty;
            _survivalTimers.Add(new SurvivalTimer
            {
                Id = Regex.IsMatch(savedId, "^[a-fA-F0-9]{32}$")
                    ? savedId
                    : Guid.NewGuid().ToString("N"),
                Label = label,
                DurationSeconds = saved.DurationSeconds,
                EndsAt = endsAt,
                PausedRemainingSeconds = pausedRemaining,
                IsPaused = isPaused,
                Completed = completed,
                CompletionNotified = completed
            });
        }

        _survivalTimerUiSignature = string.Empty;
    }

    private void RestoreSteamFriendWatchlist(
        IEnumerable<SteamFriendWatchEntry>? savedEntries,
        string? selectedEntryId,
        string? autoFollowEntryId)
    {
        _steamFriendWatchlist.Clear();
        _steamFriendWatchlist.AddRange(
            SteamFriendLogic.NormalizeEntries(savedEntries, DateTimeOffset.UtcNow));
        _selectedSteamFriendWatchId = _steamFriendWatchlist.Any(entry =>
            string.Equals(entry.Id, selectedEntryId, StringComparison.Ordinal))
            ? selectedEntryId ?? string.Empty
            : _steamFriendWatchlist.FirstOrDefault()?.Id ?? string.Empty;
        _autoFollowSteamFriendWatchId = _steamFriendWatchlist.Any(entry =>
            string.Equals(entry.Id, autoFollowEntryId, StringComparison.Ordinal))
            ? autoFollowEntryId ?? string.Empty
            : string.Empty;
    }

    private void RestoreLifeRun(LifeRunSettings? saved)
    {
        _lifeRunActive = saved?.Active == true;
        _lifeRunStageIndex = Math.Clamp(saved?.StageIndex ?? 1, 0, _lifeRunStageLabels.Length - 1);
        _lifeRunHudVisible = saved?.HudVisible ?? true;
        _lifeRunSanctuaryVisited = _lifeRunActive && saved?.SanctuaryVisited == true;
        _lifeRunPerfectDiet = _lifeRunActive && saved?.PerfectDiet == true;
        _lifeRunNestedIn = _lifeRunActive && saved?.NestedIn == true;
        _lifeRunRaisedYoung = _lifeRunActive && saved?.RaisedYoung == true;
        _spawnPlanCoverReady = _lifeRunActive && saved?.SpawnCoverReady == true;
        _spawnPlanScentChecked = _lifeRunActive && saved?.SpawnScentChecked == true;
        _spawnPlanWaterFound = _lifeRunActive && saved?.SpawnWaterFound == true;
        _spawnPlanFoodFound = _lifeRunActive && saved?.SpawnFoodFound == true;
        _zoneBriefIndex = _lifeRunActive
            ? (int)ZoneBriefLogic.NormalizeZone(saved?.CurrentZoneIndex ?? 0)
            : 0;
        _lifeRunMigrationVisits = _lifeRunActive ? Math.Clamp(saved?.MigrationVisits ?? 0, 0, 99) : 0;
        _lifeRunPatrolVisits = _lifeRunActive ? Math.Clamp(saved?.PatrolVisits ?? 0, 0, 99) : 0;
        _lifeRunMassMigrationVisited = _lifeRunActive && saved?.MassMigrationVisited == true;
        _lifeRunFertilityStatus = _lifeRunActive ? Math.Clamp(saved?.FertilityStatus ?? 0, 0, 2) : 0;
        _lifeRunSpasmStatus = _lifeRunActive ? Math.Clamp(saved?.SpasmStatus ?? 0, 0, 2) : 0;
        _lifeRunSpeciesClass = _lifeRunActive ? Math.Clamp(saved?.SpeciesClass ?? 0, 0, 2) : 0;
        _dietSpeciesIndex = _lifeRunActive
            ? DietCoachLogic.NormalizeSpeciesIndex(saved?.DietSpeciesIndex ?? 0)
            : 0;
        _dietTargetIndex = _lifeRunActive
            ? DietCoachLogic.NormalizeTargetIndex(saved?.DietTargetIndex ?? 0)
            : 0;
        _dietSlot1 = _lifeRunActive ? DietCoachLogic.NormalizeNutrient(saved?.DietSlot1 ?? 0) : 0;
        _dietSlot2 = _lifeRunActive ? DietCoachLogic.NormalizeNutrient(saved?.DietSlot2 ?? 0) : 0;
        _dietSlot3 = _lifeRunActive ? DietCoachLogic.NormalizeNutrient(saved?.DietSlot3 ?? 0) : 0;
        _lifeRunGrowthPercent = _lifeRunActive
            ? Math.Clamp(saved?.GrowthPercent ?? GrowthPlannerLogic.StageAnchor(_lifeRunStageIndex), 0, 100)
            : 25;
        _growthServerMultiplierIndex = _lifeRunActive
            ? Math.Clamp(
                saved?.GrowthServerMultiplierIndex ?? GrowthPlannerLogic.DefaultLiveMapMultiplierIndex,
                0,
                GrowthPlannerLogic.ServerMultipliers.Length - 1)
            : GrowthPlannerLogic.DefaultLiveMapMultiplierIndex;
        _growthPaused = _lifeRunActive && saved?.GrowthPaused == true;
        if (_lifeRunActive) _lifeRunStageIndex = GrowthPlannerLogic.StageIndex(_lifeRunGrowthPercent);
        _elderEntombCount = _lifeRunActive
            ? Math.Clamp(saved?.ElderEntombCount ?? 0, 0, ElderLineageLogic.MaximumEntombCount)
            : 0;
        _elderPrimeConfirmed = _lifeRunActive
                               && _lifeRunGrowthPercent >= 75
                               && saved?.ElderPrimeConfirmed == true;
        _elderConfirmed = _lifeRunActive
                          && _lifeRunGrowthPercent >= 100
                          && saved?.ElderConfirmed == true;
        var savedNest = saved?.NestPlanner;
        _nestAutoHatchGuidanceEnabled = savedNest?.AutoHatchGuidanceEnabled ?? true;
        var nest = NestPlannerLogic.Normalize(new NestPlannerSnapshot(
            _lifeRunActive && savedNest?.Active == true,
            savedNest?.PhaseIndex ?? 0,
            savedNest?.PartnerReady == true,
            savedNest?.SiteReady == true,
            savedNest?.DebrisReady == true,
            savedNest?.ReservesReady == true,
            savedNest?.AccessIndex ?? 0,
            savedNest?.EggTarget ?? 2,
            savedNest?.EggsLaid ?? 0,
            savedNest?.EggsHatched ?? 0,
            savedNest?.YoungRaised ?? 0,
            savedNest?.TimerDurationIndex ?? 1));
        ApplyNestPlannerSnapshot(nest);
        _mutationLoadout.Clear();
        if (_lifeRunActive && saved?.MutationLoadout is { Count: > 0 } savedLoadout)
        {
            _mutationLoadout.AddRange(MutationPlannerLogic.NormalizeLoadout(
                savedLoadout.Select(item => new MutationLoadoutItem(item.Slot, item.MutationId ?? string.Empty, item.Status))));
        }
        _mutationBuildFocusIndex = _lifeRunActive
            ? MutationBuildLogic.NormalizeFocusIndex(saved?.MutationBuildFocusIndex ?? 0)
            : 0;
        _mutationUnlockProgress.Clear();
        if (_lifeRunActive && saved?.MutationUnlockProgress is { Count: > 0 } savedUnlockProgress)
        {
            _mutationUnlockProgress.AddRange(MutationUnlockLogic.NormalizeProgress(
                savedUnlockProgress.Select(item =>
                    new MutationUnlockProgress(item.ChallengeId ?? string.Empty, item.Value))));
        }
        _mutationUnlockSelectedIndex = _lifeRunActive
            ? MutationUnlockLogic.NormalizeSelectedIndex(saved?.MutationUnlockSelectedIndex ?? 0)
            : 0;
        _lifeRunStartedAt = DateTimeOffset.UtcNow;
        if (_lifeRunActive)
        {
            try
            {
                var restored = DateTimeOffset.FromUnixTimeMilliseconds(saved?.StartedAtUnixMs ?? 0);
                var now = DateTimeOffset.UtcNow;
                _lifeRunStartedAt = restored > now.AddMinutes(5) || now - restored > TimeSpan.FromDays(365)
                    ? now
                    : restored;
            }
            catch (ArgumentOutOfRangeException)
            {
                _lifeRunStartedAt = DateTimeOffset.UtcNow;
            }
        }
        _newLifeRunConfirmationPending = false;
        _clearNestConfirmationPending = false;
        _clearNestConfirmationRevision++;
        _nestPlannerUiSignature = string.Empty;
        _growthPlannerUiSignature = string.Empty;
        _elderLineageUiSignature = string.Empty;
        _recordEntombConfirmationPending = false;
        _recordEntombConfirmationRevision++;
        _zoneBriefUiSignature = string.Empty;
        _lifeRunUiSignature = string.Empty;
        _mutationPlannerUiSignature = string.Empty;
        _mutationRemoveConfirmationSlot = 0;
        _mutationRemoveConfirmationRevision++;
        _mutationUnlockUiSignature = string.Empty;
        _mutationUnlockResetConfirmationId = string.Empty;
        _mutationUnlockResetConfirmationRevision++;
    }

    private void RestoreLifeRunHistory(IEnumerable<LifeRunHistoryEntry>? savedEntries)
    {
        _lifeRunHistory.Clear();
        _lifeRunHistory.AddRange(LifeRunHistoryLogic.NormalizeEntries(
            savedEntries,
            DateTimeOffset.Now));
        _lifeRunHistoryUiSignature = string.Empty;
        _clearLifeRunHistoryConfirmationPending = false;
        _clearLifeRunHistoryConfirmationRevision++;
    }

    private void RestoreSurvivalIncident(
        string? savedId,
        long savedStartedAtUnixMs,
        int savedAdditionalSeconds,
        bool savedHudCollapsed)
    {
        ResetRecoveryMonitorState();
        _survivalIncidentId = SurvivalAssistantLogic.NormalizeIncidentId(savedId);
        _survivalIncidentStartedAt = DateTimeOffset.UtcNow;
        _survivalIncidentAdditionalSeconds = SurvivalAssistantLogic.Find(_survivalIncidentId) is { } incident
            ? SurvivalAssistantLogic.NormalizeAdditionalSeconds(incident, savedAdditionalSeconds)
            : 0;
        if (!string.IsNullOrEmpty(_survivalIncidentId))
        {
            try
            {
                var restored = DateTimeOffset.FromUnixTimeMilliseconds(savedStartedAtUnixMs);
                var now = DateTimeOffset.UtcNow;
                _survivalIncidentStartedAt = restored > now.AddMinutes(5) || now - restored > TimeSpan.FromDays(7)
                    ? now
                    : restored;
            }
            catch (ArgumentOutOfRangeException)
            {
                _survivalIncidentStartedAt = DateTimeOffset.UtcNow;
            }
        }
        if (SurvivalAssistantLogic.Find(_survivalIncidentId) is { } restoredStopEatingIncident
            && !SurvivalAssistantLogic.ShouldRestoreIncident(
                restoredStopEatingIncident,
                _survivalIncidentStartedAt,
                DateTimeOffset.UtcNow,
                _survivalIncidentAdditionalSeconds))
        {
            _survivalIncidentId = string.Empty;
            _survivalIncidentAdditionalSeconds = 0;
        }
        _survivalIncidentEstimateCompletionAnnounced = true;
        _survivalIncidentFinalMinutePulsing = false;
        _survivalIncidentPickerOpen = false;
        var expiredEstimate = SurvivalAssistantLogic.Find(_survivalIncidentId) is { ExpectedSeconds: > 0 } restoredIncident
                              && SurvivalAssistantLogic.RemainingSeconds(
                                  restoredIncident,
                                  _survivalIncidentStartedAt,
                                  DateTimeOffset.UtcNow,
                                  _survivalIncidentAdditionalSeconds) == 0;
        _survivalIncidentHudCollapsed = SurvivalAssistantLogic.HudPresentation(
            _survivalIncidentId,
            savedHudCollapsed && !expiredEstimate).IsCollapsed;
        _survivalIncidentUiSignature = string.Empty;
    }

    private void SaveSettings()
    {
        try
        {
            SaveSettingsCore();
        }
        catch (Exception ex)
        {
            // Saving preferences is deliberately non-fatal. Keep the overlay usable and
            // make failures visible in App tools instead of losing changes silently.
            _settingsPersistenceError = $"{ex.GetType().Name}: {ex.Message}";
            UpdateSettingsStorageStatus();
        }
    }

    private void SaveSettingsCore()
    {
        EnsureCommunityServerProfiles();
        SyncCurrentCommunityServerProfile();
        if (GetFocusModeDefinition(_activeFocusModeId) is not { } activeFocusMode
            || !FocusDisplaySettingsMatch(activeFocusMode))
        {
            _activeFocusModeId = string.Empty;
        }

        var workArea = SystemParameters.WorkArea;
        var resolvedWidth = _isDocked && double.IsFinite(_dockRestoreWidth)
            ? _dockRestoreWidth
            : double.IsFinite(ActualWidth) && ActualWidth > 0
            ? ActualWidth
            : double.IsFinite(Width) && Width > 0 ? Width : 472;
        var resolvedHeight = _isDocked && double.IsFinite(_dockRestoreHeight)
            ? _dockRestoreHeight
            : double.IsFinite(ActualHeight) && ActualHeight > 0
            ? ActualHeight
            : double.IsFinite(Height) && Height > 0 ? Height : 560;
        var resolvedLeft = _isDocked && double.IsFinite(_dockRestoreLeft)
            ? _dockRestoreLeft
            : double.IsFinite(Left)
            ? Left
            : workArea.Left + (workArea.Width - resolvedWidth) / 2;
        var resolvedTop = _isDocked && double.IsFinite(_dockRestoreTop)
            ? _dockRestoreTop
            : double.IsFinite(Top)
            ? Top
            : workArea.Top + (workArea.Height - resolvedHeight) / 2;
        var settings = new MapperSettings
        {
            SchemaVersion = MapperSettings.CurrentSchemaVersion,
            Width = resolvedWidth,
            Height = resolvedHeight,
            Left = resolvedLeft,
            Top = resolvedTop,
            OpacityIndex = _opacityIndex,
            MapLightModeIndex = _mapLightModeIndex,
            HudDetailModeIndex = _hudDetailModeIndex,
            SmartHudEnabled = _smartHudEnabled,
            LiteModeEnabled = _liteModeEnabled,
            AutomaticUpdatesEnabled = _automaticUpdatesEnabled,
            OnboardingTutorialVersionCompleted = _onboardingTutorialVersionCompleted,
            HudDockMirrored = _hudDockMirrored,
            ClipboardCaptureSoundEnabled = _clipboardCaptureSoundEnabled,
            HudLayoutProfiles = _hudLayoutProfiles.Select(profile => new HudLayoutProfileSettings
            {
                Name = profile.Name,
                HudDockMirrored = profile.HudDockMirrored,
                Expanded = profile.Expanded,
                Width = profile.Width,
                Height = profile.Height,
                HudDetailModeIndex = profile.HudDetailModeIndex,
                NavigationHudVisible = profile.NavigationHudVisible,
                VitalsHudVisible = profile.VitalsHudVisible,
                SurvivalHudVisible = profile.SurvivalHudVisible,
                AlertHudVisible = profile.AlertHudVisible,
                QuickKeysHudVisible = profile.QuickKeysHudVisible,
                QuickKeysModeIndex = profile.QuickKeysModeIndex,
                SavedAtUnixMs = profile.SavedAtUnixMs
            }).ToList(),
            ZoomPresetIndex = _zoomPresetIndex,
            TrailDurationIndex = _trailDurationIndex,
            ArrivalAlertIndex = _arrivalAlertIndex,
            DangerAlertIndex = _dangerAlertIndex,
            PackSpreadAlertIndex = _packSpreadAlertIndex,
            EncounterAlertIndex = _encounterAlertIndex,
            EncounterMemoryIndex = _encounterMemoryIndex,
            LandmarkLabelDensityIndex = _landmarkLabelDensityIndex,
            MarkerStyleIndex = _markerStyleIndex,
            PlayerLabelsVisible = _playerLabelsVisible,
            FriendOnly = _friendOnly,
            HeadingUp = _headingUp,
            LookAheadEnabled = _lookAheadEnabled,
            SmartZoomEnabled = _smartZoomEnabled,
            RangeRingsVisible = _rangeRingsVisible,
            RangeRingModeIndex = _rangeRingModeIndex,
            MapGridVisible = _mapGridVisible,
            BreadcrumbTrailVisible = _breadcrumbTrailVisible,
            ExplorationEnabled = _explorationEnabled,
            TerrainRouteStyle = _terrainRouteStyle,
            TerrainGapPolicy = _terrainGapPolicy,
            TerrainRouteConfidenceVisible = _terrainRouteConfidenceVisible,
            LearnedPassageRoutingEnabled = _learnedPassageRoutingEnabled,
            LearnedPassageVisible = _learnedPassageVisible,
            FriendRadarVisible = _friendRadarVisible,
            EncounterHudVisible = _encounterHudVisible,
            NearestPlaceVisible = _nearestPlaceVisible,
            StaleSoundEnabled = _staleSoundEnabled,
            TimerSoundEnabled = _timerSoundEnabled,
            RememberLastPosition = _rememberLastPosition,
            AlwaysOnTop = _alwaysOnTop,
            OverlayLocked = _overlayLocked,
            PlayFocusEnabled = _playFocusEnabled,
            NavigationHudVisible = _navigationHudVisible,
            VitalsHudVisible = _vitalsHudVisible,
            SurvivalHudVisible = _survivalHudVisible,
            AlertHudVisible = _alertHudVisible,
            QuickKeysHudVisible = _quickKeysHudVisible,
            QuickKeysModeIndex = _quickKeysModeIndex,
            AimGuideEnabled = _aimGuideEnabled,
            AimGuideGrowthIndex = _aimGuideGrowthIndex,
            AimGuideGrowthSyncEnabled = _aimGuideGrowthSyncEnabled,
            AimGuideCameraIndex = _aimGuideCameraIndex,
            AimGuideModeIndex = _aimGuideModeIndex,
            AimGuideSize = _aimGuideSize,
            AimGuideDepthScale = _aimGuideDepthScale,
            AimGuideHorizontalOffset = _aimGuideHorizontalOffset,
            AimGuideVerticalOffset = _aimGuideVerticalOffset,
            AimGuideAttackIndex = _aimGuideAttackIndex,
            AimGuideAreaVisible = _aimGuideAreaVisible,
            AimGuideCenterCueVisible = _aimGuideCenterCueVisible,
            AimGuideUncertaintyVisible = _aimGuideUncertaintyVisible,
            AimGuideLabelVisible = _aimGuideLabelVisible,
            AimCalibrationProfiles = _aimCalibrationProfiles.Select(profile => new AimCalibrationProfileSettings
            {
                SpeciesId = profile.SpeciesId,
                AttackId = profile.AttackId,
                GrowthIndex = profile.GrowthIndex,
                CameraIndex = profile.CameraIndex,
                ModeIndex = profile.ModeIndex,
                Size = profile.Size,
                DepthScale = profile.DepthScale,
                HorizontalOffset = profile.HorizontalOffset,
                VerticalOffset = profile.VerticalOffset,
                ConfirmedMatches = profile.ConfirmedMatches,
                InsideMisses = profile.InsideMisses,
                OutsideHits = profile.OutsideHits,
                UpdatedAtUnixMs = profile.UpdatedAtUnixMs
            }).ToList(),
            ServerSessionProfileId = _serverSessionProfileId,
            ServerSessionName = _serverSessionName,
            CommunityServerAddress = _communityServerAddress,
            CommunityServerWatchEnabled = _communityServerWatchEnabled,
            CommunityServerSlotAlertEnabled = _communityServerSlotAlertEnabled,
            UniversalCoordinateCaptureEnabled = _universalCoordinateCaptureEnabled,
            VisibleHudSensorEnabled = _visibleHudSensorEnabled,
            VisibleHudCalibrationScale = _visibleHudCalibration.Scale,
            VisibleHudCalibrationOffsetX = _visibleHudCalibration.OffsetX,
            VisibleHudCalibrationOffsetY = _visibleHudCalibration.OffsetY,
            VisibleHudCalibrationScore = _visibleHudCalibration.Score,
            AutoLocateOnGameStart = _autoLocateOnGameStart,
            PlayerSyncSetupVersion = CurrentPlayerSyncSetupVersion,
            SelectedCommunityServerProfileId = _selectedCommunityServerProfileId,
            CommunityServerProfiles = _communityServerProfiles.Select(profile =>
                new CommunityServerProfileSettings
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    Address = profile.Address,
                    WatchEnabled = profile.WatchEnabled,
                    SlotAlertEnabled = profile.SlotAlertEnabled,
                    GrowthMultiplierIndex = profile.GrowthMultiplierIndex,
                    IsleyJoinLink = SanitizeCommunityIsleyJoinLink(profile.IsleyJoinLink)
                }).ToList(),
            SteamFriendWatchlist = _steamFriendWatchlist.Select(entry => new SteamFriendWatchEntry
            {
                Id = entry.Id,
                ProfileUrl = entry.ProfileUrl,
                SteamId64 = entry.SteamId64,
                MapName = entry.MapName,
                AddedAtUnixMs = entry.AddedAtUnixMs
            }).ToList(),
            SelectedSteamFriendWatchId = _selectedSteamFriendWatchId,
            AutoFollowSteamFriendWatchId = _autoFollowSteamFriendWatchId,
            IsleyRelayJoinLink = _isleyRelayJoinLink,
            HotkeyBindings = HotkeyBindingLogic.ToSettings(_hotkeyBindings.Values),
            CommandFavoriteActionIds = _commandFavoriteActionIds.ToList(),
            CommandRecentActionIds = _commandRecentActionIds.ToList(),
            VoiceEnabled = _voiceEnabled,
            VoiceAutoOpen = _voiceAutoOpen,
            VoiceHudVisible = _voiceHudVisible,
            VoicePttKeyIndex = _voicePttKeyIndex,
            VoiceServerUrl = _voiceServerUrl,
            VoiceNatAssist = _voiceNatAssist,
            VoiceProximityEnabled = _voiceProximityEnabled,
            VoiceRangeIndex = _voiceRangeIndex,
            VoiceEchoCancellation = _voiceEchoCancellation,
            VoiceNoiseSuppression = _voiceNoiseSuppression,
            VoiceAutoGainControl = _voiceAutoGainControl,
            VoiceMicMeterEnabled = _voiceMicMeterEnabled,
            VoiceQualityMonitorEnabled = _voiceQualityMonitorEnabled,
            GuideSelectedSpeciesId = _guideSelectedSpeciesId,
            GuideFavoriteSpeciesIds = _guideFavoriteSpeciesIds.ToList(),
            SurvivalIncidentId = _survivalIncidentId,
            SurvivalIncidentStartedAtUnixMs = _survivalIncidentStartedAt.ToUnixTimeMilliseconds(),
            SurvivalIncidentAdditionalSeconds = _survivalIncidentAdditionalSeconds,
            SurvivalIncidentHudCollapsed = _survivalIncidentHudCollapsed,
            FocusModeRestoreSnapshot = _focusModeRestoreSnapshot,
            ActiveFocusModeId = _activeFocusModeId,
            PressureCoachFirstDeathSeen = _pressureCoachFirstDeathSeen,
            PressureCoachFirstNestSeen = _pressureCoachFirstNestSeen,
            PressureCoachConsentRosterSeen = _pressureCoachConsentRosterSeen,
            PressureCoachPreStreamSeen = _pressureCoachPreStreamSeen,
            WhatsNewVersionSeen = _whatsNewVersionSeen,
            PreferBetaUpdates = _preferBetaUpdates,
            // Planner-owned state (growth percent/multiplier/pause, the spawn checklist, the
            // nest planner, and the mutation loadout/focus/unlock progress) is intentionally
            // NOT dual-written here anymore: it lives only in the schema-versioned
            // planner-state.json store. RestoreLifeRun keeps reading these legacy keys from
            // old files forever so pre-store installs still migrate on first launch.
            LifeRun = new LifeRunSettings
            {
                Active = _lifeRunActive,
                StartedAtUnixMs = _lifeRunStartedAt.ToUnixTimeMilliseconds(),
                StageIndex = _lifeRunStageIndex,
                HudVisible = _lifeRunHudVisible,
                SanctuaryVisited = _lifeRunSanctuaryVisited,
                PerfectDiet = _lifeRunPerfectDiet,
                NestedIn = _lifeRunNestedIn,
                RaisedYoung = _lifeRunRaisedYoung,
                CurrentZoneIndex = _zoneBriefIndex,
                MigrationVisits = _lifeRunMigrationVisits,
                PatrolVisits = _lifeRunPatrolVisits,
                MassMigrationVisited = _lifeRunMassMigrationVisited,
                FertilityStatus = _lifeRunFertilityStatus,
                SpasmStatus = _lifeRunSpasmStatus,
                SpeciesClass = _lifeRunSpeciesClass,
                DietSpeciesIndex = _dietSpeciesIndex,
                DietTargetIndex = _dietTargetIndex,
                DietSlot1 = _dietSlot1,
                DietSlot2 = _dietSlot2,
                DietSlot3 = _dietSlot3,
                ElderEntombCount = _elderEntombCount,
                ElderPrimeConfirmed = _elderPrimeConfirmed,
                ElderConfirmed = _elderConfirmed
            },
            LifeRunHistory = _lifeRunHistory.Select(entry => new LifeRunHistoryEntry
            {
                Id = entry.Id,
                EndedAtUnixMs = entry.EndedAtUnixMs,
                SpeciesId = entry.SpeciesId,
                SpeciesName = entry.SpeciesName,
                Outcome = entry.Outcome,
                DurationSeconds = entry.DurationSeconds,
                FinalGrowthPercent = entry.FinalGrowthPercent,
                StageIndex = entry.StageIndex,
                TrackedMilestones = entry.TrackedMilestones,
                PrimeConditions = entry.PrimeConditions,
                PrimeRequired = entry.PrimeRequired,
                ServerName = entry.ServerName
            }).ToList(),
            SurvivalTimers = _survivalTimers.Take(4).Select(timer => new SurvivalTimerSettings
            {
                Id = timer.Id,
                Label = timer.Label,
                DurationSeconds = timer.DurationSeconds,
                EndsAtUnixMs = timer.EndsAt.ToUnixTimeMilliseconds(),
                PausedRemainingSeconds = timer.PausedRemainingSeconds,
                IsPaused = timer.IsPaused,
                Completed = timer.Completed
            }).ToList()
        };
        var serializedSettings = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        var failures = new List<string>();
        var candidates = new[] { _activeSettingsPath, PrimarySettingsPath, PortableSettingsPath }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            string? temporaryPath = null;
            try
            {
                var settingsDirectory = Path.GetDirectoryName(candidate);
                if (string.IsNullOrWhiteSpace(settingsDirectory))
                {
                    continue;
                }
                Directory.CreateDirectory(settingsDirectory);
                temporaryPath = Path.Combine(
                    settingsDirectory,
                    $".{Path.GetFileName(candidate)}.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(temporaryPath, serializedSettings);
                File.Move(temporaryPath, candidate, overwrite: true);
                temporaryPath = null;
                if (!File.Exists(candidate))
                {
                    throw new IOException($"Windows did not retain {candidate}");
                }
                if (!string.Equals(File.ReadAllText(candidate), serializedSettings, StringComparison.Ordinal))
                {
                    throw new IOException($"Windows did not verify the saved preferences in {candidate}");
                }
                _activeSettingsPath = candidate;
                _settingsLastSavedAt = DateTimeOffset.Now;
                _settingsPersistenceError = string.Empty;
                UpdateSettingsStorageStatus();
                return;
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(candidate)}: {ex.GetType().Name}");
                if (!string.IsNullOrWhiteSpace(temporaryPath))
                {
                    try { File.Delete(temporaryPath); } catch { }
                }
            }
        }

        _settingsPersistenceError = string.Join(" · ", failures);
        UpdateSettingsStorageStatus();
    }

    private void UpdateSettingsStorageStatus()
    {
        if (PreferencesStorageText is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_settingsPersistenceError))
        {
            PreferencesStorageText.Text = "PREFERENCES NOT SAVED";
            PreferencesStorageText.Foreground = (Brush)FindResource("WarningBrush");
            PreferencesStorageText.ToolTip = _settingsPersistenceError;
            return;
        }

        var portable = string.Equals(
            _activeSettingsPath,
            PortableSettingsPath,
            StringComparison.OrdinalIgnoreCase);
        PreferencesStorageText.Text = portable
            ? "PREFERENCES · PORTABLE STORAGE"
            : "PREFERENCES · LOCAL APP DATA";
        PreferencesStorageText.Foreground = (Brush)FindResource("SecondaryTextBrush");
        var savedDetail = _settingsLastSavedAt is { } savedAt
            ? $"\nLast saved {savedAt:HH:mm:ss}"
            : string.Empty;
        PreferencesStorageText.ToolTip = portable
            ? $"Preferences are stored beside this portable Mapper build\n{_activeSettingsPath}{savedDetail}"
            : $"Preferences are stored in your Windows local app data\n{_activeSettingsPath}{savedDetail}";
    }

    private void MaybeShowConsentRosterCoach()
    {
        MaybeShowPressureCoach(
            PressureCoachLogic.ConsentRoster(
                _pressureCoachConsentRosterSeen,
                _isleyRelayState is "live" or "waiting",
                _isleyRelayConsentFiltered,
                _isleyRelayShareWithSteamFriends,
                _isleyRelayExplicitGrantCount,
                _isleyRelayFriendCount),
            () => { _pressureCoachConsentRosterSeen = true; });
    }

    private void MaybeShowPressureCoach(
        PressureCoachPresentation presentation,
        Action markSeen)
    {
        if (!presentation.Show)
        {
            return;
        }

        markSeen();
        SaveSettings();
        _ = ShowHotkeyToastAsync($"{presentation.Title} · {presentation.Detail}", true);
        AddTacticalEvent("COACH", presentation.Title, presentation.Detail);
    }

    // ===== Wave-2 overlay extras sidecar (per-peer voice volume memory and
    // Steam friend groups). Kept in a small "isley-extras.json" file beside the
    // active preferences file so the main MapperSettings schema stays untouched.
    // Load is fully tolerant (corrupt or oversized sidecars fall back to empty),
    // save mirrors the atomic temp-file + verify pattern used by SaveSettings.

    private const long OverlayExtrasMaximumBytes = 256 * 1024;

    private bool _overlayExtrasLoaded;
    private List<VoicePeerVolumeEntry> _overlayVoicePeerVolumes = [];
    private List<SteamFriendGroupEntry> _overlayFriendGroups = [];

    private IEnumerable<string> OverlayExtrasCandidatePaths() =>
        new[] { _activeSettingsPath, PrimarySettingsPath, PortableSettingsPath }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => Path.GetDirectoryName(path))
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(directory => Path.Combine(directory!, "isley-extras.json"));

    private void EnsureOverlayExtrasLoaded()
    {
        if (_overlayExtrasLoaded)
        {
            return;
        }
        _overlayExtrasLoaded = true;

        var candidates = OverlayExtrasCandidatePaths()
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
        foreach (var candidate in candidates)
        {
            try
            {
                if (new FileInfo(candidate).Length > OverlayExtrasMaximumBytes)
                {
                    continue;
                }

                var extras = JsonSerializer.Deserialize<OverlayExtrasSettings>(
                    File.ReadAllText(candidate));
                if (extras is null)
                {
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                _overlayVoicePeerVolumes = VoicePeerVolumeLogic.NormalizeEntries(
                    extras.VoicePeerVolumes,
                    now);
                _overlayFriendGroups = SteamFriendGroupLogic.NormalizeGroups(
                    extras.SteamFriendGroups,
                    _steamFriendWatchlist.Select(entry => entry.Id),
                    now);
                return;
            }
            catch (Exception exception) when (
                exception is JsonException or IOException or UnauthorizedAccessException)
            {
                // A corrupt or unreadable sidecar must never block the overlay;
                // fall through to the next candidate, then to empty extras.
            }
        }
    }

    private void SaveOverlayExtras()
    {
        var extras = new OverlayExtrasSettings
        {
            SchemaVersion = OverlayExtrasSettings.CurrentSchemaVersion,
            VoicePeerVolumes = VoicePeerVolumeLogic.NormalizeEntries(
                _overlayVoicePeerVolumes,
                DateTimeOffset.UtcNow),
            SteamFriendGroups = SteamFriendGroupLogic.NormalizeGroups(
                _overlayFriendGroups,
                validWatchIds: null,
                DateTimeOffset.UtcNow)
        };
        _overlayVoicePeerVolumes = extras.VoicePeerVolumes;
        var serialized = JsonSerializer.Serialize(extras, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        foreach (var candidate in OverlayExtrasCandidatePaths())
        {
            string? temporaryPath = null;
            try
            {
                var directory = Path.GetDirectoryName(candidate);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                Directory.CreateDirectory(directory);
                temporaryPath = Path.Combine(
                    directory,
                    $".{Path.GetFileName(candidate)}.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(temporaryPath, serialized);
                File.Move(temporaryPath, candidate, overwrite: true);
                temporaryPath = null;
                if (!File.Exists(candidate)
                    || !string.Equals(
                        File.ReadAllText(candidate),
                        serialized,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                if (!string.IsNullOrWhiteSpace(temporaryPath))
                {
                    try { File.Delete(temporaryPath); } catch { }
                }
            }
        }
    }

    // ===== Wave-5 relay viewer-stream v2 opt-in (persisted kill switch) =====
    // Default ON. Stored as the additive RelayStreamV2Enabled property of the
    // bounded isley-extras.json sidecar so the main MapperSettings schema stays
    // untouched. Load is fully tolerant (missing, oversized, or corrupt
    // sidecars fall back to ON); save edits the sidecar's JSON node tree so
    // every unrelated sidecar property is preserved, and mirrors the atomic
    // temp-file + verify pattern used by SaveSettings/SaveOverlayExtras.

    private bool _relayStreamV2Enabled = true;
    private bool _relayStreamV2Loaded;

    private bool RelayStreamV2Enabled
    {
        get
        {
            EnsureRelayStreamV2Loaded();
            return _relayStreamV2Enabled;
        }
    }

    private void EnsureRelayStreamV2Loaded()
    {
        if (_relayStreamV2Loaded)
        {
            return;
        }
        _relayStreamV2Loaded = true;

        var candidates = OverlayExtrasCandidatePaths()
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc);
        foreach (var candidate in candidates)
        {
            try
            {
                if (new FileInfo(candidate).Length > OverlayExtrasMaximumBytes)
                {
                    continue;
                }

                var extras = JsonSerializer.Deserialize<OverlayExtrasSettings>(
                    File.ReadAllText(candidate));
                if (extras is null)
                {
                    continue;
                }
                _relayStreamV2Enabled = extras.RelayStreamV2Enabled;
                return;
            }
            catch (Exception exception) when (
                exception is JsonException or IOException or UnauthorizedAccessException)
            {
                // A corrupt or unreadable sidecar must never block the overlay;
                // fall through to the next candidate, then to the default ON.
            }
        }
    }

    private void SetRelayStreamV2Enabled(bool enabled)
    {
        if (_relayStreamV2Loaded && _relayStreamV2Enabled == enabled)
        {
            return;
        }
        _relayStreamV2Enabled = enabled;
        _relayStreamV2Loaded = true;
        SaveRelayStreamV2Preference();
    }

    private void SaveRelayStreamV2Preference()
    {
        foreach (var candidate in OverlayExtrasCandidatePaths())
        {
            string? temporaryPath = null;
            try
            {
                var directory = Path.GetDirectoryName(candidate);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                System.Text.Json.Nodes.JsonObject document;
                if (File.Exists(candidate)
                    && new FileInfo(candidate).Length <= OverlayExtrasMaximumBytes)
                {
                    try
                    {
                        document = System.Text.Json.Nodes.JsonNode
                                       .Parse(File.ReadAllText(candidate)) as
                                   System.Text.Json.Nodes.JsonObject
                                   ?? new System.Text.Json.Nodes.JsonObject();
                    }
                    catch (JsonException)
                    {
                        // A corrupt sidecar has nothing worth preserving;
                        // heal it with a fresh document below.
                        document = new System.Text.Json.Nodes.JsonObject();
                    }
                }
                else
                {
                    document = new System.Text.Json.Nodes.JsonObject();
                }

                document["RelayStreamV2Enabled"] = _relayStreamV2Enabled;
                if (document["SchemaVersion"] is null)
                {
                    document["SchemaVersion"] = OverlayExtrasSettings.CurrentSchemaVersion;
                }
                var serialized = document.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                Directory.CreateDirectory(directory);
                temporaryPath = Path.Combine(
                    directory,
                    $".{Path.GetFileName(candidate)}.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(temporaryPath, serialized);
                File.Move(temporaryPath, candidate, overwrite: true);
                temporaryPath = null;
                if (!File.Exists(candidate)
                    || !string.Equals(
                        File.ReadAllText(candidate),
                        serialized,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                return;
            }
            catch (Exception exception) when (
                exception is JsonException or IOException or UnauthorizedAccessException)
            {
                if (!string.IsNullOrWhiteSpace(temporaryPath))
                {
                    try { File.Delete(temporaryPath); } catch { }
                }
            }
        }
    }
}
