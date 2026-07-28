namespace Isley;

internal static class VisibleHudSensor
{
    private const uint InvalidPixel = 0xFFFFFFFF;

    private readonly record struct ScreenRegion(
        double Left,
        double Top,
        double Right,
        double Bottom,
        double ExpectedFullDensity,
        double MinimumPresence);

    // Current Evrima HUD positions are expressed as fractions of the game client,
    // so the same detector works across ordinary 16:9 and ultrawide resolutions.
    private static readonly ScreenRegion FoodRegion = new(
        0.905, 0.755, 0.950, 0.865, 0.38, 0.08);
    private static readonly ScreenRegion StaminaRegion = new(
        0.855, 0.820, 0.925, 0.965, 0.25, 0.06);
    private static readonly ScreenRegion WaterRegion = new(
        0.940, 0.835, 0.985, 0.935, 0.55, 0.06);

    internal static bool TryRead(
        nint gameWindow,
        DateTimeOffset capturedAt,
        VisibleHudCalibration calibration,
        out VisibleHudSensorSample sample)
    {
        sample = default;
        if (!TryResolveWindow(
                gameWindow,
                out var origin,
                out var width,
                out var height))
        {
            return false;
        }

        var screenDc = NativeMethods.GetDC(0);
        if (screenDc == 0)
        {
            return false;
        }

        try
        {
            var foodDensity = SampleCyanDensity(
                screenDc, origin, width, height, FoodRegion, calibration);
            var staminaDensity = SampleCyanDensity(
                screenDc, origin, width, height, StaminaRegion, calibration);
            var waterDensity = SampleCyanDensity(
                screenDc, origin, width, height, WaterRegion, calibration);
            if (foodDensity < FoodRegion.MinimumPresence
                || staminaDensity < StaminaRegion.MinimumPresence
                || waterDensity < WaterRegion.MinimumPresence)
            {
                return false;
            }

            var redEdgeRatio = SampleRedEdgeRatio(screenDc, origin, width, height);
            var health = VisibleHudSensorLogic.EstimateHealthPercent(redEdgeRatio);
            var food = VisibleHudSensorLogic.EstimateFillPercent(
                foodDensity,
                FoodRegion.ExpectedFullDensity);
            var water = VisibleHudSensorLogic.EstimateFillPercent(
                waterDensity,
                WaterRegion.ExpectedFullDensity);
            var stamina = VisibleHudSensorLogic.EstimateFillPercent(
                staminaDensity,
                StaminaRegion.ExpectedFullDensity);
            var presence = Math.Min(
                1,
                Math.Min(
                    foodDensity / FoodRegion.ExpectedFullDensity,
                    Math.Min(
                        staminaDensity / StaminaRegion.ExpectedFullDensity,
                        waterDensity / WaterRegion.ExpectedFullDensity)));
            var confidence = Math.Clamp(0.50 + presence * 0.38, 0.50, 0.88);

            sample = new VisibleHudSensorSample(
                capturedAt,
                health,
                food,
                water,
                stamina,
                confidence,
                redEdgeRatio >= 0.005);
            return true;
        }
        finally
        {
            NativeMethods.ReleaseDC(0, screenDc);
        }
    }

    internal static bool TryCalibrate(
        nint gameWindow,
        DateTimeOffset capturedAt,
        out VisibleHudCalibration calibration)
    {
        calibration = VisibleHudCalibration.Default;
        if (!TryResolveWindow(
                gameWindow,
                out var origin,
                out var width,
                out var height))
        {
            return false;
        }

        var screenDc = NativeMethods.GetDC(0);
        if (screenDc == 0)
        {
            return false;
        }

        try
        {
            var best = VisibleHudCalibration.Default;
            foreach (var scale in new[] { 0.85, 1.0, 1.15 })
            {
                foreach (var offsetX in new[] { -0.02, 0.0, 0.02 })
                {
                    foreach (var offsetY in new[] { -0.02, 0.0, 0.02 })
                    {
                        var candidate = new VisibleHudCalibration(
                            scale, offsetX, offsetY, 0, capturedAt);
                        var scored = ScoreCalibration(
                            screenDc, origin, width, height, candidate);
                        if (scored.Score > best.Score) best = scored;
                    }
                }
            }

            foreach (var scaleDelta in new[] { -0.05, 0.0, 0.05 })
            {
                foreach (var offsetXDelta in new[] { -0.01, 0.0, 0.01 })
                {
                    foreach (var offsetYDelta in new[] { -0.01, 0.0, 0.01 })
                    {
                        var candidate = new VisibleHudCalibration(
                            best.Scale + scaleDelta,
                            best.OffsetX + offsetXDelta,
                            best.OffsetY + offsetYDelta,
                            0,
                            capturedAt);
                        var scored = ScoreCalibration(
                            screenDc, origin, width, height, candidate);
                        if (scored.Score > best.Score) best = scored;
                    }
                }
            }

            calibration = VisibleHudSensorLogic.NormalizeCalibration(best);
            return calibration.Score >= 0.45;
        }
        finally
        {
            NativeMethods.ReleaseDC(0, screenDc);
        }
    }

    private static VisibleHudCalibration ScoreCalibration(
        nint dc,
        NativeMethods.NativePoint origin,
        int width,
        int height,
        VisibleHudCalibration candidate)
    {
        var food = SampleCyanDensity(dc, origin, width, height, FoodRegion, candidate);
        var stamina = SampleCyanDensity(dc, origin, width, height, StaminaRegion, candidate);
        var water = SampleCyanDensity(dc, origin, width, height, WaterRegion, candidate);
        if (food < FoodRegion.MinimumPresence
            || stamina < StaminaRegion.MinimumPresence
            || water < WaterRegion.MinimumPresence)
        {
            return candidate;
        }

        var score = Math.Clamp(
            (Math.Min(1, food / 0.34)
             + Math.Min(1, stamina / 0.21)
             + Math.Min(1, water / 0.24)) / 3,
            0,
            1);
        return candidate with { Score = score };
    }

    private static bool TryResolveWindow(
        nint gameWindow,
        out NativeMethods.NativePoint origin,
        out int width,
        out int height)
    {
        origin = new NativeMethods.NativePoint();
        width = 0;
        height = 0;
        if (gameWindow == 0
            || !NativeMethods.GetClientRect(gameWindow, out var client)
            || client.Right - client.Left < 960
            || client.Bottom - client.Top < 540
            || !NativeMethods.ClientToScreen(gameWindow, ref origin))
        {
            return false;
        }

        width = client.Right - client.Left;
        height = client.Bottom - client.Top;
        var aspect = width / (double)height;
        return aspect is >= 1.45 and <= 2.55;
    }

    private static double SampleCyanDensity(
        nint dc,
        NativeMethods.NativePoint origin,
        int width,
        int height,
        ScreenRegion region,
        VisibleHudCalibration calibration)
    {
        var transformed = VisibleHudSensorLogic.TransformRegion(
            region.Left,
            region.Top,
            region.Right,
            region.Bottom,
            calibration);
        var left = origin.X + (int)Math.Round(width * transformed.Left);
        var top = origin.Y + (int)Math.Round(height * transformed.Top);
        var right = origin.X + (int)Math.Round(width * transformed.Right);
        var bottom = origin.Y + (int)Math.Round(height * transformed.Bottom);
        var xStep = Math.Max(2, (right - left) / 28);
        var yStep = Math.Max(2, (bottom - top) / 32);
        var matching = 0;
        var total = 0;

        for (var y = top; y < bottom; y += yStep)
        {
            for (var x = left; x < right; x += xStep)
            {
                var color = NativeMethods.GetPixel(dc, x, y);
                if (color == InvalidPixel) continue;
                total++;
                if (IsHudCyan(color)) matching++;
            }
        }

        return total == 0 ? 0 : matching / (double)total;
    }

    private static double SampleRedEdgeRatio(
        nint dc,
        NativeMethods.NativePoint origin,
        int width,
        int height)
    {
        var xStep = Math.Max(6, width / 160);
        var yStep = Math.Max(6, height / 90);
        var edgeX = width * 0.07;
        var edgeY = height * 0.07;
        var matching = 0;
        var total = 0;

        for (var y = 0; y < height; y += yStep)
        {
            for (var x = 0; x < width; x += xStep)
            {
                if (x >= edgeX
                    && x <= width - edgeX
                    && y >= edgeY
                    && y <= height - edgeY)
                {
                    continue;
                }

                var color = NativeMethods.GetPixel(dc, origin.X + x, origin.Y + y);
                if (color == InvalidPixel) continue;
                total++;
                if (IsDamageRed(color)) matching++;
            }
        }

        return total == 0 ? 0 : matching / (double)total;
    }

    private static bool IsHudCyan(uint color)
    {
        var red = (int)(color & 0xFF);
        var green = (int)((color >> 8) & 0xFF);
        var blue = (int)((color >> 16) & 0xFF);
        return green > 120
               && blue > 120
               && green + blue > red * 1.30 + 18;
    }

    private static bool IsDamageRed(uint color)
    {
        var red = (int)(color & 0xFF);
        var green = (int)((color >> 8) & 0xFF);
        var blue = (int)((color >> 16) & 0xFF);
        return red > 120
               && red > green * 1.35
               && red > blue * 1.20
               && red - green > 30;
    }
}
