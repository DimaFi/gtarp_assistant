namespace GtaRpAssistant.Core;

public sealed class SessionStateMachine
{
    private static readonly IReadOnlyDictionary<AssistantSessionState, AssistantSessionState[]> Allowed = new Dictionary<AssistantSessionState, AssistantSessionState[]>
    {
        [AssistantSessionState.Dormant] = [AssistantSessionState.WaitingForGame],
        [AssistantSessionState.WaitingForGame] = [AssistantSessionState.Listening, AssistantSessionState.Paused, AssistantSessionState.Faulted],
        [AssistantSessionState.Listening] = [AssistantSessionState.SpeechDetected, AssistantSessionState.WaitingForGame, AssistantSessionState.Paused, AssistantSessionState.Faulted],
        [AssistantSessionState.SpeechDetected] = [AssistantSessionState.Transcribing, AssistantSessionState.Listening, AssistantSessionState.Paused, AssistantSessionState.Faulted],
        [AssistantSessionState.Transcribing] = [AssistantSessionState.EvaluatingIntent, AssistantSessionState.Listening, AssistantSessionState.Paused, AssistantSessionState.Faulted],
        [AssistantSessionState.EvaluatingIntent] = [AssistantSessionState.SearchingKnowledge, AssistantSessionState.Listening, AssistantSessionState.Paused, AssistantSessionState.Faulted],
        [AssistantSessionState.SearchingKnowledge] = [AssistantSessionState.GeneratingAnswer, AssistantSessionState.ValidatingAnswer, AssistantSessionState.Listening, AssistantSessionState.Paused, AssistantSessionState.Faulted],
        [AssistantSessionState.GeneratingAnswer] = [AssistantSessionState.ValidatingAnswer, AssistantSessionState.Paused, AssistantSessionState.Faulted],
        [AssistantSessionState.ValidatingAnswer] = [AssistantSessionState.ShowingOverlay, AssistantSessionState.Listening, AssistantSessionState.Paused, AssistantSessionState.Faulted],
        [AssistantSessionState.ShowingOverlay] = [AssistantSessionState.Cooldown, AssistantSessionState.Listening, AssistantSessionState.Paused],
        [AssistantSessionState.Cooldown] = [AssistantSessionState.Listening, AssistantSessionState.Paused],
        [AssistantSessionState.Paused] = [AssistantSessionState.Listening, AssistantSessionState.WaitingForGame],
        [AssistantSessionState.Faulted] = [AssistantSessionState.WaitingForGame, AssistantSessionState.Listening],
    };

    public AssistantSessionState State { get; private set; } = AssistantSessionState.Dormant;
    public event EventHandler<AssistantSessionState>? StateChanged;

    public void TransitionTo(AssistantSessionState next)
    {
        if (!Allowed.TryGetValue(State, out var targets) || !targets.Contains(next)) throw new InvalidOperationException($"Недопустимый переход {State} -> {next}");
        State = next;
        StateChanged?.Invoke(this, next);
    }

    public bool TryTransitionTo(AssistantSessionState next)
    {
        if (State == next) return true;
        if (!Allowed.TryGetValue(State, out var targets) || !targets.Contains(next)) return false;
        TransitionTo(next);
        return true;
    }
}

public sealed class AdaptiveEnergyVoiceActivityDetector(double multiplier = 2.5) : IVoiceActivityDetector
{
    private double _noiseFloor = 150;
    public VoiceActivityResult Process(ReadOnlySpan<short> samples, int sampleRate)
    {
        if (samples.IsEmpty) return new(false, 0);
        double sum = 0;
        foreach (var sample in samples) sum += (double)sample * sample;
        var rms = Math.Sqrt(sum / samples.Length);
        var speech = rms > Math.Max(300, _noiseFloor * multiplier);
        if (!speech) _noiseFloor = _noiseFloor * 0.98 + rms * 0.02;
        return new(speech, rms);
    }
}
