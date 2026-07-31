using GtaRpAssistant.Core;

namespace GtaRpAssistant.Providers;

public sealed class ProviderRegistry : IAiProviderRegistry
{
    private readonly Dictionary<string, IAiProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IAiProvider> _ordered = [];

    public IReadOnlyList<IAiProvider> Providers => _ordered;

    public void Register(IAiProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(provider.Id)) throw new ArgumentException("Provider ID is required.", nameof(provider));
        if (!_providers.TryAdd(provider.Id, provider)) throw new InvalidOperationException($"Provider '{provider.Id}' is already registered.");
        _ordered.Add(provider);
    }

    public bool TryGet(string id, out IAiProvider? provider) => _providers.TryGetValue(id, out provider);
}

public sealed class ProviderRouteResolver(IAiProviderRegistry registry) : IProviderRouteResolver
{
    public ProviderRoutePlan Resolve(ProviderTask task, ProviderRouteSettings route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route.Mode == ProviderSelectionMode.Disabled) return new(task, route.Mode, []);

        var configuredIds = EnumerateConfiguredIds(route).ToArray();
        var candidates = configuredIds.Length == 0
            ? registry.Providers
            : configuredIds.Select(id => registry.TryGet(id, out var provider) ? provider : null).OfType<IAiProvider>().ToArray();
        var selected = candidates
            .Where(provider => IsLocationAllowed(provider, route.Mode) && Supports(provider.Capabilities, task))
            .DistinctBy(provider => provider.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new(task, route.Mode, selected);
    }

    private static IEnumerable<string> EnumerateConfiguredIds(ProviderRouteSettings route)
    {
        if (!string.IsNullOrWhiteSpace(route.PrimaryProviderId)) yield return route.PrimaryProviderId;
        foreach (var id in route.FallbackProviderIds)
            if (!string.IsNullOrWhiteSpace(id)) yield return id;
    }

    private static bool IsLocationAllowed(IAiProvider provider, ProviderSelectionMode mode) => mode switch
    {
        ProviderSelectionMode.Local => provider.Capabilities.IsLocal,
        ProviderSelectionMode.Cloud => !provider.Capabilities.IsLocal,
        ProviderSelectionMode.Automatic or ProviderSelectionMode.Custom => true,
        _ => false,
    };

    private static bool Supports(ProviderCapabilities capabilities, ProviderTask task) => task switch
    {
        ProviderTask.SpeechToText => capabilities.SupportsTranscription && capabilities.SupportsAudioInput,
        ProviderTask.Chat => capabilities.SupportsChat && capabilities.SupportsTextInput,
        ProviderTask.Vision => capabilities.SupportsImageInput,
        ProviderTask.TextToSpeech => capabilities.SupportsTextToSpeech,
        ProviderTask.Embeddings => capabilities.SupportsEmbeddings,
        ProviderTask.SituationClassification => capabilities.SupportsChat && capabilities.SupportsStructuredOutput,
        _ => false,
    };
}
