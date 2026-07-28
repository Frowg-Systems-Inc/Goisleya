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
    private nint WindowMessageHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WmNcHitTest && _overlayLocked)
        {
            handled = true;
            return SelectiveLockHitTest.ContainsPackedScreenPoint(LockButton, lParam)
                ? new nint(NativeMethods.HtClient)
                : new nint(NativeMethods.HtTransparent);
        }

        if (message != WmHotKey)
        {
            return 0;
        }

        var definition = HotkeyBindingLogic.FindByMessageId(wParam.ToInt32());
        if (definition is null)
        {
            return 0;
        }

        if (!string.IsNullOrEmpty(_hotkeyCaptureActionId))
        {
            if (string.Equals(_hotkeyCaptureActionId, definition.Id, StringComparison.Ordinal))
            {
                _hotkeyCaptureActionId = string.Empty;
                _hotkeyCaptureMessage = "SHORTCUT UNCHANGED";
            }
            else
            {
                _hotkeyCaptureMessage = $"ALREADY USED BY {definition.CompactLabel}";
            }
            _hotkeyStudioUiSignature = string.Empty;
            UpdateHotkeyStudio(force: true);
            UpdateHotkeyStatus();
            handled = true;
            return 0;
        }

        ExecuteHotkeyAction(definition.Id);
        handled = true;
        return 0;
    }

    private void ExecuteHotkeyAction(string actionId)
    {
        switch (actionId)
        {
            case HotkeyBindingLogic.VisibilityId:
                ToggleVisibility();
                break;
            case HotkeyBindingLogic.InteractionId:
                ToggleInteractionMode();
                break;
            case HotkeyBindingLogic.RecenterId:
                _ = RecenterFromHotkeyAsync();
                break;
            case HotkeyBindingLogic.TimedDangerId:
                _ = DropTimedDangerFromHotkeyAsync();
                break;
            case HotkeyBindingLogic.QuickTimerId:
                _ = StartQuickTimerFromHotkeyAsync();
                break;
            case HotkeyBindingLogic.CommandPaletteId:
                ToggleCommandPalette();
                break;
            case HotkeyBindingLogic.DeathMarkerId:
                _ = DropDeathMarkerAsync();
                break;
            case HotkeyBindingLogic.TrackBearingId:
                _ = CaptureTrackBearingAsync();
                break;
            case HotkeyBindingLogic.VomitRecoveryId:
                _ = TriggerVomitRecoveryAsync(openPanelWhenStarted: false);
                break;
        }
    }

    private void RestoreHotkeyBindings(IEnumerable<HotkeyBindingSettings>? savedBindings)
    {
        _hotkeyBindings.Clear();
        foreach (var binding in HotkeyBindingLogic.Normalize(savedBindings))
        {
            _hotkeyBindings[binding.ActionId] = binding;
        }
        _hotkeyCaptureActionId = string.Empty;
        _hotkeyCaptureMessage = string.Empty;
        _hotkeyStudioUiSignature = string.Empty;
    }

    private HotkeyBinding CurrentHotkeyBinding(string actionId) =>
        _hotkeyBindings.TryGetValue(actionId, out var binding)
            ? binding
            : HotkeyBindingLogic.DefaultBinding(
                HotkeyBindingLogic.Find(actionId)
                ?? throw new ArgumentOutOfRangeException(nameof(actionId)));

    private bool IsHotkeyRegistered(string actionId) =>
        _hotkeyRegistrationStates.TryGetValue(actionId, out var registered) && registered;

    private bool RegisterHotkey(HotkeyActionDefinition definition, HotkeyBinding binding)
    {
        if (_windowHandle == 0 || !binding.Enabled)
        {
            return false;
        }

        return NativeMethods.RegisterHotKey(
            _windowHandle,
            definition.MessageId,
            binding.Modifiers | NativeMethods.ModNoRepeat,
            binding.VirtualKey);
    }

    private void RegisterAllHotkeys()
    {
        _hotkeyRegistrationStates.Clear();
        foreach (var definition in HotkeyBindingLogic.Definitions)
        {
            var binding = CurrentHotkeyBinding(definition.Id);
            _hotkeyRegistrationStates[definition.Id] =
                binding.Enabled && RegisterHotkey(definition, binding);
        }
    }

    private void UnregisterAllHotkeys()
    {
        if (_windowHandle == 0)
        {
            return;
        }

        foreach (var definition in HotkeyBindingLogic.Definitions)
        {
            if (IsHotkeyRegistered(definition.Id))
            {
                NativeMethods.UnregisterHotKey(_windowHandle, definition.MessageId);
            }
        }
        _hotkeyRegistrationStates.Clear();
    }

    private void UpdateHotkeyStatus()
    {
        if (InteractionStatusText is null)
        {
            return;
        }

        var enabledDefinitions = HotkeyBindingLogic.Definitions
            .Where(definition => CurrentHotkeyBinding(definition.Id).Enabled)
            .ToArray();
        var registeredCount = enabledDefinitions.Count(definition => IsHotkeyRegistered(definition.Id));
        var allRegistered = registeredCount == enabledDefinitions.Length;
        InteractionStatusText.Text = ResponsiveLayoutLogic.FooterHotkeyStatus(
            CurrentResponsiveOverlayPresentation(),
            registeredCount,
            enabledDefinitions.Length,
            allRegistered,
            _clickThrough,
            !string.IsNullOrEmpty(_hotkeyCaptureActionId),
            HotkeyBindingLogic.Format(CurrentHotkeyBinding(HotkeyBindingLogic.InteractionId)));
        InteractionStatusText.Foreground = !allRegistered
                                           || _clickThrough
                                           || !string.IsNullOrEmpty(_hotkeyCaptureActionId)
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("SecondaryTextBrush");
        InteractionStatusText.ToolTip = "Global shortcuts\n" + string.Join('\n',
            HotkeyBindingLogic.Definitions.Select(definition =>
            {
                var binding = CurrentHotkeyBinding(definition.Id);
                var state = !binding.Enabled
                    ? "off"
                    : IsHotkeyRegistered(definition.Id) ? "ready" : "in use";
                return $"{HotkeyBindingLogic.Format(binding)} · {definition.Description} · {state}";
            }));
        if (_overlayLocked)
        {
            InteractionStatusText.Text = "LOCKED · UNLOCK ONLY";
            InteractionStatusText.Foreground = (Brush)FindResource("AccentBrush");
            InteractionStatusText.ToolTip =
                "All pointer input passes through Isley except the unlock button.\n"
                + InteractionStatusText.ToolTip;
        }

        if (HotkeyRecoveryText is not null)
        {
            HotkeyRecoveryText.Text =
                $"Recovery: {HotkeyBindingLogic.Format(CurrentHotkeyBinding(HotkeyBindingLogic.InteractionId))} interact · " +
                $"{HotkeyBindingLogic.Format(CurrentHotkeyBinding(HotkeyBindingLogic.VisibilityId))} show / hide · " +
                $"{HotkeyBindingLogic.Format(CurrentHotkeyBinding(HotkeyBindingLogic.VomitRecoveryId))} sickness";
        }
    }

    private void UpdateHotkeyStudio(bool force = false)
    {
        if (HotkeyBindingListPanel is null
            || HotkeyStudioStatusText is null
            || HotkeyCaptureHintText is null)
        {
            return;
        }

        var signature = string.Join('|',
            _hotkeyCaptureActionId,
            _hotkeyCaptureMessage,
            string.Join(';', HotkeyBindingLogic.Definitions.Select(definition =>
            {
                var binding = CurrentHotkeyBinding(definition.Id);
                return $"{definition.Id}:{HotkeyBindingLogic.Signature(binding)}:{binding.Enabled}:" +
                       $"{IsHotkeyRegistered(definition.Id)}";
            })));
        if (!force && string.Equals(signature, _hotkeyStudioUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _hotkeyStudioUiSignature = signature;

        var captureDefinition = HotkeyBindingLogic.Find(_hotkeyCaptureActionId);
        var enabledDefinitions = HotkeyBindingLogic.Definitions
            .Where(definition => CurrentHotkeyBinding(definition.Id).Enabled)
            .ToArray();
        var conflictCount = enabledDefinitions.Count(definition => !IsHotkeyRegistered(definition.Id));
        var disabledCount = HotkeyBindingLogic.Definitions.Length - enabledDefinitions.Length;
        HotkeyStudioStatusText.Text = captureDefinition is not null
            ? $"LISTENING · {captureDefinition.Label.ToUpperInvariant()}"
            : conflictCount > 0
                ? $"{conflictCount} SHORTCUT{(conflictCount == 1 ? string.Empty : "S")} NEED A NEW BINDING"
                : $"{enabledDefinitions.Length} READY" +
                  (disabledCount > 0 ? $" · {disabledCount} OPTIONAL OFF" : " · NO CONFLICTS");
        HotkeyStudioStatusText.Foreground = captureDefinition is not null
            ? (Brush)FindResource("AccentBrush")
            : conflictCount > 0
                ? (Brush)FindResource("WarningBrush")
                : (Brush)FindResource("SecondaryTextBrush");
        HotkeyCaptureHintText.Text = captureDefinition is not null
            ? string.IsNullOrEmpty(_hotkeyCaptureMessage)
                ? "Press a letter, number, or F1-F12 with Ctrl or Alt · Esc cancels" +
                  (captureDefinition.Required ? " · recovery shortcut stays enabled" : " · Backspace turns it off")
                : _hotkeyCaptureMessage
            : string.IsNullOrEmpty(_hotkeyCaptureMessage)
                ? "Click a binding, then press the replacement. Changes apply immediately."
                : _hotkeyCaptureMessage;
        HotkeyCaptureHintText.Foreground = !string.IsNullOrEmpty(_hotkeyCaptureMessage)
                                           && !_hotkeyCaptureMessage.Contains("SAVED", StringComparison.Ordinal)
                                           && !_hotkeyCaptureMessage.Contains("UNCHANGED", StringComparison.Ordinal)
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("SecondaryTextBrush");

        HotkeyBindingListPanel.Children.Clear();
        for (var index = 0; index < HotkeyBindingLogic.Definitions.Length; index++)
        {
            var definition = HotkeyBindingLogic.Definitions[index];
            var binding = CurrentHotkeyBinding(definition.Id);
            var registered = IsHotkeyRegistered(definition.Id);
            var listening = string.Equals(
                definition.Id, _hotkeyCaptureActionId, StringComparison.Ordinal);
            var row = new StackPanel { Margin = new Thickness(1, 4, 1, 4) };
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock
            {
                Text = definition.Label,
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("PrimaryTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var stateText = new TextBlock
            {
                Text = listening
                    ? "LISTENING"
                    : !binding.Enabled
                        ? "OFF"
                        : registered ? definition.Required ? "RECOVERY" : "READY" : "IN USE",
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 7,
                FontWeight = FontWeights.Bold,
                Foreground = !binding.Enabled
                    ? (Brush)FindResource("SecondaryTextBrush")
                    : registered || listening
                        ? (Brush)FindResource("AccentBrush")
                        : (Brush)FindResource("WarningBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(stateText, 1);
            header.Children.Add(stateText);
            row.Children.Add(header);
            row.Children.Add(new TextBlock
            {
                Text = listening ? "Press the replacement now" : definition.Description,
                Margin = new Thickness(0, 2, 0, 0),
                FontSize = 7,
                Foreground = registered || listening || !binding.Enabled
                    ? (Brush)FindResource("SecondaryTextBrush")
                    : (Brush)FindResource("WarningBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var bindingButton = new Button
            {
                Tag = definition.Id,
                Content = listening ? "PRESS KEYS…" : HotkeyBindingLogic.Format(binding),
                Height = 31,
                Margin = new Thickness(-2, 4, -2, -2),
                Padding = new Thickness(4, 0, 4, 0),
                FontSize = 7,
                Style = (Style)FindResource("DrawerCompactButton"),
                ToolTip = listening
                    ? "Press the replacement shortcut now; Escape cancels"
                    : $"Change {definition.Label.ToLowerInvariant()} · " +
                      (definition.Required ? "required recovery shortcut" : "Backspace disables while listening")
            };
            bindingButton.Click += HotkeyBindingButton_Click;
            row.Children.Add(bindingButton);
            SetToggleButtonState(bindingButton, listening);
            HotkeyBindingListPanel.Children.Add(row);

            if (index < HotkeyBindingLogic.Definitions.Length - 1)
            {
                HotkeyBindingListPanel.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(1, 1, 1, 1),
                    Background = new SolidColorBrush(Color.FromArgb(48, 100, 116, 139))
                });
            }
        }
    }

    private void HotkeyBindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string actionId }
            || HotkeyBindingLogic.Find(actionId) is null)
        {
            return;
        }

        if (string.Equals(_hotkeyCaptureActionId, actionId, StringComparison.Ordinal))
        {
            _hotkeyCaptureActionId = string.Empty;
            _hotkeyCaptureMessage = "CAPTURE CANCELED";
        }
        else
        {
            _hotkeyCaptureActionId = actionId;
            _hotkeyCaptureMessage = string.Empty;
            Focus();
        }
        _hotkeyStudioUiSignature = string.Empty;
        UpdateHotkeyStudio(force: true);
        UpdateHotkeyStatus();
    }

    private async void HotkeyRestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        RestoreDefaultHotkeys(logEvent: true);
        await ShowHotkeyToastAsync("GLOBAL SHORTCUTS RESTORED", true);
    }

    private void RestoreDefaultHotkeys(bool logEvent)
    {
        if (_windowHandle != 0)
        {
            UnregisterAllHotkeys();
        }
        _hotkeyBindings.Clear();
        foreach (var binding in HotkeyBindingLogic.DefaultBindings())
        {
            _hotkeyBindings[binding.ActionId] = binding;
        }
        if (_windowHandle != 0)
        {
            RegisterAllHotkeys();
        }
        _hotkeyCaptureActionId = string.Empty;
        _hotkeyCaptureMessage = "DEFAULT SHORTCUTS RESTORED";
        _hotkeyStudioUiSignature = string.Empty;
        UpdateHotkeyStudio(force: true);
        UpdateHotkeyStatus();
        if (logEvent)
        {
            AddTacticalEvent("SYSTEM", "Global shortcuts restored", "Nine default bindings requested");
            SaveSettings();
        }
    }

    private bool ApplyCapturedHotkey(HotkeyBinding candidate)
    {
        var definition = HotkeyBindingLogic.Find(candidate.ActionId);
        if (definition is null)
        {
            return false;
        }
        var validation = HotkeyBindingLogic.ValidateCandidate(candidate, _hotkeyBindings.Values);
        if (!validation.Valid)
        {
            _hotkeyCaptureMessage = validation.Error;
            _hotkeyStudioUiSignature = string.Empty;
            UpdateHotkeyStudio(force: true);
            UpdateHotkeyStatus();
            return false;
        }

        var previous = CurrentHotkeyBinding(definition.Id);
        if (previous == candidate)
        {
            _hotkeyCaptureActionId = string.Empty;
            _hotkeyCaptureMessage = "SHORTCUT UNCHANGED";
            _hotkeyStudioUiSignature = string.Empty;
            UpdateHotkeyStudio(force: true);
            UpdateHotkeyStatus();
            return true;
        }

        if (_windowHandle != 0 && IsHotkeyRegistered(definition.Id))
        {
            NativeMethods.UnregisterHotKey(_windowHandle, definition.MessageId);
        }
        var registered = !candidate.Enabled
                         || _windowHandle == 0
                         || RegisterHotkey(definition, candidate);
        if (!registered)
        {
            var previousRestored = previous.Enabled
                                   && _windowHandle != 0
                                   && RegisterHotkey(definition, previous);
            _hotkeyRegistrationStates[definition.Id] = previousRestored;
            _hotkeyCaptureMessage = "THAT SHORTCUT IS IN USE · TRY ANOTHER";
            _hotkeyStudioUiSignature = string.Empty;
            UpdateHotkeyStudio(force: true);
            UpdateHotkeyStatus();
            return false;
        }

        _hotkeyBindings[definition.Id] = candidate;
        _hotkeyRegistrationStates[definition.Id] = candidate.Enabled && _windowHandle != 0;
        _hotkeyCaptureActionId = string.Empty;
        _hotkeyCaptureMessage = candidate.Enabled
            ? $"SAVED · {HotkeyBindingLogic.Format(candidate)}"
            : $"{definition.CompactLabel} SHORTCUT OFF";
        _hotkeyStudioUiSignature = string.Empty;
        UpdateHotkeyStudio(force: true);
        UpdateHotkeyStatus();
        AddTacticalEvent(
            "SYSTEM",
            candidate.Enabled ? "Global shortcut changed" : "Optional shortcut disabled",
            $"{definition.Label} · {HotkeyBindingLogic.Format(candidate)}");
        SaveSettings();
        return true;
    }

    private static uint CurrentCaptureModifiers()
    {
        var keyboardModifiers = Keyboard.Modifiers;
        uint modifiers = 0;
        if ((keyboardModifiers & ModifierKeys.Control) != 0) modifiers |= HotkeyBindingLogic.ModControl;
        if ((keyboardModifiers & ModifierKeys.Alt) != 0) modifiers |= HotkeyBindingLogic.ModAlt;
        if ((keyboardModifiers & ModifierKeys.Shift) != 0) modifiers |= HotkeyBindingLogic.ModShift;
        if ((keyboardModifiers & ModifierKeys.Windows) != 0) modifiers |= 0x0008;
        return modifiers;
    }

    private bool TryHandleFocusedHotkey(KeyEventArgs e)
    {
        if (e.IsRepeat)
        {
            return false;
        }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var virtualKey = (uint)Math.Max(0, KeyInterop.VirtualKeyFromKey(key));
        var modifiers = CurrentCaptureModifiers();
        var match = _hotkeyBindings.Values.FirstOrDefault(binding =>
            binding.Enabled
            && binding.VirtualKey == virtualKey
            && binding.Modifiers == modifiers);
        if (match is null)
        {
            return false;
        }
        ExecuteHotkeyAction(match.ActionId);
        return true;
    }

    private async Task RecenterFromHotkeyAsync()
    {
        _ = ShowHotkeyToastAsync("RECENTERING...", true);
        var centered = await RecenterAsync();
        await ShowHotkeyToastAsync(
            centered ? "FOLLOW RECENTERED" : "FOLLOW READY · WAITING FOR PLAYER",
            centered);
    }

    private async Task DropTimedDangerFromHotkeyAsync()
    {
        if (_streamerMode)
        {
            await ShowHotkeyToastAsync("DANGER MARKING HIDDEN IN STREAMER MODE", false);
            return;
        }

        var added = await ExecuteMapperCommandAsync(
            "window.__isley?.dropTimedPinAtSelf('danger',15,'Danger sighting') ?? false");
        if (added)
        {
            AddTacticalEvent("RECOVERY", "Danger sighting marked", "15-minute marker saved at the latest authorized position");
        }
        await ShowHotkeyToastAsync(
            added ? "15M DANGER MARKED" : "PLAYER POSITION UNAVAILABLE",
            added);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_onboardingTutorialOpen)
        {
            if (key == Key.Escape)
            {
                e.Handled = true;
                CloseOnboardingTutorial(completed: false);
                return;
            }
            if (key == Key.Left && !OnboardingTutorialLogic.IsFirst(_onboardingTutorialStepIndex))
            {
                e.Handled = true;
                _onboardingTutorialStepIndex = OnboardingTutorialLogic.Move(
                    _onboardingTutorialStepIndex,
                    -1);
                UpdateOnboardingTutorial();
                return;
            }
            if (key == Key.Right && !OnboardingTutorialLogic.IsLast(_onboardingTutorialStepIndex))
            {
                e.Handled = true;
                _onboardingTutorialStepIndex = OnboardingTutorialLogic.Move(
                    _onboardingTutorialStepIndex,
                    1);
                UpdateOnboardingTutorial();
                return;
            }

            // Keep global overlay actions from firing through a modal tutorial.
            // Tab and Enter still reach the focused tutorial buttons normally.
            return;
        }
        if (!string.IsNullOrEmpty(_hotkeyCaptureActionId))
        {
            e.Handled = true;
            if (key == Key.Escape)
            {
                _hotkeyCaptureActionId = string.Empty;
                _hotkeyCaptureMessage = "CAPTURE CANCELED";
                _hotkeyStudioUiSignature = string.Empty;
                UpdateHotkeyStudio(force: true);
                UpdateHotkeyStatus();
                return;
            }

            var definition = HotkeyBindingLogic.Find(_hotkeyCaptureActionId);
            if (definition is null)
            {
                _hotkeyCaptureActionId = string.Empty;
                UpdateHotkeyStudio(force: true);
                UpdateHotkeyStatus();
                return;
            }
            if (key is Key.LeftCtrl or Key.RightCtrl
                or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift)
            {
                return;
            }
            if (key is Key.LWin or Key.RWin)
            {
                _hotkeyCaptureMessage = "WINDOWS KEY IS NOT ALLOWED";
                _hotkeyStudioUiSignature = string.Empty;
                UpdateHotkeyStudio(force: true);
                return;
            }
            if (key is Key.Back or Key.Delete)
            {
                if (definition.Required)
                {
                    _hotkeyCaptureMessage = "RECOVERY SHORTCUT REQUIRED";
                    _hotkeyStudioUiSignature = string.Empty;
                    UpdateHotkeyStudio(force: true);
                    return;
                }
                ApplyCapturedHotkey(new HotkeyBinding(definition.Id, 0, 0, false));
                return;
            }

            var virtualKey = (uint)Math.Max(0, KeyInterop.VirtualKeyFromKey(key));
            ApplyCapturedHotkey(new HotkeyBinding(
                definition.Id,
                CurrentCaptureModifiers(),
                virtualKey,
                true));
            return;
        }

        if (TryHandleFocusedHotkey(e))
        {
            e.Handled = true;
        }
    }

    private async Task StartQuickTimerFromHotkeyAsync()
    {
        var quickTimerCount = _survivalTimers.Count(timer =>
            timer.Label.StartsWith("Quick timer", StringComparison.OrdinalIgnoreCase));
        var label = quickTimerCount == 0 ? "Quick timer" : $"Quick timer {quickTimerCount + 1}";
        var started = StartSurvivalTimer(label, 5);
        await ShowHotkeyToastAsync(
            started ? "5M QUICK TIMER STARTED" : "TIMER LIMIT · CLEAR ONE",
            started);
    }

    private void OpenHotkeyStudio()
    {
        OpenToolsWorkspace("overlay");
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => HotkeysSectionAnchor.BringIntoView()));
    }

    private void UpdateQuickKeysPresentation(bool force = false)
    {
        if (QuickKeysHudBorder is null
            || QuickKeysHudModeText is null
            || QuickKeysHudItemsPanel is null
            || QuickKeysModeButton is null
            || QuickKeysStatusText is null)
        {
            return;
        }

        var presentation = QuickKeysLogic.Present(
            _quickKeysModeIndex,
            ActualWidth > 0 ? ActualWidth : Width);
        _quickKeysModeIndex = presentation.ModeIndex;
        var show = HudSurfaceLogic.Show(_quickKeysHudVisible, _streamerMode);
        QuickKeysHudBorder.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        UpdateHudDockLayout();
        QuickKeysModeButton.Content =
            $"Quick keys mode · {presentation.ModeLabel[..1]}{presentation.ModeLabel[1..].ToLowerInvariant()}";
        QuickKeysStatusText.Text = _streamerMode
            ? "PRIVACY HIDES THE RAIL · PREFERENCE PRESERVED"
            : _quickKeysHudVisible
                ? $"ON · {presentation.ModeLabel} DEFAULTS · REBINDABLE IN GAME"
                : "OFF BY DEFAULT · TURN ON ONLY WHEN USEFUL";
        SetToggleButtonState(QuickKeysModeButton, _quickKeysHudVisible);
        if (!show)
        {
            return;
        }

        var signature = $"{presentation.ModeId}:{presentation.Entries.Count}:{ActualWidth:0}";
        if (!force && string.Equals(signature, _quickKeysUiSignature, StringComparison.Ordinal))
        {
            return;
        }
        _quickKeysUiSignature = signature;
        QuickKeysHudModeText.Text = presentation.ModeLabel;
        QuickKeysHudItemsPanel.Children.Clear();

        var cellWidth = presentation.Entries.Count <= 3
            ? 62d
            : presentation.Entries.Count == 4 ? 64d : 66d;
        foreach (var entry in presentation.Entries)
        {
            var cell = new StackPanel
            {
                Width = cellWidth,
                Margin = new Thickness(2, 0, 2, 0)
            };
            cell.Children.Add(new TextBlock
            {
                Text = entry.Keys,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = presentation.IsCompact ? 7 : 7.5,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("AccentBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            cell.Children.Add(new TextBlock
            {
                Text = entry.Action,
                Margin = new Thickness(0, 1, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = presentation.IsCompact ? 6 : 6.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("PrimaryTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            QuickKeysHudItemsPanel.Children.Add(cell);
        }
    }

    private void HudQuickKeysButton_Click(object sender, RoutedEventArgs e)
    {
        _quickKeysHudVisible = !_quickKeysHudVisible;
        _quickKeysUiSignature = string.Empty;
        UpdateHudSurfaceControls();
        SaveSettings();
    }

    private void QuickKeysModeButton_Click(object sender, RoutedEventArgs e)
    {
        _quickKeysModeIndex = (_quickKeysModeIndex + 1) % QuickKeysLogic.ModeCount;
        _quickKeysUiSignature = string.Empty;
        UpdateQuickKeysPresentation(force: true);
        SaveSettings();
    }
}
