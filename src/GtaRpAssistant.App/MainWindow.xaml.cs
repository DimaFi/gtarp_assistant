using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using GtaRpAssistant.App.Services;
using GtaRpAssistant.App.Shell;
using GtaRpAssistant.App.DesignSystem;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IAppDialogService _dialogs;
    private readonly ApplicationExecutionMode _executionMode;
    private readonly SettingsService _settings;
    private readonly SettingsWorkspace _workspace;
    private readonly ThemeService _themes;
    private HwndSource? _source;
    private GlobalVoiceHotkeyHook? _voiceHook;
    private bool _voiceHotkeyRegistered;
    private bool _globalInputConfigured;

    public MainWindow(MainViewModel viewModel, IAppDialogService dialogs, ApplicationExecutionMode executionMode, SettingsService settings, SettingsWorkspace workspace, ThemeService themes)
    {
        _viewModel = viewModel;
        _dialogs = dialogs;
        _executionMode = executionMode;
        _settings = settings;
        _workspace = workspace;
        _themes = themes;
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
        Closed += (_, _) => UnregisterGlobalInput();
    }

    private async void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        var next = _themes.Current == ApplicationTheme.Gray ? ApplicationTheme.Light : ApplicationTheme.Gray;
        _themes.Apply((int)next);
        ApplyNativeTitleBarTheme();
        _workspace.Settings.AppearanceTheme = (int)next;
        await _settings.SaveAsync(_settings.Current with { AppearanceTheme = (int)next }, CancellationToken.None);
    }

    private async void ExitApplication_Click(object sender, RoutedEventArgs e)
    {
        IsEnabled = false;
        await ((App)System.Windows.Application.Current).RequestExitAsync();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync(CancellationToken.None);
            if (!_executionMode.IsAutomation && !_settings.Current.FirstRunCompleted)
            {
                var wizard = new FirstRunWindow { Owner = this };
                wizard.ShowDialog();
                await _settings.SaveAsync(_settings.Current with { FirstRunCompleted = true }, CancellationToken.None);
                _viewModel.SelectFeature(wizard.KnowledgeOnly ? "knowledge" : "providers");
            }
            ConfigureGlobalInput();
        }
        catch (Exception ex) { _dialogs.ShowError("GTA RP Assistant", $"Ошибка инициализации: {ex.Message}"); }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (_executionMode.IsAutomation) return;
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source.AddHook(WndProc);
        ApplyNativeTitleBarTheme();
    }

    private void ApplyNativeTitleBarTheme()
    {
        if (_executionMode.IsAutomation) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        var dark = _themes.Current == ApplicationTheme.Gray ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    private void ConfigureGlobalInput()
    {
        if (_executionMode.IsAutomation || _globalInputConfigured) return;
        _globalInputConfigured = true;
        var handle = new WindowInteropHelper(this).Handle;
        const uint modifiers = 0x0001 | 0x0002 | 0x4000;
        var failures = new List<GlobalHotkeyAction>();
        if (!RegisterHotKey(handle, 1, modifiers, 0x51)) failures.Add(GlobalHotkeyAction.ToggleOverlay);
        if (!RegisterHotKey(handle, 2, modifiers, 0x50)) failures.Add(GlobalHotkeyAction.TogglePause);
        if (!RegisterHotKey(handle, 4, modifiers, 0x53)) failures.Add(GlobalHotkeyAction.ManualVision);

        _voiceHook = new();
        _voiceHook.Gesture += OnVoiceHotkeyGesture;
        try { _voiceHook.Start(); }
        catch
        {
            _voiceHook.Gesture -= OnVoiceHotkeyGesture;
            _voiceHook.Dispose();
            _voiceHook = null;
            if (SettingValues.VoiceHotkey(_settings.Current) == VoiceInteractionMode.Hold)
                failures.Add(GlobalHotkeyAction.ManualVoiceHold);
        }

        ConfigureVoiceHotkey(_settings.Current, failures);
        _settings.Changed += OnSettingsChanged;
        if (failures.Count > 0) _viewModel.ReportHotkeyFailures(failures);
    }

    private void ConfigureVoiceHotkey(AppSettings settings, List<GlobalHotkeyAction>? failures = null)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var mode = SettingValues.VoiceHotkey(settings);
        if (mode == VoiceInteractionMode.Hold)
        {
            if (_voiceHotkeyRegistered) UnregisterHotKey(handle, 3);
            _voiceHotkeyRegistered = false;
            if (_voiceHook is null) (failures ?? []).Add(GlobalHotkeyAction.ManualVoiceHold);
            return;
        }

        if (_voiceHotkeyRegistered) return;
        _voiceHotkeyRegistered = RegisterHotKey(handle, 3, 0x0001 | 0x0002 | 0x4000, 0x41);
        if (!_voiceHotkeyRegistered) (failures ?? []).Add(GlobalHotkeyAction.ManualVoice);
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) => Dispatcher.Invoke(() =>
    {
        var failures = new List<GlobalHotkeyAction>();
        ConfigureVoiceHotkey(settings, failures);
        if (failures.Count > 0) _viewModel.ReportHotkeyFailures(failures);
    });

    private void OnVoiceHotkeyGesture(object? sender, VoiceHotkeyGesture gesture)
    {
        if (SettingValues.VoiceHotkey(_settings.Current) != VoiceInteractionMode.Hold) return;
        _ = Dispatcher.InvokeAsync(() => gesture switch
        {
            VoiceHotkeyGesture.Pressed => _viewModel.HandleManualVoicePressedAsync(),
            VoiceHotkeyGesture.Released => _viewModel.HandleManualVoiceReleasedAsync(),
            _ => Task.CompletedTask,
        });
    }

    private void UnregisterGlobalInput()
    {
        _settings.Changed -= OnSettingsChanged;
        var handle = new WindowInteropHelper(this).Handle;
        for (var id = 1; id <= 4; id++) UnregisterHotKey(handle, id);
        _voiceHotkeyRegistered = false;
        if (_voiceHook is not null)
        {
            _voiceHook.Gesture -= OnVoiceHotkeyGesture;
            _voiceHook.Dispose();
            _voiceHook = null;
        }
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
            GlobalHotkeyAction.ManualVoice when SettingValues.VoiceHotkey(_settings.Current) == VoiceInteractionMode.Toggle => _viewModel.HandleManualVoiceHotkeyAsync(),
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
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
