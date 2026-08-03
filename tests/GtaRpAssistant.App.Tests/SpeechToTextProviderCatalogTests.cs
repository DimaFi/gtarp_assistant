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
}
