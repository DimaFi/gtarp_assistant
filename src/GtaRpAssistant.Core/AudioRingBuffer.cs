namespace GtaRpAssistant.Core;

public sealed class AudioRingBuffer : IAudioRingBuffer
{
    private sealed class Channel(int size)
    {
        public short[] Buffer { get; } = new short[size];
        public int Position { get; set; }
        public int Count { get; set; }
        public object Gate { get; } = new();
    }

    private readonly int _sampleRate;
    private readonly Dictionary<AudioSourceKind, Channel> _channels;

    public AudioRingBuffer(TimeSpan capacity, int sampleRate = 16_000)
    {
        if (capacity <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        Capacity = capacity;
        _sampleRate = sampleRate;
        var size = checked((int)(capacity.TotalSeconds * sampleRate));
        _channels = Enum.GetValues<AudioSourceKind>().ToDictionary(x => x, _ => new Channel(size));
    }

    public TimeSpan Capacity { get; }

    public void Write(AudioSourceKind source, ReadOnlySpan<short> samples)
    {
        var channel = _channels[source];
        lock (channel.Gate)
        {
            if (samples.Length >= channel.Buffer.Length)
            {
                samples[^channel.Buffer.Length..].CopyTo(channel.Buffer);
                channel.Position = 0;
                channel.Count = channel.Buffer.Length;
                return;
            }
            var first = Math.Min(samples.Length, channel.Buffer.Length - channel.Position);
            samples[..first].CopyTo(channel.Buffer.AsSpan(channel.Position));
            samples[first..].CopyTo(channel.Buffer);
            channel.Position = (channel.Position + samples.Length) % channel.Buffer.Length;
            channel.Count = Math.Min(channel.Buffer.Length, channel.Count + samples.Length);
        }
    }

    public AudioSnapshot ReadLast(AudioSourceKind source, TimeSpan duration)
    {
        var channel = _channels[source];
        lock (channel.Gate)
        {
            var requested = Math.Min(channel.Count, Math.Max(0, checked((int)(duration.TotalSeconds * _sampleRate))));
            var result = new short[requested];
            var start = (channel.Position - requested + channel.Buffer.Length) % channel.Buffer.Length;
            var first = Math.Min(requested, channel.Buffer.Length - start);
            channel.Buffer.AsSpan(start, first).CopyTo(result);
            channel.Buffer.AsSpan(0, requested - first).CopyTo(result.AsSpan(first));
            return new AudioSnapshot(source, _sampleRate, result);
        }
    }

    public void Clear()
    {
        foreach (var channel in _channels.Values)
        {
            lock (channel.Gate)
            {
                Array.Clear(channel.Buffer);
                channel.Position = channel.Count = 0;
            }
        }
    }
}
