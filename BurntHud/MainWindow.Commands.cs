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
    private void OpenOnboardingTutorial()
    {
        if (_isDocked)
        {
            SetDocked(false);
        }
        if (_clickThrough)
        {
            SetClickThrough(false);
        }
        if (_commandPaletteOpen)
        {
            CloseCommandPalette(returnFocus: false);
        }
        if (_toolsOpen)
        {
            SetToolsOpen(false);
        }

        _onboardingTutorialStepIndex = 0;
        _onboardingTutorialOpen = true;
        OnboardingTutorialLayer.Visibility = Visibility.Visible;
        UpdateOnboardingTutorial();
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (_onboardingTutorialOpen && IsLoaded)
                {
                    Activate();
                    OnboardingNextButton.Focus();
                }
            }));
    }

    private void UpdateOnboardingTutorial()
    {
        if (!_onboardingTutorialOpen)
        {
            return;
        }

        _onboardingTutorialStepIndex = OnboardingTutorialLogic.NormalizeIndex(
            _onboardingTutorialStepIndex);
        var step = OnboardingTutorialLogic.Step(_onboardingTutorialStepIndex);
        OnboardingStepKickerText.Text = step.Kicker;
        OnboardingTitleText.Text = step.Title;
        OnboardingBodyText.Text = step.Body;
        OnboardingTipText.Text = step.Tip;
        OnboardingServerChoicePanel.Visibility = OnboardingTutorialLogic.IsFirst(
            _onboardingTutorialStepIndex)
            ? Visibility.Visible
            : Visibility.Collapsed;
        var serverProfile = ServerSessionLogic.Find(_serverSessionProfileId);
        OnboardingServerModeStatusText.Text =
            $"{serverProfile.SelectorLabel} · {serverProfile.CompatibilityStatus}";
        SetToggleButtonState(
            OnboardingServerLiveMapButton,
            string.Equals(serverProfile.Id, ServerSessionLogic.LiveMapId, StringComparison.Ordinal));
        SetToggleButtonState(
            OnboardingServerOfficialButton,
            string.Equals(serverProfile.Id, ServerSessionLogic.OfficialId, StringComparison.Ordinal));
        SetToggleButtonState(
            OnboardingServerAnyButton,
            string.Equals(serverProfile.Id, ServerSessionLogic.CommunityId, StringComparison.Ordinal));
        OnboardingProgressBar.Maximum = OnboardingTutorialLogic.Steps.Count;
        OnboardingProgressBar.Value = _onboardingTutorialStepIndex + 1;
        OnboardingProgressText.Text = OnboardingTutorialLogic.ProgressLabel(
            _onboardingTutorialStepIndex);
        OnboardingBackButton.IsEnabled = !OnboardingTutorialLogic.IsFirst(
            _onboardingTutorialStepIndex);
        OnboardingNextButton.Content = OnboardingTutorialLogic.NextLabel(
            _onboardingTutorialStepIndex);
        OnboardingNextButton.ToolTip = OnboardingTutorialLogic.IsLast(
            _onboardingTutorialStepIndex)
            ? "Finish the quick start and begin using Isley"
            : "Go to the next tutorial step";
    }

    private void CloseOnboardingTutorial(bool completed)
    {
        if (!_onboardingTutorialOpen)
        {
            return;
        }

        _onboardingTutorialOpen = false;
        OnboardingTutorialLayer.Visibility = Visibility.Collapsed;
        _onboardingTutorialVersionCompleted = Math.Max(
            _onboardingTutorialVersionCompleted,
            OnboardingTutorialLogic.CurrentVersion);
        SaveSettings();
        Focus();
        _ = ShowHotkeyToastAsync(
            completed
                ? "QUICK START COMPLETE"
                : "QUICK START CLOSED · REPLAY FROM APP",
            true);
    }

    private void OnboardingReplayButton_Click(object sender, RoutedEventArgs e) =>
        OpenOnboardingTutorial();

    private void OnboardingCloseButton_Click(object sender, RoutedEventArgs e) =>
        CloseOnboardingTutorial(completed: false);

    private void OnboardingSkipButton_Click(object sender, RoutedEventArgs e) =>
        CloseOnboardingTutorial(completed: false);

    private void OnboardingBackButton_Click(object sender, RoutedEventArgs e)
    {
        _onboardingTutorialStepIndex = OnboardingTutorialLogic.Move(
            _onboardingTutorialStepIndex,
            -1);
        UpdateOnboardingTutorial();
    }

    private void OnboardingNextButton_Click(object sender, RoutedEventArgs e)
    {
        if (OnboardingTutorialLogic.IsLast(_onboardingTutorialStepIndex))
        {
            CloseOnboardingTutorial(completed: true);
            return;
        }

        _onboardingTutorialStepIndex = OnboardingTutorialLogic.Move(
            _onboardingTutorialStepIndex,
            1);
        UpdateOnboardingTutorial();
    }

    private void ToggleCommandPalette()
    {
        if (_commandPaletteOpen)
        {
            CloseCommandPalette();
            return;
        }

        OpenCommandPalette();
    }

    private void OpenCommandPalette()
    {
        if (_isDocked)
        {
            SetDocked(false);
        }
        if (_playFocusEnabled)
        {
            EnterPlayFocusInteraction();
        }
        if (!IsVisible)
        {
            Show();
        }

        Topmost = _alwaysOnTop;
        Activate();
        if (_clickThrough)
        {
            SetClickThrough(false);
        }

        SetToolsOpen(false);
        _commandPaletteOpen = true;
        _commandPaletteResultIndex = 0;
        CommandPaletteBorder.Visibility = Visibility.Visible;
        CommandPaletteInputBox.Text = string.Empty;
        UpdateCommandPaletteResults();
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (!_commandPaletteOpen || !IsLoaded)
                {
                    return;
                }

                Activate();
                CommandPaletteInputBox.Focus();
            }));
    }

    private void CloseCommandPalette(bool returnFocus = true)
    {
        _commandPaletteOpen = false;
        CommandPaletteBorder.Visibility = Visibility.Collapsed;
        CommandPaletteInputBox.Text = string.Empty;
        _commandPaletteMatches.Clear();
        _commandPaletteResultIndex = 0;
        if (returnFocus && IsLoaded)
        {
            Focus();
        }
    }

    private void CommandPaletteButton_Click(object sender, RoutedEventArgs e) => OpenCommandPalette();

    private void CommandPaletteCloseButton_Click(object sender, RoutedEventArgs e) => CloseCommandPalette();

    private void CommandPaletteInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (CommandPaletteResultsPanel is null)
        {
            return;
        }

        _commandPaletteResultIndex = 0;
        UpdateCommandPaletteResults();
    }

    private void CommandPaletteInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseCommandPalette();
            return;
        }

        if (_commandPaletteMatches.Count == 0)
        {
            return;
        }

        if (e.Key == Key.Down)
        {
            e.Handled = true;
            _commandPaletteResultIndex = (_commandPaletteResultIndex + 1) % _commandPaletteMatches.Count;
            UpdateCommandPaletteResults();
            return;
        }

        if (e.Key == Key.Up)
        {
            e.Handled = true;
            _commandPaletteResultIndex = (_commandPaletteResultIndex - 1 + _commandPaletteMatches.Count)
                                         % _commandPaletteMatches.Count;
            UpdateCommandPaletteResults();
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = ExecuteCommandPaletteActionAsync(_commandPaletteMatches[_commandPaletteResultIndex].Id);
        }
    }

    private void CommandPaletteAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string actionId })
        {
            _ = ExecuteCommandPaletteActionAsync(actionId);
        }
    }

    private async void CommandPaletteFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string actionId })
        {
            return;
        }

        var result = CommandQuickAccessLogic.ToggleFavorite(
            _commandFavoriteActionIds,
            actionId,
            CommandPaletteActions.Select(action => action.Id));
        if (result.LimitReached)
        {
            await ShowHotkeyToastAsync(
                $"QUICK ACCESS FULL · KEEP UP TO {CommandQuickAccessLogic.MaximumFavorites}",
                true);
            return;
        }

        if (!result.Changed)
        {
            return;
        }

        _commandFavoriteActionIds.Clear();
        _commandFavoriteActionIds.AddRange(result.Items);
        SaveSettings();
        UpdateCommandPaletteResults();
    }

    private void CommandPaletteClearRecentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_commandRecentActionIds.Count == 0)
        {
            return;
        }

        _commandRecentActionIds.Clear();
        SaveSettings();
        UpdateCommandPaletteResults();
    }

    private void UpdateCommandPaletteResults()
    {
        if (CommandPaletteResultsPanel is null
            || CommandPaletteMatchCountText is null
            || CommandPaletteClearRecentButton is null)
        {
            return;
        }

        var query = Regex.Replace(CommandPaletteInputBox?.Text ?? string.Empty, @"\s+", " ").Trim();
        List<CommandPaletteActionInfo> ranked;
        if (query.Length == 0)
        {
            var defaultActionIds = CommandQuickAccessLogic.BuildDefaultOrder(
                CommandPaletteActions.Select(action => action.Id),
                _commandFavoriteActionIds,
                _commandRecentActionIds,
                maximumResults: 7);
            var actionsById = CommandPaletteActions.ToDictionary(
                action => action.Id,
                StringComparer.OrdinalIgnoreCase);
            ranked = defaultActionIds
                .Where(actionsById.ContainsKey)
                .Select(actionId => actionsById[actionId])
                .ToList();
        }
        else
        {
            ranked = CommandPaletteActions
                .Select(action =>
                {
                    var baseScore = ScoreCommandPaletteAction(action, query);
                    if (baseScore < 0)
                    {
                        return new { Action = action, Score = baseScore };
                    }

                    var favoriteBonus = ContainsCommandAction(_commandFavoriteActionIds, action.Id)
                        ? 30
                        : 0;
                    var recentIndex = IndexOfCommandAction(_commandRecentActionIds, action.Id);
                    var recentBonus = recentIndex >= 0 ? Math.Max(4, 18 - recentIndex * 2) : 0;
                    return new
                    {
                        Action = action,
                        Score = baseScore + favoriteBonus + recentBonus
                    };
                })
                .Where(candidate => candidate.Score >= 0)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Action.Title, StringComparer.OrdinalIgnoreCase)
                .Take(7)
                .Select(candidate => candidate.Action)
                .ToList();
        }

        _commandPaletteMatches.Clear();
        _commandPaletteMatches.AddRange(ranked);
        _commandPaletteResultIndex = _commandPaletteMatches.Count == 0
            ? 0
            : Math.Clamp(_commandPaletteResultIndex, 0, _commandPaletteMatches.Count - 1);
        CommandPaletteResultsPanel.Children.Clear();
        CommandPaletteMatchCountText.Text = _commandPaletteMatches.Count == 0
            ? "NO MATCHING PLAYER TOOL"
            : string.IsNullOrWhiteSpace(query)
                ? $"{_commandPaletteMatches.Count} SHOWN · {_commandFavoriteActionIds.Count} FAVORITE{(_commandFavoriteActionIds.Count == 1 ? string.Empty : "S")}"
                : $"{_commandPaletteMatches.Count} MATCH{(_commandPaletteMatches.Count == 1 ? string.Empty : "ES")}";
        CommandPaletteEmptyText.Visibility = Visibility.Visible;
        CommandPaletteEmptyText.Text = _commandPaletteMatches.Count == 0
            ? "No command matches that search. Try map, life, timer, route, pack, layer, or privacy."
            : query.Length > 0
                ? "Star a result to keep it in Quick Access."
                : _commandFavoriteActionIds.Count == 0
                    ? "Star any tool to keep it at the top."
                    : "Favorites first, then your latest tools.";
        CommandPaletteClearRecentButton.Visibility = _commandRecentActionIds.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        for (var index = 0; index < _commandPaletteMatches.Count; index++)
        {
            var action = _commandPaletteMatches[index];
            var selected = index == _commandPaletteResultIndex;
            var isFavorite = ContainsCommandAction(_commandFavoriteActionIds, action.Id);
            var isRecent = ContainsCommandAction(_commandRecentActionIds, action.Id);
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = action.Title,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            content.Children.Add(new TextBlock
            {
                Text = $"{(isFavorite ? "FAVORITE" : isRecent ? "RECENT" : "PLAYER TOOL")} · {action.Detail}",
                Margin = new Thickness(0, 2, 0, 0),
                FontSize = 8,
                FontWeight = FontWeights.SemiBold,
                Foreground = selected
                    ? new SolidColorBrush(Color.FromRgb(8, 46, 62))
                    : (Brush)FindResource("SecondaryTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var row = new Grid
            {
                Height = 45,
                Margin = new Thickness(0, 0, 0, 4)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var button = new Button
            {
                Style = (Style)FindResource("DrawerButton"),
                Height = 45,
                Margin = new Thickness(0),
                Padding = new Thickness(9, 4, 9, 4),
                Tag = action.Id,
                ToolTip = action.Detail,
                Content = content
            };
            button.Click += CommandPaletteAction_Click;
            SetToggleButtonState(button, selected);
            row.Children.Add(button);

            var favoriteButton = new Button
            {
                Style = (Style)FindResource("ChromeButton"),
                Width = 34,
                Height = 45,
                Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(0),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Tag = action.Id,
                Content = isFavorite ? "★" : "☆",
                ToolTip = isFavorite
                    ? $"Remove {action.Title} from Quick Access"
                    : $"Add {action.Title} to Quick Access"
            };
            Grid.SetColumn(favoriteButton, 1);
            System.Windows.Automation.AutomationProperties.SetName(
                favoriteButton,
                isFavorite
                    ? $"Remove {action.Title} from Quick Access"
                    : $"Add {action.Title} to Quick Access");
            favoriteButton.Click += CommandPaletteFavoriteButton_Click;
            SetToggleButtonState(favoriteButton, isFavorite);
            row.Children.Add(favoriteButton);
            CommandPaletteResultsPanel.Children.Add(row);
        }
    }

    private static bool ContainsCommandAction(IEnumerable<string> actionIds, string actionId) =>
        actionIds.Any(candidate =>
            string.Equals(candidate, actionId, StringComparison.OrdinalIgnoreCase));

    private static int IndexOfCommandAction(IReadOnlyList<string> actionIds, string actionId)
    {
        for (var index = 0; index < actionIds.Count; index++)
        {
            if (string.Equals(actionIds[index], actionId, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private void RecordRecentCommandAction(string actionId)
    {
        var next = CommandQuickAccessLogic.RecordRecent(
            _commandRecentActionIds,
            actionId,
            CommandPaletteActions.Select(action => action.Id));
        if (_commandRecentActionIds.SequenceEqual(next, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _commandRecentActionIds.Clear();
        _commandRecentActionIds.AddRange(next);
        SaveSettings();
    }

    private static int ScoreCommandPaletteAction(CommandPaletteActionInfo action, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            var frequentIndex = Array.FindIndex(CommandPaletteActions, candidate => candidate.Id == action.Id);
            return Math.Max(0, 100 - frequentIndex);
        }

        var normalizedQuery = query.ToLowerInvariant();
        var title = action.Title.ToLowerInvariant();
        var combined = $"{title} {action.Detail} {action.Keywords}".ToLowerInvariant();
        var tokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Any(token => !combined.Contains(token, StringComparison.Ordinal)))
        {
            return FuzzyCommandPaletteScore(title, normalizedQuery);
        }

        var score = 20;
        if (title.Equals(normalizedQuery, StringComparison.Ordinal)) score += 220;
        if (title.StartsWith(normalizedQuery, StringComparison.Ordinal)) score += 140;
        if (title.Contains(normalizedQuery, StringComparison.Ordinal)) score += 80;
        score += tokens.Count(token => title.Split(' ').Any(word => word.StartsWith(token, StringComparison.Ordinal))) * 25;
        score -= Math.Min(20, title.Length / 4);
        return score;
    }

    // Fuzzy fallback so compact queries like "gc" (Growth Clock) or "ssl"
    // (Start Safe Logout) still surface the intended tool.
    private static int FuzzyCommandPaletteScore(string title, string normalizedQuery)
    {
        if (normalizedQuery.Length < 2
            || normalizedQuery.Length > 12
            || normalizedQuery.Contains(' ', StringComparison.Ordinal))
        {
            return -1;
        }

        var initials = string.Concat(
            title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => word[0]));
        if (initials.StartsWith(normalizedQuery, StringComparison.Ordinal))
        {
            return 60 + (initials.Length == normalizedQuery.Length ? 25 : 0);
        }

        var position = 0;
        var gaps = 0;
        foreach (var character in normalizedQuery)
        {
            var found = title.IndexOf(character, position);
            if (found < 0)
            {
                return -1;
            }

            gaps += found - position;
            position = found + 1;
        }

        return Math.Max(1, 25 - Math.Min(20, gaps / 2));
    }

    private async Task ExecuteCommandPaletteActionAsync(string actionId)
    {
        RecordRecentCommandAction(actionId);
        CloseCommandPalette(returnFocus: false);
        if (!LiveMapServicesActive && RequiresLiveMapServices(actionId))
        {
            OpenToolsWorkspace("overlay");
            await ShowHotkeyToastAsync("LIVE MAP MODE REQUIRED", true);
            return;
        }
        switch (actionId)
        {
            case "next-move":
                OpenMapToolsAtSection("next-move");
                break;
            case "recenter":
                await RecenterFromHotkeyAsync();
                break;
            case "death-marker":
                await DropDeathMarkerAsync();
                break;
            case "quick-timer":
                await StartQuickTimerFromHotkeyAsync();
                break;
            case "restart-watch":
                OpenMapToolsAtSection("restart-watch");
                break;
            case "safe-logout":
                if (_safeLogoutGuardState == SafeLogoutGuardState.Ready)
                {
                    await StartSafeLogoutGuardAsync();
                }
                else
                {
                    OpenMapToolsAtSection("safe-logout");
                    await ShowHotkeyToastAsync("LOGOUT GUARD OPEN", true);
                }
                break;
            case "safe-logout-setup":
                OpenMapToolsAtSection("safe-logout");
                await ShowHotkeyToastAsync("SAFE LOGOUT READY · START WHEN THE WARNING SHORTENS", true);
                break;
            case "tactical-brief":
                await CopyTacticalBriefAsync();
                break;
            case "vomit-help":
                await TriggerVomitRecoveryAsync(openPanelWhenStarted: true);
                break;
            case "wound-check":
                _woundCheckExpanded = true;
                _coreVitalsUiSignature = string.Empty;
                UpdateCoreVitals(force: true);
                OpenMapToolsAtSection("core-vitals");
                break;
            case "session-trail":
            case "exploration":
            case "timers":
            case "life-run":
            case "spawn-plan":
            case "zone-brief":
            case "life-journal":
            case "growth-clock":
            case "nest-planner":
            case "survival-assistant":
            case "core-vitals":
            case "field-conditions":
            case "diet-coach":
            case "prime-planner":
            case "elder-lineage":
            case "mutation-planner":
            case "mutation-build-lab":
            case "mutation-unlocks":
            case "tactical-log":
            case "navigation":
            case "resource-finder":
            case "routes":
            case "trip-check":
            case "recovery":
            case "players":
            case "steam-friends":
                OpenMapToolsAtSection(actionId);
                break;
            case "water-crossing":
                OpenMapToolsAtSection("water-crossing");
                await StartWaterCrossingMeasurementAsync(resetBanks: !_measurementActive);
                break;
            case "measure-crossing":
                OpenMapToolsAtSection("water-crossing");
                await StartWaterCrossingMeasurementAsync(resetBanks: true);
                break;
            case "clear-crossing-check":
                ResetWaterCrossingCheck(logEvent: true);
                UpdateMeasurementStatus();
                UpdateWaterCrossingCheck(force: true);
                await ShowHotkeyToastAsync("WATER CROSSING CHECK CLEAR", true);
                break;
            case "shoreline-check":
                await StartShorelineCheckAsync(openSection: true);
                break;
            case "shoreline-check-clear":
                ResetShorelineCheck(logEvent: true);
                UpdateShorelineCheck(force: true);
                UpdateTacticalBrief();
                UpdateNextMove(force: true);
                await ShowHotkeyToastAsync("SHORELINE CHECK COMPLETE · VERIFY IN GAME", true);
                break;
            case "terrain-course":
            {
                OpenMapToolsAtSection("routes");
                var routed = await ExecuteMapperCommandAsync(
                    "window.__isley?.startTerrainCourse() ?? false");
                await ShowHotkeyToastAsync(
                    routed ? "ROAD / TRAIL COURSE ACTIVE" : "CHOOSE A REACHABLE DESTINATION FIRST",
                    routed);
                break;
            }
            case "route-confidence":
                TerrainRouteConfidenceButton_Click(
                    TerrainRouteConfidenceButton,
                    new RoutedEventArgs());
                await ShowHotkeyToastAsync(
                    _terrainRouteConfidenceVisible ? "ROUTE EVIDENCE ON" : "ROUTE EVIDENCE OFF",
                    true);
                break;
            case "terrain-danger":
                OpenMapToolsAtSection("routes");
                TerrainCommunityHazardsButton_Click(
                    TerrainCommunityHazardsButton,
                    new RoutedEventArgs());
                break;
            case "block-passage":
                await ReportBlockedTerrainPassageAsync(showToast: true);
                break;
            case "route-style":
                OpenMapToolsAtSection("routes");
                await CycleTerrainRouteStyleAsync(showToast: true);
                break;
            case "route-gaps":
                OpenMapToolsAtSection("routes");
                await CycleTerrainGapPolicyAsync(showToast: true);
                break;
            case "paste-route":
                OpenMapToolsAtSection("routes");
                await PasteSharedRouteFromClipboardAsync();
                break;
            case "route-clipboard-coords":
                await RouteClipboardCoordinatesAsync(openSection: true);
                break;
            case "escape-route":
                await StartEscapeRouteAsync();
                break;
            case "sound-finder":
                await SetTrackFinderModeAsync(TrackFinderMode.Sound, showToast: false);
                OpenMapToolsAtSection(actionId);
                break;
            case "scent-finder":
                await SetTrackFinderModeAsync(TrackFinderMode.Scent, showToast: false);
                OpenMapToolsAtSection(actionId);
                break;
            case "pins":
            case "alert-zones":
                OpenToolsWorkspace("pins");
                break;
            case "no-go-areas":
                OpenToolsWorkspace("pins");
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(() => NoGoAreasSectionAnchor.BringIntoView()));
                break;
            case "layers":
                OpenToolsWorkspace("layers");
                break;
            case "app":
            case "focus-modes":
                OpenToolsWorkspace("overlay");
                break;
            case "tutorial":
                OpenOnboardingTutorial();
                break;
            case "check-updates":
                OpenToolsWorkspace("hub");
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(() => CheckForIsleyUpdateButton?.BringIntoView()));
                await RefreshIsleyUpdateAsync(userRequested: true);
                break;
            case "aim-guide":
                AimGuideButton_Click(AimGuideButton, new RoutedEventArgs());
                await ShowHotkeyToastAsync(_aimGuideEnabled ? "AIM GUIDE ON" : "AIM GUIDE OFF", true);
                break;
            case "aim-calibration":
                OpenToolsWorkspace("overlay");
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(() => AimCalibrationSectionAnchor.BringIntoView()));
                await ShowHotkeyToastAsync("AIM CALIBRATION OPEN · TEST REPEATED EDGES IN GAME", true);
                break;
            case "aim-growth-sync":
                AimGuideGrowthSyncButton_Click(
                    AimGuideGrowthSyncButton,
                    new RoutedEventArgs());
                await ShowHotkeyToastAsync(
                    _aimGuideGrowthSyncEnabled
                        ? "LIVE AIM GROWTH SYNC ON"
                        : "AIM GROWTH SET TO MANUAL",
                    true);
                break;
            case "vitals-hud":
                VitalsHudButton_Click(VitalsHudButton, new RoutedEventArgs());
                await ShowHotkeyToastAsync(_vitalsHudVisible ? "CORE VITALS HUD ON" : "CORE VITALS HUD OFF", true);
                break;
            case "dock-overlay":
                SetDocked(true);
                break;
            case "patch-watch":
                OpenPatchWatch();
                break;
            case "focus-combat":
                await ApplyFocusModeAsync("combat");
                break;
            case "focus-nest":
                await ApplyFocusModeAsync("nest");
                break;
            case "hotkeys":
                OpenHotkeyStudio();
                break;
            case "server-session":
                OpenToolsWorkspace("overlay");
                break;
            case "private-server-connect":
                await ConnectPrivateServerFromClipboardAsync();
                break;
            case "map-pins-share":
                await CopyPinShareCodeAsync();
                break;
            case "map-pins-import":
                await ImportPinShareCodeFromClipboardAsync();
                break;
            case "map-undo-clear":
                await UndoMapClearAsync();
                break;
            case "map-routes-share":
                await CopyRouteShareCodeAsync();
                break;
            case "map-routes-import":
                await ImportRouteShareCodeFromClipboardAsync();
                break;
            case "map-nogo-share":
                await CopyNoGoShareCodeAsync();
                break;
            case "map-nogo-import":
                await ImportNoGoShareCodeFromClipboardAsync();
                break;
            case "map-route-replan":
                await ToggleRouteAutoReplanAsync();
                break;
            case "encounter-history":
                await CopyEncounterHistoryAsync();
                break;
            case "universal-coordinates":
                ToggleUniversalCoordinateCapture();
                if (LiveMapServicesActive)
                {
                    OpenMapToolsAtSection("terrain-probe");
                }
                await ShowHotkeyToastAsync(
                    _universalCoordinateCaptureEnabled ? "PLAYER SYNC ON" : "PLAYER SYNC OFF",
                    true);
                break;
            case "save-slope-avoidance":
                if (LiveMapServicesActive)
                {
                    OpenMapToolsAtSection("terrain-probe");
                }
                await SaveMeasuredSlopeAvoidanceAsync(showToast: true);
                break;
            case "community-server-watch":
                OpenToolsWorkspace("overlay");
                if (!CommunitySessionActive)
                {
                    await ShowHotkeyToastAsync("SELECT COMMUNITY SESSION", true);
                }
                break;
            case "voice-chat":
            case "voice-quality":
                OpenToolsWorkspace("voice");
                break;
            case "join-voice-room":
                OpenToolsWorkspace("voice");
                await PasteVoiceInviteFromClipboardAsync(showToast: true);
                break;
            case "voice-share-route":
                await ShareCurrentRouteToVoiceAsync(showToast: true);
                break;
            case "field-guide":
                OpenToolsWorkspace("guide");
                break;
            case "combat-guide":
                OpenGuideCombatBrief();
                break;
            case "fight-check":
                OpenFightCheck();
                break;
            case "sighting-check":
                OpenManualSighting();
                break;
            case "play-focus":
                TogglePlayFocus();
                await ShowHotkeyToastAsync(
                    _playFocusEnabled ? "PLAY FOCUS ON" : "PLAY FOCUS OFF",
                    true);
                break;
            case "server-status":
                OpenToolsWorkspace("overlay");
                await RefreshServerStatusAsync(userInitiated: true);
                break;
            case "copy-server-address":
                await CopyServerAddressAsync();
                break;
            case "map-lighting":
                CycleMapLightMode();
                await ShowHotkeyToastAsync(
                    $"MAP LIGHT · {_mapLightModeLabels[_mapLightModeIndex].ToUpperInvariant()}",
                    true);
                break;
            case "hud-detail":
                CycleHudDetailMode();
                await ShowHotkeyToastAsync(
                    $"HUD DETAIL · {_hudDetailModeLabels[_hudDetailModeIndex].ToUpperInvariant()}",
                    true);
                break;
            case "hud-surfaces":
                OpenToolsWorkspace("overlay");
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(() => HudSurfacesSectionAnchor.BringIntoView()));
                await ShowHotkeyToastAsync("HUD SURFACES OPEN", true);
                break;
            case "quick-keys":
                HudQuickKeysButton_Click(HudQuickKeysButton, new RoutedEventArgs());
                await ShowHotkeyToastAsync(
                    _quickKeysHudVisible ? "QUICK KEYS ON" : "QUICK KEYS OFF",
                    true);
                break;
            case "smart-hud":
                ToggleSmartHud();
                await ShowHotkeyToastAsync(
                    _smartHudEnabled ? "SMART HUD · ON" : "SMART HUD · OFF",
                    true);
                break;
            case "lite-mode":
                await SetLiteModeAsync(!_liteModeEnabled);
                await ShowHotkeyToastAsync(
                    _liteModeEnabled ? "LITE MODE ON" : "LITE MODE OFF",
                    true);
                break;
            case "hud-dock":
                _hudDockMirrored = !_hudDockMirrored;
                _hudDockUiSignature = string.Empty;
                UpdateHudDockLayout(animate: true);
                SaveSettings();
                await ShowHotkeyToastAsync(
                    _hudDockMirrored ? "HUD DOCK · INTEL LEFT" : "HUD DOCK · INTEL RIGHT",
                    true);
                break;
            case "marker-style":
                MarkerStyleButton_Click(MarkerStyleButton, new RoutedEventArgs());
                await ShowHotkeyToastAsync(
                    $"MARKERS · {_markerStyleLabels[_markerStyleIndex].ToUpperInvariant()}",
                    true);
                break;
            case "pack-center":
            {
                var stopping = _packRouteActive;
                var routed = await ExecuteMapperCommandAsync(stopping
                    ? "window.__isley?.clearWaypoint() ?? false"
                    : "window.__isley?.routeToPackCenter() ?? false");
                await ShowHotkeyToastAsync(
                    routed ? stopping ? "PACK ROUTE STOPPED" : "FOLLOWING PACK CENTER" : "PACK CENTER UNAVAILABLE",
                    routed);
                break;
            }
            case "pack-outlier":
            {
                var stopping = _packOutlierRouteActive;
                var routed = await ExecuteMapperCommandAsync(stopping
                    ? "window.__isley?.clearWaypoint() ?? false"
                    : "window.__isley?.routeToPackOutlier() ?? false");
                await ShowHotkeyToastAsync(
                    routed
                        ? stopping ? "PACK OUTLIER ROUTE STOPPED" : "FOLLOWING PACK OUTLIER"
                        : "PACK OUTLIER UNAVAILABLE",
                    routed);
                break;
            }
            case "pack-alert":
            {
                PackSpreadAlertButton_Click(PackSpreadAlertButton, new RoutedEventArgs());
                var packAlertDistance = _packSpreadAlertDistances[_packSpreadAlertIndex];
                await ShowHotkeyToastAsync(
                    packAlertDistance <= 0 ? "PACK SPREAD ALERT OFF" : $"PACK ALERT · {packAlertDistance:0} MU",
                    true);
                break;
            }
            case "encounter-hud":
                EncounterHudButton_Click(EncounterHudButton, new RoutedEventArgs());
                await ShowHotkeyToastAsync(
                    _encounterHudVisible ? "ENCOUNTER HUD ON" : "ENCOUNTER HUD OFF",
                    true);
                break;
            case "encounter-alert":
            {
                EncounterAlertButton_Click(EncounterAlertButton, new RoutedEventArgs());
                var encounterAlertDistance = _encounterAlertDistances[_encounterAlertIndex];
                await ShowHotkeyToastAsync(
                    encounterAlertDistance <= 0
                        ? "ENCOUNTER ALERT OFF"
                        : $"ENCOUNTER ALERT · {encounterAlertDistance:0} MU",
                    true);
                break;
            }
            case "encounter-memory":
            {
                EncounterMemoryButton_Click(EncounterMemoryButton, new RoutedEventArgs());
                var encounterMemorySeconds = _encounterMemoryDurations[_encounterMemoryIndex];
                await ShowHotkeyToastAsync(
                    encounterMemorySeconds <= 0
                        ? "LAST-SEEN MEMORY OFF"
                        : $"LAST-SEEN MEMORY · {encounterMemorySeconds / 60}M",
                    true);
                break;
            }
            case "clear-encounter-memory":
            {
                var cleared = await ExecuteMapperCommandAsync(
                    "window.__isley?.clearEncounterMemory() ?? false");
                if (cleared)
                {
                    _encounterMemoryTrackCount = 0;
                    _rememberedEncounterCount = 0;
                    _rememberedEncounterNewestAgeMs = null;
                    _nearestRememberedEncounterDistance = null;
                    _nearestRememberedEncounterBearing = null;
                    _nearestRememberedEncounterCardinal = string.Empty;
                    UpdateEncounterAwareness();
                }
                await ShowHotkeyToastAsync(
                    cleared ? "RECENT CONTACTS CLEARED" : "NO RECENT CONTACTS",
                    true);
                break;
            }
            case "hub":
                OpenToolsWorkspace("hub");
                break;
            case "heading":
                HeadingModeButton_Click(HeadingModeButton, new RoutedEventArgs());
                await ShowHotkeyToastAsync(_headingUp ? "HEADING-UP MODE" : "NORTH-UP MODE", true);
                break;
            case "look-ahead":
                FollowFramingButton_Click(FollowFramingButton, new RoutedEventArgs());
                await ShowHotkeyToastAsync(
                    _lookAheadEnabled ? "LOOK-AHEAD FRAMING" : "CENTERED FRAMING",
                    true);
                break;
            case "smart-zoom":
                SmartZoomButton_Click(SmartZoomButton, new RoutedEventArgs());
                await ShowHotkeyToastAsync(
                    _smartZoomEnabled ? "SMART ZOOM ON" : "SMART ZOOM OFF",
                    true);
                break;
            case "grid":
                MapGridButton_Click(MapGridButton, new RoutedEventArgs());
                await ShowHotkeyToastAsync(_mapGridVisible ? "TACTICAL GRID ON" : "TACTICAL GRID OFF", true);
                break;
            case "place-labels":
                LandmarkLabelDensityButton_Click(LandmarkLabelDensityButton, new RoutedEventArgs());
                await ShowHotkeyToastAsync(
                    $"PLACE LABELS {_landmarkLabelDensityModes[_landmarkLabelDensityIndex].ToUpperInvariant()}",
                    true);
                break;
            case "rings":
                RangeRingsButton_Click(RangeRingsButton, new RoutedEventArgs());
                if (_rangeRingModeIndex <= 0)
                {
                    await ShowHotkeyToastAsync("RANGE RINGS OFF", true);
                }
                else
                {
                    var ringMode = _rangeRingModes[_rangeRingModeIndex];
                    await ShowHotkeyToastAsync(
                        $"RANGE RINGS {ringMode.Inner}/{ringMode.Outer} MU",
                        true);
                }
                break;
            case "waypoint":
                WaypointButton_Click(WaypointButton, new RoutedEventArgs());
                await ShowHotkeyToastAsync(_waypointArmed ? "SELECT WAYPOINT ON MAP" : "WAYPOINT CLEARED", true);
                break;
            case "streamer":
                StreamerModeButton_Click(StreamerModeButton, new RoutedEventArgs());
                await ShowHotkeyToastAsync(_streamerMode ? "STREAMER MODE ON" : "STREAMER MODE OFF", true);
                break;
            case "reload":
                RefreshButton_Click(this, new RoutedEventArgs());
                await ShowHotkeyToastAsync("LIVE MAP RELOADING", true);
                break;
            case "preset-navigation":
            case "preset-survival":
            {
                var preset = actionId == "preset-navigation" ? "navigation" : "survival";
                var applied = await ExecuteMapperCommandAsync(
                    $"window.__isley?.applyLayerPreset('{preset}') ?? false");
                await ShowHotkeyToastAsync(
                    applied ? $"{preset.ToUpperInvariant()} LAYERS APPLIED" : "MAP LAYERS UNAVAILABLE",
                    applied);
                break;
            }
            case "layout-profiles":
                OpenLayoutProfilesSection();
                await ShowHotkeyToastAsync("LAYOUT PROFILES OPEN", true);
                break;
            case "layout-profile-save":
                SaveLayoutProfileFromCommand();
                break;
            case "capture-sound":
                await ToggleCaptureSoundAsync();
                break;
            case "diagnostics-export":
                await ExportDiagnosticsBundleAsync();
                break;
            case "nest-timer-alerts":
                await CycleNestTimerAlertPresetAsync();
                break;
            case "server-rate-preset-apply":
                await ApplyNextServerRatePresetAsync();
                break;
            case "server-rate-preset-save":
                await SaveCustomServerRatePresetAsync();
                break;
        }
    }

    private void OpenToolsWorkspace(string section)
    {
        if (_clickThrough)
        {
            SetClickThrough(false);
        }
        SetToolsOpen(true);
        ShowToolsSection(section);
        ToolsScrollViewer.ScrollToTop();
    }

    private void OpenPatchWatch()
    {
        OpenToolsWorkspace("overlay");
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                var offset = PatchWatchSectionAnchor.TranslatePoint(
                    new Point(0, 0),
                    OverlayToolsPanel).Y;
                ToolsScrollViewer.ScrollToVerticalOffset(Math.Max(0, offset - 4));
            }));
    }

    private void OpenMapToolsAtSection(string section)
    {
        if (_clickThrough)
        {
            SetClickThrough(false);
        }
        SetToolsOpen(true);
        ShowToolsSection("map");
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => JumpToMapToolsSection(section)));
    }

    private void MapSectionJumpButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string section })
        {
            JumpToMapToolsSection(section);
        }
    }

    private void JumpToMapToolsSection(string section)
    {
        FrameworkElement anchor = section switch
        {
            "next-move" => NextMoveSectionAnchor,
            "exploration" => ExplorationSectionAnchor,
            "session-trail" => SessionActivitySectionAnchor,
            "timers" => SessionActivitySectionAnchor,
            "restart-watch" => RestartWatchSectionAnchor,
            "safe-logout" => SafeLogoutSectionAnchor,
            "life-run" => LifeRunSectionAnchor,
            "spawn-plan" => _lifeRunActive ? SpawnPlanAnchor : LifeRunSectionAnchor,
            "zone-brief" => _lifeRunActive ? ZoneBriefAnchor : LifeRunSectionAnchor,
            "life-journal" => LifeRunHistorySectionAnchor,
            "survival-assistant" => SurvivalAssistantSectionAnchor,
            "core-vitals" => CoreVitalsSectionAnchor,
            "shoreline-check" => ShorelineCheckSectionAnchor,
            "field-conditions" => FieldConditionsSectionAnchor,
            "diet-coach" => _lifeRunActive ? DietCoachSectionAnchor : LifeRunSectionAnchor,
            "growth-clock" => _lifeRunActive ? GrowthClockSectionAnchor : LifeRunSectionAnchor,
            "nest-planner" => _lifeRunActive ? NestPlannerSectionAnchor : LifeRunSectionAnchor,
            "prime-planner" => LifeRunSectionAnchor,
            "elder-lineage" => _lifeRunActive ? ElderLineageSectionAnchor : LifeRunSectionAnchor,
            "mutation-planner" => _lifeRunActive ? MutationPlannerSectionAnchor : LifeRunSectionAnchor,
            "mutation-build-lab" => _lifeRunActive ? MutationBuildSectionAnchor : LifeRunSectionAnchor,
            "mutation-unlocks" => _lifeRunActive ? MutationUnlockSectionAnchor : LifeRunSectionAnchor,
            "tactical-log" => TacticalLogSectionAnchor,
            "sound-finder" => SoundFinderSectionAnchor,
            "scent-finder" => SoundFinderSectionAnchor,
            "resource-finder" => ResourceFinderSectionAnchor,
            "trip-check" => TripReadinessSectionAnchor,
            "water-crossing" => WaterCrossingSectionAnchor,
            "routes" => RouteSectionAnchor,
            "terrain-probe" => TerrainProbeSectionAnchor,
            "recovery" => RecoverySectionAnchor,
            "sighting-check" => ManualSightingSectionAnchor,
            "players" => PlayersSectionAnchor,
            "steam-friends" => SteamFriendsSectionAnchor,
            _ => NavigationSectionAnchor
        };
        _mapToolsJumpSection = section is "session-trail" or "restart-watch" or "safe-logout" or "exploration" or "tactical-log" or "survival-assistant" or "core-vitals" or "shoreline-check" or "field-conditions" or "life-run" or "spawn-plan" or "zone-brief" or "life-journal" or "diet-coach" or "growth-clock" or "nest-planner" or "prime-planner" or "elder-lineage" or "mutation-planner" or "mutation-build-lab" or "mutation-unlocks"
            ? "timers"
            : section is "trip-check" or "water-crossing"
                ? "routes"
            : section is "sighting-check"
                ? "players"
                : section is "timers" or "routes" or "terrain-probe" or "recovery" or "players" or "steam-friends"
                    ? section == "terrain-probe" ? "routes" : section
            : "navigation";
        UpdateMapSectionJumpState();
        var offset = anchor.TranslatePoint(new Point(0, 0), MapToolsPanel).Y;
        ToolsScrollViewer.ScrollToVerticalOffset(Math.Max(0, offset - 4));
    }

    private void ToolsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_toolsSection != "map" || MapToolsPanel.Visibility != Visibility.Visible || !IsLoaded)
        {
            return;
        }

        var probe = e.VerticalOffset + 18;
        var sections = new (string Name, FrameworkElement Anchor)[]
        {
            ("navigation", NavigationSectionAnchor),
            ("timers", SessionActivitySectionAnchor),
            ("routes", RouteSectionAnchor),
            ("recovery", RecoverySectionAnchor),
            ("players", PlayersSectionAnchor)
        };
        var active = "navigation";
        foreach (var section in sections)
        {
            var y = section.Anchor.TranslatePoint(new Point(0, 0), MapToolsPanel).Y;
            if (y <= probe)
            {
                active = section.Name;
            }
        }

        if (!string.Equals(active, _mapToolsJumpSection, StringComparison.Ordinal))
        {
            _mapToolsJumpSection = active;
            UpdateMapSectionJumpState();
        }
    }

    private void UpdateMapSectionJumpState()
    {
        if (MapJumpNavigationButton is null)
        {
            return;
        }
        SetToggleButtonState(MapJumpNavigationButton, _mapToolsJumpSection == "navigation");
        SetToggleButtonState(MapJumpTimersButton, _mapToolsJumpSection == "timers");
        SetToggleButtonState(MapJumpRoutesButton, _mapToolsJumpSection == "routes");
        SetToggleButtonState(MapJumpRecoveryButton, _mapToolsJumpSection == "recovery");
        SetToggleButtonState(
            MapJumpPlayersButton,
            _mapToolsJumpSection is "players" or "steam-friends");
    }

    private void ToolsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_commandPaletteOpen)
        {
            CloseCommandPalette(returnFocus: false);
        }
        SetToolsOpen(!_toolsOpen);
    }

    private void MapToolsTabButton_Click(object sender, RoutedEventArgs e) => ShowToolsSection("map");

    private void PinsToolsTabButton_Click(object sender, RoutedEventArgs e) => ShowToolsSection("pins");

    private void LayerToolsTabButton_Click(object sender, RoutedEventArgs e) => ShowToolsSection("layers");

    private void OverlayToolsTabButton_Click(object sender, RoutedEventArgs e) => ShowToolsSection("overlay");

    private void GuideToolsTabButton_Click(object sender, RoutedEventArgs e) => ShowToolsSection("guide");

    private void HubToolsTabButton_Click(object sender, RoutedEventArgs e) => ShowToolsSection("hub");

    private void ShowToolsSection(string section)
    {
        _toolsSection = section;
        if (!string.Equals(section, "pins", StringComparison.Ordinal))
        {
            _destinationSearchRevision++;
            ClearPlaceSuggestions();
        }
        UpdateToolsSection();
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (IsLoaded && ToolsScrollViewer is not null)
                {
                    ToolsScrollViewer.ScrollToTop();
                }
            }));
    }

    private void UpdateToolsSection()
    {
        if (MapToolsPanel is null)
        {
            return;
        }

        MapToolsPanel.Visibility = _toolsSection == "map" ? Visibility.Visible : Visibility.Collapsed;
        PinsToolsPanel.Visibility = _toolsSection == "pins" ? Visibility.Visible : Visibility.Collapsed;
        LayerToolsPanel.Visibility = _toolsSection == "layers" ? Visibility.Visible : Visibility.Collapsed;
        OverlayToolsPanel.Visibility = _toolsSection == "overlay" ? Visibility.Visible : Visibility.Collapsed;
        VoiceToolsPanel.Visibility = _toolsSection == "voice" ? Visibility.Visible : Visibility.Collapsed;
        GuideToolsPanel.Visibility = _toolsSection == "guide" ? Visibility.Visible : Visibility.Collapsed;
        HubToolsPanel.Visibility = _toolsSection == "hub" ? Visibility.Visible : Visibility.Collapsed;
        var responsivePresentation = CurrentResponsiveOverlayPresentation();
        MapSectionJumpBar.Visibility = _toolsSection == "map" && responsivePresentation.ShowMapSectionJumpBar
            ? Visibility.Visible
            : Visibility.Collapsed;
        SetToggleButtonState(MapToolsTabButton, _toolsSection == "map");
        SetToggleButtonState(PinsToolsTabButton, _toolsSection == "pins");
        SetToggleButtonState(LayerToolsTabButton, _toolsSection == "layers");
        SetToggleButtonState(OverlayToolsTabButton, _toolsSection == "overlay");
        SetToggleButtonState(VoiceToolsTabButton, _toolsSection == "voice");
        SetToggleButtonState(GuideToolsTabButton, _toolsSection == "guide");
        SetToggleButtonState(HubToolsTabButton, _toolsSection == "hub");
        var sectionGuide = _toolsSection switch
        {
            "map" => (
                "PLAY & NAVIGATE",
                "Follow yourself, plan routes, check survival, and manage your pack."),
            "pins" => (
                "MARKERS & DESTINATIONS",
                "Save places, build routes, and mark danger without sharing private coordinates."),
            "layers" => (
                "MAP VIEW",
                "Choose a simple preset or turn individual map details on and off."),
            "overlay" => (
                "OVERLAY SETTINGS",
                "Choose your server mode, privacy, size, input behavior, and visual comfort."),
            "voice" => (
                "PROXIMITY VOICE",
                "Set up push-to-talk, your microphone, and private proximity voice status."),
            "guide" => (
                "FIELD GUIDE",
                "Get species-specific controls, survival help, combat checks, and current references."),
            "hub" => (
                "MORE PLAYER TOOLS",
                "Open companion tools, updates, support, and links that do not belong on the map."),
            _ => (
                "ISLEY TOOLS",
                "Choose what you want to do.")
        };
        ToolsSectionHeadingText.Text = sectionGuide.Item1;
        ToolsSectionHelpText.Text = sectionGuide.Item2;
        if (_toolsSection == "guide")
        {
            UpdateFieldGuide(force: true);
        }
        else if (_toolsSection == "overlay")
        {
            UpdateOfficialPatchPresentation();
        }
        UpdateMapSectionJumpState();
    }

    private void SetToolsOpen(bool open)
    {
        if (open && _isDocked)
        {
            SetDocked(false);
        }
        if (open && _commandPaletteOpen)
        {
            CloseCommandPalette(returnFocus: false);
        }
        _toolsOpen = open;
        if (!open)
        {
            _destinationSearchRevision++;
            ClearPlaceSuggestions();
        }
        ToolsDrawer.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ToolsButton.Content = open ? "CLOSE" : "TOOLS";
        SetToggleButtonState(ToolsButton, open);
        if (open)
        {
            UpdateToolsSection();
            HelpTipBorder.Visibility = Visibility.Collapsed;
        }
    }
}
