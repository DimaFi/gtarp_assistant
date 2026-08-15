using System.Globalization;
using System.IO;
using System.Speech.Recognition;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.App;

/// <summary>Lightweight offline fallback that uses a Windows speech language pack.</summary>
public sealed class WindowsSpeechRecognitionProvider : ISpeechToTextProvider
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public string Id => "windows-speech-stt";
    public ProviderKind Kind => ProviderKind.BuiltIn;
    public ProviderCapabilities Capabilities => new()
    {
        SupportsAudioInput = true,
        SupportsTranscription = true,
        IsLocal = true,
    };

    public Task<IReadOnlyList<ProviderModelInfo>> GetModelsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ProviderModelInfo>>(HasRussianRecognizer()
            ? [new("windows-ru-ru", "Распознавание речи Windows (русский)", new HashSet<ProviderTask> { ProviderTask.SpeechToText })]
            : []);
    }

    public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var available = HasRussianRecognizer();
        return Task.FromResult(new ProviderHealth(available,
            available
                ? "Встроенное распознавание речи Windows готово."
                : "В Windows не установлен русский пакет распознавания речи.",
            available ? ["windows-ru-ru"] : null));
    }

    public Task<TranscriptResult> TranscribeAsync(AudioSegment segment, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (segment.SampleRate != 16_000 || segment.Channels != 1)
            throw new ArgumentException("Распознавание Windows ожидает PCM16 mono 16 kHz.", nameof(segment));
        if (segment.PcmData.IsEmpty) throw new ArgumentException("Аудиосегмент пуст.", nameof(segment));

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var recognizer = new SpeechRecognitionEngine(Russian);
            recognizer.LoadGrammar(new DictationGrammar());
            using var wave = new MemoryStream(CreateWave(segment.PcmData.Span, segment.SampleRate, segment.Channels));
            recognizer.SetInputToWaveStream(wave);
            var result = recognizer.Recognize();
            cancellationToken.ThrowIfCancellationRequested();
            if (result is null || string.IsNullOrWhiteSpace(result.Text))
                throw new InvalidOperationException("Речь не распознана. Говорите ближе к микрофону и повторите.");
            return new TranscriptResult(result.Text.Trim(), result.Confidence);
        }, cancellationToken);
    }

    private static bool HasRussianRecognizer()
    {
        try { return SpeechRecognitionEngine.InstalledRecognizers().Any(x => x.Culture.Name.Equals(Russian.Name, StringComparison.OrdinalIgnoreCase)); }
        catch (Exception ex) when (ex is PlatformNotSupportedException or InvalidOperationException) { return false; }
    }

    private static byte[] CreateWave(ReadOnlySpan<byte> pcm, int sampleRate, int channels)
    {
        const short bitsPerSample = 16;
        var bytes = new byte[44 + pcm.Length];
        using var stream = new MemoryStream(bytes);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + pcm.Length);
        writer.Write("WAVEfmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(pcm.Length);
        writer.Write(pcm);
        return bytes;
    }
}
