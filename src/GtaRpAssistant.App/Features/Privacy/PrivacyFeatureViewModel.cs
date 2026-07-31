using System.Windows.Input;
using GtaRpAssistant.Core;
using GtaRpAssistant.Infrastructure.Windows;
using Microsoft.Extensions.Logging;
using GtaRpAssistant.App.Services;
using GtaRpAssistant.App.Shell;

namespace GtaRpAssistant.App.Features;

public sealed class PrivacyFeatureViewModel : FeatureViewModel
{
    private readonly SettingsService _settingsService;
    private readonly ITextToSpeechService _textToSpeech;
    private readonly AssistantSessionCoordinator _coordinator;
    private readonly AudioSessionController _audioSession;
    private readonly ILogger<PrivacyFeatureViewModel> _logger;
    private readonly ApplicationUiState _ui;
    private readonly SettingsWorkspace _workspace;
    private readonly ICommand _saveSettingsCommand;
    private IReadOnlyList<string> _voices = [];
    private IReadOnlyList<AudioOutputDevice> _voiceOutputDevices = [];

    public PrivacyFeatureViewModel(
        ApplicationUiState ui,
        SettingsWorkspace workspace,
        SettingsSaveCoordinator save,
        SettingsService settingsService,
        ITextToSpeechService textToSpeech,
        AssistantSessionCoordinator coordinator,
        AudioSessionController audioSession,
        ILogger<PrivacyFeatureViewModel> logger) : base(ui, workspace)
    {
        _ui = ui;
        _workspace = workspace;
        _saveSettingsCommand = save.SaveCommand;
        _settingsService = settingsService;
        _textToSpeech = textToSpeech;
        _coordinator = coordinator;
        _audioSession = audioSession;
        _logger = logger;
        RefreshVoiceDevicesCommand = new RelayCommand(RefreshVoiceDevices);
        ClearContextCommand = new RelayCommand(ClearContext);
        save.SettingsSaved += (_, value) => { if (value.VoiceMode == 1 && ProviderSettingsMigration.IsWindowsTtsEnabled(value)) EnsureVoiceDevicesLoaded(); };
    }

    public SettingsEditor Settings => _workspace.Settings;
    public IReadOnlyList<string> Voices { get => _voices; private set => Set(ref _voices, value); }
    public IReadOnlyList<AudioOutputDevice> VoiceOutputDevices { get => _voiceOutputDevices; private set => Set(ref _voiceOutputDevices, value); }
    public string PipelineStatus => _ui.PipelineStatus;
    public ICommand RefreshVoiceDevicesCommand { get; }
    public ICommand ClearContextCommand { get; }
    public ICommand SaveSettingsCommand => _saveSettingsCommand;

    public void Initialize()
    {
        VoiceOutputDevices = [new(-1, "Устройство Windows по умолчанию")];
        if (_settingsService.Current.VoiceMode == 1 && ProviderSettingsMigration.IsWindowsTtsEnabled(_settingsService.Current)) RefreshVoiceDevices();
    }

    public void EnsureVoiceDevicesLoaded()
    {
        if (Voices.Count == 0) RefreshVoiceDevices();
    }

    public async Task SpeakIfEnabledAsync(AssistantAnswer answer)
    {
        if (!answer.CanSpeak || _settingsService.Current.VoiceMode != 1 || !ProviderSettingsMigration.IsWindowsTtsEnabled(_settingsService.Current)) return;
        try
        {
            await _textToSpeech.SpeakAsync(answer.Message, _settingsService.Current.VoiceName,
                _settingsService.Current.VoiceOutputDevice, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("TTS failed; type={ErrorType}", ex.GetType().Name);
        }
    }

    public void StopSpeech() => _textToSpeech.Stop();

    private void RefreshVoiceDevices()
    {
        try
        {
            Voices = _textToSpeech.GetVoices();
            VoiceOutputDevices = _textToSpeech.GetOutputDevices();
            _ui.PipelineStatus = $"Найдено голосов Windows: {Voices.Count}; устройств вывода: {VoiceOutputDevices.Count}.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning("TTS device enumeration failed; type={ErrorType}", ex.GetType().Name);
            _ui.PipelineStatus = $"TTS недоступен: {ex.GetType().Name}";
        }
    }

    private void ClearContext()
    {
        _coordinator.ClearContext();
        _audioSession.ClearBuffers();
        _ui.PipelineStatus = "Временные audio/transcript буферы очищены.";
    }
}
