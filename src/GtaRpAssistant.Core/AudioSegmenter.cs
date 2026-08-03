using System.Buffers.Binary;

namespace GtaRpAssistant.Core;

public sealed class EnergyAudioSegmenter
{
    private readonly int _sampleRate;
    private readonly int _minSamples;
    private readonly int _maxSamples;
    private readonly int _silenceToEndSamples;
    private readonly int _postRollSamples;
    private readonly short[] _preRoll;
    private readonly short[] _segment;
    private int _prePosition;
    private int _preCount;
    private int _segmentCount;
    private int _silenceSamples;
    private bool _active;

    public EnergyAudioSegmenter(int sampleRate = 16_000, TimeSpan? preRoll = null, TimeSpan? postRoll = null, TimeSpan? silenceToEnd = null, TimeSpan? minimum = null, TimeSpan? maximum = null)
    {
        _sampleRate = sampleRate;
        _preRoll = new short[Samples(preRoll ?? TimeSpan.FromMilliseconds(250))];
        _postRollSamples = Samples(postRoll ?? TimeSpan.FromMilliseconds(400));
        _silenceToEndSamples = Samples(silenceToEnd ?? TimeSpan.FromMilliseconds(700));
        _minSamples = Samples(minimum ?? TimeSpan.FromMilliseconds(300));
        _maxSamples = Samples(maximum ?? TimeSpan.FromSeconds(20));
        _segment = new short[_maxSamples + _preRoll.Length + _silenceToEndSamples];
    }

    public bool IsActive => _active;

    public AudioSegment? Process(AudioSourceKind source, ReadOnlySpan<short> samples, bool speechDetected, DateTimeOffset frameEndedAt)
    {
        if (samples.IsEmpty) return null;
        if (!_active && speechDetected)
        {
            _active = true;
            CopyPreRoll();
        }

        if (_active)
        {
            var writable = Math.Min(samples.Length, _segment.Length - _segmentCount);
            samples[..writable].CopyTo(_segment.AsSpan(_segmentCount));
            _segmentCount += writable;
            _silenceSamples = speechDetected ? 0 : _silenceSamples + writable;
            if (_segmentCount >= _maxSamples || _silenceSamples >= _silenceToEndSamples || writable < samples.Length)
            {
                var trim = Math.Max(0, _silenceSamples - _postRollSamples);
                var finalCount = Math.Max(0, _segmentCount - trim);
                var result = finalCount >= _minSamples ? CreateSegment(source, finalCount, frameEndedAt - TimeSpan.FromSeconds((double)trim / _sampleRate)) : null;
                ResetSegment();
                WritePreRoll(samples);
                return result;
            }
        }

        WritePreRoll(samples);
        return null;
    }

    public AudioSegment? Flush(AudioSourceKind source, DateTimeOffset endedAt)
    {
        if (!_active) return null;
        var finalCount = _segmentCount;
        var result = finalCount >= _minSamples ? CreateSegment(source, finalCount, endedAt) : null;
        ResetSegment();
        return result;
    }

    public void Reset()
    {
        ResetSegment();
        Array.Clear(_preRoll);
        _prePosition = _preCount = 0;
    }

    private AudioSegment CreateSegment(AudioSourceKind source, int sampleCount, DateTimeOffset endedAt)
    {
        var pcm = new byte[sampleCount * sizeof(short)];
        for (var i = 0; i < sampleCount; i++) BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2, 2), _segment[i]);
        return new(Guid.NewGuid(), source, endedAt - TimeSpan.FromSeconds((double)sampleCount / _sampleRate), endedAt, _sampleRate, 1, pcm);
    }

    private void CopyPreRoll()
    {
        if (_preRoll.Length == 0) { _segmentCount = 0; return; }
        var start = (_prePosition - _preCount + _preRoll.Length) % _preRoll.Length;
        var first = Math.Min(_preCount, _preRoll.Length - start);
        _preRoll.AsSpan(start, first).CopyTo(_segment);
        _preRoll.AsSpan(0, _preCount - first).CopyTo(_segment.AsSpan(first));
        _segmentCount = _preCount;
    }

    private void WritePreRoll(ReadOnlySpan<short> samples)
    {
        if (_preRoll.Length == 0) return;
        if (samples.Length >= _preRoll.Length)
        {
            samples[^_preRoll.Length..].CopyTo(_preRoll);
            _prePosition = 0;
            _preCount = _preRoll.Length;
            return;
        }
        var first = Math.Min(samples.Length, _preRoll.Length - _prePosition);
        samples[..first].CopyTo(_preRoll.AsSpan(_prePosition));
        samples[first..].CopyTo(_preRoll);
        _prePosition = (_prePosition + samples.Length) % _preRoll.Length;
        _preCount = Math.Min(_preRoll.Length, _preCount + samples.Length);
    }

    private void ResetSegment() { _active = false; _segmentCount = _silenceSamples = 0; }
    private int Samples(TimeSpan duration) => checked((int)Math.Round(duration.TotalSeconds * _sampleRate));
}
