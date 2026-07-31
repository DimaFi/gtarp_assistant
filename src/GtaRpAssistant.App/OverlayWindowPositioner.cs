using System.Windows;

namespace GtaRpAssistant.App;

internal static class OverlayWindowPositioner
{
    public static void Position(Window window, string position, nint targetWindow, double minimumHeight = 180)
    {
        var screen = targetWindow != 0
            ? System.Windows.Forms.Screen.FromHandle(targetWindow)
            : System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        var scaleX = 1d / dpi.DpiScaleX;
        var scaleY = 1d / dpi.DpiScaleY;
        var area = screen.WorkingArea;
        var workingArea = new Rect(
            area.Left * scaleX,
            area.Top * scaleY,
            area.Width * scaleX,
            area.Height * scaleY);
        var point = OverlayPlacement.Calculate(
            workingArea,
            new System.Windows.Size(window.Width, Math.Max(window.ActualHeight, minimumHeight)),
            position);
        window.Left = point.X;
        window.Top = point.Y;
    }
}

public static class OverlayPlacement
{
    public static System.Windows.Point Calculate(System.Windows.Rect workingArea, System.Windows.Size overlaySize, string position)
    {
        const double margin = 24;
        const double topOffset = 60;
        var left = position.Contains("Left", StringComparison.OrdinalIgnoreCase)
            ? workingArea.Left + margin
            : workingArea.Right - overlaySize.Width - margin;
        var top = position.Contains("Bottom", StringComparison.OrdinalIgnoreCase)
            ? workingArea.Bottom - overlaySize.Height - margin
            : workingArea.Top + topOffset;

        var maximumLeft = Math.Max(workingArea.Left, workingArea.Right - overlaySize.Width);
        var maximumTop = Math.Max(workingArea.Top, workingArea.Bottom - overlaySize.Height);
        return new(
            Math.Clamp(left, workingArea.Left, maximumLeft),
            Math.Clamp(top, workingArea.Top, maximumTop));
    }
}
