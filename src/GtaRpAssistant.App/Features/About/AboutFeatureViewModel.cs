using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Input;
using GtaRpAssistant.App.Services;
using GtaRpAssistant.App.Shell;

namespace GtaRpAssistant.App.Features;

public sealed class AboutFeatureViewModel : FeatureViewModel, IDisposable
{
    private readonly ApplicationUiState _ui;
    private readonly SettingsWorkspace _workspace;
    private SettingsEditor _settings;

    public AboutFeatureViewModel(ApplicationUiState ui, SettingsWorkspace workspace) : base(ui, workspace)
    {
        _ui = ui;
        _workspace = workspace;
        _settings = workspace.Settings;
        _ui.PropertyChanged += OnUiChanged;
        _workspace.PropertyChanged += OnWorkspaceChanged;
        _settings.PropertyChanged += OnSettingsChanged;
        RefreshCommand = new RelayCommand(Refresh);
    }

    public string Version => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(App).Assembly.GetName().Version?.ToString(3)
        ?? "неизвестна";

    public string Platform => $"{RuntimeInformation.OSDescription.Trim()} · {RuntimeInformation.ProcessArchitecture}";
    public string DisplayVersion => Version.Split('+', 2)[0];
    public string Runtime => $".NET {Environment.Version}";
    public string Knowledge => $"{_ui.TotalArticleCount} статей · {_ui.OfficialArticleCount} официальных · {_ui.CommunityArticleCount} community";
    public string KnowledgeCount => _ui.TotalArticleCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string KnowledgeBreakdown => $"{_ui.OfficialArticleCount} официальных · {_ui.CommunityArticleCount} community";
    public string LocalAi => string.IsNullOrWhiteSpace(_workspace.Settings.Model)
        ? "Модель не выбрана · knowledge-first режим доступен"
        : $"{_workspace.Settings.Model} · {_workspace.Settings.Endpoint}";
    public string Cloud => _workspace.Settings.AllowCloud ? "Разрешён пользователем" : "Выключен";
    public string DataDirectory => AppPaths.DataDirectory;
    public string DiagnosticSummary => string.Join(Environment.NewLine,
        $"GTA RP Assistant {Version}",
        Platform,
        Runtime,
        $"Knowledge: {Knowledge}",
        $"Local AI: {LocalAi}",
        $"Cloud: {Cloud}",
        $"Data: {DataDirectory}");

    public ICommand RefreshCommand { get; }

    public void Dispose()
    {
        _ui.PropertyChanged -= OnUiChanged;
        _workspace.PropertyChanged -= OnWorkspaceChanged;
        _settings.PropertyChanged -= OnSettingsChanged;
    }

    private void OnUiChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ApplicationUiState.OfficialArticleCount) or nameof(ApplicationUiState.CommunityArticleCount))
            Refresh();
    }

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsWorkspace.Settings) || ReferenceEquals(_settings, _workspace.Settings)) return;
        _settings.PropertyChanged -= OnSettingsChanged;
        _settings = _workspace.Settings;
        _settings.PropertyChanged += OnSettingsChanged;
        Refresh();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsEditor.Model) or nameof(SettingsEditor.Endpoint) or nameof(SettingsEditor.AllowCloud))
            Refresh();
    }

    private void Refresh()
    {
        Raise(nameof(Version));
        Raise(nameof(Platform));
        Raise(nameof(Runtime));
        Raise(nameof(KnowledgeCount));
        Raise(nameof(KnowledgeBreakdown));
        Raise(nameof(Knowledge));
        Raise(nameof(LocalAi));
        Raise(nameof(Cloud));
        Raise(nameof(DataDirectory));
        Raise(nameof(DiagnosticSummary));
    }
}
