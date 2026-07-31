using System.Runtime.InteropServices;
using GtaRpAssistant.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GtaRpAssistant.Infrastructure.Windows;

public sealed record RenderDeviceInfo(string Id, string DisplayName, bool IsDefault);

public static class WasapiRenderDeviceCatalog
{
    public static IReadOnlyList<RenderDeviceInfo> GetActiveRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultId = GetDefaultId(enumerator);
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(x => new RenderDeviceInfo(x.ID, x.FriendlyName, x.ID == defaultId))
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static string? GetDefaultId(MMDeviceEnumerator enumerator)
    {
        try { using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); return device.ID; }
        catch (COMException) { return null; }
    }
}

public sealed class WasapiGameAudioCaptureService : IAudioCaptureService
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MMDevice _device;
    private readonly WasapiLoopbackCapture _capture;
    private readonly BufferedWaveProvider _buffer;
    private readonly WdlResamplingSampleProvider _resampler;
    private readonly float[] _floatBuffer = new float[4096];
    private readonly short[] _shortBuffer = new short[4096];
    private TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _started;
    private bool _disposed;

    public WasapiGameAudioCaptureService(string renderDeviceId)
    {
        _device = _enumerator.GetDevice(renderDeviceId);
        _capture = new WasapiLoopbackCapture(_device);
        _buffer = new BufferedWaveProvider(_capture.WaveFormat) { BufferDuration = TimeSpan.FromSeconds(2), DiscardOnBufferOverflow = true, ReadFully = false };
        ISampleProvider samples = _buffer.ToSampleProvider();
        if (samples.WaveFormat.Channels > 1) samples = new DownmixSampleProvider(samples);
        _resampler = new WdlResamplingSampleProvider(samples, 16_000);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
    }

    public AudioSourceKind SourceKind => AudioSourceKind.GameAudio;
    public event EventHandler<AudioFrameEventArgs>? FrameCaptured;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_started) return Task.CompletedTask;
        _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _capture.StartRecording(); _started = true; return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started) return;
        _capture.StopRecording();
        await _stopped.Task.WaitAsync(cancellationToken);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_started || e.BytesRecorded == 0) return;
        _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        while (true)
        {
            var read = _resampler.Read(_floatBuffer, 0, _floatBuffer.Length);
            if (read <= 0) break;
            for (var i = 0; i < read; i++) _shortBuffer[i] = (short)Math.Clamp((int)Math.Round(_floatBuffer[i] * short.MaxValue), short.MinValue, short.MaxValue);
            FrameCaptured?.Invoke(this, new AudioFrameEventArgs(SourceKind, _shortBuffer.AsMemory(0, read), 16_000));
            if (read < _floatBuffer.Length) break;
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _started = false;
        if (e.Exception is null) _stopped.TrySetResult(); else _stopped.TrySetException(e.Exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (_started) await StopAsync(CancellationToken.None);
        _capture.DataAvailable -= OnDataAvailable; _capture.RecordingStopped -= OnRecordingStopped;
        _capture.Dispose(); _device.Dispose(); _enumerator.Dispose(); _disposed = true;
    }

    private sealed class DownmixSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private float[] _sourceBuffer = [];
        public DownmixSampleProvider(ISampleProvider source) { _source = source; WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1); }
        public WaveFormat WaveFormat { get; }
        public int Read(float[] buffer, int offset, int count)
        {
            var channels = _source.WaveFormat.Channels; var required = count * channels;
            if (_sourceBuffer.Length < required) _sourceBuffer = new float[required];
            var sourceRead = _source.Read(_sourceBuffer, 0, required); var frames = sourceRead / channels;
            for (var frame = 0; frame < frames; frame++) { float sum = 0; for (var channel = 0; channel < channels; channel++) sum += _sourceBuffer[frame * channels + channel]; buffer[offset + frame] = sum / channels; }
            return frames;
        }
    }
}
