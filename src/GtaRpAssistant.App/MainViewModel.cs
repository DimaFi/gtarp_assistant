using System.IO;
using System.Windows.Input;
using GtaRpAssistant.App.Shell;

namespace GtaRpAssistant.App;

public sealed class MainViewModel : ObservableObject
{
    private readonly ApplicationLifecycleCoordinator _lifecycle;
    private object? _selectedPage;

    public MainViewModel(
        ApplicationUiState ui,
        FeatureRegistry featureRegistry,
        ApplicationLifecycleCoordinator lifecycle)
    {
        _lifecycle = lifecycle;
        ui.PropertyChanged += (_, e) => Raise(e.PropertyName);
        TogglePauseCommand = new AsyncRelayCommand(_lifecycle.TogglePauseAsync);

        NavigationItems = featureRegistry.Features
            .Select(x => new ShellNavigationItem(x.Id, x.Title, x.Symbol, x.Content, SelectPage))
            .ToArray();
        SelectPage(NavigationItems[0]);
        Ui = ui;
    }

    private ApplicationUiState Ui { get; }
    public string AppStatus => Ui.AppStatus;
    public IReadOnlyList<ShellNavigationItem> NavigationItems { get; }
    public object? SelectedPage { get => _selectedPage; private set => Set(ref _selectedPage, value); }
    public bool IsInitialized => _lifecycle.IsInitialized;
    public ICommand TogglePauseCommand { get; }

    public Task InitializeAsync(CancellationToken cancellationToken) =>
        _lifecycle.InitializeAsync(cancellationToken);

    public Task HandleOverlayHotkeyAsync() => _lifecycle.HandleOverlayHotkeyAsync();
    public Task HandleManualVoiceHotkeyAsync() => _lifecycle.HandleManualVoiceHotkeyAsync();
    public Task HandleManualVoicePressedAsync() => _lifecycle.HandleManualVoicePressedAsync();
    public Task HandleManualVoiceReleasedAsync() => _lifecycle.HandleManualVoiceReleasedAsync();
    public Task TogglePauseFromHotkeyAsync() => _lifecycle.TogglePauseAsync();
    public void ReportHotkeyFailures(IReadOnlyCollection<GlobalHotkeyAction> failures) => _lifecycle.ReportHotkeyFailures(failures);
    public Task HandleVisionHotkeyAsync() => _lifecycle.HandleVisionHotkeyAsync();

    public void SelectFeature(string id)
    {
        var item = NavigationItems.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (item is not null) SelectPage(item);
    }

    private void SelectPage(ShellNavigationItem selected)
    {
        foreach (var item in NavigationItems) item.IsSelected = ReferenceEquals(item, selected);
        SelectedPage = selected.Content;
    }
}

public static class AppPaths
{
    public static string DataDirectory { get; } = ResolveDataDirectory();

    private static string ResolveDataDirectory()
    {
        var overridden = Environment.GetEnvironmentVariable("GTA_RP_ASSISTANT_DATA_DIR");
        return string.IsNullOrWhiteSpace(overridden)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GtaRpAssistant")
            : Path.GetFullPath(overridden);
    }
}
