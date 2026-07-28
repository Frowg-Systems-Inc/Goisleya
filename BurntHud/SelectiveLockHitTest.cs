using System.Windows;

namespace Isley;

internal static class SelectiveLockHitTest
{
    internal static bool ContainsPackedScreenPoint(FrameworkElement unlockButton, nint packedPoint)
    {
        if (!unlockButton.IsLoaded
            || !unlockButton.IsVisible
            || unlockButton.ActualWidth <= 0
            || unlockButton.ActualHeight <= 0)
        {
            return false;
        }

        var packed = unchecked((int)packedPoint.ToInt64());
        var screenX = unchecked((short)(packed & 0xFFFF));
        var screenY = unchecked((short)((packed >> 16) & 0xFFFF));
        var topLeft = unlockButton.PointToScreen(new Point(0, 0));
        var bottomRight = unlockButton.PointToScreen(
            new Point(unlockButton.ActualWidth, unlockButton.ActualHeight));
        return screenX >= Math.Min(topLeft.X, bottomRight.X)
               && screenX < Math.Max(topLeft.X, bottomRight.X)
               && screenY >= Math.Min(topLeft.Y, bottomRight.Y)
               && screenY < Math.Max(topLeft.Y, bottomRight.Y);
    }
}
