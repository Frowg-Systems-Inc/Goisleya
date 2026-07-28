namespace Isley;

internal readonly record struct AimViewportBounds(
    int Left,
    int Top,
    int Width,
    int Height);

internal static class AimViewportLogic
{
    private const int MinimumClientDimension = 64;
    private const int MaximumClientDimension = 32768;
    private const int MaximumScreenCoordinate = 262144;

    internal static bool TryResolveClientArea(
        int clientWidth,
        int clientHeight,
        int screenLeft,
        int screenTop,
        out AimViewportBounds bounds)
    {
        bounds = default;
        if (clientWidth < MinimumClientDimension
            || clientHeight < MinimumClientDimension
            || clientWidth > MaximumClientDimension
            || clientHeight > MaximumClientDimension)
        {
            return false;
        }

        var left = (long)screenLeft;
        var top = (long)screenTop;
        var right = left + clientWidth;
        var bottom = top + clientHeight;
        if (left < -MaximumScreenCoordinate
            || top < -MaximumScreenCoordinate
            || right > MaximumScreenCoordinate
            || bottom > MaximumScreenCoordinate)
        {
            return false;
        }

        bounds = new AimViewportBounds(
            screenLeft,
            screenTop,
            clientWidth,
            clientHeight);
        return true;
    }
}
