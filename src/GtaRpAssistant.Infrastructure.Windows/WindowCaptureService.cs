using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GtaRpAssistant.Infrastructure.Windows;

[SupportedOSPlatform("windows6.1")]
public sealed class WindowCaptureService
{
    public byte[] CapturePng(nint windowHandle)
    {
        if (windowHandle == 0 || !IsWindow(windowHandle)) throw new InvalidOperationException("Окно GTA не найдено.");
        if (!GetWindowRect(windowHandle, out var rect)) throw new InvalidOperationException("Не удалось получить границы окна GTA.");
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0 || width > 16_384 || height > 16_384) throw new InvalidOperationException("Некорректный размер окна GTA.");
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
}
