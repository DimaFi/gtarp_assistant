using System.Net.Http.Headers;
using System.Text.Json;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.Providers;

public sealed class OpenAiCompatibleTransport
{
    public OpenAiCompatibleTransport(HttpClient httpClient, Uri baseUri, string? apiKey, TimeSpan timeout, bool isLocal)
    {
        if (!isLocal && baseUri.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("Удалённый endpoint должен использовать HTTPS", nameof(baseUri));
        if (isLocal && !baseUri.IsLoopback) throw new ArgumentException("Локальный endpoint должен быть loopback", nameof(baseUri));
        HttpClient = httpClient;
        HttpClient.BaseAddress = baseUri.AbsoluteUri.EndsWith('/') ? baseUri : new Uri(baseUri.AbsoluteUri + "/");
        HttpClient.Timeout = timeout;
        if (!string.IsNullOrWhiteSpace(apiKey)) HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public HttpClient HttpClient { get; }

    public async Task<IReadOnlyList<ProviderModelInfo>> GetModelsAsync(CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync("models", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];
        return data.EnumerateArray()
            .Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => new ProviderModelInfo(id!, id!))
            .ToArray();
    }

    public async Task<ProviderHealth> CheckModelAsync(string modelId, string availableMessage, string missingMessage, CancellationToken cancellationToken)
    {
        try
        {
            var models = await GetModelsAsync(cancellationToken);
            var ids = models.Select(model => model.Id).ToArray();
            return ids.Contains(modelId, StringComparer.Ordinal) ? new(true, availableMessage, ids) : new(false, missingMessage, ids);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, "Timeout"); }
        catch (HttpRequestException ex) { return new(false, ex.Message); }
        catch (JsonException) { return new(false, "Endpoint вернул невалидный JSON"); }
    }
}
