using GtaRpAssistant.App;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.App.Tests;

public sealed class SpeechToTextProviderCatalogTests
{
    [Fact]
    public async Task DisabledRoute_ReturnsEmptyLease()
    {
        var secrets = new RecordingSecretStore();
        var catalog = new SpeechToTextProviderCatalog(secrets);
        var settings = ConfiguredSettings([], new());

        await using var route = await catalog.CreateAvailableRouteAsync(settings, CancellationToken.None);

        Assert.Empty(route.Providers);
        Assert.Equal(0, secrets.ReadCount);
    }

    [Fact]
    public async Task CloudProvider_IsNotCreatedWhenCloudIsBlocked()
    {
        var secrets = new RecordingSecretStore();
        var connection = new ProviderConnectionSettings
        {
            Id = "cloud-stt",
            DisplayName = "Cloud STT",
            Kind = ProviderKind.OpenAiCompatible,
            BaseUri = new("https://example.invalid/v1"),
            ModelId = "speech-model",
            SecretReference = "cloud-key",
            IsLocal = false,
        };
        var settings = ConfiguredSettings(
            [connection],
            new() { Mode = ProviderSelectionMode.Cloud, PrimaryProviderId = connection.Id });

        await using var route = await new SpeechToTextProviderCatalog(secrets)
            .CreateAvailableRouteAsync(settings, CancellationToken.None);

        Assert.Empty(route.Providers);
        Assert.Equal(0, secrets.ReadCount);
    }

    [Fact]
    public async Task EmbeddedProvider_IsPreferredWithoutChangingConfiguredFallbacks()
    {
        var embedded = new AvailableSpeechToTextProvider();
        var settings = ConfiguredSettings([], new());

        await using var route = await new SpeechToTextProviderCatalog(new RecordingSecretStore(), embedded)
            .CreateAvailableRouteAsync(settings, CancellationToken.None);

        Assert.Same(embedded, Assert.Single(route.Providers));
    }

    [Fact]
    public async Task EmbeddedProvider_CanBeDisabledIndependently()
    {
        var settings = ConfiguredSettings([], new()) with { EmbeddedSttEnabled = false };

        await using var route = await new SpeechToTextProviderCatalog(new RecordingSecretStore(), new AvailableSpeechToTextProvider())
            .CreateAvailableRouteAsync(settings, CancellationToken.None);

        Assert.Empty(route.Providers);
    }

    private static AppSettings ConfiguredSettings(
        IReadOnlyList<ProviderConnectionSettings> connections,
        ProviderRouteSettings speechRoute) =>
        new(
            AllowCloud: false,
            ProviderSettingsVersion: ProviderSettingsMigration.CurrentVersion,
            ProviderConnections: connections,
            ProviderRouting: new() { SpeechToText = speechRoute });

    private sealed class RecordingSecretStore : ISecretStore
    {
        public int ReadCount { get; private set; }
        public Task SaveAsync(string key, string value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult<string?>("secret");
        }
        public Task DeleteAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class AvailableSpeechToTextProvider : ISpeechToTextProvider
    {
        public string Id => "embedded-test";
        public ProviderKind Kind => ProviderKind.BuiltIn;
        public ProviderCapabilities Capabilities => new() { SupportsAudioInput = true, SupportsTranscription = true, IsLocal = true };
        public Task<IReadOnlyList<ProviderModelInfo>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderModelInfo>>([]);
        public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderHealth(true, "ready"));
        public Task<TranscriptResult> TranscribeAsync(AudioSegment segment, CancellationToken cancellationToken) =>
            Task.FromResult(new TranscriptResult("ok", 1));
    }
}
