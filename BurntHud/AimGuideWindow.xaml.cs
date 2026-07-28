using System.Windows;
using System.Windows.Interop;

namespace Isley;

internal enum AimGuideMode
{
    Reticle,
    FrontArc,
    FrontAndRear
}

internal enum AimGuideAlignmentMode
{
    Unavailable,
    GameClient,
    MonitorPreview,
    MonitorFallback
}

public partial class AimGuideWindow : Window
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    internal AimGuideWindow()
    {
        InitializeComponent();
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var styles = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLong(
            handle,
            NativeMethods.GwlExStyle,
            styles | NativeMethods.WsExTransparent | WsExNoActivate | WsExToolWindow);
    }

    internal void Configure(
        AimGuideMode mode,
        double size,
        double depthScale,
        double horizontalOffset,
        double verticalOffset,
        bool areaVisible,
        bool centerCueVisible,
        bool uncertaintyVisible,
        bool labelVisible,
        string speciesLabel,
        string attackLabel,
        string growthLabel,
        string cameraLabel,
        int confirmedMatches,
        int insideMisses,
        int outsideHits)
    {
        GuideViewbox.Width = Math.Clamp(size, 90, 520);
        GuideViewbox.Height = Math.Clamp(size, 90, 520);
        GuideOffsetTransform.X = Math.Clamp(horizontalOffset, -240, 240);
        GuideOffsetTransform.Y = Math.Clamp(verticalOffset, -240, 240);
        AimDepthTransform.ScaleY = Math.Clamp(depthScale, 0.55, 1.40);

        var reticleMode = mode == AimGuideMode.Reticle;
        OuterArea.Visibility = areaVisible && !reticleMode ? Visibility.Visible : Visibility.Collapsed;
        ReticleArea.Visibility = areaVisible && reticleMode ? Visibility.Visible : Visibility.Collapsed;
        FrontArc.Visibility = areaVisible && !reticleMode ? Visibility.Visible : Visibility.Collapsed;
        RearArc.Visibility = areaVisible && mode == AimGuideMode.FrontAndRear
            ? Visibility.Visible
            : Visibility.Collapsed;

        OuterUncertaintyArea.Visibility = uncertaintyVisible && !reticleMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        FrontUncertaintyArc.Visibility = uncertaintyVisible && !reticleMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        RearUncertaintyArc.Visibility = uncertaintyVisible && mode == AimGuideMode.FrontAndRear
            ? Visibility.Visible
            : Visibility.Collapsed;
        ReticleUncertaintyArea.Visibility = uncertaintyVisible && reticleMode
            ? Visibility.Visible
            : Visibility.Collapsed;

        var evidence = AimCalibrationLogic.EvaluateEvidence(
            confirmedMatches,
            insideMisses,
            outsideHits);
        var uncertaintyThickness = evidence.HasContradiction
            ? 16d
            : evidence.EffectiveMatches switch
        {
            0 => 16d,
            <= 2 => 13d,
            <= 4 => 10d,
            _ => 7d
        };
        FrontUncertaintyArc.StrokeThickness = uncertaintyThickness;
        RearUncertaintyArc.StrokeThickness = uncertaintyThickness;
        OuterUncertaintyArea.StrokeThickness = Math.Max(6, uncertaintyThickness - 2);
        ReticleUncertaintyArea.StrokeThickness = Math.Max(5, uncertaintyThickness - 4);
        CenterCue.Visibility = centerCueVisible ? Visibility.Visible : Visibility.Collapsed;
        GuideLabelBorder.Visibility = labelVisible ? Visibility.Visible : Visibility.Collapsed;

        var species = string.IsNullOrWhiteSpace(speciesLabel) ? "SPECIES" : speciesLabel.Trim();
        var attack = string.IsNullOrWhiteSpace(attackLabel) ? "ATTACK" : attackLabel.Trim();
        var growth = string.IsNullOrWhiteSpace(growthLabel) ? "GROWTH" : growthLabel.Trim();
        var camera = string.IsNullOrWhiteSpace(cameraLabel)
            ? "CAMERA"
            : cameraLabel.Replace(" CAMERA", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        var confidence = AimCalibrationLogic.ConfidenceLabel(
            evidence.Matches,
            evidence.InsideMisses,
            evidence.OutsideHits);
        var tested = evidence.HasContradiction
            ? $"{evidence.Label} M{evidence.Matches}/I{evidence.InsideMisses}/O{evidence.OutsideHits}"
            : evidence.Matches > 0 ? $"{confidence} x{evidence.Matches}" : confidence;
        GuideLabel.Text = $"{species} · {attack} · {growth} · {camera} · {tested}";
    }

    internal AimGuideAlignmentMode AlignToForegroundViewport(
        nint foregroundWindow,
        bool preferClientArea)
    {
        if (foregroundWindow == nint.Zero)
        {
            return AimGuideAlignmentMode.Unavailable;
        }

        if (preferClientArea && TryAlignToClientArea(foregroundWindow))
        {
            return AimGuideAlignmentMode.GameClient;
        }

        var monitor = NativeMethods.MonitorFromWindow(
            foregroundWindow,
            NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo
        {
            Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };
        if (monitor == nint.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return AimGuideAlignmentMode.Unavailable;
        }

        var aligned = ApplyBounds(new AimViewportBounds(
            info.Monitor.Left,
            info.Monitor.Top,
            Math.Max(1, info.Monitor.Right - info.Monitor.Left),
            Math.Max(1, info.Monitor.Bottom - info.Monitor.Top)));
        if (!aligned)
        {
            return AimGuideAlignmentMode.Unavailable;
        }

        return preferClientArea
            ? AimGuideAlignmentMode.MonitorFallback
            : AimGuideAlignmentMode.MonitorPreview;
    }

    private bool TryAlignToClientArea(nint foregroundWindow)
    {
        if (!NativeMethods.GetClientRect(foregroundWindow, out var clientRect))
        {
            return false;
        }

        var clientOrigin = new NativeMethods.NativePoint();
        if (!NativeMethods.ClientToScreen(foregroundWindow, ref clientOrigin)
            || !AimViewportLogic.TryResolveClientArea(
                clientRect.Right - clientRect.Left,
                clientRect.Bottom - clientRect.Top,
                clientOrigin.X,
                clientOrigin.Y,
                out var bounds))
        {
            return false;
        }

        return ApplyBounds(bounds);
    }

    private bool ApplyBounds(AimViewportBounds bounds)
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        return NativeMethods.SetWindowPos(
            handle,
            nint.Zero,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoZOrder);
    }
}
