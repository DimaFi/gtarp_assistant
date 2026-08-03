using System.Net.Http;
using GtaRpAssistant.Core;
using GtaRpAssistant.Providers;

namespace GtaRpAssistant.App;

public interface ISpeechToTextProviderCatalog
{
    Task<SpeechToTextProviderRoute> CreateAvailableRouteAsync(AppSettings settings, CancellationToken cancellationToken);
}

public sealed class SpeechToTextProviderRoute(
    IReadOnlyList<ISpeechToTextProvider> providers,
    IReadOnlyList<HttpClient> clients) : IAsyncDisposable
{
    private int _disposed;

    public IReadOnlyList<ISpeechToTextProvider> Providers { get; } = providers;

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        foreach (var client in clients) client.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class SpeechToTextProviderCatalog(ISecretStore secrets, ISpeechToTextProvider? embeddedProvider = null) : ISpeechToTextProviderCatalog
{
    public async Task<SpeechToTextProviderRoute> CreateAvailableRouteAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var value = ProviderSettingsMigration.Migrate(settings);
        var clients = new List<HttpClient>();
        try
        {
            var registry = new ProviderRegistry();
            var route = value.ProviderRouting!.SpeechToText;
            var ids = EnumerateIds(route).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var connection in value.ProviderConnections!.Where(connection => connection.Enabled && ids.Contains(connection.Id)))
            {
                if (!SupportsSpeechToText(connection.Kind)) continue;
                if (string.IsNullOrWhiteSpace(connection.ModelId) || (!connection.IsLocal && !value.AllowCloud)) continue;

                var secret = string.IsNullOrWhiteSpace(connection.SecretReference)
                    ? null
                    : await secrets.GetAsync(connection.SecretReference, cancellationToken);
                var client = new HttpClient();
                try
                {
                    registry.Register(new OpenAiCompatibleSpeechToTextProvider(client, new(
                        connection.BaseUri,
                        connection.ModelId,
                        secret,
                        connection.Timeout,
                        connection.IsLocal,
                        value.Language,
                        connection.Id,
                        connection.Kind)));
                    clients.Add(client);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            }

            var configured = new ProviderRouteResolver(registry)
                .Resolve(ProviderTask.SpeechToText, route)
                .Providers
                .OfType<ISpeechToTextProvider>();
            var available = new List<ISpeechToTextProvider>();
            if (value.EmbeddedSttEnabled && embeddedProvider is not null
                && (await embeddedProvider.CheckHealthAsync(cancellationToken)).IsAvailable)
                available.Add(embeddedProvider);
            foreach (var provider in configured)
            {
                if (!provider.Capabilities.IsLocal && !value.AllowCloud) continue;
                if ((await provider.CheckHealthAsync(cancellationToken)).IsAvailable) available.Add(provider);
            }

            return new(available, clients);
        }
        catch
        {
            foreach (var client in clients) client.Dispose();
            throw;
        }
    }

    private static bool SupportsSpeechToText(ProviderKind kind) =>
        kind is ProviderKind.OpenAiCompatible
            or ProviderKind.OpenAi
            or ProviderKind.OpenRouter
            or ProviderKind.Groq
            or ProviderKind.LmStudio
            or ProviderKind.Ollama
            or ProviderKind.CustomHttp;

    private static IEnumerable<string> EnumerateIds(ProviderRouteSettings route)
    {
        if (!string.IsNullOrWhiteSpace(route.PrimaryProviderId)) yield return route.PrimaryProviderId;
        foreach (var id in route.FallbackProviderIds)
            if (!string.IsNullOrWhiteSpace(id)) yield return id;
    }
}
