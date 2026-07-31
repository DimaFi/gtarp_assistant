namespace GtaRpAssistant.Core;

public enum ProviderSelectionMode
{
    Disabled,
    Cloud,
    Local,
    Automatic,
    Custom,
}

public enum ProviderKind
{
    BuiltIn,
    OpenAiCompatible,
    OpenAi,
    Anthropic,
    Gemini,
    OpenRouter,
    Groq,
    LmStudio,
    Ollama,
    CustomHttp,
    MicroModel,
}

public enum ProviderTask
{
    SpeechToText,
    Chat,
    Vision,
    TextToSpeech,
    Embeddings,
    SituationClassification,
}

public sealed record ProviderCapabilities
{
    public bool SupportsTextInput { get; init; }
    public bool SupportsImageInput { get; init; }
    public bool SupportsAudioInput { get; init; }
    public bool SupportsChat { get; init; }
    public bool SupportsTranscription { get; init; }
    public bool SupportsTextToSpeech { get; init; }
    public bool SupportsEmbeddings { get; init; }
    public bool SupportsStreamingInput { get; init; }
    public bool SupportsStreamingOutput { get; init; }
    public bool SupportsStructuredOutput { get; init; }
    public bool SupportsJsonMode { get; init; }
    public bool IsLocal { get; init; }
    public bool RequiresApiKey { get; init; }
    public bool MayHaveFreeTier { get; init; }
}

public sealed record ProviderModelInfo(string Id, string DisplayName, IReadOnlySet<ProviderTask>? Tasks = null);

public sealed record ProviderConnectionSettings
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required ProviderKind Kind { get; init; }
    public required Uri BaseUri { get; init; }
    public string? ModelId { get; init; }
    public string? SecretReference { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public bool Enabled { get; init; } = true;
    public bool IsLocal { get; init; }
}

public sealed record ProviderRouteSettings
{
    public ProviderSelectionMode Mode { get; init; } = ProviderSelectionMode.Disabled;
    public string? PrimaryProviderId { get; init; }
    public IReadOnlyList<string> FallbackProviderIds { get; init; } = [];
}

public sealed record ProviderRoutingSettings
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public ProviderRouteSettings SpeechToText { get; init; } = new();
    public ProviderRouteSettings Chat { get; init; } = new();
    public ProviderRouteSettings Vision { get; init; } = new();
    public ProviderRouteSettings TextToSpeech { get; init; } = new();
    public ProviderRouteSettings Embeddings { get; init; } = new();
    public ProviderRouteSettings SituationClassification { get; init; } = new();

    public ProviderRouteSettings For(ProviderTask task) => task switch
    {
        ProviderTask.SpeechToText => SpeechToText,
        ProviderTask.Chat => Chat,
        ProviderTask.Vision => Vision,
        ProviderTask.TextToSpeech => TextToSpeech,
        ProviderTask.Embeddings => Embeddings,
        ProviderTask.SituationClassification => SituationClassification,
        _ => throw new ArgumentOutOfRangeException(nameof(task), task, null),
    };
}

public sealed record ProviderRoutePlan(ProviderTask Task, ProviderSelectionMode Mode, IReadOnlyList<IAiProvider> Providers)
{
    public IAiProvider? Primary => Providers.FirstOrDefault();
    public IReadOnlyList<IAiProvider> Fallbacks => Providers.Skip(1).ToArray();
}
