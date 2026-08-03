namespace GtaRpAssistant.Core;

public sealed class VoiceInteractionStateMachine
{
    private static readonly IReadOnlyDictionary<VoiceInteractionState, VoiceInteractionState[]> Allowed =
        new Dictionary<VoiceInteractionState, VoiceInteractionState[]>
        {
            [VoiceInteractionState.Idle] = [VoiceInteractionState.Arming],
            [VoiceInteractionState.Arming] = [VoiceInteractionState.Listening, VoiceInteractionState.Cancelled, VoiceInteractionState.Faulted],
            [VoiceInteractionState.Listening] = [VoiceInteractionState.SpeechDetected, VoiceInteractionState.Transcribing, VoiceInteractionState.Cancelled, VoiceInteractionState.Faulted],
            [VoiceInteractionState.SpeechDetected] = [VoiceInteractionState.Listening, VoiceInteractionState.Transcribing, VoiceInteractionState.Cancelled, VoiceInteractionState.Faulted],
            [VoiceInteractionState.Transcribing] = [VoiceInteractionState.Preview, VoiceInteractionState.Cancelled, VoiceInteractionState.Faulted],
            [VoiceInteractionState.Preview] = [VoiceInteractionState.Submitting, VoiceInteractionState.Listening, VoiceInteractionState.Cancelled, VoiceInteractionState.Faulted],
            [VoiceInteractionState.Submitting] = [VoiceInteractionState.AnswerReady, VoiceInteractionState.Cancelled, VoiceInteractionState.Faulted],
            [VoiceInteractionState.AnswerReady] = [VoiceInteractionState.Speaking, VoiceInteractionState.Idle, VoiceInteractionState.Arming],
            [VoiceInteractionState.Speaking] = [VoiceInteractionState.Idle, VoiceInteractionState.Cancelled, VoiceInteractionState.Faulted],
            [VoiceInteractionState.Cancelled] = [VoiceInteractionState.Idle, VoiceInteractionState.Arming],
            [VoiceInteractionState.Faulted] = [VoiceInteractionState.Idle, VoiceInteractionState.Arming],
        };

    private readonly object _sync = new();
    private VoiceInteractionSnapshot _snapshot = IdleSnapshot();

    public VoiceInteractionSnapshot Snapshot
    {
        get
        {
            lock (_sync) return _snapshot;
        }
    }

    public event EventHandler<VoiceInteractionSnapshot>? StateChanged;

    public VoiceInteractionSnapshot Start(VoiceInteractionMode mode, bool autoSubmit, TimeSpan maxDuration)
    {
        if (maxDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxDuration));
        VoiceInteractionSnapshot next;
        lock (_sync)
        {
            if (_snapshot.IsActive && _snapshot.State != VoiceInteractionState.AnswerReady)
                throw new InvalidOperationException($"Голосовой запрос уже активен: {_snapshot.State}.");

            var now = DateTimeOffset.UtcNow;
            next = new(Guid.NewGuid(), mode, VoiceInteractionState.Arming, now, now + maxDuration, null, null, autoSubmit);
            _snapshot = next;
        }
        StateChanged?.Invoke(this, next);
        return next;
    }

    public bool TryTransition(VoiceInteractionState next, string? transcript = null, string? detail = null)
    {
        VoiceInteractionSnapshot changed;
        lock (_sync)
        {
            if (_snapshot.State == next) return true;
            if (!Allowed.TryGetValue(_snapshot.State, out var targets) || !targets.Contains(next)) return false;
            changed = _snapshot with
            {
                State = next,
                Transcript = transcript ?? _snapshot.Transcript,
                Detail = detail,
            };
            _snapshot = changed;
        }
        StateChanged?.Invoke(this, changed);
        return true;
    }

    public void Reset()
    {
        VoiceInteractionSnapshot changed;
        lock (_sync)
        {
            if (_snapshot.State == VoiceInteractionState.Idle) return;
            if (!Allowed.TryGetValue(_snapshot.State, out var targets) || !targets.Contains(VoiceInteractionState.Idle))
                throw new InvalidOperationException($"Недопустимый сброс голосового запроса из состояния {_snapshot.State}.");
            changed = IdleSnapshot();
            _snapshot = changed;
        }
        StateChanged?.Invoke(this, changed);
    }

    private static VoiceInteractionSnapshot IdleSnapshot() =>
        new(Guid.Empty, VoiceInteractionMode.Toggle, VoiceInteractionState.Idle, DateTimeOffset.MinValue, null, null, null, false);
}
