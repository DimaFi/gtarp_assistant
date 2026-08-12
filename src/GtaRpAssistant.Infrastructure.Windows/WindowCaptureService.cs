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
        using var bitmap = CaptureBitmap(windowHandle);
        var width = Math.Clamp(targetWidth, 64, bitmap.Width);
        var height = Math.Max(36, (int)Math.Round(bitmap.Height * (double)width / bitmap.Width));
        using var scaled = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(scaled)) graphics.DrawImage(bitmap, 0, 0, width, height);
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
        if (windowHandle == 0 || !IsWindow(windowHandle)) throw new InvalidOperationException("Окно GTA не найдено.");
        if (!GetWindowRect(windowHandle, out var rect)) throw new InvalidOperationException("Не удалось получить границы окна GTA.");
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0 || width > 16_384 || height > 16_384) throw new InvalidOperationException("Некорректный размер окна GTA.");
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
}
