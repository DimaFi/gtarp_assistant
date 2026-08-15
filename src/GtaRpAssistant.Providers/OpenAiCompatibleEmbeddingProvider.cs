using System.Net.Http.Json;
using System.Text.Json;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.Providers;

public sealed record OpenAiCompatibleEmbeddingOptions(
    Uri BaseUri,
    string ModelId,
    string? ApiKey = null,
    TimeSpan? Timeout = null,
    bool IsLocal = true,
    string? ProviderId = null,
    ProviderKind Kind = ProviderKind.OpenAiCompatible);

public sealed class OpenAiCompatibleEmbeddingProvider : IBatchEmbeddingProvider, IModelIdentifiedProvider
{
    private const int MaxInputs = 16;
    private const int MaxInputCharacters = 2400;
    private const int MaxDimensions = 65_536;
    private readonly HttpClient _http;
    private readonly OpenAiCompatibleEmbeddingOptions _options;
    private readonly OpenAiCompatibleTransport _transport;

    public OpenAiCompatibleEmbeddingProvider(HttpClient httpClient, OpenAiCompatibleEmbeddingOptions options)
    {
        _http = httpClient;
        _options = options;
        _transport = new(httpClient, options.BaseUri, options.ApiKey, options.Timeout ?? TimeSpan.FromSeconds(15), options.IsLocal);
    }

    public string Id => _options.ProviderId ?? (_options.IsLocal ? "local-embeddings" : "openai-compatible-embeddings");
    public string ModelId => _options.ModelId;
    public ProviderKind Kind => _options.Kind;
    public ProviderCapabilities Capabilities => new()
    {
        SupportsTextInput = true,
        SupportsEmbeddings = true,
        IsLocal = _options.IsLocal,
        RequiresApiKey = !_options.IsLocal,
    };

    public Task<IReadOnlyList<ProviderModelInfo>> GetModelsAsync(CancellationToken cancellationToken) => _transport.GetModelsAsync(cancellationToken);
    public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken) =>
        _transport.CheckModelAsync(_options.ModelId, "Embedding-модель доступна", "Embedding-модель отсутствует", cancellationToken);

    public async Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken cancellationToken) =>
        (await EmbedAsync([text], cancellationToken))[0];

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count is < 1 or > MaxInputs) throw new ArgumentOutOfRangeException(nameof(texts));
        var input = texts.Select(x => string.IsNullOrWhiteSpace(x)
            ? throw new ArgumentException("Embedding input не может быть пустым.", nameof(texts))
            : x.Length <= MaxInputCharacters ? x : x[..MaxInputCharacters]).ToArray();
        using var response = await _http.PostAsJsonAsync("embeddings", new { model = _options.ModelId, input, encoding_format = "float" }, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Embedding endpoint не вернул массив data.");

        var result = new ReadOnlyMemory<float>[input.Length];
        var seen = new bool[input.Length];
        foreach (var item in data.EnumerateArray())
        {
            var index = item.GetProperty("index").GetInt32();
            var values = item.GetProperty("embedding");
            if (index < 0 || index >= result.Length || seen[index] || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is < 1 or > MaxDimensions)
                throw new InvalidDataException("Embedding response имеет неверный index или размерность.");
            var vector = values.EnumerateArray().Select(x => x.GetSingle()).ToArray();
            if (vector.Any(x => !float.IsFinite(x))) throw new InvalidDataException("Embedding response содержит нечисловое значение.");
            result[index] = vector;
            seen[index] = true;
        }
        if (seen.Any(x => !x) || result.Select(x => x.Length).Distinct().Count() != 1)
            throw new InvalidDataException("Embedding response неполный или имеет разные размерности.");
        return result;
    }
}
