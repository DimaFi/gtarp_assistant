using GtaRpAssistant.Core;

namespace GtaRpAssistant.App;

public static class ProviderSettingsMigration
{
    public const int CurrentVersion = 1;
    public const string LocalChatId = "legacy-local-chat";
    public const string CloudChatId = "legacy-cloud-chat";
    public const string LocalSttId = "legacy-local-stt";
    public const string CloudSttId = "legacy-cloud-stt";
    public const string LocalVisionId = "legacy-local-vision";
    public const string CloudVisionId = "legacy-cloud-vision";
    public const string WindowsTtsId = "windows-tts";
    private static readonly HashSet<string> LegacyIds = new([
        LocalChatId, CloudChatId, LocalSttId, CloudSttId, LocalVisionId, CloudVisionId, WindowsTtsId,
    ], StringComparer.OrdinalIgnoreCase);

    public static AppSettings Migrate(AppSettings settings)
    {
        if (settings.ProviderSettingsVersion >= CurrentVersion && settings.ProviderConnections is not null && settings.ProviderRouting is not null)
            return settings;

        if (settings.ProviderConnections is not null && settings.ProviderRouting is not null)
            return settings with { ProviderSettingsVersion = CurrentVersion };

        var connections = new List<ProviderConnectionSettings>();
        var endpoint = ParseEndpoint(settings.Endpoint);
        var cloudEndpoint = ParseEndpoint(settings.CloudEndpoint);

        string? localChat = null;
        string? cloudChat = null;
        string? stt = null;
        string? localVision = null;
        string? cloudVision = null;

        if (endpoint is { IsLoopback: true })
        {
            localChat = AddConnection(connections, LocalChatId, "Legacy local Chat", endpoint, settings.Model, "chat-provider-api-key", true);
            stt = AddConnection(connections, LocalSttId, "Legacy local STT", endpoint, settings.SttModel, "chat-provider-api-key", true);
            localVision = AddConnection(connections, LocalVisionId, "Legacy local Vision", endpoint, VisionModel(settings, settings.Model), "chat-provider-api-key", true);
        }
        else if (endpoint is not null && settings.AllowCloud && endpoint.Scheme == Uri.UriSchemeHttps)
        {
            stt = AddConnection(connections, CloudSttId, "Legacy cloud STT", endpoint, settings.SttModel, "chat-provider-api-key", false);
        }

        if (settings.AllowCloud && cloudEndpoint is { IsLoopback: false } && cloudEndpoint.Scheme == Uri.UriSchemeHttps)
        {
            cloudChat = AddConnection(connections, CloudChatId, "Legacy cloud Chat", cloudEndpoint, settings.CloudModel, "cloud-provider-api-key", false);
            cloudVision = AddConnection(connections, CloudVisionId, "Legacy cloud Vision", cloudEndpoint, VisionModel(settings, settings.CloudModel), "cloud-provider-api-key", false);
        }

        connections.Add(new()
        {
            Id = WindowsTtsId,
            DisplayName = "Windows text-to-speech",
            Kind = ProviderKind.BuiltIn,
            BaseUri = new Uri("builtin://windows-tts"),
            ModelId = "windows-system-voice",
            IsLocal = true,
        });

        var routing = new ProviderRoutingSettings
        {
            SpeechToText = SingleRoute(stt, endpoint?.IsLoopback == true),
            Chat = PairRoute(localChat, cloudChat),
            Vision = settings.VisionEnabled ? PairRoute(localVision, cloudVision) : new(),
            TextToSpeech = settings.VoiceMode == 1
                ? new() { Mode = ProviderSelectionMode.Local, PrimaryProviderId = WindowsTtsId }
                : new(),
            Embeddings = new(),
            SituationClassification = new(),
        };

        return settings with
        {
            ProviderSettingsVersion = CurrentVersion,
            ProviderConnections = connections,
            ProviderRouting = routing,
        };
    }

    public static AppSettings ApplyLegacyConnectionEdits(AppSettings settings)
    {
        settings = Migrate(settings);
        var rebuilt = Migrate(settings with { ProviderSettingsVersion = 0, ProviderConnections = null, ProviderRouting = null });
        var customConnections = settings.ProviderConnections!.Where(connection => !LegacyIds.Contains(connection.Id));
        var mergedConnections = customConnections.Concat(rebuilt.ProviderConnections!).ToArray();
        var current = settings.ProviderRouting!;
        var legacy = rebuilt.ProviderRouting!;
        return settings with
        {
            ProviderSettingsVersion = CurrentVersion,
            ProviderConnections = mergedConnections,
            ProviderRouting = current with
            {
                SpeechToText = MergeRoute(current.SpeechToText, legacy.SpeechToText),
                Chat = MergeRoute(current.Chat, legacy.Chat),
                Vision = MergeRoute(current.Vision, legacy.Vision),
                TextToSpeech = MergeRoute(current.TextToSpeech, legacy.TextToSpeech),
            },
        };
    }

    public static bool IsWindowsTtsEnabled(AppSettings settings)
    {
        var route = Migrate(settings).ProviderRouting!.TextToSpeech;
        if (route.Mode is ProviderSelectionMode.Disabled or ProviderSelectionMode.Cloud) return false;
        return string.Equals(route.PrimaryProviderId, WindowsTtsId, StringComparison.OrdinalIgnoreCase)
            || route.FallbackProviderIds.Contains(WindowsTtsId, StringComparer.OrdinalIgnoreCase);
    }

    private static Uri? ParseEndpoint(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
    private static string VisionModel(AppSettings settings, string fallback) => string.IsNullOrWhiteSpace(settings.VisionModel) ? fallback : settings.VisionModel;

    private static string? AddConnection(List<ProviderConnectionSettings> connections, string id, string displayName, Uri endpoint, string model, string secretReference, bool isLocal)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;
        connections.Add(new()
        {
            Id = id,
            DisplayName = displayName,
            Kind = ProviderKind.OpenAiCompatible,
            BaseUri = endpoint,
            ModelId = model,
            SecretReference = secretReference,
            Timeout = TimeSpan.FromSeconds(45),
            IsLocal = isLocal,
        });
        return id;
    }

    private static ProviderRouteSettings SingleRoute(string? providerId, bool isLocal) => providerId is null
        ? new()
        : new() { Mode = isLocal ? ProviderSelectionMode.Local : ProviderSelectionMode.Cloud, PrimaryProviderId = providerId };

    private static ProviderRouteSettings PairRoute(string? localId, string? cloudId)
    {
        if (localId is not null && cloudId is not null)
            return new() { Mode = ProviderSelectionMode.Automatic, PrimaryProviderId = localId, FallbackProviderIds = [cloudId] };
        if (localId is not null) return new() { Mode = ProviderSelectionMode.Local, PrimaryProviderId = localId };
        if (cloudId is not null) return new() { Mode = ProviderSelectionMode.Cloud, PrimaryProviderId = cloudId };
        return new();
    }

    private static ProviderRouteSettings MergeRoute(ProviderRouteSettings current, ProviderRouteSettings rebuilt)
    {
        var currentIds = Enumerable.Repeat(current.PrimaryProviderId, 1).Concat(current.FallbackProviderIds).Where(id => !string.IsNullOrWhiteSpace(id));
        return currentIds.Any(id => !LegacyIds.Contains(id!)) ? current : rebuilt with { Mode = current.Mode };
    }
}
