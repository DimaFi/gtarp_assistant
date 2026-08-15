using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.Infrastructure.Windows;

[SupportedOSPlatform("windows6.1")]
public sealed class WindowCaptureService
{
    public ScreenFrame CaptureAnalysisFrame(nint windowHandle, int targetWidth = 320)
    {
        var rect = GetValidatedWindowRect(windowHandle);
        var sourceWidth = rect.Right - rect.Left;
        var sourceHeight = rect.Bottom - rect.Top;
        var width = Math.Clamp(targetWidth, 64, sourceWidth);
        var height = Math.Max(36, (int)Math.Round(sourceHeight * (double)width / sourceWidth));
        using var scaled = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(scaled))
        {
            var destination = graphics.GetHdc();
            var source = GetDC(0);
            try
            {
                if (source == 0) throw new InvalidOperationException("Не удалось открыть экранный device context.");
                _ = SetStretchBltMode(destination, 4);
                if (!StretchBlt(destination, 0, 0, width, height, source, rect.Left, rect.Top, sourceWidth, sourceHeight, 0x00CC0020))
                    throw new InvalidOperationException("Не удалось получить уменьшенный кадр окна GTA.");
            }
            finally
            {
                if (source != 0) _ = ReleaseDC(0, source);
                graphics.ReleaseHdc(destination);
            }
        }
        var pixels = new byte[width * height];
        var data = scaled.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            unsafe
            {
                var scan = (byte*)data.Scan0;
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var source = scan + y * data.Stride + x * 3;
                        pixels[y * width + x] = (byte)((source[2] * 77 + source[1] * 150 + source[0] * 29) >> 8);
                    }
            }
        }
        finally { scaled.UnlockBits(data); }
        return new(width, height, pixels, DateTimeOffset.UtcNow);
    }

    public byte[] CapturePng(nint windowHandle)
    {
        using var bitmap = CaptureBitmap(windowHandle);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static Bitmap CaptureBitmap(nint windowHandle)
    {
        var rect = GetValidatedWindowRect(windowHandle);
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0 || width > 16_384 || height > 16_384) throw new InvalidOperationException("Некорректный размер окна GTA.");
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static Rect GetValidatedWindowRect(nint windowHandle)
    {
        if (windowHandle == 0 || !IsWindow(windowHandle)) throw new InvalidOperationException("Окно GTA не найдено.");
        if (!GetWindowRect(windowHandle, out var rect)) throw new InvalidOperationException("Не удалось получить границы окна GTA.");
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0 || width > 16_384 || height > 16_384) throw new InvalidOperationException("Некорректный размер окна GTA.");
        return rect;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint deviceContext);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(nint deviceContext, int mode);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(nint destination, int xDestination, int yDestination, int widthDestination, int heightDestination,
        nint source, int xSource, int ySource, int widthSource, int heightSource, int rasterOperation);
}
