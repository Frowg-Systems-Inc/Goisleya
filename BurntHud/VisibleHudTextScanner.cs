using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Isley;

internal static class VisibleHudTextScanner
{
    private const int MaximumOcrDimension = 2_000;
    private const int SrcCopy = 0x00CC0020;
    private const int CaptureBlt = 0x40000000;

    internal static async Task<VisibleHudTextReadout> ReadAsync(nint gameWindow)
    {
        var source = CaptureClient(gameWindow);
        if (source is null)
        {
            return VisibleHudTextLogic.Parse(null);
        }

        if (Math.Max(source.PixelWidth, source.PixelHeight) > MaximumOcrDimension)
        {
            var scale = MaximumOcrDimension /
                        (double)Math.Max(source.PixelWidth, source.PixelHeight);
            var transformed = new TransformedBitmap(
                source,
                new ScaleTransform(scale, scale));
            transformed.Freeze();
            source = transformed;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        using var encoded = new MemoryStream();
        encoder.Save(encoded);
        var bytes = encoded.ToArray();

        using var randomAccess = new InMemoryRandomAccessStream();
        using (var output = randomAccess.GetOutputStreamAt(0))
        using (var writer = new DataWriter(output))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
        }
        randomAccess.Seek(0);

        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
        {
            return VisibleHudTextLogic.Parse(null);
        }

        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(randomAccess);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);
        return VisibleHudTextLogic.Parse(result.Text);
    }

    private static BitmapSource? CaptureClient(nint gameWindow)
    {
        if (gameWindow == 0
            || !NativeMethods.GetClientRect(gameWindow, out var client))
        {
            return null;
        }

        var width = client.Right - client.Left;
        var height = client.Bottom - client.Top;
        if (width < 960 || height < 540)
        {
            return null;
        }

        var origin = new NativeMethods.NativePoint();
        if (!NativeMethods.ClientToScreen(gameWindow, ref origin))
        {
            return null;
        }

        var screenDc = NativeMethods.GetDC(0);
        if (screenDc == 0) return null;
        var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        if (memoryDc == 0)
        {
            NativeMethods.ReleaseDC(0, screenDc);
            return null;
        }

        var bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
        if (bitmap == 0)
        {
            NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(0, screenDc);
            return null;
        }

        var previous = NativeMethods.SelectObject(memoryDc, bitmap);
        try
        {
            if (!NativeMethods.BitBlt(
                    memoryDc,
                    0,
                    0,
                    width,
                    height,
                    screenDc,
                    origin.X,
                    origin.Y,
                    SrcCopy | CaptureBlt))
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                0,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (previous != 0) NativeMethods.SelectObject(memoryDc, previous);
            NativeMethods.DeleteObject(bitmap);
            NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(0, screenDc);
        }
    }
}
