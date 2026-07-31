using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using GtaRpAssistant.App.Services;
using GtaRpAssistant.App.Shell;

namespace GtaRpAssistant.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IAppDialogService _dialogs;
    private readonly ApplicationExecutionMode _executionMode;
    private HwndSource? _source;

    public MainWindow(MainViewModel viewModel, IAppDialogService dialogs, ApplicationExecutionMode executionMode)
    {
        _viewModel = viewModel;
        _dialogs = dialogs;
        _executionMode = executionMode;
        InitializeComponent();
        if (executionMode.IsAutomation)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -20_000;
            Top = -20_000;
            ShowActivated = false;
            ShowInTaskbar = false;
        }
        DataContext = viewModel;
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => UnregisterHotkeys();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync(CancellationToken.None);
        }
        catch (Exception ex) { _dialogs.ShowError("GTA RP Assistant", $"Ошибка инициализации: {ex.Message}"); }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (_executionMode.IsAutomation) return;
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source.AddHook(WndProc);
        var registered = RegisterHotKey(handle, 1, 0x0001 | 0x0002, 0x51)
            & RegisterHotKey(handle, 2, 0x0001 | 0x0002, 0x50)
            & RegisterHotKey(handle, 3, 0x0001 | 0x0002, 0x41)
            & RegisterHotKey(handle, 4, 0x0001 | 0x0002, 0x53);
        if (!registered) _viewModel.ReportHotkeyFailure();
    }

    private void UnregisterHotkeys()
    {
        var handle = new WindowInteropHelper(this).Handle;
        for (var id = 1; id <= 4; id++) UnregisterHotKey(handle, id);
        if (_source is not null) _source.RemoveHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != 0x0312) return 0;
        handled = true;
        _ = GlobalHotkeyMap.FromRegistrationId(wParam.ToInt32()) switch
        {
            GlobalHotkeyAction.ToggleOverlay => _viewModel.HandleOverlayHotkeyAsync(),
            GlobalHotkeyAction.TogglePause => _viewModel.TogglePauseFromHotkeyAsync(),
            GlobalHotkeyAction.ManualVoice => _viewModel.HandleManualVoiceHotkeyAsync(),
            GlobalHotkeyAction.ManualVision => _viewModel.HandleVisionHotkeyAsync(),
            _ => Task.CompletedTask,
        };
        return 0;
    }

    public Task TogglePauseAsync() => _viewModel.TogglePauseFromHotkeyAsync();

    public bool IsApplicationInitialized => _viewModel.IsInitialized;

    public void RunUiSmoke()
    {
        if (_viewModel.NavigationItems.Count == 0)
            throw new InvalidOperationException("UI smoke requires at least one feature.");

        foreach (var item in _viewModel.NavigationItems)
        {
            item.SelectCommand.Execute(null);
            UpdateLayout();

            if (!ReferenceEquals(_viewModel.SelectedPage, item.Content) || !ReferenceEquals(FeatureHost.Content, item.Content))
                throw new InvalidOperationException($"Feature '{item.Id}' was not selected in the shell.");
            if (!item.IsSelected || _viewModel.NavigationItems.Count(x => x.IsSelected) != 1)
                throw new InvalidOperationException($"Feature '{item.Id}' has an invalid navigation selection state.");
            if (item.Content is not FrameworkElement content || content.ActualWidth <= 0 || content.ActualHeight <= 0)
                throw new InvalidOperationException($"Feature '{item.Id}' did not produce a visible layout.");
            if (string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(content)))
                throw new InvalidOperationException($"Feature '{item.Id}' has no root AutomationId.");

            var automationIds = UiVisualTestHelper.GetCustomAutomationIds(content);
            if (automationIds.Count < 2)
                throw new InvalidOperationException($"Feature '{item.Id}' exposes no actionable automation elements.");
            UiVisualTestHelper.ValidateAutomationContract(content, AutomationProperties.GetAutomationId(content));
        }

        _viewModel.NavigationItems[0].SelectCommand.Execute(null);
        UpdateLayout();
    }

    public IReadOnlyList<string> CaptureFeatureSnapshots(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        RunUiSmoke();

        var paths = new List<string>(_viewModel.NavigationItems.Count);
        foreach (var item in _viewModel.NavigationItems)
        {
            item.SelectCommand.Execute(null);
            UpdateLayout();
            var path = Path.Combine(outputDirectory, $"{item.Id}.png");
            CaptureShellSnapshot(path);
            paths.Add(path);
        }

        _viewModel.NavigationItems[0].SelectCommand.Execute(null);
        UpdateLayout();
        return paths;
    }

    public string CaptureFeatureSnapshot(string outputDirectory, string featureId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureId);
        Directory.CreateDirectory(outputDirectory);
        var content = SelectFeatureForAutomation(featureId);
        UiVisualTestHelper.ValidateAutomationContract(content, AutomationProperties.GetAutomationId(content));
        var path = Path.Combine(outputDirectory, $"{featureId}.png");
        CaptureShellSnapshot(path);
        return path;
    }

    private void CaptureShellSnapshot(string path) => UiVisualTestHelper.CaptureWindow(this, path);

    public FrameworkElement SelectFeatureForAutomation(string id)
    {
        var item = _viewModel.NavigationItems.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Feature '{id}' is not registered.");
        item.SelectCommand.Execute(null);
        UpdateLayout();
        return item.Content as FrameworkElement
            ?? throw new InvalidOperationException($"Feature '{id}' is not a FrameworkElement.");
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hWnd, int id);
}
