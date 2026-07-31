using System.IO;
using System.Speech.Synthesis;
using GtaRpAssistant.Core;
using NAudio.Wave;

namespace GtaRpAssistant.App;

public sealed class WindowsTextToSpeechService : ITextToSpeechService, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _activeGate = new();
    private SpeechSynthesizer? _activeSynthesizer;
    private WaveOutEvent? _activeOutput;

    public IReadOnlyList<string> GetVoices()
    {
        using var synth = new SpeechSynthesizer();
        return synth.GetInstalledVoices().Where(x => x.Enabled).Select(x => x.VoiceInfo.Name).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public IReadOnlyList<AudioOutputDevice> GetOutputDevices()
    {
        var result = new List<AudioOutputDevice> { new(-1, "Устройство Windows по умолчанию") };
        for (var index = 0; index < WaveOut.DeviceCount; index++) result.Add(new(index, WaveOut.GetCapabilities(index).ProductName));
        return result;
    }

    public async Task SpeakAsync(string text, string? voice, int outputDevice, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var wave = new MemoryStream();
            using (var synth = new SpeechSynthesizer())
            {
                lock (_activeGate) _activeSynthesizer = synth;
                try
                {
                    if (!string.IsNullOrWhiteSpace(voice)) synth.SelectVoice(voice);
                    synth.SetOutputToWaveStream(wave);
                    using var synthesisCancellation = cancellationToken.Register(synth.SpeakAsyncCancelAll);
                    await Task.Run(() => synth.Speak(text), cancellationToken);
                }
                finally { lock (_activeGate) _activeSynthesizer = null; }
            }
            wave.Position = 0;
            using var reader = new WaveFileReader(wave);
            using var output = new WaveOutEvent { DeviceNumber = outputDevice };
            lock (_activeGate) _activeOutput = output;
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            output.PlaybackStopped += (_, args) => { if (args.Exception is null) completed.TrySetResult(); else completed.TrySetException(args.Exception); };
            using var playbackCancellation = cancellationToken.Register(output.Stop);
            output.Init(reader);
            output.Play();
            await completed.Task.WaitAsync(cancellationToken);
            lock (_activeGate) _activeOutput = null;
        }
        finally { _gate.Release(); }
    }

    public void Stop()
    {
        lock (_activeGate)
        {
            _activeSynthesizer?.SpeakAsyncCancelAll();
            _activeOutput?.Stop();
        }
    }

    public void Dispose() { Stop(); _gate.Dispose(); }
}
