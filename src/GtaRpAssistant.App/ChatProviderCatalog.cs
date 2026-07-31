using System.Net.Http;
using System.Text.Json;
using GtaRpAssistant.Core;
using GtaRpAssistant.Providers;

namespace GtaRpAssistant.App;

public sealed class ChatProviderCatalog(SettingsService settings, ISecretStore secrets) : IChatProviderCatalog, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<HttpClient> _clients = [];
    private string _fingerprint = "";
    private DateTimeOffset _checkedAt;
    private ChatProviderAvailability? _cached;

    public async Task<ChatProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var value = ProviderSettingsMigration.Migrate(settings.Current);
            var fingerprint = JsonSerializer.Serialize(new { value.ProviderSettingsVersion, value.ProviderConnections, value.ProviderRouting, value.AllowCloud });
            if (!string.Equals(fingerprint, _fingerprint, StringComparison.Ordinal))
                await RebuildAsync(value, fingerprint, cancellationToken);
            if (_cached is not null && DateTimeOffset.UtcNow - _checkedAt < TimeSpan.FromSeconds(30)) return _cached;

            var configured = _configuredRoute;
            var available = new List<IChatProvider>(configured.Count);
            foreach (var provider in configured)
            {
                if (!provider.Capabilities.IsLocal && !value.AllowCloud) continue;
                if ((await provider.CheckHealthAsync(cancellationToken)).IsAvailable) available.Add(provider);
            }

            var local = available.FirstOrDefault(provider => provider.Capabilities.IsLocal);
            var cloud = available.FirstOrDefault(provider => !provider.Capabilities.IsLocal);
            _cached = new(local, cloud, local is not null, cloud is not null, available);
            _checkedAt = DateTimeOffset.UtcNow;
            return _cached;
        }
        finally { _gate.Release(); }
    }

    private IReadOnlyList<IChatProvider> _configuredRoute = [];

    public void Invalidate()
    {
        _fingerprint = "";
        _cached = null;
        _checkedAt = default;
    }

    private async Task RebuildAsync(AppSettings value, string fingerprint, CancellationToken cancellationToken)
    {
        DisposeClients();
        var registry = new ProviderRegistry();
        var route = value.ProviderRouting!.Chat;
        var ids = EnumerateIds(route).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in value.ProviderConnections!.Where(connection => connection.Enabled && ids.Contains(connection.Id)))
        {
            if (connection.Kind is not (ProviderKind.OpenAiCompatible or ProviderKind.OpenAi or ProviderKind.OpenRouter or ProviderKind.Groq or ProviderKind.LmStudio or ProviderKind.Ollama or ProviderKind.CustomHttp)) continue;
            if (string.IsNullOrWhiteSpace(connection.ModelId)) continue;
            var secret = string.IsNullOrWhiteSpace(connection.SecretReference)
                ? null
                : await secrets.GetAsync(connection.SecretReference, cancellationToken);
            var client = new HttpClient();
            try
            {
                var localGeneration = connection.IsLocal ? SettingValues.LocalAi(value) : null;
                var provider = new OpenAiCompatibleChatProvider(client, new(
                    connection.BaseUri,
                    connection.ModelId,
                    secret,
                    connection.Timeout,
                    connection.IsLocal,
                    connection.Id,
                    connection.Kind,
                    localGeneration?.MaxOutputTokens,
                    localGeneration?.IdleUnload));
                registry.Register(provider);
                _clients.Add(client);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        _configuredRoute = new ProviderRouteResolver(registry).Resolve(ProviderTask.Chat, route).Providers.OfType<IChatProvider>().ToArray();
        _fingerprint = fingerprint;
        _cached = null;
        _checkedAt = default;
    }

    private static IEnumerable<string> EnumerateIds(ProviderRouteSettings route)
    {
        if (!string.IsNullOrWhiteSpace(route.PrimaryProviderId)) yield return route.PrimaryProviderId;
        foreach (var id in route.FallbackProviderIds)
            if (!string.IsNullOrWhiteSpace(id)) yield return id;
    }

    private void DisposeClients()
    {
        foreach (var client in _clients) client.Dispose();
        _clients.Clear();
        _configuredRoute = [];
    }

    public void Dispose()
    {
        DisposeClients();
        _gate.Dispose();
    }
}
