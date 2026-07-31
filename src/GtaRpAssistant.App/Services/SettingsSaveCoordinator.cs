using System.Windows.Input;
using GtaRpAssistant.Core;
using GtaRpAssistant.App.Features;
using GtaRpAssistant.App.Shell;
using Microsoft.Extensions.Logging;

namespace GtaRpAssistant.App.Services;

public sealed class SettingsSaveCoordinator
{
    private readonly SettingsApplicationService _application;
    private readonly SettingsWorkspace _workspace;
    private readonly AudioDeviceSelectionState _audioSelection;
    private readonly ApplicationUiState _ui;
    private readonly ILogger<SettingsSaveCoordinator> _logger;

    public SettingsSaveCoordinator(
        SettingsApplicationService application,
        SettingsWorkspace workspace,
        AudioDeviceSelectionState audioSelection,
        ApplicationUiState ui,
        ILogger<SettingsSaveCoordinator> logger)
    {
        _application = application;
        _workspace = workspace;
        _audioSelection = audioSelection;
        _ui = ui;
        _logger = logger;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
    }

    public ICommand SaveCommand { get; }
    public event EventHandler<AppSettings>? SettingsSaved;

    public async Task SaveAsync()
    {
        try
        {
            var value = await _application.SaveAsync(
                _workspace.Settings,
                _workspace.ApiKey,
                _workspace.CloudApiKey,
                _audioSelection.Microphone?.Id,
                _audioSelection.RenderDevice?.Id,
                CancellationToken.None);
            _ui.PipelineStatus = "Настройки применены. API-ключи защищены DPAPI CurrentUser.";
            SettingsSaved?.Invoke(this, value);
        }
        catch (Exception ex)
        {
            _logger.LogError("Settings save failed; type={ErrorType}", ex.GetType().Name);
            _ui.PipelineStatus = $"Не удалось сохранить настройки: {ex.GetType().Name}";
        }
    }
}
