using System.Buffers.Binary;
using System.Net.Http.Headers;
using System.Text.Json;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.Providers;

public sealed record SpeechToTextOptions(
    Uri BaseUri,
    string ModelId,
    string? ApiKey = null,
    TimeSpan? Timeout = null,
    bool IsLocal = true,
    string? Language = "ru",
    string? ProviderId = null,
    ProviderKind Kind = ProviderKind.OpenAiCompatible);

public sealed class OpenAiCompatibleSpeechToTextProvider : ISpeechToTextProvider
{
    private readonly HttpClient _http;
    private readonly SpeechToTextOptions _options;
    private readonly OpenAiCompatibleTransport _transport;

    public OpenAiCompatibleSpeechToTextProvider(HttpClient httpClient, SpeechToTextOptions options)
    {
        _http = httpClient;
        _transport = new(httpClient, options.BaseUri, options.ApiKey, options.Timeout ?? TimeSpan.FromSeconds(45), options.IsLocal);
        _options = options;
    }

    public string Id => _options.ProviderId ?? (_options.IsLocal ? "local-openai-compatible-stt" : "openai-compatible-stt");
    public ProviderKind Kind => _options.Kind;
    public ProviderCapabilities Capabilities => new()
    {
        SupportsAudioInput = true,
        SupportsTranscription = true,
        IsLocal = _options.IsLocal,
        RequiresApiKey = !_options.IsLocal,
    };

    public Task<IReadOnlyList<ProviderModelInfo>> GetModelsAsync(CancellationToken cancellationToken) => _transport.GetModelsAsync(cancellationToken);

    public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken) =>
        _transport.CheckModelAsync(_options.ModelId, "STT provider доступен", "STT model отсутствует", cancellationToken);

    public async Task<TranscriptResult> TranscribeAsync(AudioSegment segment, CancellationToken cancellationToken)
    {
        if (segment.SampleRate != 16_000 || segment.Channels != 1) throw new ArgumentException("STT ожидает PCM16 mono 16 kHz", nameof(segment));
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(_options.ModelId), "model");
        if (!string.IsNullOrWhiteSpace(_options.Language)) form.Add(new StringContent(_options.Language), "language");
        var audio = new ByteArrayContent(CreateWave(segment.PcmData.Span, segment.SampleRate, segment.Channels));
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "file", "segment.wav");
        using var response = await _http.PostAsync("audio/transcriptions", form, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var text = json.RootElement.GetProperty("text").GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("STT вернул пустой текст");
        var confidence = json.RootElement.TryGetProperty("confidence", out var confidenceValue) && confidenceValue.TryGetDouble(out var parsed) ? Math.Clamp(parsed, 0, 1) : 1;
        return new(text, confidence);
    }

    private static byte[] CreateWave(ReadOnlySpan<byte> pcm, int sampleRate, int channels)
    {
        var result = new byte[44 + pcm.Length];
        "RIFF"u8.CopyTo(result); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), 36 + pcm.Length); "WAVEfmt "u8.CopyTo(result.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(16), 16); BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(22), checked((short)channels));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(24), sampleRate); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(28), sampleRate * channels * 2); BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(32), checked((short)(channels * 2))); BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(34), 16);
        "data"u8.CopyTo(result.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(40), pcm.Length); pcm.CopyTo(result.AsSpan(44)); return result;
    }
}
