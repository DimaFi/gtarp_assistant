using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GtaRpAssistant.App;

public partial class OverlayWindow : Window
{
    private CancellationTokenSource? _hideCancellation;

    public OverlayWindow() { InitializeComponent(); SourceInitialized += (_, _) => ApplyStyles(); }
    public event EventHandler? ExpandRequested;

    public async Task ShowAsync(OverlayPresentation presentation, TimeSpan duration, string position = "TopRight", nint targetWindow = default, CancellationToken cancellationToken = default)
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
        OverlayWindowPositioner.Position(this, position, targetWindow);
        try { await Task.Delay(duration, linked.Token); }
        catch (OperationCanceledException) { }
        if (ReferenceEquals(_hideCancellation, hideCancellation)) Hide();
    }

    public void HideOverlay() { _hideCancellation?.Cancel(); Hide(); }

    private void ApplyActivityLayout(OverlayActivity activity)
    {
        var isActivity = activity != OverlayActivity.None;
        Width = isActivity ? 250 : 420;
        TitleText.Visibility = isActivity ? Visibility.Collapsed : Visibility.Visible;
        MessageText.Visibility = isActivity ? Visibility.Collapsed : Visibility.Visible;
        StepsList.Visibility = !isActivity && StepsList.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SourceBadge.Visibility = isActivity ? Visibility.Collapsed : Visibility.Visible;
        ActionsPanel.Visibility = isActivity ? Visibility.Collapsed : Visibility.Visible;
        StatusPill.Margin = isActivity ? new Thickness(0) : new Thickness(12, 0, 0, 0);
        StatusPill.HorizontalAlignment = isActivity
            ? System.Windows.HorizontalAlignment.Left
            : System.Windows.HorizontalAlignment.Stretch;
        System.Windows.Controls.Grid.SetColumn(StatusPill, isActivity ? 0 : 1);
        System.Windows.Controls.Grid.SetColumnSpan(StatusPill, isActivity ? 2 : 1);
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
