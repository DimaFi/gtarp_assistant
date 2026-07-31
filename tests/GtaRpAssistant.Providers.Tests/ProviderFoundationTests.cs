using GtaRpAssistant.Core;
using GtaRpAssistant.Providers;

namespace GtaRpAssistant.Providers.Tests;

public sealed class ProviderFoundationTests
{
    [Fact]
    public void Registry_RejectsDuplicateIdsIgnoringCase()
    {
        var registry = new ProviderRegistry();
        registry.Register(Chat("primary", true));
        Assert.Throws<InvalidOperationException>(() => registry.Register(Chat("PRIMARY", false)));
    }

    [Fact]
    public void Route_PreservesConfiguredPrimaryAndFallbackOrder()
    {
        var registry = Registry(Chat("cloud-primary", false), Chat("local-fallback", true));
        var route = new ProviderRouteSettings
        {
            Mode = ProviderSelectionMode.Automatic,
            PrimaryProviderId = "cloud-primary",
            FallbackProviderIds = ["local-fallback"],
        };

        var plan = new ProviderRouteResolver(registry).Resolve(ProviderTask.Chat, route);

        Assert.Equal(["cloud-primary", "local-fallback"], plan.Providers.Select(provider => provider.Id));
    }

    [Fact]
    public void CloudAndLocalModes_AreEqualFiltersNotPriorityRules()
    {
        var registry = Registry(Chat("cloud", false), Chat("local", true));
        var configured = new ProviderRouteSettings
        {
            Mode = ProviderSelectionMode.Cloud,
            PrimaryProviderId = "local",
            FallbackProviderIds = ["cloud"],
        };
        var resolver = new ProviderRouteResolver(registry);

        Assert.Equal(["cloud"], resolver.Resolve(ProviderTask.Chat, configured).Providers.Select(provider => provider.Id));
        Assert.Equal(["local"], resolver.Resolve(ProviderTask.Chat, configured with { Mode = ProviderSelectionMode.Local }).Providers.Select(provider => provider.Id));
    }

    [Fact]
    public void Routes_AreIndependentPerTaskAndValidateCapabilities()
    {
        var registry = Registry(Chat("chat", true), Stt("stt", true), Vision("vision", false));
        var settings = new ProviderRoutingSettings
        {
            Chat = Route("chat"),
            SpeechToText = Route("stt"),
            Vision = Route("vision"),
            TextToSpeech = Route("chat"),
        };
        var resolver = new ProviderRouteResolver(registry);

        Assert.Equal("chat", resolver.Resolve(ProviderTask.Chat, settings.Chat).Primary?.Id);
        Assert.Equal("stt", resolver.Resolve(ProviderTask.SpeechToText, settings.SpeechToText).Primary?.Id);
        Assert.Equal("vision", resolver.Resolve(ProviderTask.Vision, settings.Vision).Primary?.Id);
        Assert.Empty(resolver.Resolve(ProviderTask.TextToSpeech, settings.TextToSpeech).Providers);
    }

    [Fact]
    public void DisabledRoute_NeverReturnsAProvider()
    {
        var resolver = new ProviderRouteResolver(Registry(Chat("chat", true)));
        Assert.Empty(resolver.Resolve(ProviderTask.Chat, new() { Mode = ProviderSelectionMode.Disabled, PrimaryProviderId = "chat" }).Providers);
    }

    private static ProviderRouteSettings Route(string id) => new() { Mode = ProviderSelectionMode.Automatic, PrimaryProviderId = id };
    private static ProviderRegistry Registry(params IAiProvider[] providers)
    {
        var registry = new ProviderRegistry();
        foreach (var provider in providers) registry.Register(provider);
        return registry;
    }

    private static FakeProvider Chat(string id, bool local) => new(id, local, new() { SupportsTextInput = true, SupportsChat = true, SupportsStructuredOutput = true, IsLocal = local });
    private static FakeProvider Stt(string id, bool local) => new(id, local, new() { SupportsAudioInput = true, SupportsTranscription = true, IsLocal = local });
    private static FakeProvider Vision(string id, bool local) => new(id, local, new() { SupportsTextInput = true, SupportsImageInput = true, IsLocal = local });

    private sealed class FakeProvider(string id, bool local, ProviderCapabilities capabilities) : IAiProvider
    {
        public string Id { get; } = id;
        public ProviderKind Kind => local ? ProviderKind.BuiltIn : ProviderKind.OpenAiCompatible;
        public ProviderCapabilities Capabilities { get; } = capabilities;
        public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken) => Task.FromResult(new ProviderHealth(true, "ok"));
        public Task<IReadOnlyList<ProviderModelInfo>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderModelInfo>>([]);
    }
}
