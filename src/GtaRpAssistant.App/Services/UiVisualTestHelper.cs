using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Interop;

namespace GtaRpAssistant.App.Services;

internal static class UiVisualTestHelper
{
    public static IReadOnlyList<string> GetCustomAutomationIds(DependencyObject root) =>
        Enumerate(root)
            .Select(AutomationProperties.GetAutomationId)
            .Where(x => !string.IsNullOrWhiteSpace(x) && x.Contains('.', StringComparison.Ordinal))
            .ToArray();

    public static void ValidateAutomationContract(DependencyObject root, params string[] requiredIds)
    {
        var ids = GetCustomAutomationIds(root);
        var duplicateId = ids.GroupBy(x => x, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1)?.Key;
        if (duplicateId is not null)
            throw new InvalidOperationException($"Visual tree contains duplicate AutomationId '{duplicateId}'.");

        foreach (var requiredId in requiredIds)
        {
            if (!ids.Contains(requiredId, StringComparer.Ordinal))
                throw new InvalidOperationException($"Visual tree does not expose required AutomationId '{requiredId}'.");
        }
    }

    public static T Find<T>(DependencyObject root, string automationId) where T : DependencyObject
    {
        var element = Enumerate(root)
            .OfType<T>()
            .FirstOrDefault(x => string.Equals(AutomationProperties.GetAutomationId(x), automationId, StringComparison.Ordinal));
        return element ?? throw new InvalidOperationException($"Automation element '{automationId}' was not found.");
    }

    public static void Click(DependencyObject root, string automationId)
    {
        var button = Find<System.Windows.Controls.Button>(root, automationId);
        if (!button.IsEnabled) throw new InvalidOperationException($"Automation button '{automationId}' is disabled.");
        var peer = new ButtonAutomationPeer(button);
        var provider = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider
            ?? throw new InvalidOperationException($"Automation button '{automationId}' does not expose InvokePattern.");
        provider.Invoke();
    }

    public static void Focus(DependencyObject root, string automationId)
    {
        var element = Find<FrameworkElement>(root, automationId);
        if (!element.Focusable || !element.IsEnabled || !element.IsVisible)
            throw new InvalidOperationException($"Automation element '{automationId}' cannot receive keyboard focus.");
        element.Focus();
        Keyboard.Focus(element);
        element.Dispatcher.Invoke(DispatcherPriority.Input, new Action(() => { }));
        if (!element.IsKeyboardFocusWithin)
            throw new InvalidOperationException($"Automation element '{automationId}' did not receive keyboard focus.");
    }

    public static void MoveFocusAndRequire(DependencyObject root, string fromId, string expectedId)
    {
        var from = Find<FrameworkElement>(root, fromId);
        Focus(root, fromId);
        if (!from.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)))
            throw new InvalidOperationException($"Focus could not move forward from '{fromId}'.");
        from.Dispatcher.Invoke(DispatcherPriority.Input, new Action(() => { }));
        if (Keyboard.FocusedElement is not DependencyObject focused ||
            !string.Equals(AutomationProperties.GetAutomationId(focused), expectedId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Focus from '{fromId}' did not move to '{expectedId}'.");
    }

    public static void Capture(FrameworkElement visual, string path, bool requireOpaque = false)
        => CaptureCore(visual, path, requireOpaque, null);

    public static void CaptureComposite(FrameworkElement root, string path, params FrameworkElement[] layers)
        => CaptureCore(root, path, true, layers);

    public static void CaptureWindow(Window window, string path)
    {
        WaitForRenderPass(window);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0 || !GetClientRect(handle, out var bounds))
            throw new InvalidOperationException("Window client area is unavailable for snapshot capture.");
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0) throw new InvalidOperationException("Window client area has invalid dimensions.");

        InvalidOperationException? lastFailure = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            if (window.Content is FrameworkElement content)
            {
                ForceFullTreeRebuild(content);
                content.Opacity = attempt % 2 == 0 ? .998 : .999;
                WaitForRenderPass(content);
                content.Opacity = 1;
                content.InvalidateVisual();
            }
            window.InvalidateVisual();
            window.UpdateLayout();
            ShowWindow(handle, 4);
            InvalidateRect(handle, 0, true);
            RedrawWindow(handle, 0, 0, 0x0001u | 0x0004u | 0x0080u | 0x0100u | 0x0200u | 0x0400u);
            UpdateWindow(handle);
            WaitForRenderPass(window, 150 + attempt * 100);
            DwmFlush();

            using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            var deviceContext = graphics.GetHdc();
            bool rendered;
            try { rendered = PrintWindow(handle, deviceContext, 0x00000001u | 0x00000002u); }
            finally { graphics.ReleaseHdc(deviceContext); }
            if (!rendered)
            {
                lastFailure = new InvalidOperationException("PrintWindow failed to render the client area.");
                continue;
            }

            try
            {
                ValidateNativeFrame(bitmap);
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                return;
            }
            catch (InvalidOperationException ex)
            {
                lastFailure = ex;
            }
        }

        throw new InvalidOperationException("Native snapshot remained incomplete after 5 render attempts.", lastFailure);
    }

    private static void CaptureCore(FrameworkElement visual, string path, bool requireOpaque, IReadOnlyList<FrameworkElement>? layers)
    {
        if (requireOpaque) ForceFullTreeRebuild(visual);
        WaitForRenderPass(visual);
        foreach (var element in Enumerate(visual).OfType<UIElement>()) element.InvalidateVisual();
        visual.UpdateLayout();

        var pixelWidth = checked((int)Math.Ceiling(visual.ActualWidth));
        var pixelHeight = checked((int)Math.Ceiling(visual.ActualHeight));
        if (pixelWidth <= 0 || pixelHeight <= 0)
            throw new InvalidOperationException("Visual has no renderable dimensions.");

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        var snapshot = new DrawingVisual();
        using (var drawing = snapshot.RenderOpen())
        {
            if (layers is null)
            {
                drawing.DrawRectangle(new VisualBrush(visual) { Stretch = Stretch.Fill }, null, new Rect(0, 0, pixelWidth, pixelHeight));
            }
            else
            {
                var background = (visual as System.Windows.Controls.Panel)?.Background ?? System.Windows.Media.Brushes.Transparent;
                drawing.DrawRectangle(background, null, new Rect(0, 0, pixelWidth, pixelHeight));
                foreach (var layer in layers)
                {
                    var origin = layer.TransformToAncestor(visual).Transform(new System.Windows.Point());
                    drawing.DrawRectangle(
                        new VisualBrush(layer) { Stretch = Stretch.Fill },
                        null,
                        new Rect(origin.X, origin.Y, layer.ActualWidth, layer.ActualHeight));
                }
            }
        }
        bitmap.Render(snapshot);
        if (requireOpaque) ValidateOpaqueFrame(bitmap, pixelWidth, pixelHeight);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void ForceFullTreeRebuild(FrameworkElement visual)
    {
        var visibility = visual.Visibility;
        visual.Visibility = Visibility.Collapsed;
        visual.Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
        visual.Visibility = visibility;
        visual.InvalidateMeasure();
        visual.InvalidateArrange();
        visual.UpdateLayout();
    }

    private static void WaitForRenderPass(DispatcherObject visual, int delayMilliseconds = 75)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, visual.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(delayMilliseconds),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void ValidateOpaqueFrame(BitmapSource bitmap, int width, int height)
    {
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        bitmap.CopyPixels(pixels, stride, 0);
        var opaque = 0;
        for (var offset = 3; offset < pixels.Length; offset += 4)
            if (pixels[offset] >= 250) opaque++;
        var ratio = (double)opaque / (width * height);
        if (ratio < .99)
            throw new InvalidOperationException($"Snapshot contains transparent render gaps: {ratio:P1} opaque pixels.");
    }

    private static void ValidateNativeFrame(System.Drawing.Bitmap bitmap)
    {
        var baseline = bitmap.GetPixel(0, 0);
        var sampled = 0;
        var different = 0;
        for (var y = 0; y < bitmap.Height; y += 4)
        {
            for (var x = 0; x < bitmap.Width; x += 4)
            {
                var pixel = bitmap.GetPixel(x, y);
                sampled++;
                if (Math.Abs(pixel.R - baseline.R) + Math.Abs(pixel.G - baseline.G) + Math.Abs(pixel.B - baseline.B) > 18) different++;
            }
        }
        if (sampled == 0 || (double)different / sampled < .08)
            throw new InvalidOperationException("Native snapshot does not contain enough rendered UI detail.");
        if (!HasVisibleDetail(bitmap, 0, 0, bitmap.Width, Math.Max(1, bitmap.Height / 7)))
            throw new InvalidOperationException("Native snapshot is missing the application header.");
        if (!HasVisibleDetail(bitmap, 0, bitmap.Height * 9 / 10, bitmap.Width, bitmap.Height))
            throw new InvalidOperationException("Native snapshot is missing the application footer.");
    }

    private static bool HasVisibleDetail(System.Drawing.Bitmap bitmap, int left, int top, int right, int bottom)
    {
        var sampled = 0;
        var detailed = 0;
        for (var y = top; y < bottom; y += 2)
        {
            for (var x = left; x < right; x += 2)
            {
                var pixel = bitmap.GetPixel(x, y);
                sampled++;
                if (Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) > 80) detailed++;
            }
        }
        return sampled > 0 && (double)detailed / sampled >= .002;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out NativeRect bounds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint window, nint deviceContext, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(nint window, nint updateRectangle, nint updateRegion, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(nint window, nint rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateWindow(nint window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    private static IEnumerable<DependencyObject> Enumerate(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var descendant in Enumerate(VisualTreeHelper.GetChild(root, index)))
                yield return descendant;
        }
    }
}
