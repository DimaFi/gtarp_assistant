using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GtaRpAssistant.App;

public partial class OverlayWindow : Window
{
    private CancellationTokenSource? _hideCancellation;

    public OverlayWindow() { InitializeComponent(); SourceInitialized += (_, _) => ApplyStyles(); }
    public event EventHandler? ExpandRequested;
    public event EventHandler<OverlayPositionChangedEventArgs>? PositionChangedByUser;

    public async Task ShowAsync(
        OverlayPresentation presentation,
        TimeSpan duration,
        string position = "TopRight",
        nint targetWindow = default,
        CancellationToken cancellationToken = default,
        bool autoHide = true,
        double? customLeft = null,
        double? customTop = null)
    {
        _hideCancellation?.Cancel();
        _hideCancellation?.Dispose();
        var hideCancellation = new CancellationTokenSource();
        _hideCancellation = hideCancellation;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(hideCancellation.Token, cancellationToken);
        TitleText.Text = presentation.Title;
        MessageText.Text = presentation.Message;
        StepsList.ItemsSource = presentation.CompactSteps;
        StepsList.Visibility = presentation.CompactSteps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusPill.Text = presentation.Status;
        StatusPill.Tone = presentation.Tone;
        StatusPill.Activity = presentation.Activity;
        CardShell.Tone = presentation.Tone;
        SourceText.Text = $"{presentation.Source} · {presentation.Updated}";
        ApplyActivityLayout(presentation.Activity);
        Show();
        OverlayWindowPositioner.Position(this, position, targetWindow, customLeft: customLeft, customTop: customTop);
        try { await Task.Delay(duration, linked.Token); }
        catch (OperationCanceledException) { }
        if (autoHide && ReferenceEquals(_hideCancellation, hideCancellation)) Hide();
    }

    public void HideOverlay() { _hideCancellation?.Cancel(); Hide(); }

    private void ApplyActivityLayout(OverlayActivity activity)
    {
        var isActivity = activity != OverlayActivity.None;
        Width = isActivity ? 390 : 420;
        ActivityGrid.Visibility = isActivity ? Visibility.Visible : Visibility.Collapsed;
        AnswerPanel.Visibility = isActivity ? Visibility.Collapsed : Visibility.Visible;
        if (!isActivity)
        {
            StopActivityAnimation();
            return;
        }

        ActivityStatusText.Text = activity switch
        {
            OverlayActivity.Listening => "Слушаю вас",
            OverlayActivity.Thinking => "Формирую ответ",
            _ => "Ответ помощника",
        };
        ActivityMessageText.Text = MessageText.Text;
        var accent = activity == OverlayActivity.Listening
            ? System.Windows.Media.Color.FromRgb(59, 214, 150)
            : System.Windows.Media.Color.FromRgb(90, 154, 255);
        ActivityCore.Fill = new SolidColorBrush(accent);
        InnerActivityRing.Fill = new SolidColorBrush(System.Windows.Media.Color.Multiply(accent, .9f));
        OuterActivityRing.Fill = new SolidColorBrush(accent);
        StartActivityAnimation();
    }

    private void StartActivityAnimation()
    {
        var pulse = new DoubleAnimation(.82, 1.08, TimeSpan.FromMilliseconds(680))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        var outerPulse = pulse.Clone();
        outerPulse.Duration = TimeSpan.FromMilliseconds(920);
        OuterRingScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, outerPulse);
        OuterRingScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, outerPulse.Clone());
        InnerRingScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pulse);
        InnerRingScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pulse.Clone());
    }

    private void StopActivityAnimation()
    {
        OuterRingScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        OuterRingScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        InnerRingScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        InnerRingScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || IsButton(e.OriginalSource as DependencyObject)) return;
        try
        {
            DragMove();
            PositionChangedByUser?.Invoke(this, new OverlayPositionChangedEventArgs(Left, Top));
        }
        catch (InvalidOperationException) { }
    }

    private static bool IsButton(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is System.Windows.Controls.Primitives.ButtonBase) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private void Expand_Click(object sender, RoutedEventArgs e) => ExpandRequested?.Invoke(this, EventArgs.Empty);
    private void Hide_Click(object sender, RoutedEventArgs e) => HideOverlay();

    private void ApplyStyles()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, -20).ToInt64();
        SetWindowLongPtr(handle, -20, new nint(style | 0x00000080L | 0x08000000L));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}

public sealed record OverlayPositionChangedEventArgs(double Left, double Top);
