namespace GtaRpAssistant.Core;

public sealed class TranscriptBuffer(TimeSpan ttl, TimeProvider? timeProvider = null)
{
    private readonly object _gate = new();
    private readonly List<TranscriptEntry> _entries = [];
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private TimeSpan _ttl = ttl;

    public void Add(TranscriptEntry entry)
    {
        lock (_gate)
        {
            PruneCore();
            _entries.Add(entry);
            _entries.Sort((a, b) => a.StartedAt.CompareTo(b.StartedAt));
        }
    }

    public IReadOnlyList<TranscriptEntry> Snapshot()
    {
        lock (_gate) { PruneCore(); return _entries.ToArray(); }
    }

    public void Clear() { lock (_gate) _entries.Clear(); }
    public void SetTtl(TimeSpan value)
    {
        if (value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(value));
        lock (_gate) { _ttl = value; PruneCore(); }
    }
    public bool Remove(Guid id) { lock (_gate) return _entries.RemoveAll(x => x.Id == id) > 0; }
    private void PruneCore() => _entries.RemoveAll(x => x.EndedAt < _time.GetUtcNow() - _ttl);
}
