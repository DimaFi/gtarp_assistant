using System.Threading.Channels;
using GtaRpAssistant.Core;
using GtaRpAssistant.Infrastructure.Windows;
using Microsoft.Extensions.Logging;

namespace GtaRpAssistant.App;

public sealed record AudioSessionStartOptions(MicrophoneDeviceInfo Microphone, RenderDeviceInfo? RenderDevice, AppSettings Settings, string? ApiKey, GameProcessInfo? GameProcess);

public sealed class AudioSessionController(
    AssistantSessionCoordinator coordinator,
    ISpeechToTextProviderCatalog speechToTextProviders,
    VoiceInteractionCoordinator voiceInteraction,
    ILogger<AudioSessionController> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AdaptiveEnergyVoiceActivityDetector _microphoneVad = new();
    private readonly AdaptiveEnergyVoiceActivityDetector _gameVad = new();
    private readonly EnergyAudioSegmenter _microphoneSegmenter = new();
    private readonly EnergyAudioSegmenter _gameSegmenter = new();
    private AudioRingBuffer _audioBuffer = new(TimeSpan.FromMinutes(3));
    private WasapiMicrophoneCaptureService? _microphone;
    private IAudioCaptureService? _gameAudio;
    private IReadOnlyList<ISpeechToTextProvider> _sttRoute = [];
    private SpeechToTextProviderRoute? _sttProviderRoute;
    private CancellationTokenSource? _cancellation;
    private Channel<AudioSegment>? _microphoneSegments;
    private Channel<AudioSegment>? _gameSegments;
    private SemaphoreSlim? _segmentSignal;
    private Task? _worker;
    private RenderDeviceInfo? _renderDevice;
    private GameProcessInfo? _gameProcess;
    private AppSettings _sessionSettings = new();
    private bool _gameAudioSuspended;
    private DateTimeOffset _manualVoiceUntil;
    private long _lastMicrophoneLevelTick;

    public bool IsListening => _microphone is not null;
    public bool IsGameAudioActive => _gameAudio is not null;
    public VoiceInteractionSnapshot VoiceInteraction => voiceInteraction.Snapshot;
    public string GameCaptureMode { get; private set; } = "off";
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? TranscriptRecognized;
    public event EventHandler<double>? MicrophoneLevelChanged;
    public event EventHandler? StateChanged;

    public async Task StartAsync(AudioSessionStartOptions options, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_microphone is not null) return;
            var value = ProviderSettingsMigration.Migrate(options.Settings);
            if (value.EnableGameAudio && options.RenderDevice is null) throw new InvalidOperationException("Выберите устройство вывода.");
            _renderDevice = options.RenderDevice;
            _gameProcess = options.GameProcess;
            _sessionSettings = value;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _sttProviderRoute = await speechToTextProviders.CreateAvailableRouteAsync(value, _cancellation.Token);
            _sttRoute = _sttProviderRoute.Providers;
            if (_sttRoute.Count == 0) throw new InvalidOperationException("STT route выключен или не содержит доступных провайдеров.");
            _microphoneSegmenter.Reset(); _gameSegmenter.Reset();
            _microphoneSegments = CreateChannel(); _gameSegments = CreateChannel(); _segmentSignal = new(0);
            _worker = RunWorkerAsync(_cancellation.Token);
            _microphone = new(options.Microphone.Id);
            _microphone.FrameCaptured += OnMicrophoneFrame;
            await _microphone.StartAsync(_cancellation.Token);
            if (voiceInteraction.Snapshot.State == VoiceInteractionState.Arming)
                voiceInteraction.TryMarkListening();
            if (value.EnableGameAudio && !_gameAudioSuspended) await StartGameAudioCoreAsync(_cancellation.Token);
            StatusChanged?.Invoke(this, _gameAudio is null ? "WASAPI microphone активен: PCM16 mono 16 kHz." : $"Микрофон и game audio активны; режим: {GameCaptureMode}.");
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            await StopCoreAsync();
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _gate.Release(); }
    }

    public bool ToggleManualVoiceRequest(bool autoSubmit)
    {
        var started = voiceInteraction.Toggle(VoiceInteractionMode.Toggle, autoSubmit, TimeSpan.FromSeconds(20));
        _manualVoiceUntil = started ? DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20) : DateTimeOffset.MinValue;
        if (started && IsListening) voiceInteraction.TryMarkListening();
        StateChanged?.Invoke(this, EventArgs.Empty);
        return started;
    }

    public void CancelManualVoiceRequest(string detail = "Голосовой вопрос отменён.")
    {
        _manualVoiceUntil = DateTimeOffset.MinValue;
        voiceInteraction.Cancel(detail);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool ConfirmManualVoiceRequest(string editedTranscript) =>
        voiceInteraction.ConfirmPreview(editedTranscript);

    public async Task RebindGameProcessAsync(GameProcessInfo? process)
    {
        await _gate.WaitAsync();
        try
        {
            _gameProcess = process;
            if (_microphone is null || !_sessionSettings.EnableGameAudio || _gameAudioSuspended) return;
            await StopGameAudioCoreAsync();
            if (_cancellation is not null) await StartGameAudioCoreAsync(_cancellation.Token);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _gate.Release(); }
    }

    public async Task ApplyPerformanceAsync(ProcessPerformanceSnapshot snapshot)
    {
        var shouldSuspend = !snapshot.Actions.GameAudioStt;
        await _gate.WaitAsync();
        try
        {
            if (_gameAudioSuspended == shouldSuspend) return;
            _gameAudioSuspended = shouldSuspend;
            if (shouldSuspend && _gameAudio is not null)
            {
                await StopGameAudioCoreAsync();
                logger.LogInformation("Game audio STT suspended by performance controller; cpu={Cpu:F1}; working_set={WorkingSet}", snapshot.CpuPercent, snapshot.WorkingSetBytes);
            }
            else if (!shouldSuspend && _microphone is not null && _sessionSettings.EnableGameAudio && _cancellation is not null) await StartGameAudioCoreAsync(_cancellation.Token);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _gate.Release(); }
    }

    public void SetBufferDuration(TimeSpan duration)
    {
        if (IsListening) return;
        _audioBuffer.Clear();
        _audioBuffer = new(duration);
    }

    public void ClearBuffers()
    {
        _audioBuffer.Clear(); _microphoneSegmenter.Reset(); _gameSegmenter.Reset();
    }

    private static Channel<AudioSegment> CreateChannel() => Channel.CreateBounded<AudioSegment>(new BoundedChannelOptions(4) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest });

    private async Task StartGameAudioCoreAsync(CancellationToken cancellationToken)
    {
        if (_renderDevice is null || _gameAudio is not null) return;
        if (_sessionSettings.PreferProcessLoopback && _gameProcess is not null && ProcessLoopbackCaptureService.IsSupported)
        {
            var capture = new ProcessLoopbackCaptureService(_gameProcess.ProcessId);
            capture.FrameCaptured += OnGameAudioFrame;
            try
            {
                await capture.StartAsync(cancellationToken);
                _gameAudio = capture; GameCaptureMode = $"process-specific PID {_gameProcess.ProcessId}"; return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                capture.FrameCaptured -= OnGameAudioFrame;
                await capture.DisposeAsync();
                logger.LogWarning("Process loopback unavailable; type={ErrorType}; using system fallback", ex.GetType().Name);
            }
        }
        var fallback = new WasapiGameAudioCaptureService(_renderDevice.Id);
        fallback.FrameCaptured += OnGameAudioFrame;
        await fallback.StartAsync(cancellationToken);
        _gameAudio = fallback; GameCaptureMode = "system loopback fallback";
    }

    private async Task StopCoreAsync()
    {
        await StopGameAudioCoreAsync();
        var microphone = _microphone; _microphone = null;
        if (microphone is not null)
        {
            microphone.FrameCaptured -= OnMicrophoneFrame;
            await microphone.StopAsync(CancellationToken.None);
            await microphone.DisposeAsync();
        }
        _cancellation?.Cancel();
        if (_worker is not null) { try { await _worker; } catch (OperationCanceledException) { } }
        _worker = null; _microphoneSegments = null; _gameSegments = null;
        _segmentSignal?.Dispose(); _segmentSignal = null;
        _cancellation?.Dispose(); _cancellation = null;
        if (_sttProviderRoute is not null) await _sttProviderRoute.DisposeAsync();
        _sttProviderRoute = null; _sttRoute = [];
        _renderDevice = null;
        _microphoneSegmenter.Reset(); _gameSegmenter.Reset();
        MicrophoneLevelChanged?.Invoke(this, 0);
        if (voiceInteraction.Snapshot.IsActive)
            CancelManualVoiceRequest("Аудиосессия остановлена.");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task StopGameAudioCoreAsync()
    {
        var gameAudio = _gameAudio; _gameAudio = null;
        if (gameAudio is not null)
        {
            gameAudio.FrameCaptured -= OnGameAudioFrame;
            await gameAudio.StopAsync(CancellationToken.None);
            await gameAudio.DisposeAsync();
        }
        GameCaptureMode = "off";
    }

    private void OnMicrophoneFrame(object? sender, AudioFrameEventArgs e) => ProcessFrame(e, _microphoneVad, _microphoneSegmenter, _microphoneSegments);
    private void OnGameAudioFrame(object? sender, AudioFrameEventArgs e) => ProcessFrame(e, _gameVad, _gameSegmenter, _gameSegments);
    private void ProcessFrame(AudioFrameEventArgs e, AdaptiveEnergyVoiceActivityDetector vad, EnergyAudioSegmenter segmenter, Channel<AudioSegment>? channel)
    {
        if (_cancellation is null) return;
        _audioBuffer.Write(e.Source, e.Samples.Span);
        var activity = vad.Process(e.Samples.Span, e.SampleRate);
        if (e.Source == AudioSourceKind.UserMicrophone)
        {
            var tick = Environment.TickCount64;
            var previous = Interlocked.Read(ref _lastMicrophoneLevelTick);
            if (tick - previous >= 80 && Interlocked.CompareExchange(ref _lastMicrophoneLevelTick, tick, previous) == previous)
                MicrophoneLevelChanged?.Invoke(this, MicrophoneTestService.CalculateLevel(e.Samples.Span));
        }
        if (e.Source == AudioSourceKind.UserMicrophone
            && activity.SpeechDetected
            && DateTimeOffset.UtcNow <= _manualVoiceUntil)
            voiceInteraction.TryMarkSpeechDetected();
        var segment = segmenter.Process(e.Source, e.Samples.Span, activity.SpeechDetected, DateTimeOffset.UtcNow);
        if (segment is not null && channel?.Writer.TryWrite(segment) == true) _segmentSignal?.Release();
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await _segmentSignal!.WaitAsync(cancellationToken);
            if (_microphoneSegments!.Reader.TryRead(out var microphone)) { await TranscribeAsync(microphone, cancellationToken); continue; }
            if (_gameSegments!.Reader.TryRead(out var game)) await TranscribeAsync(game, cancellationToken);
        }
    }

    private async Task TranscribeAsync(AudioSegment segment, CancellationToken cancellationToken)
    {
        var manualRequest = segment.Source == AudioSourceKind.UserMicrophone
            && DateTimeOffset.UtcNow <= _manualVoiceUntil;
        using var requestCancellation = manualRequest
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, voiceInteraction.RequestCancellationToken)
            : null;
        var operationToken = requestCancellation?.Token ?? cancellationToken;
        try
        {
            if (_sttRoute.Count == 0) return;
            var value = _sessionSettings;
            if (manualRequest) voiceInteraction.TryMarkTranscribing();
            StatusChanged?.Invoke(this, $"STT {segment.Source}: сегмент {(segment.EndedAt - segment.StartedAt).TotalSeconds:F1} сек…");
            TranscriptResult? result = null;
            foreach (var provider in _sttRoute)
            {
                if (!provider.Capabilities.IsLocal && !value.AllowCloud) continue;
                if (segment.Source == AudioSourceKind.GameAudio && !provider.Capabilities.IsLocal && !value.AllowGameAudioCloud)
                {
                    logger.LogInformation("Game audio cloud transcription blocked by privacy setting; provider={Provider}", provider.Id);
                    continue;
                }
                try
                {
                    result = await provider.TranscribeAsync(segment, operationToken);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("STT provider failed; provider={Provider}; type={ErrorType}", provider.Id, ex.GetType().Name);
                }
            }
            if (result is null) throw new InvalidOperationException("Ни один STT provider не обработал сегмент.");
            var entry = new TranscriptEntry(segment.Id, segment.Source, segment.StartedAt, segment.EndedAt, result.Text, result.Confidence);
            if (entry.Source == AudioSourceKind.GameAudio)
            {
                await coordinator.ProcessAsync(new(entry, AssistantActivationKind.AutomaticVoice, value.Server, value.AllowCloud, false), cancellationToken);
                StatusChanged?.Invoke(this, "Game audio добавлен только в контекст; активация запрещена.");
                return;
            }
            TranscriptRecognized?.Invoke(this, result.Text);
            var activation = manualRequest ? AssistantActivationKind.ManualVoice : AssistantActivationKind.AutomaticVoice;
            if (activation == AssistantActivationKind.AutomaticVoice && SettingValues.Proactive(value) == ProactiveMode.Off) return;
            if (manualRequest)
            {
                var decision = await voiceInteraction.WaitForPreviewDecisionAsync(result.Text, operationToken);
                entry = entry with { Text = decision.Text };
            }
            if (manualRequest)
                await voiceInteraction.SubmitAsync(entry, value, operationToken);
            else
                await coordinator.ProcessAsync(
                    new(entry, activation, value.Server, value.AllowCloud, false),
                    operationToken);
            if (manualRequest)
            {
                _manualVoiceUntil = DateTimeOffset.MinValue;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (OperationCanceledException) when (manualRequest && operationToken.IsCancellationRequested)
        {
            StatusChanged?.Invoke(this, "Голосовой вопрос отменён.");
        }
        catch (Exception ex)
        {
            logger.LogWarning("STT segment failed; type={ErrorType}; source={Source}", ex.GetType().Name, segment.Source);
            StatusChanged?.Invoke(this, $"STT faulted: {ex.GetType().Name}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _audioBuffer.Clear();
        _gate.Dispose();
    }
}
