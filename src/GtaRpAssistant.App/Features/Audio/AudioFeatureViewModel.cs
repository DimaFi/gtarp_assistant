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
    private readonly MicrophoneTestService _microphoneTest;
    private readonly IAppDialogService _dialogs;
    private IReadOnlyList<MicrophoneDeviceInfo> _microphones = [];
    private IReadOnlyList<RenderDeviceInfo> _renderDevices = [];
    private readonly ICommand _saveSettingsCommand;
    private double _microphoneLevel;
    private bool _isTestingMicrophone;

    public AudioFeatureViewModel(
        ApplicationUiState ui,
        SettingsWorkspace workspace,
        SettingsSaveCoordinator save,
        AudioDeviceSelectionState selection,
        SettingsService settingsService,
        AudioSessionController audioSession,
        GameSessionMonitor gameMonitor,
        IUiDispatcher dispatcher,
        MicrophoneTestService microphoneTest,
        IAppDialogService dialogs,
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
        _microphoneTest = microphoneTest;
        _dialogs = dialogs;
        _saveSettingsCommand = save.SaveCommand;
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        ToggleListeningCommand = new AsyncRelayCommand(ToggleListeningAsync);
        TestMicrophoneCommand = new AsyncRelayCommand(TestMicrophoneAsync, () => !IsTestingMicrophone && !_audioSession.IsListening);
        BrowseEmbeddedSttPackCommand = new RelayCommand(BrowseEmbeddedSttPack);
        audioSession.StatusChanged += (_, status) => _dispatcher.Invoke(() => _ui.PipelineStatus = status);
        audioSession.MicrophoneLevelChanged += (_, level) => _dispatcher.Invoke(() => MicrophoneLevel = level);
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
    public double MicrophoneLevel { get => _microphoneLevel; private set { if (Set(ref _microphoneLevel, value)) Raise(nameof(MicrophoneLevelText)); } }
    public string MicrophoneLevelText => $"Уровень: {MicrophoneLevel:P0}";
    public bool IsTestingMicrophone { get => _isTestingMicrophone; private set => Set(ref _isTestingMicrophone, value); }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand ToggleListeningCommand { get; }
    public ICommand TestMicrophoneCommand { get; }
    public ICommand BrowseEmbeddedSttPackCommand { get; }
    public ICommand SaveSettingsCommand => _saveSettingsCommand;

    public void Initialize() => RefreshDevices();

    private void BrowseEmbeddedSttPack()
    {
        var selected = _dialogs.PickFolder("Выберите папку локального STT-пака", Settings.EmbeddedSttPackPath);
        if (!string.IsNullOrWhiteSpace(selected)) Settings.EmbeddedSttPackPath = selected;
    }

    public async Task<bool> BeginManualVoiceRequestAsync(VoiceInteractionMode mode)
    {
        if (SelectedMicrophone is null)
        {
            _ui.PipelineStatus = "Выберите микрофон.";
            return false;
        }
        if (!_audioSession.ToggleManualVoiceRequest(mode, Settings.VoiceAutoSubmit))
        {
            _ui.PipelineStatus = "Голосовой вопрос отменён.";
            return false;
        }
        _ui.PipelineStatus = "Голосовой вопрос: говорите в течение 20 секунд.";
        if (!_audioSession.IsListening) await ToggleListeningAsync();
        if (_audioSession.IsListening) return true;
        _audioSession.CancelManualVoiceRequest("Не удалось запустить микрофон или STT.");
        return false;
    }

    public Task StopAsync() => _audioSession.StopAsync();
    public bool EndManualVoiceRequest() => _audioSession.EndManualVoiceRequest();
    public bool ConfirmManualVoiceRequest(string editedTranscript) => _audioSession.ConfirmManualVoiceRequest(editedTranscript);
    public void CancelManualVoiceRequest() => _audioSession.CancelManualVoiceRequest();
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
            _logger.LogError("Audio session failed; type={ErrorType}; message={ErrorMessage}", ex.GetType().Name, ex.Message);
            _ui.PipelineStatus = $"Ошибка аудиосессии: {ex.Message}";
        }
    }

    private async Task TestMicrophoneAsync()
    {
        if (_audioSession.IsListening)
        {
            _ui.PipelineStatus = "Остановите активное прослушивание перед тестом микрофона.";
            return;
        }
        if (SelectedMicrophone is null)
        {
            _ui.PipelineStatus = "Выберите микрофон.";
            return;
        }
        IsTestingMicrophone = true;
        _ui.PipelineStatus = "Тест микрофона: говорите в течение 3 секунд…";
        try
        {
            var result = await _microphoneTest.RunAsync(
                SelectedMicrophone.Id,
                TimeSpan.FromSeconds(3),
                level => _dispatcher.Invoke(() => MicrophoneLevel = level),
                CancellationToken.None);
            _ui.PipelineStatus = result.SignalDetected
                ? $"Микрофон работает. Пиковый уровень: {result.PeakLevel:P0}."
                : "Сигнал почти не обнаружен. Проверьте устройство и разрешение Windows на микрофон.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Microphone test failed; type={ErrorType}", ex.GetType().Name);
            _ui.PipelineStatus = $"Тест микрофона не выполнен: {ex.Message}";
        }
        finally
        {
            IsTestingMicrophone = false;
            MicrophoneLevel = 0;
        }
    }
}
