using System.IO;
using System.Windows;
using GtaRpAssistant.App.Features;
using GtaRpAssistant.App.Services;
using GtaRpAssistant.Core;
using GtaRpAssistant.Infrastructure.Windows;
using Microsoft.Extensions.Logging;

namespace GtaRpAssistant.App.Shell;

public sealed class ApplicationLifecycleCoordinator : IAsyncDisposable
{
    private readonly SettingsService _settingsService;
    private readonly SettingsApplicationService _settingsApplication;
    private readonly KnowledgeCatalogService _knowledgeCatalog;
    private readonly SettingsWorkspace _workspace;
    private readonly ApplicationUiState _ui;
    private readonly IAppDialogService _dialogs;
    private readonly RuleBasedIntentDetector _intent;
    private readonly AssistantSessionCoordinator _session;
    private readonly IProactivePolicy _proactivePolicy;
    private readonly OverlayService _overlay;
    private readonly GameSessionMonitor _gameMonitor;
    private readonly ProcessPerformanceMonitor _performanceMonitor;
    private readonly VisionWorkflowService _vision;
    private readonly AudioFeatureViewModel _audioFeature;
    private readonly PrivacyFeatureViewModel _privacyFeature;
    private readonly VoiceInteractionCoordinator _voiceInteraction;
    private readonly ILogger<ApplicationLifecycleCoordinator> _logger;
    private readonly ApplicationExecutionMode _executionMode;
    private bool _paused;

    public ApplicationLifecycleCoordinator(
        SettingsService settingsService,
        SettingsApplicationService settingsApplication,
        KnowledgeCatalogService knowledgeCatalog,
        SettingsWorkspace workspace,
        ApplicationUiState ui,
        IAppDialogService dialogs,
        RuleBasedIntentDetector intent,
        AssistantSessionCoordinator session,
        IProactivePolicy proactivePolicy,
        OverlayService overlay,
        GameSessionMonitor gameMonitor,
        ProcessPerformanceMonitor performanceMonitor,
        VisionWorkflowService vision,
        AudioFeatureViewModel audioFeature,
        PrivacyFeatureViewModel privacyFeature,
        VoiceInteractionCoordinator voiceInteraction,
        MicroModelOverlayCoordinator microModelOverlay,
        ApplicationExecutionMode executionMode,
        ILogger<ApplicationLifecycleCoordinator> logger)
    {
        _settingsService = settingsService;
        _settingsApplication = settingsApplication;
        _knowledgeCatalog = knowledgeCatalog;
        _workspace = workspace;
        _ui = ui;
        _dialogs = dialogs;
        _intent = intent;
        _session = session;
        _proactivePolicy = proactivePolicy;
        _overlay = overlay;
        _gameMonitor = gameMonitor;
        _performanceMonitor = performanceMonitor;
        _vision = vision;
        _audioFeature = audioFeature;
        _privacyFeature = privacyFeature;
        _voiceInteraction = voiceInteraction;
        _ = microModelOverlay;
        _executionMode = executionMode;
        _logger = logger;

        _session.StateChanged += (_, _) => Ui(UpdateAppStatus);
        _session.AnswerProduced += OnAnswerProduced;
        _gameMonitor.ProcessChanged += OnGameProcessChanged;
        _performanceMonitor.SnapshotAvailable += OnPerformanceSnapshot;
        _overlay.SnoozeRequested += (_, _) => _proactivePolicy.Snooze(TimeSpan.FromMinutes(5));
        _overlay.IncorrectReported += (_, answer) => _logger.LogWarning(
            "Incorrect hint reported; source={Source}; facts={FactCount}",
            answer.SourceTitle ?? "none",
            answer.UsedFactIds.Count);
        _overlay.DetailsRequested += (_, answer) => _dialogs.ShowAnswerDetails(answer);
        _audioFeature.RuntimeStateChanged += (_, _) => UpdateAppStatus();
        _overlay.VoicePreviewConfirmed += (_, text) =>
        {
            if (!_audioFeature.ConfirmManualVoiceRequest(text))
                _ui.PipelineStatus = "Voice preview уже недоступен.";
        };
        _overlay.VoicePreviewCancelled += (_, _) => _audioFeature.CancelManualVoiceRequest();
        _voiceInteraction.StateChanged += OnVoiceInteractionStateChanged;
    }

    public bool IsInitialized { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (IsInitialized) return;
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var loaded = await _settingsApplication.LoadAsync(cancellationToken);
        _workspace.Apply(loaded);
        _audioFeature.Initialize();
        _privacyFeature.Initialize();

        var catalog = await _knowledgeCatalog.InitializeAsync(cancellationToken);
        _ui.OfficialArticleCount = catalog.OfficialArticles;
        _ui.CommunityArticleCount = catalog.CommunityArticles;
        _session.Start(_executionMode.IsAutomation || !_settingsService.Current.WatchGta);
        if (!_executionMode.IsAutomation && _settingsService.Current.WatchGta)
            await _gameMonitor.StartAsync(cancellationToken);
        await _performanceMonitor.StartAsync(cancellationToken);

        _ui.PipelineStatus = $"Готово: статей {catalog.TotalArticles}. Transcript → Intent → Knowledge → Router → Validator → Overlay";
        IsInitialized = true;
        UpdateAppStatus();
    }

    public async Task TogglePauseAsync()
    {
        _paused = !_paused;
        _ui.IsPaused = _paused;
        _session.SetPaused(_paused);
        if (_paused)
        {
            _privacyFeature.StopSpeech();
            await _audioFeature.StopAsync();
        }
        UpdateAppStatus();
    }

    public async Task HandleOverlayHotkeyAsync()
    {
        if (_overlay.IsVisible)
        {
            await _overlay.HideAsync();
            return;
        }

        await _overlay.ShowAsync(
            new(
                AnswerDecision.AskForMoreInformation,
                "GTA RP Assistant",
                "Задайте вопрос в микрофон или введите его в симуляторе.",
                [],
                null,
                null,
                false,
                "Hotkey"),
            CancellationToken.None);
    }

    public async Task HandleManualVoiceHotkeyAsync()
    {
        if (await _audioFeature.BeginManualVoiceRequestAsync(VoiceInteractionMode.Toggle))
            _ = _overlay.ShowListeningAsync(CancellationToken.None);
        else if (_overlay.IsVisible)
            await _overlay.HideAsync();
    }

    public async Task HandleManualVoicePressedAsync()
    {
        if (await _audioFeature.BeginManualVoiceRequestAsync(VoiceInteractionMode.Hold))
            _ = _overlay.ShowListeningAsync(CancellationToken.None);
    }

    public async Task HandleManualVoiceReleasedAsync()
    {
        if (_audioFeature.EndManualVoiceRequest()) return;
        if (_overlay.IsVisible) await _overlay.HideAsync();
    }

    public void ReportHotkeyFailures(IReadOnlyCollection<GlobalHotkeyAction> failures)
    {
        var names = failures.Distinct().Select(x => x switch
        {
            GlobalHotkeyAction.ToggleOverlay => "показ оверлея",
            GlobalHotkeyAction.TogglePause => "пауза",
            GlobalHotkeyAction.ManualVoice => "голосовой toggle",
            GlobalHotkeyAction.ManualVoiceHold => "голосовой hold",
            GlobalHotkeyAction.ManualVision => "снимок экрана",
            _ => "неизвестная команда",
        });
        _ui.PipelineStatus = $"Конфликт глобальной клавиши: {string.Join(", ", names)}. Функция остаётся доступна из окна приложения.";
    }

    public async Task HandleVisionHotkeyAsync()
    {
        try
        {
            var owner = System.Windows.Application.Current.MainWindow
                ?? throw new InvalidOperationException("Главное окно недоступно.");
            _ui.PipelineStatus = "Подготовка ручного снимка окна GTA…";
            await _vision.RunAsync(owner, CancellationToken.None);
            _ui.PipelineStatus = "Ручной vision завершён; изображение удалено из памяти.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Manual vision failed; type={ErrorType}", ex.GetType().Name);
            _ui.PipelineStatus = $"Vision: {ex.Message}";
        }
    }

    private async void OnAnswerProduced(object? sender, AssistantAnswer answer) =>
        await _privacyFeature.SpeakIfEnabledAsync(answer);

    private async void OnVoiceInteractionStateChanged(object? sender, VoiceInteractionSnapshot snapshot)
    {
        try
        {
            if (snapshot.State == VoiceInteractionState.Preview && !snapshot.AutoSubmit && !string.IsNullOrWhiteSpace(snapshot.Transcript))
                await _overlay.ShowVoicePreviewAsync(snapshot.Transcript, CancellationToken.None);
            else if (snapshot.State is VoiceInteractionState.Submitting or VoiceInteractionState.Cancelled or VoiceInteractionState.Faulted)
                await _overlay.HideAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Voice preview overlay failed; type={ErrorType}", ex.GetType().Name);
        }
    }

    private async void OnGameProcessChanged(object? sender, GameProcessInfo? process)
    {
        _overlay.TargetWindowHandle = process?.MainWindowHandle ?? 0;
        _session.SetGameAvailable(process is not null);
        if (process is null)
            _logger.LogInformation("Game stopped");
        else
            _logger.LogInformation("Game detected; pid={ProcessId}", process.ProcessId);
        Ui(UpdateAppStatus);

        try
        {
            await _audioFeature.RebindGameProcessAsync(process);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Game audio rebind failed; type={ErrorType}", ex.GetType().Name);
        }
    }

    private async void OnPerformanceSnapshot(object? sender, ProcessPerformanceSnapshot snapshot)
    {
        try
        {
            await _audioFeature.ApplyPerformanceAsync(snapshot);
            if (!snapshot.Actions.ExperimentalProactivity && _intent.Mode == ProactiveMode.Experimental)
                _intent.Mode = ProactiveMode.Strict;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Performance action failed; type={ErrorType}", ex.GetType().Name);
        }
    }

    private void UpdateAppStatus()
    {
        _ui.AppStatus = _paused
            ? "● Наблюдение приостановлено"
            : _audioFeature.IsListening && _audioFeature.IsGameAudioActive
                ? $"● Микрофон и GTA audio активны · {_audioFeature.GameCaptureMode}"
                : _audioFeature.IsListening
                    ? "● Микрофон активен"
                    : _gameMonitor.Current is not null
                        ? "● GTA обнаружена · локальная обработка"
                        : "● Ожидание GTA · локальная обработка";
    }

    private static void Ui(Action action)
    {
        if (System.Windows.Application.Current.Dispatcher.CheckAccess()) action();
        else System.Windows.Application.Current.Dispatcher.Invoke(action);
    }

    public ValueTask DisposeAsync()
    {
        _voiceInteraction.StateChanged -= OnVoiceInteractionStateChanged;
        _privacyFeature.StopSpeech();
        return ValueTask.CompletedTask;
    }
}
