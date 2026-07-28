using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Isley;

public partial class IsleyDockWindow : Window
{
    private readonly Action _restoreRequested;
    private readonly Action _vitalsRequested;
    private readonly Action _lockToggleRequested;
    private readonly Action _closeRequested;
    private bool _closingSilently;
    private bool _isLocked;
    private int _vitalSeverity;
    private nint _windowHandle;

    internal IsleyDockWindow(
        Action restoreRequested,
        Action vitalsRequested,
        Action lockToggleRequested,
        Action closeRequested,
        bool isLocked,
        DockVitalsPresentation vitals)
    {
        _restoreRequested = restoreRequested;
        _vitalsRequested = vitalsRequested;
        _lockToggleRequested = lockToggleRequested;
        _closeRequested = closeRequested;
        InitializeComponent();
        UpdateLockState(isLocked);
        UpdateVitals(vitals, animate: false);
    }

    internal void UpdateVitals(DockVitalsPresentation presentation, bool animate = true)
    {
        var wasCritical = _vitalSeverity >= 2;
        _vitalSeverity = presentation.Severity;
        DockVitalsButton.Visibility = presentation.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
        Width = presentation.Visible ? 362 : 264;
        DockVitalsSourceText.Text = presentation.SourceLabel;
        DockVitalValuesText.Text = presentation.ValuesLabel;
        DockVitalsButton.ToolTip = presentation.Tooltip;
        DockVitalsButton.Opacity = presentation.Fresh ? 1 : 0.66;

        var accent = presentation.Severity switch
        {
            >= 2 => Color.FromRgb(255, 112, 112),
            1 => Color.FromRgb(255, 163, 108),
            _ when presentation.Fresh => Color.FromRgb(88, 214, 141),
            _ => Color.FromRgb(126, 137, 149)
        };
        DockVitalValuesText.Foreground = new SolidColorBrush(accent);
        DockVitalsButton.BorderBrush = new SolidColorBrush(Color.FromArgb(0xA0, accent.R, accent.G, accent.B));

        if (animate && presentation.Severity >= 2 && !wasCritical)
        {
            DockVitalsButton.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation
                {
                    From = 0.42,
                    To = presentation.Fresh ? 1 : 0.66,
                    Duration = TimeSpan.FromMilliseconds(420),
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(2)
                });
        }

        if (IsLoaded && double.IsFinite(Left))
        {
            var workArea = SystemParameters.WorkArea;
            Left = Math.Clamp(
                Left,
                workArea.Left,
                Math.Max(workArea.Left, workArea.Right - Width));
        }
    }

    internal void CloseSilently()
    {
        _closingSilently = true;
        Close();
    }

    internal void UpdateLockState(bool locked)
    {
        _isLocked = locked;
        if (DockLockButton is null || DockLockGlyphText is null)
        {
            return;
        }

        DockLockGlyphText.Text = locked ? "\uE72E" : "\uE785";
        DockLockButton.ToolTip = locked
            ? "Unlock Isley dock; every other point passes clicks through"
            : "Lock Isley dock in place";
        System.Windows.Automation.AutomationProperties.SetName(
            DockLockButton,
            locked ? "Unlock Isley dock" : "Lock Isley dock");
        System.Windows.Automation.AutomationProperties.SetHelpText(
            DockLockButton,
            locked
                ? "The only clickable control while the Isley dock is locked"
                : "Makes the dock ignore all pointer input except this unlock button");
        DockLockButton.Background = new SolidColorBrush(
            locked ? Color.FromRgb(69, 32, 42) : Color.FromRgb(23, 33, 45));
        DockLockButton.BorderBrush = new SolidColorBrush(
            locked ? Color.FromArgb(0xCC, 255, 91, 108) : Color.FromArgb(0x80, 88, 214, 141));
        if (locked && IsVisible)
        {
            Mouse.Capture(null);
            DockLockButton.Focus();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowHandle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_windowHandle)?.AddHook(WindowMessageHook);
        if (Topmost)
        {
            EnsureTopMost(forceToggle: true);
        }
    }

    internal void EnsureTopMost(bool forceToggle = false)
    {
        if (!Topmost || !IsVisible)
        {
            return;
        }

        if (_windowHandle == 0)
        {
            _windowHandle = new WindowInteropHelper(this).Handle;
        }

        NativeMethods.TryReassertTopMost(_windowHandle, forceToggle);
    }

    private nint WindowMessageHook(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message != NativeMethods.WmNcHitTest || !_isLocked)
        {
            return 0;
        }

        handled = true;
        return SelectiveLockHitTest.ContainsPackedScreenPoint(DockLockButton, lParam)
            ? new nint(NativeMethods.HtClient)
            : new nint(NativeMethods.HtTransparent);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closingSilently)
        {
            e.Cancel = true;
            _closeRequested();
            return;
        }
        base.OnClosing(e);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isLocked && e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e) => _restoreRequested();

    private void VitalsButton_Click(object sender, RoutedEventArgs e) => _vitalsRequested();

    private void LockButton_Click(object sender, RoutedEventArgs e) => _lockToggleRequested();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => _closeRequested();
}
