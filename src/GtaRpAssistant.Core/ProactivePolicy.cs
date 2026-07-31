namespace GtaRpAssistant.Core;

public sealed class ProactivePolicy(TimeProvider? timeProvider = null) : IProactivePolicy
{
    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _shown = new();
    private readonly Dictionary<string, DateTimeOffset> _topics = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private DateTimeOffset? _snoozedUntil;
    private bool _sessionSnoozed;

    public bool CanProcess(AssistantActivationKind activation, string topic, DateTimeOffset now, out string reason)
    {
        if (activation is not AssistantActivationKind.AutomaticVoice)
        {
            reason = "manual";
            return true;
        }

        lock (_gate)
        {
            Prune(now);
            if (_sessionSnoozed || _snoozedUntil > now)
            {
                reason = "do_not_disturb";
                return false;
            }
            if (_shown.Count > 0 && now - _shown.Last() < TimeSpan.FromMinutes(1))
            {
                reason = "one_per_minute";
                return false;
            }
            if (_shown.Count >= 3)
            {
                reason = "three_per_ten_minutes";
                return false;
            }
            if (_topics.TryGetValue(NormalizeTopic(topic), out var last) && now - last < TimeSpan.FromMinutes(2))
            {
                reason = "topic_cooldown";
                return false;
            }
            reason = "allowed";
            return true;
        }
    }

    public void RecordShown(AssistantActivationKind activation, string topic, DateTimeOffset now)
    {
        if (activation is not AssistantActivationKind.AutomaticVoice) return;
        lock (_gate)
        {
            Prune(now);
            _shown.Enqueue(now);
            _topics[NormalizeTopic(topic)] = now;
        }
    }

    public void Snooze(TimeSpan duration)
    {
        lock (_gate) _snoozedUntil = _time.GetUtcNow() + duration;
    }

    public void SnoozeForSession()
    {
        lock (_gate) _sessionSnoozed = true;
    }

    public void Resume()
    {
        lock (_gate)
        {
            _sessionSnoozed = false;
            _snoozedUntil = null;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        while (_shown.Count > 0 && now - _shown.Peek() >= TimeSpan.FromMinutes(10)) _shown.Dequeue();
        foreach (var key in _topics.Where(x => now - x.Value >= TimeSpan.FromMinutes(2)).Select(x => x.Key).ToArray()) _topics.Remove(key);
        if (_snoozedUntil <= now) _snoozedUntil = null;
    }

    private static string NormalizeTopic(string topic) => TranscriptDeduplicator.Normalize(topic);
}
