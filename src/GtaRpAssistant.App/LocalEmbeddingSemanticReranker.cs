using System.Net.Http;
using System.IO;
using System.Text.Json;
using GtaRpAssistant.Core;
using GtaRpAssistant.Providers;

namespace GtaRpAssistant.App;

public sealed class LocalEmbeddingSemanticReranker(SettingsService settings, ISecretStore secrets) : ISemanticReranker, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string _fingerprint = "";
    private HttpClient? _client;
    private IEmbeddingProvider? _provider;
    private EmbeddingSemanticReranker? _reranker;
    private DateTimeOffset _healthCheckedAt;
    private bool _healthy;

    public async Task<IReadOnlyList<SemanticRelevanceScore>> ScoreAsync(string question, IReadOnlyList<KnowledgeMatch> candidates, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var value = ProviderSettingsMigration.Migrate(settings.Current);
            var fingerprint = JsonSerializer.Serialize(new { value.ProviderSettingsVersion, value.ProviderConnections, value.ProviderRouting!.Embeddings });
            if (!string.Equals(fingerprint, _fingerprint, StringComparison.Ordinal))
                await RebuildAsync(value, fingerprint, cancellationToken);
            if (_provider is null || _reranker is null) return [];
            if (DateTimeOffset.UtcNow - _healthCheckedAt >= TimeSpan.FromSeconds(30))
            {
                _healthy = (await _provider.CheckHealthAsync(cancellationToken)).IsAvailable;
                _healthCheckedAt = DateTimeOffset.UtcNow;
            }
            return _healthy ? await _reranker.ScoreAsync(question, candidates, cancellationToken) : [];
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && (ex is HttpRequestException or JsonException or InvalidDataException or TaskCanceledException))
        {
            return [];
        }
        finally { _gate.Release(); }
    }

    private async Task RebuildAsync(AppSettings value, string fingerprint, CancellationToken cancellationToken)
    {
        DisposeClient();
        var route = value.ProviderRouting!.Embeddings;
        if (route.Mode == ProviderSelectionMode.Disabled)
        {
            _fingerprint = fingerprint;
            return;
        }
        var ids = Enumerable.Repeat(route.PrimaryProviderId, 1).Concat(route.FallbackProviderIds).Where(x => !string.IsNullOrWhiteSpace(x));
        var connection = ids.Select(id => value.ProviderConnections!.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(x => x is { Enabled: true, IsLocal: true } && x.BaseUri.IsLoopback && !string.IsNullOrWhiteSpace(x.ModelId));
        if (connection is null)
        {
            _fingerprint = fingerprint;
            return;
        }
        var secret = string.IsNullOrWhiteSpace(connection.SecretReference) ? null : await secrets.GetAsync(connection.SecretReference, cancellationToken);
        _client = new HttpClient();
        _provider = new OpenAiCompatibleEmbeddingProvider(_client, new(
            connection.BaseUri, connection.ModelId!, secret, connection.Timeout, true, connection.Id, connection.Kind));
        _reranker = new(_provider);
        _fingerprint = fingerprint;
        _healthCheckedAt = default;
    }

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
        _provider = null;
        _reranker = null;
        _healthy = false;
        _healthCheckedAt = default;
    }

    public void Dispose()
    {
        DisposeClient();
        _gate.Dispose();
    }
}
