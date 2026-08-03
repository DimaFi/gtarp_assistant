using GtaRpAssistant.Core;

namespace GtaRpAssistant.App;

public sealed record VoiceTranscriptDecision(string Text);

public sealed class VoiceInteractionCoordinator : IDisposable
{
    private readonly VoiceInteractionStateMachine _stateMachine;
    private readonly AssistantSessionCoordinator? _assistant;
    private readonly object _sync = new();
    private CancellationTokenSource? _requestCancellation;
    private CancellationTokenSource? _deadlineCancellation;
    private TaskCompletionSource<VoiceTranscriptDecision>? _previewDecision;

    public VoiceInteractionCoordinator(VoiceInteractionStateMachine stateMachine, AssistantSessionCoordinator? assistant = null)
    {
        _stateMachine = stateMachine;
        _assistant = assistant;
        _stateMachine.StateChanged += OnStateChanged;
    }

    public VoiceInteractionSnapshot Snapshot => _stateMachine.Snapshot;
    public CancellationToken RequestCancellationToken
    {
        get
        {
            lock (_sync) return _requestCancellation?.Token ?? CancellationToken.None;
        }
    }

    public event EventHandler<VoiceInteractionSnapshot>? StateChanged;

    public bool Toggle(VoiceInteractionMode mode, bool autoSubmit, TimeSpan maxDuration)
    {
        CancellationToken deadlineToken;
        lock (_sync)
        {
            if (_stateMachine.Snapshot.IsActive)
            {
                deadlineToken = default;
            }
            else
            {
                DisposeRequestCore();
                _requestCancellation = new();
                _deadlineCancellation = new();
                deadlineToken = _deadlineCancellation.Token;
            }
        }

        if (deadlineToken == default)
        {
            Cancel("Отменено пользователем.");
            return false;
        }

        try
        {
            _stateMachine.Start(mode, autoSubmit, maxDuration);
            _ = CancelAtDeadlineAsync(deadlineToken, maxDuration);
            return true;
        }
        catch
        {
            lock (_sync) DisposeRequestCore();
            throw;
        }
    }

    public bool TryMarkListening() => _stateMachine.TryTransition(VoiceInteractionState.Listening);
    public bool TryMarkSpeechDetected() => _stateMachine.TryTransition(VoiceInteractionState.SpeechDetected);
    public bool TryMarkTranscribing() => _stateMachine.TryTransition(VoiceInteractionState.Transcribing);
    public bool TryMarkSubmitting() => _stateMachine.TryTransition(VoiceInteractionState.Submitting);
    public bool TryMarkAnswerReady(string? detail = null) => _stateMachine.TryTransition(VoiceInteractionState.AnswerReady, detail: detail);

    public async Task<AssistantAnswer?> SubmitAsync(
        TranscriptEntry entry,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (_assistant is null) throw new InvalidOperationException("Answer coordinator не настроен.");
        if (!_stateMachine.TryTransition(VoiceInteractionState.Submitting))
            throw new InvalidOperationException($"Невозможно отправить голосовой вопрос из состояния {_stateMachine.Snapshot.State}.");
        try
        {
            var answer = await _assistant.ProcessAsync(new(
                entry,
                AssistantActivationKind.ManualVoice,
                settings.Server,
                settings.AllowCloud,
                settings.VoiceMode == 1), cancellationToken);
            _stateMachine.TryTransition(VoiceInteractionState.AnswerReady, detail: answer?.DiagnosticReason ?? "Ответ не показан.");
            return answer;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Cancel("Голосовой запрос отменён во время обработки.");
            throw;
        }
        catch
        {
            _stateMachine.TryTransition(VoiceInteractionState.Faulted, detail: "Ошибка обработки голосового вопроса.");
            throw;
        }
    }

    public async Task<VoiceTranscriptDecision> WaitForPreviewDecisionAsync(string transcript, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(transcript)) throw new ArgumentException("Распознанный текст пуст.", nameof(transcript));
        var normalized = transcript.Trim();
        TaskCompletionSource<VoiceTranscriptDecision>? decision = null;
        lock (_sync)
        {
            _deadlineCancellation?.Cancel();
            if (!_stateMachine.Snapshot.AutoSubmit)
            {
                decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _previewDecision = decision;
            }
        }

        if (!_stateMachine.TryTransition(VoiceInteractionState.Preview, normalized))
            throw new InvalidOperationException($"Невозможно показать preview из состояния {_stateMachine.Snapshot.State}.");
        if (_stateMachine.Snapshot.AutoSubmit) return new(normalized);

        using var registration = cancellationToken.Register(() => decision!.TrySetCanceled(cancellationToken));
        try
        {
            return await decision!.Task;
        }
        finally
        {
            lock (_sync)
                if (ReferenceEquals(_previewDecision, decision)) _previewDecision = null;
        }
    }

    public bool ConfirmPreview(string editedTranscript)
    {
        if (string.IsNullOrWhiteSpace(editedTranscript) || _stateMachine.Snapshot.State != VoiceInteractionState.Preview) return false;
        TaskCompletionSource<VoiceTranscriptDecision>? decision;
        lock (_sync) decision = _previewDecision;
        return decision?.TrySetResult(new(editedTranscript.Trim())) == true;
    }

    public void Cancel(string detail)
    {
        CancellationTokenSource? request;
        CancellationTokenSource? deadline;
        lock (_sync)
        {
            request = _requestCancellation;
            deadline = _deadlineCancellation;
        }
        request?.Cancel();
        deadline?.Cancel();
        TaskCompletionSource<VoiceTranscriptDecision>? preview;
        lock (_sync) preview = _previewDecision;
        preview?.TrySetCanceled();
        if (_stateMachine.Snapshot.IsActive)
            _stateMachine.TryTransition(VoiceInteractionState.Cancelled, detail: detail);
    }

    public void Reset()
    {
        var shouldReset = false;
        lock (_sync)
        {
            if (_stateMachine.Snapshot.State is VoiceInteractionState.Cancelled or VoiceInteractionState.Faulted or VoiceInteractionState.AnswerReady)
                shouldReset = true;
        }
        if (shouldReset) _stateMachine.Reset();
        lock (_sync) DisposeRequestCore();
    }

    private async Task CancelAtDeadlineAsync(CancellationToken cancellationToken, TimeSpan maxDuration)
    {
        try
        {
            await Task.Delay(maxDuration, cancellationToken);
            Cancel("Истекло время голосового запроса.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void DisposeRequestCore()
    {
        _deadlineCancellation?.Cancel();
        _deadlineCancellation?.Dispose();
        _deadlineCancellation = null;
        _requestCancellation?.Dispose();
        _requestCancellation = null;
        _previewDecision = null;
    }

    private void OnStateChanged(object? sender, VoiceInteractionSnapshot snapshot) =>
        StateChanged?.Invoke(this, snapshot);

    public void Dispose()
    {
        Cancel("Завершение приложения.");
        lock (_sync) DisposeRequestCore();
        _stateMachine.StateChanged -= OnStateChanged;
    }
}
