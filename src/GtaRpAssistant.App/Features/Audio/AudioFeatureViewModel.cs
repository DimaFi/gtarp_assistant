using System.Windows.Input;
using GtaRpAssistant.Core;
using GtaRpAssistant.Infrastructure.Windows;
using Microsoft.Extensions.Logging;
using GtaRpAssistant.App.Services;
using GtaRpAssistant.App.Shell;

namespace GtaRpAssistant.App.Features;

public sealed class AudioFeatureViewModel : FeatureViewModel
{
    private readonly SettingsService _settingsService;
    private readonly AudioSessionController _audioSession;
    private readonly GameSessionMonitor _gameMonitor;
    private readonly ILogger<AudioFeatureViewModel> _logger;
    private readonly ApplicationUiState _ui;
    private readonly SettingsWorkspace _workspace;
    private readonly AudioDeviceSelectionState _selection;
    private readonly IUiDispatcher _dispatcher;
    private IReadOnlyList<MicrophoneDeviceInfo> _microphones = [];
    private IReadOnlyList<RenderDeviceInfo> _renderDevices = [];
    private readonly ICommand _saveSettingsCommand;

    public AudioFeatureViewModel(
        ApplicationUiState ui,
        SettingsWorkspace workspace,
        SettingsSaveCoordinator save,
        AudioDeviceSelectionState selection,
        SettingsService settingsService,
        AudioSessionController audioSession,
        GameSessionMonitor gameMonitor,
        IUiDispatcher dispatcher,
        ILogger<AudioFeatureViewModel> logger) : base(ui, workspace)
    {
        _ui = ui;
        _workspace = workspace;
        _selection = selection;
        _dispatcher = dispatcher;
        _settingsService = settingsService;
        _audioSession = audioSession;
        _gameMonitor = gameMonitor;
        _logger = logger;
        _saveSettingsCommand = save.SaveCommand;
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        ToggleListeningCommand = new AsyncRelayCommand(ToggleListeningAsync);
        audioSession.StatusChanged += (_, status) => _dispatcher.Invoke(() => _ui.PipelineStatus = status);
        audioSession.StateChanged += (_, _) => _dispatcher.Invoke(() =>
        {
            Raise(nameof(ListeningButtonText));
            Raise(nameof(AudioSettingsEnabled));
            RuntimeStateChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public event EventHandler? RuntimeStateChanged;
    public SettingsEditor Settings => _workspace.Settings;
    public IReadOnlyList<MicrophoneDeviceInfo> Microphones { get => _microphones; private set => Set(ref _microphones, value); }
    public IReadOnlyList<RenderDeviceInfo> RenderDevices { get => _renderDevices; private set => Set(ref _renderDevices, value); }
    public MicrophoneDeviceInfo? SelectedMicrophone { get => _selection.Microphone; set { if (!Equals(_selection.Microphone, value)) { _selection.Microphone = value; Raise(); } } }
    public RenderDeviceInfo? SelectedRenderDevice { get => _selection.RenderDevice; set { if (!Equals(_selection.RenderDevice, value)) { _selection.RenderDevice = value; Raise(); } } }
    public string ListeningButtonText => _audioSession.IsListening ? "Остановить прослушивание" : "Начать прослушивание";
    public bool AudioSettingsEnabled => !_audioSession.IsListening;
    public bool IsListening => _audioSession.IsListening;
    public bool IsGameAudioActive => _audioSession.IsGameAudioActive;
    public string GameCaptureMode => _audioSession.GameCaptureMode;
    public ICommand RefreshDevicesCommand { get; }
    public ICommand ToggleListeningCommand { get; }
    public ICommand SaveSettingsCommand => _saveSettingsCommand;

    public void Initialize() => RefreshDevices();

    public async Task BeginManualVoiceRequestAsync()
    {
        _audioSession.BeginManualVoiceRequest();
        _ui.PipelineStatus = "Голосовой вопрос: говорите в течение 20 секунд.";
        if (!_audioSession.IsListening) await ToggleListeningAsync();
    }

    public Task StopAsync() => _audioSession.StopAsync();
    public Task RebindGameProcessAsync(GameProcessInfo? process) => _audioSession.RebindGameProcessAsync(process);
    public Task ApplyPerformanceAsync(ProcessPerformanceSnapshot snapshot) => _audioSession.ApplyPerformanceAsync(snapshot);

    private void RefreshDevices()
    {
        try
        {
            Microphones = WasapiDeviceCatalog.GetActiveMicrophones();
            RenderDevices = WasapiRenderDeviceCatalog.GetActiveRenderDevices();
            SelectedMicrophone = Microphones.FirstOrDefault(x => x.Id == _settingsService.Current.MicrophoneDeviceId) ?? Microphones.FirstOrDefault();
            SelectedRenderDevice = RenderDevices.FirstOrDefault(x => x.Id == _settingsService.Current.RenderDeviceId) ?? RenderDevices.FirstOrDefault();
            if (Microphones.Count == 0) _ui.PipelineStatus = "Активные микрофоны не найдены.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Audio device enumeration failed; type={ErrorType}", ex.GetType().Name);
            _ui.PipelineStatus = "Не удалось получить список аудиоустройств.";
        }
    }

    private async Task ToggleListeningAsync()
    {
        try
        {
            if (_audioSession.IsListening)
            {
                await _audioSession.StopAsync();
                return;
            }
            if (SelectedMicrophone is null)
            {
                _ui.PipelineStatus = "Выберите микрофон.";
                return;
            }
            var runtime = Settings.ToSettings(SelectedMicrophone.Id, SelectedRenderDevice?.Id, _settingsService.Current);
            await _audioSession.StartAsync(new(
                SelectedMicrophone, SelectedRenderDevice, runtime,
                string.IsNullOrWhiteSpace(_workspace.ApiKey) ? null : _workspace.ApiKey,
                _gameMonitor.Current), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError("Audio session failed; type={ErrorType}", ex.GetType().Name);
            _ui.PipelineStatus = $"Ошибка аудиосессии: {ex.Message}";
        }
    }
}
